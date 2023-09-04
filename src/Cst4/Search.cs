using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using CST.Conversion;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ToolTip;

namespace CST
{
    public static class Search
    {
        static Search()
        {
            AllTerms = new List<string>();
        }

        public static List<string> AllTerms
        {
            get { return allTerms; }
            set { allTerms = value; }
        }
        private static List<string> allTerms;

        public static IndexReader NdxReader
        {
            get
            {
                if (ndxReader == null)
                {
                    ndxReader = DirectoryReader.Open(FSDirectory.Open(Config.Inst.IndexDirectory));
                }
                return ndxReader;
            }
            set { ndxReader = value; }
        }
        private static IndexReader ndxReader;

		public static IndexSearcher NdxSearcher
		{
			get
			{
				if (ndxSearcher == null)
					ndxSearcher = new IndexSearcher(NdxReader);

				return ndxSearcher;
			}
		}
		private static IndexSearcher ndxSearcher;

		public static void GetMatchingTerms(
			string ipeTerm, TermMatchEvaluator tme, ListBox listBoxWords, BitArray bookBits)
		{
			if (tme.QueryType == QueryType.Word)
			{
				MatchingWord mw = new MatchingWord();
				mw.Word = ipeTerm;
				List<MatchingWordBook> matchingBooks = Search.GetMatchingWordBooks(mw.Word, bookBits);
				if (matchingBooks.Count > 0)
				{
					mw.MatchingBooks = matchingBooks;
					listBoxWords.Items.Add(mw);
				}
			}
			else
			{
				bool foundMatch = false;

				Fields fields = MultiFields.GetFields(Search.NdxReader);
				Terms terms = fields.GetTerms("text");
				TermsEnum termsEnum = terms.GetEnumerator();

				while (termsEnum.MoveNext())
				{
					string term = termsEnum.Current.Term.Utf8ToString();

					if (tme.Evaluate(term))
					{
						foundMatch = true;
						MatchingWord mw = new MatchingWord();
						mw.Word = term;
						List<MatchingWordBook> matchingBooks = Search.GetMatchingWordBooks(mw.Word, bookBits);
						if (matchingBooks.Count > 0)
						{
							mw.MatchingBooks = matchingBooks;
							listBoxWords.Items.Add(mw);
						}
					}
					else if (tme.QueryType == QueryType.StartsWith && foundMatch)
					{
						// this assumes that the term list is sorted.
						// if the last was a match and this one isn't, then we have
						// passed the possible matches for a StartsWith query.
						break;
					}
				}
			}
		}

