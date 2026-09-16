// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PcmHacking
{
    /// <summary>
    /// Try to detect corrupted firmware images.
    /// </summary>
    public class FileValidator
    {
        /// <summary>
        /// Known SHA-256 of P11 boot sector bytes [0x000000..0x001FFF] for service number 12210553.
        /// </summary>
        private const string P11BootSectorSha256_12210553 = "7c0d299b51356a50b4f9291cbd3869bd1b7b606df21a5d8d4f4f3b3781e4e8d2";

        /// <summary>
        /// Known SHA-256 of P11 boot sector bytes [0x000000..0x001FFF] for service number 12576162.
        /// </summary>
        private const string P11BootSectorSha256_12576162 = "50db097c55a56378cf71a53e746796d366f3b84b967fdc43f10f80d1daebbbc1";

        /// <summary>
        /// Second known SHA-256 of P11 boot sector bytes [0x000000..0x001FFF] for service number 12576162.
        /// </summary>
        private const string P11BootSectorSha256_12576162_2 = "bbf6af9e1a8f0815a4b087cd5d165ff3271f2643a073893fc9b912fea6a27871";

        /// <summary>
        /// Names of segments in P10 operating systems.
        /// </summary>
        private readonly string[] segmentNames_P10 =
        {
            "Operating system",
            "Engine calibration",
            "Transmission calibration",
            "System",
            "Speedometer",
        };

        /// <summary>
        /// Names of segments in BlackBox operating systems.
        /// </summary>
        private readonly string[] segmentNames_BlackBox =
        {
            "Operating system",
            "Engine calibration",
            "Fuel system",
            "System",
            "Speedometer",
            "VIN",
            "Transmission calibration",
        };

        /// <summary>
        /// Names of segments in P01 and P59 operating systems.
        /// </summary>
        private readonly string[] segmentNames_P01_P59 =
        {
            "Operating system",
            "Engine calibration",
            "Engine diagnostics.",
            "Transmission calibration",
            "Transmission diagnostics",
            "Fuel system",
            "System",
            "Speedometer",
        };

        // Names of segments in P12 operating system are documented in the ValidateChecksums() function

        /// <summary>
        /// The contents of the firmware file that someone wants to flash.
        /// </summary>
        private readonly byte[] image;

        /// <summary>
        /// Optional forced PCM type used in manual selection mode.
        /// </summary>
        private readonly PcmType? forcedType;

        /// <summary>
        /// For reporting progress and success/fail.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Constructor.
        /// </summary>
        public FileValidator(byte[] image, ILogger logger, PcmType? forcedType = null)
        {
            this.image = image;
            this.logger = logger;
            this.forcedType = forcedType;
        }

        /// <summary>
        /// Identify the file type without logging or validating checksums.
        /// Returns Undefined if the file is unrecognised or structurally invalid.
        /// </summary>
        public PcmType GetFileType()
        {
            if (!this.TryPrepareValidation(out PcmType type))
            {
                return PcmType.Undefined;
            }

            return type;
        }

        /// <summary>
        /// Identify the image (size and PCM type) and validate its checksums. Returns true for a
        /// recognized image whose checksums are valid. The OSID is logged by the caller, not here.
        /// </summary>
        public bool IdentifyAndValidate()
        {
            if (this.image.Length == 256 * 1024)
            {
                logger.AddUserMessage("Identifying 256KiB file.");
            }
            else if (this.image.Length == 512 * 1024)
            {
                logger.AddUserMessage("Identifying 512KiB file.");
            }
            else if (this.image.Length == 1024 * 1024)
            {
                logger.AddUserMessage("Identifying 1024KiB file.");
            }
            else if (this.image.Length == 2048 * 1024)
            {
                logger.AddUserMessage("Identifying 2048KiB file.");
            }
            else if (this.image.Length == 4096 * 1024)
            {
                logger.AddUserMessage("Identifying 4096KiB file.");
            }
            else
            {
                logger.AddUserMessage(
                    string.Format(
                        "Files must be 256KiB, 512KiB, 1024KiB, 2048KiB or 4096KiB. This file is {0} / {1:X} bytes long.",
                        this.image.Length,
                        this.image.Length));
                return false;
            }

            try
            {
                PcmType type = this.GetFileType();
                if (type == PcmType.Undefined)
                {
                    return false;
                }

                return this.ValidateChecksums(type);
            }
            catch (Exception exception)
            {
                logger.AddUserMessage("Unable to validate file format/checksums.");
                logger.AddDebugMessage(exception.ToString());
                return false;
            }
        }

        /// <summary>
        /// Compare the OS ID from the PCM with the OS ID from the file.
        /// </summary>
        public bool IsSameOperatingSystem(UInt32 pcmOsid)
        {
            return pcmOsid == this.GetOsidFromImage();
        }

        /// <summary>
        /// Compare the HW type from the PCM with the type determined from the file.
        /// </summary>
        public bool IsSameHardware(UInt32 pcmOsid)
        {
            UInt32 fileOsid = this.GetOsidFromImage();

            OSIDInfo pcmInfo = new OSIDInfo(pcmOsid);
            OSIDInfo fileInfo = new OSIDInfo(fileOsid);

            if (pcmInfo.HardwareType == fileInfo.HardwareType)
            {
                logger.AddUserMessage("PCM and file match hardware " + fileInfo.HardwareType.ToString());
                return true;
            }

            // Cross flashing override: the user has accepted the brick risk, so allow writing a file
            // that does not match the connected PCM (e.g. recovering a P05c that was BDM-flashed with
            // a P05b bin). Advanced recovery / developer use only.
            if (RuntimeSettings.AllowCrossFlashing)
            {
                logger.AddUserMessage(
                    $"Cross flashing enabled: writing a {fileInfo.HardwareType} file to a {pcmInfo.HardwareType} PCM.");
                return true;
            }

            logger.AddUserMessage(
                $"Hardware mismatch: the file is for a {fileInfo.HardwareType} but the connected PCM is a {pcmInfo.HardwareType}. They are not compatible.");
            return false;
        }

        /// <summary>
        /// Get the OSID from the file that the user wants to flash.
        /// </summary>
        public uint GetOsidFromImage()
        {
            if (!this.TryPrepareValidation(out PcmType type))
            {
                return 0;
            }

            return this.GetOsidFromImage(type);
        }

        private uint GetOsidFromImage(PcmType type)
        {
            UInt32 osid = 0;
            switch (type)
            {
                case PcmType.P01:
                case PcmType.P59:
                    osid = ReadUnsigned(image, 0x504);
                    break;

                case PcmType.P04:
                case PcmType.P04_Early:
                    switch (image.Length)
                    {
                        case 256 * 1024:
                            osid = ReadUnsigned(image, 0x3FFFA);
                            break;

                        case 512 * 1024:
                            if (image[0x7FFFE] == 0xFF && image[0x7FFFF] == 0xFF)
                            {
                                osid = ReadUnsigned(image, 0x7FFF8); // some 1998 P04 
                            }
                            else
                            {
                                osid = ReadUnsigned(image, 0x7FFFA);
                            }
                            break;
                    }
                    break;

                case PcmType.P05:
                case PcmType.P05b:
                case PcmType.P05c:
                    if (ReadUnsigned(image, 0x20882) == 0x012380)
                    {
                        logger.AddDebugMessage("P05c Variant, Reading OSID from ASCII at 0x208AA");
                        osid = ReadAsciiUInt32(image, 0x208AA);
                    }
                    else
                    {
                        osid = ReadUnsigned(image, 0xFFFFA);
                    }
                    break;

                case PcmType.P08:
                    osid = ReadUnsigned(image, 0x8000);
                    break;

                case PcmType.P10:
                    osid = ReadUnsigned(image, 0x52E);
                    break;

                case PcmType.P11:
                case PcmType.P12:
                case PcmType.P12b:
                    osid = ReadUnsigned(image, 0x8004);
                    break;

                case PcmType.E54:
                case PcmType.BlackBox:
                    osid = ReadUnsigned(image, 0x20004);
                    break;

                case PcmType.E38:
                    // The operating-system part number is stored as ASCII in the checksum table header
                    // at 0x1000E (e.g. "12628990"); that part number is the OSID.
                    osid = ReadAsciiUInt32(image, 0x1000E);
                    break;

                case PcmType.E92:
                    // The OS segment part number is the OSID: a big-endian uint32 at the OS segment
                    // start + 0x105 (the OS segment starts at the address stored at 0xC0126).
                    osid = this.GetU32BE(unchecked((int)this.GetU32BE(0xC0126)) + 0x105);
                    break;
            }

            return osid;
        }

        /// <summary>
        /// Validate a binary image of a known type
        /// </summary>
        private bool ValidateChecksums(PcmType type)
        {
            bool success = true;
            UInt32 tableAddress = 0;
            UInt32 segments = 0;

            switch (type)
            {
                // The E38 and E92 use their own per-segment Sum and CVN scheme, validated below by
                // ValidateSumAndCvn()/ValidateE92SumAndCvn() rather than the generic segment-table path.
                case PcmType.E38:
                case PcmType.E92:
                    break;

                // have a segment table
                case PcmType.P01:
                case PcmType.P59:
                    tableAddress = 0x50C;
                    segments = 8;
                    break;

                case PcmType.P10:
                    tableAddress = 0x546;
                    segments = 5;
                    break;

                // has a special segment table, handled in ValidateRangeP12()
                case PcmType.P12:
                case PcmType.P12b:
                    tableAddress = 0x2000C;
                    segments = 5;
                    break;

                case PcmType.BlackBox:
                    tableAddress = 0x2000C;
                    segments = 7;
                    break;

                // no segment table
                case PcmType.P04:
                case PcmType.P04_Early:
                case PcmType.P05:
                case PcmType.P05b:
                case PcmType.P05c:
                case PcmType.P08:
                case PcmType.P11:
                case PcmType.E54:
                    break;

                case PcmType.Undefined:
                    return false;

                default:
                    logger.AddDebugMessage("TODO: Implement FileValidator::ValidateChecksums for a " + type.ToString());
                    return false;
            }

            switch (type)
            {
                case PcmType.P04_Early:
                case PcmType.P04:
                case PcmType.P05:
                case PcmType.P05b:
                case PcmType.P05c:
                    success &= ValidateParamBlockP04();
                    if (ReadUnsigned(image, 0x20882) == 0x012380) { // P05c special case
                        LogChecksumTableHeader();
                        success &= ValidateRangeWordSum(type, 0x0000, 0xFFFFF, 0x20880, "Operating System");
                        success &= ValidateRangeWordSum(type, 0x8002, 0x1FFFF, 0x8000, "Engine Calibration");
                    }
                    else
                    {
                        LogChecksumTableHeader();
                        success &= ValidateRangeP04(true);
                    }
                    break;
                case PcmType.P08:
                    LogChecksumTableHeader();
                    success &= ValidateRangeByteSum(type, 0, 0x7FFFB, 0x8004, "Whole File");
                    break;
                case PcmType.P11:
                    LogChecksumTableHeader();
                    success &= ValidateRangeWordSum(type, 0, 0x7FFFB, 0x8000, "Whole File");
                    break;
                // The P12b shares the P12's checksum records; only the flash behind them is bigger.
                case PcmType.P12:
                case PcmType.P12b:
                    LogChecksumTableHeader();
                    success &= ValidateRangeP12(0x922, 0x900, 0x94A, 2, "Boot Block");
                    success &= ValidateRangeP12(0x8022, 0, 0x804A, 2, "Operating System");
                    success &= ValidateRangeP12(0x80C4, 0, 0x80E4, 2, "Engine Calibration");
                    success &= ValidateRangeP12(0x80F7, 0, 0x8117, 2, "Engine Diagnostics");
                    success &= ValidateRangeP12(0x812A, 0, 0x814A, 2, "Transmission Calibration");
                    success &= ValidateRangeP12(0x815D, 0, 0x817D, 2, "Transmission Diagnostics");
                    success &= ValidateRangeP12(0x805E, 0, 0x807E, 2, "Speedometer");
                    success &= ValidateRangeP12(0x8091, 0, 0x80B1, 2, "System");
                    break;
                case PcmType.E38:
                    // The E38 has its own per-segment Sum and CVN scheme. A bad sum means the image is
                    // corrupt; a bad CVN is only a warning, so it does not fail validation here.
                    return this.ValidateSumAndCvn() != SumCvnVerdict.SumError;

                case PcmType.E92:
                    // The E92 uses the same policy with its own per-segment Sum and CVN scheme.
                    return this.ValidateE92SumAndCvn() != SumCvnVerdict.SumError;

                case PcmType.E54:
                    LogChecksumTableHeader();
                    success &= ValidateRangeWordSum(type, 0x20002, 0x6FFFF, 0x20000, "Operating System");
                    success &= ValidateRangeWordSum(type, 0x8002, 0x19FFF, 0x8000, "Engine Calibration");
                    success &= ValidateRangeWordSum(type, 0x1A002, 0x1C7FF, 0x1A000, "Engine Diagnostics");
                    success &= ValidateRangeWordSum(type, 0x1C002, 0x1DFFF, 0x1C000, "Fuel");
                    success &= ValidateRangeWordSum(type, 0x1E002, 0x1EFFF, 0x1E000 , "System");
                    success &= ValidateRangeWordSum(type, 0x1F002, 0x1FFEF, 0x1F000, "Speedometer");
                    break;

                // The rest can use the generic code
                default:
                    LogChecksumTableHeader();
                    for (UInt32 segment = 0; segment < segments; segment++)
                    {
                        UInt32 startAddressLocation = tableAddress + (segment * 8);
                        UInt32 endAddressLocation = startAddressLocation + 4;

                        UInt32 startAddress = ReadUnsigned(image, startAddressLocation);
                        UInt32 endAddress = ReadUnsigned(image, endAddressLocation);

                        // For most segments, the first two bytes are the checksum, so they're not counted in the computation.
                        // For the overall "segment" in a P01/P59 the checkum is in the middle.
                        UInt32 checksumAddress;
                        switch (type)
                        {
                            case PcmType.P01:
                            case PcmType.P59:
                                checksumAddress = startAddress == 0 ? 0x500 : startAddress;
                                break;

                            case PcmType.P10:
                                checksumAddress = startAddress == 0 ? 0x52A : startAddress;
                                break;

                            default:
                                checksumAddress = startAddress;
                                break;
                        }

                        if (startAddress != 0)
                        {
                            startAddress += 2;
                        }

                        string segmentName;
                        switch (type)
                        {
                            case PcmType.P10:
                                segmentName = segmentNames_P10[segment];
                                break;

                            case PcmType.BlackBox:
                                segmentName = segmentNames_BlackBox[segment];
                                break;

                            default:
                                segmentName = segmentNames_P01_P59[segment];
                                break;
                        }

                        if ((startAddress >= image.Length) || (endAddress >= image.Length) || (checksumAddress >= image.Length))
                        {
                            logger.AddUserMessage("Checksum table is corrupt.");
                            return false;
                        }

                        success &= ValidateRangeWordSum(type, startAddress, endAddress, checksumAddress, segmentName);
                    }
                    break;
            }
            return success;
        }

        /// <summary>
        /// 
        /// </summary>
        private UInt32 ReadUnsigned(byte[] image, UInt32 offset)
        {
            return BitConverter.ToUInt32(image.Skip((int)offset).Take(4).Reverse().ToArray(), 0);
        }

        /// <summary>
        /// ReadIntFromASCII, used to convert a number from ascii text to an int
        /// Reads ascii numbers up to a max of 16 bytes deeps, protects from overflow
        /// returns 0 if it hits the max length, or the data read is not ascii numerical
        /// </summary>
        public uint ReadAsciiUInt32(byte[] image, uint offset)
        {
            uint max = (uint)Math.Min((uint)image.Length, offset + 16);

            uint end = offset;
            while (end < max && image[end] != 0) end++;

            uint val = 0;
            for (uint i = offset; i < end; i++)
            {
                byte b = image[i];
                if (b < '0' || b > '9') return 0;
                val = val * 10 + (uint)(b - '0');
            }

            return end == offset ? 0 : val;
        }

        /// <summary>
        /// Resolve the PCM type this file will be treated as: the caller's forced type when one was
        /// supplied (manual selection), otherwise the type detected from the file's own content.
        /// </summary>
        private PcmType ResolvePcmType()
        {
            if (this.forcedType.HasValue && this.forcedType.Value != PcmType.Undefined)
            {
                return this.forcedType.Value;
            }

            return this.DetectFileType();
        }

        /// <summary>
        /// Identify the PCM type from the file's content signatures alone, ignoring any forced type.
        /// Returns Undefined if the content matches no known PCM. Use this to confirm a file matches a
        /// manually selected type without trusting (or querying) anything else.
        /// </summary>
        public PcmType DetectFileType()
        {
            // All currently supported bins are 256KiB, 512KiB, 1024KiB, 2048KiB or 4096KiB
            if ((image.Length != 256 * 1024) && (image.Length != 512 * 1024) && (image.Length != 1024 * 1024) && (image.Length != 2048 * 1024) && (image.Length != 4096 * 1024))
            {
                logger.AddUserMessage("Files of size " + image.Length.ToString("X8") + " are not supported.");
                return PcmType.Undefined;
            }

            // 256Kb Type
            // P04 512Kb
            if (image.Length == 256 * 1024)
            {
                logger.AddDebugMessage("Trying P04 256KiB");
                if ((image[0x3FFFE] == 0xA5) && (image[0x3FFFF] == 0x5A))
                {
                    return PcmType.P04_Early;
                }
            }

            // 512Kb Types
            if (image.Length == 512 * 1024)
            {
                // E54 512Kb
                // Must be before P01, P01 can pass for a E54, but an E54 cannot pass as a P01
                logger.AddDebugMessage("Trying E54 512KiB");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        if ((image[0x3FFC] == 0) && (image[0x3FFD] == 0) && (image[0x3FFE] == 0) && (image[0x3FFF] == 0)) { // This prevents 98/99 Black Box being detected at E54
                            return PcmType.E54;
                        }
                    }
                }

                // BlackBox 512Kb. Thanks to Universal Patcher team for the logic in autodetect.xml
                logger.AddDebugMessage("Trying Vortec BlackBox 512KiB");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        if ((image[0x20002] == 00) && (image[0x20003] == 01) && (image[0x2000A] == 01) && (image[0x2000B] == 00))
                        {
                            return PcmType.BlackBox;
                        }
                    }
                }

                // P01 512Kb
                logger.AddDebugMessage("Trying P01 512KiB");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        return PcmType.P01;
                    }
                }

                // P04 512Kb
                logger.AddDebugMessage("Trying P04 512KiB");
                // Last 4 bytes:
                // A5 5A FF FF = P04
                // XX XX XX XX A5 5A = P04 (XX is the OSID)
                if (((image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0x5A)) || // most P04 OR
                    ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xFF) && (image[0x7FFFF] == 0xFF)))   // Most 1998 512Kb eg Malibu 09369193, Olds 09352676, LeSabre 09379801...
                {
                    // A 512KiB image with these markers is a P04 by SIZE alone. But some early P04
                    // units (P04_Early) physically carry a 512KiB chip even though they belong to the
                    // 1996/97 V6 family - their OSID resolves to P04_Early via the service-number
                    // lookup. Identifying by size alone would mislabel them P04, so the file would not
                    // match the connected P04_Early PCM at the pre-flight checks and the 512K P04_early
                    // would be effectively unwriteable with the correct 512KiB file.
                    UInt32 osid = (image[0x7FFFE] == 0xFF && image[0x7FFFF] == 0xFF)
                        ? ReadUnsigned(image, 0x7FFF8)
                        : ReadUnsigned(image, 0x7FFFA);
                    if (osid != 0 && new OSIDInfo(osid).HardwareType == PcmType.P04_Early)
                    {
                        logger.AddDebugMessage("512KiB P04 image OSID resolves to P04_Early; using P04_Early.");
                        return PcmType.P04_Early;
                    }
                    return PcmType.P04;
                }

                logger.AddDebugMessage("Trying P10 512KiB");
                if ((image[0x17FFE] == 0x55) && (image[0x17FFF] == 0x55))
                {
                    if ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0xA5))
                    {
                        if ((image[0x534] == 0) && (image[0x535] == 00))
                        {
                            return PcmType.P10;
                        }
                    }
                }

                // P11 512KiB (boot-sector SHA-256 + 0x7FFFC marker).
                logger.AddDebugMessage("Trying P11 512KiB boot hash + marker");
                if (this.HasP11TailMarkerAt7FFFC() && this.IsKnownP11BootSector())
                {
                    return PcmType.P11;
                }

                // P08 512KiB
                logger.AddDebugMessage("Trying P08 512KiB");
                if ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0xA5))
                {
                    return PcmType.P08;
                }
            }

            // 1MiB types
            if (image.Length == 1024 * 1024)
            {
                logger.AddDebugMessage("Trying P59 1024KiB");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0xFFFFE] == 0x4A) && (image[0xFFFFF] == 0xFC))
                    {
                        return PcmType.P59;
                    }
                }

                // P05/P05c 1024KiB
                logger.AddDebugMessage("Trying P05/P05c 1024KiB");
                if ((image[0xFFFFE] == 0xA5) && (image[0xFFFFF] == 0x5A))
                {
                    if ((image[0x1FFFE] == 0xA5) && (image[0x1FFFF] == 0x5A) &&
                        (image[0x20883] == 0x01) && (image[0x20884] == 0x23) && (image[0x20885] == 0x80))
                    {
                        return PcmType.P05c;
                    }

                    UInt32 osid = ReadUnsigned(image, 0xFFFFA);
                    if (osid != 0 && new OSIDInfo(osid).HardwareType == PcmType.P05b)
                    {
                        return PcmType.P05b;
                    }
                    return PcmType.P05;
                }

                // P11 1024KiB (boot-sector SHA-256 + 0x7FFFC marker).
                logger.AddDebugMessage("Trying P11 1024KiB boot hash + marker");
                if (this.HasP11TailMarkerAt7FFFC() && this.IsKnownP11BootSector())
                {
                    return PcmType.P11;
                }

                logger.AddDebugMessage("Trying P12 1024KiB");
                if ((image[0xFFFF8] == 0xAA) && (image[0xFFFF9] == 0x55))
                {
                    return PcmType.P12;
                }
            }

            // 2024KiB types
            if (image.Length == 2048 * 1024)
            {
                logger.AddDebugMessage("Trying E38 2048KiB");
                if (this.LooksLikeE38())
                {
                    return PcmType.E38;
                }

                logger.AddDebugMessage("Trying P12b 2048KiB");
                if ((image[0x17FFF8] == 0xAA) && (image[0x17FFF9] == 0x55))
                {
                    return PcmType.P12b;
                }

            }

            // 4096KiB types
            if (image.Length == 4096 * 1024)
            {
                logger.AddDebugMessage("Trying E92 4096KiB");
                if (this.LooksLikeE92())
                {
                    return PcmType.E92;
                }
            }

            logger.AddDebugMessage("Unable to identify or validate bin image content");
            return PcmType.Undefined;
        }

        /// <summary>
        /// Column layout shared by every checksum/CVN table below. Flush-left and space-padded
        /// (no leading indent, no tabs) so the columns line up in the log's proportional font,
        /// matching the kernel CRC tables.
        /// </summary>
        private const string ChecksumTableFormat = "{0,-8}  {1,-8}  {2,-8}  {3,-8}  {4,-7}  {5}";

        /// <summary>
        /// Print the standard checksum table header.
        /// </summary>
        private void LogChecksumTableHeader()
        {
            logger.AddUserMessage(string.Format(ChecksumTableFormat, "Start", "End", "Stored", "Needed", "Verdict", "Segment Name"));
        }

        /// <summary>
        /// Print one checksum table row. Hex columns are passed pre-formatted so each table can use
        /// the digit width that suits its values (4-digit word sums, 8-digit P04 sums, etc.).
        /// </summary>
        private void LogChecksumTableRow(string start, string end, string stored, string needed, bool verdict, string name)
        {
            this.LogChecksumTableRow(start, end, stored, needed, verdict ? "Good" : "BAD", name);
        }

        /// <summary>
        /// Print one checksum table row with an explicit verdict, for rows that are neither good nor bad
        /// (e.g. "n/a" where a protected block cannot be read back and so cannot be checked).
        /// </summary>
        private void LogChecksumTableRow(string start, string end, string stored, string needed, string verdict, string name)
        {
            logger.AddUserMessage(string.Format(ChecksumTableFormat, start, end, stored, needed, verdict, name));
        }

        /// <summary>
        /// Print the header for the table of checksums.
        /// </summary>
        private void PrintHeader()
        {
            logger.AddUserMessage(string.Format(ChecksumTableFormat, "Start", "End", "Result", "File", "Actual", "Content"));
        }

        /// <summary>
        /// Validate a range (8 bit bytes).
        /// </summary>
        private bool ValidateRangeByteSum(PcmType type, UInt32 start, UInt32 end, UInt32 storage, string description)
        {
            UInt16 storedChecksum = (UInt16)((this.image[storage] << 8) + this.image[storage + 1]);
            UInt16 computedChecksum = 0;

            for (UInt32 address = start; address <= end; address ++)
            {
                switch (type)
                {
                    case PcmType.P08:
                        if (address == 0x4000)
                        {
                            address = 0x8010;
                        }

                        break;
                }

                computedChecksum += this.image[address];
            }

            bool verdict = storedChecksum == computedChecksum;

            LogChecksumTableRow(start.ToString("X6"), end.ToString("X6"), storedChecksum.ToString("X4"), computedChecksum.ToString("X4"), verdict, description);
            return verdict;
        }

        /// <summary>
        /// Validate a range (16 bit words, 2's compliment).
        /// </summary>
        private bool ValidateRangeWordSum(PcmType type, UInt32 start, UInt32 end, UInt32 storage, string description)
        {
            UInt16 storedChecksum = (UInt16)((this.image[storage] << 8) + this.image[storage + 1]);
            UInt16 computedChecksum = 0;

            for (UInt32 address = start; address <= end; address += 2)
            {

                // Sums cannot be part of their own calculation, so they are always skipped
                // Used by P01_P59, P05c, P10
                if (address == storage) 
                {
                    address += 2;
                }
                switch (type)
                {
                    case PcmType.P01:
                    case PcmType.P59:
                        if (address == 0x4000)
                        {
                            address = 0x20000;
                        }
                        break;

                    case PcmType.P05: // Only used for P05c, Other P05s use P04 routines.
                    case PcmType.P05b:
                    case PcmType.P05c:
                        if (address == 0x4000)
                        {
                            address = 0x20000;
                        }
                        break;

                    case PcmType.P08:
                        if (address == 0x4000)
                        {
                            address = 0x8010;
                        }
                        break;

                    case PcmType.P10:
                        switch (address)
                        {
                            case 0x4000:
                                address = 0x20000;
                                break;
                            case 0x7FFFA:
                                end = 0x7FFFA; // Short circuit to the end
                                break;
                        }
                        break;
                    case PcmType.P11:
                        switch (address)
                        {
                            case 0x4000:
                                address = 0x8002;
                                break;
                            case 0x18000:
                                address = 0x20000;
                                break;
                        }
                        break;
                }

                UInt16 value = (UInt16)(this.image[address] << 8);
                value |= this.image[address + 1];
                computedChecksum += value;
            }

            computedChecksum = (UInt16)((0 - computedChecksum));

            bool verdict = storedChecksum == computedChecksum;

            LogChecksumTableRow(start.ToString("X6"), end.ToString("X6"), storedChecksum.ToString("X4"), computedChecksum.ToString("X4"), verdict, description);
            return verdict;
        }

        /// <summary>
        /// Validate a range for P12
        /// Its so different it gets its own routine...
        /// </summary>
        private bool ValidateRangeP12(UInt32 segment, UInt32 offset, UInt32 index, UInt32 blocks, string description)
        {
            UInt16 storedChecksum = 0;
            UInt16 computedChecksum = 0;
            int first = 0;
            int start = 0;
            int end = 0;

            int sumaddr;
            sumaddr  = image[segment + 0] << 24;
            sumaddr += image[segment + 1] << 16;
            sumaddr += image[segment + 2] << 8;
            sumaddr += image[segment + 3];

            storedChecksum = (UInt16)((this.image[sumaddr + offset] << 8) + this.image[sumaddr + offset + 1]);

            // Lookup the start and end of each block, and add it to the sum
            for (UInt32 block = 0; block < blocks; block++)
            {
                UInt32 blockOffset = index + (block * 8);

                start  = image[blockOffset + 0] << 24;
                start += image[blockOffset + 1] << 16;
                start += image[blockOffset + 2] << 8;
                start += image[blockOffset + 3];
                end    = image[blockOffset + 4] << 24;
                end   += image[blockOffset + 5] << 16;
                end   += image[blockOffset + 6] << 8;
                end   += image[blockOffset + 7];

                for (UInt32 address = (UInt32) start; address <= end; address += 2)
                {
                    UInt16 value = (UInt16)(this.image[address] << 8);
                    value |= this.image[address + 1];
                    computedChecksum += value;
                }

                if (block == 0)
                {
                    first = start;
                }
            }

            computedChecksum = (UInt16) ((0 - computedChecksum)); // 2s compliment

            bool verdict = storedChecksum == computedChecksum;

            LogChecksumTableRow(
                first.ToString("X6"), // The start of the first block
                end.ToString("X6"),   // The end of the last block
                storedChecksum.ToString("X4"),
                computedChecksum.ToString("X4"),
                verdict,
                description);
            return verdict;
        }

        /// <summary>
        /// Validate a range for P04 and P05. Support 256KiB, 512KiB, 1024KiB images
        /// Early 512KB bins dont have a param block. This code is called with skipparamblock=true
        /// If the first attempt fails it is re-entrant with skipparamblock=false to try again
        /// </summary>
        private bool ValidateRangeP04(bool skipparamblock)
        {
            UInt32 storedChecksum = 0;
            UInt32 computedChecksum = 0;
            UInt32 start = 0;
            UInt32 end = (UInt32)(image.Length);
            UInt32 sumaddr = 0;
            bool sumFound = false;

            // Thanks to Joukoy and Kur4o for Universal Patcher and the idea to use a pattern search for the P04 sum address.
            // Working for all tested 1024KiB (P05)
            if (image.Length == 1024 * 1024)
            {
                for (UInt32 i = start; (i + 19) < end; i++)
                {
                    if (image[i] == 0xE0 &&
                    image[i + 1] == 0x8A &&
                    image[i + 2] == 0xE0 &&
                    image[i + 3] == 0x8A &&
                    image[i + 4] == 0x28 &&
                    image[i + 5] == 0x38 &&
                    //image[i + 6] == 0xCB && // seen CB, CE
                    //image[i + 7] == 0x48 && // seen 48, 57, 98
                    image[i + 8] == 0x98 &&
                    image[i + 9] == 0x82 &&
                    image[i + 10] == 0xC6 &&
                    image[i + 11] == 0x87) // (next is 98 83 26 39, but we have enough)
                    {
                        sumaddr = (UInt32)image[i + 16] << 24;
                        sumaddr |= (UInt32)image[i + 17] << 16;
                        sumaddr |= (UInt32)image[i + 18] << 8;
                        sumaddr |= (UInt32)image[i + 19];
                        logger.AddDebugMessage(string.Format("Pattern found at {0:X8}, sum address {1:X8}", i, sumaddr));
                        sumFound = true;
                        break;
                    }
                }
            }
            else
            {
                // Working for all tested 256K and 512K
                for (UInt32 i = start; (i + 19) < end; i++)
                {
                    if ( image[i    ] == 0x3C &&
                         image[i + 1] == 0x00 &&
                         image[i + 2] == 0x00 &&
                         image[i + 3] == 0xFF &&
                         image[i + 4] == 0xFF &&
                        (image[i + 5] == 0xC0 || image[i + 5] == 0xC6) &&
                        (image[i + 6] == 0x82 || image[i + 6] == 0x86) &&
                        (image[i + 7] == 0x94 || image[i + 7] == 0x96 || image[i + 7] == 0x98) &&
                        (image[i + 8] == 0x80 || image[i + 8] == 0x83) &&
                        (image[i + 9] == 0x20 || image[i + 9] == 0x26) &&
                         image[i + 10] == 0x39 &&
                         //11 CS1
                         //12 CS2
                         //13 CS3
                         //14 CS4
                        (image[i + 15] == 0x2C || image[i + 15] == 0x2E) &&
                        (image[i + 16] == 0x00 || image[i + 16] == 0x03) &&
                         image[i + 17] == 0xE0 &&
                        (image[i + 18] == 0x8E || image[i + 18] == 0x8F) &&
                         image[i + 19] == 0xE0)
                    {
                        sumaddr  = (UInt32)image[i + 11] << 24;
                        sumaddr |= (UInt32)image[i + 12] << 16;
                        sumaddr |= (UInt32)image[i + 13] << 8;
                        sumaddr |= (UInt32)image[i + 14];
                        logger.AddDebugMessage(string.Format("Pattern found at {0:X8}, sum address {1:X8}", i, sumaddr));
                        sumFound = true;
                        break;
                    }
                }
            }

            if (!sumFound || ((sumaddr + 3) >= this.image.Length))
            {
                logger.AddUserMessage("Unable to locate a valid checksum pointer for this P04/P05 image.");
                return false;
            }

            storedChecksum  = (UInt32)image[sumaddr] << 24;
            storedChecksum |= (UInt32)image[sumaddr + 1] << 16;
            storedChecksum |= (UInt32)image[sumaddr + 2] << 8;
            storedChecksum |= (UInt32)image[sumaddr + 3];

            for (UInt32 address = (UInt32)start; address < end; address += 2)
            {
                if (address == sumaddr)
                {
                    address += 4; // skip the sum
                }

                if (address == this.image.Length - 6)
                {
                    address += 4; // skip the OSID
                }

                switch (this.image.Length)
                {
                    // Note: P04 256Kb has no param block to skip
                    case 512 * 1024:
                        if (address == 0x4000 && skipparamblock == true)
                        {
                            address += 0x4000; // The param block started being used in about the 2nd year of 512KB bins
                        }
                        if (address == 0x7FFF8 && image[0x7FFFE] == 0xFF && image[0x7FFFF] == 0xFF)
                        {
                            address += 0x4; // Some 98 have a different sig and dont include the osid
                        }
                        break;
                    case 1024 * 1024: // P05
                        if (address == 0x4000)
                        {
                            address += 0xC000;
                        }
                        break;
                }

                UInt32 value = (UInt32)(this.image[address] << 8);
                value |= this.image[address + 1];
                computedChecksum += value;
            }

            bool verdict = storedChecksum == computedChecksum;

            // try the other type of 512KB if needed
            if (image.Length == 512 * 1024 && verdict == false && skipparamblock == true)
            {
                return ValidateRangeP04(false);
            }

            LogChecksumTableRow(
                0.ToString("X6"),              // The start of the first block
                (image.Length - 1).ToString("X6"), // The end of the last block
                storedChecksum.ToString("X8"),
                computedChecksum.ToString("X8"),
                verdict,
                "Whole File");
            return verdict;
        }

        /// <summary>
        /// The purpose is to block flash of images extracted from TIS that are being circulated.
        /// They have valid checksums but no param block and cause a soft brick.
        /// Consider P04 256KiB, 512KiB (no param block), 512KiB (has param block), and P05 1MiB bin
        /// 256KiB and early 512KB bins dont have a param block. 
        /// Param block may be at 4000-5FFF or 6000-7FFF
        /// 256KiB = skip the check, pass
        /// 512KiB = check if one param block is empty. Pass if both have data (assume no param block)
        /// 512KiB = validate param block if one block is empty, pass or fail
        /// 1MiB   = always validate param block, pass or fail
        /// </summary>
        private bool ValidateParamBlockP04()
        {
            switch (this.image.Length)
            {
                case 256 * 1024:
                    logger.AddDebugMessage("256KiB P04, no param block required");
                    return true;
                case 512 * 1024:
                case 1024 * 1024:
                    if (Utility.IsBlank(this.image, 0x4000, 0x2000) || Utility.IsBlank(this.image, 0x6000, 0x2000))
                    {
                        logger.AddUserMessage("P04/P05 1998+, checking for valid paramater block");
                        if ((image[0x43F6] == 0xA5) && (image[0x43F7] == 0xA0))
                        {
                            logger.AddUserMessage("Param block at 0x4000");
                            return true;
                        }
                        if ((image[0x63F6] == 0xA5) && (image[0x63F7] == 0xA0))
                        {
                            logger.AddUserMessage("Param block at 0x6000");
                            return true;
                        }
                        logger.AddUserMessage("1998+ P04/P05 with missing param block. This file is bad and would soft brick your PCM.");
                        return false;
                    }
                    logger.AddUserMessage("1997 type P04, Param block not needed");
                    return true;
            }
            logger.AddDebugMessage("BUG: ValidateParamBlockP04 called with image of invalid size");
            return false; // unreachable
        }

        /// <summary>
        /// Verify type/size/layout once up front so later stages can assume sane offsets.
        /// </summary>
        private bool TryPrepareValidation(out PcmType type)
        {
            type = this.ResolvePcmType();
            if (type == PcmType.Undefined)
            {
                return false;
            }

            if (!this.ValidateTypeLayout(type))
            {
                logger.AddUserMessage("The file structure does not match the selected or detected PCM type.");
                return false;
            }

            return true;
        }

        private bool ValidateTypeLayout(PcmType type)
        {
            switch (type)
            {
                case PcmType.P01:
                    return this.HasSize(512 * 1024) &&
                        this.HasRange(0x504, 4, "P01/P59 OSID") &&
                        this.HasRange(0x50C, 8 * 8, "P01/P59 segment table");

                case PcmType.P59:
                    return this.HasSize(1024 * 1024) &&
                        this.HasRange(0x504, 4, "P01/P59 OSID") &&
                        this.HasRange(0x50C, 8 * 8, "P01/P59 segment table");

                case PcmType.P04_Early:
                    return this.HasSize(256 * 1024, 512 * 1024);

                case PcmType.P04:
                    return this.HasSize(512 * 1024);

                case PcmType.P05:
                case PcmType.P05b:
                case PcmType.P05c:
                    return this.HasSize(1024 * 1024) &&
                        this.HasRange(0x20882, 4, "P05 variant marker") &&
                        this.HasRange(0xFFFFA, 4, "P05 OSID");

                case PcmType.P08:
                    return this.HasSize(512 * 1024) &&
                        this.HasRange(0x8000, 4, "P08 OSID") &&
                        this.HasRange(0x8004, 2, "P08 checksum");

                case PcmType.P10:
                    return this.HasSize(512 * 1024) &&
                        this.HasRange(0x52E, 4, "P10 OSID") &&
                        this.HasRange(0x546, 5 * 8, "P10 segment table");

                case PcmType.P11:
                    return this.HasSize(512 * 1024, 1024 * 1024) &&
                        this.HasP11TailMarkerAt7FFFC() &&
                        this.IsKnownP11BootSector();

                case PcmType.P12:
                    return this.HasSize(1024 * 1024) &&
                        this.HasRange(0x8004, 4, "P12 OSID") &&
                        this.ValidateP12Layout();

                case PcmType.P12b:
                    return this.HasSize(2048 * 1024) &&
                        this.HasRange(0x8004, 4, "P12b OSID") &&
                        this.ValidateP12Layout();

                case PcmType.E38:
                    return this.HasSize(2048 * 1024) && this.LooksLikeE38();

                case PcmType.E92:
                    return this.HasSize(4096 * 1024) && this.LooksLikeE92();

                case PcmType.E54:
                    return this.HasSize(512 * 1024) &&
                        this.HasRange(0x20004, 4, "E54 OSID");

                case PcmType.BlackBox:
                    return this.HasSize(512 * 1024) &&
                        this.HasRange(0x20004, 4, "BlackBox OSID") &&
                        this.HasRange(0x2000C, 5 * 8, "BlackBox segment table");
            }

            return false;
        }

        private bool ValidateP12Layout()
        {
            UInt32[] layout = new UInt32[]
            {
                0x922, 0x900, 0x94A, 2,
                0x8022, 0, 0x804A, 2,
                0x80C4, 0, 0x80E4, 2,
                0x80F7, 0, 0x8117, 2,
                0x812A, 0, 0x814A, 2,
                0x815D, 0, 0x817D, 2,
                0x805E, 0, 0x807E, 2,
                0x8091, 0, 0x80B1, 2
            };

            for (int i = 0; i < layout.Length; i += 4)
            {
                UInt32 segment = layout[i];
                UInt32 offset = layout[i + 1];
                UInt32 index = layout[i + 2];
                UInt32 blocks = layout[i + 3];

                if (!this.HasRange(segment, 4, "P12 checksum segment pointer"))
                {
                    return false;
                }

                if (!this.HasRange(index, blocks * 8, "P12 checksum block table"))
                {
                    return false;
                }

                UInt32 sumaddr = this.ReadUnsigned(this.image, segment);
                if (!this.HasRange(sumaddr + offset, 2, "P12 checksum location"))
                {
                    return false;
                }

                for (UInt32 block = 0; block < blocks; block++)
                {
                    UInt32 blockOffset = index + (block * 8);
                    UInt32 start = this.ReadUnsigned(this.image, blockOffset);
                    UInt32 end = this.ReadUnsigned(this.image, blockOffset + 4);

                    if (start > end || end >= this.image.Length)
                    {
                        logger.AddDebugMessage("P12 checksum block range is invalid.");
                        return false;
                    }
                }
            }

            return true;
        }

        private bool HasSize(params int[] sizes)
        {
            foreach (int size in sizes)
            {
                if (this.image.Length == size)
                {
                    return true;
                }
            }

            logger.AddDebugMessage("Unexpected file size for selected/detected type.");
            return false;
        }

        private bool HasRange(UInt32 start, UInt32 length, string purpose)
        {
            if (length == 0)
            {
                return true;
            }

            UInt64 endExclusive = (UInt64)start + (UInt64)length;
            if (endExclusive <= (UInt64)this.image.Length)
            {
                return true;
            }

            logger.AddDebugMessage($"{purpose} is out of range.");
            return false;
        }

        /// <summary>
        /// Verify the boot sector hash used to positively identify P11 images.
        /// </summary>
        private bool IsKnownP11BootSector()
        {
            if (!this.HasRange(0x0000, 0x2000, "P11 boot hash range"))
            {
                return false;
            }

            string hash = this.ComputeSha256Hex(0x0000, 0x2000);
            bool match =
                string.Equals(hash, P11BootSectorSha256_12210553, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hash, P11BootSectorSha256_12576162, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(hash, P11BootSectorSha256_12576162_2, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                logger.AddDebugMessage(
                    "P11 boot sector hash mismatch. Found: " + hash +
                    ", Expected one of: " +
                    P11BootSectorSha256_12210553 + " (12210553)," +
                    P11BootSectorSha256_12576162 + " (12576162)," +
                    P11BootSectorSha256_12576162_2 + " (12576162).");
            }

            return match;
        }

        /// <summary>
        /// Verify the P11 marker A5 5A A5 A5 at address 0x7FFFC.
        /// </summary>
        private bool HasP11TailMarkerAt7FFFC()
        {
            if (!this.HasRange(0x7FFFC, 4, "P11 marker range"))
            {
                return false;
            }

            bool match =
                (this.image[0x7FFFC] == 0xA5) &&
                (this.image[0x7FFFD] == 0x5A) &&
                (this.image[0x7FFFE] == 0xA5) &&
                (this.image[0x7FFFF] == 0xA5);

            if (!match)
            {
                logger.AddDebugMessage("P11 marker A5 5A A5 A5 was not found at 0x7FFFC.");
            }

            return match;
        }

        private string ComputeSha256Hex(UInt32 offset, UInt32 length)
        {
            byte[] segment = new byte[(int)length];
            Buffer.BlockCopy(this.image, (int)offset, segment, 0, (int)length);

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(segment);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
        }

        // GM E38 PCM 2 MiB images carry a checksum table at 0x10000 describing six segments, each with
        // a 16-bit two's-complement word sum and a 16-bit CRC (the CVN). A bad sum means the image is
        // corrupt and would brick the PCM (error); a bad CVN only means the calibration-verification
        // number will not match (warning).

        /// <summary>Outcome of <see cref="ValidateSumAndCvn"/>.</summary>
        public enum SumCvnVerdict
        {
            /// <summary>Not a 2 MB image with a readable checksum table.</summary>
            NotApplicable,

            /// <summary>All sums and CVNs are valid.</summary>
            Good,

            /// <summary>Sums are valid but one or more CVNs are bad - usable, but warn the user.</summary>
            CvnWarning,

            /// <summary>One or more sums are bad - the image is corrupt and must not be flashed.</summary>
            SumError,
        }

        /// <summary>
        /// Validate a 2 MiB E38 PCM image and log the per-segment Sum and CVN tables. A bad sum is
        /// an error; a bad CVN is only a warning.
        /// </summary>
        public SumCvnVerdict ValidateSumAndCvn()
        {
            if (this.image.Length != 0x200000)
            {
                logger.AddUserMessage(string.Format(
                    "File must be 0x200000 bytes; this file is 0x{0:X} bytes.", this.image.Length));
                return SumCvnVerdict.NotApplicable;
            }

            List<Tuple<string, int, int>> segments;
            try
            {
                segments = this.CollectSumCvnSegments();
            }
            catch (Exception exception)
            {
                logger.AddUserMessage("Checksum table is invalid: " + exception.Message);
                return SumCvnVerdict.NotApplicable;
            }

            logger.AddUserMessage("Validating 2MB file.");

            logger.AddUserMessage("Checksum validation:");
            LogChecksumTableHeader();
            bool anySumBad = false;
            foreach (Tuple<string, int, int> segment in segments)
            {
                UInt16 stored = this.GetU16BE(segment.Item2);
                UInt16 needed = this.CalcSegmentSum(segment.Item2, segment.Item3);
                bool good = stored == needed;
                anySumBad |= !good;
                LogChecksumTableRow(segment.Item2.ToString("X6"), segment.Item3.ToString("X6"), stored.ToString("X4"), needed.ToString("X4"), good, segment.Item1);
            }

            logger.AddUserMessage("CVN validation:");
            LogChecksumTableHeader();
            bool anyCvnBad = false;
            foreach (Tuple<string, int, int> segment in segments)
            {
                UInt16 stored = this.GetU16BE(segment.Item2 + 0x1E);
                UInt16 needed = this.CalcSegmentCvn(segment.Item2, segment.Item3);
                bool good = stored == needed;
                anyCvnBad |= !good;
                LogChecksumTableRow(segment.Item2.ToString("X6"), segment.Item3.ToString("X6"), stored.ToString("X4"), needed.ToString("X4"), good, segment.Item1);
            }

            if (anySumBad)
            {
                // A bad sum means the image is corrupt; flashing it would brick the PCM.
                logger.AddUserMessage("This file is corrupt. It would render your PCM unusable.");
                return SumCvnVerdict.SumError;
            }

            logger.AddUserMessage("All checksums are valid.");
            if (anyCvnBad)
            {
                // A bad CVN does not stop the file from being used; surface it as a warning only.
                logger.AddUserMessage("Warning: One or more CVNs are bad.");
                return SumCvnVerdict.CvnWarning;
            }

            return SumCvnVerdict.Good;
        }

        private List<Tuple<string, int, int>> CollectSumCvnSegments()
        {
            const int index = 0x10000;
            Tuple<int, int, string>[] layout =
            {
                Tuple.Create(0x24, 0x28, "Operating System"),
                Tuple.Create(0x48, 0x4C, "Engine Operations"),
                Tuple.Create(0x6B, 0x6F, "Engine Diagnostics"),
                Tuple.Create(0x8E, 0x92, "Fuel System"),
                Tuple.Create(0xB1, 0xB5, "System"),
                Tuple.Create(0xD4, 0xD8, "Speedometer"),
            };

            List<Tuple<string, int, int>> segments = new List<Tuple<string, int, int>>();
            for (int i = 0; i < layout.Length; i++)
            {
                int startAddr = unchecked((int)this.GetU32BE(index + layout[i].Item1));
                int endAddr = unchecked((int)this.GetU32BE(index + layout[i].Item2));
                int segNum = i + 1;

                if (startAddr >= this.image.Length)
                    throw new InvalidOperationException(string.Format("Segment {0} start out of range: 0x{1:X6}", segNum, startAddr));
                if (endAddr >= this.image.Length)
                    throw new InvalidOperationException(string.Format("Segment {0} end out of range: 0x{1:X6}", segNum, endAddr));
                if ((startAddr & 0x1) != 0)
                    throw new InvalidOperationException(string.Format("Segment {0} start not word-aligned: 0x{1:X6}", segNum, startAddr));
                if ((endAddr & 0x1) == 0)
                    throw new InvalidOperationException(string.Format("Segment {0} end not word-aligned: 0x{1:X6}", segNum, endAddr));
                if (endAddr <= startAddr)
                    throw new InvalidOperationException(string.Format("Segment {0} range invalid: 0x{1:X6}-0x{2:X6}", segNum, startAddr, endAddr));
                if ((endAddr - startAddr) < 0x24)
                    throw new InvalidOperationException(string.Format("Segment {0} range too short: 0x{1:X6}-0x{2:X6}", segNum, startAddr, endAddr));

                segments.Add(Tuple.Create(layout[i].Item3, startAddr, endAddr));
            }

            return segments;
        }

        /// <summary>
        /// The six E38 master flash segments described by the checksum table at flash 0x10000. These are
        /// the boundaries a boot loader write splits the master image on. Throws
        /// <see cref="InvalidOperationException"/> if the table is missing or inconsistent.
        /// </summary>
        public static List<FlashSegment> GetE38MasterSegments(byte[] image)
        {
            const int index = 0x10000;
            (int StartOffset, int EndOffset, string Name)[] layout =
            {
                (0x24, 0x28, "Operating System"),
                (0x48, 0x4C, "Engine Operations"),
                (0x6B, 0x6F, "Engine Diagnostics"),
                (0x8E, 0x92, "Fuel System"),
                (0xB1, 0xB5, "System"),
                (0xD4, 0xD8, "Speedometer"),
            };

            var segments = new List<FlashSegment>();
            for (int i = 0; i < layout.Length; i++)
            {
                int startAddr = unchecked((int)U32BE(image, index + layout[i].StartOffset));
                int endAddr = unchecked((int)U32BE(image, index + layout[i].EndOffset));
                int segNum = i + 1;

                if (startAddr >= image.Length)
                    throw new InvalidOperationException(string.Format("Segment {0} start out of range: 0x{1:X6}", segNum, startAddr));
                if (endAddr >= image.Length)
                    throw new InvalidOperationException(string.Format("Segment {0} end out of range: 0x{1:X6}", segNum, endAddr));
                if ((startAddr & 0x1) != 0)
                    throw new InvalidOperationException(string.Format("Segment {0} start not word-aligned: 0x{1:X6}", segNum, startAddr));
                if ((endAddr & 0x1) == 0)
                    throw new InvalidOperationException(string.Format("Segment {0} end not word-aligned: 0x{1:X6}", segNum, endAddr));
                if (endAddr <= startAddr)
                    throw new InvalidOperationException(string.Format("Segment {0} range invalid: 0x{1:X6}-0x{2:X6}", segNum, startAddr, endAddr));
                if ((endAddr - startAddr) < 0x24)
                    throw new InvalidOperationException(string.Format("Segment {0} range too short: 0x{1:X6}-0x{2:X6}", segNum, startAddr, endAddr));

                segments.Add(new FlashSegment(startAddr, endAddr, layout[i].Name));
            }

            return segments;
        }

        private static UInt32 U32BE(byte[] image, int offset)
        {
            if (offset < 0 || offset + 3 >= image.Length)
                throw new InvalidOperationException(string.Format("u32 read out of range at 0x{0:X6}", offset));
            return (UInt32)((image[offset] << 24) | (image[offset + 1] << 16) | (image[offset + 2] << 8) | image[offset + 3]);
        }

        /// <summary>
        /// Identify an E38 image by structure: a 2 MiB file whose checksum table at 0x10000 parses
        /// into the expected six well-formed segments. Reuses the same table reader the checksum
        /// validation uses, so detection and validation never diverge.
        /// </summary>
        private bool LooksLikeE38()
        {
            if (this.image.Length != 0x200000)
            {
                return false;
            }

            try
            {
                this.CollectSumCvnSegments();
                return true;
            }
            catch
            {
                return false;
            }
        }

        // GM E92 (Freescale MPC5674F) 4 MiB checksum scheme. Six segments, described by a pointer table
        // in the OS segment, each carry a 16-bit two's-complement word Sum and a GM CRC-16 CVN. A code
        // signature locates five more 32-bit word Sums covering extra regions. The 0..0x40000 boot block
        // is protected (reads all 0xFF from a PCM) and is reported n/a rather than failed. A bad Sum
        // means the image is corrupt; a bad CVN is only a warning - the same policy as the E38.
        //
        // Thanks to Joukoy and Kur4o for Universal Patcher: the segment layout, the code signature that
        // locates the extra sums, and the values this table is checked against all came from there.

        private const int E92BootBlockSize = 0x40000;

        /// <summary>
        /// One E92 checksum: the blocks it covers, where its value is stored, and how wide it is. The
        /// block table already excludes the bytes holding the checksum itself, so the blocks are summed
        /// as-is. Applicable is false for the protected boot block, which is reported n/a.
        /// </summary>
        private sealed class E92Checksum
        {
            public string Name = string.Empty;
            public int Address;
            public int Digits = 4;      // hex digits in the stored value: 4 (16-bit) or 8 (32-bit)
            public bool Applicable = true;
            public List<Tuple<int, int>> Blocks = new List<Tuple<int, int>>();

            public int Start => this.Blocks.Count > 0 ? this.Blocks[0].Item1 : 0;
            public int End => this.Blocks.Count > 0 ? this.Blocks[this.Blocks.Count - 1].Item2 : 0;
        }

        // The six normal segments' pointer-table layout (verified against known-good bins). AddrPtr is a
        // big-endian uint32 pointer to the segment start; the CVN/Sum are stored at start + offset; the
        // CVN/Sum blocks are [start,end] uint32 pairs read from the block pointers. Names echo UP.
        private static readonly (string Name, int AddrPtr, int CvnOffset, int CvnBlockPtr, int CvnBlockCount, int SumOffset, int SumBlockPtr, int SumBlockCount)[] E92Layout =
        {
            ("OS",         0xC0126, 0x120, 0xC0136, 3, 0x100, 0xC014E, 2),
            ("System",     0xC0162, 0x020, 0xC016A, 2, 0x000, 0xC017A, 1),
            ("Fuel",       0xC0186, 0x020, 0xC018E, 2, 0x000, 0xC019E, 1),
            ("Speedo",     0xC01AA, 0x020, 0xC01B2, 2, 0x000, 0xC01C2, 1),
            ("EngineDiag", 0xC01CE, 0x020, 0xC01D6, 2, 0x000, 0xC01E6, 1),
            ("Engine",     0xC01F2, 0x020, 0xC01FA, 2, 0x000, 0xC020A, 1),
        };

        /// <summary>
        /// The six E92 master flash segments, in the order a boot loader write streams them: the OS
        /// first, then the calibration segments. Each segment runs from its pointer-table start to the
        /// end of the last block its Sum covers. Throws <see cref="InvalidOperationException"/> if the
        /// table is missing or inconsistent.
        /// </summary>
        public static List<FlashSegment> GetE92MasterSegments(byte[] image)
        {
            var segments = new List<FlashSegment>();
            foreach (var segment in E92Layout)
            {
                int startAddr = unchecked((int)U32BE(image, segment.AddrPtr));
                int endAddr = unchecked((int)U32BE(image, segment.SumBlockPtr + (8 * (segment.SumBlockCount - 1)) + 4));

                if (startAddr < 0 || startAddr >= image.Length)
                    throw new InvalidOperationException(string.Format("{0} start out of range: 0x{1:X6}", segment.Name, startAddr));
                if (endAddr < 0 || endAddr >= image.Length)
                    throw new InvalidOperationException(string.Format("{0} end out of range: 0x{1:X6}", segment.Name, endAddr));
                if (endAddr <= startAddr)
                    throw new InvalidOperationException(string.Format("{0} range invalid: 0x{1:X6}-0x{2:X6}", segment.Name, startAddr, endAddr));
                if (endAddr - startAddr < segment.CvnOffset)
                    throw new InvalidOperationException(string.Format("{0} range too short: 0x{1:X6}-0x{2:X6}", segment.Name, startAddr, endAddr));

                // The Sum sits at the head of the segment's own header record, which is what the OS
                // module leads with.
                segments.Add(new FlashSegment(startAddr, endAddr, segment.Name, segment.SumOffset));
            }

            return segments;
        }

        // 28-byte code signature that locates the extra 32-bit word-sum regions (null = wildcard byte).
        private static readonly byte?[] E92ExtSignature =
        {
            0x00, 0x0B, 0xFF, 0xFF, 0x48, 0x13, 0x00, 0x04, 0x30, 0x6E, null, null, 0x00, 0x04, 0x44, 0x44,
            0x30, 0x6E, null, null, 0x7C, 0x63, 0x00, 0x34, 0x68, 0x53, 0x00, 0x04,
        };

        // Extra 32-bit word-sum segments. At the signature match M, each checksum is stored at
        // value@(M+CsOff) and covers [value@(M+StartOff), value@(M+CsOff)-1]. Cs2Off < 0 = no Checksum 2.
        private static readonly (string Name, int Start1Off, int Cs1Off, int Start2Off, int Cs2Off)[] E92ExtLayout =
        {
            ("SYSSPD ext",  64, 68, 76, 80),
            ("ENG extra 1", 28, 32, 40, 44),
            ("ENG extra 2", 52, 56, -1, -1),
        };

        /// <summary>
        /// The six segments' Sums, then the boot block, then the extra 32-bit Sums located by the code
        /// signature. Throws <see cref="InvalidOperationException"/> if a pointer or block is out of range.
        /// </summary>
        private List<E92Checksum> CollectE92Sums()
        {
            List<E92Checksum> sums = new List<E92Checksum>();

            foreach (var segment in E92Layout)
            {
                int start = this.E92SegmentStart(segment);
                sums.Add(new E92Checksum
                {
                    Name = segment.Name,
                    Address = start + segment.SumOffset,
                    Blocks = this.ReadE92Blocks(segment.SumBlockPtr, segment.SumBlockCount),
                });
            }

            sums.Add(this.E92BootChecksum());

            int signature = this.FindE92ExtSignature();
            if (signature >= 0)
            {
                foreach (var ext in E92ExtLayout)
                {
                    this.AddE92ExtSum(sums, ext.Name, signature, ext.Start1Off, ext.Cs1Off);
                    if (ext.Cs2Off >= 0)
                    {
                        this.AddE92ExtSum(sums, ext.Name, signature, ext.Start2Off, ext.Cs2Off);
                    }
                }
            }

            return sums;
        }

        /// <summary>The six segments' CVNs, then the boot block.</summary>
        private List<E92Checksum> CollectE92Cvns()
        {
            List<E92Checksum> cvns = new List<E92Checksum>();

            foreach (var segment in E92Layout)
            {
                int start = this.E92SegmentStart(segment);
                cvns.Add(new E92Checksum
                {
                    Name = segment.Name,
                    Address = start + segment.CvnOffset,
                    Blocks = this.ReadE92Blocks(segment.CvnBlockPtr, segment.CvnBlockCount),
                });
            }

            cvns.Add(this.E92BootChecksum());
            return cvns;
        }

        private int E92SegmentStart((string Name, int AddrPtr, int CvnOffset, int CvnBlockPtr, int CvnBlockCount, int SumOffset, int SumBlockPtr, int SumBlockCount) segment)
        {
            int start = unchecked((int)this.GetU32BE(segment.AddrPtr));
            if (start < 0 || start >= this.image.Length)
                throw new InvalidOperationException(string.Format("{0} start out of range: 0x{1:X6}", segment.Name, start));
            return start;
        }

        /// <summary>
        /// The boot block entry. The block is protected, so a PCM read leaves it all 0xFF and there is
        /// nothing to check; it is listed for completeness as n/a and never counted as a failure.
        /// </summary>
        private E92Checksum E92BootChecksum()
        {
            return new E92Checksum
            {
                Name = "Boot Block",
                Applicable = false,
                Blocks = new List<Tuple<int, int>> { Tuple.Create(0, E92BootBlockSize - 1) },
            };
        }

        /// <summary>
        /// One extra 32-bit Sum, whose covered range and storage address are file pointers read at the
        /// signature match. Skipped if the pointers do not describe a range inside the image.
        /// </summary>
        private void AddE92ExtSum(List<E92Checksum> sums, string name, int signature, int startOffset, int checksumOffset)
        {
            int start = unchecked((int)this.GetU32BE(signature + startOffset));
            int checksumAddress = unchecked((int)this.GetU32BE(signature + checksumOffset));
            if (start < 0 || checksumAddress <= start || checksumAddress + 4 > this.image.Length)
            {
                return;
            }

            sums.Add(new E92Checksum
            {
                Name = name,
                Address = checksumAddress,
                Digits = 8,
                Blocks = new List<Tuple<int, int>> { Tuple.Create(start, checksumAddress - 1) },
            });
        }

        /// <summary>Find the E92 extra-segment code signature; returns its offset, or -1 if absent.</summary>
        private int FindE92ExtSignature()
        {
            byte?[] signature = E92ExtSignature;
            int limit = this.image.Length - signature.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < signature.Length; j++)
                {
                    if (signature[j].HasValue && this.image[i + j] != signature[j]!.Value)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Read <paramref name="count"/> [start,end] block pointers (big-endian uint32 pairs) starting at the table pointer.</summary>
        private List<Tuple<int, int>> ReadE92Blocks(int pointer, int count)
        {
            List<Tuple<int, int>> blocks = new List<Tuple<int, int>>();
            for (int i = 0; i < count; i++)
            {
                int start = unchecked((int)this.GetU32BE(pointer + (8 * i)));
                int end = unchecked((int)this.GetU32BE(pointer + (8 * i) + 4));
                if (start < 0 || end < start || end >= this.image.Length)
                    throw new InvalidOperationException(string.Format("E92 checksum block out of range: 0x{0:X6}-0x{1:X6}", start, end));
                blocks.Add(Tuple.Create(start, end));
            }
            return blocks;
        }

        /// <summary>The value stored for a checksum, read at its own width.</summary>
        private uint StoredE92Value(E92Checksum checksum)
            => checksum.Digits == 4 ? this.GetU16BE(checksum.Address) : this.GetU32BE(checksum.Address);

        /// <summary>Two's-complement 16-bit word sum over a checksum's blocks, masked to its width.</summary>
        private uint CalcE92Sum(E92Checksum checksum)
        {
            long total = 0;
            foreach (Tuple<int, int> block in checksum.Blocks)
            {
                for (int address = block.Item1; address < block.Item2; address += 2)
                {
                    total += (this.image[address] << 8) | this.image[address + 1];
                }
            }

            ulong mask = checksum.Digits == 4 ? 0xFFFFUL : 0xFFFFFFFFUL;
            return (uint)((~(ulong)total + 1) & mask);
        }

        /// <summary>GM CRC-16 over a checksum's blocks, byte-swapped as stored (an E92 CVN).</summary>
        private uint CalcE92Cvn(E92Checksum checksum)
        {
            UInt16 crc = 0;
            foreach (Tuple<int, int> block in checksum.Blocks)
            {
                crc = this.Crc16Gm(crc, block.Item1, block.Item2);
            }

            return (uint)(((crc & 0x00FF) << 8) | ((crc & 0xFF00) >> 8));
        }

        /// <summary>
        /// Log one E92 checksum row and report whether it is good. A checksum that cannot be validated
        /// (the protected boot block) prints n/a and reports good, so it never fails the file.
        /// </summary>
        private bool LogE92Row(E92Checksum checksum, uint needed)
        {
            string start = checksum.Start.ToString("X6");
            string end = checksum.End.ToString("X6");
            string digits = "X" + checksum.Digits;

            if (!checksum.Applicable)
            {
                LogChecksumTableRow(start, end, "n/a", "n/a", "n/a", checksum.Name);
                return true;
            }

            uint stored = this.StoredE92Value(checksum);
            bool good = stored == needed;
            LogChecksumTableRow(start, end, stored.ToString(digits), needed.ToString(digits), good, checksum.Name);
            return good;
        }

        /// <summary>
        /// Validate a 4 MiB E92 image: a Sum table then a CVN table, both in the standard checksum table
        /// format. A bad Sum is an error; a bad CVN is only a warning; the protected boot block is n/a and
        /// never fails. Mirrors <see cref="ValidateSumAndCvn"/>.
        /// </summary>
        public SumCvnVerdict ValidateE92SumAndCvn()
        {
            if (this.image.Length != 0x400000)
            {
                logger.AddUserMessage(string.Format(
                    "File must be 0x400000 bytes; this file is 0x{0:X} bytes.", this.image.Length));
                return SumCvnVerdict.NotApplicable;
            }

            List<E92Checksum> sums;
            List<E92Checksum> cvns;
            try
            {
                sums = this.CollectE92Sums();
                cvns = this.CollectE92Cvns();
            }
            catch (Exception exception)
            {
                logger.AddUserMessage("Checksum table is invalid: " + exception.Message);
                return SumCvnVerdict.NotApplicable;
            }

            logger.AddUserMessage("Validating 4MB file.");

            logger.AddUserMessage("Checksum validation:");
            LogChecksumTableHeader();
            bool anySumBad = false;
            foreach (E92Checksum sum in sums)
            {
                anySumBad |= !this.LogE92Row(sum, this.CalcE92Sum(sum));
            }

            logger.AddUserMessage("CVN validation:");
            LogChecksumTableHeader();
            bool anyCvnBad = false;
            foreach (E92Checksum cvn in cvns)
            {
                anyCvnBad |= !this.LogE92Row(cvn, this.CalcE92Cvn(cvn));
            }

            if (anySumBad)
            {
                logger.AddUserMessage("This file is corrupt. It would render your PCM unusable.");
                return SumCvnVerdict.SumError;
            }

            logger.AddUserMessage("All checksums are valid.");
            if (anyCvnBad)
            {
                logger.AddUserMessage("Warning: One or more CVNs are bad.");
                return SumCvnVerdict.CvnWarning;
            }

            return SumCvnVerdict.Good;
        }

        /// <summary>
        /// Identify an E92 image by structure: a 4 MiB file whose segment table parses into the expected
        /// six well-formed segments. Reuses the same table reader the checksum validation uses, so
        /// detection and validation never diverge.
        /// </summary>
        private bool LooksLikeE92()
        {
            if (this.image.Length != 0x400000)
            {
                return false;
            }

            try
            {
                this.CollectE92Cvns();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private UInt16 GetU16BE(int offset)
        {
            if (offset < 0 || offset + 1 >= this.image.Length)
                throw new InvalidOperationException(string.Format("u16 read out of range at 0x{0:X6}", offset));
            return (UInt16)((this.image[offset] << 8) | this.image[offset + 1]);
        }

        private UInt32 GetU32BE(int offset) => U32BE(this.image, offset);

        // 16-bit two's-complement sum of the words from start+2..end (the stored sum sits at start).
        private UInt16 CalcSegmentSum(int startAddr, int endAddr)
        {
            int total = 0;
            for (int idx = startAddr + 2; idx <= endAddr; idx += 2)
            {
                total += this.GetU16BE(idx);
            }
            return (UInt16)((((total & 0xFFFF) ^ 0xFFFF) + 1) & 0xFFFF);
        }

        // GM CRC-16 (reflected, poly 0xA001) used for the CVN.
        private UInt16 Crc16Gm(int initCrc, int startAddr, int endAddr)
        {
            int crc = initCrc & 0xFFFF;
            for (int idx = startAddr; idx <= endAddr; idx++)
            {
                crc ^= this.image[idx] & 0xFF;
                for (int bit = 0; bit < 8; bit++)
                {
                    crc = ((crc & 0x0001) != 0) ? ((crc >> 1) ^ 0xA001) & 0xFFFF : (crc >> 1) & 0xFFFF;
                }
            }
            return (UInt16)(crc & 0xFFFF);
        }

        // CVN: CRC over [start+2 .. start+0x1D] then [start+0x20 .. end], with the result byte-swapped
        // (the 0x1E..0x1F window holding the stored CVN itself is excluded).
        private UInt16 CalcSegmentCvn(int startAddr, int endAddr)
        {
            UInt16 crc = this.Crc16Gm(0, startAddr + 2, startAddr + 0x1D);
            crc = this.Crc16Gm(crc, startAddr + 0x20, endAddr);
            return (UInt16)(((crc & 0x00FF) << 8) | ((crc & 0xFF00) >> 8));
        }
    }
}
