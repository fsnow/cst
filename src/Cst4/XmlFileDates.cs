using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace CST
{
    [Serializable()]
    public class XmlFileDates
    {
        public XmlFileDates()
        {
            fileDates = new DateTime[Books.Inst.Count];
        }

        static XmlFileDates()
        {
            fileName = "xml.dat";
        }

        public static XmlFileDates Inst
        {
			get
			{
				if (xmlFileDates == null)
					xmlFileDates = new XmlFileDates();

				// If books are added, we have to expand the FileDates array.
				// The deserialized version is too short for the new books.
				if (xmlFileDates.FileDates.Length < Books.Inst.Count)
				{
					DateTime[] temp = new DateTime[Books.Inst.Count];
					xmlFileDates.FileDates.CopyTo(temp, 0);
					xmlFileDates.FileDates = temp;
				}

				return xmlFileDates;
			}
        }

        private static XmlFileDates xmlFileDates;
        private static string fileName;

        public DateTime[] FileDates
        {
            get { return fileDates; }
            set { fileDates = value; }
        }
        private DateTime[] fileDates;


        public static void Serialize()
        {
            FileStream fs = new FileStream(fileName, FileMode.Create);

            BinaryFormatter formatter = new BinaryFormatter();
            try
            {
                formatter.Serialize(fs, xmlFileDates);
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
                xmlFileDates = (XmlFileDates)formatter.Deserialize(fs);
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