		/*
		 * GetMatchingTermsWithContext does search with multiple terms (word + context words).
		 * Lucene does not do phrase/multi-word searches with wildcards, so we will have to build this
		 * on top of the public APIs. Remember to consider rewriting this to use offsets stored in the Payload
		 * starting with Lucene 2.3.
		 * 
		 * Here's the algorithm:
		 * For each query term, get a List of matching words and store in matchingWordsArray
		 * Bail if any of the Lists are empty
		 * Calculate a BitArray for each matching words representing the Books that contain the term
		 *		and are in the bookBits filter
		 * Calculate a BitArray of the Books that have a term from each list.
		 * Remove the words that are not in candidate Books BitArray, resulting in new word Lists 
		 *		in the matchingWordsArray
		 * For each document, get the term positions for each matching word
		 *		Put the positions for the first term in a List, the others in a Set
		 * 
		 * 
		 */
		public static void GetMatchingTermsWithContext(
			string[] ipeTerms, TermMatchEvaluator[] tmes, ListBox listBoxWords, BitArray bookBits, 
			int contextDistance, bool isPhrase)
		{
			DateTime start = DateTime.Now;

			List<string>[] matchingWordsArray = new List<string>[ipeTerms.Length];
			//List<WordPosition>[] wordPositionsArray = new List<WordPosition>[ipeTerms.Length];

			for (int i = 0; i < ipeTerms.Length; i++)
			{
				string ipeTerm = ipeTerms[i];
				TermMatchEvaluator tme = tmes[i];
				matchingWordsArray[i] = new List<string>();

				if (tme.QueryType == QueryType.Word)
				{
					matchingWordsArray[i].Add(ipeTerm);
				}
				else
				{
					bool foundMatch = false;

					Fields fields = MultiFields.GetFields(Search.NdxReader);
					Terms terms = fields.GetTerms("text");
					TermsEnum termsEnum = terms.GetEnumerator();
					while (termsEnum.MoveNext())
					{
						string term = termsEnum.Current.Term.Utf8ToString();

						if (tme.Evaluate(term))
						{
							foundMatch = true;
							matchingWordsArray[i].Add(term);
						}
						else if (tme.QueryType == QueryType.StartsWith && foundMatch)
						{
							// this assumes that the term list is sorted.
							// if the last was a match and this one isn't, then we have
							// passed the possible matches for a StartsWith query.
							break;
						}
					}
				}
			}

			// if any of the lists of terms matching the query word or context words is empty, we're done
			for (int i = 0; i < matchingWordsArray.Length; i++)
			{
				if (matchingWordsArray[i].Count == 0)
					return;
			}

			// get the books containing each term 
			List<BitArray>[] matchingBookBitsArray = new List<BitArray>[ipeTerms.Length];
			for (int i = 0; i < matchingWordsArray.Length; i++)
			{
				List<string> matchingWords = matchingWordsArray[i];
				matchingBookBitsArray[i] = new List<BitArray>();

				for (int j = 0; j < matchingWords.Count; j++)
				{
					matchingBookBitsArray[i].Add(Search.GetMatchingBookBits(matchingWords[j], bookBits));
				}
			}

			// calculate a BitArray for each query term including all books that contain a matching term
			BitArray[] perTermBitArrays = new BitArray[ipeTerms.Length];
			for (int i = 0; i < matchingBookBitsArray.Length; i++)
			{
				List<BitArray> matchingBookBitsList = matchingBookBitsArray[i];
				perTermBitArrays[i] = new BitArray(Books.Inst.Count);

				foreach (BitArray bits in matchingBookBitsList)
				{
					perTermBitArrays[i].Or(bits);
				}
			}

			// calculate a BitArray of the books that have at least one matching term for each query term
			BitArray candidateBooks = new BitArray(Books.Inst.Count, true);
			for (int i = 0; i < matchingBookBitsArray.Length; i++)
			{
				candidateBooks.And(perTermBitArrays[i]);
			}

			// find and remove the words that have no book on the candidate books list
			for (int i = 0; i < matchingWordsArray.Length; i++)
			{
				List<string> matchingWords = matchingWordsArray[i];
				List<BitArray> matchingBookBits = matchingBookBitsArray[i];
				ISet<int> deleteThese = new HashSet<int>();
				
				for (int j = 0; j < matchingBookBits.Count; j++)
				{
					BitArray commonBooks = matchingBookBits[j].And(candidateBooks);
					int onCount = 0;
					for (int k = 0; k < commonBooks.Length; k++)
					{
						onCount += (commonBooks[k] ? 1 : 0);
					}

					if (onCount == 0)
						deleteThese.Add(j);
				}

				if (deleteThese.Count > 0)
				{
					List<string> matchingWords2 = new List<string>();
					List<BitArray> matchingBookBits2 = new List<BitArray>();

					for (int j = 0; j < matchingBookBits.Count; j++)
					{
						if (deleteThese.Contains(j) == false)
						{
							matchingWords2.Add(matchingWords[j]);
							matchingBookBits2.Add(matchingBookBits[j]);
						}
					}

					matchingWordsArray[i] = matchingWords2;
					matchingBookBitsArray[i] = matchingBookBits2;
				}
			}

			TimeSpan elapsed = DateTime.Now.Subtract(start);

			// The results data structure:
			// the string is all of the words concatenated together with ~ as the separator
			//		(word1~word2~wordn)
			// the first int key is the book id
			// the second int key is the numeric position, the same as WordPosition.position
			// The first terms are identified by IsFirstTerm == true and the context
			//		words false
			Dictionary<string, SortedDictionary<int, SortedDictionary<int, WordPosition>>> results =
				new Dictionary<string, SortedDictionary<int, SortedDictionary<int, WordPosition>>>();

			for (int bookIndex = 0; bookIndex < Books.Inst.Count; bookIndex++)
			{
				if (candidateBooks[bookIndex] == false)
					continue;

				List<WordPosition> firstTermPositions = GetFirstTermPositions(
					matchingWordsArray[0], matchingBookBitsArray[0], bookIndex);

				List<Dictionary<int, WordPosition>> contextWordHashes = new List<Dictionary<int, WordPosition>>();
				for (int j = 1; j < matchingWordsArray.Length; j++)
				{
					contextWordHashes.Add(GetContextTermPositions(matchingWordsArray[j], matchingBookBitsArray[j], bookIndex));
				}

				List<WordPosition> contextWordPositions = new List<WordPosition>();

				// iterate over all of the word positions of the words matching the first query term
				foreach (WordPosition wp in firstTermPositions)
				{
					contextWordPositions.Clear();
					for (int j = 0; j < contextWordHashes.Count; j++)
					{
						WordPosition contextPos = GetContextPosition(wp, 
							(isPhrase ? j+1 : contextDistance), contextWordHashes[j], isPhrase);
						if (contextPos == null)
							break;
						else
							contextWordPositions.Add(contextPos);
					}

					if (contextWordHashes.Count == contextWordPositions.Count)
					{
						// MATCH!!

						string concatted = matchingWordsArray[0][wp.WordIndex];
						for (int j = 0; j < contextWordPositions.Count; j++)
						{
							WordPosition cwp = contextWordPositions[j];
							// the 0th element of matchingWordsArray is the first term position, 
							// so the context words start at index 1
							concatted += "~" + matchingWordsArray[j + 1][cwp.WordIndex];
						}

						SortedDictionary<int, SortedDictionary<int, WordPosition>> bookDict;
						SortedDictionary<int, WordPosition> posDict;

						if (results.ContainsKey(concatted) == false)
						{
							bookDict = new SortedDictionary<int, SortedDictionary<int, WordPosition>>();
							results.Add(concatted, bookDict);
						}
						else
							bookDict = results[concatted];

						if (bookDict.ContainsKey(bookIndex) == false)
						{
							posDict = new SortedDictionary<int, WordPosition>();
							bookDict.Add(bookIndex, posDict);
						}
						else
							posDict = bookDict[bookIndex];

						// Add the first term position and the context positions to the position dictionary.
						// Since a position can occur both as a first term position and a context position
						// for a different first term, we will give preference to the first term position.
						if (posDict.ContainsKey(wp.Position))
							posDict.Remove(wp.Position);

						posDict.Add(wp.Position, wp);

						// add the word string to the WordPosition since the word array is going out of scope
						wp.Word = matchingWordsArray[0][wp.WordIndex];

						int termNum = 1;
						foreach (WordPosition cwp in contextWordPositions)
						{
							if (posDict.ContainsKey(cwp.Position))
								continue;

							posDict.Add(cwp.Position, cwp);

							// add the word string to the WordPosition since the word array is going out of scope
							cwp.Word = matchingWordsArray[termNum][cwp.WordIndex];

							termNum++;
						}
					}
				}
			}

			// display results
			List<MatchingMultiWord> mmws = new List<MatchingMultiWord>();
			foreach (string multiword in results.Keys)
			{
				MatchingMultiWord mmw = new MatchingMultiWord();
				mmw.SetMultiWord(multiword);
				mmw.SetBookResults(results[multiword]);
				mmw.IsPhrase = isPhrase;
				mmws.Add(mmw);
			}
			mmws.Sort(delegate(MatchingMultiWord mmw1, MatchingMultiWord mmw2) { return mmw1.CompareTo(mmw2); });

			foreach (MatchingMultiWord mmw in mmws)
			{
				listBoxWords.Items.Add(mmw);
			}

			TimeSpan elapsed3 = start.Subtract(DateTime.Now);
			
			//int x = 0;
		}

