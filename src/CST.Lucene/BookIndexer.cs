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

        public string XmlDirectory { get; set; } = string.Empty;

        public string IndexDirectory { get; set; } = string.Empty;

        public IndexWriter IndexWriter
        {
            get { return indexWriter; }
        }
        private IndexWriter indexWriter = null!;
        // The FSDirectory backing indexWriter. IndexWriter does not own/dispose the directory it's given,
        // so we hold and dispose it ourselves in CloseIndexWriter (was leaked per refresh). (SRCH-4)
        private FSDirectory? indexDirectory;

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
            try
            {
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

                // If every book already has a docId AND nothing changed, the index is complete — index
                // nothing (but still close the writer below so the write.lock is released — an early
                // return here used to leak the writer). Must also require no changedFiles: an
                // already-indexed book can still be a *changed* book that needs re-indexing, so relying
                // on `allIndexed` alone would skip incremental updates. (#61)
                bool nothingToDo = allIndexed && changedFiles.Count == 0;

                if (!nothingToDo)
                {
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
                }

                msgCallback("Optimizing search index.");
                CloseIndexWriter(commit: true);   // commit + dispose (releases the write.lock)
            }
            catch
            {
                // Release the write.lock and discard the partial index so in-session retries don't hit
                // LockObtainFailedException and a failed run never leaves a half-built index committed. (SRCH-4)
                CloseIndexWriter(commit: false);
                throw;
            }

            msgCallback("Checking search index.");
            GetAllDocIds(msgCallback);
        }

        private void GetAllDocIds(IndxMsgDelegate msgCallback)
        {
            // try/finally so the FSDirectory (and reader) are disposed on every path, including the
            // early returns below (previously leaked one FSDirectory per call). (SRCH-4)
            FSDirectory fSDirectory = FSDirectory.Open(IndexDirectory);
            try
            {
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
                try
                {
                    // Lucene.NET 4.8 quirk: MultiFields.GetLiveDocs returns null when the segment(s) have NO
                    // deletions — meaning ALL docs are live, not "nothing to do". When it is non-null, IBits.Get(i)
                    // is TRUE for a *live* doc, so we skip only deleted docs. (Previously this returned on null and
                    // did `if (Get(i)) continue` — inverted — so it set no docIds on a clean index and mapped
                    // deleted docs on a dirty one. #61)
                    IBits liveDocs = MultiFields.GetLiveDocs(indexReader);

                    Books books = Books.Inst;

                    int j = 0;
                    for (int i = 0; i < indexReader.MaxDoc; i++)
                    {
                        if (liveDocs != null && !liveDocs.Get(i))
                            continue; // skip deleted docs

                        Document doc = indexReader.Document(i);
                        books.SetDocId(doc.Get("file"), i);

                        msgCallback("Checking search index. (Book " + j + " of " + books.Count + ")");

                        j++;
                    }
                }
                finally
                {
                    indexReader.Dispose();
                }
            }
            finally
            {
                fSDirectory.Dispose();
            }
        }

        private void OpenIndexWriter(bool create)
        {
            if (indexWriter == null)
            {
                IndexWriterConfig config = new IndexWriterConfig(LuceneVersion.LUCENE_48,
                    new DevaXmlAnalyzer(LuceneVersion.LUCENE_48));
                config.UseCompoundFile = true;
                // Honor 'create': a fresh directory starts a new index; an existing one is appended to.
                config.OpenMode = create ? OpenMode.CREATE : OpenMode.CREATE_OR_APPEND;
                indexDirectory = FSDirectory.Open(IndexDirectory);
                indexWriter = new IndexWriter(indexDirectory, config);
            }
        }

        // Always releases the write.lock and disposes the FSDirectory, even on failure. commit=true
        // commits then disposes the writer; commit=false rolls back (discards uncommitted changes and
        // closes) so a failed indexing run never persists a partial index. (SRCH-4)
        private void CloseIndexWriter(bool commit)
        {
            var writer = indexWriter;
            indexWriter = null!;
            try
            {
                if (writer != null)
                {
                    if (commit)
                    {
                        writer.Commit();
                        writer.Dispose(true);
                    }
                    else
                    {
                        writer.Rollback();
                    }
                }
            }
            finally
            {
                indexDirectory?.Dispose();
                indexDirectory = null;
            }
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
