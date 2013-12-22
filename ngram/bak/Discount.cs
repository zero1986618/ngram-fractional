using System;

namespace ngram
{
    internal abstract class Discount
    {
        public static Discount GetDiscount(string smoothing, int order, bool countsAreModified = false,
            bool prepareCountsAtEnd = false)
        {
            if (smoothing == "gt")
                return new GoodTuring(gtmin[order], gtmax[order]);
            if (smoothing == "kn")
                return new KneserNey(gtmin[order], countsAreModified, prepareCountsAtEnd);
            if (smoothing == "wb")
                return new WittenBell(gtmin[order]);
            return new ModifiedKneserNey(gtmin[order], countsAreModified, prepareCountsAtEnd);
        }

        public bool Interpolate;

        public virtual double discount(long count, long totalCount, long vocabSize)
        {
            return 1.0;
        }

        // weight given to the lower-order distribution when interpolating high-order estimates (none by default)
        public virtual double LowerOrderWeight(long totalCount, long observedVocab, long min2Vocab, long min3Vocab)
        {
            return 0.0;
        }
        public virtual double DiscountMass(FracType ft)
        {
            return 1;
        }
        // check if discounting disabled
        public virtual bool Nodiscount()
        {
            return false;
        }

        // dummy estimator for when there is nothing to estimate
        public virtual bool Estimate(long[] countOfCounts, int order)
        {
            return true;
        }

        public virtual bool Estimate(double[] countOfCounts, int order)
        {
            return true;
        }

        // dummy estimator for when there is nothing to estimate
        public virtual bool Estimate(BinaryFile binaryFile, int order)
        {
            return true;
        }

        public virtual void PrepareCounts(BinaryFile binaryFile, int order, int maxOrder)
        {
        }

        public const int GtDefaultMinCount = 1;
        public const int GtDefaultMaxCount = 5;
        public static float[] gtmin = LMConfig.GetOptionList<float>("gtmin");
        public static int[] gtmax = LMConfig.GetOptionList<int>("gtmax");
    }

    internal class WittenBell : Discount
    {
        private readonly float _minCount;

        public WittenBell(float mincount = 0)
        {
            _minCount = mincount;
        }

        public override double discount(long count, long totalCount, long observedVocab)
        {
            return (count <= 0)
                ? 1.0
                : (count < _minCount)
                    ? 0.0
                    : ((double) totalCount/(totalCount + observedVocab));
        }

        public double discount(float count, float totalCount,
            int observedVocab)
        {
            return (count <= 0)
                ? 1.0
                : (count < _minCount)
                    ? 0.0
                    : ((double) totalCount/(totalCount + observedVocab));
        }

        public override double LowerOrderWeight(long totalCount, long observedVocab,
            long min2Vocab, long min3Vocab)
        {
            return (double) observedVocab/(totalCount + observedVocab);
        }

        public override bool Nodiscount()
        {
            return false;
        }

        public override bool Estimate(BinaryFile file, int order)
        {
            return true;
        }
    }

    internal class GoodTuring : Discount
    {
        public GoodTuring(float mincount = GtDefaultMinCount, int maxcount = GtDefaultMaxCount)
        {
            _minCount = mincount;
            _maxCount = maxcount;
            _discountCoeffs = new double[_maxCount + 1];
            _discountCoeffs[0] = 1;
        }

        public override double discount(long count, long totalCount, long vocabSize)
        {
            if (count <= 0)
                return 1;
            if (count < _minCount)
                return 0;
            return count > _maxCount ? 1.0 : _discountCoeffs[count];
        }

        public override bool Nodiscount()
        {
            return _minCount <= 1 && _maxCount <= 0;
        }

        public override bool Estimate(BinaryFile binaryFile, int order)
        {
            long[] countsOfCounts = new long[_maxCount + 2];
            int[] context = new int[order + 1];
            BinLMIter binLMIter = new BinLMIter(binaryFile, order);
            while (binLMIter.MoveNextX(ref context) >= 0)
            {
                if (binaryFile.vocab.IsNonEvent(context[order - 1]))
                    continue;
                if (context[context.Length - 1] <= _maxCount + 1)
                    countsOfCounts[context[context.Length - 1]]++;
            }
            return Estimate(countsOfCounts, order);
        }

