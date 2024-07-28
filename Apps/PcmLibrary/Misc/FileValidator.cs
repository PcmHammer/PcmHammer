using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking
{
    /// <summary>
    /// Try to detect corrupted firmware images.
    /// </summary>
    public class FileValidator
    {
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

        // Names of segments in P12 operating system are documented in the ValidateChecksums() function

        /// <summary>
        /// The contents of the firmware file that someone wants to flash.
        /// </summary>
        private readonly byte[] image;

        /// <summary>
        /// For reporting progress and success/fail.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Constructor.
        /// </summary>
        public FileValidator(byte[] image, ILogger logger)
        {
            this.image = image;
            this.logger = logger;
        }

        /// <summary>
        /// Indicate whether the image is valid or not.
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            if (this.image.Length == 256 * 1024)
            {
                this.logger.AddUserMessage("Identifying 256kb file.");
            }
            else if (this.image.Length == 512 * 1024)
            {
                this.logger.AddUserMessage("Identifying 512kb file.");
            }
            else if (this.image.Length == 1024 * 1024)
            {
                this.logger.AddUserMessage("Identifying 1024Kb file.");
            }
            else if (this.image.Length == 2048 * 1024)
            {
                this.logger.AddUserMessage("Identifying 2048Kb file.");
            }
            else
            {
                this.logger.AddUserMessage(
                    string.Format(
                        "Files must be 256k, 512k, 1024k or 2048k. This file is {0} / {1:X} bytes long.",
                        this.image.Length,
                        this.image.Length));
                return false;
            }

            UInt32 fileOsid = this.GetOsidFromImage();
            this.logger.AddUserMessage("File operating system ID: " + fileOsid);

            return this.ValidateChecksums();
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
                this.logger.AddUserMessage("PCM and file are both for the same Hardware " + fileInfo.HardwareType.ToString());
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
            UInt32 osid = 0;

            if (image.Length == 256 * 1024 || image.Length == 512 * 1024 || image.Length == 1024 * 1024 || image.Length == 2048 * 1024) // bin valid sizes
            {
                PcmType type = this.ValidateSignatures();
                switch (type)
                {
                    case PcmType.P01_P59:
                        osid = ReadUnsigned(image, 0x504);
                        break;

                    case PcmType.P04:
                    case PcmType.P04_256k:
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
                        osid = ReadUnsigned(image, 0xFFFFA);
                        break;

                    case PcmType.P08:
                        osid = ReadUnsigned(image, 0x8000);
                        break;

                    case PcmType.P10:
                        osid = ReadUnsigned(image, 0x52E);
                        break;

                    case PcmType.P12:
                        osid = ReadUnsigned(image, 0x8004);
                        break;

                    case PcmType.E54:
                    case PcmType.BlackBox:
                        osid = ReadUnsigned(image, 0x20004);
                        break;
                }
            }
            return osid;
        }

        /// <summary>
        /// Validate a binary image of a known type
        /// </summary>
        private bool ValidateChecksums()
        {
            bool success = true;
            UInt32 tableAddress = 0;
            UInt32 segments = 0;

            PcmType type = this.ValidateSignatures();

            switch (type)
            {
                // have a segment table
                case PcmType.P01_P59:
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
                    segments = 5;
                    break;

                // no segment table
                case PcmType.P04:
                case PcmType.P04_256k:
                case PcmType.P05:
                case PcmType.P08:
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
                case PcmType.P04_256k:
                case PcmType.P04:
                case PcmType.P05:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\t\tNeeded\t\tVerdict\tSegment Name");
                    success &= ValidateRangeP04(true);
                    break;
                case PcmType.P08:
                    this.logger.AddUserMessage("\tStart\tEnd\tStored\tNeeded\tVerdict\tSegment Name");
                    success &= ValidateRangeByteSum(type, 0, 0x7FFFB, 0x8004, "Whole File");
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
                            case PcmType.P01_P59:
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
        /// Validate signatures
        /// </summary>
        private PcmType ValidateSignatures()
        {
            // All currently supported bins are 256Kb, 512Kb, 1Mb or 2Mb
            if ((image.Length != 256 * 1024) && (image.Length != 512 * 1024) && (image.Length != 1024 * 1024) && (image.Length != 2048 * 1024))
            {
                this.logger.AddUserMessage("Files of size " + image.Length.ToString("X8") + " are not supported.");
                return PcmType.Undefined;
            }

            // 256Kb Type
            // P04 512Kb
            if (image.Length == 256 * 1024)
            {
                this.logger.AddDebugMessage("Trying P04 256Kb");
                if ((image[0x3FFFE] == 0xA5) && (image[0x3FFFF] == 0x5A))
                {
                    this.logger.AddUserMessage("File is P04 256Kb.");
                    return PcmType.P04_256k;
                }
            }

            // 512Kb Types
            if (image.Length == 512 * 1024)
            {
                // E54 512Kb
                // Must be before P01, P01 can pass for a E54, but an E54 cannot pass as a P01
                this.logger.AddDebugMessage("Trying E54 512Kb");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFC] == 0x4A) && (image[0x7FFFD] == 0xFC) && (image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        if ((image[0x3FFC] == 0) && (image[0x3FFD] == 0) && (image[0x3FFE] == 0) && (image[0x3FFF] == 0)) { // This prevents 98/99 Black Box being detected at E54
                            this.logger.AddUserMessage("File is E54 512Kb.");
                            return PcmType.E54;
                        }
                    }
                }

                // BlackBox 512Kb. Thanks to Universal Patcher team for the logic in autodetect.xml
                this.logger.AddDebugMessage("Trying Vortec BlackBox 512Kb");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        if ((image[0x20002] == 00) && (image[0x20003] == 01) && (image[0x2000A] == 01) && (image[0x2000B] == 00))
                        {
                            this.logger.AddUserMessage("File is Vortec BlackBox 512Kb.");
                            return PcmType.BlackBox;
                        }
                    }
                }

                // P01 512Kb
                this.logger.AddDebugMessage("Trying P01 512Kb");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0x7FFFE] == 0x4A) && (image[0x7FFFF] == 0xFC))
                    {
                        this.logger.AddUserMessage("File is P01 512Kb.");
                        return PcmType.P01_P59;
                    }
                }

                // P04 512Kb
                this.logger.AddDebugMessage("Trying P04 512Kb");
                // Last 4 bytes:
                // A5 5A FF FF = P04
                // XX XX XX XX A5 5A = P04 (XX is the OSID)
                if (((image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0x5A)) || // most P04 OR
                    ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xFF) && (image[0x7FFFF] == 0xFF)))   // Most 1998 512Kb eg Malibu 09369193, Olds 09352676, LeSabre 09379801...
                {
                    this.logger.AddUserMessage("File is P04 512kb.");
                    return PcmType.P04;
                }

                this.logger.AddDebugMessage("Trying P10 512Kb");
                if ((image[0x17FFE] == 0x55) && (image[0x17FFF] == 0x55))
                {
                    if ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0xA5))
                    {
                        this.logger.AddUserMessage("File is P10 512Kb.");
                        return PcmType.P10;
                    }
                }

                // P08 512Kb
                this.logger.AddDebugMessage("Trying P08 512Kb");
                if ((image[0x7FFFC] == 0xA5) && (image[0x7FFFD] == 0x5A) && (image[0x7FFFE] == 0xA5) && (image[0x7FFFF] == 0xA5))
                {
                    this.logger.AddUserMessage("File is P08 512Kb.");
                    return PcmType.P08;
                }
            }

            // 1Mb types
            if (image.Length == 1024 * 1024)
            {
                this.logger.AddDebugMessage("Trying P59 1Mb");
                if ((image[0x1FFFE] == 0x4A) && (image[0x1FFFF] == 0xFC))
                {
                    if ((image[0xFFFFE] == 0x4A) && (image[0xFFFFF] == 0xFC))
                    {
                        this.logger.AddUserMessage("File is P59 1Mb.");
                        return PcmType.P01_P59;
                    }
                }

                // P05 1Mb
                this.logger.AddDebugMessage("Trying P05 1Mb");
                if ((image[0xFFFFE] == 0xA5) && (image[0xFFFFF] == 0x5A))
                {
                    this.logger.AddUserMessage("File is P05 1Mb.");
                    return PcmType.P05;
                }

                this.logger.AddDebugMessage("Trying P12 1Mb");
                if ((image[0xFFFF8] == 0xAA) && (image[0xFFFF9] == 0x55))
                {
                    this.logger.AddUserMessage("File is P12 1Mb.");
                    return PcmType.P12;
                }
            }

            // 2Mb types
            if (image.Length == 2048 * 1024)
            {
                this.logger.AddDebugMessage("Trying P12 2Mb");
                if ((image[0x17FFF8] == 0xAA) && (image[0x17FFF9] == 0x55))
                {
                    this.logger.AddUserMessage("File is P12 2Mb.");
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
                switch (type)
                {
                    case PcmType.P01_P59:
                        if (address == 0x500)
                        {
                            address = 0x502;
                        }

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
                            case 0x52A:
                                address = 0x52C;
                                break;

                            case 0x4000:
                                address = 0x20000;
                                break;

                            case 0x7FFFA:
                                end = 0x7FFFA; // A hacky way to short circuit the end
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
                start  = image[index + (block * 8) + 0] << 24;
                start += image[index + (block * 8) + 1] << 16;
                start += image[index + (block * 8) + 2] << 8;
                start += image[index + (block * 8) + 3];
                end    = image[index + (block * 8) + 4] << 24;
                end   += image[index + (block * 8) + 5] << 16;
                end   += image[index + (block * 8) + 6] << 8;
                end   += image[index + (block * 8) + 7];

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
        /// Validate a range for P04 and P05. Support 256, 512, 1024K images
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

            // Thanks Joukoy for Universal Patcher and the idea to use a pattern search for the P04 sum address.
            // Working for all tested 1024K (P05)
            if (image.Length == 1024 * 1024)
            {
                for (UInt32 i = start; i < end; i++)
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
                        break;
                    }
                }
            }
            else
            {
                // Working for all tested 256K and 512K
                for (UInt32 i = start; i < end; i++)
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
                        break;
                    }
                }
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
    }
}