		private static List<WordPosition> GetFirstTermPositions(
			List<string> matchingWords, List<BitArray> matchingBookBits, int bookIndex)
		{
			int docId = Books.Inst[bookIndex].DocId;
			List<WordPosition> wordPositions = new List<WordPosition>();

			IBits liveDocs = MultiFields.GetLiveDocs(Search.NdxReader);

			for (int i = 0; i < matchingWords.Count; i++)
			{
				// skip words that are not in the book
				if (matchingBookBits[i][bookIndex] == false)
					continue;

				DocsAndPositionsEnum dape = MultiFields.GetTermPositionsEnum(Search.NdxReader, liveDocs, "text",
					new BytesRef(System.Text.UTF8Encoding.UTF8.GetBytes(matchingWords[i])));

				List<MatchingWordBook> matchingBooks = new List<MatchingWordBook>();

				int termPosDoc = dape.NextDoc();
				while (termPosDoc != DocIdSetIterator.NO_MORE_DOCS && termPosDoc < docId)
				{
					termPosDoc = dape.NextDoc();
				}

				for (int j = 0; j < dape.Freq; j++)
				{
					wordPositions.Add(new WordPosition(i, dape.NextPosition(), j, true));
				}
			}

			wordPositions.Sort(delegate(WordPosition wp1, WordPosition wp2) { return wp1.CompareTo(wp2); });
			return wordPositions;
		}

