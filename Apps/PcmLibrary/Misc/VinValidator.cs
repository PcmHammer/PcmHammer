using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    public class VinValidator
    {
        public static bool IsValid(string vin, out int invalidCharacterIndex, out char requiredCheckDigit)
        {
            invalidCharacterIndex = -1;
            requiredCheckDigit = 'X';

            // Array of VIN character position weight factors:
            ushort[] CharWeight = new ushort[] { 8, 7, 6, 5, 4, 3, 2, 10, 0, 9, 8, 7, 6, 5, 4, 3, 2 };
            ushort checksum = 0;
            for (int i = 0; i < 17; i++)
            {
                ushort digitVal = CharWeight[i];
                if (char.IsDigit(vin[i]))
                {
                    digitVal *= ((ushort)char.GetNumericValue(vin[i]));
                }
                else
                {
                    switch (char.ToUpper(vin[i]))
                    {
                        case 'A':
                        case 'J':
                            digitVal *= 1;
                            break;
                        case 'B':
                        case 'K':
                        case 'S':
                            digitVal *= 2;
                            break;
                        case 'C':
                        case 'L':
                        case 'T':
                            digitVal *= 3;
                            break;
                        case 'D':
                        case 'M':
                        case 'U':
                            digitVal *= 4;
                            break;
                        case 'E':
                        case 'N':
                        case 'V':
                            digitVal *= 5;
                            break;
                        case 'F':
                        case 'W':
                            digitVal *= 6;
                            break;
                        case 'G':
                        case 'P':
                        case 'X':
                            digitVal *= 7;
                            break;
                        case 'H':
                        case 'Y':
                            digitVal *= 8;
                            break;
                        case 'R':
                        case 'Z':
                            digitVal *= 9;
                            break;
                        default:
                            invalidCharacterIndex = i;
                            return false;
                    }
                }
                checksum += digitVal;
            }

            checksum %= 11;

            char CheckDigit = 'X';

            if (checksum < 10)
            {
                CheckDigit = checksum.ToString()[0];
            }

            if (vin[8] == CheckDigit)
            {
                return true;
            }
            else
            {
                requiredCheckDigit = CheckDigit;
                return false;
            }
        }
    }
}