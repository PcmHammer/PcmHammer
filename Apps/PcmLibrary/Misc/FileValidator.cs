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
        /// Prevent repeating forced-type user message for each signature pass.
        /// </summary>
        private bool forcedTypeLogged;

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
            this.forcedTypeLogged = false;
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
        /// Indicate whether the image is valid or not.
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            if (this.image.Length == 256 * 1024)
            {
                this.logger.AddUserMessage("Identifying 256KiB file.");
            }
            else if (this.image.Length == 512 * 1024)
            {
                this.logger.AddUserMessage("Identifying 512KiB file.");
            }
            else if (this.image.Length == 1024 * 1024)
            {
                this.logger.AddUserMessage("Identifying 1024KiB file.");
            }
            else if (this.image.Length == 2048 * 1024)
            {
                this.logger.AddUserMessage("Identifying 2048KiB file.");
            }
            else
            {
                this.logger.AddUserMessage(
                    string.Format(
                        "Files must be 256KiB, 512KiB, 1024KiB or 2048KiB. This file is {0} / {1:X} bytes long.",
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

                UInt32 fileOsid = this.GetOsidFromImage(type);
                this.logger.AddUserMessage("File operating system ID: " + fileOsid);

                return this.ValidateChecksums(type);
            }
            catch (Exception exception)
            {
                this.logger.AddUserMessage("Unable to validate file format/checksums.");
                this.logger.AddDebugMessage(exception.ToString());
                return false;
            }
        }

        /// <summary>
        /// Compare the OS ID from the PCM with the OS ID from the file.
        /// </summary>
        public bool IsSameOperatingSystem(UInt32 pcmOsid)
        {
            UInt32 fileOsid = this.GetOsidFromImage();

            if (pcmOsid == fileOsid)
            {
                this.logger.AddUserMessage("PCM and file are both operating system " + pcmOsid);
                return true;
            }

            this.logger.AddUserMessage("Operating system IDs do not match.");
            this.logger.AddUserMessage("PCM operating system ID: " + pcmOsid);
            this.logger.AddUserMessage("File operating system ID: " + fileOsid);
            return false;
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
                this.logger.AddUserMessage("PCM and file match hardware " + fileInfo.HardwareType.ToString());
                return true;
            }

            this.logger.AddUserMessage("Hardware types do not match. This file is not compatible with this PCM");
            this.logger.AddUserMessage("PCM Hardware is: " + pcmInfo.HardwareType.ToString());
            this.logger.AddUserMessage("File requires: " + fileInfo.HardwareType.ToString());
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
                    osid = ReadUnsigned(image, 0x8004);
                    break;

                case PcmType.E54:
                case PcmType.BlackBox:
                    osid = ReadUnsigned(image, 0x20004);
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
                    this.logger.AddDebugMessage("TODO: Implement FileValidator::ValidateChecksums for a " + type.ToString());
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
                        this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
                        success &= ValidateRangeWordSum(type, 0x0000, 0xFFFFF, 0x20880, "Operating System");
                        success &= ValidateRangeWordSum(type, 0x8002, 0x1FFFF, 0x8000, "Engine Calibration");
                    }
                    else
                    {
                        this.logger.AddUserMessage("\tStart\tEnd\tStored\t\tNeeded\t\tVerdict\tSegment Name");
                        success &= ValidateRangeP04(true);
                    }
                    break;
                case PcmType.P08:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
                    success &= ValidateRangeByteSum(type, 0, 0x7FFFB, 0x8004, "Whole File");
                    break;
                case PcmType.P11:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
                    success &= ValidateRangeWordSum(type, 0, 0x7FFFB, 0x8000, "Whole File");
                    break;
                case PcmType.P12:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
                    success &= ValidateRangeP12(0x922, 0x900, 0x94A, 2, "Boot Block");
                    success &= ValidateRangeP12(0x8022, 0, 0x804A, 2, "Operating System");
                    success &= ValidateRangeP12(0x80C4, 0, 0x80E4, 2, "Engine Calibration");
                    success &= ValidateRangeP12(0x80F7, 0, 0x8117, 2, "Engine Diagnostics");
                    success &= ValidateRangeP12(0x812A, 0, 0x814A, 2, "Transmission Calibration");
                    success &= ValidateRangeP12(0x815D, 0, 0x817D, 2, "Transmission Diagnostics");
                    success &= ValidateRangeP12(0x805E, 0, 0x807E, 2, "Speedometer");
                    success &= ValidateRangeP12(0x8091, 0, 0x80B1, 2, "System");
                    break;
                case PcmType.E54:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
                    success &= ValidateRangeWordSum(type, 0x20002, 0x6FFFF, 0x20000, "Operating System");
                    success &= ValidateRangeWordSum(type, 0x8002, 0x19FFF, 0x8000, "Engine Calibration");
                    success &= ValidateRangeWordSum(type, 0x1A002, 0x1C7FF, 0x1A000, "Engine Diagnostics");
                    success &= ValidateRangeWordSum(type, 0x1C002, 0x1DFFF, 0x1C000, "Fuel");
                    success &= ValidateRangeWordSum(type, 0x1E002, 0x1EFFF, 0x1E000 , "System");
                    success &= ValidateRangeWordSum(type, 0x1F002, 0x1FFEF, 0x1F000, "Speedometer");
                    break;

                // The rest can use the generic code
                default:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
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
                            this.logger.AddUserMessage("Checksum table is corrupt.");
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
        /// Validate signatures
        /// </summary>
        private PcmType ValidateSignatures()
        {
            if (this.forcedType.HasValue && this.forcedType.Value != PcmType.Undefined)
            {
                if (!this.forcedTypeLogged)
                {
                    this.logger.AddUserMessage("File type forced to " + this.forcedType.Value + " (manual selection).");
                    this.forcedTypeLogged = true;
                }

                return this.forcedType.Value;
            }

            // All currently supported bins are 256KiB, 512KiB, 1024KiB or 20248KiB
            if ((image.Length != 256 * 1024) && (image.Length != 512 * 1024) && (image.Length != 1024 * 1024) && (image.Length != 2048 * 1024))
            {
                this.logger.AddUserMessage("Files of size " + image.Length.ToString("X8") + " are not supported.");
                return PcmType.Undefined;
            }

            // 256Kb Type
            // P04 512Kb
            if (image.Length == 256 * 1024)
            {
                this.logger.AddDebugMessage("Trying P04 256KiB");
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
                this.logger.AddDebugMessage("Trying E54 512KiB");
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
                this.logger.AddDebugMessage("Trying Vortec BlackBox 512KiB");
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
                this.logger.AddDebugMessage("Trying P01 512KiB");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        return PcmType.P01;
                    }
                }

                // P04 512Kb
                this.logger.AddDebugMessage("Trying P04 512KiB");
                // Last 4 bytes:
                // A5 5A FF FF = P04
                // XX XX XX XX A5 5A = P04 (XX is the OSID)
                if (((image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0x5A)) || // most P04 OR
                    ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xFF) && (image[0x7FFFF] == 0xFF)))   // Most 1998 512Kb eg Malibu 09369193, Olds 09352676, LeSabre 09379801...
                {
                    return PcmType.P04;
                }

                this.logger.AddDebugMessage("Trying P10 512KiB");
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
                this.logger.AddDebugMessage("Trying P11 512KiB boot hash + marker");
                if (this.HasP11TailMarkerAt7FFFC() && this.IsKnownP11BootSector())
                {
                    return PcmType.P11;
                }

                // P08 512KiB
                this.logger.AddDebugMessage("Trying P08 512KiB");
                if ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0xA5))
                {
                    return PcmType.P08;
                }
            }

            // 1MiB types
            if (image.Length == 1024 * 1024)
            {
                this.logger.AddDebugMessage("Trying P59 1024KiB");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0xFFFFE] == 0x4A) && (image[0xFFFFF] == 0xFC))
                    {
                        return PcmType.P59;
                    }
                }

                // P05/P05c 1024KiB
                this.logger.AddDebugMessage("Trying P05/P05c 1024KiB");
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
                this.logger.AddDebugMessage("Trying P11 1024KiB boot hash + marker");
                if (this.HasP11TailMarkerAt7FFFC() && this.IsKnownP11BootSector())
                {
                    return PcmType.P11;
                }

                this.logger.AddDebugMessage("Trying P12 1024KiB");
                if ((image[0xFFFF8] == 0xAA) && (image[0xFFFF9] == 0x55))
                {
                    return PcmType.P12;
                }
            }

            // 2024KiB types
            if (image.Length == 2048 * 1024)
            {
                this.logger.AddDebugMessage("Trying P12 2048KiB");
                if ((image[0x17FFF8] == 0xAA) && (image[0x17FFF9] == 0x55))
                {
                    return PcmType.P12;
                }
            }

            this.logger.AddDebugMessage("Unable to identify or validate bin image content");
            return PcmType.Undefined;
        }

        /// <summary>
        /// Print the header for the table of checksums.
        /// </summary>
        private void PrintHeader()
        {
            this.logger.AddUserMessage("\tStart\tEnd\tResult\tFile\tActual\tContent");
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

            string error = string.Format(
                "\t{0:X5}\t{1:X5}\t{2:X4}\t{3:X4}\t{4:X4}\t{5}",
                start,
                end,
                storedChecksum,
                computedChecksum,
                verdict ? "Good" : "BAD",
                description);

            this.logger.AddUserMessage(error);
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

            string error = string.Format(
                "\t{0:X5}\t{1:X5}\t{2:X4}\t{3:X4}\t{4:X4}\t{5}",
                start,
                end,
                storedChecksum,
                computedChecksum,
                verdict ? "Good" : "BAD",
                description);

            this.logger.AddUserMessage(error);
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

            string error = string.Format(
                "\t{0:X5}\t{1:X5}\t{2:X4}\t{3:X4}\t{4:X4}\t{5}",
                first, // The start of the first block
                end,   // The end of the last block
                storedChecksum,
                computedChecksum,
                verdict ? "Good" : "BAD",
                description);

            this.logger.AddUserMessage(error);
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

            // Thanks Joukoy for Universal Patcher and the idea to use a pattern search for the P04 sum address.
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
                this.logger.AddUserMessage("Unable to locate a valid checksum pointer for this P04/P05 image.");
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

            string error = string.Format(
                "\t{0:X5}\t{1:X5}\t{2:X8}\t{3:X8}\t{4:X4}\t{5}",
                0,              // The start of the first block
                image.Length-1, // The end of the last block
                storedChecksum,
                computedChecksum,
                verdict ? "Good" : "BAD",
                "Whole File");

            this.logger.AddUserMessage(error);
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
                    this.logger.AddDebugMessage("256KiB P04, no param block required");
                    return true;
                case 512 * 1024:
                case 1024 * 1024:
                    if (Utility.IsBlank(this.image, 0x4000, 0x2000) || Utility.IsBlank(this.image, 0x6000, 0x2000))
                    {
                        this.logger.AddUserMessage("P04/P05 1998+, checking for valid paramater block");
                        if ((image[0x43F6] == 0xA5) && (image[0x43F7] == 0xA0))
                        {
                            this.logger.AddUserMessage("Param block at 0x4000");
                            return true;
                        }
                        if ((image[0x63F6] == 0xA5) && (image[0x63F7] == 0xA0))
                        {
                            this.logger.AddUserMessage("Param block at 0x6000");
                            return true;
                        }
                        this.logger.AddUserMessage("1998+ P04/P05 with missing param block. This file is bad and would soft brick your PCM.");
                        return false;
                    }
                    this.logger.AddUserMessage("1997 type P04, Param block not needed");
                    return true;
            }
            this.logger.AddDebugMessage("BUG: ValidateParamBlockP04 called with image of invalid size");
            return false; // unreachable
        }

        /// <summary>
        /// Verify type/size/layout once up front so later stages can assume sane offsets.
        /// </summary>
        private bool TryPrepareValidation(out PcmType type)
        {
            type = this.ValidateSignatures();
            if (type == PcmType.Undefined)
            {
                return false;
            }

            if (!this.ValidateTypeLayout(type))
            {
                this.logger.AddUserMessage("The file structure does not match the selected or detected PCM type.");
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
                    return this.HasSize(1024 * 1024, 2048 * 1024) &&
                        this.HasRange(0x8004, 4, "P12 OSID") &&
                        this.ValidateP12Layout();

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
                        this.logger.AddDebugMessage("P12 checksum block range is invalid.");
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

            this.logger.AddDebugMessage("Unexpected file size for selected/detected type.");
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

            this.logger.AddDebugMessage($"{purpose} is out of range.");
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
                string.Equals(hash, P11BootSectorSha256_12576162, StringComparison.OrdinalIgnoreCase);
            if (!match)
            {
                this.logger.AddDebugMessage(
                    "P11 boot sector hash mismatch. Found: " + hash +
                    ", Expected one of: " + P11BootSectorSha256_12210553 +
                    " (12210553), " + P11BootSectorSha256_12576162 + " (12576162).");
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
                this.logger.AddDebugMessage("P11 marker A5 5A A5 A5 was not found at 0x7FFFC.");
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
    }
}
