using System;
using System.Collections.Generic;
using System.IO;
namespace ngram
{
    class Vocab
    {
        public int LineNums;
        public int WordCounts;
        readonly Dictionary<string, int> _word2Index;
        readonly List<string> _index2Word;
        const string VocabUnknown = "<unk>";
        const string VocabSentStart = "<s>";
        const string VocabSentEnd = "</s>";
        public Dictionary<string, int> Word2Index
        {
            get { return _word2Index; }
        }
        public Vocab()
        {
            _word2Index = new Dictionary<string, int>();
            _index2Word = new List<string>();
            ToLower = false;
            _unkIndex = AddWord(VocabUnknown);
            if(!reverse)
            {
                _bosIndex = AddWord(VocabSentStart);
                _eosIndex = AddWord(VocabSentEnd);
            }
            else
            {
                _eosIndex = AddWord(VocabSentEnd);
                _bosIndex = AddWord(VocabSentStart);
            }

            AddNonEvent(!reverse ? _bosIndex : _eosIndex);
        }

        private static bool reverse = LMConfig.GetOption("reverse", false);
        public Vocab(string vfile)
        {
            _word2Index = new Dictionary<string, int>();
            _index2Word = new List<string>();
            
            ToLower = false;
            StreamReader vreader = new StreamReader(vfile);
            int wordcount = 0;
            while (true)
            {
                string line = vreader.ReadLine();
                if (line == null)
                    break;
                _index2Word.Add(line);
                _word2Index.Add(line, wordcount);
                wordcount++;
            }
            vreader.Close();
            _unkIndex = _word2Index.ContainsKey("<unk>") ? _word2Index["<unk>"] : int.MaxValue;
            _bosIndex = _word2Index.ContainsKey("<s>") ? _word2Index["<s>"] : int.MaxValue;
            _eosIndex = _word2Index.ContainsKey("</s>") ? _word2Index["</s>"] : int.MaxValue;
            AddNonEvent(!reverse ? _bosIndex : _eosIndex);
        }

        public void GetVocab(string text)
        {
            StreamReader sr = new StreamReader(text);
            int lc = 0;
            while (true)
            {
                string line = sr.ReadLine();
                if (line == null)
                    break;
                string[] words = line.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if (!reverse)
                {
                    int[] wids = AddWords(words);
                }
                else
                {
                    for (int i = 0; i < words.Length; i++)
                        AddWord(words[words.Length - 1 - i]);
                }
                lc++;
                WordCounts += words.Length + 2;
                if (lc%10000 == 0)
                    Console.Write("\rLine " + lc);
            }
            Console.Write("\rLine " + lc);
            Console.WriteLine();
            LineNums = lc;
            sr.Close();
        }
       
        public void Dump(string vfile)
        {
            StreamWriter vsw = new StreamWriter(vfile);
            vsw.WriteLine(string.Join("\n", _index2Word));
            vsw.Close();
        }

        public int[] AddWords(string[] words)
        {
            int[] indexs = new int[words.Length];
            for (int i = 0; i < words.Length; i++)
                indexs[i] = AddWord(words[i]);
            return indexs;
        }

        public int AddWord(string word)
        {
            if (ToLower)
                word = word.ToLower();
            if (_word2Index.ContainsKey(word))
                return _word2Index[word];
            int index = _word2Index.Count;
            _word2Index.Add(word, index);
            _index2Word.Add(word);
            return index;
        }

        // declare word to be a non-event
        void AddNonEvent(int word)
        {
            _nonEventMap.Add(word);
        }

        readonly HashSet<int> _nonEventMap = new HashSet<int>();      
        public string GetWords(int wids)
        {
            return _index2Word[wids];
        }
        public string[] GetWords(int[] wids)
        {
            string[] words = new string[wids.Length];
            for (int i = 0; i < wids.Length; i++)
                words[i] = _index2Word[wids[i]];
            return words;
        }
        public int GetIndex(string word)
        {
            return _word2Index.ContainsKey(word) ? _word2Index[word] : _word2Index[VocabUnknown];
        }

        public int[] GetIndexs(string[] words)
        {
            int[] indexs = new int[words.Length];
            for (int i = 0; i < words.Length; i++)
                indexs[i] = GetIndex(words[i]);
            return indexs;
        }

        public bool IsNonEvent(int word)	// non-event?
        {
            return (!UnkIsWord && (word == _unkIndex)) || _nonEventMap.Contains(word);
        }

        readonly int _unkIndex;		// <unk> index
        readonly int _bosIndex;		// <s> index
        readonly int _eosIndex;		// </s> index
        public int BOSIndex { get { return _bosIndex; } }
        public int EOSIndex { get { return _eosIndex; } }
        public int UnkIndex { get { return _unkIndex; } }
        public bool UnkIsWord = LMConfig.GetOption("unk", false);// consider <unk> a regular word, default false
        public bool ToLower = LMConfig.GetOption("toLower", false);			// map word strings to lowercase
    }
}