using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using CST.Avalonia.UiTests.TestSupport;

// [AvaloniaFact] needs ONE application per test assembly, declared at assembly level. Every test in this
// project runs in the same process, so this attribute is the whole project's Avalonia application - see
// HeadlessStyleTestApp for what that does and does not change for the tests that predate it.
[assembly: AvaloniaTestApplication(typeof(HeadlessStyleTestApp))]

namespace CST.Avalonia.UiTests.TestSupport;

/// <summary>
/// The headless Avalonia application that <c>[AvaloniaFact]</c> tests run inside. It exists so a XAML
/// style selector that matches NOTHING can fail a test. (#655)
///
/// <para><b>What it is for.</b> A selector that matches no element compiles, deploys and does nothing:
/// no warning, no exception, no log line. #646 shipped exactly that -
/// <c>Border#PART_LayoutRoot &gt; ContentPresenter#PART_HeaderPresenter</c>, dead because Avalonia's
/// <c>&gt;</c> resolves on the LOGICAL parent and the header presenter's logical parent is
/// <c>Grid#PART_Header</c> - and a full green suite said nothing. In dark mode the default foreground is
/// already white, so the bug was invisible there; whether a human caught it came down to which OS
/// appearance the reviewer happened to be running.</para>
///
/// <para><b>What it loads, and why exactly this.</b> <see cref="FluentTheme"/>, Dock's Fluent theme and
/// the app's own <c>DockStyles.axaml</c>, mirroring <c>App.axaml</c>'s <c>Application.Styles</c>. Fluent
/// supplies the control templates whose named parts the selectors reach into (a missing template makes
/// every such assertion vacuous rather than red); Dock's theme supplies the <c>DockApplicationAccent*</c>
/// brushes, which are defined in no other loaded resource dictionary. Deliberately NOT loaded: the app's
/// own <c>Application.Resources</c>, its converters-as-resources and its <c>ControlRecycling</c> wiring.
/// Those belong to a running app; a style test that needed them would be testing the app's startup path
/// under a name that promises otherwise.</para>
///
/// <para><b>What this does NOT prove</b>, in the issue author's words: "Not a substitute for looking at
/// the app - headless proves a selector matched and a property took a value, not that the result is
/// legible or attractive." A green run here means the rule reached the element it names. Whether the
/// resulting colours are readable, or the right choice, still needs eyes on the app in both theme
/// variants. Do not let this fixture retire that step.</para>
///
/// <para><b>Why this lives in its own test project.</b> An Avalonia application is process-global, and so
/// is the dispatcher it binds: once the first <c>[AvaloniaFact]</c> starts the headless session,
/// <c>Dispatcher.UIThread</c> belongs to that session's thread and <c>CheckAccess()</c> is false on every
/// other thread in the process. Put this application in <c>CST.Avalonia.Tests</c> and 88 of its tests fail
/// - measured, see the note in <c>CST.Avalonia.UiTests.csproj</c> for the breakdown. Nothing in a test can
/// undo that, so the two sets of assumptions live in two assemblies, which is two processes.
/// <c>HeadlessApplicationLeakTests</c> pins the facts that split turns on, so the day they stop being true
/// the split can be revisited on evidence rather than on this comment.</para>
/// </summary>
public class HeadlessStyleTestApp : Application
{
    public override void Initialize()
    {
        // Mirrors App.axaml's <Application.Styles>: FluentTheme, then Dock's Fluent theme, then ours.
        // Order matters the same way it does there - later styles win, and DockStyles.axaml is written
        // expecting to sit last.
        Styles.Add(new FluentTheme());
        Styles.Add(LoadStyle("avares://Dock.Avalonia.Themes.Fluent/DockFluentTheme.axaml"));
        Styles.Add(LoadStyle("avares://CST.Avalonia/Styles/DockStyles.axaml"));
    }

    // StyleInclude resolves Source against a base URI. Every source here is absolute, so the base is
    // never consulted; it is supplied because the constructor requires one.
    private static StyleInclude LoadStyle(string uri) =>
        new(new Uri("avares://CST.Avalonia/Styles/")) { Source = new Uri(uri) };

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessStyleTestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                // Real (headless) drawing rather than the no-op renderer: the selectors under test are
                // reached through control TEMPLATES, and a template is only applied once the control is
                // measured and arranged.
                UseHeadlessDrawing = true
            });
}
