using System;
using System.Collections.Generic;

namespace ngram
{
    public class FracType
    {
        public FracType()
        {
            ResetToLog();
        }
        private bool init = true;
        private double[] a = new double[5];

        public void Reset()
        {
        }
        public void ResetToLog()
        {
            for (int i = 0; i < a.Length; i++)
                a[i] = -1e5;
            init = true;
        }

        public void UpdateLog(double p)
        {
            while (p > 1)
            {
                UpdateLog(1);
                p -= 1;
            }
            if (p == 0)
                return;
            if (p == 1)
            {
                if (init)
                {
                    a[0] = -1e5;
                    a[1] = 0;
                    init = false;
                }
                else
                {
                    for (int i = 4; i > 0; i--)
                        a[i] = a[i - 1];
                    a[0] = -1e5;
                }
            }
            else
            {
                if (init)
                {
                    a[0] = Math.Log(1 - p);
                    a[1] = Math.Log(p);
                    init = false;
                }
                else
                {
                    for (int i = 4; i > 0; i--)
                        a[i] = logadd(a[i - 1] + Math.Log(p), a[i] + Math.Log(1 - p));
                    a[0] += Math.Log(1 - p);
                }
            }
        }

        public double this[int index]
        {
            get { return a[index]; }
            set { a[index] = value; }
        }

        public void ChangeLogToReal()
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] < -308)
                    a[i] = 0;
                else
                    a[i] = Math.Exp(a[i]);
            }
        }

        public double DiscountMass(FracType ft, List<double> discount)
        {
            double n3 = 1 - ft[0] - ft[1] - ft[2];
            if (n3 < 0)
                n3 = ft[3];
            double mass = discount[0]*ft[1] + discount[1]*ft[2] + discount[2]*n3;
            return mass;
        }

        private double logadd(double logx, double logy)
        {
            if (Math.Abs(logx) > 308)
                return logy;
            if (Math.Abs(logy) > 308)
                return logx;
            return logx + Math.Log(1 + Math.Exp(logy - logx));
        }
    }
}