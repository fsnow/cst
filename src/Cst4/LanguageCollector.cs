using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace CST
{
    public class CultureInfoDisplayItem {

        public CultureInfoDisplayItem(string displayName, CultureInfo cultureInfo) {
            DisplayName = displayName;
            CultureInfo = cultureInfo;
        }

        public override string ToString() {
            return DisplayName;
        }

        public string DisplayName;

        public readonly CultureInfo CultureInfo;
    }

    
    /// <summary>
	///   Class responsible to collect available localized resources.
	/// </summary>
	public class LanguageCollector 
	{

        public enum LanguageNameDisplay {
            DisplayName,
            EnglishName,
            NativeName
        }

        /// <summary>
        ///   Initializes <c>LanguageCollector</c> object with a list of 
        ///   available localized resources based on subfolders names.
        /// </summary>
        public LanguageCollector()
		{
            m_availableCultureInfos = GetApplicationAvailableCultures();
            Debug.Assert(m_availableCultureInfos != null, "m_availableCultureInfos is null in LanguageCollector()");
        }

        /// <summary>
        ///   Initializes <c>LanguageCollector</c> object with a list of 
        ///   available localized resources based on subfolders names plus
        ///   <c>CultureInfo</c> supplied as a default culture.
        /// </summary>
        /// <param name="defaultCultureInfo">
        ///   Default culure for which application did not create subfolder.
        /// </param>
        public LanguageCollector(CultureInfo defaultCultureInfo) : this() 
		{
            if (!m_availableCultureInfos.Contains(defaultCultureInfo)) {
                m_availableCultureInfos.Add(defaultCultureInfo);
                m_availableCultureInfos.Sort(new CultureInfoComparer());
            }
            Debug.Assert(m_availableCultureInfos != null, "m_availableCultureInfos is null");
        }

        private class CultureInfoComparer : IComparer {

            int IComparer.Compare(object x, object y) {
                return Compare((CultureInfo)x, (CultureInfo)y);
            }

			// FES 2008-02-21: changed the comparison field from Name to NativeName
            public int Compare(CultureInfo cix, CultureInfo ciy) {
                return string.Compare(cix.NativeName, ciy.NativeName);
            }

        }

        /// <summary>
        ///   Returns an array of <c>CultureInfoDisplayItem</c> objects for
        ///   all available localized resources.
        /// </summary>
        /// <param name="languageNameToDisplay">
        ///   <c>LanguageNameDisplay</c> value defining how language will be displayed.
        /// </param>
        /// <param name="currentLanguage">
        ///   Index of currently active UI culture.
        /// </param>
        /// <returns>
        ///   An array of <c>CultureInfoDisplayItem</c> objects, sorted by their 
        ///   names (not <c>DisplayName</c>s).
        /// </returns>
        public CultureInfoDisplayItem[] GetLanguages(LanguageNameDisplay languageNameToDisplay, 
			out int currentLanguage) 
		{
            CultureInfoDisplayItem[] cidi = new CultureInfoDisplayItem[m_availableCultureInfos.Count];
            currentLanguage = -1;
            string currentCulture = System.Threading.Thread.CurrentThread.CurrentUICulture.Name;
            string parentCulture = System.Threading.Thread.CurrentThread.CurrentUICulture.Parent.Name;

			// FES: if the user is running Windows in a language other than English that we do not
			// have a translation for, we have to fall back to English
			int englishLanguage = -1; 

            for (int i = 0; i < m_availableCultureInfos.Count; i++)
			{
                CultureInfo ci = (CultureInfo)m_availableCultureInfos[i];
                string displayName = GetDisplayName(ci, languageNameToDisplay);

				if (ci.Name == "en")
					englishLanguage = i;

                cidi[i] = new CultureInfoDisplayItem(displayName, ci);
                if (currentCulture == ci.Name || (currentLanguage == -1 && parentCulture == ci.Name))
                    currentLanguage = i;
            }

            //Debug.Assert(currentLanguage > -1 && currentLanguage < m_availableCultureInfos.Count, 
				//"test of currentLanguage failed in GetLanguages");

			if ((currentLanguage < 0 || currentLanguage >= m_availableCultureInfos.Count)
				&& englishLanguage >= 0)
			{
				currentLanguage = englishLanguage;
			}

            return cidi;
        }

        private string GetDisplayName(CultureInfo cultureInfo, LanguageNameDisplay languageNameToDisplay) {
            switch (languageNameToDisplay) {
            case LanguageNameDisplay.DisplayName:
                return cultureInfo.DisplayName;
            case LanguageNameDisplay.EnglishName:
                return cultureInfo.EnglishName;
            case LanguageNameDisplay.NativeName:
                return cultureInfo.NativeName;
            }
            Debug.Assert(false, string.Format("Not supported LanguageNameDisplay value {0}", languageNameToDisplay));
            return "";
        }

        private Hashtable GetAllCultures() {
            CultureInfo[] cis = CultureInfo.GetCultures(CultureTypes.AllCultures);
            Hashtable allCultures = new Hashtable(cis.Length);
            foreach (CultureInfo ci in cis) {
                allCultures.Add(ci.Name, ci);
            }
            return allCultures;
        }

        private ArrayList GetApplicationAvailableCultures() {
            ArrayList availableCultures = new ArrayList();
            Hashtable allCultures = GetAllCultures();
            string executableRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            foreach (string directory in Directory.GetDirectories(executableRoot)) {
                string subDirectory = Path.GetFileName(directory);
                CultureInfo ci = (CultureInfo)allCultures[subDirectory];
                if (ci != null) {
                    availableCultures.Add(ci);
                }
            }
            return availableCultures;
        }

        private ArrayList m_availableCultureInfos;
	}
}
