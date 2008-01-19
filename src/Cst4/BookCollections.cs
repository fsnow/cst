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
    public class BookCollections
    {
        public BookCollections()
        {
        }

		static BookCollections()
        {
            fileName = "book-colls.dat";
        }

		public static BookCollections Inst
        {
			get
			{
				if (bookColls == null)
					bookColls = new BookCollections();

				return bookColls;
			}
        }

		private static BookCollections bookColls;
        private static string fileName;


        public Dictionary<string, BookCollection> Colls
        {
            get
			{
				if (bookCollections == null)
					bookCollections = new Dictionary<string, BookCollection>();

				return bookCollections;
			}
            set { bookCollections = value; }
        }
        private Dictionary<string, BookCollection> bookCollections;

        public static void Serialize()
        {
            FileStream fs = new FileStream(fileName, FileMode.Create);

            BinaryFormatter formatter = new BinaryFormatter();
            try
            {
                formatter.Serialize(fs, bookColls);
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
                bookColls = (BookCollections)formatter.Deserialize(fs);
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
