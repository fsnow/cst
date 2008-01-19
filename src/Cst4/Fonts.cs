using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using CST.Conversion;

namespace CST
{
    public static class Fonts
    {
        static Fonts()
        {
            faces = new Dictionary<Script, string>();
            controlFontSizes = new Dictionary<Script, float>();

            faces[Script.Bengali] = "Vrinda";
            controlFontSizes[Script.Bengali] = 14.0f;

            faces[Script.Cyrillic] = "Doulos SIL";
            controlFontSizes[Script.Cyrillic] = 10.0f;

            faces[Script.Devanagari] = "CDAC-GISTSurekh";
            controlFontSizes[Script.Devanagari] = 10.0f;

            faces[Script.Gujarati] = "Shruti";

            faces[Script.Gurmukhi] = "Raavi";

            faces[Script.Kannada] = "Tunga";

            faces[Script.Khmer] = "Khmer Mondulkiri U OT ls";
            controlFontSizes[Script.Khmer] = 15.0f;

            faces[Script.Latin] = "Microsoft Sans Serif";

            faces[Script.Malayalam] = "Kartika";

            faces[Script.Myanmar] = "Myanmar1";
            controlFontSizes[Script.Myanmar] = 12.0f;

            faces[Script.Sinhala] = "KaputaUnicode";
            controlFontSizes[Script.Sinhala] = 13.0f;

            faces[Script.Telugu] = "Gautami";

            faces[Script.Thai] = "Microsoft Sans Serif";

            faces[Script.Tibetan] = "Tibetan Machine Uni";
            controlFontSizes[Script.Tibetan] = 12.0f;
        }

        private static Dictionary<Script, string> faces;
        private static Dictionary<Script, float> controlFontSizes;


        public static Font GetControlFont(Script script)
        {
            string face = "Microsoft Sans Serif";
            if (faces.ContainsKey(script))
                face = faces[script];

            float size = 9.75f;
            if (controlFontSizes.ContainsKey(script))
                size = controlFontSizes[script];

            return new Font(face, size);
        }

        public static Font GetListBoxFont(Script script)
        {
            return GetControlFont(script);
        }

        public static Script GetWindowsSafeScript(Script script)
        {
			if (WindowsVersion == WindowsVersion.XP)
			{
				if (script == Script.Bengali ||  // Bengali is supported, but the font is too small to read
					script == Script.Cyrillic || // Cyrillic is supported, but the diacritics for Pali are not
					script == Script.Khmer ||
					script == Script.Myanmar ||
					script == Script.Sinhala ||
					script == Script.Tibetan)
				{
					return Script.Latin;
				}
				else
					return script;
			}
			else
				return script;
        }

		public static WindowsVersion WindowsVersion
		{
			get
			{
				if (windowsVersion == WindowsVersion.Unknown)
					windowsVersion = GetWindowsVersion();

				return windowsVersion;
			}
		}
		private static WindowsVersion windowsVersion;


		private static WindowsVersion GetWindowsVersion()
		{
			WindowsVersion wv = WindowsVersion.Unknown;
			Version osv = System.Environment.OSVersion.Version;
			if (osv.Major < 5)
				wv = WindowsVersion.PreXP;
			else if (osv.Major == 5)
			{
				if (osv.Minor == 0) // Windows 2000 is 5.0
					wv = WindowsVersion.PreXP;
				else // XP is 5.1, Windows Server 2003 is 5.2
					wv = WindowsVersion.XP;
			}
			else if (osv.Major == 6)
			{
				if (osv.Minor == 0)
					wv = WindowsVersion.Vista; // Vista and Windows Server 2008 are 6.0
				else
					wv = WindowsVersion.PostVista;
			}
			else if (osv.Major > 6)
				wv = WindowsVersion.PostVista;

			return wv;
		}
    }
}
 