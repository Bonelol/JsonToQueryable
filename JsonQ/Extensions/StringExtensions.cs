using System;

namespace JsonQ.Extensions
{
    internal static class StringExtensions
    {
        /// <summary>
        /// If value is 'True' or '1' return true, otherwise return false.
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        internal static bool ToBoolean(this string text)
        {
            if (text == null) return false;
            if (text.Trim() == "1") return true;

            bool result;
            bool.TryParse(text, out result);

            return result;
        }

        internal static double ToDouble(this string text)
        {
            double result;
            Double.TryParse(text, out result);

            return result;
        }

        internal static int ToInt32(this string text)
        {
            int result;
            Int32.TryParse(text, out result);

            return result;
        }

        internal static float ToFloat(this string text)
        {
            float result;
            float.TryParse(text, out result);

            return result;
        }
    }
}
