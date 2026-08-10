using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #572: book zoom reaches CefBrowserHost through two reflection hops into non-public members of
// WebViewControl and CefGlue. Reflection into a third-party package's internals breaks SILENTLY on an
// upgrade - the shortcut simply stops doing anything, with no exception and nothing obviously wrong.
//
// These tests exist to convert that into a build failure. If a WebViewControl or CefGlue bump renames or
// re-scopes either member, this goes red and names the member, instead of the feature quietly dying in the
// field. Nothing here needs a live browser: resolution is static.
public class CefBrowserAccessTests
{
    [Fact]
    public void ReflectionChain_StillResolves_AgainstTheCurrentPackages()
    {
        Assert.True(
            CefBrowserAccess.IsAvailable,
            $"The CEF reflection chain no longer resolves. Unresolved: '{CefBrowserAccess.MissingMembers}'. " +
            "A WebViewControl/CefGlue upgrade has most likely renamed or re-scoped these members. Book zoom " +
            "(#572) is inert until the chain in CefBrowserAccess is updated to match.");
    }

    [Fact]
    public void MissingMembers_IsEmpty_WhenChainResolves()
    {
        // Guards the diagnostic itself: a MissingMembers that stays empty while IsAvailable is false would
        // make the failure above unactionable.
        if (CefBrowserAccess.IsAvailable)
            Assert.Equal("", CefBrowserAccess.MissingMembers);
        else
            Assert.NotEqual("", CefBrowserAccess.MissingMembers);
    }

    // Names alone are not enough. If a package bump keeps both property names but changes either TYPE, the
    // `is not BaseCefBrowser` / `is not CefBrowser` pattern checks in TryGetBrowserHost fall through to null
    // on every call — the feature dies silently while the name-only assertions above stay green. (fable review)
    [Fact]
    public void WebViewUnderlyingBrowser_IsStillAssignableToBaseCefBrowser()
    {
        var type = CefBrowserAccess.WebViewBrowserPropertyType;
        Assert.NotNull(type);
        Assert.True(
            typeof(Xilium.CefGlue.Common.BaseCefBrowser).IsAssignableFrom(type),
            $"WebView.UnderlyingBrowser is now '{type}', which does not derive from BaseCefBrowser. " +
            "TryGetBrowserHost's type check will return null forever until CefBrowserAccess is updated.");
    }

    [Fact]
    public void BaseCefBrowserUnderlyingBrowser_IsStillCefBrowser()
    {
        var type = CefBrowserAccess.BaseBrowserPropertyType;
        Assert.NotNull(type);
        Assert.True(
            typeof(Xilium.CefGlue.CefBrowser).IsAssignableFrom(type),
            $"BaseCefBrowser.UnderlyingBrowser is now '{type}', not a CefBrowser. Book zoom is inert until " +
            "CefBrowserAccess is updated to match.");
    }

    [Fact]
    public void CefBrowser_StillExposesGetHost_Publicly()
    {
        // The third hop is public API rather than reflection, so a rename here is a compile error in
        // CefBrowserAccess — but only while something still calls it. Pinned so the contract is documented
        // in one place with the other two.
        var getHost = typeof(Xilium.CefGlue.CefBrowser).GetMethod("GetHost", System.Type.EmptyTypes);
        Assert.NotNull(getHost);
        Assert.True(getHost!.IsPublic);
        Assert.Equal(typeof(Xilium.CefGlue.CefBrowserHost), getHost.ReturnType);
    }

    [Fact]
    public void TryGetBrowserHost_ReturnsNull_ForNullWebView()
    {
        // The call sites treat null as "zoom unavailable, do nothing". Anything thrown here would surface as
        // a crash on a keystroke.
        Assert.Null(CefBrowserAccess.TryGetBrowserHost(null, Serilog.Log.Logger));
    }
}
