using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text.RegularExpressions;
using System.Xml;

namespace CST
{
    [Serializable()]
    public class ChapterLists
    {
        public ChapterLists()
        {
            chpListArray = new List<DivTag>[Books.Inst.Count];
        }

        public List<DivTag>[] ChpListArray
        {
            get { return chpListArray; }
            set { chpListArray = value; }
        }
        private List<DivTag>[] chpListArray;



        static ChapterLists()
        {
            fileName = "chplists.dat";
        }

        public static ChapterLists Inst
        {
			get
			{
				if (chapterLists == null)
					chapterLists = new ChapterLists();

				// if books are added, we have to expand the FileDates array.
				// The deserialized version is too short for the new books.
				if (chapterLists.ChpListArray.Length < Books.Inst.Count)
				{
					List<DivTag>[] temp = new List<DivTag>[Books.Inst.Count];
					chapterLists.ChpListArray.CopyTo(temp, 0);
					chapterLists.ChpListArray = temp;
				}

				return chapterLists;
			}
        }

        public List<DivTag> this[int index]
        {
            get
            {
                return ChpListArray[index];
            }
        }

        public static void Generate(List<int> changedFiles)
        {
            Books books = Books.Inst;
            ChapterLists chapterLists = ChapterLists.Inst;

            foreach (int index in changedFiles)
            {
                Book book = books[index];
                Dictionary<string, int> chapterListTypes = new Dictionary<string, int>();
                if (book.ChapterListTypes != null && book.ChapterListTypes.Length > 0)
                {
                    int i = 0;
                    foreach (string type in book.ChapterListTypes.Split(new char[] { ',' }))
                    {
                        chapterListTypes[type.Trim()] = i;
                        i++;
                    }
                }

                string bookPath = Config.Inst.XmlDirectory + Path.DirectorySeparatorChar + book.FileName;

                StreamReader sr = new StreamReader(bookPath);
                string devXml = sr.ReadToEnd();
                sr.Close();

                XmlDocument xml = new XmlDocument();
                xml.LoadXml(devXml);

                List<DivTag> divTags = null;

                XmlNodeList divNodes = xml.GetElementsByTagName("div");
                foreach (XmlNode divNode in divNodes)
                {
                    if (divNode.Attributes["type"] == null)
                        continue;

                    string type = divNode.Attributes["type"].Value;
                    if (chapterListTypes.ContainsKey(type))
                    {
                        if (divTags == null)
                            divTags = new List<DivTag>();

                        string id = "";
                        if (divNode.Attributes["id"] != null)
                            id = divNode.Attributes["id"].Value;

                        string heading = "";
                        XmlNode headNode = divNode.SelectSingleNode("head");
						if (headNode != null)
							heading = headNode.InnerXml;

						// remove footnotes from chapter list headings
						heading = Regex.Replace(heading, "<note>(.+?)</note>", "");
						heading = heading.Trim();

                        if (id.Length > 0 && heading.Length > 0)
                        {
                            int indentLevel = CountUnderscores(id);
                            divTags.Add(new DivTag(id, "".PadRight(indentLevel * 3) + heading));
                        }
                    }
                }

                if (divTags != null && divTags.Count > 0)
                    chapterLists.ChpListArray[book.Index] = divTags;
            }
        }

        private static int CountUnderscores(string str)
        {
            int count = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (str[i] == '_')
                    count++;
            }

            return count;
        }

        private static ChapterLists chapterLists;
        private static string fileName;

        public static void Serialize()
        {
            FileStream fs = new FileStream(fileName, FileMode.Create);

            BinaryFormatter formatter = new BinaryFormatter();
            try
            {
                formatter.Serialize(fs, chapterLists);
            }
            catch (SerializationException e)
            {
                Console.WriteLine("Failed to serialize. Reason: " + e.Message);
                throw;
            }
            finally
            {
                fs.Close();
            }
        }

        public static void Deserialize()
        {
            if (File.Exists(fileName) == false)
                return;

            FileInfo fi = new FileInfo(fileName);
            if (fi.Length == 0)
            {
                File.Delete(fileName);
                return;
            }

            FileStream fs = new FileStream(fileName, FileMode.Open);
            try
            {
                BinaryFormatter formatter = new BinaryFormatter();
                chapterLists = (ChapterLists)formatter.Deserialize(fs);
            }
            catch (SerializationException e)
            {
                Console.WriteLine("Failed to deserialize. Reason: " + e.Message);
                throw;
            }
            finally
            {
                fs.Close();
            }
        }
    }
}
