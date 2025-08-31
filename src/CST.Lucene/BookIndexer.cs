using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;
using CST;

namespace CST
{
    public delegate void IndxMsgDelegate(string message);

    public class BookIndexer
    {
        public BookIndexer()
        {
        }

        public string XmlDirectory { get; set; }

        public string IndexDirectory { get; set; }

        public IndexWriter IndexWriter
        {
            get { return indexWriter; }
        }
        private IndexWriter indexWriter;

        /// <summary>activ
        /// 
        /// </summary>
        /// <param name="deleteIndexFirst"></param>
        public void IndexAll(IndxMsgDelegate msgCallback, List<int> changedFiles)
        {
            // we need the doc ids to know if all the books are in the index
            // FSnow 2020-04-19 IndexExists method not in 4.8
            //if (IndexReader.IndexExists(IndexDirectory))
            //{

            DirectoryInfo di = new DirectoryInfo(IndexDirectory);
            if (di.Exists && di.GetFiles().Length > 0)
            {
                msgCallback("Checking search index.");
                GetAllDocIds(msgCallback);
            }
            
            OpenIndexWriter(!di.Exists);
            Books books = Books.Inst;

            // IndexBook() will handle deletion of existing documents, so no need to delete here
            indexWriter.Flush(triggerMerge: true, applyAllDeletes: true);

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

            // Only index the changed files, not all books with DocId < 0
            if (changedFiles.Count > 0)
            {
                int i = 1;
                foreach (int changedFileIndex in changedFiles)
                {
                    Book book = books[changedFileIndex];
                    msgCallback("Building search index. (Book " + i + " of " + changedFiles.Count + ")");
                    IndexBook(book);
                    i++;
                }
            }
            else
            {
                // Initial indexing - process all books with no DocId
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
            }

            msgCallback("Optimizing search index.");
            CloseIndexWriter();

            msgCallback("Checking search index.");
            GetAllDocIds(msgCallback);
        }

        private void GetAllDocIds(IndxMsgDelegate msgCallback)
        {
            FSDirectory fSDirectory = FSDirectory.Open(IndexDirectory);
            
            // Check if a valid index exists before trying to open it
            try
            {
                if (!DirectoryReader.IndexExists(fSDirectory))
                {
                    // No index exists yet, nothing to get
                    return;
                }
            }
            catch
            {
                // Directory might exist but contain no valid index
                return;
            }
            
            IndexReader indexReader = DirectoryReader.Open(fSDirectory);

            IBits liveDocs = MultiFields.GetLiveDocs(indexReader);
            if (liveDocs == null)
            {
                return;
            }

            Books books = Books.Inst;

            int j = 0;
            for (int i = 0; i < indexReader.MaxDoc; i++)
            {
                if (liveDocs.Get(i))
                    continue;

                Document doc = indexReader.Document(i);
                books.SetDocId(doc.Get("file"), i);

                msgCallback("Checking search index. (Book " + j + " of " + books.Count + ")");

                j++;
            }
            indexReader.Dispose();
        }

        private void OpenIndexWriter(bool create)
        {
            if (indexWriter == null)
            {
                
                IndexWriterConfig config = new IndexWriterConfig(LuceneVersion.LUCENE_48,
                    new DevaXmlAnalyzer(LuceneVersion.LUCENE_48));
                config.UseCompoundFile = true;
                config.OpenMode = OpenMode.CREATE_OR_APPEND;
                indexWriter = new IndexWriter(FSDirectory.Open(IndexDirectory), config);
            }
        }

        private void CloseIndexWriter()
        {
            indexWriter.Commit();
            indexWriter.Dispose(true);
        }

        private void IndexBook(Book book)
        {
            // Always delete any existing document with this filename before adding the new version
            // This handles both initial indexing (where deletion finds nothing) and incremental updates
            indexWriter.DeleteDocuments(new Term("file", book.FileName));

            // read text of document
            StreamReader sr = new StreamReader(XmlDirectory + Path.DirectorySeparatorChar + book.FileName);
            string deva = sr.ReadToEnd();
            sr.Close();

            FieldType ft = new FieldType(TextField.TYPE_NOT_STORED);
            ft.IsIndexed = true;
            ft.IsStored = false;
            ft.IsTokenized = true;
            ft.OmitNorms = false;
            ft.StoreTermVectors = true;
            ft.StoreTermVectorOffsets = true;
            ft.StoreTermVectorPayloads = true;
            ft.StoreTermVectorPositions = true;
            ft.IndexOptions = IndexOptions.DOCS_AND_FREQS_AND_POSITIONS_AND_OFFSETS;
            ft.Freeze();

            // setup Document object, storing text and some additional fields
            Document doc = new Document
            {
                // Use StringField for "file" so it's indexed and can be used for deletion
                new StringField("file", book.FileName, Field.Store.YES),
                new StoredField("matn", book.MatnField),
                new StoredField("pitaka", book.PitakaField),
                new Field("text", deva, ft)
            };

            // can you return results in numerically sorted order?
            // new Field("index", book.Index.ToString(), Field.Store.YES, Field.Index.NO));

            // add document to index
            indexWriter.AddDocument(doc);
            indexWriter.Flush(triggerMerge: true, applyAllDeletes: true);
        }

    }
}
