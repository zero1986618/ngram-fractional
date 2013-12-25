using System;
using System.Collections;
using System.IO;

namespace ngram
{
    internal class CountsBinMaker
    {
        public readonly long[][] CountsOfCounts;
        public readonly double[][] FracCountsOfCounts;
        private string _text;
        private string _weight;
        private int order;
        public Vocab Vocab;
        public CountsBinMaker(string text, int _order, string weight="")
        {
            int mknCount = 5;
            _text = text;
            order = _order;
            _weight = weight;
            CountsOfCounts = new long[order][];
            FracCountsOfCounts = new double[order][];
            for (int i = 0; i < order; i++)
            {
                FracCountsOfCounts[i] = new double[Math.Max(mknCount, Discount.gtmax[i + 1] + 2)];
                CountsOfCounts[i] = new long[Math.Max(mknCount, Discount.gtmax[i + 1] + 2)];
            }
        }
        public void GetVocab()
        {
            //index 文件会纪录每个句子中valid的n-gram的起始地址
            Vocab = new Vocab();
            StreamReader sr = new StreamReader(_text);
            int lc = 0;
            while (true)
            {
                string line = sr.ReadLine();
                if (line == null)
                    break;
                string[] words = line.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                if(!reverse)
                {
                    int[] wids = Vocab.AddWords(words);
                }
                else
                {
                    for (int i = 0; i < words.Length; i++)
                        Vocab.AddWord(words[words.Length - 1 - i]);
                }
                lc++;
                Vocab.WordCounts += words.Length + 2;
                if (lc%1000 == 0)
                    Console.Write("\rLine " + lc);
            }
            sr.Close();
            Console.Write("\rLine " + lc);
            Console.WriteLine();
            Vocab.LineNums = lc;
        }

        private static bool reverse = LMConfig.GetOption("reverse", false);
        public void MakeCountsBin()
        {
            GetVocab();
            HugeArray<int> warray = new HugeArray<int>(Vocab.WordCounts + (Vocab.LineNums + 1)*Math.Max(0, order - 1));
            StreamReader sweight = null;
            HugeArray<float> weightarray = null;
            if (File.Exists(_weight))
            {
                sweight = new StreamReader(_weight);
                weightarray = new HugeArray<float>(Vocab.WordCounts + (Vocab.LineNums + 1)*Math.Max(0, order - 1));
            }
            BitArray maskarray = new BitArray(Vocab.WordCounts + (Vocab.LineNums + 1)*Math.Max(0, order - 1));
            int linecount = 0;
            int wordcount = 0;
            int wwcount = 0;
            int maskcount = 0;
            float sentweight = LMConfig.GetOption("sentenceweight", 1.0f);
            if (sweight != null)
                sentweight = float.Parse(sweight.ReadLine());
            for (int i = 0; i < order - 1; i++)
            {
                warray[wordcount++] = reverse ? Vocab.EOSIndex : Vocab.BOSIndex;
                maskarray[maskcount++] = false;
                if (sweight != null)
                    weightarray[wwcount++] = sentweight;
            }

            StreamReader sr = new StreamReader(_text);
            while (true)
            {
                string line = sr.ReadLine();
                if (line == null)
                    break;
                linecount++;
                string[] words = line.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
                int[] wids = new int[words.Length + 2];
                int[] getwids = Vocab.GetIndexs(words);
                for (int i = 0; i < getwids.Length; i++)
                {
                    wids[i + 1] = getwids[i];
                    if (reverse)
                        wids[i + 1] = getwids[getwids.Length - 1 - i];
                }
                wids[0] = Vocab.BOSIndex;
                wids[wids.Length - 1] = Vocab.EOSIndex;
                if (reverse)
                {
                    wids[0] = Vocab.EOSIndex;
                    wids[wids.Length - 1] = Vocab.BOSIndex;
                }
                foreach (int t in wids)
                {
                    warray[wordcount++] = t;
                    if (sweight != null)
                        weightarray[wwcount++] = sentweight;
                    maskarray[maskcount++] = true;
                }
                for (int i = 0; i < order - 1; i++)
                {
                    warray[wordcount++] = reverse ? Vocab.BOSIndex : Vocab.EOSIndex;
                    if (sweight != null)
                        weightarray[wwcount++] = sentweight;
                    maskarray[maskcount++] = false;
                }
                if (sweight != null)
                {
                    line = sweight.ReadLine();
                    if (line != null)
                        sentweight = float.Parse(line);
                }
                if (linecount%10000 == 0)
                    Console.Write("\rLine " + linecount);
            }
            sr.Close();
            if (sweight != null)
                sweight.Close();
            Console.Write("\rLine " + linecount);
            Console.WriteLine();
            Console.WriteLine(wordcount - warray.Length);
            SuffixSortWithPrepareCounts(warray, maskarray, Vocab.Word2Index.Count, weightarray, _needPrepareCounts);
        }

