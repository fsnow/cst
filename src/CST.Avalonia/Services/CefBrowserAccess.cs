using System;
using System.Linq;
using System.Reflection;
using Serilog;
using WebViewControl;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;

namespace CST.Avalonia.Services;

/// <summary>
/// Reaches the underlying <see cref="CefBrowserHost"/> behind a <see cref="WebView"/>. (#572)
///
/// <para>
/// Most of CEF's browser-level API — zoom, find-in-page, print-to-PDF — has no equivalent on
/// <c>WebViewControl.WebView</c>, which exposes navigation, script execution and little else. Getting at it
/// takes two hops, and only the first two are non-public:
/// </para>
/// <list type="number">
///   <item><c>WebView.UnderlyingBrowser</c> — <b>internal</b> getter, returns WebViewControl's own
///   <c>ChromiumBrowser</c>. That type is internal too, but it derives from the public
///   <see cref="BaseCefBrowser"/>, so the value can be typed once retrieved.</item>
///   <item><c>BaseCefBrowser.UnderlyingBrowser</c> — <b>protected</b> getter, returns <see cref="CefBrowser"/>.</item>
///   <item><c>CefBrowser.GetHost()</c> — public from here on.</item>
/// </list>
///
/// <para>
/// This is the first place in the codebase to reflect into CEF internals. (An earlier note claimed #112's
/// printing work had already mapped this chain; it had not — printing shipped <c>window.print()</c> through
/// JavaScript and never touched the browser object.) Because both hops bind to non-public members of a
/// third-party package, a WebViewControl or CefGlue upgrade can break them silently, so:
/// </para>
/// <list type="bullet">
///   <item>every failure degrades to a null return and a log line, never an exception at a call site;</item>
///   <item><see cref="Probe"/> reports resolvability once at startup, so a broken chain shows up in the log
///   rather than as a mysteriously dead keyboard shortcut;</item>
///   <item><c>CefBrowserAccessTests</c> asserts both members still resolve, so a package bump fails the
///   build rather than the feature.</item>
/// </list>
/// </summary>
public static class CefBrowserAccess
{
    private const BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    // Resolved once. Nullable rather than throwing: a null here must disable the feature, not break startup.
    private static readonly PropertyInfo? WebViewUnderlyingBrowser =
        typeof(WebView).GetProperty("UnderlyingBrowser", NonPublicInstance);

    private static readonly PropertyInfo? BaseBrowserUnderlyingBrowser =
        typeof(BaseCefBrowser).GetProperty("UnderlyingBrowser", NonPublicInstance);

    /// <summary>
    /// True when both non-public hops resolved. False means any CEF-level feature must disable itself.
    /// </summary>
    public static bool IsAvailable => WebViewUnderlyingBrowser != null && BaseBrowserUnderlyingBrowser != null;

    /// <summary>
    /// Names of the members that failed to resolve, for logging. Empty when <see cref="IsAvailable"/>.
    /// </summary>
    public static string MissingMembers => string.Join(", ", new[]
    {
        WebViewUnderlyingBrowser == null ? "WebView.UnderlyingBrowser" : null,
        BaseBrowserUnderlyingBrowser == null ? "BaseCefBrowser.UnderlyingBrowser" : null,
    }.Where(m => m != null));

    /// <summary>
    /// The resolved property types, for the tests to pin. Names surviving an upgrade is not enough: if
    /// either property keeps its name but changes type, the <c>is not</c> checks in
    /// <see cref="TryGetBrowserHost"/> return null forever and the feature dies silently — the exact
    /// failure the tests exist to catch. (fable review)
    /// </summary>
    internal static Type? WebViewBrowserPropertyType => WebViewUnderlyingBrowser?.PropertyType;
    internal static Type? BaseBrowserPropertyType => BaseBrowserUnderlyingBrowser?.PropertyType;

    /// <summary>
    /// Logs whether the reflection chain resolved. Call once during startup so a package upgrade that breaks
    /// it is visible in the log instead of presenting as a silently dead shortcut.
    /// </summary>
    public static void Probe(ILogger logger)
    {
        if (IsAvailable)
            logger.Information("CEF browser access available (WebView.UnderlyingBrowser + BaseCefBrowser.UnderlyingBrowser resolved)");
        else
            logger.Warning("CEF browser access UNAVAILABLE - unresolved: {Missing}. Book zoom (#572) will be inert; " +
                           "a WebViewControl/CefGlue upgrade most likely renamed or re-scoped these members", MissingMembers);
    }

    /// <summary>
    /// Returns the browser host for <paramref name="webView"/>, or null when the chain is unavailable or the
    /// browser is not yet created. Never throws.
    /// </summary>
    public static CefBrowserHost? TryGetBrowserHost(WebView? webView, ILogger logger)
    {
        if (webView == null || !IsAvailable) return null;

        try
        {
            // Not just an optimisation: before initialization UnderlyingBrowser is null, and asking a
            // half-built browser for its host is exactly the sort of thing that crashes CEF rather than
            // returning null.
            if (!webView.IsBrowserInitialized) return null;

            if (WebViewUnderlyingBrowser!.GetValue(webView) is not BaseCefBrowser chromium)
                return null;

            if (BaseBrowserUnderlyingBrowser!.GetValue(chromium) is not CefBrowser browser)
                return null;

            return browser.GetHost();
        }
        catch (Exception ex)
        {
            logger.Error("Failed to reach the CEF browser host | {Details}", ex.Message);
            return null;
        }
    }
}
