using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #42: the book font face is now a setting injected into a SINGLE stylesheet at transform time, replacing
// the fourteen per-script stylesheets that differed only in their font-family line. Two things have to hold
// or books render wrong in ways that are hard to attribute: every script must resolve to a real font stack,
// and "no choice made" must stay distinguishable from "chose the default".
public class BookFontTests
{
    private static Mock<ISettingsService> SettingsMock()
    {
        var m = new Mock<ISettingsService>();
        m.SetupGet(s => s.Settings).Returns(new Settings());
        return m;
    }

    // ---- Shipped defaults ------------------------------------------------------------------------

    [Fact]
    public void EveryScriptTheStylesheetsCoveredHasItsOwnShippedFace()
    {
        // Asserting For() is non-empty would pass trivially — it falls back to the Latin stack, which IS
        // the botched-migration symptom. Has() is the question that can actually fail. (fable review)
        foreach (var script in BookFontDefaults.All.Keys)
            Assert.True(BookFontDefaults.Has(script), $"No book font stack of its own for {script}.");
    }

    [Fact]
    public void TheDefaultsCoverTheFourteenScriptsTheStylesheetsDid()
    {
        Assert.Equal(14, BookFontDefaults.All.Count);
    }

    [Fact]
    public void NoDefaultCarriesATrailingSemicolonOrNewline()
    {
        // These are interpolated straight into `font-family: {stack};` in the stylesheet. A stray semicolon
        // would terminate the declaration early and silently drop whatever CSS followed it.
        foreach (var (script, face) in BookFontDefaults.All)
        {
            Assert.DoesNotContain(";", face);
            Assert.Equal(face.Trim(), face);
            Assert.DoesNotContain("\n", face);
        }
    }

    [Fact]
    public void QuotedFaceNamesUseDoubleQuotes_NotSingle()
    {
        // The stack lands inside an XSLT-produced style block. Single quotes would still parse as CSS, but
        // the shipped stacks are consistently double-quoted and mixing them invites a quoting bug later.
        foreach (var (_, face) in BookFontDefaults.All)
            Assert.DoesNotContain("'", face);
    }

    // ---- Resolution ------------------------------------------------------------------------------

    [Fact]
    public void WithNoUserChoice_TheShippedDefaultIsUsed()
    {
        var settings = SettingsMock();
        foreach (var script in BookFontDefaults.All.Keys)
            Assert.Equal(BookFontDefaults.For(script), BookFontResolver.Resolve(settings.Object, script));
    }

