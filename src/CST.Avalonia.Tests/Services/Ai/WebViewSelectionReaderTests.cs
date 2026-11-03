using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using Xunit;
using Failure = CST.Avalonia.Services.Ai.WebViewSelectionReader.Failure;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Reading the assistant's selection by evaluating a script, against fake transports. (#827)
///
/// <para>The browser cannot be run here, so what is under test is every decision the read makes about what
/// came back: the bound, the failures and their causes, staleness, and the null-versus-empty contract that
/// keeps a failed read from looking like "nothing selected" (#581).</para>
/// </summary>
public class WebViewSelectionReaderTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);
    private static readonly Serilog.ILogger Log = Serilog.Core.Logger.None;
    private static string? Current() => null;

    private static Task<WebViewSelectionReader.Result> Read(
        Func<string, TimeSpan, Task<string?>> evaluate, Func<string?>? staleness = null) =>
        WebViewSelectionReader.ReadAsync(evaluate, staleness ?? Current, Short, Log);

    private static Func<string, TimeSpan, Task<string?>> Answers(string? json) => (_, _) => Task.FromResult(json);

    [Fact]
    public async Task A_selection_and_its_paragraph_come_back()
    {
        var r = await Read(Answers("{\"t\":\"evam me sutam\",\"p\":\"548\"}"));

        Assert.Equal(Failure.None, r.Failure);
        Assert.Equal("evam me sutam", r.Text);
        Assert.Equal(548, r.Paragraph);
    }

    /// <summary>Nothing selected is an answer, not a failure: empty, never null. (#581)</summary>
    [Fact]
    public async Task Nothing_selected_is_empty_not_null()
    {
        var r = await Read(Answers("{\"t\":\"\",\"p\":\"\"}"));

        Assert.Equal(Failure.None, r.Failure);
        Assert.Equal("", r.Text);
        Assert.Null(r.Paragraph);
    }

    [Fact]
    public async Task A_paragraph_that_is_not_a_number_is_no_paragraph()
    {
        var r = await Read(Answers("{\"t\":\"x\",\"p\":\"\"}"));

        Assert.Equal("x", r.Text);
        Assert.Null(r.Paragraph);
    }

    [Fact]
    public async Task A_read_that_never_answers_fails_at_the_bound()
    {
        var never = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var r = await Read((_, _) => never.Task);

        Assert.Equal(Failure.Timeout, r.Failure);
        Assert.Null(r.Text);
    }

    /// <summary>An answer after the bound is not used, and its task is observed rather than left unobserved.</summary>
    [Fact]
    public async Task A_late_answer_is_not_used()
    {
        var late = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var r = await Read((_, _) => late.Task);
        late.SetResult("{\"t\":\"too late\",\"p\":\"1\"}");

        Assert.Equal(Failure.Timeout, r.Failure);
        Assert.Null(r.Text);
    }

    /// <summary>A disposed browser, or a script that did not return, arrives as null.</summary>
    [Fact]
    public async Task A_null_value_is_a_failure_not_an_empty_selection()
    {
        var r = await Read(Answers(null));

        Assert.Equal(Failure.ScriptNull, r.Failure);
        Assert.Null(r.Text);
    }

    [Fact]
    public async Task The_librarys_timeout_is_reported_as_one()
    {
        var r = await Read((_, _) => Task.FromException<string?>(new TimeoutException("Script evaluation timed out")));

        Assert.Equal(Failure.LibraryTimeout, r.Failure);
        Assert.Null(r.Text);
    }

    [Fact]
    public async Task Any_other_exception_is_reported_with_its_type()
    {
        var r = await Read((_, _) => Task.FromException<string?>(new InvalidOperationException("boom")));

        Assert.Equal(Failure.Exception, r.Failure);
        Assert.Contains("InvalidOperationException", r.Detail);
        Assert.Null(r.Text);
    }

    [Fact]
    public async Task An_exception_thrown_before_a_task_exists_is_reported_too()
    {
        var r = await Read((_, _) => throw new ObjectDisposedException("WebView"));

        Assert.Equal(Failure.Exception, r.Failure);
        Assert.Null(r.Text);
    }

    [Fact]
    public async Task A_value_that_is_not_the_scripts_json_is_a_parse_failure()
    {
        var r = await Read(Answers("not json"));

        Assert.Equal(Failure.Parse, r.Failure);
        Assert.Null(r.Text);
    }

    /// <summary>
    /// JSON.stringify writes an unpaired surrogate as an escape, and reading it back as a string throws
    /// InvalidOperationException rather than JsonException. It is a parse failure, not an exception out of the
    /// read. (#827 review)
    /// </summary>
    [Fact]
    public async Task A_lone_surrogate_in_the_reply_is_a_parse_failure()
    {
        var r = await Read(Answers("{\"t\":\"a\\ud800b\",\"p\":\"1\"}"));

        Assert.Equal(Failure.Parse, r.Failure);
        Assert.Null(r.Text);
    }

    /// <summary>
    /// A read that crossed a reload is about a document that is gone. An empty selection from it would tell
    /// the assistant the reader selected nothing; it must be a failure. (#827)
    /// </summary>
    [Fact]
    public async Task A_read_that_crossed_a_reload_is_a_failure_even_if_it_found_nothing_selected()
    {
        var generation = 1;
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var reading = WebViewSelectionReader.ReadAsync(
            (_, _) => answer.Task,
            () => generation != 1 ? "the book was reloaded" : null,
            TimeSpan.FromSeconds(5), Log);
        generation++;
        answer.SetResult("{\"t\":\"\",\"p\":\"\"}");
        var r = await reading;

        Assert.Equal(Failure.Stale, r.Failure);
        Assert.Equal("the book was reloaded", r.Detail);
        Assert.Null(r.Text);
    }

    /// <summary>The script is passed the same bound the read waits on, so the library frees its queue too.</summary>
    [Fact]
    public async Task The_library_is_given_the_same_bound()
    {
        TimeSpan? given = null;

        await Read((_, timeout) => { given = timeout; return Task.FromResult<string?>("{\"t\":\"\",\"p\":\"\"}"); });

        Assert.Equal(Short, given);
    }

    /// <summary>
    /// CefGlue evaluates a script as a function body, so without a top-level return the value is undefined and
    /// arrives as null — which is how this codebase came to believe "EvaluateScript returns null". (#827)
    /// </summary>
    [Fact]
    public void The_script_ends_in_a_top_level_return()
    {
        Assert.Matches(new Regex(@"\breturn JSON\.stringify\(\{ t: text, p: para \}\);\s*$"), WebViewSelectionReader.Script);
        Assert.DoesNotMatch(new Regex(@"^\s*\(function"), WebViewSelectionReader.Script);
    }

    [Fact]
    public void The_bound_is_short()
    {
        // An abandoned read holds the book's script queue until the library's timer expires, so this is also
        // how long a stuck read can delay status updates. The probe's reads took 0-39 ms. (#827)
        Assert.InRange(WebViewSelectionReader.Timeout, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
    }
}
