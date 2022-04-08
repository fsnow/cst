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

            faces[Script.Cyrillic] = "Doulos SIL";
            controlFontSizes[Script.Cyrillic] = 10.0f;

            faces[Script.Devanagari] = "Kokila";
            faces[Script.Gujarati] = "Shruti";
            faces[Script.Gurmukhi] = "Raavi";
            faces[Script.Kannada] = "Tunga";

            faces[Script.Khmer] = "Khmer Mondulkiri U OT ls";
            controlFontSizes[Script.Khmer] = 15.0f;

            faces[Script.Latin] = "Microsoft Sans Serif";
            faces[Script.Malayalam] = "Kartika";
            faces[Script.Myanmar] = "Myanmar Text";
            faces[Script.Sinhala] = "Iskoola Pota";
            faces[Script.Telugu] = "Gautami";
            faces[Script.Thai] = "Leelawadee UI";
            faces[Script.Tibetan] = "Microsoft Himalaya";
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
    }
}
 