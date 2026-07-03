using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace CST
{
	public class Books : IEnumerable<Book>
	{
		// Thread-safe lazy init: background indexing (BookIndexer on Task.Run) can race UI startup
		// restore for the first access. An unlocked "if (books == null) books = new Books()" could
		// construct two instances, so DocIds written to one wouldn't be visible on the other. (CORE-1)
		private static readonly Lazy<Books> instance = new(() => new Books());

		public static Books Inst => instance.Value;


		public Books()
		{
			bookList = new List<Book>();
			booksByFile = new Dictionary<string, Book>();
			booksByDocId = new Dictionary<int, Book>();

			PopulateBookList();
			CalculateBitArrays();
		}

		private List<Book> bookList;
		private Dictionary<string, Book> booksByFile;
		private Dictionary<int, Book> booksByDocId;

		// Guards booksByDocId (and the Book.DocId it indexes). The search path re-syncs DocIds and the
		// indexer writes them, both off the UI thread; concurrent Dictionary writes are undefined
		// behavior in .NET. (CORE-1)
		private readonly object docIdLock = new();

		public void SetDocId(string file, int docId)
		{
			lock (docIdLock)
			{
				Book book = booksByFile[file];
				book.DocId = docId;
				booksByDocId[book.DocId] = book;
			}
		}

		public Book FromDocId(int docId)
		{
			lock (docIdLock)
			{
				return booksByDocId[docId];
			}
		}

		public IEnumerator<Book> GetEnumerator()
		{
			foreach (Book b in bookList)
			{
				yield return b;
			}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		private void PopulateBookList()
		{
			Book book;

			book = new Book();
			book.Index = 0;
			book.FileName = "s0101m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/दीघ निकाय/सीलक्खन्धवग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/सीलक्खन्धवग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 61;
			book.TikaIndex = 108;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 1;
			book.FileName = "s0102m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/दीघ निकाय/महावग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/महावग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 62;
			book.TikaIndex = 109;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 2;
			book.FileName = "s0103m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/दीघ निकाय/पाथिकवग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/पाथिकवग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 63;
			book.TikaIndex = 110;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 3;
			book.FileName = "s0201m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/मज्झिम निकाय/मूलपण्णासपाळि";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/मूलपण्णासपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 64;
			book.TikaIndex = 113;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 4;
			book.FileName = "s0202m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/मज्झिम निकाय/मज्झिमपण्णासपाळि";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/मज्झिमपण्णासपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 65;
			book.TikaIndex = 114;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 5;
			book.FileName = "s0203m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/मज्झिम निकाय/उपरिपण्णासपाळि";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/उपरिपण्णासपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 66;
			book.TikaIndex = 115;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 6;
			book.FileName = "s0301m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/संयुत्त निकाय/सगाथावग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/सगाथावग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 67;
			book.TikaIndex = 116;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 7;
			book.FileName = "s0302m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/संयुत्त निकाय/निदानवग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/निदानवग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 68;
			book.TikaIndex = 117;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 8;
			book.FileName = "s0303m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/संयुत्त निकाय/खन्धवग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/खन्धवग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 69;
			book.TikaIndex = 118;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 9;
			book.FileName = "s0304m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/संयुत्त निकाय/सळायतनवग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/सळायतनवग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 70;
			book.TikaIndex = 119;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 10;
			book.FileName = "s0305m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/संयुत्त निकाय/महावग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/महावग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 71;
			book.TikaIndex = 120;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 11;
			book.FileName = "s0401m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/एककनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/एककनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 72;
			book.TikaIndex = 121;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 12;
			book.FileName = "s0402m1.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/दुकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/दुकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 73;
			book.TikaIndex = 122;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 13;
			book.FileName = "s0402m2.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/तिकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/तिकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 73;
			book.TikaIndex = 122;
			book.ChapterListTypes = "book,pannasaka,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 14;
			book.FileName = "s0402m3.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/चतुक्कनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/चतुक्कनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 73;
			book.TikaIndex = 122;
			book.ChapterListTypes = "book,pannasaka,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 15;
			book.FileName = "s0403m1.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/पञ्चकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/पञ्चकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 74;
			book.TikaIndex = 123;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 16;
			book.FileName = "s0403m2.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/छक्कनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/छक्कनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 74;
			book.TikaIndex = 123;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 17;
			book.FileName = "s0403m3.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/सत्तकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/सत्तकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 74;
			book.TikaIndex = 123;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 18;
			book.FileName = "s0404m1.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/अट्ठकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/अट्ठकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 75;
			book.TikaIndex = 124;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 19;
			book.FileName = "s0404m2.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/नवकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/नवकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 75;
			book.TikaIndex = 124;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 20;
			book.FileName = "s0404m3.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/दसकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/दसकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 75;
			book.TikaIndex = 124;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 21;
			book.FileName = "s0404m4.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/अङ्गुत्तर निकाय/एकादसकनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/एकादसकनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 75;
			book.TikaIndex = 124;
			book.ChapterListTypes = "book,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 22;
			book.FileName = "s0501m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/खुद्दकपाठपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/खुद्दकपाठपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 76;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 23;
			book.FileName = "s0502m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/धम्मपदपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/धम्मपदपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 77;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 24;
			book.FileName = "s0503m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/उदानपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/उदानपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 78;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 25;
			book.FileName = "s0504m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/इतिवुत्तकपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/इतिवुत्तकपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 79;
			book.ChapterListTypes = "book,nipata";
			bookList.Add(book);

			book = new Book();
			book.Index = 26;
			book.FileName = "s0505m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/सुत्तनिपातपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/सुत्तनिपातपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 80;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 27;
			book.FileName = "s0506m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/विमानवत्थुपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/विमानवत्थुपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 81;
			book.ChapterListTypes = "book,vimana";
			bookList.Add(book);

			book = new Book();
			book.Index = 28;
			book.FileName = "s0507m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/पेतवत्थुपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/पेतवत्थुपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 82;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 29;
			book.FileName = "s0508m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/थेरगाथापाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/थेरगाथापाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 83; // 83 + 84
			book.ChapterListTypes = "book,nipata";
			bookList.Add(book);

			book = new Book();
			book.Index = 30;
			book.FileName = "s0509m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/थेरीगाथापाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/थेरीगाथापाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 85;
			book.ChapterListTypes = "book,nipata";
			bookList.Add(book);

			book = new Book();
			book.Index = 31;
			book.FileName = "s0510m1.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/अपदानपाळि-१";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/अपदानपाळि-१";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.AtthakathaIndex = 86;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 32;
			book.FileName = "s0510m2.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/अपदानपाळि-२";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/अपदानपाळि-२";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.AtthakathaIndex = 86;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 33;
			book.FileName = "s0511m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/बुद्धवंसपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/बुद्धवंसपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 87;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 34;
			book.FileName = "s0512m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/चरियापिटकपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/चरियापिटकपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 88;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 35;
			book.FileName = "s0513m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/जातकपाळि-१";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातकपाळि-१";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 89; // 89 + 90 + 91 + 92
			book.ChapterListTypes = "book,nipata";
			bookList.Add(book);

			book = new Book();
			book.Index = 36;
			book.FileName = "s0514m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/जातकपाळि-२";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातकपाळि-२";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 93; // 93 + 94 + 95
			book.ChapterListTypes = "book,nipata";
			bookList.Add(book);

			book = new Book();
			book.Index = 37;
			book.FileName = "s0515m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/महानिद्देसपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/महानिद्देसपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 96;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 38;
			book.FileName = "s0516m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/चूळनिद्देसपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/चूळनिद्देसपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 97;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 39;
			book.FileName = "s0517m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/पटिसम्भिदामग्गपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/पटिसम्भिदामग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 98;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 40;
			book.FileName = "s0519m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/नेत्तिप्पकरणपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/नेत्तिप्पकरणपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 99;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 41;
			book.FileName = "s0518m.nrf.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/मिलिन्दपञ्हपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/मिलिन्दपञ्हपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			// unlinked
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 42;
			book.FileName = "s0520m.nrf.xml";
			book.LongNavPath = "तिपिटक (मूल)/सुत्त पिटक/खुद्दक निकाय/पेटकोपदेसपाळि";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/पेटकोपदेसपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Sutta;
			// unlinked
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 43;
			book.FileName = "vin01m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/विनय पिटक/पाराजिकपाळि";
			book.ShortNavPath = "वि॰ पि॰/पाराजिकपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 100;
			book.TikaIndex = 127; // 127 + 128
			book.ChapterListTypes = "book,kanda";
			bookList.Add(book);

			book = new Book();
			book.Index = 44;
			book.FileName = "vin02m1.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/विनय पिटक/पाचित्तियपाळि";
			book.ShortNavPath = "वि॰ पि॰/पाचित्तियपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 101;
			book.TikaIndex = 129;
			book.ChapterListTypes = "book,subbook,kanda";
			bookList.Add(book);

			book = new Book();
			book.Index = 45;
			book.FileName = "vin02m2.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/विनय पिटक/महावग्गपाळि";
			book.ShortNavPath = "वि॰ पि॰/महावग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 102;
			book.TikaIndex = 129;
			book.ChapterListTypes = "book,khandaka";
			bookList.Add(book);

			book = new Book();
			book.Index = 46;
			book.FileName = "vin02m3.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/विनय पिटक/चूळवग्गपाळि";
			book.ShortNavPath = "वि॰ पि॰/चूळवग्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 103;
			book.TikaIndex = 129;
			book.ChapterListTypes = "book,khandaka";
			bookList.Add(book);

			book = new Book();
			book.Index = 47;
			book.FileName = "vin02m4.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/विनय पिटक/परिवारपाळि";
			book.ShortNavPath = "वि॰ पि॰/परिवारपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.AtthakathaIndex = 104;
			book.TikaIndex = 129;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 48;
			book.FileName = "abh01m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/धम्मसङ्गणीपाळि";
			book.ShortNavPath = "अभि॰ पि॰/धम्मसङ्गणीपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 105;
			book.ChapterListTypes = "book,chapter";
			bookList.Add(book);

			book = new Book();
			book.Index = 49;
			book.FileName = "abh02m.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/विभङ्गपाळि";
			book.ShortNavPath = "अभि॰ पि॰/विभङ्गपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 106;
			book.TikaIndex = 141;
			bookList.Add(book);

			book = new Book();
			book.Index = 50;
			book.FileName = "abh03m1.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/धातुकथापाळि";
			book.ShortNavPath = "अभि॰ पि॰/धातुकथापाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 51;
			book.FileName = "abh03m2.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/पुग्गलपञ्ञत्तिपाळि";
			book.ShortNavPath = "अभि॰ पि॰/पुग्गलपञ्ञत्तिपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 52;
			book.FileName = "abh03m3.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/कथावत्थुपाळि";
			book.ShortNavPath = "अभि॰ पि॰/कथावत्थुपाळि";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = -1;  // TODO: real ṭīkā linking here is complex (many-to-many); 99999 was a placeholder that left the ṭīkā button enabled-but-dead (HasTika is TikaIndex >= 0). -1 disables it until the linking is revisited. (CORE-2)
			bookList.Add(book);

			book = new Book();
			book.Index = 53;
			book.FileName = "abh03m4.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/यमकपाळि-१";
			book.ShortNavPath = "अभि॰ पि॰/यमकपाळि-१";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 54;
			book.FileName = "abh03m5.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/यमकपाळि-२";
			book.ShortNavPath = "अभि॰ पि॰/यमकपाळि-२";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 55;
			book.FileName = "abh03m6.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/यमकपाळि-३";
			book.ShortNavPath = "अभि॰ पि॰/यमकपाळि-३";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 56;
			book.FileName = "abh03m7.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/पट्ठानपाळि-१";
			book.ShortNavPath = "अभि॰ पि॰/पट्ठानपाळि-१";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = -1;  // TODO: real ṭīkā linking here is complex (many-to-many); 99999 was a placeholder that left the ṭīkā button enabled-but-dead (HasTika is TikaIndex >= 0). -1 disables it until the linking is revisited. (CORE-2)
			bookList.Add(book);

			book = new Book();
			book.Index = 57;
			book.FileName = "abh03m8.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/पट्ठानपाळि-२";
			book.ShortNavPath = "अभि॰ पि॰/पट्ठानपाळि-२";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 58;
			book.FileName = "abh03m9.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/पट्ठानपाळि-३";
			book.ShortNavPath = "अभि॰ पि॰/पट्ठानपाळि-३";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 59;
			book.FileName = "abh03m10.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/पट्ठानपाळि-४";
			book.ShortNavPath = "अभि॰ पि॰/पट्ठानपाळि-४";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = -1;  // TODO: real ṭīkā linking here is complex (many-to-many); 99999 was a placeholder that left the ṭīkā button enabled-but-dead (HasTika is TikaIndex >= 0). -1 disables it until the linking is revisited. (CORE-2)
			bookList.Add(book);

			book = new Book();
			book.Index = 60;
			book.FileName = "abh03m11.mul.xml";
			book.LongNavPath = "तिपिटक (मूल)/अभिधम्म पिटक/पट्ठानपाळि-५";
			book.ShortNavPath = "अभि॰ पि॰/पट्ठानपाळि-५";
			book.Matn = CommentaryLevel.Mula;
			book.Pitaka = Pitaka.Abhidhamma;
			book.AtthakathaIndex = 107;
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 61;
			book.FileName = "s0101a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/दीघ निकाय (अट्ठकथा)/सीलक्खन्धवग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/सीलक्खन्धवग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 0;
			book.TikaIndex = 108;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 62;
			book.FileName = "s0102a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/दीघ निकाय (अट्ठकथा)/महावग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/महावग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 1;
			book.TikaIndex = 109;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 63;
			book.FileName = "s0103a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/दीघ निकाय (अट्ठकथा)/पाथिकवग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/पाथिकवग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 2;
			book.TikaIndex = 110;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 64;
			book.FileName = "s0201a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/मज्झिम निकाय (अट्ठकथा)/मूलपण्णास-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/मूलपण्णास-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 3;
			book.TikaIndex = 113;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 65;
			book.FileName = "s0202a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/मज्झिम निकाय (अट्ठकथा)/मज्झिमपण्णास-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/मज्झिमपण्णास-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 4;
			book.TikaIndex = 114;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 66;
			book.FileName = "s0203a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/मज्झिम निकाय (अट्ठकथा)/उपरिपण्णास-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/उपरिपण्णास-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 5;
			book.TikaIndex = 115;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 67;
			book.FileName = "s0301a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/संयुत्त निकाय (अट्ठकथा)/सगाथावग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/सगाथावग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 6;
			book.TikaIndex = 116;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 68;
			book.FileName = "s0302a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/संयुत्त निकाय (अट्ठकथा)/निदानवग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/निदानवग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 7;
			book.TikaIndex = 117;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 69;
			book.FileName = "s0303a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/संयुत्त निकाय (अट्ठकथा)/खन्धवग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/खन्धवग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 8;
			book.TikaIndex = 118;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 70;
			book.FileName = "s0304a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/संयुत्त निकाय (अट्ठकथा)/सळायतनवग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/सळायतनवग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 9;
			book.TikaIndex = 119;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 71;
			book.FileName = "s0305a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/संयुत्त निकाय (अट्ठकथा)/महावग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/महावग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 10;
			book.TikaIndex = 120;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 72;
			book.FileName = "s0401a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/अङ्गुत्तर निकाय (अट्ठकथा)/एककनिपात-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/एककनिपात-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 11;
			book.TikaIndex = 121;
			book.ChapterListTypes = "book,intro,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 73;
			book.FileName = "s0402a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/अङ्गुत्तर निकाय (अट्ठकथा)/दुक-तिक-चतुक्कनिपात-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/दुक-तिक-चतुक्कनिपात-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Multi;
			book.MulaIndex = 12; // 12 + 13 + 14
			book.TikaIndex = 122;
			book.ChapterListTypes = "book,pannasaka,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 74;
			book.FileName = "s0403a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/अङ्गुत्तर निकाय (अट्ठकथा)/पञ्चक-छक्क-सत्तकनिपात-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/पञ्चक-छक्क-सत्तकनिपात-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Multi;
			book.MulaIndex = 15; // 15 + 16 + 17
			book.TikaIndex = 123;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 75;
			book.FileName = "s0404a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/अङ्गुत्तर निकाय (अट्ठकथा)/अट्ठकादिनिपात-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/अट्ठकादिनिपात-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Multi;
			book.MulaIndex = 18; // 18 + 19 + 20 + 21
			book.TikaIndex = 124;
			book.ChapterListTypes = "book,pannasaka,vagga,peyyala";
			bookList.Add(book);

			book = new Book();
			book.Index = 76;
			book.FileName = "s0501a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/खुद्दकपाठ-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/खुद्दकपाठ-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 22;
			bookList.Add(book);

			book = new Book();
			book.Index = 77;
			book.FileName = "s0502a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/धम्मपद-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/धम्मपद-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 23;
			bookList.Add(book);

			book = new Book();
			book.Index = 78;
			book.FileName = "s0503a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/उदान-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/उदान-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 24;
			bookList.Add(book);

			book = new Book();
			book.Index = 79;
			book.FileName = "s0504a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/इतिवुत्तक-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/इतिवुत्तक-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 25;
			bookList.Add(book);

			book = new Book();
			book.Index = 80;
			book.FileName = "s0505a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/सुत्तनिपात-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/सुत्तनिपात-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 26;
			bookList.Add(book);

			book = new Book();
			book.Index = 81;
			book.FileName = "s0506a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/विमानवत्थु-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/विमानवत्थु-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 27;
			bookList.Add(book);

			book = new Book();
			book.Index = 82;
			book.FileName = "s0507a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/पेतवत्थु-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/पेतवत्थु-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 28;
			bookList.Add(book);

			book = new Book();
			book.Index = 83;
			book.FileName = "s0508a1.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/थेरगाथा-अट्ठकथा-१";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/थेरगाथा-अट्ठकथा-१";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 29;
			bookList.Add(book);

			book = new Book();
			book.Index = 84;
			book.FileName = "s0508a2.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/थेरगाथा-अट्ठकथा-२";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/थेरगाथा-अट्ठकथा-२";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 29;
			bookList.Add(book);

			book = new Book();
			book.Index = 85;
			book.FileName = "s0509a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/थेरीगाथा-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/थेरीगाथा-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 30;
			bookList.Add(book);

			book = new Book();
			book.Index = 86;
			book.FileName = "s0510a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/अपदान-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/अपदान-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 31; // 31 + 32
			bookList.Add(book);

			book = new Book();
			book.Index = 87;
			book.FileName = "s0511a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/बुद्धवंस-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/बुद्धवंस-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 33;
			bookList.Add(book);

			book = new Book();
			book.Index = 88;
			book.FileName = "s0512a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/चरियापिटक-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/चरियापिटक-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 34;
			bookList.Add(book);

			book = new Book();
			book.Index = 89;
			book.FileName = "s0513a1.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-१";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-१";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 35;
			bookList.Add(book);

			book = new Book();
			book.Index = 90;
			book.FileName = "s0513a2.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-२";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-२";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 35;
			bookList.Add(book);

			book = new Book();
			book.Index = 91;
			book.FileName = "s0513a3.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-३";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-३";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 35;
			bookList.Add(book);


			book = new Book();
			book.Index = 92;
			book.FileName = "s0513a4.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-४";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-४";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 35;
			bookList.Add(book);

			book = new Book();
			book.Index = 93;
			book.FileName = "s0514a1.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-५";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-५";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 36;
			bookList.Add(book);

			book = new Book();
			book.Index = 94;
			book.FileName = "s0514a2.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-६";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-६";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 36;
			bookList.Add(book);

			book = new Book();
			book.Index = 95;
			book.FileName = "s0514a3.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/जातक-अट्ठकथा-७";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/जातक-अट्ठकथा-७";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Split;
			book.MulaIndex = 36;
			bookList.Add(book);

			book = new Book();
			book.Index = 96;
			book.FileName = "s0515a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/महानिद्देस-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/महानिद्देस-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 37;
			bookList.Add(book);

			book = new Book();
			book.Index = 97;
			book.FileName = "s0516a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/चूळनिद्देस-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/चूळनिद्देस-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 38;
			bookList.Add(book);

			book = new Book();
			book.Index = 98;
			book.FileName = "s0517a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/पटिसम्भिदामग्ग-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/पटिसम्भिदामग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 39;
			bookList.Add(book);

			book = new Book();
			book.Index = 99;
			book.FileName = "s0519a.att.xml";
			book.LongNavPath = "अट्ठकथा/सुत्त पिटक (अट्ठकथा)/खुद्दक निकाय (अट्ठकथा)/नेत्तिप्पकरण-अट्ठकथा";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/नेत्तिप्पकरण-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 40;
			bookList.Add(book);

			book = new Book();
			book.Index = 100;
			book.FileName = "vin01a.att.xml";
			book.LongNavPath = "अट्ठकथा/विनय पिटक (अट्ठकथा)/पाराजिककण्ड-अट्ठकथा";
			book.ShortNavPath = "वि॰ पि॰/पाराजिककण्ड-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.MulaIndex = 43;
			book.TikaIndex = 127; // 127 + 128
			bookList.Add(book);

			book = new Book();
			book.Index = 101;
			book.FileName = "vin02a1.att.xml";
			book.LongNavPath = "अट्ठकथा/विनय पिटक (अट्ठकथा)/पाचित्तिय-अट्ठकथा";
			book.ShortNavPath = "वि॰ पि॰/पाचित्तिय-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.MulaIndex = 44;
			book.TikaIndex = 129;
			bookList.Add(book);

			book = new Book();
			book.Index = 102;
			book.FileName = "vin02a2.att.xml";
			book.LongNavPath = "अट्ठकथा/विनय पिटक (अट्ठकथा)/महावग्ग-अट्ठकथा";
			book.ShortNavPath = "वि॰ पि॰/महावग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.MulaIndex = 45;
			book.TikaIndex = 129;
			bookList.Add(book);

			book = new Book();
			book.Index = 103;
			book.FileName = "vin02a3.att.xml";
			book.LongNavPath = "अट्ठकथा/विनय पिटक (अट्ठकथा)/चूळवग्ग-अट्ठकथा";
			book.ShortNavPath = "वि॰ पि॰/चूळवग्ग-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.MulaIndex = 46;
			book.TikaIndex = 129;
			bookList.Add(book);

			book = new Book();
			book.Index = 104;
			book.FileName = "vin02a4.att.xml";
			book.LongNavPath = "अट्ठकथा/विनय पिटक (अट्ठकथा)/परिवार-अट्ठकथा";
			book.ShortNavPath = "वि॰ पि॰/परिवार-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Whole;
			book.MulaIndex = 47;
			book.TikaIndex = 129;
			bookList.Add(book);

			book = new Book();
			book.Index = 105;
			book.FileName = "abh01a.att.xml";
			book.LongNavPath = "अट्ठकथा/अभिधम्म पिटक (अट्ठकथा)/धम्मसङ्गणि-अट्ठकथा";
			book.ShortNavPath = "अभि॰ पि॰/धम्मसङ्गणि-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Abhidhamma;
			book.MulaIndex = 48;
			bookList.Add(book);

			book = new Book();
			book.Index = 106;
			book.FileName = "abh02a.att.xml";
			book.LongNavPath = "अट्ठकथा/अभिधम्म पिटक (अट्ठकथा)/सम्मोहविनोदनी-अट्ठकथा";
			book.ShortNavPath = "अभि॰ पि॰/सम्मोहविनोदनी-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Abhidhamma;
			book.MulaIndex = 49;
			book.TikaIndex = 141;
			bookList.Add(book);

			book = new Book();
			book.Index = 107;
			book.FileName = "abh03a.att.xml";
			book.LongNavPath = "अट्ठकथा/अभिधम्म पिटक (अट्ठकथा)/पञ्चपकरण-अट्ठकथा";
			book.ShortNavPath = "अभि॰ पि॰/पञ्चपकरण-अट्ठकथा";
			book.Matn = CommentaryLevel.Atthakatha;
			book.Pitaka = Pitaka.Abhidhamma;
			book.MulaIndex = 50; // 50 - 60
			book.TikaIndex = 142;
			bookList.Add(book);

			book = new Book();
			book.Index = 108;
			book.FileName = "s0101t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/दीघ निकाय (टीका)/सीलक्खन्धवग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/सीलक्खन्धवग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 0;
			book.AtthakathaIndex = 61;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 109;
			book.FileName = "s0102t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/दीघ निकाय (टीका)/महावग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/महावग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 1;
			book.AtthakathaIndex = 62;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 110;
			book.FileName = "s0103t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/दीघ निकाय (टीका)/पाथिकवग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/पाथिकवग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 2;
			book.AtthakathaIndex = 63;
			book.ChapterListTypes = "book,sutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 111;
			book.FileName = "s0104t.nrf.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/दीघ निकाय (टीका)/सीलक्खन्धवग्ग-अभिनवटीका-१";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/सीलक्खन्धवग्ग-अभिनवटीका-१";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 112;
			book.FileName = "s0105t.nrf.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/दीघ निकाय (टीका)/सीलक्खन्धवग्ग-अभिनवटीका-२";
			book.ShortNavPath = "सु॰ पि॰/दी॰ नि॰/सीलक्खन्धवग्ग-अभिनवटीका-२";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 113;
			book.FileName = "s0201t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/मज्झिम निकाय (टीका)/मूलपण्णास-टीका";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/मूलपण्णास-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 3;
			book.AtthakathaIndex = 64;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 114;
			book.FileName = "s0202t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/मज्झिम निकाय (टीका)/मज्झिमपण्णास-टीका";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/मज्झिमपण्णास-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 4;
			book.AtthakathaIndex = 65;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 115;
			book.FileName = "s0203t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/मज्झिम निकाय (टीका)/उपरिपण्णास-टीका";
			book.ShortNavPath = "सु॰ पि॰/म॰ नि॰/उपरिपण्णास-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 5;
			book.AtthakathaIndex = 66;
			book.ChapterListTypes = "book,vagga";
			bookList.Add(book);

			book = new Book();
			book.Index = 116;
			book.FileName = "s0301t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/संयुत्त निकाय (टीका)/सगाथावग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/सगाथावग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 6;
			book.AtthakathaIndex = 67;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 117;
			book.FileName = "s0302t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/संयुत्त निकाय (टीका)/निदानवग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/निदानवग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 7;
			book.AtthakathaIndex = 68;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 118;
			book.FileName = "s0303t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/संयुत्त निकाय (टीका)/खन्धवग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/खन्धवग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 8;
			book.AtthakathaIndex = 69;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 119;
			book.FileName = "s0304t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/संयुत्त निकाय (टीका)/सळायतनवग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/सळायतनवग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 9;
			book.AtthakathaIndex = 70;
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 120;
			book.FileName = "s0305t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/संयुत्त निकाय (टीका)/महावग्ग-टीका";
			book.ShortNavPath = "सु॰ पि॰/सं॰ नि॰/महावग्ग-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 10;
			book.AtthakathaIndex = 71; 
			book.ChapterListTypes = "book,samyutta";
			bookList.Add(book);

			book = new Book();
			book.Index = 121;
			book.FileName = "s0401t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/अङ्गुत्तरनिकाय (टीका)/एककनिपात-टीका";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/एककनिपात-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Whole;
			book.MulaIndex = 11;
			book.AtthakathaIndex = 72;
			bookList.Add(book);

			book = new Book();
			book.Index = 122;
			book.FileName = "s0402t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/अङ्गुत्तरनिकाय (टीका)/दुक-तिक-चतुक्कनिपात-टीका";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/दुक-तिक-चतुक्कनिपात-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Multi;
			book.MulaIndex = 12; // 12 + 13 + 14
			book.AtthakathaIndex = 73;
			bookList.Add(book);

			book = new Book();
			book.Index = 123;
			book.FileName = "s0403t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/अङ्गुत्तरनिकाय (टीका)/पञ्चक-छक्क-सत्तकनिपात-टीका";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/पञ्चक-छक्क-सत्तकनिपात-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Multi;
			book.MulaIndex = 15; // 15 + 16 + 17
			book.AtthakathaIndex = 74;
			bookList.Add(book);

			book = new Book();
			book.Index = 124;
			book.FileName = "s0404t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/अङ्गुत्तरनिकाय (टीका)/अट्ठकादिनिपात-टीका";
			book.ShortNavPath = "सु॰ पि॰/अ॰ नि॰/अट्ठकादिनिपात-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			book.BookType = BookType.Multi;
			book.MulaIndex = 18; // 18 + 19 + 20 + 21
			book.AtthakathaIndex = 75;
			bookList.Add(book);

			// The filenames of this book and the next are out of order, but this is the order
			// in which the books are listed in CSCD3
			book = new Book();
			book.Index = 125;
			book.FileName = "s0519t.tik.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/खुद्दकनिकाय (टीका)/नेत्तिप्पकरण-टीका";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/नेत्तिप्पकरण-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 126;
			book.FileName = "s0501t.nrf.xml";
			book.LongNavPath = "टीका/सुत्त पिटक (टीका)/खुद्दकनिकाय (टीका)/नेत्तिविभाविनी";
			book.ShortNavPath = "सु॰ पि॰/खु॰ नि॰/नेत्तिविभाविनी";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Sutta;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 127;
			book.FileName = "vin01t1.tik.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/सारत्थदीपनी-टीका-१";
			book.ShortNavPath = "वि॰ पि॰/सारत्थदीपनी-टीका-१";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Split;
			book.MulaIndex = 43;
			book.AtthakathaIndex = 100;
			bookList.Add(book);

			book = new Book();
			book.Index = 128;
			book.FileName = "vin01t2.tik.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/सारत्थदीपनी-टीका-२";
			book.ShortNavPath = "वि॰ पि॰/सारत्थदीपनी-टीका-२";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Split;
			book.MulaIndex = 43;
			book.AtthakathaIndex = 100;
			bookList.Add(book);

			book = new Book();
			book.Index = 129;
			book.FileName = "vin02t.tik.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/सारत्थदीपनी-टीका-३";
			book.ShortNavPath = "वि॰ पि॰/सारत्थदीपनी-टीका-३";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			book.BookType = BookType.Multi;
			book.MulaIndex = 44; // 44 + 45 + 46 + 47
			book.AtthakathaIndex = 101; // 101 + 102 + 103 + 104
			bookList.Add(book);

			book = new Book();
			book.Index = 130;
			book.FileName = "vin04t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/द्वेमातिकापाळि";
			book.ShortNavPath = "वि॰ पि॰/द्वेमातिकापाळि";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 131;
			book.FileName = "vin05t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/विनयसङ्गह-अट्ठकथा";
			book.ShortNavPath = "वि॰ पि॰/विनयसङ्गह-अट्ठकथा";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 132;
			book.FileName = "vin06t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/वजिरबुद्धि-टीका";
			book.ShortNavPath = "वि॰ पि॰/वजिरबुद्धि-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 133;
			book.FileName = "vin07t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/विमतिविनोदनी-टीका";
			book.ShortNavPath = "वि॰ पि॰/विमतिविनोदनी-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 134;
			book.FileName = "vin08t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/विनयालङ्कार-टीका";
			book.ShortNavPath = "वि॰ पि॰/विनयालङ्कार-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 135;
			book.FileName = "vin09t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/कङ्खावितरणीपुराण-टीका";
			book.ShortNavPath = "वि॰ पि॰/कङ्खावितरणीपुराण-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 136;
			book.FileName = "vin10t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/विनयविनिच्छय-उत्तरविनिच्छय";
			book.ShortNavPath = "वि॰ पि॰/विनयविनिच्छय-उत्तरविनिच्छय";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 137;
			book.FileName = "vin11t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/विनयविनिच्छय-टीका";
			book.ShortNavPath = "वि॰ पि॰/विनयविनिच्छय-टीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 138;
			book.FileName = "vin12t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/पाचित्यादियोजनापाळि";
			book.ShortNavPath = "वि॰ पि॰/पाचित्यादियोजनापाळि";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 139;
			book.FileName = "vin13t.nrf.xml";
			book.LongNavPath = "टीका/विनयपिटक (टीका)/खुद्दसिक्खा-मूलसिक्खा";
			book.ShortNavPath = "वि॰ पि॰/खुद्दसिक्खा-मूलसिक्खा";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Vinaya;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 140;
			book.FileName = "abh01t.tik.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/धम्मसङ्गणी-मूलटीका";
			book.ShortNavPath = "अभि॰ पि॰/धम्मसङ्गणी-मूलटीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 141;
			book.FileName = "abh02t.tik.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/विभङ्ग-मूलटीका";
			book.ShortNavPath = "अभि॰ पि॰/विभङ्ग-मूलटीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			book.MulaIndex = 49;
			book.AtthakathaIndex = 106;
			bookList.Add(book);

			book = new Book();
			book.Index = 142;
			book.FileName = "abh03t.tik.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/पञ्चपकरण-मूलटीका";
			book.ShortNavPath = "अभि॰ पि॰/पञ्चपकरण-मूलटीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			book.MulaIndex = 50; // 50 - 60
			book.AtthakathaIndex = 107;
			bookList.Add(book);

			book = new Book();
			book.Index = 143;
			book.FileName = "abh04t.nrf.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/धम्मसङ्गणी-अनुटीका";
			book.ShortNavPath = "अभि॰ पि॰/धम्मसङ्गणी-अनुटीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 144;
			book.FileName = "abh05t.nrf.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/पञ्चपकरण-अनुटीका";
			book.ShortNavPath = "अभि॰ पि॰/पञ्चपकरण-अनुटीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 145;
			book.FileName = "abh06t.nrf.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/अभिधम्मावतारो-नामरूपपरिच्छेदो";
			book.ShortNavPath = "अभि॰ पि॰/अभिधम्मावतारो-नामरूपपरिच्छेदो";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 146;
			book.FileName = "abh07t.nrf.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/अभिधम्मत्थसङ्गहो";
			book.ShortNavPath = "अभि॰ पि॰/अभिधम्मत्थसङ्गहो";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 147;
			book.FileName = "abh08t.nrf.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/अभिधम्मावतार-पुराणटीका";
			book.ShortNavPath = "अभि॰ पि॰/अभिधम्मावतार-पुराणटीका";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 148;
			book.FileName = "abh09t.nrf.xml";
			book.LongNavPath = "टीका/अभिधम्म पिटक (टीका)/अभिधम्ममातिकापाळि";
			book.ShortNavPath = "अभि॰ पि॰/अभिधम्ममातिकापाळि";
			book.Matn = CommentaryLevel.Tika;
			book.Pitaka = Pitaka.Abhidhamma;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 149;
			book.FileName = "e0101n.mul.xml";
			book.LongNavPath = "अञ्‍ञ/विसुद्धिमग्ग/विसुद्धिमग्ग-१";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 150;
			book.FileName = "e0102n.mul.xml";
			book.LongNavPath = "अञ्‍ञ/विसुद्धिमग्ग/विसुद्धिमग्ग-२";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 151;
			book.FileName = "e0103n.att.xml";
			book.LongNavPath = "अञ्‍ञ/विसुद्धिमग्ग/विसुद्धिमग्ग-महाटीका-१";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 152;
			book.FileName = "e0104n.att.xml";
			book.LongNavPath = "अञ्‍ञ/विसुद्धिमग्ग/विसुद्धिमग्ग-महाटीका-२";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 153;
			book.FileName = "e0105n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/विसुद्धिमग्ग/विसुद्धिमग्ग-निदानकथा";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 154;
			book.FileName = "e0901n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/दीघनिकाय (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 155;
			book.FileName = "e0902n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/मज्झिमनिकाय (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 156;
			book.FileName = "e0903n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/संयुत्तनिकाय (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 157;
			book.FileName = "e0904n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/अङ्गुत्तरनिकाय (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 158;
			book.FileName = "e0905n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/विनयपिटक (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 159;
			book.FileName = "e0906n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/अभिधम्मपिटक (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 160;
			book.FileName = "e0907n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/संगायन-पुच्छा विस्सज्‍जना/अट्ठकथा (पु-वि)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 161;
			book.FileName = "e0201n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/लेडी सयाडो गन्थ-सङ्गहो/निरुत्तिदीपनी";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 162;
			book.FileName = "e0301n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/लेडी सयाडो गन्थ-सङ्गहो/परमत्थदीपनी सङ्गहमहाटीकापाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 163;
			book.FileName = "e0401n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/लेडी सयाडो गन्थ-सङ्गहो/अनुदीपनीपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 164;
			book.FileName = "e0501n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/लेडी सयाडो गन्थ-सङ्गहो/पट्ठानुद्देसदीपनीपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 165;
			book.FileName = "e0601n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/नमक्‍कारटीका";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 166;
			book.FileName = "e0602n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/महापणामपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 167;
			book.FileName = "e0603n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/लक्खणातो बुद्धथोमनागाथा";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 168;
			book.FileName = "e0604n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/सुतवन्दना";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 169;
			book.FileName = "e0605n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/जिनालङ्कार";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 170;
			book.FileName = "e0606n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/कमलाञ्‍जलि";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 171;
			book.FileName = "e0607n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/पज्‍जमधु";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 172;
			book.FileName = "e0608n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/बुद्ध-वन्दना गन्थ-सङ्गहो/बुद्धगुणगाथावली";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 173;
			book.FileName = "e0701n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/वंस-गन्थ-सङ्गहो/चूळगन्थवंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 174;
			book.FileName = "e0702n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/वंस-गन्थ-सङ्गहो/सासनवंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 175;
			book.FileName = "e0703n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/वंस-गन्थ-सङ्गहो/महावंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 176;
			book.FileName = "e0801n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/मोग्गल्‍लानब्याकरणं";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 177;
			book.FileName = "e0802n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/कच्‍चायनब्याकरणं";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 178;
			book.FileName = "e0803n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/सद्दनीतिप्पकरणं (पदमाला)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 179;
			book.FileName = "e0804n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/सद्दनीतिप्पकरणं (धातुमाला)";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 180;
			book.FileName = "e0805n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/पदरूपसिद्धि";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 181;
			book.FileName = "e0806n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/मोगल्‍लानपञ्‍चिका";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 182;
			book.FileName = "e0807n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/पयोगसिद्धिपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 183;
			book.FileName = "e0808n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/वुत्तोदयपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 184;
			book.FileName = "e0809n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/अभिधानप्पदापिकापाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 185;
			book.FileName = "e0810n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/अभिधानप्पदापिकाटीका";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 186;
			book.FileName = "e0811n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/सुबोधालङ्कारपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 187;
			book.FileName = "e0812n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/सुबोधालङ्कारटीका";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 188;
			book.FileName = "e0813n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/ब्याकरण गन्थ-सङ्गहो/बालावतार गण्ठिपदत्थविनिच्छयसार";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 189;
			book.FileName = "e1001n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/कविदप्पणनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 190;
			book.FileName = "e1002n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/नीतिमञ्‍जरी";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 191;
			book.FileName = "e1003n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/धम्मनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 192;
			book.FileName = "e1004n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/महारहनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 193;
			book.FileName = "e1005n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/लोकनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 194;
			book.FileName = "e1006n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/सुत्तन्तनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 195;
			book.FileName = "e1007n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/सूरस्सतिनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 196;
			book.FileName = "e1008n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/चाणक्यनीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 197;
			book.FileName = "e1009n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/नरदक्खदीपनी";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 198;
			book.FileName = "e1010n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/नीति-गन्थ-सङ्गहो/चतुरारक्खदीपनी";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 199;
			book.FileName = "e1101n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/पकिण्णक-गन्थ-सङ्गहो/रसवाहिनी";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 200;
			book.FileName = "e1102n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/पकिण्णक-गन्थ-सङ्गहो/सीमविसोधनीपाठ";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 201;
			book.FileName = "e1103n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/पकिण्णक-गन्थ-सङ्गहो/वेस्सन्तरगीति";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 202;
			book.FileName = "e1201n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/मोग्गल्‍लान वुत्तिविवरणपञ्‍चिका";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 203;
			book.FileName = "e1202n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/थूपवंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 204;
			book.FileName = "e1203n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/दाठवंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 205;
			book.FileName = "e1204n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/धातुपाठविलासिनिया";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 206;
			book.FileName = "e1205n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/धातुवंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 207;
			book.FileName = "e1206n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/हत्थवनगल्‍लविहारवंस";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 208;
			book.FileName = "e1207n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/जिनचरितय";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 209;
			book.FileName = "e1208n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/जिनवंसदीपं";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 210;
			book.FileName = "e1209n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/तेलकटाहगाथा";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 211;
			book.FileName = "e1210n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/मिलिदटीका";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 212;
			book.FileName = "e1211n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/पदमञ्‍जरी";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 213;
			book.FileName = "e1212n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/पदसाधनं";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 214;
			book.FileName = "e1213n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/सद्दबिन्दुपकरणं";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 215;
			book.FileName = "e1214n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/कच्‍चायनधातुमञ्‍जुसा";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			book = new Book();
			book.Index = 216;
			book.FileName = "e1215n.nrf.xml";
			book.LongNavPath = "अञ्‍ञ/सिहळ-गन्थ-सङ्गहो/सामन्तकूटवण्णना";
			book.ShortNavPath = book.LongNavPath;
			book.Matn = CommentaryLevel.Other;
			book.Pitaka = Pitaka.Other;
			// unlinked
			bookList.Add(book);

			foreach (Book book2 in bookList)
			{
				booksByFile[book2.FileName] = book2;
			}
		}

		public BitArray MulaBits
		{
			get { return mulaBits; }
			private set { mulaBits = value; }
		}
		private BitArray mulaBits = null!;

		public BitArray AtthaBits
		{
			get { return atthaBits; }
			private set { atthaBits = value; }
		}
		private BitArray atthaBits = null!;

		public BitArray TikaBits
		{
			get { return tikaBits; }
			private set { tikaBits = value; }
		}
		private BitArray tikaBits = null!;

		public BitArray SuttaBits
		{
			get { return suttaBits; }
			private set { suttaBits = value; }
		}
		private BitArray suttaBits = null!;

		public BitArray VinayaBits
		{
			get { return vinayaBits; }
			private set { vinayaBits = value; }
		}
		private BitArray vinayaBits = null!;

		public BitArray AbhiBits
		{
			get { return abhiBits; }
			private set { abhiBits = value; }
		}
		private BitArray abhiBits = null!;

		public BitArray OtherBits
		{
			get { return otherBits; }
			private set { otherBits = value; }
		}
		private BitArray otherBits = null!;


		private void CalculateBitArrays()
		{
			mulaBits = new BitArray(bookList.Count);
			atthaBits = new BitArray(bookList.Count);
			tikaBits = new BitArray(bookList.Count);
			suttaBits = new BitArray(bookList.Count);
			vinayaBits = new BitArray(bookList.Count);
			abhiBits = new BitArray(bookList.Count);
			otherBits = new BitArray(bookList.Count);

			int i = 0;
			foreach (Book book in bookList)
			{
				if (book.Matn == CommentaryLevel.Mula)
					mulaBits[i] = true;
				else if (book.Matn == CommentaryLevel.Atthakatha)
					atthaBits[i] = true;
				else if (book.Matn == CommentaryLevel.Tika)
					tikaBits[i] = true;

				if (book.Pitaka == Pitaka.Sutta)
					suttaBits[i] = true;
				else if (book.Pitaka == Pitaka.Vinaya)
					vinayaBits[i] = true;
				else if (book.Pitaka == Pitaka.Abhidhamma)
					abhiBits[i] = true;
				else if (book.Pitaka == Pitaka.Other)
					otherBits[i] = true;

				i++;
			}
		}

		public Book this[int index]
		{
			get
			{
				return bookList[index];
			}
		}

		public Book this[string file]
		{
			get
			{
				return booksByFile[file];
			}
		}

		public int Count
		{
			get
			{
				return bookList.Count;
			}
		}
	}

	public enum Pitaka
	{
		Vinaya,
		Sutta,
		Abhidhamma,
		Other
	}

	public enum CommentaryLevel
	{
		Mula,
		Atthakatha,
		Tika,
		Other
	}

	public enum BookType
	{
		Whole,
		Multi,
		Split,
		Unknown
	}
}
