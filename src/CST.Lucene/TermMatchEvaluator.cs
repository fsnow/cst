using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CST
{
    public class TermMatchEvaluator
    {
        public TermMatchEvaluator(string query, bool isRegex)
        {
            regex = null;
            woStar = "";

            if (isRegex)
            {
                queryType = QueryType.Regex;
                string pattern = query;
                if (pattern.StartsWith("^") == false)
                    pattern = "^" + pattern;
                if (pattern.EndsWith("$") == false)
                    pattern += "$";
                regex = new Regex(pattern);
            }

            // contains questions marks or multiple wildcards.
            // replace * with .* and use regex Match class
            else if (query.Contains("?") ||
                (query.IndexOf("*") != query.LastIndexOf("*")))
            {
                queryType = QueryType.Regex;
                string pattern = query.Replace("?", ".{1}");
                pattern = pattern.Replace("*", ".*");
                pattern = "^" + pattern + "$";
                regex = new Regex(pattern);
            }
            else if (query.EndsWith("*"))
            {
                queryType = QueryType.StartsWith;
                woStar = query.Substring(0, query.Length - 1);
            }
            else if (query.StartsWith("*"))
            {
                queryType = QueryType.EndsWith;
                woStar = query.Substring(1);
            }
            else
                queryType = QueryType.Word;

        }

        public QueryType QueryType
        {
            get { return queryType; }
            set { queryType = value; }
        }
        private QueryType queryType;

        private Regex regex;
        private string woStar;

        public bool Evaluate(string term)
        {
            if (queryType == QueryType.Regex)
                return regex.Match(term).Success;
            else if (queryType == QueryType.StartsWith)
                return term.StartsWith(woStar);
            else if (queryType == QueryType.EndsWith)
                return term.EndsWith(woStar);
            else
                return false;
        }
    }

    public enum QueryType
    {
        Regex,
        StartsWith,
        EndsWith,
        Word
    }
}
