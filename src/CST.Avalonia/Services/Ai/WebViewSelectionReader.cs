using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Reads the reader's selection out of a book's browser by evaluating a script and returning its value. (#827)
///
/// <para><b>Why not the title channel.</b> The selection used to travel out through <c>document.title</c>, which
/// Chromium caps at 4096 characters — about 1,100 characters of Myanmar or 2,900 of Latin after #829. A script's
/// return value travels in a CEF process message instead: the #827 probe read a whole book, 661,884 characters,
/// in 22 ms on macOS and 403,195 in 39 ms on Windows.</para>
///
/// <para><b>Why "EvaluateScript returns null" was believed.</b> CefGlue evaluates a script as a function body,
/// <c>cefglue.evaluateScript(function() { … })</c>, so a script must end in a top-level <c>return</c>. One without
/// it — an IIFE expression, the shape CefSharp's <c>EvaluateScriptAsync</c> takes — yields <c>undefined</c>,
/// which arrives as null. <see cref="Script"/> returns.</para>
///
/// <para>The transport is a delegate so the decisions here can be tested without a browser.</para>
/// </summary>
public static class WebViewSelectionReader
{
    /// <summary>
    /// How long a read may take before it counts as failed.
    ///
    /// <para><b>Two seconds.</b> The #827 probe's reads took 0–39 ms for selections up to 661,884 characters,
    /// so this is about fifty times the slowest. It is passed to the library as well as bounding our own wait:
    /// the library's timer starts only when the book's script queue reaches the script, and its expiry is what
    /// frees that queue. So the value is also how long an abandoned read can hold up the book's other scripts
    /// (status updates, scroll restoration), and a renderer stuck that long has worse problems than a
    /// selection — the probe saw a sibling view in the same renderer stop repainting while one was blocked.</para>
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The selection and the paragraph it starts in, as JSON: <c>{"t": text, "p": paragraph}</c>.
    ///
    /// <para><b>The paragraph is found in DOCUMENT ORDER, not by geometry.</b> Comparing the selection's pixel top
    /// with an anchor's was a race between an empty inline <c>&lt;a&gt;</c> box and a text line, with anchor
    /// positions rounded when the cache was built — so a selection at the very start of a paragraph resolved to
    /// the previous one, which can be in the previous sutta. Anchor names carry a book-code suffix in Multi
    /// volumes (<c>para548_dn1</c>) and can be a printed range (<c>para548-9</c>). (#649)</para>
    ///
    /// <para><b>It must end in a top-level <c>return</c></b> — see the class remarks.</para>
    /// </summary>
    public const string Script = @"
        var sel = window.getSelection ? window.getSelection() : null;
        var text = sel ? sel.toString() : '';
        var para = '';
        if (sel && sel.rangeCount > 0 && text.length > 0) {
            var startNode = sel.getRangeAt(0).startContainer;
            var anchors = document.querySelectorAll('a[name]');
            var best = null;
            for (var i = 0; i < anchors.length; i++) {
                var a = anchors[i];
                if (!a.name || a.name.indexOf('para') !== 0) continue;
                var rel = a.compareDocumentPosition(startNode);
                // The selection's node comes after this anchor, so the anchor precedes it.
                if (rel === 0 || (rel & Node.DOCUMENT_POSITION_FOLLOWING)) best = a;
                // querySelectorAll returns document order: once one stops preceding, so do the rest.
                else break;
            }
            if (!best && anchors.length > 0) {
                for (var j = 0; j < anchors.length; j++) {
                    if (anchors[j].name && anchors[j].name.indexOf('para') === 0) { best = anchors[j]; break; }
                }
            }
            var name = best ? (best.name || '') : '';
            if (name.indexOf('para') === 0) {
                var t = name.substring(4);
                var underscore = t.indexOf('_');
                if (underscore !== -1) t = t.substring(0, underscore);
                var dash = t.indexOf('-');
                if (dash !== -1) t = t.substring(0, dash);
                para = t;
            }
        }
        return JSON.stringify({ t: text, p: para });";

    /// <summary>Why a read produced no selection. <see cref="None"/> means it did.</summary>
    public enum Failure
    {
        None,
        /// <summary>Our bound elapsed before the script answered.</summary>
        Timeout,
        /// <summary>The library reported its own timeout.</summary>
        LibraryTimeout,
        /// <summary>The script's value arrived as null — the browser was disposed, or the script did not return.</summary>
        ScriptNull,
        /// <summary>The value was not the JSON the script returns.</summary>
        Parse,
        /// <summary>The call threw something other than a timeout.</summary>
        Exception,
        /// <summary>The book was reloaded, re-scripted or rebound, or the browser replaced, during the read.</summary>
        Stale,
    }

    public readonly record struct Result(string? Text, int? Paragraph, Failure Failure, string? Detail, long ElapsedMs);

    /// <summary>
    /// Run <see cref="Script"/> through <paramref name="evaluate"/> and decode the answer.
    ///
    /// <para><b>Null, never "", unless the reader really selected nothing.</b> A read that crossed a reload or a
    /// shutdown — <paramref name="staleness"/> names it — is a failure: an empty answer there would tell the
    /// assistant the reader selected nothing, which is the report "the AI ignored my selection". (#581)</para>
    ///
    /// <para>No <c>ConfigureAwait(false)</c>: the caller's <paramref name="staleness"/> check reads UI state, so
    /// the continuation must come back to the caller's context.</para>
    /// </summary>
    /// <param name="evaluate">Runs a script with a library timeout; the browser's <c>EvaluateScript&lt;string&gt;</c>.</param>
    /// <param name="staleness">Called after the answer arrives: null if the browser and its document are the
    /// ones the read started on, else the reason they are not.</param>
    public static async Task<Result> ReadAsync(
        Func<string, TimeSpan, Task<string?>> evaluate, Func<string?> staleness, TimeSpan timeout, ILogger logger)
    {
        var watch = Stopwatch.StartNew();
        Task<string?> call;
        try
        {
            // The same value bounds the library's evaluation and our own wait. The library's alone does not
            // bound the wait: its timer starts only when the script is dequeued.
            call = evaluate(Script, timeout);
        }
        catch (Exception ex)
        {
            return Fail(Classify(ex), Describe(ex), watch);
        }

        var done = await Task.WhenAny(call, Task.Delay(timeout));
        if (done != call)
        {
            // Abandoned, not cancelled: nothing can cancel it. Observe it, so its eventual exception is not an
            // unobserved task exception, and record how it ended.
            _ = call.ContinueWith(t => logger.Information(
                    "Abandoned selection read finished later: {Status}{Error}",
                    t.Status, t.Exception is { } e ? " — " + Describe(e.GetBaseException()) : ""),
                TaskScheduler.Default);
            return Fail(Failure.Timeout, null, watch);
        }

        string? raw;
        try { raw = await call; }
        catch (Exception ex) { return Fail(Classify(ex), Describe(ex), watch); }

        if (staleness() is { } stale) return Fail(Failure.Stale, stale, watch);
        if (raw is null) return Fail(Failure.ScriptNull, null, watch);

        return Parse(raw) is { } selection
            ? new Result(selection.Text, selection.Paragraph, Failure.None, null, watch.ElapsedMilliseconds)
            : Fail(Failure.Parse, raw.Length > 80 ? raw[..80] + "…" : raw, watch);
    }

    /// <summary>The script's JSON as text and paragraph; null if it is not that JSON.</summary>
    public static (string Text, int? Paragraph)? Parse(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.String)
                return null;
            int? paragraph = doc.RootElement.TryGetProperty("p", out var p)
                             && p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var n)
                ? n : null;
            return (t.GetString()!, paragraph);
        }
        // Not only JsonException: GetString() throws InvalidOperationException on a lone surrogate, and
        // JSON.stringify writes one for an unpaired half in the page. Any reply that is not the script's JSON is
        // a Parse failure, never an exception out of the read. (#827 review)
        catch (Exception) { return null; }
    }

    /// <summary>WebViewControl's own timeout is a <c>JavascriptException</c> named "Timeout", whose constructors
    /// are internal; it is recognised by its message so a test can stand in for it with a TimeoutException.</summary>
    private static Failure Classify(Exception ex) =>
        ex is TimeoutException
        || (ex.GetType().Name == "JavascriptException"
            && (ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("not initialized", StringComparison.OrdinalIgnoreCase)))
            ? Failure.LibraryTimeout
            : Failure.Exception;

    private static string Describe(Exception ex) => ex.GetType().Name + ": " + ex.Message;

    private static Result Fail(Failure failure, string? detail, Stopwatch watch) =>
        new(null, null, failure, detail, watch.ElapsedMilliseconds);
}
