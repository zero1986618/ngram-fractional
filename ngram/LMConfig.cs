using System;
using System.Collections.Generic;
using System.Configuration;
using System.Text;
using System.Text.RegularExpressions;

namespace ngram
{
    class LMConfig
    {
        private static readonly Dictionary<string, string> OptDict = new Dictionary<string, string>();
        public static void SetArgs(string[] argList)
        {
            foreach (string key in ConfigurationManager.AppSettings.Keys)
                if (!OptDict.ContainsKey(key.ToLower()))
                    OptDict.Add(key.ToLower(), ConfigurationManager.AppSettings[key]);
                else OptDict[key.ToLower()] = ConfigurationManager.AppSettings[key];
            if (argList != null)
                for (int i = 0; i < argList.Length; i++)
                {
                    if (argList[i].Length <= 1 || argList[i][0] != '-')
                        continue;
                    if ((i + 1 < argList.Length && argList[i].StartsWith("-")) || i + 1 >= argList.Length)
                    {
                        StringBuilder optVal = new StringBuilder("true");
                        string optKey = argList[i].Substring(1).ToLower();
                        if (i + 1 < argList.Length && !argList[i + 1].StartsWith("-"))
                        {
                            optVal.Clear();
                            for (int j = i + 1; j < argList.Length && !argList[j].StartsWith("-"); j++)
                                optVal.Append(argList[j].Trim() + " ");
                        }
                        string soptVal = optVal.ToString().Trim();
                        soptVal = Regex.Replace(soptVal, "^\"", "");
                        soptVal = Regex.Replace(soptVal, "\"$", "");
                        if (!OptDict.ContainsKey(optKey))
                            OptDict.Add(optKey, soptVal);
                        else OptDict[optKey] = soptVal;
                    }
                }
        }

        public static string GetOption(string optKey)
        {
            string optVal = null;
            string lowOptKey = optKey.ToLower();
            if (OptDict.ContainsKey(lowOptKey))
                optVal = OptDict[lowOptKey];
            return optVal;
        }

        public static T GetOption<T>(string optKey, T defaultValue)
        {
            string optVal = GetOption(optKey);
            if (optVal == null)
                return defaultValue;
            return ChangeType<T>(optVal);
        }

        static T ChangeType<T>(string optKey)
        {
            if (Type.GetTypeCode(typeof(T)) == TypeCode.Boolean)
                optKey = optKey == "0" || optKey.ToLower() == "false" || optKey.ToLower() == "no" ? "false" : "true";
            return (T)Convert.ChangeType(optKey, typeof(T));
        }

        public static T[] GetOptionList<T>(string key)
        {
            string v = GetOption(key);
            if (v == null)
                return null;
            string[] tokens = v.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            T[] tvect = Array.ConvertAll(tokens, ChangeType<T>);
            return tvect;
        }
    }
}