		/// <summary>
		/// Given a list of words matching one of the context query terms and a book index, this returns a
		/// Dictionary with the numeric position as a key (same as WordPosition.position) and a WordPosition
		/// object as the value. 
		/// </summary>
		/// <param name="matchingWords">a List of words matching one of the context query terms</param>
		/// <param name="matchingBookBits">a List, parallel to matchingWords, of BitArrays that indicate the 
		/// books that contain the word from matchingWord</param>
		/// <param name="bookIndex">the book index</param>
		/// <returns></returns>
		private static Dictionary<int, WordPosition> GetContextTermPositions(
			List<string> matchingWords, List<BitArray> matchingBookBits, int bookIndex)
		{
			int docId = Books.Inst[bookIndex].DocId;
			Dictionary<int, WordPosition> wordPositions = new Dictionary<int, WordPosition>();

			IBits liveDocs = MultiFields.GetLiveDocs(Search.NdxReader);

			for (int i = 0; i < matchingWords.Count; i++)
			{
				// skip words that are not in the book
				if (matchingBookBits[i][bookIndex] == false)
					continue;

				DocsAndPositionsEnum dape = MultiFields.GetTermPositionsEnum(Search.NdxReader, liveDocs, "text",
					new BytesRef(System.Text.UTF8Encoding.UTF8.GetBytes(matchingWords[i])));

				// skip to this book
				int termPosDoc = dape.NextDoc();
				while (termPosDoc != DocIdSetIterator.NO_MORE_DOCS && termPosDoc < docId)
				{
					termPosDoc = dape.NextDoc();
				}

				for (int j = 0; j < dape.Freq; j++)
				{
					int nextPos = dape.NextPosition();
					wordPositions.Add(nextPos, new WordPosition(i, nextPos, j, false));
				}
			}

			return wordPositions;
		}

		public static WordPosition GetContextPosition(WordPosition wp, int contextDistance, 
			Dictionary<int, WordPosition> wordPosMap, bool isPhrase)
		{
			if (isPhrase)
			{
				if (wordPosMap.ContainsKey(wp.Position + contextDistance))
					return wordPosMap[wp.Position + contextDistance];
			}
			else
			{
				for (int i = 1; i <= contextDistance; i++)
				{
					if (wordPosMap.ContainsKey(wp.Position + i))
						return wordPosMap[wp.Position + i];

					if (wordPosMap.ContainsKey(wp.Position - i))
						return wordPosMap[wp.Position - i];
				}
			}

			return null;
		}

		/*
					wordPositionsArray[i] = new List<WordPosition>();
		 
					foreach (MatchingWordBook mwb in matchingWordBooks)
					{
						TermPositionVector tpv = (TermPositionVector)NdxReader.GetTermFreqVector(mwb.Book.DocId, "text");
						string[] termArray = tpv.GetTerms();

						int index = System.Array.BinarySearch(termArray, word, new IpeComparer());
						TermVectorOffsetInfo[] tvoiArray = tpv.GetOffsets(index);
						int[] termPositions = tpv.GetTermPositions(index);


						for (int k = 0; k < termPositions.Length; k++)
						{
							WordPosition wp = new WordPosition();
							wp.BookIndex = mwb.Book.Index;
							wp.StartOffset = tvoiArray[k].GetStartOffset();
							wp.EndOffset = tvoiArray[k].GetEndOffset();
							wp.Position = termPositions[k];
							wp.WordIndex = j;
						}
					}
		 */

