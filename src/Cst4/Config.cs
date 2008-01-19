using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Windows.Forms;

using CST.Conversion;

namespace CST
{
	[Serializable()]
	public class Config
	{
		public Config()
		{
		}

		static Config()
		{
		}

		public static Config Inst
		{
			get
			{
				if (config == null)
					config = new Config();

				return config;
			}
		}

		private static Config config;

		public string IndexDirectory
		{
			get
			{
				if (indexDirectory == null || indexDirectory.Length == 0)
					indexDirectory = "Index";

				return indexDirectory;
			}
			set { indexDirectory = value; }
		}
		private string indexDirectory;

		public string XmlDirectory
		{
			get
			{
				if (xmlDirectory == null || xmlDirectory.Length == 0)
					xmlDirectory = "Xml";
				return xmlDirectory;
			}
			set { xmlDirectory = value; }
		}
		private string xmlDirectory;

		public string XslDirectory
		{
			get
			{
				if (xslDirectory == null || xslDirectory.Length == 0)
					xslDirectory = "Xsl";

				return xslDirectory;
			}
			set { xslDirectory = value; }
		}
		private string xslDirectory;

		public string ReferenceDirectory
		{
			get
			{
				if (refDirectory == null || refDirectory.Length == 0)
					refDirectory = "Reference";
				return refDirectory;
			}
			set { refDirectory = value; }
		}
		private string refDirectory;

		public string EnglishDictionaryDirectory
		{
			get
			{
				return "en";
			}
		}

		public string HindiDictionaryDirectory
		{
			get
			{
				return "hi";
			}
		}
		
		public string PaliEnglishDictionaryFile
		{
			get
			{
				return "pali-english-dictionary.txt";
			}
		}

		public string PaliHindiDictionaryFile
		{
			get
			{
				return "pali-hindi-dictionary.txt";
			}
		}
	}
}
