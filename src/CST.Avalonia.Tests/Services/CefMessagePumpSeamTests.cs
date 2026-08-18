using System;
using System.Collections;
using System.Reflection;
using CST.Avalonia.Services;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Handlers;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #523: the macOS pump fix replaces CefGlue's browser-process handler at runtime, reaching into four
/// non-public members BY NAME.
///
/// <para><b>These tests exist because the failure mode is silent.</b> If a package upgrade renames any of
/// them, <c>CefMessagePump</c> logs a warning and leaves CefGlue's repeating 1 ms timer in place — no crash,
/// no error, just ~38% of a core burned at idle on every macOS install, which nobody would notice until
/// someone re-measured. Pinning the members here turns that into a build failure.</para>
///
/// <para>Same reasoning and same idiom as <c>CefBrowserAccessTests</c>, which pins the reflection chain book
/// zoom depends on. Nothing here initializes CEF — these are metadata lookups, so they run anywhere.</para>
/// </summary>
public class CefMessagePumpSeamTests
{
    /// <summary><c>CefApp._roots</c> is how we find the live app instance after CEF starts.</summary>
    [Fact]
    public void CefAppRootsField_StillExists()
    {
        var roots = CefMessagePump.RootsField();

        Assert.NotNull(roots);
        Assert.True(roots!.IsStatic);
        Assert.True(typeof(IDictionary).IsAssignableFrom(roots.FieldType),
            $"CefApp._roots must stay dictionary-like to enumerate; it is now {roots.FieldType}.");
    }

    /// <summary>
    /// <c>BrowserCefApp._browserProcessHandler</c>. Located on the internal concrete app type, so this test
    /// finds that type the same way the code does — by looking for the field on a candidate type.
    /// </summary>
    [Fact]
    public void BrowserCefApp_StillHoldsItsProcessHandler()
    {
        var appType = typeof(BrowserProcessHandler).Assembly.GetType("Xilium.CefGlue.Common.BrowserCefApp");

        Assert.True(appType is not null,
            "BrowserCefApp not found. CefMessagePump.FindLiveCefApp scans _roots for a type carrying " +
            "_browserProcessHandler, so a rename here means the pump fix silently stops applying (#523).");

        Assert.NotNull(CefMessagePump.BrowserProcessHandlerField(appType!));
    }

    /// <summary>
    /// <c>CommonBrowserProcessHandler._handler</c> is the slot we overwrite. Declared readonly — reflection can
    /// still write it — but the field must exist and must hold a <see cref="BrowserProcessHandler"/>.
    /// </summary>
    [Fact]
    public void CommonBrowserProcessHandler_StillHasTheSwappableHandlerSlot()
    {
        var type = typeof(BrowserProcessHandler).Assembly
            .GetType("Xilium.CefGlue.Common.InternalHandlers.CommonBrowserProcessHandler");

        Assert.True(type is not null, "CommonBrowserProcessHandler not found (#523).");

        var field = CefMessagePump.InnerHandlerField(type!);
        Assert.NotNull(field);
        Assert.Equal(typeof(BrowserProcessHandler), field!.FieldType);
    }

    /// <summary>
    /// <c>AvaloniaBrowserProcessHandler._current</c> holds the running 1 ms subscription we dispose. Losing
    /// this one is the mildest failure — both timers would run — so the code only warns, and this test is what
    /// makes it visible.
    /// </summary>
    [Fact]
    public void OutgoingHandler_StillExposesItsRunningSubscription()
    {
        var type = typeof(AvaloniaCefBrowser).Assembly
            .GetType("Xilium.CefGlue.Avalonia.AvaloniaBrowserProcessHandler");

        Assert.True(type is not null,
            "CefGlue no longer ships AvaloniaBrowserProcessHandler. Re-measure idle CPU (#523): the upstream " +
            "1 ms pump may have been fixed, in which case CefMessagePump can be deleted rather than repaired.");

        var field = CefMessagePump.CurrentSubscriptionField(type!);
        Assert.NotNull(field);
        Assert.True(typeof(IDisposable).IsAssignableFrom(field!.FieldType),
            $"_current must stay IDisposable to be stopped; it is now {field.FieldType}.");
    }