		public static List<MatchingWordBook> GetMatchingWordBooks(string term, BitArray bookBits)
        {
            Books books = Books.Inst;

			IBits liveDocs = MultiFields.GetLiveDocs(Search.NdxReader);
			DocsAndPositionsEnum dape = MultiFields.GetTermPositionsEnum(Search.NdxReader, liveDocs, "text",
				new BytesRef(System.Text.UTF8Encoding.UTF8.GetBytes(term)));

			List<MatchingWordBook> matchingBooks = new List<MatchingWordBook>();
			int lastDocId = -1;
			int docId = dape.NextDoc();

			while (docId != DocIdSetIterator.NO_MORE_DOCS)
			{
				// for reasons unknown, TermDocs sometimes returns 
				// multiple instances of the same doc with the same frequency
				// FSnow 2020-05-17 See if this is still true
				if (docId == lastDocId)
				{
					docId = dape.NextDoc();
					continue;
				}
				else
					lastDocId = docId;

				Book book = books.FromDocId(docId);
                if (bookBits[book.Index])
                {
                    MatchingWordBook mwb = new MatchingWordBook();
                    mwb.Book = book;
                    mwb.Count = dape.Freq;
                    matchingBooks.Add(mwb);
                }

				docId = dape.NextDoc();
			}

            return matchingBooks;
        }

		public static BitArray GetMatchingBookBits(string term, BitArray bookBits)
		{
			Books books = Books.Inst;
			IBits liveDocs = MultiFields.GetLiveDocs(Search.NdxReader);
			DocsAndPositionsEnum dape = MultiFields.GetTermPositionsEnum(Search.NdxReader, liveDocs, "text",
				new BytesRef(System.Text.UTF8Encoding.UTF8.GetBytes(term)));
			BitArray matchingBookBits = new BitArray(books.Count);
			int lastDocId = -1;
			int docId = dape.NextDoc();
			while (docId != DocIdSetIterator.NO_MORE_DOCS)
			{
				// for reasons unknown, TermDocs sometimes returns 
				// multiple instances of the same doc with the same frequency
				if (docId == lastDocId)
					continue;
				else
					lastDocId = docId;

				Book book = books.FromDocId(docId);
				matchingBookBits.Set(book.Index, true);
			}

			return matchingBookBits.And(bookBits);
		}


		public static string HighlightMultiWordTerms(string devXml, int docId, 
			SortedDictionary<int, WordPosition> wordPositions, out int totalHits)
		{
			// FSnow 2022-04-22 TODO: pasting in this section for later
			/*
			IBits liveDocs = MultiFields.GetLiveDocs(Search.NdxReader);
			DocsAndPositionsEnum dape = MultiFields.GetTermPositionsEnum(Search.NdxReader, liveDocs, "text",
				new BytesRef(System.Text.UTF8Encoding.UTF8.GetBytes(matchingWords[i])));

			// skip to this book
			int termPosDoc = dape.NextDoc();
			while (termPosDoc != DocIdSetIterator.NO_MORE_DOCS && termPosDoc < docId)
			{
				termPosDoc = dape.NextDoc();
			}

			for (int j = 0; j < dape.Freq; j++)
			{
				int nextPos = dape.NextPosition();
				wordPositions.Add(nextPos, new WordPosition(i, nextPos, j, false));
			}
			*/

			// FSnow 2022-04-22 TODO: commenting out everything except the return value
			/*
			TermPositionVector tpv = (TermPositionVector)NdxReader.GetTermFreqVector(docId, "text");
			string[] termArray = tpv.GetTerms();

			foreach (WordPosition wp in wordPositions.Values)
			{
				int termIndex = System.Array.BinarySearch(termArray, wp.Word, new IpeComparer());
				TermVectorOffsetInfo[] tvoiArray = tpv.GetOffsets(termIndex);
				int[] termPositions = tpv.GetTermPositions(termIndex);

				// check some assumptions
				if (termPositions[wp.PositionIndex] != wp.Position)
				{
					//int z1 = 0;
				}
				if (termPositions.Length != tvoiArray.Length)
				{
					//int z2 = 0;
				}

				wp.StartOffset = tvoiArray[wp.PositionIndex].GetStartOffset();
				wp.EndOffset = tvoiArray[wp.PositionIndex].GetEndOffset();
			}

			StringBuilder sb = new StringBuilder();
			int i = 0;

			string hiBoldOpen = "<hi rend=\"bold\">";
			string hiClose = "</hi>";

			int pos = 0;

			foreach (WordPosition wp in wordPositions.Values)
			{
				int start = wp.StartOffset;
				int end = wp.EndOffset;

				// prevents a crash. this coincides with hitting the "int z1 = 0;" breakpoint debugging line above.
				if (start < pos)
					continue;

				sb.Append(devXml.Substring(pos, start - pos));

				string word = devXml.Substring(start, end - start + 1);
				bool hasHiOpen = word.Contains("<hi");
				bool hasHiClose = word.Contains("</hi");

				string hitRend = (wp.IsFirstTerm ? "hit" : "context");
				string hiHitOpen = "<hi rend=\"" + hitRend + "\"";
				if (wp.IsFirstTerm)
					hiHitOpen += " id=\"hit" + i + "\"";
				hiHitOpen += ">";

				if ((hasHiOpen && hasHiClose) || (!hasHiOpen && !hasHiClose))
				{
					sb.Append(hiHitOpen);
					sb.Append(word);
					sb.Append(hiClose);
				}
				else if (hasHiOpen)
				{
					sb.Append(hiHitOpen);
					sb.Append(word);
					sb.Append(hiClose); // close hi bold
					sb.Append(hiClose); // close hi hit
					sb.Append(hiBoldOpen);
				}
				else if (hasHiClose)
				{
					sb.Append(hiClose); // close hi bold
					sb.Append(hiHitOpen);
					sb.Append(hiBoldOpen);
					sb.Append(word);
					sb.Append(hiClose); // close hi hit
				}

				pos = end + 1;

				if (wp.IsFirstTerm)
					i++;
			}
			sb.Append(devXml.Substring(pos, devXml.Length - pos));

			totalHits = i;

			return sb.ToString();
			*/

			// TODO: the minimum to return
			totalHits = 0;
			return devXml;
		}

