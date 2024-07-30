using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public class KeyAlgorithm
    {
        /// <summary>
        /// Gets the unlock key from the given algorithm index and seed.
        /// </summary>
        /// <remarks>
        /// Updated with correct keygenerator that will support all algos.
        /// Updated January 10, 2020 - Gampy <pcmhacking.net>
        ///   Added algorithm index bounds checking, removed GetKey_?() hacks and removed unused elements.
        /// </remarks>
        public static UInt16 GetKey(int algo, UInt16 seed)
        {
            if ((algo >= 0) && (algo <= 255))
            {
                if (seed != 0xFFFF)
                {
                    return unchecked((UInt16)KeyAlgo(seed, algo));
                }
                else
                {
                    // 0xFFFF seed is non-standard and indicates that Parameter block is not programmed
                    // so the key is also 0xFFFF. This sometimes happens after SPS flashing.
                    return 0xFFFF;
                }
            }
            else
            {
                return 0x0000;
            }
        }

        public static int key_value; // key value

        public static byte[][] bytearray1 = new byte[][] {
            new byte[]{133, 182, 150, 10, 42, 169, 33, 65, 75, 82, 231, 46, 1 },        // #0
            new byte[]{1, 20, 97, 2, 107, 106, 5, 126, 203, 3, 76, 6, 110},             // #1
            new byte[]{111, 126, 237, 150, 20, 228, 224, 107, 203, 1, 42, 97, 135},
            new byte[]{135, 20, 208, 25, 126, 226, 126, 76, 9, 251, 42, 166, 244},
            new byte[]{245, 152, 176, 252, 126, 219, 127, 76, 6, 26, 20, 218, 104},
            new byte[]{105, 20, 226, 197, 152, 129, 81, 107, 213, 2, 42, 8, 160},
            new byte[]{160, 42, 169, 58, 20, 1, 191, 107, 237, 11, 76, 5, 205},
            new byte[]{204, 20, 174, 141, 126, 251, 217, 76, 2, 247, 152, 202, 52},
            new byte[]{52, 42, 192, 123, 20, 111, 88, 152, 193, 38, 76, 6, 192},
            new byte[]{192, 20, 1, 56, 76, 5, 18, 126, 70, 150, 42, 226, 2},
            new byte[]{2, 20, 66, 196, 152, 182, 188, 76, 5, 46, 126, 48, 44},
            new byte[]{44, 152, 56, 8, 126, 242, 148, 107, 224, 2, 76, 3, 72},
            new byte[]{72, 126, 114, 224, 152, 185, 176, 20, 92, 39, 42,171, 8},
            new byte[]{8, 126, 192, 58, 42, 164, 13, 107, 71, 5, 76, 11, 5},
            new byte[]{4, 107, 80, 2, 126, 80, 210, 76, 5, 253, 152, 24, 203},          // #14 P04
            new byte[]{202, 126, 48, 207, 107, 4, 5, 42, 213, 46, 152, 217, 218},
            new byte[]{219, 126, 160, 15, 20, 107, 4, 107, 0, 5, 76, 1, 133},           // #16 Vortec Black Box
            new byte[]{133, 152, 50, 105, 20, 252, 179, 126, 249, 203, 107, 174, 3},
            new byte[]{3, 126, 176, 99, 76, 1, 64, 20, 78, 66, 152,202, 52},
            new byte[]{53, 152, 44, 1, 20, 192, 40, 42, 67, 105, 107, 198, 1},
            new byte[]{1, 76, 1, 137, 42, 211, 214, 20, 138, 41, 126, 224, 71},
            new byte[]{70, 76, 1, 20, 42, 222, 204, 152, 151, 117, 20, 138, 27},
            new byte[]{26, 42, 82, 150, 126, 96, 172, 20, 240, 86,107, 71, 1},
            new byte[]{0, 126, 140, 8, 76, 9, 176, 20, 244, 171, 42, 98, 203},
            new byte[]{202, 20, 129, 74, 76, 6, 228, 152, 145, 112, 42, 5, 76},
            new byte[]{76, 20, 24, 70, 76, 6, 96, 152, 112, 115, 126, 24, 144},
            new byte[]{144, 76, 7, 13, 152, 176, 131, 20, 48, 133,42, 194, 24},
            new byte[]{24, 107, 20, 3, 42, 52, 192, 20, 52, 209, 76, 1, 209},
            new byte[]{208, 126, 128, 63, 20, 65, 129, 76, 11, 147, 152, 185, 115},
            new byte[]{114, 76, 5, 33, 20, 30, 23, 126, 119, 203, 42, 205, 66},
            new byte[]{66, 152, 7, 79, 20, 24, 1, 126, 13, 59,42, 2, 254},
            new byte[]{254, 126, 211, 91, 20, 82, 44, 76, 1, 12, 152, 74, 110},
            new byte[]{110, 152, 144, 72, 107, 4, 7, 20, 16, 138, 126, 4, 152},
            new byte[]{152, 20, 12, 92, 76, 1, 62, 107, 43, 9, 152, 134, 48},
            new byte[]{48, 107, 165, 5, 42, 14, 212, 126, 70, 105,20, 224, 31},
            new byte[]{30, 76, 5, 100, 20, 16, 148, 42, 162, 4, 152, 177, 75},
            new byte[]{74, 42, 91, 90, 107, 12, 3, 126, 144, 139, 20, 20, 36},
            new byte[]{36, 42, 150, 49, 152, 1, 1, 20, 128, 56, 107, 14, 2},
            new byte[]{2, 20, 42, 2, 107, 213, 1, 126, 248, 1, 42, 37,7},
            new byte[]{6, 152, 31, 222, 126, 225, 183, 20, 142, 25, 76, 9, 36},
            new byte[]{36, 20, 82, 1, 126, 56, 151, 42, 190, 56, 152, 212, 40},         // #40 P01/P59
            new byte[]{40, 20, 209, 128, 76, 11, 0, 152, 242, 204, 42, 81, 12},
            new byte[]{12, 76, 10, 40, 20, 211, 197, 126, 248, 99, 107,255, 11},
            new byte[]{10, 76, 7, 0, 126, 238, 24, 20, 208, 193, 42, 10, 58},
            new byte[]{58, 152, 144, 57, 107, 32, 7, 76, 11, 42, 20, 15, 4},
            new byte[]{4, 42, 178, 48, 20, 48, 170, 126, 116, 247, 107, 64, 7},
            new byte[]{6, 20, 31, 35, 107, 60, 11, 42, 80, 245, 126, 244,31},
            new byte[]{30, 126, 244, 31, 42, 12, 76, 152, 1, 172, 76, 6, 150},
            new byte[]{150, 76, 10, 128, 20, 15, 86, 152, 151, 47, 126, 100, 39},
            new byte[]{38, 126, 251, 149, 76, 10, 241, 42, 99, 117, 152, 100, 30},
            new byte[]{30, 20, 81, 162, 42, 148, 123, 107, 8, 11, 76, 2,64},
            new byte[]{64, 42, 192, 192, 152, 119, 40, 76, 1, 13, 20, 65, 1},
            new byte[]{0, 126, 168, 34, 107, 128, 1, 152, 23, 189, 20, 142, 32},
            new byte[]{32, 20, 240, 134, 107, 185, 6, 152, 177, 9, 76, 2, 193},
            new byte[]{192, 42, 33, 128, 107, 152, 6, 20, 32, 23, 76, 2,28},
            new byte[]{28, 107, 111, 3, 76, 9, 17, 20, 42, 13, 152, 44, 145},
            new byte[]{144, 76, 1, 49, 152, 58, 114, 42, 156, 61, 126, 153, 237},
            new byte[]{236, 107, 101, 7, 76, 10, 119, 126, 248, 218, 152, 63, 82},
            new byte[]{82, 42, 30, 121, 107, 153, 3, 126, 91, 162, 152,115, 30},
            new byte[]{30, 20, 49, 132, 126, 110, 224, 152, 136, 135, 42, 109, 65},
            new byte[]{64, 42, 12, 231, 107, 90, 7, 126, 178, 152, 20, 241, 81},
            new byte[]{80, 42, 84, 166, 20, 89, 213, 76, 6, 114, 107, 87, 10},
            new byte[]{10, 42, 95, 70, 107, 65, 3, 152, 5, 100,126, 179, 212},
            new byte[]{212, 42, 139, 144, 76, 5, 97, 126, 148, 128, 20, 93, 38},
            new byte[]{38, 152, 106, 101, 76, 7, 178, 42, 188, 114, 126, 118, 113},
            new byte[]{112, 152, 127, 230, 42, 46, 177, 76, 6, 112, 20, 6, 103},
            new byte[]{102, 126, 241, 62, 76, 1, 5, 42, 16, 96, 20, 147, 193},          // #66 P10
            new byte[]{192, 126, 228, 53, 107, 120, 6, 42, 16, 22, 152, 167, 123},
            new byte[]{122, 76, 2, 235, 152, 59, 231, 20, 67, 48, 107, 171, 6},
            new byte[]{6, 42, 132, 181, 152, 1, 38, 20, 187, 101, 126, 65,4},
            new byte[]{4, 126, 204, 6, 20, 205, 246, 107, 74, 7, 76, 5, 143},
            new byte[]{142, 20, 106, 202, 126, 106, 28, 42, 169, 49, 107, 172, 7},
            new byte[]{6, 152, 188, 199, 20, 203, 179, 126, 197, 231, 76, 7, 220},
            new byte[]{220, 126, 229, 157, 107, 75, 6, 76, 2, 138, 152,3, 175},
            new byte[]{174, 152, 95, 173, 76, 7, 5, 107, 34, 2, 20, 57, 138},
            new byte[]{138, 42, 73, 49, 152, 224, 242, 107, 203, 10, 76, 11, 234},
            new byte[]{234, 42, 126, 90, 152, 8, 98, 76, 5, 12, 107, 229, 5},
            new byte[]{4, 126, 250, 215, 20, 8, 80, 76, 10, 110, 152,198, 45},
            new byte[]{44, 126, 255, 18, 20, 173, 211, 152, 224, 53, 42, 151, 39},
            new byte[]{38, 76, 10, 48, 20, 8, 80, 126, 53, 248, 107, 37, 2},
            new byte[]{2, 107, 210, 6, 42, 76, 165, 76, 3, 76, 126, 162, 142},
            new byte[]{142, 20, 30, 184, 126, 225, 162, 107, 132,11, 152, 96, 16},
            new byte[]{16, 107, 131, 3, 20, 96, 188, 126, 32, 156, 152, 2, 24},
            new byte[]{24, 42, 26, 239, 107, 26, 2, 126, 153, 248, 76, 1, 12},
            new byte[]{12, 20, 20, 48, 76, 10, 7, 107, 1, 1, 126, 232, 115},
            new byte[]{114, 20, 16, 131, 76, 7, 59, 107, 235, 3, 152, 30, 1},
            new byte[]{0, 20, 180, 16, 42, 60, 45, 126, 176, 133, 107, 172, 3},
            new byte[]{2, 107, 184, 5, 20, 161, 150, 152, 35, 29, 126, 192, 6},
            new byte[]{6, 76, 6, 55, 107, 132, 7, 152, 83, 203, 126, 44, 25},
            new byte[]{24, 126, 80, 216, 20, 6, 221, 152, 14, 234, 76, 3, 215},
            new byte[]{214, 76, 2, 35, 20, 112, 243, 152, 133, 23, 42, 154, 232},
            new byte[]{232, 152, 100, 154, 107, 154, 1, 20, 65, 64, 126, 16, 110},      // #91 P12
            new byte[]{110, 76, 10, 180, 42, 223, 220, 152, 118, 10,126, 32, 193},
            new byte[]{192, 76, 10, 113, 107, 73, 1, 126, 192, 71, 20, 244, 143},
            new byte[]{142, 152, 107, 237, 42, 135, 167, 20, 208, 241, 107, 249, 3},
            new byte[]{2, 20, 117, 7, 76, 9, 193, 107, 89, 6, 126, 8, 115},
            new byte[]{114, 126, 32, 34, 76, 1, 14, 20, 64, 16, 152, 169, 17},
            new byte[]{16, 76, 10, 134, 20, 79, 84, 107, 80, 1, 42, 27, 6},
            new byte[]{6, 42, 88, 165, 20, 89, 81, 107, 212, 1, 126, 16, 64},
            new byte[]{64, 76, 5, 88, 20, 8, 32, 107, 2, 11, 42, 160, 146},
            new byte[]{146, 152, 194, 36, 20, 154, 227, 76, 7, 8, 107, 7, 5},
            new byte[]{4, 107, 97, 6, 126, 117, 203, 76, 1, 10, 20, 99, 119},
            new byte[]{118, 152, 100, 71, 76, 7, 47, 42, 158, 208, 126, 144, 135},
            new byte[]{134, 42, 57, 214, 126, 226, 135, 152, 231, 7, 76, 5,41},
            new byte[]{40, 76, 5, 106, 126, 248, 16, 152, 143, 80, 42, 64, 86},
            new byte[]{86, 42, 13, 198, 126, 7, 184, 107, 27, 11, 20, 176, 53},
            new byte[]{52, 152, 215, 182, 107, 1, 11, 76, 3, 252, 126, 247, 191},
            new byte[]{190, 126, 132, 3, 42, 48, 22, 152, 103, 201, 20,132, 94},
            new byte[]{94, 76, 5, 14, 20, 253, 131, 152, 228, 12, 126, 143, 191},
            new byte[]{190, 126, 16, 228, 107, 94, 11, 76, 5, 223,152, 132, 57},
            new byte[]{56, 126, 14, 143, 76, 5, 1, 152, 164, 110, 107, 179, 6},
            new byte[]{6, 76, 10, 28, 152, 176, 97, 42, 186, 2,20, 118, 64},
            new byte[]{64, 76, 3, 8, 20, 137, 46, 126, 134, 66, 107, 40, 2},
            new byte[]{2, 76, 6, 44, 107, 47, 9, 152, 133, 251, 42,48, 62},
            new byte[]{62, 20, 177, 194, 152, 188, 61, 107, 37, 11, 42, 229, 184},
            new byte[]{184, 42, 214, 2, 76, 1, 75, 107, 236, 3, 152,91, 131},
            new byte[]{130, 107, 194, 7, 20, 190, 43, 152, 214, 202, 42, 119, 146},
            new byte[]{146, 152, 96, 250, 20, 210, 48, 76, 9, 85,126, 14, 36},
            new byte[]{36, 76, 11, 208, 20, 231, 150, 152, 112, 249, 107, 121, 11},
            new byte[]{10, 152, 111, 137, 76, 11, 48, 107, 64,7, 20, 32, 45},
            new byte[]{44, 107, 154, 9, 42, 173, 218, 20, 190, 140, 76, 11, 203},
            new byte[]{202, 126, 13, 214, 42, 220, 81, 20, 224, 12, 107, 14, 3},
            new byte[]{2, 76, 11, 255, 42, 208, 198, 107, 30, 3, 126, 134, 74},
            new byte[]{74, 126, 183, 154, 76, 5, 179, 20, 117, 224, 107, 192, 11},
            new byte[]{10, 20, 135, 120, 76, 1, 208, 152, 4, 110, 42, 40, 7},
            new byte[]{6, 42, 152, 6, 107, 10, 10, 76, 10, 81, 152, 244, 235},
            new byte[]{234, 107, 10, 3, 20, 227, 29, 126, 216, 163, 152, 160,72},
            new byte[]{72, 107, 44, 9, 20, 80, 4, 152, 128, 64, 126, 249, 51},
            new byte[]{50, 152, 97, 223, 20, 2, 37, 126, 196, 75, 42, 160,102},
            new byte[]{102, 20, 29, 71, 126, 133, 14, 42, 132, 161, 152, 188, 230},
            new byte[]{230, 20, 244, 55, 152, 14, 49, 107, 116, 6, 42,59, 138},
            new byte[]{138, 20, 87, 204, 126, 163, 12, 152, 80, 84, 42, 242, 171},
            new byte[]{170, 20, 61, 6, 126, 244, 69, 107, 196, 1,76, 1, 195},
            new byte[]{194, 126, 5, 82, 42, 234, 81, 76, 3, 239, 20, 255, 216},
            new byte[]{216, 126, 69, 19, 42, 84, 96, 152, 82, 76,20, 160, 248},
            new byte[]{248, 152, 10, 208, 76, 9, 3, 107, 37, 9, 20, 116, 29},
            new byte[]{28, 152, 174, 6, 20, 185, 107, 126, 177, 45,107, 0, 7},
            new byte[]{6, 20, 88, 4, 76, 5, 32, 126, 161, 4, 152, 58, 212},
            new byte[]{212, 152, 140, 61, 20, 112, 143, 76, 6, 16, 126,2, 192},
            new byte[]{192, 76, 2, 247, 126, 165, 99, 42, 1, 41, 20, 17, 149},
            new byte[]{148, 20, 194, 26, 152, 194, 76, 107, 204, 6, 42,49, 193},
            new byte[]{192, 20, 64, 19, 126, 116, 135, 152, 149, 75, 42, 79, 21},
            new byte[]{20, 20, 33, 219, 152, 47, 3, 42, 171, 88,126, 72, 223},
            new byte[]{222, 20, 20, 240, 152, 3, 9, 42, 226, 212, 76, 2, 195},
            new byte[]{194, 152, 160, 67, 20, 128, 11, 42, 45, 1,76, 2, 87},
            new byte[]{86, 107, 17, 3, 42, 226, 14, 152, 183, 84, 20, 146, 224},
            new byte[]{224, 76, 6, 88, 152, 160, 96, 42, 16, 64,20, 238, 76},
            new byte[]{76, 152, 42, 3, 76, 5, 44, 20, 176, 19, 42, 212, 235},
            new byte[]{234, 152, 2, 29, 20, 91, 17, 42, 119, 128,126, 216, 117},
            new byte[]{116, 107, 5, 2, 126, 171, 22, 20, 33, 147, 76, 6, 184},
            new byte[]{184, 126, 128, 11, 76, 5, 89, 42, 128,142, 20, 29, 163},
            new byte[]{162, 20, 117, 184, 76, 5, 152, 152, 17, 93, 126, 212, 79},
            new byte[]{78, 76, 6, 2, 20, 216, 65, 107, 239, 7, 152, 166, 28},
            new byte[]{28, 126, 94, 189, 152, 96, 183, 107, 4, 6, 42, 164, 67},
            new byte[]{66, 42, 135, 63, 126, 24, 130, 152, 246, 179, 20, 56, 252},
            new byte[]{252, 76, 9, 72, 42, 113, 250, 20, 108, 145, 152, 243,71},
            new byte[]{70, 126, 251, 245, 20, 90, 168, 152, 50, 216, 42, 205, 48},
            new byte[]{48, 20, 32, 234, 152, 15, 29, 76, 7, 2, 42, 160,3},
            new byte[]{2, 20, 208, 161, 126, 100, 193, 76, 1, 10, 42, 254, 113},
            new byte[]{112, 76, 1, 254, 152, 51, 26, 42, 192, 212, 126, 65,128},
            new byte[]{128, 126, 252, 194, 76, 11, 58, 152, 138, 102, 20, 209, 9},
            new byte[]{8, 42, 241, 128, 20, 232, 4, 126, 212, 1, 107,121, 11},
            new byte[]{10, 76, 10, 127, 107, 56, 11, 20, 225, 78, 152, 64, 17},
            new byte[]{16, 76, 9, 30, 126, 28, 250, 20, 63, 26, 152,17, 118},
            new byte[]{118, 42, 162, 10, 20, 22, 128, 152, 128, 72, 76, 9, 138},
            new byte[]{138, 20, 128, 136, 42, 112, 89, 76, 1, 61,107, 4, 7},
            new byte[]{6, 76, 5, 40, 152, 128, 25, 20, 201, 172, 42, 145, 198},
            new byte[]{198, 107, 96, 2, 126, 203, 37, 20, 116, 2,152, 182, 1},
            new byte[]{0, 20, 195, 14, 76, 2, 48, 42, 78, 48, 152, 41, 128},
            new byte[]{128, 152, 8, 212, 20, 41, 6, 42, 244, 186, 76,5, 22},
            new byte[]{22, 126, 40, 128, 20, 99, 5, 42, 209, 97, 76, 2, 0},
            new byte[]{0, 152, 160, 72, 107, 62, 1, 76, 2, 216, 20, 129,12},
            new byte[]{12, 152, 30, 9, 20, 8, 134, 126, 170, 29, 76, 6, 240},
            new byte[]{240, 107, 182, 1, 20, 125, 169, 152, 80, 97, 42, 210, 166},
            new byte[]{166, 42, 19, 26, 152, 99, 20, 76, 2, 64, 126, 248, 202},
            new byte[]{202, 20, 137, 48, 152, 174, 80, 42, 141, 96, 126,134, 241},
            new byte[]{240, 20, 161, 230, 152, 6, 176, 76, 6, 4, 107, 244, 2},
            new byte[]{2, 76, 1, 192, 42, 129, 200, 20, 2, 15, 152,145, 136},
            new byte[]{136, 42, 44, 165, 152, 135, 10, 126, 80, 112, 76, 1, 128},
            new byte[]{128, 152, 192, 208, 42, 110, 21, 76, 1, 38,107, 246, 1},
            new byte[]{0, 126, 29, 1, 152, 98, 1, 76, 10, 240, 20, 25, 208},
            new byte[]{208, 76, 3, 67, 20, 113, 202, 152, 63, 129,42, 1, 186},
            new byte[]{186, 152, 114, 230, 20, 30, 48, 76, 1, 68, 126, 244, 182},
            new byte[]{182, 20, 195, 146, 107, 17, 6, 126, 140,209, 76, 6, 178},
            new byte[]{178, 76, 1, 240, 20, 93, 210, 126, 166, 27, 107, 50, 3},
            new byte[]{2, 76, 2, 239, 20, 65, 65, 152, 91, 27,42, 76, 209},
            new byte[]{208, 126, 71, 29, 107, 138, 6, 152, 99, 25, 20, 99, 141},
            new byte[]{140, 20, 240, 3, 42, 56, 117, 107, 33, 2,76, 9, 166},
            new byte[]{166, 20, 4, 156, 126, 48, 75, 76, 1, 30, 42, 176, 30},
            new byte[]{30, 20, 125, 246, 126, 48, 100, 76, 2, 158,152, 178, 5},
            new byte[]{4, 152, 244, 128, 76, 9, 253, 42, 83, 12, 126, 160, 100},
            new byte[]{100, 107, 150, 2, 126, 88, 218, 20, 99,128, 76, 1, 114},
            new byte[]{114, 76, 2, 236, 126, 82, 125, 152, 222, 250, 20, 17, 32},
            new byte[]{32, 20, 12, 152, 107, 26, 11, 42, 128, 208, 126, 218, 210},
            new byte[]{210, 152, 194, 21, 20, 11, 208, 126, 50, 15, 107, 217,3},
            new byte[]{2, 152, 232, 215, 20, 19, 196, 76, 9, 80, 126, 144, 162},
            new byte[]{162, 126, 159, 48, 20, 197, 26, 42, 106, 11, 76, 3,216},
            new byte[]{216, 20, 145, 105, 42, 157, 17, 152, 186, 243, 76, 7, 62},
            new byte[]{62, 42, 1, 224, 126, 196, 19, 76, 11, 22, 152,131, 131},
            new byte[]{130, 42, 244, 80, 126, 48, 155, 76, 11, 216, 20, 191, 54},
            new byte[]{54, 126, 110, 181, 20, 205, 187, 152, 191,240, 76, 10, 61},
            new byte[]{60, 126, 2, 121, 76, 11, 155, 152, 45, 94, 20, 161, 3},
            new byte[]{2, 20, 53, 254, 152, 61, 240, 126, 81, 129, 107, 85, 2},
            new byte[]{2, 20, 132, 85, 76, 2, 54, 107, 137, 3, 152, 12, 96},
            new byte[]{96, 126, 120, 247, 76, 1, 144, 152, 196, 167, 107, 9, 2},
            new byte[]{2, 152, 19, 179, 126, 56, 52, 76, 5, 104, 20, 19, 192},
            new byte[]{192, 76, 7, 0, 126, 32, 44, 42, 12, 210, 107, 138, 10},
            new byte[]{10, 20, 192, 60, 42, 12, 117, 126, 183, 60, 76, 9, 253},
            new byte[]{252, 152, 3, 96, 107, 28, 1, 126, 240, 1, 76, 3, 28},
            new byte[]{28, 42, 49, 12, 107, 193, 2, 20, 1, 190, 152, 14, 173},
            new byte[]{172, 126, 64, 12, 107, 7, 6, 42, 13, 165, 152, 140, 240},
            new byte[]{240, 152, 243, 237, 107, 168, 7, 20, 14, 160, 42, 129,148},
            new byte[]{148, 42, 13, 208, 126, 72, 14, 20, 192, 1, 152, 22, 68},
            new byte[]{68, 42, 29, 192, 107, 101, 9, 20, 234, 193, 126,222, 208},
            new byte[]{208, 126, 48, 185, 20, 243, 156, 107, 62, 3, 76, 9, 192},
            new byte[]{192, 152, 209, 244, 76, 1, 168, 107, 21, 2,42, 7, 225},
            new byte[]{224, 42, 247, 20, 152, 96, 123, 76, 6, 248, 126, 70, 250},
            new byte[]{250, 42, 80, 67, 20, 232, 30, 76, 7, 214,126, 18, 78},
            new byte[]{78, 20, 56, 199, 42, 97, 19, 107, 253, 3, 76, 6, 220},
            new byte[]{220, 20, 16, 208, 152, 194, 6, 42, 241, 1,107, 209, 5},
            new byte[]{4, 20, 145, 53, 126, 23, 33, 42, 1, 208, 76, 10, 240},
            new byte[]{240, 107, 113, 1, 126, 159, 186, 42, 191,120, 76, 5, 96},
            new byte[]{96, 76, 2, 80, 107, 47, 11, 42, 161, 2, 152, 125, 38},
            new byte[]{38, 152, 165, 214, 20, 209, 128, 107, 107, 11, 42, 174, 54},
            new byte[]{54, 152, 33, 65, 126, 2, 56, 20, 193, 128, 76, 1, 30},
            new byte[]{30, 126, 128, 237, 20, 209, 152, 107, 44, 6, 42, 14, 12},
            new byte[]{12, 20, 128, 35, 107, 78, 11, 42, 81, 2, 152, 206, 186},
            new byte[]{186, 42, 72, 1, 107, 24, 6, 152, 198, 27,76, 7, 190},
            new byte[]{190, 76, 11, 65, 107, 217, 3, 20, 128, 31, 42, 49, 62},
            new byte[]{62, 76, 9, 193, 126, 57, 175, 152, 61, 24, 20, 6, 100},
            new byte[]{100, 152, 191, 213, 20, 88, 51, 76, 2, 70, 126, 140, 120},
            new byte[]{120, 20, 254, 30, 126, 21, 126, 152, 82, 128, 76, 9, 149},
            new byte[]{148, 76, 1, 104, 126, 146, 43, 20, 78, 65, 152, 59, 20},
            new byte[]{20, 126, 160, 34, 152, 25, 31, 20, 188, 200, 76, 2, 16},
            new byte[]{16, 107, 148, 5, 76, 6, 159, 152, 212, 136, 126, 145, 91},
            new byte[]{90, 152, 28, 107, 42, 178, 132, 20, 237, 89, 76, 5, 112},
            new byte[]{112, 20, 6, 240, 126, 56, 176, 42, 79, 190, 76, 11, 240},
            new byte[]{240, 20, 208, 125, 42, 99, 180, 107, 113, 3, 126, 241, 233},
            new byte[]{232, 42, 56, 32, 126, 138, 7, 152, 99, 114, 20, 1, 252},
            new byte[]{252, 107, 212, 3, 76, 2, 20, 20, 7, 32, 42, 237, 177},
            new byte[]{176, 42, 42, 233, 20, 10, 160, 76, 1, 88, 107, 144, 6},
            new byte[]{6, 76, 1, 165, 126, 61, 107, 20, 52, 158, 152, 64, 1},
            new byte[]{0, 42, 176, 201, 20, 43, 128, 152, 19, 205, 126, 248, 78},
            new byte[]{78, 42, 2, 249, 20, 146, 255, 76, 1, 15, 152, 32, 94},
            new byte[]{94, 20, 28, 240, 107, 201, 1, 152, 53, 145, 76, 1, 189},
            new byte[]{188, 76, 6, 29, 20, 208, 194, 126, 13, 129, 107, 102, 9},
            new byte[]{8, 20, 188, 23, 76, 3, 64, 152, 123, 241, 42, 13, 90},
            new byte[]{90, 152, 2, 140, 126, 134, 172, 76, 3, 27, 42, 33, 56},
            new byte[]{56, 20, 200, 22, 76, 1, 118, 42, 44, 124, 152, 16, 240},
            new byte[]{240, 107, 40, 6, 76, 1, 24, 152, 232, 99, 20, 156, 24},
            new byte[]{24, 20, 224, 185, 42, 191, 12, 152, 2, 48, 76, 1, 143},
            new byte[]{142, 20, 4, 130, 42, 21, 56, 76, 10, 193, 152, 100, 195},
            new byte[]{194, 107, 14, 7, 20, 40, 97, 76, 1, 2, 42, 127, 210},
            new byte[]{210, 20, 21, 52, 42, 174, 242, 126, 2, 4, 76, 1, 56},
            new byte[]{56, 152, 42, 134, 107, 231, 1, 20, 47, 152, 42, 6, 137},
            new byte[]{136, 107, 1, 3, 152, 178, 38, 20, 40, 222, 42, 155, 120}};

        public static void Op_code_add(int high_byte, int low_byte)
        {
            int i1;
            int i3;
            high_byte = (high_byte << 8);
            i1 = (high_byte | low_byte);
            i3 = (i1 + key_value);
            key_value = (65535 & i3);
        }

        public static void Op_code_comp(int high_byte, int low_byte)
        {
            if (high_byte > low_byte)
            {
                key_value = (65535 & (~key_value));
            }
            else
                key_value = (65535 & (~(key_value) + 1));
        }

        public static void Op_code_rot_lt(int high_byte, int low_byte)
        {
            int i1;
            int i2;
            i2 = key_value;
            i1 = key_value;
            i2 = ((i2 << (high_byte & 31)));
            i1 = ((i1 >> ((16 - high_byte) & 31)));
            key_value = (65535 & (i2 | i1));
        }

        public static void Op_code_rot_rt(int high_byte, int low_byte)
        {
            int i1;
            int i2;
            i2 = key_value;
            i1 = key_value;
            i2 = ((i2 >> (low_byte & 31)));
            i1 = ((i1 << ((16 - low_byte) & 31)));
            key_value = (65535 & (i2 | i1));
        }

        public static void Op_code_sub(int high_byte, int low_byte)
        {
            int i2;
            int i3;
            high_byte = (high_byte << 8);
            i2 = (high_byte | low_byte);
            i3 = (key_value - i2);
            key_value = (65535 & i3);
        }

        public static void Op_code_swap_add(int high_byte, int low_byte)
        {
            int i1;
            int i2;
            int i3;
            int i4;
            int i5;
            i2 = (key_value & 65280);
            i3 = (key_value & 255);
            i2 = (i2 >> 8);
            i3 = (i3 << 8);
            i4 = (i2 | i3);
            if (high_byte >= low_byte)
            {
                i1 = (255 & high_byte) << 8;
                i1 = i1 | low_byte;
                i5 = (i4 + i1);
            }
            else
            {
                i1 = (255 & low_byte);
                i1 = (i1 << 8);
                high_byte = (255 & high_byte);
                i1 = i1 | high_byte;
                i5 = (i4 + i1);
            }
            key_value = (65535 & i5);
        }

        public static int KeyAlgo(int seed, int algo)
        {
            key_value = seed;
            bool Done = true;
            int byte1 = 1;
            bool loop = true;
            int byte2 = 0;

            while (loop)
            {
                if ((!Done))
                {
                    loop = false;
                }
                else
                {
                    byte2 = bytearray1[algo][byte1];
                    switch (byte2)
                    {
                        case 20:
                            Op_code_add(bytearray1[algo][byte1 + 1], bytearray1[algo][byte1 + 2]);
                            break;
                        case 42:
                            Op_code_comp(bytearray1[algo][byte1 + 1], bytearray1[algo][byte1 + 2]);
                            break;
                        case 76:
                            Op_code_rot_lt(bytearray1[algo][byte1 + 1], bytearray1[algo][byte1 + 2]);
                            break;
                        case 107:
                            Op_code_rot_rt(bytearray1[algo][byte1 + 1], bytearray1[algo][byte1 + 2]);
                            break;
                        case 126:
                            Op_code_swap_add(bytearray1[algo][byte1 + 1], bytearray1[algo][byte1 + 2]);
                            break;
                        case 152:
                            Op_code_sub(bytearray1[algo][byte1 + 1], bytearray1[algo][byte1 + 2]);
                            break;
                    }
                    if (byte1 >= 10)
                    {
                        Done = false;
                    }
                    else
                    {
                        byte1 = byte1 + 3;
                    }
                }
            }
            return key_value;
        }
    }
}