    /// <summary>
    /// Our replacement derives from the public <see cref="BrowserProcessHandler"/> and overrides
    /// <c>OnScheduleMessagePumpWork(long)</c>. If that stops being overridable our override is never called and
    /// the swap achieves nothing.
    /// </summary>
    [Fact]
    public void OnScheduleMessagePumpWork_IsStillOverridable()
    {
        var method = typeof(CefBrowserProcessHandler).GetMethod(
            "OnScheduleMessagePumpWork",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.True(method!.IsVirtual, "OnScheduleMessagePumpWork must stay virtual (#523).");
        Assert.False(method.IsFinal, "OnScheduleMessagePumpWork must not be sealed (#523).");

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(long), parameters[0].ParameterType);
    }

    /// <summary><see cref="BrowserProcessHandler"/> is the one supported, non-reflective part of this.</summary>
    [Fact]
    public void BrowserProcessHandler_IsStillPublicAndInheritable()
    {
        Assert.True(typeof(BrowserProcessHandler).IsPublic);
        Assert.False(typeof(BrowserProcessHandler).IsSealed);
    }

    /// <summary>
    /// Before CEF starts there is no app instance, so the lookup must return null rather than throw — that is
    /// what lets the install poll harmlessly until CEF is up.
    /// </summary>
    [Fact]
    public void FindLiveCefApp_ReturnsNull_BeforeCefStarts()
    {
        Assert.Null(CefMessagePump.FindLiveCefApp());
    }

    // ---- behaviour, not just metadata (fable review) --------------------------------------------------

    /// <summary>
    /// A request of "now" must stay "now". Upstream turned <c>&lt;= 0</c> into a 1 ms wait; turning it into a
    /// 33 ms wait would add latency to every urgent wake, which is the regression this change must not cause.
    /// </summary>
    [Fact]
    public void DueDelay_TreatsNowAsNow()
    {
        Assert.Equal(0, CefMessagePump.DueDelayMs(0));
        Assert.Equal(0, CefMessagePump.DueDelayMs(-1));
        Assert.Equal(0, CefMessagePump.DueDelayMs(long.MinValue));
    }

    /// <summary>A request shorter than the pump period is honoured exactly — we never make CEF wait longer
    /// than it asked.</summary>
    [Theory]
    [InlineData(1L)]
    [InlineData(5L)]
    [InlineData(32L)]
    public void DueDelay_HonoursShortRequests(long requested)
    {
        Assert.Equal(requested, CefMessagePump.DueDelayMs(requested));
    }

    /// <summary>
    /// Long requests are capped at the pump period. The repeating timer doubles as the watchdog that recovers a
    /// lost tick, so it must never sleep for the multi-second delay CEF asks for when it is idle.
    /// </summary>
    [Theory]
    [InlineData(34L)]
    [InlineData(1000L)]
    [InlineData(long.MaxValue)]
    public void DueDelay_CapsLongRequestsAtThePumpPeriod(long requested)
    {
        Assert.Equal(33, CefMessagePump.DueDelayMs(requested));
    }

    /// <summary>
    /// The swap writes a <c>readonly</c> instance field by reflection. That is legal today, but .NET already
    /// refuses reflection writes to <i>static</i> InitOnly fields and the instance case could follow — in which
    /// case the swap throws, is caught, and the pump silently stays at 1 kHz. No metadata assertion can catch a
    /// runtime-behaviour change, so actually perform the write. (fable review)
    /// </summary>
    [Fact]
    public void ReadonlyHandlerField_IsStillWritableByReflection()
    {
        var type = typeof(BrowserProcessHandler).Assembly
            .GetType("Xilium.CefGlue.Common.InternalHandlers.CommonBrowserProcessHandler");
        Assert.True(type is not null, "CommonBrowserProcessHandler not found (#523).");

        var field = CefMessagePump.InnerHandlerField(type!);
        Assert.NotNull(field);

        // An uninitialized instance is enough: we are testing the field write, not the type's behaviour.
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type!);
        var sentinel = new SentinelHandler();

        field!.SetValue(instance, sentinel);

        Assert.Same(sentinel, field.GetValue(instance));
    }

    private sealed class SentinelHandler : BrowserProcessHandler { }
}
