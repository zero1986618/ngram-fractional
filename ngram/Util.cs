using System;
using System.Text;

namespace ngram
{
    class Util
    {
        public static float Int2Float(int val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            return BitConverter.ToSingle(bytes, 0);
        }
        public static string FormatTimeTicks(long ticks)
        {
            long ms = Math.Abs(ticks) / 10000;
            long sec = ms / 1000;
            long min = sec / 60;
            long h = min / 60;
            sec %= 60;
            min %= 60;
            return string.Format("{0:D2}:{1:D2}:{2:D2}", h, min, sec);
        }

        public static string TimeDiff(DateTime d1, DateTime d2)
        {
            return FormatTimeTicks(d2.Ticks - d1.Ticks);
        }

        private static int _strDup;
        public static string GenRandStr(int strlen)
        {
            StringBuilder sb = new StringBuilder();
            long num2 = DateTime.Now.Ticks + _strDup;
            _strDup++;
            Random random = new Random(((int)(((ulong)num2) & 0xffffffffL)) | ((int)(num2 >> _strDup)));
            for (int i = 0; i < strlen; i++)
            {
                int num = random.Next();
                if ((num % 2) == 0)
                    sb.Append((char)(0x30 + ((ushort)(num % 10))));
                else
                    sb.Append((char)(0x41 + ((ushort)(num % 0x1a))));
            }
            return sb.ToString();
        }
    }
}