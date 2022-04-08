using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
    [Serializable()]
    public class DivTag
    {
        public DivTag(string id, string heading)
        {
            this.id = id;
            this.heading = heading;
        }

        public override string ToString()
        {
            return ScriptConverter.Convert(
                Heading,
                Script.Devanagari,
                bookScript,
                true);
        }
        public string Id
        {
            get { return id; }
            set { id = value; }
        }
        private string id;

        public string Heading
        {
            get { return heading; }
            set { heading = value; }
        }
        private string heading;

		public Script BookScript
		{
			get { return bookScript; }
			set { bookScript = value; }
		}
		private Script bookScript;
    }
}
