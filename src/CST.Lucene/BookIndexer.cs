using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using CST.Collections;

namespace CST
{
    public delegate void IndxMsgDelegate(string message);

    public class BookIndexer
    {
        public BookIndexer()
        {
        }

        public string XmlDirectory
        {
            get { return xmlDirectory; }
            set { xmlDirectory = value; }
        }
        private string xmlDirectory;

        public string IndexDirectory
        {
            get { return indexDirectory; }
            set { indexDirectory = value; }
        }
        private string indexDirectory;

        public IndexModifier IndexModifier
        {
            get { return indexModifier; }
        }
        private IndexModifier indexModifier;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="deleteIndexFirst"></param>
        public void IndexAll(IndxMsgDelegate msgCallback, List<int> changedFiles)
        {
            // we need the doc ids to know if all the books are in the index
            if (IndexReader.IndexExists(IndexDirectory))
            {
                msgCallback("Checking search index.");
                GetAllDocIds(msgCallback);
            }

            DirectoryInfo di = new DirectoryInfo(IndexDirectory);
            OpenIndexModifier(!di.Exists);
            Books books = Books.Inst;

            if (changedFiles.Count > 0)
            {
                foreach (int changedFile in changedFiles)
                {
                    DeleteBook(books[changedFile]);
                }
            }

            bool allIndexed = true;
            foreach (Book book in books)
            {
                if (book.DocId < 0)
                {
                    allIndexed = false;
                    break;
                }
            }

            // if all the books have doc IDs in the search index, we're done
            if (allIndexed)
                return;

            int i = 1;
            foreach (Book book in books)
            {
                if (book.DocId < 0)
                {
                    msgCallback("Building search index. (Book " + i + " of " + books.Count + ")");
                    IndexBook(book);
                }

                i++;
            }

            msgCallback("Optimizing search index.");
            CloseIndexModifier();

            msgCallback("Checking search index.");
            GetAllDocIds(msgCallback);
        }

        private void GetAllDocIds(IndxMsgDelegate msgCallback)
        {
            IndexReader indexReader = IndexReader.Open(IndexDirectory);

            Books books = Books.Inst;

            int j = 0;
            for (int i = 0; i < indexReader.MaxDoc(); i++)
            {
                if (indexReader.IsDeleted(i))
                    continue;

                Document doc = indexReader.Document(i);
                books.SetDocId(doc.Get("file"), i);

                msgCallback("Checking search index. (Book " + j + " of " + books.Count + ")");

                j++;
            }

            indexReader.Close();
        }

        private void OpenIndexModifier(bool create)
        {
            if (indexModifier == null)
            {
                indexModifier = new IndexModifier(IndexDirectory, new DevaXmlAnalyzer(), create);
                indexModifier.SetUseCompoundFile(true);
                indexModifier.SetMaxFieldLength(Int32.MaxValue);
            }
        }

        private void CloseIndexModifier()
        {
            indexModifier.Optimize();
            indexModifier.Close();
        }

        private void IndexBook(Book book)
        {
            // delete document from the search index if it's already in the index
            if (book.DocId >= 0)
                indexModifier.DeleteDocument(book.DocId);

            // read text of document
            StreamReader sr = new StreamReader(XmlDirectory + Path.DirectorySeparatorChar + book.FileName);
            string deva = sr.ReadToEnd();
            sr.Close();

            // setup Document object, storing text and some additional fields
            Document doc = new Document();
            // changed this from Store.YES to Store.NO on 2 Sept 07. We'll see if it breaks anything.
            doc.Add(new Field("text", deva, Field.Store.NO, Field.Index.TOKENIZED, Field.TermVector.WITH_POSITIONS_OFFSETS));
            doc.Add(new Field("file", book.FileName, Field.Store.YES, Field.Index.NO));
            doc.Add(new Field("matn", book.MatnField, Field.Store.YES, Field.Index.NO));
            doc.Add(new Field("pitaka", book.PitakaField, Field.Store.YES, Field.Index.NO));
            // can you return results in numerically sorted order?
            //doc.Add(new Field("index", book.Index.ToString(), Field.Store.YES, Field.Index.NO));

            // add document to index
            indexModifier.AddDocument(doc);
        }

        private void DeleteBook(Book book)
        {
            if (book != null && book.DocId >= 0)
            {
                indexModifier.DeleteDocument(book.DocId);
                book.DocId = -1;
            }
        }
    }
}
