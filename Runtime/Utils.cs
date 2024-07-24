using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Unity.PolySpatial.Extensions
{ 
    public static partial class Utils
    {
        public static int LexicalCompare(string x, string y)
        {
            if (x == null || y == null)
            {
                return 0;
            }

            int xLength = x.Length;
            int yLength = y.Length;

            int xPos = 0;
            int yPos = 0;

            while (xPos < xLength && yPos < yLength)
            {
                if (char.IsDigit(x[xPos]) && char.IsDigit(y[yPos]))
                {
                    int xNumEnd = FindNumberEndIndex(x, xPos);
                    int yNumEnd = FindNumberEndIndex(y, yPos);

                    int xNumeric = int.Parse(x.Substring(xPos, xNumEnd - xPos + 1));
                    int yNumeric = int.Parse(y.Substring(yPos, yNumEnd - yPos + 1));

                    int result = xNumeric.CompareTo(yNumeric);
                    if (result != 0)
                    {
                        return result;
                    }

                    xPos = xNumEnd + 1;
                    yPos = yNumEnd + 1;
                }
                else
                {
                    int result = x[xPos].CompareTo(y[yPos]);
                    if (result != 0)
                    {
                        return result;
                    }

                    xPos++;
                    yPos++;
                }
            }

            return xLength.CompareTo(yLength);
        }

        private static int FindNumberEndIndex(string str, int startIndex)
        {
            int endIndex = startIndex;
            while (endIndex < str.Length && char.IsDigit(str[endIndex]))
            {
                endIndex++;
            }

            return endIndex - 1;
        }
    }
}