        private bool _useCutoff = LMConfig.GetOption("useCutoff", false);
        private readonly bool _needPrepareCounts = LMConfig.GetOption("needPrepareCounts", true);
        public static int CompareList(int[] list1, int[] list2)
        {
            if (list1 == null || list2 == null || list1.Length != list2.Length)
                throw new Exception("List null or length not match!");
            for (int i = 0; i < list1.Length - 2; i++)
                if (list1[i] != list2[i])
                    return list1[1] - list2[i];
            return 0;
        }
        public static long TotalCount = 0;
        private void SuffixSortWithPrepareCounts(HugeArray<int> wa, BitArray bitArray, int vocabSize, HugeArray<float> ww = null, bool needPrepareCounts = false)
        {
            float sentweight = LMConfig.GetOption("sentenceweight", 1.0f);
            bool applyFracMKNSmoothing = LMConfig.GetOption("applyFracMKNSmoothing", false);
            string smoothing = LMConfig.GetOption("smoothing");
            if (smoothing == "gt")
                needPrepareCounts = false;
            if (smoothing == "mkn" || smoothing == "kn")
                _useCutoff = false;
            int[] ngrams = new int[order];
            int len = wa.Length - order + 1;
            int[] ccount = new int[vocabSize];
            int[] pcount = new int[vocabSize + 1];
            HugeArray<int> sindex = new HugeArray<int>(len);
            HugeArray<int> pindex = new HugeArray<int>(len);
            for (int i = 0; i < sindex.Length; i++)
                sindex[i] = i;
            for (int i = 1; i < len; i++)
                ccount[wa[i + order - 1]]++;
            //这部分是suffix-sort
            for (int k = 0; k < order; k++)
            {
                DateTime dateTime = DateTime.Now;
                for (int i = 0; i < sindex.Length; i++)
                    pindex[i] = sindex[i];
                for (int i = 0; i < pcount.Length; i++)
                    pcount[i] = 0;
                ccount[wa[0 + order - k - 1]]++;
                if (k != 0)
                    ccount[wa[len + order - k - 1]]--;
                for (int i = 1; i < ccount.Length + 1; i++)
                    pcount[i] = pcount[i - 1] + ccount[i - 1];
                for (int i = 0; i < len; i++)
                {
                    int index = pindex[i] + order - k - 1;
                    sindex[pcount[wa[index]]] = pindex[i];
                    pcount[wa[index]]++;
                }
                Console.WriteLine("\t {0} seconds", Util.TimeDiff(DateTime.Now, dateTime));
            }

            //输出sorted的ngram和count
            {
                int[] uniqCount = new int[order - 1];
                int[] normCount = new int[order];
                FracType[] fracNormCount = new FracType[order];
                for (int i = 0; i < fracNormCount.Length; i++)
                    fracNormCount[i] = new FracType();
                BitArray[] appearArrays = new BitArray[order - 1];
                for (int i = 0; i < appearArrays.Length; i++)
                    appearArrays[i] = new BitArray(vocabSize + 1);
                FracType[][] fracTypeAppearArrays = new FracType[order - 1][];
                for (int i = 0; i < fracTypeAppearArrays.Length; i++)
                {
                    fracTypeAppearArrays[i] = new FracType[vocabSize + 1];
                    for (int j = 0; j < vocabSize + 1; j++)
                        fracTypeAppearArrays[i][j] = new FracType();
                }
                bool initial = true;
                int[] currgram = new int[order + 1];
                int[] prevrgram = new int[order + 1];
                int prevValidPos = 0;
                bool finalAppend = false;
                for (int i = 0; i < sindex.Length || !finalAppend; i++)
                {
                    float localweight = sentweight;
                    if (ww != null && i > 0 && i < sindex.Length && sindex[i] < ww.Length)
                        localweight = ww[sindex[i]];
                    if (i >= sindex.Length)
                    {
                        finalAppend = true;
                        for (int j = 0; j < order; j++)
                            currgram[j] = int.MaxValue;
                    }
                    if (finalAppend || bitArray[sindex[i]])
                    {
                        int currValidPos = 0;
                        if (!finalAppend)
                        {
                            for (int j = sindex[i]; j < sindex[i] + order; j++)
                            {
                                if (bitArray[j])
                                    currValidPos++;
                                currgram[j - sindex[i]] = wa[j];
                            }
                        }
                        currgram[order] = 1;
                        if (initial)
                        {
                            for (int j = 0; j < currgram.Length; j++)
                                prevrgram[j] = currgram[j];
                            for (int j = 0; j < currValidPos; j++)
                            {
                                normCount[j] = 1; //each n-gram appear once                                
                                fracNormCount[j].UpdateLog(localweight);
                            }
                            initial = false;
                            prevValidPos = currValidPos;
                            continue;
                        }
                        {
                            int matchIndex = 0;
                            int endPosition = Math.Min(currgram.Length - 1, prevValidPos);
                            for (int j = 0; j < endPosition; j++)
                                if (currgram[j] != prevrgram[j])
                                    break;
                                else
                                    matchIndex++;
                            for (int j = 0; j < matchIndex; j++)
                            {
                                normCount[j]++;                               
                                fracNormCount[j].UpdateLog(localweight);
                                int prevIndx = sindex[i] - 1;
                                //need add by one
                                if (needPrepareCounts && j != order - 1 && wa[sindex[i]] != Vocab.BOSIndex)
                                {
                                    if (!appearArrays[j][wa[prevIndx]])
                                    {
                                        uniqCount[j]++;
                                        appearArrays[j][wa[prevIndx]] = true;
                                    }
                                    fracTypeAppearArrays[j][wa[prevIndx]].UpdateLog(localweight);
                                }
                            }
                            for (int j = matchIndex; j < endPosition; j++) //need dump @ Math.Min(order, prevValidPos)
                            {
                                int ngramCount = !needPrepareCounts ||
                                                 prevrgram[0] == Vocab.BOSIndex || j == order - 1
                                    ? normCount[j]
                                    : uniqCount[j];

                                if (!Vocab.IsNonEvent(prevrgram[j]) && j == 0)
                                    TotalCount += ngramCount;
                                ngrams[j]++;
                                if (ngramCount < CountsOfCounts[j].Length)
                                    CountsOfCounts[j][ngramCount]++;
                                normCount[j] = 0;
                                //need backward to clear the bitarray[j]
                                if (needPrepareCounts && j < order - 1 && prevrgram[0] != Vocab.BOSIndex)
                                {
                                    int preCount = 0;
                                    int preIndex = 0;
                                    FracType ft = new FracType();
                                    while (true)
                                    {
                                        int x = i - preIndex - 1;
                                        int rprevIndx = sindex[x] - 1;
                                        if (bitArray[sindex[x]] && bitArray[sindex[x] + j] &&
                                            appearArrays[j][wa[rprevIndx]])
                                        {
                                            preCount++;
                                            double xcount = 1 - Math.Exp(fracTypeAppearArrays[j][wa[rprevIndx]][0]);
                                            fracTypeAppearArrays[j][wa[rprevIndx]].ResetToLog();
                                            ft.UpdateLog(xcount);
                                            appearArrays[j][wa[rprevIndx]] = false;
                                        }
                                        preIndex++;
                                        if (preCount >= uniqCount[j])
                                            break;
                                    }
                                    ft.ChangeLogToReal();
                                    for (int k = 1; k <= 5; k++)
                                        FracCountsOfCounts[j][k - 1] += ft[k - 1];
                                }
                                else
                                {
                                    fracNormCount[j].ChangeLogToReal();
                                    for (int k = 1; k <= 5; k++)
                                        FracCountsOfCounts[j][k - 1] += fracNormCount[j][k - 1];
                                }
                                fracNormCount[j].ResetToLog();
                            }
                            for (int j = matchIndex; j < Math.Min(order, currValidPos); j++) //need dump
                            {
                                normCount[j] = 1;
                                fracNormCount[j].UpdateLog(localweight);
                            }
                            for (int j = matchIndex; j < Math.Min(order - 1, currValidPos); j++) //need dump
                            {
                                uniqCount[j] = 1;
                                int prevIndx = sindex[i] - 1;
                                appearArrays[j][wa[prevIndx]] = true;
                                fracTypeAppearArrays[j][wa[prevIndx]].UpdateLog(localweight);
                            }
                            for (int j = matchIndex; j < currgram.Length; j++)
                                prevrgram[j] = currgram[j];
                            prevValidPos = currValidPos;
                        }
                    }
                }
            }
            Discount[] discounts = new Discount[order];
            {
                string method = LMConfig.GetOption("smoothing");
                bool interpolate = LMConfig.GetOption("interpolate", true);
                bool countsAreModified = LMConfig.GetOption("countsAreModified", false);
                bool prepareCountsAtEnd = LMConfig.GetOption("prepareCountsAtEnd", false);
                for (int i = 1; i <= order; i++)
                { 
                    discounts[i - 1] = Discount.GetDiscount(method, i, countsAreModified, prepareCountsAtEnd);
                    discounts[i - 1].Interpolate = interpolate;
                    discounts[i - 1].Estimate(FracCountsOfCounts[i - 1], i);
                }
                Console.WriteLine();
            }

            {
                int[] uniqCount = new int[order - 1];
                int[] normCount = new int[order];
                float[] fnormCount = new float[order];
                FracType[] fracNormCount = new FracType[order];
                for (int i = 0; i < fracNormCount.Length; i++)
                    fracNormCount[i] = new FracType();
                BinaryWriter[] binaryWriters = new BinaryWriter[order];
                BitArray[] appearArrays = new BitArray[order - 1];
                for (int i = 0; i < appearArrays.Length; i++)
                    appearArrays[i] = new BitArray(vocabSize + 1);
                FracType[][] fracTypeAppearArrays = new FracType[order - 1][];
                for (int i = 0; i < fracTypeAppearArrays.Length; i++)
                {
                    fracTypeAppearArrays[i] = new FracType[vocabSize + 1];
                    for (int j = 0; j < vocabSize + 1; j++)
                        fracTypeAppearArrays[i][j] = new FracType();
                }

                for (int i = 0; i < order; i++)
                    binaryWriters[i] =
                        new BinaryWriter(new FileStream(smoothing + "." + (i + 1) + "gram.bin", FileMode.Create));
                bool initial = true;
                int[] currgram = new int[order + 1];
                int[] prevrgram = new int[order + 1];
                if (Vocab.UnkIsWord)
                {
                    binaryWriters[0].Write(prevrgram[0]);
                    binaryWriters[0].Write(prevrgram[1]);
                    if (applyFracMKNSmoothing)
                        binaryWriters[0].Write(prevrgram[1]);
                }
                int prevValidPos = 0;
                bool finalAppend = false;
                for (int i = 0; i < sindex.Length || !finalAppend; i++)
                {
                    float localweight = sentweight;
                    if (ww != null && i > 0 && i < sindex.Length && sindex[i] < ww.Length)
                        localweight = ww[sindex[i]];
                    if (i >= sindex.Length)
                    {
                        finalAppend = true;
                        for (int j = 0; j < order; j++)
                            currgram[j] = int.MaxValue;
                    }

                    if (finalAppend || bitArray[sindex[i]])
                    {
                        int currValidPos = 0;
                        if (!finalAppend)
                        {
                            for (int j = sindex[i]; j < sindex[i] + order; j++)
                            {
                                if (bitArray[j])
                                    currValidPos++;
                                currgram[j - sindex[i]] = wa[j];
                            }
                        }
                        currgram[order] = 1;
                        if (initial)
                        {
                            for (int j = 0; j < currgram.Length; j++)
                                prevrgram[j] = currgram[j];
                            for (int j = 0; j < currValidPos; j++)
                            {
                                normCount[j] = 1; //each n-gram appear once
                                fracNormCount[j].UpdateLog(localweight);
                                fnormCount[j] = localweight;
                            }
                            initial = false;
                            prevValidPos = currValidPos;
                            continue;
                        }
                        {
                            int matchIndex = 0;
                            int endPosition = Math.Min(currgram.Length - 1, prevValidPos);
                            for (int j = 0; j < endPosition; j++)
                                if (currgram[j] != prevrgram[j])
                                    break;
                                else
                                    matchIndex++;
                            for (int j = 0; j < matchIndex; j++)
                            {
                                normCount[j]++;
                                fnormCount[j] += localweight;
                                fracNormCount[j].UpdateLog(localweight);
                                int prevIndx = sindex[i] - 1;
                                //need add by one
                                if (needPrepareCounts && j != order - 1 && wa[sindex[i]] != Vocab.BOSIndex)
                                {
                                    if (!appearArrays[j][wa[prevIndx]])
                                    {
                                        uniqCount[j]++;
                                        appearArrays[j][wa[prevIndx]] = true;
                                    }
                                    fracTypeAppearArrays[j][wa[prevIndx]].UpdateLog(localweight);
                                }
                            }
                            for (int j = matchIndex; j < endPosition; j++) //need dump @ Math.Min(order, prevValidPos)
                            {
                                int ngramCount = !needPrepareCounts ||
                                                 prevrgram[0] == Vocab.BOSIndex || j == order - 1
                                    ? normCount[j]
                                    : uniqCount[j];
                                if (!Vocab.IsNonEvent(prevrgram[j]) && j == 0)
                                    TotalCount += ngramCount;
                                double fractCount = 0; //need dump here
                                double mass;                             
                                //need backward to clear the bitarray[j]
                                if (needPrepareCounts && j < order - 1 && prevrgram[0] != Vocab.BOSIndex)
                                {
                                    int preCount = 0;
                                    int preIndex = 0;
                                    FracType ft = new FracType();
                                    while (true)
                                    {
                                        int x = i - preIndex - 1;
                                        int rprevIndx = sindex[x] - 1;
                                        if (bitArray[sindex[x]] && bitArray[sindex[x] + j] &&
                                            appearArrays[j][wa[rprevIndx]])
                                        {
                                            preCount++;
                                            double xcount = 1 - Math.Exp(fracTypeAppearArrays[j][wa[rprevIndx]][0]);
                                            fracTypeAppearArrays[j][wa[rprevIndx]].ResetToLog();
                                            ft.UpdateLog(xcount);
                                            fractCount += xcount;
                                            appearArrays[j][wa[rprevIndx]] = false;
                                        }
                                        preIndex++;
                                        if (preCount >= uniqCount[j])
                                            break;
                                    }
                                    ft.ChangeLogToReal();
                                    mass = discounts[j].DiscountMass(ft, fractCount);
                                    for (int k = 1; k <= 5; k++)
                                        FracCountsOfCounts[j][k - 1] += ft[k - 1];
                                }
                                else
                                {
                                    fractCount = fnormCount[j];
                                    fracNormCount[j].ChangeLogToReal();
                                    mass = discounts[j].DiscountMass(fracNormCount[j], fractCount);
                                    for (int k = 1; k <= 5; k++)
                                        FracCountsOfCounts[j][k - 1] += fracNormCount[j][k - 1];
                                }

                                fracNormCount[j].ResetToLog();
                                //here we need to output mass and frac-count
                                if (!_useCutoff || ngramCount >= Discount.gtmin[j + 1])
                                {
                                    for (int l = 0; l <= j; l++)
                                        binaryWriters[j].Write(prevrgram[l]);
                                    if (applyFracMKNSmoothing)
                                    {
                                        binaryWriters[j].Write((float) fractCount);
                                        binaryWriters[j].Write((float) mass);
                                    }
                                    else
                                        binaryWriters[j].Write(ngramCount);
                                }

                                normCount[j] = 0;
                                fnormCount[j] = 0;
                            }
                            for (int j = matchIndex; j < Math.Min(order, currValidPos); j++) //need dump
                            {
                                normCount[j] = 1;
                                fracNormCount[j].UpdateLog(localweight);
                                fnormCount[j] = localweight;
                            }
                            for (int j = matchIndex; j < Math.Min(order - 1, currValidPos); j++) //need dump
                            {
                                uniqCount[j] = 1;
                                int prevIndx = sindex[i] - 1;
                                appearArrays[j][wa[prevIndx]] = true;
                                fracTypeAppearArrays[j][wa[prevIndx]].UpdateLog(localweight);
                            }
                            for (int j = matchIndex; j < currgram.Length; j++)
                                prevrgram[j] = currgram[j];
                            prevValidPos = currValidPos;
                        }
                    }
                }
                Console.WriteLine("Number of n-grams:\t{0}", string.Join(" ", ngrams));
                foreach (BinaryWriter binaryWriter in binaryWriters)
                    binaryWriter.Close();
            }
        }
    }
}