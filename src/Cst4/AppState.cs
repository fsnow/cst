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
    public class AppState
    {
        public AppState()
        {
            currentScript = Script.Latin;
        }

		static AppState()
        {
            fileName = "app-state.dat";
        }

        public static AppState Inst
        {
			get
			{
				if (appState == null)
					appState = new AppState();

				return appState;
			}
        }

		private static AppState appState;
        private static string fileName;


		public MruList MruList
		{
			get
			{
				if (mruList == null)
					mruList = new MruList();

				return mruList;
			}
			set { mruList = value; }
		}
		private MruList mruList;

        // main form properties

        public FormWindowState WindowState
        {
            get { return windowState; }
            set { windowState = value; }
        }
        private FormWindowState windowState;

        public Size Size
        {
            get { return size; }
            set { size = value; }
        }
        private Size size;

        public Point Location
        {
            get { return location; }
            set { location = value; }
        }
        private Point location;

		public string InterfaceLanguage
		{
			get { return interfaceLanguage; }
			set { interfaceLanguage = value; }
		}
		private string interfaceLanguage;

        public Script CurrentScript
        {
            get { return currentScript; }
            set { currentScript = value; }
        }
        private Script currentScript;


        // search form properties

        public bool SearchFormShown
        {
            get { return searchFormShown; }
            set { searchFormShown = value; }
        }
        private bool searchFormShown;

        public Point SearchFormLocation
        {
            get { return searchFormLocation; }
            set { searchFormLocation = value; }
        }
        private Point searchFormLocation;

        public string SearchTerms
        {
            get { return searchTerms; }
            set { searchTerms = value; }
        }
        private string searchTerms;

		public int SearchContextDistance
		{
			get { return searchContextDistance; }
			set { searchContextDistance = value; }
		}
		private int searchContextDistance;

        public int SearchBookCollSelected
        {
            get { return searchBookCollSelected; }
            set { searchBookCollSelected = value; }
        }
        private int searchBookCollSelected;

        public bool SearchVinaya
        {
            get { return searchVinaya; }
            set { searchVinaya = value; }
        }
        private bool searchVinaya;

        public bool SearchSutta
        {
            get { return searchSutta; }
            set { searchSutta = value; }
        }
        private bool searchSutta;

        public bool SearchAbhi
        {
            get { return searchAbhi; }
            set { searchAbhi = value; }
        }
        private bool searchAbhi;

        public bool SearchMula
        {
            get { return searchMula; }
            set { searchMula = value; }
        }
        private bool searchMula;

        public bool SearchAttha
        {
            get { return searchAttha; }
            set { searchAttha = value; }
        }
        private bool searchAttha;

        public bool SearchTika
        {
            get { return searchTika; }
            set { searchTika = value; }
        }
        private bool searchTika;

        public bool SearchOtherTexts
        {
            get { return searchOtherTexts; }
            set { searchOtherTexts = value; }
        }
        private bool searchOtherTexts;

        public bool SearchAll
        {
            get { return searchAll; }
            set { searchAll = value; }
        }
        private bool searchAll;

        public int SearchUse
        {
            get { return searchUse; }
            set { searchUse = value; }
        }
        private int searchUse;

        public int[] SearchWordsSelected
        {
            get { return searchWordsSelected; }
            set { searchWordsSelected = value; }
        }
        private int[] searchWordsSelected;

        public int SearchBookSelected
        {
            get { return searchBookSelected; }
            set { searchBookSelected = value; }
        }
        private int searchBookSelected;


        // Select a Book form properties

        public bool SelectFormShown
        {
            get { return selectFormShown; }
            set { selectFormShown = value; }
        }
        private bool selectFormShown;

        public Point SelectFormLocation
        {
            get { return selectFormLocation; }
            set { selectFormLocation = value; }
        }
        private Point selectFormLocation;

        public Size SelectFormSize
        {
            get { return selectFormSize; }
            set { selectFormSize = value; }
        }
        private Size selectFormSize;

		public BitArray SelectFormNodeStates
		{
			get { return selectFormNodeStates; }
			set { selectFormNodeStates = value; }
		}
		private BitArray selectFormNodeStates;

        public bool DictionaryShown
        {
            get { return dictionaryShown; }
            set { dictionaryShown = value; }
        }
        private bool dictionaryShown;

        public Point DictionaryLocation
        {
            get { return dictionaryLocation; }
            set { dictionaryLocation = value; }
        }
        private Point dictionaryLocation;

		public Size DictionarySize
		{
			get { return dictionarySize; }
			set { dictionarySize = value; }
		}
		private Size dictionarySize;

        public string DictionaryUserText
        {
            get { return dictionaryUserText; }
            set { dictionaryUserText = value; }
        }
        private string dictionaryUserText;

        public int DictionaryWordSelected
        {
            get { return dictionaryWordSelected; }
            set { dictionaryWordSelected = value; }
        }
        private int dictionaryWordSelected;

		public int DictionaryLanguageIndex
		{
			get { return dictionaryLanguageIndex; }
			set { dictionaryLanguageIndex = value; }
		}
		private int dictionaryLanguageIndex;

        public List<AppStateBookWindow> BookWindows
        {
            get { return bookWindows; }
            set { bookWindows = value; }
        }
        private List<AppStateBookWindow> bookWindows;


        public static void Serialize()
        {
            FileStream fs = new FileStream(fileName, FileMode.Create);

            BinaryFormatter formatter = new BinaryFormatter();
            try
            {
                formatter.Serialize(fs, appState);
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
                appState = (AppState)formatter.Deserialize(fs);
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