        public static string HighlightTerms(string devXml, int docId,
			List<string> terms, out int totalHits)
        {
            Books books = Books.Inst;
            IBits liveDocs = MultiFields.GetLiveDocs(ndxReader);
			List<Tuple<int, int>> positionsList = new List<Tuple<int, int>>();

			foreach (string term in terms)
			{
				DocsAndPositionsEnum dape = MultiFields.GetTermPositionsEnum(ndxReader, liveDocs, "text",
	new BytesRef(System.Text.UTF8Encoding.UTF8.GetBytes(term)));

				int lastDocId = -1;
				int thisDocId = dape.NextDoc();

				// advance the doc pointer to the doc we're highlighting (docId)
				while (thisDocId != DocIdSetIterator.NO_MORE_DOCS)
				{
					// for reasons unknown, TermDocs sometimes returns 
					// multiple instances of the same doc with the same frequency
					// FSnow 2020-05-17 See if this is still true
					if (thisDocId == lastDocId)
					{
						thisDocId = dape.NextDoc();
						continue;
					}
					else if (thisDocId == docId)
						break;

					lastDocId = thisDocId;
                    thisDocId = dape.NextDoc();
                }

				// there were no hits on this book for this term?
				if (thisDocId != docId)
					continue;

				// iterate over occurrences of the term in this book
				int posCount = 0;
				while (posCount < dape.Freq)
				{
					dape.NextPosition();
					int start = dape.StartOffset;
					int end = dape.EndOffset;
					posCount++;
					positionsList.Add(new Tuple<int, int>(start, end));
				}
			}

            // iterate over positionsList
            positionsList.Sort(new TupleItem1of2IntsComparer());

            Book book = books.FromDocId(docId);

			StringBuilder sb = new StringBuilder();
			int i = 0;

			string hiBoldOpen = "<hi rend=\"bold\">";
			string hiClose = "</hi>";

            int pos = 0;
            foreach (Tuple<int, int> posTuple in positionsList)
			{
				int start = posTuple.Item1;
				int end = posTuple.Item2;
				sb.Append(devXml.Substring(pos, start - pos));

				string word = devXml.Substring(start, end - start + 1);
				bool hasHiOpen = word.Contains("<hi");
				bool hasHiClose = word.Contains("</hi");

				string hiHitOpen = "<hi rend=\"hit\" id=\"hit" + i + "\">";

				if ((hasHiOpen && hasHiClose) || (!hasHiOpen && !hasHiClose))
				{
					sb.Append(hiHitOpen);
					sb.Append(word);
					sb.Append(hiClose);
				}
				else if (hasHiOpen)
				{
					sb.Append(hiHitOpen);
					sb.Append(word);
					sb.Append(hiClose); // close hi bold
					sb.Append(hiClose); // close hi hit
					sb.Append(hiBoldOpen);
				}

                else if (hasHiClose)
				{
					sb.Append(hiClose); // close hi bold
					sb.Append(hiHitOpen);
					sb.Append(hiBoldOpen);
					sb.Append(word);
					sb.Append(hiClose); // close hi hit
				}

				pos = end + 1;
				i++;
            }
            sb.Append(devXml.Substring(pos, devXml.Length - pos));

			totalHits = i;

			return sb.ToString();
        }