    [Fact]
    public void AUserChoiceWins()
    {
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Devanagari"].BookFontFamily = "Noto Serif Devanagari";

        // Quoted: a user choice is always CSS-quoted, so "Bodoni 72"-style names stay valid. See the CSS
        // safety section below.
        Assert.Equal("\"Noto Serif Devanagari\"", BookFontResolver.Resolve(settings.Object, Script.Devanagari));
        // ...and only for that script.
        Assert.Equal(BookFontDefaults.For(Script.Latin), BookFontResolver.Resolve(settings.Object, Script.Latin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyOrBlankChoiceFallsBackRatherThanRenderingNothing(string stored)
    {
        // An empty font-family would leave the browser to choose with no guidance at all.
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Thai"].BookFontFamily = stored;
        Assert.Equal(BookFontDefaults.For(Script.Thai), BookFontResolver.Resolve(settings.Object, Script.Thai));
    }

    [Fact]
    public void ResolveNeverReturnsEmpty_EvenWithNoSettingsServiceAtAll()
    {
        // The resolver is called from the render path, which must not depend on settings having loaded.
        foreach (var script in Enum.GetValues<Script>())
            Assert.False(string.IsNullOrWhiteSpace(BookFontResolver.Resolve(null, script)));
    }

    [Fact]
    public void AScriptMissingFromSettingsStillResolves()
    {
        // A settings file written before a script existed has no entry for it.
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts.Remove("Thai");
        Assert.Equal(BookFontDefaults.For(Script.Thai), BookFontResolver.Resolve(settings.Object, Script.Thai));
    }

    // ---- "Riding the default" is distinct from "chose the default" -------------------------------

    [Fact]
    public void HasUserChoice_IsFalseUntilSomethingIsStored()
    {
        // This distinction is why the setting stores empty rather than a copy of the default: a user who
        // never chose keeps tracking the shipped stack instead of being frozen against today's value.
        var settings = SettingsMock();
        Assert.False(BookFontResolver.HasUserChoice(settings.Object, Script.Devanagari));

        settings.Object.Settings.FontSettings.ScriptFonts["Devanagari"].BookFontFamily = "Some Font";
        Assert.True(BookFontResolver.HasUserChoice(settings.Object, Script.Devanagari));
    }

    [Fact]
    public void StoringTheDefaultVerbatimStillCountsAsAChoice()
    {
        // Documents the accepted consequence: if the UI ever wrote the default string instead of empty,
        // that script would stop tracking future changes to the shipped stack. The Settings seeding path
        // deliberately avoids this by not routing the loaded value through the setter.
        //
        // Note the resolved value is NOT byte-identical to the shipped stack in that case: a stored value
        // is treated as a user choice and re-quoted family by family. Same fonts, same order, canonical
        // quoting — but it demonstrates that copying the default into the setting is not a no-op.
        var settings = SettingsMock();
        var shipped = BookFontDefaults.For(Script.Latin);
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = shipped;

        Assert.True(BookFontResolver.HasUserChoice(settings.Object, Script.Latin));

        var resolved = BookFontResolver.Resolve(settings.Object, Script.Latin);
        Assert.Contains("Times Ext Roman", resolved);
        Assert.Contains("Gentium", resolved);
        Assert.Equal(shipped.Split(',').Length, resolved.Split(',').Length);
    }

    // ---- CSS safety of a user-chosen face ---------------------------------------------------------
    // The chosen name is interpolated into a <style> element. Two separate hazards live here, and the
    // first is reachable through the ordinary font picker.

    [Fact]
    public void AFaceNameContainingADigitTokenIsQuoted()
    {
        // "Bodoni 72" and friends ship on macOS and the picker offers them. Unquoted, `font-family:
        // Bodoni 72;` is not valid CSS — the whole declaration is dropped and the book silently renders in
        // the browser default with nothing to explain it. (fable review)
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = "Bodoni 72";

        var resolved = BookFontResolver.Resolve(settings.Object, Script.Latin);
        Assert.Equal("\"Bodoni 72\"", resolved);
    }

    [Fact]
    public void AHandWrittenStackStaysAStack()
    {
        // Quoting the whole value would collapse a legitimate stack into one nonsense family name.
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = "Gentium, Doulos SIL";

        Assert.Equal("\"Gentium\", \"Doulos SIL\"", BookFontResolver.Resolve(settings.Object, Script.Latin));
    }

    [Fact]
    public void AlreadyQuotedNamesAreNotDoubleQuoted()
    {
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = "\"Times Ext Roman\"";
        Assert.Equal("\"Times Ext Roman\"", BookFontResolver.Resolve(settings.Object, Script.Latin));
    }

    [Fact]
    public void MarkupInAFaceNameCannotTerminateTheStyleElement()
    {
        // Only reachable by hand-editing one's own settings file, so self-inflicted — but the HTML parser
        // ends <style> at a literal "</style" regardless of CSS string context, so quoting alone does not
        // contain it. The angle brackets must not survive as themselves. (fable review)
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily =
            "</style><script>alert(1)</script>";

        var resolved = BookFontResolver.Resolve(settings.Object, Script.Latin);
        Assert.DoesNotContain("<", resolved);
        Assert.DoesNotContain(">", resolved);
    }

    [Fact]
    public void ASemicolonCannotEndTheDeclarationEarly()
    {
        // A bare semicolon would terminate font-family and let whatever followed be read as new CSS.
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = "Foo; color: red";

        var resolved = BookFontResolver.Resolve(settings.Object, Script.Latin);
        // Whatever it becomes, the semicolon must be inside quotes rather than loose in the declaration.
        Assert.StartsWith("\"", resolved);
        Assert.EndsWith("\"", resolved);
    }

    [Fact]
    public void AQuoteInAFaceNameIsEscaped_NotLeftToCloseTheString()
    {
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = "Wei\"rd";
        var resolved = BookFontResolver.Resolve(settings.Object, Script.Latin);
        Assert.Equal("\"Wei\\\"rd\"", resolved);
    }

    [Fact]
    public void TheShippedDefaultsArePassedThroughUntouched()
    {
        // They are ours, already correctly quoted, and re-quoting would mangle a multi-family stack.
        var settings = SettingsMock();
        foreach (var (script, shipped) in BookFontDefaults.All)
            Assert.Equal(shipped, BookFontResolver.Resolve(settings.Object, script));
    }

    [Fact]
    public void ADegenerateStoredValueFallsBackToTHISScriptsStack_NotLatins()
    {
        // "," is not whitespace, so it passes the null/blank guard and only reduces to nothing inside the
        // sanitiser. Falling back to Latin there would put a Latin stack in a Devanagari book — tofu, which
        // is worse than the default the user never chose. (fable review)
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Devanagari"].BookFontFamily = ",";

        Assert.Equal(BookFontDefaults.For(Script.Devanagari),
            BookFontResolver.Resolve(settings.Object, Script.Devanagari));
    }

    [Fact]
    public void AValueThatSurvivesTheBlankGuardButSanitisesToNothingStillFallsBack()
    {
        // The gap a pre-quote length check cannot see. None of these is whitespace, so the blank guard
        // passes them, and each family is non-empty going INTO the quoter and empty coming out — the quoter
        // drops control characters and unpaired surrogates. Filtering only before quoting left a stack of
        // `""`, whose length is 2, so the degenerate-value fallback above never fired and the book got
        // `font-family: "";` — valid CSS matching no family at all, i.e. the browser default, in a
        // Devanagari book. Built in code rather than in InlineData so the escapes cannot be mistaken for
        // literal text. (fable review, second pass)
        var control = ((char)1).ToString();          // one control character, nothing else
        var loneSurrogate = ((char)0xD800).ToString();     // one unpaired surrogate, nothing else

        foreach (var stored in new[] { control, loneSurrogate, control + "," + loneSurrogate })
        {
            var settings = SettingsMock();
            settings.Object.Settings.FontSettings.ScriptFonts["Devanagari"].BookFontFamily = stored;

            Assert.Equal(BookFontDefaults.For(Script.Devanagari),
                BookFontResolver.Resolve(settings.Object, Script.Devanagari));
        }
    }

    [Theory]
    [InlineData("Bad\u0000Font")]
    [InlineData("Bad\u0001Font")]
    [InlineData("Bad\ud800Font")]     // lone high surrogate
    public void ControlCharactersAndLoneSurrogatesAreDropped(string stored)
    {
        // These are not merely invalid CSS: the XmlWriter behind XslCompiledTransform rejects them, so one
        // in a hand-edited settings value fails the entire transform and the book renders as an error page.
        var settings = SettingsMock();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = stored;

        var resolved = BookFontResolver.Resolve(settings.Object, Script.Latin);
        Assert.All(resolved, c => Assert.True(c >= 0x20 || c == '\t'));
        Assert.DoesNotContain(resolved, char.IsSurrogate);
    }

    [Fact]
    public void TheSanitisedStackAlwaysSurvivesAnActualXsltTransform()
    {
        // The end-to-end property that matters: whatever is stored, the value can be passed as an XSLT
        // string parameter without throwing. Pins findings 5 and 6 against the real API rather than by
        // reasoning about it.
        var xslt = new System.Xml.Xsl.XslCompiledTransform();
        using var sr = new System.IO.StringReader(
            "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">" +
            "<xsl:param name=\"f\"/><xsl:template match=\"/\"><r><xsl:value-of select=\"$f\"/></r></xsl:template>" +
            "</xsl:stylesheet>");
        using var xr = System.Xml.XmlReader.Create(sr);
        xslt.Load(xr);

        foreach (var stored in new[] { "Bodoni 72", "</style><script>x</script>", ",", "A\u0000B", "A\ud800B", "Wei\"rd" })
        {
            var settings = SettingsMock();
            settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookFontFamily = stored;
            var face = BookFontResolver.Resolve(settings.Object, Script.Latin);

            var args = new System.Xml.Xsl.XsltArgumentList();
            args.AddParam("f", "", face);
            using var w = new System.IO.StringWriter();
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml("<x/>");
            xslt.Transform(doc, args, w);   // must not throw for any stored value
        }
    }

    // ---- Resolving the saved name to a ComboBox item ---------------------------------------------
    // A ComboBox renders SelectedItem only when it equals an item of ItemsSource. Assigning the raw saved
    // string leaves the control BLANK on any difference in case or spacing, and always when the font is
    // gone — which is exactly what happened in testing.

    [Fact]
    public void TheSavedNameResolvesToTheMatchingListEntry()
    {
        var list = new List<string> { "Default", "Shree Devanagari 714", "Kohinoor Devanagari" };
        Assert.Equal("Shree Devanagari 714",
            AppearanceSettingsViewModel.ResolveBookFontSelection(list, "Shree Devanagari 714"));
    }

    [Fact]
    public void MatchingIgnoresCaseAndSurroundingSpace()
    {
        // The saved value can come from a hand-edited file or an older build; the displayed item must be
        // the LIST's spelling, not the saved one, or the control renders blank.
        var list = new List<string> { "Default", "Shree Devanagari 714" };
        Assert.Equal("Shree Devanagari 714",
            AppearanceSettingsViewModel.ResolveBookFontSelection(list, "  shree devanagari 714 "));
    }

    [Fact]
    public void AnUninstalledFontShowsTheDefaultLabel_WithoutErasingTheSavedValue()
    {
        // Display-only fallback. The saved setting is untouched so the choice returns by itself once the
        // font is reinstalled — the #67 rule, and the hook #573 will report through.
        var list = new List<string> { "Default", "Kohinoor Devanagari" };
        Assert.Equal("Default",
            AppearanceSettingsViewModel.ResolveBookFontSelection(list, "A Font That Was Uninstalled"));
    }

    [Fact]
    public void NoSavedChoiceShowsTheDefaultLabel()
    {
        var list = new List<string> { "Default", "Kohinoor Devanagari" };
        Assert.Equal("Default", AppearanceSettingsViewModel.ResolveBookFontSelection(list, null));
        Assert.Equal("Default", AppearanceSettingsViewModel.ResolveBookFontSelection(list, ""));
        Assert.Equal("Default", AppearanceSettingsViewModel.ResolveBookFontSelection(list, "   "));
    }

    // ---- Persistence -----------------------------------------------------------------------------

    [Fact]
    public void TheBookFaceSurvivesAJsonRoundTrip()
    {
        var settings = new Settings();
        settings.FontSettings.ScriptFonts["Myanmar"].BookFontFamily = "Padauk";

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<Settings>(json)!;

        Assert.Equal("Padauk", back.FontSettings.ScriptFonts["Myanmar"].BookFontFamily);
    }

    [Fact]
    public void ASettingsFileWrittenBeforeThisFeatureLoadsAsNoChoice()
    {
        var json = """{"FontSettings":{"ScriptFonts":{"Latin":{"FontFamily":"","FontSize":12}}}}""";
        var back = System.Text.Json.JsonSerializer.Deserialize<Settings>(json)!;
        Assert.Equal("", back.FontSettings.ScriptFonts["Latin"].BookFontFamily);
    }

    [Fact]
    public void TheBookFaceIsIndependentOfTheChromeFace()
    {
        // The two live on the same record and are the app's most confusable pair of settings — #42's own
        // description warns about it, and the Settings description used to get it wrong.
        var settings = new Settings();
        var entry = settings.FontSettings.ScriptFonts["Devanagari"];
        entry.FontFamily = "Chrome Face";
        entry.BookFontFamily = "Book Face";

        Assert.Equal("Chrome Face", entry.FontFamily);
        Assert.Equal("Book Face", entry.BookFontFamily);
    }
}
