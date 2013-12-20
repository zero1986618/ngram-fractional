using System;
using System.IO;

namespace ngram
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            LMConfig.SetArgs(args);
            DateTime dateTime = DateTime.Now;
            DateTime dateTimeStart = DateTime.Now;
            int order = LMConfig.GetOption("order", 5);
            string lmbin = LMConfig.GetOption("LMBinFile");
            string lmarpa = LMConfig.GetOption("LMArpaFile");
            string countbin = LMConfig.GetOption("CountBinFile");
            string method = LMConfig.GetOption("smoothing");
            string text = LMConfig.GetOption("text");
            string weight = LMConfig.GetOption("weight");
            string vocab = LMConfig.GetOption("vocab");
            Console.WriteLine("Start Building {0}-gram LM based on {1} smoothing", order, method);
            CountsBinMaker binMaker = new CountsBinMaker(text, order, weight);
            binMaker.MakeCountsBin();
            binMaker.Vocab.Dump(vocab);
            
            Console.WriteLine("Step-1:\tMakeCountBin time:{0} seconds", Util.TimeDiff(DateTime.Now, dateTime));
            dateTime = DateTime.Now;
            RecurseGenBin recurseGenBin = new RecurseGenBin(order);
            recurseGenBin.GenBin(countbin);
            recurseGenBin.BinaryFile.vocab = binMaker.Vocab;
            Console.WriteLine("Step-2:\tGenCountBin time:{0} seconds", Util.TimeDiff(DateTime.Now, dateTime));
            dateTime = DateTime.Now;
            bool interpolate = LMConfig.GetOption("interpolate", true);
            bool countsAreModified = LMConfig.GetOption("countsAreModified", false);
            bool prepareCountsAtEnd = LMConfig.GetOption("prepareCountsAtEnd", false);
            bool needPrepareCounts = LMConfig.GetOption("needPrepareCounts", false);
            Discount[] discounts = new Discount[order];
            for (int i = 1; i <= order; i++)
            {
                discounts[i - 1] = Discount.GetDiscount(method, i, countsAreModified, prepareCountsAtEnd);
                discounts[i - 1].Interpolate = interpolate;
                if (!needPrepareCounts)
                    discounts[i - 1].Estimate(recurseGenBin.BinaryFile, i);
                else
                    discounts[i - 1].Estimate(binMaker.CountsOfCounts[i - 1], i);
            }
            bool applyFracMKNSmoothing = LMConfig.GetOption("applyFracMKNSmoothing", false);
            Console.WriteLine("applyFracMKNSmoothing：{0}", applyFracMKNSmoothing);
            Console.WriteLine("Step-3:\tBuild Discount Model time:{0} seconds", Util.TimeDiff(DateTime.Now, dateTime));
            dateTime = DateTime.Now;
            bLM bLM = new bLM(binMaker.Vocab, recurseGenBin.BinaryFile);
            if (applyFracMKNSmoothing)
                bLM.EstimateMKN(discounts);
            else
                bLM.Estimate(discounts);
            Console.WriteLine("Step-4:\tCalculate Probability time:{0} seconds", Util.TimeDiff(DateTime.Now, dateTime));
            dateTime = DateTime.Now;
            bool dumpBinLM = LMConfig.GetOption("dumpBinLM", false);
            if (dumpBinLM)
                recurseGenBin.BinaryFile.DumpBinLM(lmbin);
            bool dumpArpa = LMConfig.GetOption("dumpArpaLM", false);
            if (dumpArpa)
                recurseGenBin.BinaryFile.DumpArpaLM(lmarpa);
            recurseGenBin.BinaryFile.Dispose();
            string smoothing = LMConfig.GetOption("smoothing");
            Console.WriteLine("Step-5:\tDump Bin&Arpa Model time:{0} seconds", Util.TimeDiff(DateTime.Now, dateTime));
            Console.WriteLine("Step-6:\tDelete temporary files.");
            for (int i = 0; i < order; i++)
                if (File.Exists(smoothing + "." + (i + 1) + "gram.bin"))
                    File.Delete(smoothing + "." + (i + 1) + "gram.bin");
            if (File.Exists(countbin))
                File.Delete(countbin);
            Console.WriteLine("Total time:{0} seconds", Util.TimeDiff(DateTime.Now, dateTimeStart));
        }
    }
}