		public static string AdvancedSearch(string queryString)
		{
			/*
			queryString = ScriptConverter.Convert(queryString, Script.Unknown, Script.Ipe);
			Query query = null;
			try
			{
				QueryParser qp = new QueryParser("text", new IpeAnalyzer());
				query = qp.Parse(queryString);
			}
			catch (Exception ex)
			{
				return "Lucene ParseException: " + ex.Message;
			}

			Hits hits = null;
			try
			{
				hits = NdxSearcher.Search(query);
			}
			catch (Exception ex)
			{
				return "Lucene Searching Exception: " + ex.Message;
			}

			int hitCount = hits.Length();
			StringBuilder sb = new StringBuilder();
			sb.Append("Total hits: " + hitCount + "\r\n");
			*/
			/*	IEnumerator hitsIterator = hits.Iterator();
				while (hitsIterator.MoveNext())
				{
					Hit hit = (Hit)hitsIterator.Current;
					Book book = Books.Inst.FromDocId(hit.GetId());
					if (book == null)
					{
						sb.Append("Error: Could not get Book object for file");
						continue;
					}

					sb.Append(ScriptConverter.Convert(book.ShortNavPath, Script.Devanagari, AppState.Inst.CurrentScript, true)
						+ " (Score: " + ((float)(hit.GetScore() * 100.0)).ToString("0.00") + ")\r\n");
				}
				*/

			/*
			for (int i = 0; i < hitCount; i++)
			{
				Document doc = hits.Doc(i);
				Book book = Books.Inst.FromDocId(hits.Id(i));
				if (book == null)
				{
					sb.Append("Error: Could not get Book object for file");
					continue;
				}

				sb.Append(ScriptConverter.Convert(book.ShortNavPath, Script.Devanagari, AppState.Inst.CurrentScript, true)
					+ " (Score: " + ((double)(hits.Score(i) * 100.0)).ToString("0.00") + ")\r\n");

				//sb.Append("     ").Append(NdxSearcher.Explain(query, i)).Append("\r\n\r\n");
			}

			return sb.ToString();
			*/

			return "";
		}

		/*
				public static void GetTermsWithWildcardTermEnum(string ipeTerm)
				{
					List<string> wTerms = new List<string>();

					if (ipeTerm.Contains("*") || ipeTerm.Contains("?"))
					{
						// Wildcard seems to mean "one or more matching letters", not zero letters.
						// For this reason, we lookup the unwildcarded term and add it to the list if its in the index
						if (ipeTerm.Contains("*"))
						{
							string woStar = ipeTerm.Replace("*", "");
							wTerms.Add(woStar);
						}

						WildcardTermEnum wte = new WildcardTermEnum(NdxReader, new Term("text", ipeTerm));
						while (wte.Next())
						{
							wTerms.Add(wte.Term().Text());
						}
					}

					for (int i = 0; i < wTerms.Count; i++)
					{
						wTerms[i] = Ipe2Latn.Convert(wTerms[i]);
					}

					wTerms.Sort(new IpeComparer());
				}

				public static void GetAllDocIds()
				{
					Books books = Books.Inst;

					for (int i = 0; i < NdxReader.MaxDoc(); i++)
					{
						if (NdxReader.IsDeleted(i))
							continue;

						Document doc = NdxReader.Document(i);
						books.SetDocId(doc.Get("file"), i);
					}
				}
		*/
    }
}
