using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU {
    public enum PcmType
    {
        Undefined = 0, // required for failed osid test on binary file
        P01,
        P59,
        P04_Early,
        P04,
        P05,
        P08,
        P10,
        P11,
        P12,
        E54, //E54 (01-04 LB7 Duramax) 
        E60, //E60 (04-05 LLY Duramax)
        BlackBox
    }

    public abstract class ECUBase {
        public List<OSInfo> KnownOperatingSystems { get; set; }

        public OSInfo CurrentOS { get; private set; }

        /// <summary>
        /// Define a manufacturer name for this PCM. Used mostly for display purposes.
        /// </summary>
        public string? Manufacturer { get; set; }

        /// <summary>
        /// Descriptive text.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Indicates whether this PCM is supported by the app.
        /// </summary>
        public bool IsSupported { get; set; }

        /// <summary>
        /// Indicates whether this PCM is supported to read
        /// </summary>
        public bool IsSupportedRead { get; set; }

        /// <summary>
        /// Indicates whether this PCM is supported to write
        /// </summary>
        public bool IsSupportedWrite { get; set; }

        /// <summary>
        /// Indicates whether this PCM is supports writing the slave CPU. If it cannot, we must block (or warn) OSID change.
        /// </summary>
        public bool IsSupportedWriteSlaveCPU { get; set; }

        /// <summary>
        /// Indicates whether this PCM is supported to write by flash segment (later PCMs)
        /// </summary>
        public bool IsSupportedWriteBySegment { get; set; }

        /// <summary>
        /// PCM requires a kernel loader
        /// </summary>
        /// <remarks>
        /// We make dual use of this, once it has outlived it's usefulness,
        /// we use it as a state switch between Loader and Kernel.
        /// </remarks>
        public bool LoaderRequired { get; set; }

        /// <summary>
        /// What type of hardware it is
        /// </summary>
        public PcmType HardwareType { get; set; }

        /// <summary>
        /// What service number is it (0/false for unknown)
        /// </summary>
        public uint ServiceNumber { get; private set; }

        /// <summary>
        /// Does it have a slave CPU?
        /// </summary>
        public bool HardwareSlaveCPU { get; set; }

        /// <summary>
        /// Name of the kernel file to use.
        /// </summary>
        public string KernelFileName { get; set; } // Given the naming scheme, this entry shouldn't be needed. Refactor these calls to do something like $"{FileType}-{HardwareType}.bin";

        /// <summary>
        /// Base address to begin writing the kernel to.
        /// </summary>
        public int KernelBaseAddress { get; set; }

        /// <summary>
        /// Name of the kernel loader file to use.
        /// </summary>
        public string LoaderFileName { get; set; } // Given the naming scheme, this entry shouldn't be needed. Refactor these calls to do something like $"{FileType}-{HardwareType}.bin";

        /// <summary>
        /// Base address to begin writing the kernel loader to.
        /// </summary>
        public int LoaderBaseAddress { get; set; }

        /// <summary>
        /// Base address to begin reading or writing the ROM contents.
        /// </summary>
        public int ImageBaseAddress { get; set; }

        /// <summary>
        /// Size of the ROM.
        /// </summary>
        public int ImageSize { get; set; }

        /// <summary>
        /// Which key algorithm to use to unlock the PCM.
        /// </summary>
        public int KeyAlgorithm { get; set; }

        /// <summary>
        /// Supports file validation checksums?
        /// </summary>
        public bool ChecksumSupport { get; set; }

        /// <summary>
        /// Supports flash sector CRC?
        /// </summary>
        public bool FlashCRCSupport { get; set; }

        /// <summary>
        /// Does PCM's kernel support flash chip identification?
        /// </summary>
        public bool FlashIDSupport { get; set; }

        /// <summary>
        /// Does PCM's kernel support version number identification?
        /// </summary>
        public bool KernelVersionSupport { get; set; }

        /// <summary>
        /// PCM kernel max block size.
        /// </summary>
        public int KernelMaxBlockSize { get; set; }

        /// <summary>
        /// If false, writes must be blocked when a boot-sector write is required.
        /// </summary>
        public bool IsSupportedWriteBootSector { get; set; }

        /// <summary>
        /// Indicates that support for this PCM type is still in development.
        /// </summary>
        public bool IsUnderDevelopment { get; set; }

        public bool IsCustomOS
        {
            get
            {
                if (CurrentOS == null) return false;
                return CurrentOS.Manufacturer != Manufacturer;
            }
        }


        public ECUBase() {
            Initialize();
        }

        public ECUBase(int osid) {
            Initialize();
        }

        public void Initialize() {
            KnownOperatingSystems = new List<OSInfo>();
            IsSupported = false;
            IsSupportedRead = false;
            IsSupportedWrite = false;
            IsSupportedWriteSlaveCPU = false;
            IsSupportedWriteBySegment = false;
            IsSupportedWriteBootSector = true;
            Description = "Not Set";
            LoaderRequired = false;
            HardwareType = PcmType.Undefined;
            ServiceNumber = 0;
            HardwareSlaveCPU = false;
            KernelFileName = string.Empty;
            KernelBaseAddress = 0x0;
            LoaderFileName = string.Empty;
            LoaderBaseAddress = 0x0;
            ImageBaseAddress = 0x0;
            KeyAlgorithm = 0;
            ChecksumSupport = false;
            FlashCRCSupport = false;
            FlashIDSupport = false;
            KernelVersionSupport = false;
            KernelMaxBlockSize = 4096;
            IsUnderDevelopment = false;
        }

        public bool ECUSupportsOSID(uint osid)
        {
            string osidString = osid.ToString();
            if (osidString.Length == 7 && osidString.Substring(0, 2) == "12" && osidString[4] == '0' && (osidString[3] == '2' || osidString[3] == '3')) // Version 2 & 3 handled here.
            {
                switch (osidString[5])
                {
                    case '0': // P01
                        if(HardwareType == PcmType.P01) {
                           return true;
                        }
                        break;
                    case '5': // P59
                        if(HardwareType == PcmType.P59) {
                            return true;
                        }
                        break;
                }
            }
            if (KnownOperatingSystems.Any(x => x.OSID == osid)) {
                return true;
            }
            return false;
        }

        public void SetCurrentOSID(uint osid) {
            CurrentOS = KnownOperatingSystems.FirstOrDefault(x => x.OSID == osid);
            KeyAlgorithm = CurrentOS.KeyAlgorithm;
            ServiceNumber = (uint)CurrentOS.ServiceNumber;
            if (CurrentOS == null) {
                CurrentOS = new OSInfo("MFG", osid, 0, KeyAlgorithm);
            }
        }

        public override string ToString()
        {
            string suffix = IsCustomOS ? "COS" : "OEM";
            string servNo = ServiceNumber != 0 ? $"{ServiceNumber}." : string.Empty;
            string servString = $"{servNo}{suffix}";
            if (CurrentOS.ServiceNumber == -1)
            {
                return "Unsupported ECU";
            }
            string format = "{0}_{1} - {2} {3}K";
            return string.Format(format, Manufacturer, HardwareType, servString, ImageSize);
        }
    }
}