        public override bool Estimate(long[] countsOfCounts, int order)
        {
            Console.WriteLine("Good-Turing discounting {0}-grams", order);
            for (int i = 0; i <= _maxCount + 1; i++)
                Console.WriteLine("GT-count [{0}] = {1}", i, countsOfCounts[i]);
            if (countsOfCounts[1] == 0)
            {
                Console.Error.WriteLine("Warning: no singleton counts");
                _maxCount = 0;
            }
            while (_maxCount > 0 && countsOfCounts[_maxCount + 1] == 0)
            {
                Console.Error.WriteLine("warning: count of count {0} is zero -- lowering maxcount\n", _maxCount + 1);
                _maxCount--;
            }
            if (_maxCount <= 0)
                Console.Error.WriteLine("GT discounting disabled\n");
            else
            {
                double commonTerm = (_maxCount + 1)*(double) countsOfCounts[_maxCount + 1]/countsOfCounts[1];
                for (int i = 1; i <= _maxCount; i++)
                {
                    double coeff = 1.0;
                    if (countsOfCounts[i] == 0)
                        Console.Error.WriteLine("warning: count of count {0} is zero\n", i);
                    else
                    {
                        double coeff0 = (i + 1)*(double) countsOfCounts[i + 1]/(i*(double) countsOfCounts[i]);
                        coeff = (coeff0 - commonTerm)/(1.0 - commonTerm);
                        if (double.IsInfinity(coeff) || coeff <= ProbEpsilon || coeff0 > 1.0)
                        {
                            Console.Error.WriteLine("warning: discount coeff {0} is out of range: {1}", i, coeff);
                            coeff = 1.0;
                        }
                    }
                    _discountCoeffs[i] = coeff;
                }
            }
            return false;
        }

        private const double ProbEpsilon = 3e-06;
        private readonly float _minCount;
        private int _maxCount;
        private readonly double[] _discountCoeffs;
    }

    internal class KneserNey : Discount
    {
        protected float MinCount; // counts below this are set to 0
        protected double Discount1; // discounting constant
        protected bool CountsAreModified; // low-order counts are already modified
        protected bool PrepareCountsAtEnd; // should we modify counts after computing D

        public KneserNey(float mincount = 0, bool countsAreModified = false, bool prepareCountsAtEnd = false)
        {
            MinCount = mincount;
            Discount1 = 0.0;
            CountsAreModified = countsAreModified;
            PrepareCountsAtEnd = prepareCountsAtEnd;
        }

        public override double discount(long count, long totalCount, long vocabSize)
        {
            if (count <= 0)
                return 1.0;
            if (count < MinCount)
                return 0.0;
            return (count - Discount1)/count;
        }

        public override double LowerOrderWeight(long totalCount, long observedVocab, long min2Vocab, long min3Vocab)
        {
            return (Discount1*observedVocab/totalCount);
        }

        public override bool Estimate(BinaryFile binaryFile, int order)
        {
            if (!PrepareCountsAtEnd)
                PrepareCounts(binaryFile, order, binaryFile.Order);
            long n1 = 0;
            long n2 = 0;
            BinLMIter binLMIter = new BinLMIter(binaryFile, order);
            int[] mkeys = new int[order + 1];
            while (binLMIter.MoveNextX(ref mkeys) >= 0)
            {
                if (mkeys[order - 1] == 1)
                    continue;
                if (mkeys[mkeys.Length - 1] == 1)
                    n1++;
                if (mkeys[mkeys.Length - 1] == 2)
                    n2++;
            }
            if (n1 == 0 || n2 == 0)
                return false;
            Discount1 = 1.0*n1/(n1 + n2);
            if (PrepareCountsAtEnd)
                PrepareCounts(binaryFile, order, binaryFile.Order);
            return true;
        }

        public override bool Estimate(long[] countOfCounts, int order)
        {
            long n1 = countOfCounts[1];
            long n2 = countOfCounts[2];
            if (n1 == 0 || n2 == 0)
                return false;
            Discount1 = 1.0*n1/(n1 + n2);
            return true;
        }

        private static bool reverse = LMConfig.GetOption("reverse", false);

        public override unsafe void PrepareCounts(BinaryFile binaryFile, int order, int maxOrder)
        {
            if (CountsAreModified || order >= maxOrder)
                return;
            long start = binaryFile.vocab.BOSIndex;
            if (reverse)
                start = binaryFile.vocab.EOSIndex;
            for (int i = 0; i < order - 1; i++)
                start = ((InnerNode*) binaryFile.InnerPtr)[binaryFile.PAccCount[i] + start + 1].Child - 1;
            for (long i = binaryFile.PAccCount[order - 1] + start + 1; i < binaryFile.PAccCount[order] - 1; i++)
                ((InnerNode*) binaryFile.InnerPtr)[i].Count = 0;
            int[] keys = new int[order + 2];
            BinLMIter binLMIter = new BinLMIter(binaryFile, order + 1);
            int[] subkeys = new int[order];
            while (binLMIter.MoveNextX(ref keys) >= 0)
            {
                if (keys[keys.Length - 1] > 0)
                {
                    for (int i = 0; i < subkeys.Length; i++)
                        subkeys[i] = keys[i + 1];
                    long addr = binaryFile.FindPrefixNode(subkeys, binaryFile.Order);
                    ((InnerNode*) binaryFile.InnerPtr)[addr].Count++;
                }
            }
            CountsAreModified = true;
        }
    }

    internal class ModifiedKneserNey : KneserNey
    {
        private double _discount2; // additional discounting constants
        private double _discount3Plus;

