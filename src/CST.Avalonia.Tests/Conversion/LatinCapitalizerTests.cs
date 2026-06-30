using System;
using System.IO;
using System.Text.RegularExpressions;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion
{
    /// <summary>
    /// Tests for LatinCapitalizer, exercised through its only public caller, Deva2Latn.ConvertBook
    /// (the class itself is internal). ConvertBook marks sentence/paragraph-initial Devanagari letters,
    /// converts to Latin, then upper-cases the marked letters.
    ///
    /// Behaviour was derived by running ConvertBook over the first paragraphs of DN1 (s0101m.mul.xml):
    ///  - the first letter of a paragraph element (p / head / trailer) is capitalized;
    ///  - the first letter after a danda (U+0964), '?' or '!' is capitalized (sentence start);
    ///  - ',' and ';' do NOT start a new sentence;
    ///  - a paragraph number is a Devanagari digit (U+0966-U+096F), which is outside the
    ///    "letter" range (U+0901-U+094B), so the capital falls on the first real word, not the number;
    ///  - text inside an ignore element (note) is skipped (never capitalized), and a capital pending
    ///    from a preceding danda survives the note and lands on the first word after it.
    /// Devanagari is written with \uXXXX escapes per the repo convention.
    /// </summary>
    public class LatinCapitalizerTests
    {
        // --- Devanagari inputs (\u escapes) ---
        private const string Bhagava = "\u092D\u0917\u0935\u093E";          // -> "bhagava" + macron -> "bhagav\u0101"
        private const string Dhammo = "\u0927\u092E\u094D\u092E\u094B";     // -> "dhammo"
        private const string Sangho = "\u0938\u0919\u094D\u0918\u094B";     // -> "sa\u1E45gho"
        private const char Danda = '\u0964';                                // single danda
        private const string DevaDigitOne = "\u0967";                       // Devanagari '1'

        // --- Expected Latin (\u escapes for diacritics) ---
        private const string BhagavaCap = "Bhagav\u0101";   // Bhagav\u0101
        private const string BhagavaLower = "bhagav\u0101"; // bhagav\u0101
        private const string SanghoCap = "Sa\u1E45gho";     // Sa\u1E45gho

        private static string P(string inner) => $"<p rend=\"bodytext\">{inner}</p>";

        [Fact]
        public void ParagraphStart_CapitalizesFirstLetter()
        {
            var html = Deva2Latn.ConvertBook(P(Bhagava));
            Assert.Contains(BhagavaCap, html);
            Assert.DoesNotContain(BhagavaLower, html);
        }

        [Fact]
        public void ParagraphNumber_IsNotCapitalized_FirstWordIs()
        {
            // <hi rend="paranum">\u0967</hi><hi rend="dot">.</hi> then the first word.
            var html = Deva2Latn.ConvertBook(
                P($"<hi rend=\"paranum\">{DevaDigitOne}</hi><hi rend=\"dot\">.</hi> {Bhagava}"));

            // The number renders as "1" (a digit, never capitalized) and the word after it is capitalized.
            Assert.Contains("<hi rend=\"paranum\">1</hi>", html);
            Assert.Contains(BhagavaCap, html);
        }

        [Fact]
        public void Danda_StartsNewSentence_NextWordCapitalized_AndBecomesPeriod()
        {
            var html = Deva2Latn.ConvertBook(P($"{Bhagava}{Danda} {Dhammo}"));
            Assert.Contains($"{BhagavaCap}. Dhammo", html);
        }

        [Fact]
        public void QuestionMark_StartsNewSentence_NextWordCapitalized()
        {
            var html = Deva2Latn.ConvertBook(P($"{Bhagava}? {Dhammo}"));
            Assert.Contains($"{BhagavaCap}? Dhammo", html);
        }

        [Fact]
        public void ExclamationMark_StartsNewSentence_NextWordCapitalized()
        {
            var html = Deva2Latn.ConvertBook(P($"{Bhagava}! {Dhammo}"));
            Assert.Contains($"{BhagavaCap}! Dhammo", html);
        }

        [Fact]
        public void Comma_DoesNotStartNewSentence()
        {
            var html = Deva2Latn.ConvertBook(P($"{Bhagava}, {Dhammo}"));
            Assert.Contains($"{BhagavaCap}, dhammo", html);
        }

        [Fact]
        public void Semicolon_DoesNotStartNewSentence()
        {
            var html = Deva2Latn.ConvertBook(P($"{Bhagava}; {Dhammo}"));
            Assert.Contains($"{BhagavaCap}; dhammo", html);
        }

        [Fact]
        public void PlainSpace_DoesNotCapitalizeFollowingWord()
        {
            // Only the paragraph-initial word is capitalized; a word merely preceded by a space is not.
            var html = Deva2Latn.ConvertBook(P($"{Bhagava} {Dhammo}"));
            Assert.Contains($"{BhagavaCap} dhammo", html);
        }

        [Fact]
        public void NoteSubtree_IsNotCapitalized_AndPendingCapitalSurvivesIt()
        {
            // Danda sets a pending capital; the note subtree is skipped (its text stays lowercase) and the
            // pending capital lands on the first word after the note.
            var html = Deva2Latn.ConvertBook(P($"{Bhagava}{Danda} <note>{Dhammo}</note> {Sangho}"));
            Assert.Contains("<note>dhammo</note>", html); // note text not capitalized
            Assert.Contains(SanghoCap, html);             // word after note is capitalized
        }

        [Fact]
        public void HeadElement_CapitalizesFirstWord()
        {
            var html = Deva2Latn.ConvertBook($"<head rend=\"chapter\">{DevaDigitOne}. {Bhagava}</head>");
            Assert.Contains($"1. {BhagavaCap}", html);
        }

        [Fact]
        public void TrailerElement_CapitalizesFirstWord()
        {
            var html = Deva2Latn.ConvertBook($"<trailer rend=\"book\">{Bhagava}</trailer>");
            Assert.Contains(BhagavaCap, html);
        }

        // --- Integration over real corpus text (skips cleanly when the corpus is not present) ---

        private static string XmlDir =>
            Environment.GetEnvironmentVariable("CST_XML_DIR")
            ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "",
                            "Library", "Application Support", "CSTReader", "xml");

        [Fact]
        public void RealDn1FirstParagraph_HasExpectedSentenceCapitalization()
        {
            var path = Path.Combine(XmlDir, "s0101m.mul.xml");
            if (!File.Exists(path))
                return; // corpus not available in this environment

            var book = File.ReadAllText(path);
            var para = Regex.Match(book, "<p rend=\"bodytext\"[^>]*>.*?</p>", RegexOptions.Singleline);
            Assert.True(para.Success, "Expected a bodytext paragraph in DN1");

            var html = Deva2Latn.ConvertBook(para.Value);

            // Paragraph number stays a digit; first word capitalized.
            Assert.Contains("<hi rend=\"paranum\">1</hi>", html);
            Assert.Contains("Eva\u1E43", html);                 // Eva\u1E43 (paragraph start)
            // Each of these begins a sentence after a danda.
            Assert.Contains("Suppiyopi", html);
            Assert.Contains("Tatra", html);
            Assert.Contains("Itiha", html);
            // ',' / ';' keep the following word lowercase.
            Assert.Contains("dhammassa", html);
            // Note text is not capitalized.
            Assert.Contains("<note>anubaddh\u0101", html);      // anubaddh\u0101
        }
    }
}
