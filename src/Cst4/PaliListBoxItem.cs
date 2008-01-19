using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
    public class PaliListBoxItem
    {
        public override string ToString()
        {
            return ScriptConverter.Convert(Str, SourceScript, AppState.Inst.CurrentScript);
        }

        public string Str
        {
            get { return str; }
            set { str = value; }
        }
        private string str;

        public Script SourceScript
        {
            get { return sourceScript; }
            set { sourceScript = value; }
        }
        private Script sourceScript;
    }
}