        public ModifiedKneserNey(float mincount = 0, bool countsAreModified = false, bool prepareCountsAtEnd = false)
            : base(mincount, countsAreModified, prepareCountsAtEnd)
        {
            _discount2 = 0;
            _discount3Plus = 0;
        }

        public override double DiscountMass(FracType ft)
        {
            double n3 = 1 - ft[0] - ft[1] - ft[2];
            if (n3 < 0) n3 = ft[3];
            double mass = Discount1*ft[1] + _discount2*ft[2] + _discount3Plus*n3;
            return mass;
        }

        public override double discount(long count, long totalCount, long vocabSize)
        {
            if (count < MinCount)
                return 0.0;
            if (count == 1)
                return (count - Discount1)/count;
            if (count == 2)
                return (count - _discount2)/count;
            return (count - _discount3Plus)/count;
        }

        public override double LowerOrderWeight(long totalCount, long observedVocab, long min2Vocab, long min3Vocab)
        {
            return (Discount1*(observedVocab - min2Vocab) +
                    _discount2*(min2Vocab - min3Vocab) +
                    _discount3Plus*min3Vocab)/totalCount;
        }

        public override bool Estimate(BinaryFile binaryFile, int order)
        {
            if (!PrepareCountsAtEnd)
                PrepareCounts(binaryFile, order, binaryFile.Order);
            long n1 = 0;
            long n2 = 0;
            long n3 = 0;
            long n4 = 0;
            BinLMIter binLMIter = new BinLMIter(binaryFile, order);
            int[] mkeys = new int[order + 1];
            int nonEventCount = 0;
            while (binLMIter.MoveNextX(ref mkeys) >= 0)
            {
                if (binaryFile.vocab.IsNonEvent(mkeys[order - 1])) //IsNonEvent
                {
                    nonEventCount++;
                    continue;
                }
                if (mkeys[mkeys.Length - 1] == 1)
                    n1++;
                if (mkeys[mkeys.Length - 1] == 2)
                    n2++;
                if (mkeys[mkeys.Length - 1] == 3)
                    n3++;
                if (mkeys[mkeys.Length - 1] == 4)
                    n4++;
            }
            Console.WriteLine("Modified Kneser-Ney under {0} gram", order);
            Console.WriteLine("n1 = {0}", n1);
            Console.WriteLine("n2 = {0}", n2);
            Console.WriteLine("n3 = {0}", n3);
            Console.WriteLine("n4 = {0}", n4);
            double y = (double) n1/(n1 + 2*n2);
            Discount1 = 1 - 2*y*n2/n1;
            _discount2 = 2 - 3*y*n3/n2;
            _discount3Plus = 3 - 4*y*n4/n3;
            if (Discount1 < 0.0 || _discount2 < 0.0 || _discount3Plus < 0.0)
            {
                Console.Error.WriteLine("one of modified KneserNey discounts is negative\n");
                return false;
            }
            if (PrepareCountsAtEnd)
                PrepareCounts(binaryFile, order, binaryFile.Order);
            return true;
        }

        public override bool Estimate(long[] countOfCounts, int order)
        {
            long n1 = countOfCounts[1];
            long n2 = countOfCounts[2];
            long n3 = countOfCounts[3];
            long n4 = countOfCounts[4];
            Console.WriteLine("Modified Kneser-Ney under {0} gram", order);
            Console.WriteLine("n1 = {0}", n1);
            Console.WriteLine("n2 = {0}", n2);
            Console.WriteLine("n3 = {0}", n3);
            Console.WriteLine("n4 = {0}", n4);
            double y = (double) n1/(n1 + 2*n2);
            Discount1 = 1 - 2*y*n2/n1;
            _discount2 = 2 - 3*y*n3/n2;
            _discount3Plus = 3 - 4*y*n4/n3;
            if (Discount1 < 0.0 || _discount2 < 0.0 || _discount3Plus < 0.0)
            {
                Console.Error.WriteLine("one of modified KneserNey discounts is negative\n");
                return false;
            }
            return true;
        }

        public override bool Estimate(double[] countOfCounts, int order)
        {
            double n1 = countOfCounts[1];
            double n2 = countOfCounts[2];
            double n3 = countOfCounts[3];
            double n4 = countOfCounts[4];
            Console.WriteLine("Fractional Modified Kneser-Ney under {0} gram", order);
            Console.WriteLine("n1 = {0:F4}", n1);
            Console.WriteLine("n2 = {0:F4}", n2);
            Console.WriteLine("n3 = {0:F4}", n3);
            Console.WriteLine("n4 = {0:F4}", n4);
            double y = n1/(n1 + 2*n2);
            Discount1 = 1 - 2*y*n2/n1;
            _discount2 = 2 - 3*y*n3/n2;
            _discount3Plus = 3 - 4*y*n4/n3;
            if (Discount1 < 0.0 || _discount2 < 0.0 || _discount3Plus < 0.0)
            {
                Console.Error.WriteLine("one of modified KneserNey discounts is negative\n");
                return false;
            }
            return true;
        }
    }
}