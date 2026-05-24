using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU {
    public abstract class ECUBase {
        public List<OSInfo> KnownOperatingSystems { get; set; }

        public OSInfo CurrentOSID { get; private set; }

        /// <summary>
        /// Descriptive text.
        /// </summary>
        public string Description { get; set; }

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
        public string KernelFileName { get; set; }

        /// <summary>
        /// Base address to begin writing the kernel to.
        /// </summary>
        public int KernelBaseAddress { get; set; }

        /// <summary>
        /// Name of the kernel loader file to use.
        /// </summary>
        public string LoaderFileName { get; set; }

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
        public bool IsSupportedWriteBootSector { get; protected set; }

        /// <summary>
        /// Indicates that support for this PCM type is still in development.
        /// </summary>
        public bool IsUnderDevelopment { get; protected set; }


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

        public virtual bool ECUSupportsOSID(uint osid) {
            return KnownOperatingSystems.Any(x => x.OSID == osid);
        }

        public void SetCurrentOSID(uint osid) {
            CurrentOSID = KnownOperatingSystems.FirstOrDefault(x => x.OSID == osid);
            if (CurrentOSID == null) {
                CurrentOSID = new OSInfo(osid, 0, "Unknown OS", KeyAlgorithm);
            }
        }
    }
}
