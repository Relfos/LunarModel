using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Pluralize.NET;

namespace LunarModel
{
    public static class Utils
    {
        public static string CapUpper(this string s)
        {
            return char.ToUpper(s[0]) + s.Substring(1);
        }
        public static string CapLower(this string s)
        {
            return char.ToLower(s[0]) + s.Substring(1);
        }

        private static IPluralize pluralizer = new Pluralizer();

        public static string Pluralize(this string value)
        {           
            return pluralizer.Pluralize(value);
        }
    }
}
