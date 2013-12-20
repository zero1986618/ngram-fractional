using System;
using System.Linq;

namespace ngram
{
    unsafe class bLM
    {
        private readonly Vocab _vocab;
        private readonly BinaryFile _binFile;
        private readonly int _order;
        public bLM(Vocab vocab, BinaryFile binaryFile)
        {
            _vocab = vocab;
            _binFile = binaryFile;
            _order = binaryFile.Order;
        }

        public float CalProb(int[] wid)
        {
            return CalProb(0, wid.Length, wid);
        }

        public float CalProb(int start, int end, int[] wids)
        {
            int order = end - start;
            int wid = wids[start];
            if (!_vocab.UnkIsWord)
                wid = wids[start] - 1;
            if (wid < 0 || wid >= _binFile.NGramcounts[0])
                return order == 1 ? (float)Math.Log(0.0001) : CalProb(start + 1, end, wids);
            long pos = wid;
            if (order >= 2)
            {
                for (int level = 1; level <= order - 2; level++) // from bigram to n-1 gram
                {
                    pos = IndexSearch(((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[level - 1] + pos].Child,
                                      ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[level - 1] + pos + 1].Child - 1,
                                      wids[start + level],
                                      level);
                    if (pos < 0) // no back-off
                        return CalProb(start + 1, end, wids);
                }
                float bowt = ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[order - 2] + pos].Bow;
                //check on final level here
                pos = IndexSearch(((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[order - 2] + pos].Child,
                                  ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[order - 2] + pos + 1].Child - 1,
                                  wids[start + order - 1],
                                  order - 1);
                if (pos < 0)// || float.IsNaN(((InnerNode*) binFile.InnerPtr)[pos].Prob))
                    return bowt + CalProb(start + 1, end, wids);
            }

