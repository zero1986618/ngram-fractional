using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace ngram
{
    internal enum Mode
    {
        BinCounts,
        BinProb
    }
    unsafe class BinaryFile
    {
        private const int Signature = 0x12112112;
        public long[] NGramcounts;
        public long[] ValidNGrams;
        private LMHead _header;
        public readonly List<long> PAccCount = new List<long>();
        private readonly MemoryMappedFile _mmf;//InnerNode(LeafNode):[wid-int prob-float (next-int bowt-float)]
        private readonly MemoryMappedViewAccessor _mmva;
        private readonly byte* _baseptr = (byte*)(new IntPtr(-1));
        private readonly byte* _innerptr;
        private readonly byte* _finalptr;        
        private readonly string _lmbin;
        public Vocab vocab;
        public byte* InnerPtr
        {
            get { return _innerptr; }
        }
        public byte* FinalPtr
        {
            get { return _finalptr; }
        }
        public int Order
        {
            get { return _header.Order; }
        }
        public void Dispose()
        {
            _mmva.SafeMemoryMappedViewHandle.ReleasePointer();
            _mmva.Dispose();
            _mmf.Dispose();
        }
        /*
        * Function:    Generate file in memory-mapping mode based on number of ngrams on each level
        * Author:      Yinggong Zhao(zero1986618@gmail.com)
        * Date:        2012-2-25 17:24
        * Modify:
        *      1. Remove fields of pOffset in memory-mapped file 
         *     2. Change from int to long
         *     3. Move _mmva to class member and add dispose function
        */
        public BinaryFile(string file, long [] ngrams, Mode mode = Mode.BinCounts, int unk = int.MaxValue)
        {
            _lmbin = file;
            NGramcounts = ngrams;
            ValidNGrams = new long[ngrams.Length];
            _header.Order = NGramcounts.Length;

            _header.Signature = Signature;
            int innerNodeSize = mode == Mode.BinCounts ? sizeof (InnerNode) : sizeof (InnerProbNode);
            int leafNodeSize = mode == Mode.BinCounts ? sizeof (LeafNode) : sizeof (LeafProbNode);
            long innerCount = 0;
            for (int i = 0; i < _header.Order - 1; i++)
                innerCount += NGramcounts[i];
            long innerNodeCount = innerCount + NGramcounts.Length - 1;
            long finalNodeCount = NGramcounts[NGramcounts.Length - 1] + 1;
            PAccCount.Add(0);
            for (int i = 1; i < _header.Order; i++)
                PAccCount.Add(PAccCount[i - 1] + NGramcounts[i - 1] + 1);
            long lmsize = sizeof (LMHead) + PAccCount.Count*sizeof (long) + innerNodeCount*innerNodeSize +
                          finalNodeCount*leafNodeSize;
            long innerNodeStartPos = sizeof (LMHead) + PAccCount.Count*sizeof (long);
            long finalNodeStartPos = innerNodeStartPos + innerNodeCount*innerNodeSize;            
            _mmf = MemoryMappedFile.CreateFromFile(_lmbin, FileMode.Create, Util.GenRandStr(6), lmsize);           
            _mmva = _mmf.CreateViewAccessor();
            _mmva.SafeMemoryMappedViewHandle.AcquirePointer(ref _baseptr);

            ((uint*) _baseptr)[0] = _header.Signature;
            ((int*) _baseptr)[1] = _header.Order;
            ((int*) _baseptr)[2] = unk;
            _innerptr = _baseptr + innerNodeStartPos;
            _finalptr = _baseptr + finalNodeStartPos;
            byte* pNgram = _baseptr + sizeof (LMHead);
            for (int i = 0; i < _header.Order; i++)
                ((long*) pNgram)[i] = NGramcounts[i];
        }

        /*
         * Function:    Open file in memory-mapping mode
         * Author:      Yinggong Zhao(zero1986618@gmail.com)
         * Date:        2012-2-25 17:24
         * Modify:
         *      1. remove fields of pOffset in memory-mapped file 
         *      2. move _mmva to class member and add dispose function
         */
        public BinaryFile(string file, Mode mode = Mode.BinCounts)
        {
            _mmf = MemoryMappedFile.CreateFromFile(file, FileMode.Open, Util.GenRandStr(6));
            _mmva = _mmf.CreateViewAccessor();
            _mmva.SafeMemoryMappedViewHandle.AcquirePointer(ref _baseptr);
            int innerNodeSize = mode == Mode.BinCounts ? sizeof(InnerNode) : sizeof(InnerProbNode);
            _header.Order = ((int*) _baseptr)[1];
            byte* pOffset = _baseptr + sizeof (LMHead);
            NGramcounts = new long[_header.Order];
            ValidNGrams = new long[_header.Order];
            for (int i = 0; i < _header.Order; i++)
                NGramcounts[i] = ((long*) pOffset)[i];

            long innerCount = 0;
            for (int i = 0; i < _header.Order - 1; i++)
                innerCount += NGramcounts[i];
            long innerNodeCount = innerCount + NGramcounts.Length - 1;
            PAccCount.Add(0);
            for (int i = 1; i < _header.Order; i++)
                PAccCount.Add(PAccCount[i - 1] + NGramcounts[i - 1] + 1);
            long innerNodeStartPos = sizeof(LMHead) + _header.Order * sizeof(long);//PAccCount.Count*sizeof (int) +
            long finalNodeStartPos = innerNodeStartPos + innerNodeCount*innerNodeSize;
            _innerptr = _baseptr + innerNodeStartPos;
            _finalptr = _baseptr + finalNodeStartPos;
        }

        public int NGramCounts(int level)
        {
            BinLMIter binLMIter = new BinLMIter(this, level);
            int[] keys = new int[level + 1];
            long currPos;
            int validGrams = 0;
            while ((currPos = binLMIter.MoveNextX(ref keys)) >= 0)
            {
                if (level == Order)
                {
                    LeafNode leafNode =
                        ((LeafNode*) FinalPtr)[currPos - PAccCount[level - 1]];
                    if (float.IsNaN(leafNode.Prob))
                        continue;
                    validGrams++;
                }
                else
                {
                    InnerNode innerNode = ((InnerNode*) InnerPtr)[currPos];
                    if (float.IsNaN(innerNode.Prob))
                        continue;
                    validGrams++;
                }
            }
            return validGrams;
        }
        /*
        * Function:    Dump a prob-bin file from count-bin file
        * Author:      Yinggong Zhao(zero1986618@gmail.com)
        * Date:        2012-2-29 16:20
        * Modify:
        *      1. when prob is infinity, output -99 instead        
        */
        public void DumpBinLM(string binfile)
        {
            //long[] counts = new long[Order];
            //for (int level = 1; level <= Order; level++)
            //    counts[level - 1] = NGramCounts(level);
            long[] probAccCounts = new long[Order + 1];
            for (int i = 0; i < Order - 1; i++)
                probAccCounts[i + 1] = probAccCounts[i] + ValidNGrams[i] + 1;
            BinaryFile lmfile = new BinaryFile(binfile, ValidNGrams, Mode.BinProb, vocab.UnkIndex);
            for (int level = 1; level <= Order; level++)
            {
                int currAccPos = 0;
                BinLMIter binLMIter = new BinLMIter(this, level);
                int[] keys = new int[level + 1];
                long currCountsPos;
                long currProbPos = 0;
                if (level < Order)
                    currProbPos = probAccCounts[level - 1];
                while ((currCountsPos = binLMIter.MoveNextX(ref keys)) >= 0)
                {
                    if (level == Order)
                    {
                        LeafNode leafNode =
                            ((LeafNode*) FinalPtr)[currCountsPos - PAccCount[level - 1]];
                        if (float.IsNaN(leafNode.Prob))
                            continue;
                        ((LeafProbNode*) lmfile.FinalPtr)[currProbPos].Prob = leafNode.Prob;
                        ((LeafProbNode*) lmfile.FinalPtr)[currProbPos].Index = leafNode.Index;
                        currProbPos++;
                    }
                    else
                    {
                        InnerNode innerNode = ((InnerNode*) InnerPtr)[currCountsPos];
                        if (float.IsNaN(innerNode.Prob))
                            continue;
                        ((InnerProbNode*) lmfile.InnerPtr)[currProbPos].Prob = float.IsPositiveInfinity(innerNode.Prob)
                                                                                   ? -99
                                                                                   : innerNode.Prob;
                        ((InnerProbNode*) lmfile.InnerPtr)[currProbPos].Bow = innerNode.Bow;
                        ((InnerProbNode*) lmfile.InnerPtr)[currProbPos].Index = innerNode.Index;
                        ((InnerProbNode*) lmfile.InnerPtr)[currProbPos].Child = currAccPos;
                        for (long nextLevelPos = ((InnerNode*) InnerPtr)[currCountsPos].Child;
                             nextLevelPos < ((InnerNode*) InnerPtr)[currCountsPos + 1].Child;
                             nextLevelPos++)
                        {
                            if (level == Order - 1)
                            {
                                float prob = ((LeafNode*) FinalPtr)[nextLevelPos].Prob;
                                if (!float.IsNaN(prob))
                                    currAccPos++;
                            }
                            else
                            {
                                float prob = ((InnerNode*) InnerPtr)[nextLevelPos + PAccCount[level]].Prob;
                                if (!float.IsNaN(prob))
                                    currAccPos++;
                            }
                        }
                        currProbPos++;
                    }
                }
            }
            for (int i = 0; i < Order - 1; i++)
            {
                InnerProbNode innerNode = new InnerProbNode
                                              {
                                                  Index = 0x00ff00ff,
                                                  Prob = 0x00ee00ee,
                                                  Child = ValidNGrams[i + 1]
                                              };
                ((InnerProbNode*) lmfile.InnerPtr)[probAccCounts[i + 1] - 1] = innerNode;
            }
            {
                LeafProbNode leafNode = new LeafProbNode {Index = 0x00ff00ff, Prob = 0x00ee00ee};
                ((LeafProbNode*)lmfile.FinalPtr)[ValidNGrams[ValidNGrams.Length - 1]] = leafNode;
            }
        }

        //here we need to modify the code to accelerate the transformation speed
        public long FindPrefixNode(int[] wids, int order)
        {
            int level = wids.Length;
            long m = 0;
            long low = 0, high = PAccCount[1] - 1;
            long baseAddr = 0;
            for (int i = 0; i < level; i++) //for each level
            {
                baseAddr = PAccCount[i];
                bool flag = false;
                while (low <= high)
                {
                    m = (low + high)/2;
                    if (m < 0)
                        return -1;
                    int ret;
                    if (i == order - 1)
                        ret = ((LeafNode*) _finalptr)[m].Index - wids[i];
                    else
                        ret = ((InnerNode*) _innerptr)[baseAddr + m].Index - wids[i];
                    if (ret == 0)
                    {
                        flag = true;
                        break;
                    }
                    if (ret > 0)
                        high = m - 1;
                    else
                        low = m + 1;
                }
                if (!flag)
                    return -1;
                if (i < level && i < order - 2)
                {
                    low = ((InnerNode*) _innerptr)[baseAddr + m].Child;
                    high = ((InnerNode*) _innerptr)[baseAddr + m + 1].Child - 1;
                }
            }
            return baseAddr + m;
        }

        public void DumpArpaLM(string arpafile)
        {            
            //int[] counts = new int[Order + 1];
            //for (int level = 1; level <= Order; level++)
            //    counts[level] = NGramCounts(level);
            StreamWriter streamWriter = new StreamWriter(arpafile);
            streamWriter.WriteLine();
            streamWriter.WriteLine(@"\data\");
            for (int i = 0; i < ValidNGrams.Length; i++)
                streamWriter.WriteLine("ngram {0}={1}", i + 1, ValidNGrams[i]);
            streamWriter.WriteLine();
            for (int level = 1; level <= Order; level++)
            {
                streamWriter.WriteLine(@"\{0}-grams:", level);
                BinLMIter binLMIter = new BinLMIter(this, level);

                int[] keys = new int[level + 1];
                long currPos;                
                int validGrams = 0;
                int totalGrams = 0;
                int[] context = new int[level];
                while ((currPos = binLMIter.MoveNextX(ref keys)) >= 0)
                {
                    totalGrams++;
                    if (level == Order)
                    {
                        LeafNode innerNode = ((LeafNode*)FinalPtr)[currPos - PAccCount[level - 1]];
                        if (float.IsNaN(innerNode.Prob))
                            continue;
                        validGrams++;
                        for (int i = 0; i < context.Length; i++)
                            context[i] = keys[i];
                        streamWriter.WriteLine(innerNode.Prob + "\t" + string.Join(" ", vocab.GetWords(context)));
                    }
                    else
                    {
                        InnerNode innerNode = ((InnerNode*) InnerPtr)[currPos];
                        if (float.IsNaN(innerNode.Prob))
                            continue;
                        validGrams++;
                        for (int i = 0; i < context.Length; i++)
                            context[i] = keys[i];
                        string ngrams = string.Join(" ", vocab.GetWords(context));
                        float prob = innerNode.Prob;
                        if (innerNode.Bow != 0)
                            streamWriter.WriteLine((float.IsPositiveInfinity(prob) ? -99 : prob) + "\t" +
                                                   ngrams + "\t" + innerNode.Bow);
                        else
                            streamWriter.WriteLine((float.IsPositiveInfinity(prob) ? -99 : prob) + "\t" + ngrams);
                    }
                }
                streamWriter.WriteLine();
            }
            streamWriter.WriteLine(@"\end\");
            streamWriter.Close();
        }
    }
}