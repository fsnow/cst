﻿using System;
using System.Collections.Generic;
using System.Text;
using CST.Conversion;

namespace CST
{
    public class IpeWordChecker
    {
        public IpeWordChecker()
        {
            MakeValidConjunctHash();
        }

        public IpeWordChecker(string word)
        {
            this.word = word;
            MakeValidConjunctHash();
        }

        private void MakeValidConjunctHash()
        {
            validConjuncts = new HashSet<string>();

            string[] latnConjuncts = {
                "k", "kk", "kkh", "kkhy", "ky", "kl", "kr", "kv",
                "kh", "khy", "khv",
                "g", "gg", "ggh", "gdh", "gy", "gr", "gv",
                "gh",
                "ṅk", "ṅky", "ṅkh", "ṅkhy", "ṅg", "ṅgh",
                "c", "cc", "cch",
                "ch",
                "j", "jj", "jjh",
                "jh",
                "ñ", "ñc", "ñch", "ñj", "ñjh", "ññ", "ñh",
                "ṭ", "ṭṭ", "ṭṭh", "ṭṭhy",
                "ṭh",
                "ḍ", "ḍḍ", "ḍḍh",
                "ḍh",
                "ṇ", "ṇṭ", "ṇṭh", "ṇḍ", "ṇṇ", "ṇy", "ṇh",
                "t", "tt", "tth", "tthy", "tn", "ty", "tr", "tl", "tv",
                "th", "thy",
                "d", "dd", "ddh", "dm", "dy", "dr", "dv", 
                "dh", "dhy", "dhv",
                "n", "nt", "nty", "ntr", "ntv", "nth", "nd", "ndr", "ndh", "ndhy", "nn", "ny", "nv", "nh",
                "p", "pp", "pph", "py", "pr", "pl",
                "ph",
                "b", "bb", "bbh", "by", "br", "bl", "bv",
                "bh", "bhy",
                "m", "mp", "mph", "mb", "mbh", "mm", "my", "mh",
                "y", "yy", "yv", "yh",
                "r", "rr",
                "l", "ly", "ll",
                "v", "vy", "vv", "vh",
                "s", "st", "sth", "sn", "sp", "sm", "sy", "sv", "ss",
                "h", "hm", "hy", "hv",
                "ḷ", "ḷv", "ḷh"
            };

            foreach (string conj in latnConjuncts)
            {
                validConjuncts.Add(Latn2Ipe.Convert(conj)); 
            }

            validWords = new HashSet<string>();

            string[] latnWords = {
                "ṅa"
            };

            foreach (string word in latnWords)
            {
                validWords.Add(Latn2Ipe.Convert(word));
            }
        }

        private ISet<string> validConjuncts;
        private ISet<string> validWords;

        public bool IsBad()
        {
            if (validWords.Contains(word))
                return false;

            return HasNSequentialVowels(3) ||
                StartsWithTwoVowels() ||
                EndsWithTwoVowels() ||
                EndsWithConsonant() ||
                StartWithNiggahita() ||
                HasNiggahitaAfterConsonant() ||
                HasNSequentialNiggahitas(2) ||
                HasInvalidConsonantConjuncts();
        }

        public bool HasNSequentialVowels(int n)
        {
            int vowelCount = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (IsVowel(word[i]))
                {
                    vowelCount++;
                    if (vowelCount >= n)
                        return true;
                }
                else
                    vowelCount = 0;
            }

            return false;
        }

        // superceded by HasInvalidConjuncts()
        public bool HasNSequentialConsonants(int n)
        {
            int consCount = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (IsConsonant(word[i]))
                {
                    consCount++;
                    if (consCount >= n)
                        return true;
                }
                else
                    consCount = 0;
            }

            return false;
        }

        public bool HasNSequentialNiggahitas(int n)
        {
            int nCount = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (IsNiggahita(word[i]))
                {
                    nCount++;
                    if (nCount >= n)
                        return true;
                }
                else
                    nCount = 0;
            }

            return false;
        }

        public bool HasInvalidConsonantConjuncts()
        {
            string conj = "";
            for (int i = 0; i < word.Length; i++)
            {
                if (IsConsonant(word[i]))
                    conj += word[i];
                else
                {
                    if (conj.Length > 0 && IsInvalidConjunct(conj))
                        return true;
                    conj = "";
                }
            }
            if (conj.Length > 0 && IsInvalidConjunct(conj))
                return true;

            return false;
        }

        public bool StartsWithTwoVowels()
        {
            if (word.Length >= 2 &&
                IsVowel(word[0]) &&
                IsVowel(word[1]))
            {
                return true;
            }

            return false;
        }

        public bool EndsWithTwoVowels()
        {
            if (word.Length >= 2 &&
                IsVowel(word[word.Length - 1]) &&
                IsVowel(word[word.Length - 2]))
            {
                return true;
            }

            return false;
        }

        public bool EndsWithConsonant()
        {
            if (word.Length > 0 &&
                IsConsonant(word[word.Length - 1]))
            {
                return true;
            }

            return false;
        }

        private bool IsVowel(char c)
        {
            int ccode = System.Convert.ToInt32(c);
            if (ccode >= 0xC1 && ccode <= 0xC8)
                return true;

            return false;
        }

        public bool StartWithNiggahita()
        {
            if (word.Length > 0 &&
                IsNiggahita(word[0]))
            {
                return true;
            }

            return false;
        }

        public bool HasNiggahitaAfterConsonant()
        {
            int index = 0;
            char niggahita = Convert.ToChar(0xC0);

            while (index >= 0)
            {
                index = word.IndexOf(niggahita, index);
                if (index != -1)
                {
                    if (index > 0 && IsConsonant(word[index - 1]))
                        return true;

                    index++;
                }
            }

            return false;
        }

        private bool IsConsonant(char c)
        {
            int ccode = System.Convert.ToInt32(c);
            if (ccode >= 0xC9 && ccode <= 0xE9 && ccode != 0xD7)
                return true;

            return false;
        }

        private bool IsNiggahita(char c)
        {
            int ccode = System.Convert.ToInt32(c);
            return (ccode == 0xC0);
        }

        private bool IsInvalidConjunct(string conj)
        {
            return (validConjuncts.Contains(conj) == false);
        }

        public string Word
        {
            get { return word; }
            set { word = value; }
        }
        private string word;

    }
}