            return order < _binFile.Order
                       ? ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[order - 1] + pos].Prob
                       : ((LeafNode*)_binFile.FinalPtr)[pos].Prob;
        }

        private long IndexSearch(long low, long high, int wid, int level)
        {
            while (low <= high)
            {
                long mid = (low + high) / 2;
                int remain;
                if (level == _binFile.Order - 1)
                    remain = ((LeafNode*)_binFile.FinalPtr)[mid].Index - wid;
                else
                    remain = ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[level] + mid].Index - wid;
                if (remain == 0)
                {
                    float prob = level == _binFile.Order - 1
                                     ? ((LeafNode*)_binFile.FinalPtr)[mid].Prob
                                     : ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[level] + mid].Prob;
                    if (float.IsNaN(prob) || float.IsInfinity(prob))
                        return -1;
                    return mid;
                }
                if (remain > 0)
                    high = mid - 1;
                else
                    low = mid + 1;
            }
            return -1;
        }

        bool ContainUNK(int[] context)
        {
            for (int i = 0; i < context.Length - 1; i++)
                if (context[i] == _vocab.UnkIndex)
                    return true;
            return false;
        }

        static int VocabSize(Vocab vocab)
        {
            return vocab.Word2Index.Values.Count(val => !vocab.IsNonEvent(val));
        }
        bool applyFracMKNSmoothing = LMConfig.GetOption("applyFracMKNSmoothing", false);
        private bool _useCutoff = LMConfig.GetOption("useCutoff", false);
        readonly string _smoothing = LMConfig.GetOption("smoothing");
        private float level0Bow;

        public bool EstimateMKN(Discount[] discounts)
        {
            for (int i = 0; i < _binFile.NGramcounts.Length; i++)
                _binFile.ValidNGrams[i] = _binFile.NGramcounts[i];
            if (_smoothing == "mkn" || _smoothing == "kn")
                _useCutoff = false;
            int specialIndex = _vocab.EOSIndex;
            bool reverse = LMConfig.GetOption("reverse", false);
            if (reverse)
                specialIndex = _vocab.BOSIndex;
            if (!reverse)
                ((InnerNode*) _binFile.InnerPtr)[_vocab.BOSIndex].Prob = float.PositiveInfinity;
            else
                ((InnerNode*) _binFile.InnerPtr)[_vocab.EOSIndex].Prob = float.PositiveInfinity;
            ((InnerNode*) _binFile.InnerPtr)[_vocab.UnkIndex].Prob = float.NaN;
            for (int currOrder = 1; currOrder <= _order; currOrder++)
            {
                Console.Error.Write("Calculate probability for {0}-grams\r", currOrder);
                bool interpolate = discounts != null && discounts[currOrder - 1] != null &&
                                   discounts[currOrder - 1].Interpolate;
                BinLMIter nGramCountsIter = new BinLMIter(_binFile, currOrder - 1);
                int[] contextCount = new int[currOrder];
                long currPos;
                while ((currPos = nGramCountsIter.MoveNextX(ref contextCount)) >= 0)
                {
                    if ((currOrder > 1 && contextCount[currOrder - 2] == specialIndex))
                        continue;
                    long startPos = _vocab.BOSIndex;
                    if (reverse)
                        startPos = _vocab.EOSIndex;
                    long endPos = _binFile.PAccCount[1] - 1;
                    if (currOrder > 1)
                    {
                        startPos = ((InnerNode*) _binFile.InnerPtr)[currPos].Child;
                        endPos = ((InnerNode*) _binFile.InnerPtr)[currPos + 1].Child;
                    }
                    double totalCount = 0;
                    double subtract = 0;
                    for (long iterPos = startPos; iterPos < endPos; iterPos++)
                    {
                        int count;
                        int index;
                        float mass;
                        if (currOrder == _order)
                        {
                            count =
                                ((LeafNode*) _binFile.FinalPtr)[iterPos].Count;
                            index =
                                ((LeafNode*) _binFile.FinalPtr)[iterPos].Index;
                            mass =
                                ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob;
                        }
                        else
                        {
                            index =
                                ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Index;
                            count =
                                ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Count;
                            mass =
                                ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob;
                        }
                        if (_vocab.IsNonEvent(index))
                            continue;
                        if (applyFracMKNSmoothing)
                        {
                            float fcount = Util.Int2Float(count);
                            totalCount += fcount;
                            subtract += mass;
                        }
                        else
                        {
                            totalCount += count;
                            double discount = discounts[currOrder - 1].discount(count, (long) totalCount, 0);
                            subtract += (1 - discount)*count;
                        }
                    }
                    if (totalCount == 0)
                        continue;
                    if (totalCount < Discount.gtmin[currOrder])
                        for (long iterPos = startPos; iterPos < endPos; iterPos++)
                        {
                            if (currOrder == _order)
                                ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob = float.NaN;
                            else
                                ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob =
                                    float.NaN;
                            _binFile.ValidNGrams[currOrder - 1]--;
                        }
                    else
                        for (long iterPos = startPos; iterPos < endPos; iterPos++)
                        {
                            int count;
                            int index;
                            float mass;
                            if (currOrder == _order)
                            {
                                count =
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Count;
                                index =
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Index;
                                mass =
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob;
                            }
                            else
                            {
                                index =
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Index;
                                count =
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Count;
                                mass =
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob;
                            }
                            if (_vocab.IsNonEvent(index))
                                continue;
                            if (applyFracMKNSmoothing)
                            {
                                float fcount = Util.Int2Float(count) - mass;
                                float prob = (float) Math.Log10(fcount/totalCount);
                                if (currOrder == _order)
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob = mass == 0 ? float.NaN : prob;
                                else
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob =
                                        mass == 0 ? float.NaN : prob;
                            }
                            else
                            {
                                double discount = discounts[currOrder - 1].discount(count, (long) totalCount, 0);
                                float prob = (float) Math.Log10(discount*count/totalCount);
                                if (discount == 0)
                                    _binFile.ValidNGrams[currOrder - 1]--;
                                if (currOrder == _order)
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob = discount == 0 ? float.NaN : prob;
                                else
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob =
                                        discount == 0 ? float.NaN : prob;
                            }
                        }
                    if (currOrder > 1)
                        ((InnerNode*) _binFile.InnerPtr)[currPos].Bow = (float) Math.Log10(subtract/totalCount);
                    else level0Bow = (float) Math.Log10(subtract/totalCount);
                }
                ComputeBOWsX(currOrder - 1, interpolate);
            }
            Console.WriteLine();
            FixupProbs();
            if (!reverse)
                ((InnerNode*) _binFile.InnerPtr)[_vocab.BOSIndex].Prob = -99;
            else
                ((InnerNode*) _binFile.InnerPtr)[_vocab.EOSIndex].Prob = -99;
            ((InnerNode*) _binFile.InnerPtr)[_vocab.UnkIndex].Prob = level0Bow;
            return true;
        }

        public bool Estimate(Discount[] discounts)
        {
            for (int i = 0; i < _binFile.NGramcounts.Length; i++)
                _binFile.ValidNGrams[i] = _binFile.NGramcounts[i];
            if (_smoothing == "mkn" || _smoothing == "kn")
                _useCutoff = false;
            int specialIndex = _vocab.EOSIndex;
            Console.WriteLine(string.Join(" ", _binFile.ValidNGrams));
            bool reverse = LMConfig.GetOption("reverse", false);
            if(reverse)
                specialIndex = _vocab.BOSIndex;

            int vocabSize = VocabSize(_vocab);
            // Ensure <s> unigram exists (being a non-event, it is not inserted in distributeProb(), yet is assumed by much other software).
            if (!reverse)
                ((InnerNode*)_binFile.InnerPtr)[_vocab.BOSIndex].Prob = float.PositiveInfinity;
            else 
                ((InnerNode*) _binFile.InnerPtr)[_vocab.EOSIndex].Prob = float.PositiveInfinity;

            ((InnerNode*) _binFile.InnerPtr)[_vocab.UnkIndex].Prob = float.NaN;
            for (int currOrder = 1; currOrder <= _order; currOrder++)
            {
                bool noDiscount = (discounts == null) || (discounts[currOrder - 1] == null) || discounts[currOrder - 1].Nodiscount();
                if (!noDiscount && discounts[currOrder - 1] != null)
                    discounts[currOrder - 1].PrepareCounts(_binFile, currOrder, _order);
                BinLMIter nGramCountsIter = new BinLMIter(_binFile, currOrder - 1);
                int[] contextCount = new int[currOrder];
                long currPos;
                while ((currPos = nGramCountsIter.MoveNextX(ref contextCount)) >= 0)
                {   
                    if ((currOrder > 1 && contextCount[currOrder - 2] == specialIndex) ||
                        _vocab.IsNonEvent(_vocab.UnkIndex) && ContainUNK(contextCount))
                        continue;
                    bool interpolate = discounts != null && discounts[currOrder - 1] != null && discounts[currOrder - 1].Interpolate;
                    long startPos = _vocab.BOSIndex;
                    if (reverse)
                        startPos = _vocab.EOSIndex;
                    long endPos = _binFile.PAccCount[1] - 1;
                    if (currOrder > 1)
                    {
                        startPos = ((InnerNode*)_binFile.InnerPtr)[currPos].Child;
                        endPos = ((InnerNode*)_binFile.InnerPtr)[currPos + 1].Child;
                    }
                    long totalCount = 0;
                    long observedVocab = 0, min2Vocab = 0, min3Vocab = 0;
                    if (_useCutoff)
                        totalCount = currOrder == 1 ? CountsBinMaker.TotalCount : contextCount[contextCount.Length - 1];
                    else
                        for (long iterPos = startPos; iterPos < endPos; iterPos++)
                        {
                            int count;
                            int index;
                            if (currOrder == _order)
                            {
                                count =
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Count;
                                index =
                                    ((LeafNode*) _binFile.FinalPtr)[iterPos].Index;
                            }
                            else
                            {
                                index =
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Index;
                                count =
                                    ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Count;
                            }
                            if (_vocab.IsNonEvent(index))
                                continue;
                            totalCount += count;
                            observedVocab++;
                            if (count >= 2)
                                min2Vocab++;
                            if (count >= 3)
                                min3Vocab++;
                        }
                    if (totalCount == 0)
                        continue;
                    int[] subContext = new int[currOrder - 1];
                    if (totalCount < Discount.gtmin[currOrder])
                    {
                        for (long iterPos = startPos; iterPos < endPos; iterPos++)
                        {
                            if (currOrder == _order)
                                ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob = float.NaN;
                            else
                                ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob =
                                    float.NaN;
                            _binFile.ValidNGrams[currOrder - 1]--;
                        }
                    }
                    else
                        while (true)
                        {
                            double totalProb = 0;
                            for (long iterPos = startPos; iterPos < endPos; iterPos++)
                            {
                                int count;
                                int index;
                                if (currOrder == _order)
                                {
                                    count = ((LeafNode*)_binFile.FinalPtr)[iterPos].Count;//- binFile.PAccCount[binFile.Order - 1]
                                    index = ((LeafNode*)_binFile.FinalPtr)[iterPos].Index;// - binFile.PAccCount[binFile.Order - 1]
                                }
                                else
                                {
                                    count =
                                        ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Count;
                                    index =
                                        ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Index;
                                }
                                if (currOrder > 1 && count == 0)
                                    continue;
                                float lprob;
                                double discount;
                                if (_vocab.IsNonEvent(index))
                                {
                                    if (currOrder > 1 || index == _vocab.UnkIndex)
                                        continue;
                                    lprob = float.PositiveInfinity;
                                    discount = 1.0;
                                }
                                else
                                {
                                    discount = noDiscount
                                        ? 1.0
                                        : discounts[currOrder - 1].discount(count, totalCount, observedVocab);
                                    double prob = discount * count / totalCount;
                                    if (discount != 0 && interpolate)
                                    {
                                        double lowerOrderWeight = discounts[currOrder - 1].LowerOrderWeight(totalCount,
                                                                                                            observedVocab,
                                                                                                            min2Vocab,
                                                                                                            min3Vocab);
                                        double lowerOrderProb = -1 * Math.Log10(vocabSize);
                                        if (lowerOrderWeight != 0 && currOrder > 1)
                                        {
                                            for (int j = 0; j < currOrder - 2; j++)
                                                subContext[j] = contextCount[j + 1];
                                            subContext[subContext.Length - 1] = index;
                                            lowerOrderProb = CalProb(subContext);
                                        }
                                        prob += lowerOrderWeight * Math.Pow(10, lowerOrderProb);
                                        if (prob > 1.0)
                                            Console.WriteLine("prob larger than 1");
                                    }
                                    lprob = (float)Math.Log10(prob);
                                    if (discount != 0)
                                        totalProb += prob;
                                }
                                if (discount == 0)
                                    _binFile.ValidNGrams[currOrder - 1]--;
                                if (currOrder == _order)
                                    ((LeafNode*)_binFile.FinalPtr)[iterPos].Prob = discount == 0 ? float.NaN : lprob;// - binFile.PAccCount[binFile.Order - 1]
                                else
                                    ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[currOrder - 1] + iterPos].Prob =
                                        discount == 0 ? float.NaN : lprob;
                            }
                            if (!noDiscount && totalCount > 0 && observedVocab < vocabSize && totalProb > 1.0 - ProbEpsilon)
                            {
                                if (interpolate)
                                    interpolate = false;
                                else
                                    totalCount += 1;
                                continue;
                            }
                            break;
                        }
                }
                ComputeBOWs(currOrder - 1);
            }
            Console.WriteLine(string.Join(" ", _binFile.ValidNGrams));
            FixupProbs();
            Console.WriteLine(string.Join(" ", _binFile.ValidNGrams));
            return true;
        }
        void FixupProbs()
        {
            int[] levelFixs = new int[_order];
            {
                int currContextOrder = _order - 1;
                BinLMIter binLMIter = new BinLMIter(_binFile, currContextOrder);
                int[] context = new int[currContextOrder + 1];
                long currPos;
                while ((currPos = binLMIter.MoveNextX(ref context)) >= 0)
                {
                    bool validContext = false;
                    long startPos = ((InnerNode*) _binFile.InnerPtr)[currPos].Child;
                    long endPos = ((InnerNode*) _binFile.InnerPtr)[currPos + 1].Child;
                    for (long iterPos = startPos; iterPos < endPos; iterPos++)
                    {
                        float prob = currContextOrder == _order - 1
                            ? ((LeafNode*) _binFile.FinalPtr)[iterPos].Prob
                            : ((InnerNode*) _binFile.InnerPtr)[
                                _binFile.PAccCount[currContextOrder] + iterPos].Prob;
                        if (!float.IsNaN(prob))
                        {
                            validContext = true;
                            break;
                        }
                    }
                    if (!validContext)
                        continue;
                    for (int subContextOrder = 0; subContextOrder < currContextOrder; subContextOrder++)
                    {
                        int[] subcontext = new int[currContextOrder - subContextOrder];
                        for (int k = 0; k < subcontext.Length; k++)
                            subcontext[k] = context[k + subContextOrder];
                        long index = _binFile.FindPrefixNode(subcontext, _binFile.Order);
                        if (index != -1 && !float.IsNaN(((InnerNode*) _binFile.InnerPtr)[index].Prob))
                            continue;
                        ((InnerNode*) _binFile.InnerPtr)[index].Prob = float.NegativeInfinity;
                        levelFixs[currContextOrder - subContextOrder]++;
                    }
                }
            }

            for (int currContextOrder = 1; currContextOrder < _order; currContextOrder++)
            {
                BinLMIter binLMIter = new BinLMIter(_binFile, currContextOrder);
                int[] context = new int[currContextOrder + 1];
                long currPos;
                while ((currPos = binLMIter.MoveNextX(ref context)) >= 0)
                {
                    InnerNode innerNode = ((InnerNode*) _binFile.InnerPtr)[currPos];
                    if (float.IsNegativeInfinity(innerNode.Prob))
                    {
                        int[] xsscontext = new int[currContextOrder];
                        for (int k = 0; k < xsscontext.Length; k++)
                            xsscontext[k] = context[k];
                        ((InnerNode*) _binFile.InnerPtr)[currPos].Prob = CalProb(xsscontext);
                        _binFile.ValidNGrams[currContextOrder - 1]++;
                    }
                }
            }
        }

        private void ComputeBOWs(int order)
        {
            int[] context = new int[order + 1];
            BinLMIter binLMIter = new BinLMIter(_binFile, order);
            long currPos;
            while ((currPos = (binLMIter.MoveNextX(ref context))) >= 0) //absolute offset
            {
                double numerator = 0, denominator = 0;
                if (ComputeBOW(currPos, context, order, ref numerator, ref denominator))
                {
                    if (order == 0)
                        DistributeProb(numerator);
                    else if (numerator == 0 || denominator == 0)
                        ((InnerNode*)_binFile.InnerPtr)[currPos].Bow = (float)LogPOne; //+ binFile.PAccCount[order]
                    else
                        ((InnerNode*)_binFile.InnerPtr)[currPos].Bow =
                            (float)(Math.Log10(numerator) - Math.Log10(denominator)); //+ binFile.PAccCount[order]
                    if (float.IsInfinity(((InnerNode*)_binFile.InnerPtr)[currPos].Bow))
                        Console.WriteLine();
                }
                else
                    ((InnerNode*)_binFile.InnerPtr)[currPos].Bow = float.PositiveInfinity; // + binFile.PAccCount[order]
            }
        }
        private void ComputeBOWsX(int order, bool interpolate)
        {
            int vocabsize = VocabSize(_vocab);
            
            int[] context = new int[order + 1];
            BinLMIter binLMIter = new BinLMIter(_binFile, order);
            long xcurrPos;
            while ((xcurrPos = (binLMIter.MoveNextX(ref context))) >= 0) //absolute offset
            {
                double numerator = 1, denominator = 1;
                long startPos = _vocab.BOSIndex;
                long endPos = _binFile.PAccCount[1] - 1;
                float bow = level0Bow;
                if (order > 0)
                {
                    startPos = ((InnerNode*) _binFile.InnerPtr)[xcurrPos].Child;
                    bow = ((InnerNode*) _binFile.InnerPtr)[xcurrPos].Bow;
                    endPos = ((InnerNode*) _binFile.InnerPtr)[xcurrPos + 1].Child;
                }
                int[] fcontext = new int[order];
                for (long currPos = startPos; currPos < endPos; currPos++) //relative offsest
                {
                    double lowLogProb = 0;
                    float prob;
                    int index;
                    if (order == _binFile.Order - 1)
                    {
                        prob = ((LeafNode*) _binFile.FinalPtr)[currPos].Prob;
                        index = ((LeafNode*) _binFile.FinalPtr)[currPos].Index;
                    }
                    else
                    {
                        prob = ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[order] + currPos].Prob;
                        index = ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[order] + currPos].Index;
                    }
                    if (float.IsInfinity(prob) || float.IsNaN(prob))
                        continue;
                    if (order > 0)
                    {
                        fcontext[order - 1] = index;
                        for (int j = 0; j < order - 1; j++)
                            fcontext[j] = context[j + 1];
                        lowLogProb = CalProb(fcontext);
                        denominator -= Math.Pow(10, lowLogProb);
                    }
                    else lowLogProb = -Math.Log10(vocabsize + 1);
                    prob = (float) (Math.Pow(10, prob));
                    if (interpolate)
                        prob += (float) Math.Pow(10, lowLogProb + bow);
                    numerator -= prob;
                    if (order == _binFile.Order - 1)
                        ((LeafNode*) _binFile.FinalPtr)[currPos].Prob = (float) Math.Log10(prob);
                    else
                        ((InnerNode*) _binFile.InnerPtr)[_binFile.PAccCount[order] + currPos].Prob =
                            (float) Math.Log10(prob);
                }
                if (order > 0)
                    ((InnerNode*) _binFile.InnerPtr)[xcurrPos].Bow =
                        (float) (Math.Log10(numerator) - Math.Log10(denominator));
                else
                    level0Bow -= (float)Math.Log10(vocabsize + 1);
            }
        }

        private static bool reverse = LMConfig.GetOption("reverse", false);
        private bool ComputeBOW(long pos, int[] context, int clen, ref double numerator, ref double denominator)
        {
            numerator = 1;
            denominator = 1;
            long startPos = _vocab.BOSIndex;
            if(reverse)
                startPos = _vocab.EOSIndex;

            long endPos = _binFile.PAccCount[1] - 1;
            if (clen > 0)
            {
                startPos = ((InnerNode*)_binFile.InnerPtr)[pos].Child;// error here, relative//binFile.PAccCount[clen - 1] +
                endPos = ((InnerNode*)_binFile.InnerPtr)[pos + 1].Child;//or absolute position//binFile.PAccCount[clen - 1] + 
            }
            int[] fcontext = new int[clen];
            for (long currPos = startPos; currPos < endPos; currPos++)//relative offsest
            {
                float prob;
                int index;
                if (clen == _binFile.Order - 1)
                {
                    prob = ((LeafNode*)_binFile.FinalPtr)[currPos].Prob;
                    index = ((LeafNode*)_binFile.FinalPtr)[currPos].Index;
                }
                else
                {
                    prob = ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[clen] + currPos].Prob;
                    index = ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[clen] + currPos].Index;
                }
                if (!float.IsNaN(prob))
                {
                    if (!float.IsInfinity(prob))
                        numerator -= Math.Pow(10, prob);
                    if (clen > 0)
                    {
                        fcontext[clen - 1] = index;
                        for (int j = 0; j < clen - 1; j++)
                            fcontext[j] = context[j + 1];
                        denominator -= Math.Pow(10, CalProb(fcontext));
                    }
                }
            }
            if (numerator < 0 && numerator > -1 * ProbEpsilon)
                numerator = 0;
            if (denominator < 0 && denominator > -1 * ProbEpsilon)
                denominator = 0;
            if (denominator == 0 && numerator > ProbEpsilon)
            {
                double scale = -1 * Math.Log10(1 - numerator);
                for (long currPos = startPos; currPos < endPos; currPos++)
                {
                    if (clen < _binFile.Order - 1)
                    {
                        float prob = ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[clen] + currPos].Prob;
                        if (!float.IsInfinity(prob) && !float.IsNaN(prob))
                            ((InnerNode*)_binFile.InnerPtr)[_binFile.PAccCount[clen] + currPos].Prob += (float)scale;
                    }
                    else
                    {
                        float prob = ((LeafNode*)_binFile.FinalPtr)[currPos].Prob;
                        if (!float.IsInfinity(prob) && !float.IsNaN(prob))
                            ((LeafNode*)_binFile.FinalPtr)[currPos].Prob += (float)scale;
                    }
                }
                numerator = 0;
                return true;
            }
            if (numerator < 0)
                return false;
            if (denominator < 0)
            {
                if (numerator > ProbEpsilon)
                    return false;
                numerator = 0;
                denominator = 0;
                return true;
            }
            return true;
        }
      
        const double LogPOne = 0.0;// log(1)
        const double ProbEpsilon = 3e-06;
        //re-compute backoff weight for all contexts of a given order    
        void DistributeProb(double mass)
        {
            int numWords = 0;
            int numZeroProbs = 0;
            long startPos = 0;
            long endPos = _binFile.PAccCount[1] - 1;
            for (long currPos = startPos; currPos < endPos; currPos++)
            {
                InnerNode innerNode = ((InnerNode*)_binFile.InnerPtr)[currPos];
                if (!_vocab.IsNonEvent(innerNode.Index))
                {
                    numWords++;
                    if (float.IsInfinity(innerNode.Prob) || float.IsNaN(innerNode.Prob))
                        numZeroProbs++;
                }
            }
            double add1 = mass / numZeroProbs;
            double add2 = mass / numWords;
            for (long currPos = startPos; currPos < endPos; currPos++)
            {
                InnerNode innerNode = ((InnerNode*)_binFile.InnerPtr)[currPos];
                if (!_vocab.IsNonEvent(innerNode.Index))
                {
                    if (numZeroProbs > 0)
                    {
                        if (float.IsInfinity(innerNode.Prob) || float.IsNaN(innerNode.Prob))
                            ((InnerNode*)_binFile.InnerPtr)[currPos].Prob = (float)Math.Log10(add1);
                    }
                    else
                    {
                        if (float.IsNaN(innerNode.Prob))//float.IsInfinity(innerNode.Prob)
                            ((InnerNode*)_binFile.InnerPtr)[currPos].Prob = 0;
                        ((InnerNode*)_binFile.InnerPtr)[currPos].Prob = (float)Math.Log10(Math.Pow(10, innerNode.Prob) + add2);
                    }
                }
            }
        }
    }
}