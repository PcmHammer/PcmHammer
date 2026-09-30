// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// What a UI needs to know about a VIN the user typed. The standard's check digit is not
    /// universal - CAN PCMs are routinely found with VINs that fail it - so <see cref="CanWrite"/>
    /// enforces only the 17-character protocol requirement and <see cref="MeetsStandard"/> is advisory.
    /// </summary>
    public sealed class VinAssessment
    {
        /// <summary>True when the VIN satisfies the North American standard, check digit included.</summary>
        public bool MeetsStandard { get; }

        /// <summary>True when the VIN is 17 characters, the only hard requirement.</summary>
        public bool CanWrite { get; }

        /// <summary>The message to show beside the input.</summary>
        public string Message { get; }

        /// <summary>
        /// The check digit that would satisfy the standard, or 'X' when there is nothing to fix.
        /// Drives a "fix the check digit" button.
        /// </summary>
        public char SuggestedCheckDigit { get; }

        private VinAssessment(bool meetsStandard, bool canWrite, string message, char suggestedCheckDigit)
        {
            this.MeetsStandard = meetsStandard;
            this.CanWrite = canWrite;
            this.Message = message;
            this.SuggestedCheckDigit = suggestedCheckDigit;
        }

        /// <summary>Assess a VIN the user has typed.</summary>
        public static VinAssessment Of(string? vin)
        {
            vin = (vin ?? string.Empty).Trim().ToUpperInvariant();

            if (vin.Length != 17)
            {
                return new VinAssessment(
                    meetsStandard: false,
                    canWrite: false,
                    message: string.Format(
                        "The VIN must be 17 characters long.{0}This is {1} characters.",
                        Environment.NewLine,
                        vin.Length),
                    suggestedCheckDigit: 'X');
            }

            if (VinValidator.IsValid(vin, out int invalidCharacterIndex, out char requiredCheckDigit))
            {
                return new VinAssessment(true, true, "The VIN is valid. Good!", 'X');
            }

            if (invalidCharacterIndex >= 0)
            {
                // Almost certainly a typo, but still 17 bytes the PCM will accept: the user decides.
                return new VinAssessment(
                    meetsStandard: false,
                    canWrite: true,
                    message: string.Format(
                        "The \"{0}\" at position {1} is not a letter or number.",
                        vin[invalidCharacterIndex],
                        invalidCharacterIndex + 1),
                    suggestedCheckDigit: 'X');
            }

            if (requiredCheckDigit != 'X')
            {
                return new VinAssessment(
                    meetsStandard: false,
                    canWrite: true,
                    message: string.Format(
                        "The check digit at position 9 does not match the standard (expected {0}).{1}"
                            + "This is normal for some CAN PCMs; you can still write it.",
                        requiredCheckDigit,
                        Environment.NewLine),
                    suggestedCheckDigit: requiredCheckDigit);
            }

            return new VinAssessment(
                meetsStandard: false,
                canWrite: true,
                message: "The VIN does not meet the standard, but you can still write it.",
                suggestedCheckDigit: 'X');
        }

        /// <summary>
        /// The VIN with the standard check digit applied at position 9, or the input unchanged when
        /// there is nothing to fix.
        /// </summary>
        public string ApplySuggestedCheckDigit(string vin)
        {
            if (this.SuggestedCheckDigit == 'X' || vin == null || vin.Length != 17)
            {
                return vin ?? string.Empty;
            }

            char[] characters = vin.ToCharArray();
            characters[8] = this.SuggestedCheckDigit;   // position 9 (one-based) is index 8
            return new string(characters);
        }
    }
}
