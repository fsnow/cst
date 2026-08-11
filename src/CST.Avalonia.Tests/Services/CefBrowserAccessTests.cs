using System.Linq;
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

    // ---- Find in Page (#570) -------------------------------------------------------------------
    // Find reaches CEF through the same chain, plus three API surfaces of its own. All three are ordinary
    // public API rather than reflection, so a rename is a compile error — but the FindHandler PROPERTY is
    // the extension point the whole feature hangs on, and a change to its accessibility or type would be a
    // silent behaviour loss rather than a build break.

    [Fact]
    public void BaseCefBrowser_StillExposesASettableFindHandler()
    {
        var prop = typeof(Xilium.CefGlue.Common.BaseCefBrowser)
            .GetProperty("FindHandler", System.Reflection.BindingFlags.Instance |
                                        System.Reflection.BindingFlags.Public |
                                        System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(prop);
        Assert.True(prop!.SetMethod?.IsPublic == true,
            "BaseCefBrowser.FindHandler no longer has a public setter. Find in Page attaches its result " +
            "handler through it; without that, searches still run but no match counts ever arrive.");
        Assert.True(typeof(Xilium.CefGlue.Common.Handlers.FindHandler).IsAssignableFrom(prop.PropertyType),
            $"BaseCefBrowser.FindHandler is now '{prop.PropertyType}'.");
    }

    [Fact]
    public void FindHandler_IsStillSubclassable()
    {
        // CstFindHandler derives from it and overrides OnFindResult. A sealed class or a non-virtual
        // callback would break find with no compile error at the call sites.
        var t = typeof(Xilium.CefGlue.Common.Handlers.FindHandler);
        Assert.True(t.IsPublic);
        Assert.False(t.IsSealed);

        // It is ABSTRACT with a PROTECTED parameterless constructor — subclassable, which is the whole
        // requirement, but not directly instantiable. (GetConstructor's default overload only returns
        // public ones, which is why the accessibility has to be asked for explicitly.)
        var ctor = t.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic,
            binder: null, System.Type.EmptyTypes, modifiers: null);
        Assert.NotNull(ctor);
        Assert.True(ctor!.IsPublic || ctor.IsFamily,
            "Handlers.FindHandler's parameterless constructor is no longer reachable from a subclass, " +
            "so CstFindHandler cannot derive from it.");

        // OnFindResult is declared on the BASE, Xilium.CefGlue.CefFindHandler, and reflection does not
        // return non-public members of base classes — so it has to be looked up where it lives.
        Assert.True(typeof(Xilium.CefGlue.CefFindHandler).IsAssignableFrom(t),
            $"Handlers.FindHandler no longer derives from CefFindHandler (base is '{t.BaseType}').");

        var onFindResult = typeof(Xilium.CefGlue.CefFindHandler)
            .GetMethod("OnFindResult", System.Reflection.BindingFlags.Instance |
                                       System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(onFindResult);
        Assert.True(onFindResult!.IsVirtual, "CefFindHandler.OnFindResult is no longer overridable.");

        // The callback's shape, which CstFindHandler overrides literally.
        Assert.Equal(
            new[] { "CefBrowser", "Int32", "Int32", "CefRectangle", "Int32", "Boolean" },
            onFindResult.GetParameters().Select(p => p.ParameterType.Name).ToArray());
    }

    [Fact]
    public void CefBrowserHost_StillExposesFindAndStopFinding_WithTheExpectedShape()
    {
        // Pinned because the shape HAS changed before: CEF 120 dropped the leading `identifier` argument
        // that older versions' Find took. A future bump doing something similar would otherwise surface as
        // a confusing compile error far from here.
        var find = typeof(Xilium.CefGlue.CefBrowserHost).GetMethod("Find",
            new[] { typeof(string), typeof(bool), typeof(bool), typeof(bool) });
        Assert.NotNull(find);
        Assert.True(find!.IsPublic);

        var stop = typeof(Xilium.CefGlue.CefBrowserHost).GetMethod("StopFinding", new[] { typeof(bool) });
        Assert.NotNull(stop);
        Assert.True(stop!.IsPublic);
    }

    [Fact]
    public void TryGetChromiumBrowser_ReturnsNull_ForNullWebView()
    {
        Assert.Null(CefBrowserAccess.TryGetChromiumBrowser(null, Serilog.Log.Logger));
    }

    [Fact]
    public void TryGetBrowserHost_ReturnsNull_ForNullWebView()
    {
        // The call sites treat null as "zoom unavailable, do nothing". Anything thrown here would surface as
        // a crash on a keystroke.
        Assert.Null(CefBrowserAccess.TryGetBrowserHost(null, Serilog.Log.Logger));
    }
}
