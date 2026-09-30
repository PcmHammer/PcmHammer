// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// Which operations a detected PCM can actually be asked to do, and what a UI should default to.
    /// Shared so the front ends' pickers cannot disagree.
    /// </summary>
    public sealed class OperationOptions
    {
        /// <summary>The write types to offer, in display order. Empty when writing is not possible.</summary>
        public IReadOnlyList<WriteType> WriteTypes { get; }

        /// <summary>The PCM detection found, or null when nothing answered.</summary>
        public OSIDInfo? Detected { get; }

        /// <summary>Whether a write can be offered at all.</summary>
        public bool CanWrite => this.WriteTypes.Count > 0;

        /// <summary>Whether a read can be offered.</summary>
        public bool CanRead { get; }

        /// <summary>One or two lines describing what was detected, for a label beside the choices.</summary>
        public string DetectionMessage { get; }

        private OperationOptions(
            OSIDInfo? detected, IReadOnlyList<WriteType> writeTypes, bool canRead, string detectionMessage)
        {
            this.Detected = detected;
            this.WriteTypes = writeTypes;
            this.CanRead = canRead;
            this.DetectionMessage = detectionMessage;
        }

        /// <summary>
        /// What to offer for the detected PCM. With nothing detected every operation is offered and
        /// the managers reject what the PCM turns out not to support.
        /// </summary>
        public static OperationOptions For(OSIDInfo? detected)
        {
            if (detected == null || !detected.IsSupported)
            {
                return new OperationOptions(
                    detected,
                    AllWriteTypes,
                    canRead: true,
                    detectionMessage: "Could not detect a PCM" + Environment.NewLine + "Choose the options manually");
            }

            if (!detected.IsSupportedWrite)
            {
                // Read-only PCM: there is nothing to write, so offer no write type at all.
                return new OperationOptions(
                    detected,
                    Array.Empty<WriteType>(),
                    canRead: detected.IsSupportedRead,
                    detectionMessage: string.Format(
                        "Detected: {0}{1}Writing is not supported", detected.HardwareType, Environment.NewLine));
            }

            // Clone is always available and stays first, so it remains the fallback default. Segment
            // writes need by-segment support (the E38 is clone-only), parameters need a parameter
            // block, and a test write needs a kernel write path to rehearse.
            bool bySegment = detected.IsSupportedWriteBySegment;
            List<WriteType> offered = new List<WriteType> { WriteType.Full };
            if (bySegment)
            {
                offered.Add(WriteType.OsPlusCalibrationPlusBoot);
                offered.Add(WriteType.Calibration);
            }

            if (detected.HasParameterBlocks)
            {
                offered.Add(WriteType.Parameters);
            }

            if (detected.IsSupportedTestWrite)
            {
                offered.Add(WriteType.TestWrite);
            }

            string message = bySegment
                ? string.Format("Detected: {0}", detected.HardwareType)
                : string.Format(
                    "Detected: {0}{1}Segment writes not supported{1}Clone only",
                    detected.HardwareType,
                    Environment.NewLine);

            return new OperationOptions(detected, offered, canRead: detected.IsSupportedRead, detectionMessage: message);
        }

        /// <summary>Whether a given write type is one of the offered ones.</summary>
        public bool Offers(WriteType writeType) => this.WriteTypes.Contains(writeType);

        /// <summary>
        /// The write type to preselect: the request if supported, else <see cref="WritePlan.DefaultWriteType"/>,
        /// else the first offered. <see cref="WriteType.None"/> only when none is offered.
        /// </summary>
        public WriteType PreferredWriteType(WriteType requested)
        {
            if (this.Offers(requested))
            {
                return requested;
            }

            WriteType fallback = WritePlan.DefaultWriteType();
            if (this.Offers(fallback))
            {
                return fallback;
            }

            return this.WriteTypes.Count > 0 ? this.WriteTypes[0] : WriteType.None;
        }

        /// <summary>
        /// The types a manual picker should list. Excludes <see cref="PcmType.Undefined"/>, which means
        /// auto-detect rather than a type.
        /// </summary>
        public static IEnumerable<PcmType> SelectablePcmTypes()
        {
            foreach (PcmType type in Enum.GetValues(typeof(PcmType)).Cast<PcmType>())
            {
                if (type == PcmType.Undefined)
                {
                    continue;
                }

                if (new OSIDInfo(type).IsSupported)
                {
                    yield return type;
                }
            }
        }

        /// <summary>Every write type, for the no-detection case. Clone first, to match the offered order.</summary>
        private static readonly WriteType[] AllWriteTypes =
        {
            WriteType.Full,
            WriteType.OsPlusCalibrationPlusBoot,
            WriteType.Calibration,
            WriteType.Parameters,
            WriteType.TestWrite,
        };

        /// <summary>The confirmation text to show before a write of this type.</summary>
        public static string DescribeWrite(WriteType writeType)
        {
            switch (writeType)
            {
                case WriteType.Parameters:
                    return "This will update the parameter block on your PCM.";

                case WriteType.OsPlusCalibrationPlusBoot:
                    return "This will replace the operating system and calibration on your PCM.";

                case WriteType.Calibration:
                    return "This will replace the calibration on your PCM.";

                case WriteType.Full:
                    return "This will replace the contents of the flash memory on your PCM.";

                default:
                    return "This will update your PCM.";
            }
        }

        /// <summary>A short label for a write type, for radio buttons and list items.</summary>
        public static string Label(WriteType writeType)
        {
            switch (writeType)
            {
                case WriteType.Full:
                    return "Clone (Full Flash)";

                case WriteType.OsPlusCalibrationPlusBoot:
                    return "Operating System + Calibration";

                case WriteType.Calibration:
                    return "Calibration";

                case WriteType.Parameters:
                    return "Parameters";

                case WriteType.TestWrite:
                    return "Test Write (writes nothing)";

                case WriteType.Compare:
                    return "Compare";

                default:
                    return writeType.ToString();
            }
        }
    }
}
