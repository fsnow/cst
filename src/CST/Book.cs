using System;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	[Serializable()]
    public class Book
    {
        public Book()
        {
            DocId = -1;
			BookType = BookType.Unknown;
            MulaIndex = -1;
            AtthakathaIndex = -1;
            TikaIndex = -1;
        }

        public string FileName
        {
            get { return fileName; }
            set { fileName = value; }
        }
        private string fileName;

        public string LongNavPath
        {
            get { return longNavPath; }
            set { longNavPath = value; }
        }
        private string longNavPath;

        public string ShortNavPath
        {
            get { return shortNavPath; }
            set { shortNavPath = value; }
        }
        private string shortNavPath;

        /// <summary>
        /// "sut", "abh", "vin" or "other"
        /// </summary>
        public string PitakaField
        {
            get
            {
                if (pitaka == Pitaka.Sutta)
                    return "sut";
                else if (pitaka == Pitaka.Vinaya)
                    return "vin";
                else if (pitaka == Pitaka.Abhidhamma)
                    return "abh";
                else
                    return "other";
            }
        }
        public Pitaka Pitaka
        {
            get { return pitaka; }
            set { pitaka = value; }
        }
        private Pitaka pitaka;
    
        /// <summary>
        /// "mul", "att", "tik" or "other"
        /// </summary>
        public string MatnField
        {
            get
            {
                if (matn == CommentaryLevel.Mula)
                    return "mul";
                else if (matn == CommentaryLevel.Atthakatha)
                    return "att";
                else if (matn == CommentaryLevel.Tika)
                    return "tik";
                else 
                    return "other";
            }
        }
        public CommentaryLevel Matn
        {
            get { return matn; }
            set { matn = value; }
        }
        private CommentaryLevel matn;

        public int Index
        {
            get { return index; }
            set { index = value; }
        }
        private int index;

        public BookType BookType
        {
            get { return bookType; }
            set { bookType = value; }
        }
        private BookType bookType;

        public int MulaIndex
        {
            get { return mulaIndex; }
            set { mulaIndex = value; }
        }
        private int mulaIndex;

        public int AtthakathaIndex
        {
            get { return atthaIndex; }
            set { atthaIndex = value; }
        }
        private int atthaIndex;

        public int TikaIndex
        {
            get { return tikaIndex; }
            set { tikaIndex = value; }
        }
        private int tikaIndex;

        public int DocId
        {
            get { return docId; }
            set { docId = value; }
        }
        private int docId;

        public string ChapterListTypes
        {
            get { return chapterListTypes; }
            set { chapterListTypes = value; }
        }
        private string chapterListTypes;
    }
}
