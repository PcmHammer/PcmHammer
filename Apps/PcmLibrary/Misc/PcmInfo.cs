// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking
{
    public enum PcmType
    {
        Undefined = 0, // required for failed osid test on binary file
        P01,
        P59,
        P04_Early,
        P04,
        P05,
        P05b,
        P05c,
        P08,
        P10,
        P11,
        P12,
        E38,
        E54, // 01-04 LB7 Duramax
        E60, // 4-05 LLY Duramax
        BlackBox
    }

    /// <summary>
    /// Which kernel a PCM operation needs. Read and verify use the read kernel; write and
    /// test-write use the write kernel. Most PCMs use one kernel for both (see
    /// <see cref="OSIDInfo.GetKernelFileName"/>).
    /// </summary>
    public enum KernelOperation
    {
        Read,
        Write,
    }

    /// <summary>
    /// A flashable slave CPU module: the package <see cref="PackageImage.Target"/> it maps to (e.g.
    /// "slave-os"), and the DID that reports its part number.
    /// </summary>
    public readonly struct SlaveModuleId
    {
        public string Target { get; }
        public byte Did { get; }

        public SlaveModuleId(string target, byte did)
        {
            this.Target = target;
            this.Did = did;
        }
    }

    /// <summary>
    /// This combines various metadata about whatever PCM we've connected to.
    /// </summary>
    /// <remarks>
    /// This was ported from the LS1 flashing utility originally posted at pcmhacking.net.
    /// </remarks>
    public class OSIDInfo
    {
        /// <summary>
        /// Operating system ID.
        /// </summary>
        public uint OSID { get; private set; }

        /// <summary>
        /// Descriptive text.
        /// </summary>
        public string Description { get; private set; } = string.Empty;

        /// <summary>
        /// Indicates whether this PCM is supported by the app.
        /// </summary>
        public bool IsSupported { get; private set; }

        /// <summary>
        /// Indicates whether this PCM is supported to read
        /// </summary>
        public bool IsSupportedRead { get; private set; }

        /// <summary>
        /// Indicates whether this PCM is supported to write
        /// </summary>
        public bool IsSupportedWrite { get; private set; }

        /// <summary>
        /// Indicates whether this PCM is supports writing the slave CPU. If it cannot, we must block (or warn) OSID change.
        /// </summary>
        public bool IsSupportedWriteSlaveCPU { get; private set; }

        /// <summary>
        /// Indicates whether this PCM is supported to write by flash segment (later PCMs)
        /// </summary>
        public bool IsSupportedWriteBySegment { get; private set; }

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
        public PcmType HardwareType { get; private set; }

        /// <summary>
        /// What service number is it (0/false for unknown)
        /// </summary>
        public uint ServiceNumber { get; private set; }

        /// <summary>
        /// Does it have a slave CPU?
        /// </summary>
        public bool HardwareSlaveCPU { get; private set; }

        /// <summary>
        /// The slave CPU's flashable modules. Empty for PCMs without a distinct slave module.
        /// </summary>
        public IReadOnlyList<SlaveModuleId> SlaveModules { get; private set; } = Array.Empty<SlaveModuleId>();

        /// <summary>
        /// Name of the kernel file to use. The generic kernel for this PCM; used for any
        /// operation that does not have an operation-specific kernel defined below.
        /// </summary>
        public string KernelFileName { get; private set; } = string.Empty;

        /// <summary>
        /// Optional kernel used specifically for read/verify. Empty -> fall back to
        /// <see cref="KernelFileName"/>. (Most PCMs use a single kernel for everything.)
        /// </summary>
        public string ReadKernelFileName { get; private set; } = string.Empty;

        /// <summary>
        /// Optional kernel used specifically for write/test-write. Empty -> fall back to
        /// <see cref="KernelFileName"/>.
        /// </summary>
        public string WriteKernelFileName { get; private set; } = string.Empty;

        /// <summary>
        /// The kernel file to load for the given operation: the read- or write-specific kernel
        /// when defined, otherwise the generic <see cref="KernelFileName"/>. Read and verify pass
        /// <see cref="KernelOperation.Read"/>; write and test-write pass
        /// <see cref="KernelOperation.Write"/>.
        /// </summary>
        public string GetKernelFileName(KernelOperation operation)
        {
            string specific = operation == KernelOperation.Write ? this.WriteKernelFileName : this.ReadKernelFileName;
            return string.IsNullOrEmpty(specific) ? this.KernelFileName : specific;
        }

        /// <summary>
        /// Base address to begin writing the kernel to.
        /// </summary>
        public int KernelBaseAddress { get; private set; }

        /// <summary>
        /// Address at which the uploaded kernel begins executing. Usually the kernel base, but some
        /// </summary>
        public int KernelRunAddress { get; private set; }

        /// <summary>
        /// Indicates whether this PCM can be programmed through its resident boot loader.
        /// </summary>
        public bool IsSupportedBootLoaderWrite { get; private set; }

        /// <summary>
        /// Address the boot loader stages module data at, before programming the flash named by the
        /// module header.
        /// </summary>
        public uint BootLoaderStagingAddress { get; private set; }

        /// <summary>
        /// Maximum boot loader download message length, including the six byte TransferData header.
        /// </summary>
        public int BootLoaderBlockSize { get; private set; }

        /// <summary>
        /// Length of the master OS module header, which is sent as a message of its own ahead of the
        /// module data.
        /// </summary>
        public int BootLoaderMasterHeaderLength { get; private set; }

        /// <summary>
        /// Length of the slave OS module header, which is sent as a message of its own ahead of the
        /// module data.
        /// </summary>
        public int BootLoaderSlaveHeaderLength { get; private set; }

        /// <summary>
        /// DID of the handshake that starts the master burn.
        /// </summary>
        public byte BootLoaderMasterHandshakeDid { get; private set; }

        /// <summary>
        /// DID of the handshake that engages the slave.
        /// </summary>
        public byte BootLoaderSlaveHandshakeDid { get; private set; }

        /// <summary>
        /// Name of the flash routine file the boot loader runs to program the master.
        /// </summary>
        public string BootLoaderMasterLibraryFileName { get; private set; } = string.Empty;

        /// <summary>
        /// Name of the flash routine file the boot loader relays to the slave to program it.
        /// </summary>
        public string BootLoaderSlaveDriverFileName { get; private set; } = string.Empty;

        /// <summary>
        /// Start of the SRAM parameter mirror the boot loader write fills with 0xFF, so the stale mirror
        /// is not saved back over the new parameter flash at shutdown. 0 disables the fill.
        /// </summary>
        public uint SramEraseStart { get; private set; }

        /// <summary>
        /// Length of the <see cref="SramEraseStart"/> range. 0 disables the fill.
        /// </summary>
        public uint SramEraseLength { get; private set; }

        /// <summary>
        /// Which GMLAN protocol variant this CAN PCM speaks (None for non-CAN PCMs). Selects the
        /// <see cref="CanKernelUploadProtocol"/> in <see cref="CanCommands.UploadKernel"/>.
        /// </summary>
        public GMLANProtocol GMLANProtocol { get; private set; }

        /// <summary>
        /// Name of the kernel loader file to use.
        /// </summary>
        public string LoaderFileName { get; private set; } = string.Empty;

        /// <summary>
        /// Base address to begin writing the kernel loader to.
        /// </summary>
        public int LoaderBaseAddress { get; private set; }

        /// <summary>
        /// Base address to begin reading or writing the ROM contents.
        /// </summary>
        public int ImageBaseAddress { get; private set; }

        /// <summary>
        /// Size of the ROM.
        /// </summary>
        public int ImageSize { get; private set; }

        /// <summary>
        /// Which key algorithm to use to unlock the PCM.
        /// </summary>
        public int KeyAlgorithm { get; private set; }

        /// <summary>
        /// Supports file validation checksums?
        /// </summary>
        public bool ChecksumSupport { get; private set; }

        /// <summary>
        /// Supports flash sector CRC?
        /// </summary>
        public bool FlashCRCSupport { get; private set; }

        /// <summary>
        /// Does PCM's kernel support flash chip identification?
        /// </summary>
        public bool FlashIDSupport { get; private set; }

        /// <summary>
        /// Does PCM's kernel support version number identification?
        /// </summary>
        public bool KernelVersionSupport { get; private set; }

        /// <summary>
        /// Should the app ask the kernel whether the IAC (idle air control) driver chip is present?
        /// </summary>
        /// <remarks>
        /// Check if the PCM contains a IAC driver chip.
        /// Enable for kernels that this function (currently P01/P59 only)
        /// </remarks>
        public bool DetectIAC { get; private set; }

        /// <summary>
        /// PCM kernel max block size.
        /// </summary>
        public int KernelMaxBlockSize { get; private set; }

        /// <summary>
        /// If false, writes must be blocked when a boot-sector write is required.
        /// </summary>
        public bool IsSupportedWriteBootSector { get; private set; }

        /// <summary>
        /// Indicates that support for this PCM type is still in development.
        /// </summary>
        public bool IsUnderDevelopment { get; private set; }

        /// <summary>
        /// The bus this PCM communicates on.
        /// </summary>
        public BusProtocol BusProtocol { get; private set; }

        /// <summary>
        /// Populate this object based on the given PcmType.
        /// </summary>
        public bool PCMInfo(PcmType pcmType)
        {
            this.IsSupported = false;
            this.IsSupportedRead = false;
            this.IsSupportedWrite = false;
            this.IsSupportedWriteSlaveCPU = false;
            this.IsSupportedWriteBySegment = false;
            this.IsSupportedWriteBootSector = true;
            this.Description = "Not Set";
            this.LoaderRequired = false;
            this.HardwareType = PcmType.Undefined;
            this.ServiceNumber = 0;
            this.HardwareSlaveCPU = false;
            this.SlaveModules = Array.Empty<SlaveModuleId>();
            this.KernelFileName = string.Empty;
            this.ReadKernelFileName = string.Empty;
            this.WriteKernelFileName = string.Empty;
            this.KernelBaseAddress = 0x0;
            this.KernelRunAddress = 0x0;
            this.IsSupportedBootLoaderWrite = false;
            this.BootLoaderStagingAddress = 0x0;
            this.BootLoaderBlockSize = 0x0;
            this.BootLoaderMasterHeaderLength = 0x0;
            this.BootLoaderSlaveHeaderLength = 0x0;
            this.BootLoaderMasterHandshakeDid = 0x0;
            this.BootLoaderSlaveHandshakeDid = 0x0;
            this.BootLoaderMasterLibraryFileName = string.Empty;
            this.BootLoaderSlaveDriverFileName = string.Empty;
            this.SramEraseStart = 0x0;
            this.SramEraseLength = 0x0;
            this.GMLANProtocol = GMLANProtocol.None;
            this.BusProtocol = BusProtocol.Vpw;
            this.LoaderFileName = string.Empty;
            this.LoaderBaseAddress = 0x0;
            this.ImageBaseAddress = 0x0;
            this.KeyAlgorithm = 0;
            this.ChecksumSupport = false;
            this.FlashCRCSupport = false;
            this.FlashIDSupport = false;
            this.KernelVersionSupport = false;
            this.DetectIAC = false;
            this.KernelMaxBlockSize = 4096;
            this.IsUnderDevelopment = false;


            switch (pcmType)
            {
                case PcmType.Undefined:
                    this.Description = "unknown";
                    this.HardwareType = PcmType.Undefined;
                    this.ServiceNumber = 0;
                    break;

                case PcmType.P01:
                    this.Description = "P01 512KiB";
                    this.HardwareType = PcmType.P01;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = true;
                    this.IsSupportedWriteBySegment = true;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P01.bin";
                    this.KernelBaseAddress = 0xFF8000;
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 40;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.DetectIAC = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P59:
                    this.Description = "P59 1MiB";
                    this.HardwareType = PcmType.P59;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = true;
                    this.IsSupportedWriteBySegment = true;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P01.bin";
                    this.KernelBaseAddress = 0xFF8000;
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 1024 * 1024;
                    this.KeyAlgorithm = 40;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.DetectIAC = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P04_Early:
                    this.Description = "P04 1996/1997 256KiB V6";
                    this.HardwareType = PcmType.P04_Early;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = true;
                    this.IsSupportedWriteBySegment = false;
                    this.LoaderRequired = true;
                    this.KernelFileName = "Kernel-P04_Early.bin";
                    this.KernelBaseAddress = 0xFF8000;
                    this.LoaderFileName = "Loader-P04.bin";
                    this.LoaderBaseAddress = 0xFF9890;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 256 * 1024;
                    this.KeyAlgorithm = 6;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P04:
                    this.Description = "P04 1998+ 512KiB V6";
                    this.HardwareType = PcmType.P04;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = true;
                    this.IsSupportedWriteBySegment = false;
                    this.LoaderRequired = true;
                    this.KernelFileName = "Kernel-P04.bin";
                    this.KernelBaseAddress = 0xFF8000;
                    this.LoaderFileName = "Loader-P04.bin";
                    this.LoaderBaseAddress = 0xFF9890;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 14;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P05:
                    this.Description = "P05 (VPW)";
                    this.HardwareType = PcmType.P05;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = false;
                    this.IsSupportedWriteBootSector = false;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P05.bin";
                    this.KernelBaseAddress = 0xFFC100;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 1024 * 1024;
                    this.KeyAlgorithm = 0x35;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P05b:
                    this.Description = "P05b (VPW+CAN)";
                    this.HardwareType = PcmType.P05b;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = false;
                    this.IsSupportedWriteBootSector = false;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P05.bin";
                    this.KernelBaseAddress = 0xFFC100;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 1024 * 1024;
                    this.KeyAlgorithm = 0x35;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P05c:
                    this.Description = "P05c (CAN)";
                    this.HardwareType = PcmType.P05c;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = false;
                    this.IsSupportedWriteBootSector = true; // tested on bench, boot sector write successful.
                    this.BusProtocol = BusProtocol.Can500k;
                    this.GMLANProtocol = GMLANProtocol.P05c;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P05c-read.bin";       // read / verify
                    this.WriteKernelFileName = "Kernel-P05c-write.bin"; // write / test-write
                    this.KernelBaseAddress = 0xFF61AE; // must be this load address
                    this.KernelRunAddress = 0xFF61AE;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 1024 * 1024;
                    this.KeyAlgorithm = 0x35;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P08:
                    this.Description = "P08 512KiB i4";
                    this.HardwareType = PcmType.P08;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = true;
                    this.IsSupportedWriteBySegment = false;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P08.bin";
                    this.KernelBaseAddress = 0xFFABE0;
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 13;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P10:
                    this.Description = "P10 1Mb";
                    this.HardwareType = PcmType.P10;
                    this.HardwareSlaveCPU = true;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = true;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P10.bin";
                    this.KernelBaseAddress = 0xFFB800;
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 66;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P11:
                    this.Description = "P11";
                    this.HardwareType = PcmType.P11;
                    this.IsUnderDevelopment = true;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = true;
                    this.IsSupportedWriteBootSector = true;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P11.bin";
                    this.KernelBaseAddress = 0xFFC000;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 0x0D;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.P12:
                    this.Description = "P12 1Mb (Atlas I4/I5/I6)";
                    this.HardwareType = PcmType.P12;
                    this.HardwareSlaveCPU = true;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = true;
                    this.IsSupportedWriteBootSector = false;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-P12.bin";
                    this.KernelBaseAddress = 0xFF2000; // or FF0000? https://pcmhacking.net/forums/viewtopic.php?f=42&t=7742&start=450#p115622
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 1024 * 1024;
                    this.KeyAlgorithm = 91;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.E54:
                    this.Description = "E54 LB7 Duramax";
                    this.HardwareType = PcmType.E54;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = true;
                    this.IsSupportedWriteBySegment = true;
                    this.Description = "E54";
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-E54.bin";
                    this.KernelBaseAddress = 0xFF9100;
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 54;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.BlackBox:
                    this.Description = "Vortec BlackBox";
                    this.HardwareType = PcmType.BlackBox;
                    this.HardwareSlaveCPU = false;
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.IsSupportedWriteSlaveCPU = false;
                    this.IsSupportedWriteBySegment = false;
                    this.LoaderRequired = false;
                    this.KernelFileName = "Kernel-BlackBox.bin";
                    this.KernelBaseAddress = 0xFFC300;
                    this.LoaderFileName = string.Empty;
                    this.LoaderBaseAddress = 0x0;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 512 * 1024;
                    this.KeyAlgorithm = 16;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.KernelMaxBlockSize = 4096;
                    break;

                case PcmType.E38:
                    this.Description = "E38";
                    this.HardwareType = PcmType.E38;
                    this.HardwareSlaveCPU = true;
                    // The slave CPU's two flash modules, and the SWMI DIDs that report their part numbers.
                    this.SlaveModules = new[]
                    {
                        new SlaveModuleId("slave-os", 0xC9),          // SWMI 09
                        new SlaveModuleId("slave-calibration", 0xCA), // SWMI 10
                    };
                    this.IsSupported = true;
                    this.IsSupportedRead = true;
                    this.IsSupportedWrite = true;
                    this.BusProtocol = BusProtocol.Can500k;
                    this.GMLANProtocol = GMLANProtocol.E38;
                    this.KernelFileName = "Kernel-E38.bin";
                    this.KernelBaseAddress = 0x003FC430;
                    this.KernelRunAddress = 0x003FC434;
                    this.IsSupportedBootLoaderWrite = true;
                    this.BootLoaderStagingAddress = 0x003F9090;
                    this.BootLoaderBlockSize = 0x0FFE;
                    this.BootLoaderMasterHeaderLength = 0x800;
                    this.BootLoaderSlaveHeaderLength = 0x80;
                    this.BootLoaderMasterHandshakeDid = 0xC1;
                    this.BootLoaderSlaveHandshakeDid = 0xC9;
                    this.BootLoaderMasterLibraryFileName = "bootlib-E38-Master.bin";
                    this.BootLoaderSlaveDriverFileName = "bootlib-E38-Slave.bin";
                    // The mirror's validity marker lives below the staging address, so the fill survives
                    // the module data that later streams over the top of this range. 0x2000 covers the
                    // mirror and stays below the kernel base.
                    this.SramEraseStart = 0x003F8000;
                    this.SramEraseLength = 0x2000;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 2048 * 1024;
                    this.KeyAlgorithm = 0x92;
                    this.ChecksumSupport = true;
                    this.FlashCRCSupport = true;
                    this.FlashIDSupport = true;
                    this.KernelVersionSupport = true;
                    this.IsUnderDevelopment = false;
                    break;

                case PcmType.E60:
                    this.Description = "E60 LLY Duramax";
                    this.HardwareType = PcmType.E60;
                    this.KeyAlgorithm = 2;
                    this.ImageBaseAddress = 0x0;
                    this.ImageSize = 1024 * 1024;
                    break;

                default: return false;
            }
            return true;
        }

        /// <summary>
        /// Populate this object based on the given PCM type.
        /// </summary>
        public OSIDInfo(PcmType pcmType)
        {
            this.OSID = 0;
            this.PCMInfo(pcmType);
        }

        /// <summary>
        /// Populate this object based on the given OSID.
        /// </summary>
        public OSIDInfo(uint osid)
        {
            this.OSID = osid;

            // special cases for COS
            string osidString = osid.ToString();

            // Some COS formats appear to follow the following convention. Note counting from 0, not 1.
            // 0 = 1
            // 1 = 2
            // 2 = COS type. Have observed 5 = 2 Bar RTT, 6 = 3 Bar Non-RTT, 7 = 1 Bar RTT, 8 = MAF RTT
            // 3 = Version number. 1 Appears to have custom keys, 2 and 3 do not.
            // 4 = 0
            // 5 = 0 for P01, 5 for P59
            // 6 = OS variant
            if (osidString.Length == 7 && osidString.Substring(0, 2) == "12" && osidString[4] == '0' &&  (osidString[3] == '2' || osidString[3] == '3')) // Version 2 & 3 handled here.
            {
                switch (osidString[5])
                {
                    case '0': // P01
                        PCMInfo(PcmType.P01);
                        this.Description = "VCM Suite P01 COS 512KiB";
                        return;
                    case '5': // P59
                        PCMInfo(PcmType.P59);
                        this.Description = "VCM Suite P59 COS 1MiB";
                        return;
                }
            }

            // OSID Lookups
            switch (osid)
            {
                // LB7 Duramax EFI Live COS
                case 1337601:
                case 1337605:
                case 1710001:
                case 1710005:
                case 1887301:
                case 1887305:
                case 2444101:
                case 2444105:
                case 2600601:
                case 2600605:
                case 2685301:
                case 2685305:
                case 3904401:
                case 3904405:
                    PCMInfo(PcmType.E54);
                    this.Description = "E54 LB7 EFILive COS";
                    break;

                // LB7 Duramax service no 9388505
                case 15063376:
                case 15097100:
                case 15188873:
                    PCMInfo(PcmType.E54);
                    this.Description = "E54 Service No 9388505";
                    break;

                // LB7 Duramax service no 12210729
                case 15085499:
                case 15094441:
                case 15166853:
                case 15186006:
                case 15189044:
                    PCMInfo(PcmType.E54);
                    this.Description = "E54 Service No 12210729";
                    break;

                // E54 service number unknown
                case 9393838:
                    PCMInfo(PcmType.E54);
                    this.Description = "E54";
                    this.ServiceNumber = 0;
                    break;

                // LLY Duramax E60 service no 12244189 - 1mbyte?
                case 15141668:
                case 15193885:
                case 15228758:
                case 15231599:
                case 15231600:
                case 15879103:
                case 15087230:
                    PCMInfo(PcmType.E60);
                    this.Description = "E60 Service No 12244189";
                    break;

                // LL7 Duramax EFI Live Cos
                case 04166801:
                case 04166805:
                case 05160001:
                case 05160005:
                case 05388501:
                case 05388505:
                case 05875801:
                case 05875805:
                    PCMInfo(PcmType.E60);
                    this.Description = "LLY EFILive COS";
                    break;

                // VCM Suite COS Version 1
                case 1251001:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite 2 Bar";
                    this.KeyAlgorithm = 3;
                    break;

                case 1261001:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite 3 Bar";
                    this.KeyAlgorithm = 4;
                    break;

                case 1271001:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite Mafless";
                    this.KeyAlgorithm = 5;
                    break;

                case 1281001:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite MAF RTT";
                    this.KeyAlgorithm = 6;
                    break;

                case 1271002:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite Mafless";
                    this.KeyAlgorithm = 7;
                    break;

                case 1251002:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite 2 Bar";
                    this.KeyAlgorithm = 8;
                    break;

                case 1261002:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite MAF RTT";
                    this.KeyAlgorithm = 9;
                    break;

                case 1281002:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite 3 Bar";
                    this.KeyAlgorithm = 10;
                    break;

                case 1271003:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite Mafless";
                    this.KeyAlgorithm = 11;
                    break;

                case 1251003:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite 2 Bar";
                    this.KeyAlgorithm = 12;
                    break;

                case 1261003:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite 3 Bar";
                    this.KeyAlgorithm = 13;
                    break;

                case 1281003:
                    PCMInfo(PcmType.P01);
                    this.Description = "VCM Suite MAF RTT";
                    this.KeyAlgorithm = 14;
                    break;

                //------- HPT COS -----------
                case 1250013:
                case 1250018:
                case 1251005:
                case 1251006:
                case 1251008:
                case 1251010:
                case 1251011:
                case 1251012:
                case 1251014:
                case 1251016:
                case 1251017:
                case 1260006:
                case 1260011:
                case 1261005:
                case 1261008:
                case 1261014:
                case 1261016:
                case 1270013:
                case 1270017:
                case 1271005:
                case 1271006:
                case 1271008:
                case 1271010:
                case 1271011:
                case 1271012:
                case 1271014:
                case 1271016:
                case 1271018:
                case 1273001:
                case 1273002:
                case 1273003:
                case 1273004:
                case 1273005:
                case 1273006:
                case 1273007:
                case 1273008:
                case 1273009:
                case 1273010:
                case 1273011:
                case 1273012: // reported on FB https://www.facebook.com/groups/pcmhammerusers/permalink/25879085071683496/
                case 1273013:
                case 1273014:
                case 1281005:
                case 1281006:
                case 1281008:
                case 1281010:
                case 1281011:
                case 1281012:
                case 1281014:
                case 1281016:
                case 1281918:
                    PCMInfo(PcmType.P01);
                    this.Description = "Unknown VCM Suite COS";
                    break;

                //-------------------------

                // 12583560
                case 12590777:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59a Hybrid Service No 12583560";
                    this.ServiceNumber = 12583560;
                    break;

                // 9354896
                case 9360360:
                case 9360361:
                case 9361140:
                case 9363996:
                case 9365637:
                case 9373372:
                case 9376077:
                case 9378746:
                case 9379910:
                case 9381344:
                case 12205612:
                case 12584929:
                case 12593359:
                case 12597506:
                    PCMInfo(PcmType.P01);
                    this.Description = "P01 Service No 9354896";
                    this.ServiceNumber = 9354896;
                    break;

                // 12200411
                case 12202088: // main 2000/2001 OS
                case 12206871:
                case 12208322:
                case 12209203:
                case 12212156:
                case 12213218:
                case 12216125:
                case 12220483:
                case 12221588:
                case 12225074: // main 2003/2004 OS
                case 12593358:
                    PCMInfo(PcmType.P01);
                    this.Description = "P01 Service No 12200411";
                    this.ServiceNumber = 12200411;
                    break;

                case 1250001:
                case 1250002:
                case 1250003:
                case 1270001:
                case 1270002:
                case 1270003:
                case 1290001:
                case 1290002:
                case 1290003:
                case 1290005:
                case 2010001:
                case 2020001:
                case 2020002:
                case 2020003:
                case 2020005:
                case 2030001:
                case 2030002:
                case 2030003:
                case 2040001:
                case 2040002:
                case 2040003:
                case 3110001:
                case 3130001:
                case 3150001:
                case 3150002:
                case 3170001:
                case 3190001:
                case 3190002:
                case 4072901:
                case 4072902:
                case 4072903:
                case 4073001:
                case 4073002:
                case 4073101:
                case 4073102:
                case 4080001:
                case 4110002:
                case 4140001:
                case 4140002:
                case 5120001:
                    PCMInfo(PcmType.P01);
                    this.ServiceNumber = 12200411;
                    string type = osid.ToString();
                    switch (Convert.ToInt32(type, 10))
                    {
                        case 1:
                            this.Description = "P01 EFI Live COS1";
                            break;

                        case 2:
                            this.Description = "P01 EFI Live COS2";
                            break;

                        case 3:
                            this.Description = "P01 EFI Live COS3";
                            break;

                        case 5:
                            this.Description = "P01 EFI Live COS5";
                            break;

                        default:
                            this.Description = "P01 EFI Live COS";
                            break;
                    }
                    break;

                case 3150003:
                case 3190003:
                case 4073003:
                case 4073103:
                case 4110001:
                case 4110003:
                case 4140003:
                case 4140004:
                case 5120002:
                case 5120003:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 EFI Live COS";
                    this.ServiceNumber = 12200411;
                    break;

                // 1Mb pcms
                // GMC Sierra service number 12589463 
                case 12591725:
                case 12592618:
                case 12593555:
                case 12606961:
                case 12612115:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 Service No 12589463";
                    this.ServiceNumber = 12589463;
                    break;

                //case 1250052:
                //case 1250058:
                //case 4073103:
                case 12564440:
                case 12585950:
                case 12588804:
                case 12592425:
                case 12592433: //Aussie
                case 12606960:
                case 12612114:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 Service No 12586242";
                    this.ServiceNumber = 12589463;
                    break;

                // usa 12586243
                case 12587603:
                case 12587604:
                case 76030003:
                case 76030004:
                case 76030005:
                case 76030006:
                case 76030007:
                case 76030008:
                case 76030009:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 Service No 12586243";
                    this.ServiceNumber = 12589463;
                    break;

                // not sure 12582605
                case 12578128:
                case 12579405:
                case 12580055:
                case 12593058:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 Service No 12582605";
                    this.ServiceNumber = 12582605;
                    break;

                // not sure 12582811
                case 12587811:
                case 12605114:
                case 12606807:
                case 12608669:
                case 12613245:
                case 12613246:
                case 12613247:
                case 12619623:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 Service No 12582811";
                    this.ServiceNumber = 12582811;
                    break;

                // not sure 12602802
                case 12597120:
                case 12613248:
                case 12619624:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59 Service No 12602802";
                    this.ServiceNumber = 12602802;
                    break;

                // P59 service number unknown
                case 12568640:
                case 12584941:
                case 12591347:
                case 12592122:
                case 12594368:
                case 12604949:
                    PCMInfo(PcmType.P59);
                    this.Description = "P59";
                    this.ServiceNumber = 0;
                    break;

                // 96/97 BlackBox is very different to 98+ and is unsupported. It has 2x 128KiB flash chips and talks its own OBD variant.
                case 9378495:
                case 16187577:
                case 16227245:
                case 16232915:
                case 16235505:
                case 16237015:
                case 16256445:
                    PCMInfo(PcmType.Undefined); 
                    this.Description = "Vortec Black Box 96/97, 5 Connector, Service No 16244210 (unsupported)";
                    this.ServiceNumber = 16244210;
                    break;

                // Vortec Black Box 98-02, 4 Plug, Service No 9366810
                case 9365095:
                case 16263425: // 9366810 'black box'
                    PCMInfo(PcmType.BlackBox);
                    this.Description = "Vortec Black Box 98/99 Service No 9366810 or 9355699";
                    this.ServiceNumber = 9366810;
                    break;
                
                // Vortec Black Box Service No 16263494
                case 9360505:
                case 9365085:
                case 16265175:
                    PCMInfo(PcmType.BlackBox);
                    this.Description = "Vortec Black Box 98-02, 4 Plug, Service No 16263494";
                    this.ServiceNumber = 16263494;
                    break;

                // BlackBox service number unknown
                case 16258745:
                    PCMInfo(PcmType.BlackBox);
                    this.Description = "BlackBox";
                    this.ServiceNumber = 0;
                    break;

                // 1996/1997 V8 Service number 16238212. Looks like the mid 90s 256k P04
                case 9350054:
                case 9350490:
                case 9350494:
                case 9350496:
                case 9350497:
                case 9352141:
                case 9352151:
                case 9352161:
                case 9352171:
                case 9352201:
                case 9352221:
                case 9352231:
                case 9352241:
                case 9352251:
                case 9352261:
                case 9352271:
                case 9352281:
                case 9352291:
                case 9352311:
                case 9352321:
                case 9352641:
                case 9352651:
                case 9354201:
                case 9354735:
                case 9354736:
                case 9354737:
                case 9354738:
                case 9354739:
                case 9354740:
                case 9355161:
                case 9355171:
                case 9355181:
                case 9355191:
                case 9355201:
                case 9355211:
                case 9355221:
                case 9355231:
                case 9359661:
                case 9365931:
                case 9365941:
                case 9365951:
                case 9365961:
                case 9365981:
                case 9366221:
                case 9369651:
                case 9375571:
                case 9375581:
                case 9375583:
                case 9375584:
                case 9375591:
                case 9375601:
                case 9375611:
                case 9375621:
                case 9375631:
                case 9375641:
                case 9375651:
                case 9384530:
                case 9384532:
                case 9384535:
                case 9384537:
                case 9384538:
                case 9384539:
                case 9384540:
                case 9384542:
                case 9384545:
                case 9384547:
                case 9384548:
                case 9384549:
                case 9384557:
                case 9384558:
                case 9384562:
                case 9384569:
                case 9384575:
                case 9384584:
                case 9384585:
                case 9384587:
                case 9384589:
                case 9384590:
                case 9384592:
                case 9393822:
                case 12480112:
                case 12593456:
                case 12593457:
                case 12593458:
                case 12593459:
                case 12596953:
                case 12596954:
                case 12596955:
                case 12596956:
                case 16238127:
                case 16238234:
                case 16238242:
                case 16238297:
                case 16238302:
                case 16238303:
                case 16238352:
                case 16238353:
                case 16238354:
                case 16238356:
                case 16238357:
                case 16238360:
                case 16238362:
                case 16244351:
                case 16244371:
                case 16244391:
                case 16255801:
                case 16256470:
                case 16256510:
                case 16256512:
                case 16256514:
                case 16256515:
                case 16256516:
                case 16256520:
                case 16256523:
                case 16256525:
                case 16256908:
                case 16264963:
                case 16264964:
                case 16264966:
                case 16265756:
                case 16266578:
                case 16266579:
                case 16266580:
                case 16266582:
                case 16266583:
                case 16266585:
                case 16267006:
                case 16267014:
                case 16267957:
                case 16267958:
                case 16267960:
                case 16267962:
                case 16267963:
                case 16267964:
                case 16267965:
                case 16267966:
                case 16267967:
                case 16267968:
                case 16267969:
                case 16268315:
                case 16268316:
                case 16268317:
                case 16268319:
                case 16268320:
                case 16268474:
                case 22480054:
                case 42480054:
                    PCMInfo(PcmType.Undefined);
                    this.Description = "1997, 1998 LS1 Corvette, Camaro, Firebird Service No 16238212";
                    this.ServiceNumber = 16238212;
                    break;

                // Service number 16207326 P04 Early
                case 9352140:
                case 9352142:
                case 9352143:
                case 9352146:
                case 9352149:
                case 9352150:
                case 9352340:
                case 9352342:
                case 9352346:
                case 9352347:
                case 9352553:
                case 9354241:
                case 9354242:
                case 9355430:
                case 9355433:
                case 9355435:
                case 9365005:
                case 9365007:
                case 9365012:
                case 9365017:
                case 9365650:
                case 9365651:
                case 9365652:
                case 9365655:
                case 9367092:
                case 16210002:
                case 16210010:
                case 16219361:
                case 16219368:
                case 16226502:
                case 16227925:
                case 16230131:
                case 16230375:
                case 16230378:
                case 16230380:
                case 16230383:
                case 16230385:
                case 16231872:
                case 16231877:
                case 16231889:
                case 16231890:
                case 16231940:
                case 16231941:
                case 16232010:
                case 16232466:
                case 16232477:
                case 16233470:
                case 16233471:
                case 16233472:
                case 16233478:
                case 16234097:
                case 16234124:
                case 16234438:
                case 16234440:
                case 16234441:
                case 16234751:
                case 16235667:
                case 16238373:
                case 16238443:
                case 16238446:
                case 16238447:
                case 16238448:
                case 16238450:
                case 16238452:
                case 16238453:
                case 16238456:
                case 16238457:
                case 16238458:
                case 16238517:
                case 16238518:
                case 16238519:
                case 16238520:
                case 16238523:
                case 16238525:
                case 16238532:
                case 16238533:
                case 16240118:
                case 16240637:
                case 16241229:
                case 16245321:
                case 16246063:
                case 16246066:
                case 16249992:
                case 16249995:
                case 16251203:
                case 16251205:
                case 16251975:
                case 16257917:
                case 16257918:
                case 16257922:
                case 16257926:
                case 16257935:
                case 16257936:
                case 16257937:
                case 16257938:
                case 16257940:
                case 16257942:
                case 16257947:
                case 16257950:
                case 16257952:
                case 16257953:
                case 16257955:
                case 16257956:
                    PCMInfo(PcmType.P04_Early);
                    this.Description = "P04 Early 256KiB Service No 16207326";
                    this.ServiceNumber = 16207326;
                    break;

                // Service numbers 16207326, 16217058
                case 16257963:
                case 16257965:
                case 16257966:
                case 16259008:
                case 16259012:
                case 16259016:
                case 16259017:
                case 16259018:
                case 16259195:
                case 16259652:
                case 16259654:
                case 16259659:
                case 16259660:
                case 16259664:
                case 16259666:
                case 16259667:
                case 16259669:
                case 16259670:
                case 16259672:
                case 16259676:
                case 16259677:
                case 16259682:
                case 16259687:
                case 16259688:
                case 16259694:
                case 16259696:
                case 16259697:
                case 16259698:
                case 16259702:
                case 16259704:
                case 16259705:
                case 16259708:
                case 16259710:
                case 16259712:
                case 16259714:
                case 16259715:
                case 16259716:
                case 16259717:
                case 16259720:
                case 16259906:
                case 16265837:
                case 16265838:
                case 16265841:
                case 16265843:
                case 16266038:
                case 16266902:
                case 16268480:
                case 16268483:
                case 16268485:
                case 16268488:
                case 24233869:
                case 24233870:
                case 24234035:
                case 24234036:
                case 24234037:
                case 24234038:
                case 28004945:
                case 28029988:
                case 93802333:
                    PCMInfo(PcmType.P04_Early);
                    this.KeyAlgorithm = 0x06;
                    this.ImageSize = 512 * 1024;
                    this.Description = "P04 Early 512KiB Service No 16207326, 16217058 or 16227797"; // SN 16217058 testing
                    this.ServiceNumber = 16217058; // cant find data to split this group but as they compatible use 16207326 for both for now.
                    break;

                // Service number
                case 9350560:
                case 9355202:
                case 9355203:
                case 9355205:
                case 9355206:
                case 9355207:
                case 9355208:
                case 9355440:
                case 9355441:
                case 9355443:
                case 9355445:
                case 9355488:
                case 9355496:
                case 9355497:
                case 9355498:
                case 9355503:
                case 9355506:
                case 9359551:
                case 9359552:
                case 9359553:
                case 9359555:
                case 9359556:
                case 9359557:
                case 9359635:
                case 9359636:
                case 9359757:
                case 9359758:
                case 9359760:
                case 9359762:
                case 9362998:
                case 9363007:
                case 9363080:
                case 9363082:
                case 9363090:
                case 9363091:
                case 9363101:
                case 9363102:
                case 9363104:
                case 9363106:
                case 9363107:
                case 9363108:
                case 9363110:
                case 9363381:
                case 9364942:
                case 9364943:
                case 9364945:
                case 9364967:
                case 9364968:
                case 9364972:
                case 9364975:
                case 9364977:
                case 9364978:
                case 9365041:
                case 9365077:
                case 9365078:
                case 9365081:
                case 9365088:
                case 9365091:
                case 9365092:
                case 9365093:
                case 9365096:
                case 9365097:
                case 9365281:
                case 9365282:
                case 9365287:
                case 9365302:
                case 9365304:
                case 9365311:
                case 9365314:
                case 9365316:
                case 9365340:
                case 9365341:
                case 9365342:
                case 9365346:
                case 9365347:
                case 9365348:
                case 9365350:
                case 9365351:
                case 9365355:
                case 9365356:
                case 9365357:
                case 9365358:
                case 9365360:
                case 9383352:
                case 12201561:
                case 16221445:
                case 16231196:
                case 16231303:
                case 16231321:
                case 16234305:
                case 16235290:
                case 16242720:
                case 16243381:
                case 16243385:
                case 16243605:
                case 16243608:
                case 16243643:
                case 16243648:
                case 16243817:
                case 16243821:
                case 16243822:
                case 16243823:
                case 16243826:
                case 16243827:
                case 16244313:
                case 16244314:
                case 16244316:
                case 16244317:
                case 16244318:
                case 16244323:
                case 16244324:
                case 16244906:
                case 16245158:
                case 16245161:
                case 16245165:
                case 16245167:
                case 16245168:
                case 16245315:
                case 16245316:
                case 16245318:
                case 16245478:
                case 16245480:
                case 16245670:
                case 16248316: // Service No 16227797
                case 16248320:
                case 16251789:
                case 16251790:
                case 16251791:
                case 16252225:
                case 16252226:
                case 16252227:
                case 16252229:
                case 16252230:
                case 16252720:
                case 16252926:
                case 16252933:
                case 16252936:
                case 16252940:
                case 16252943:
                case 16252946:
                case 16252951:
                case 16252952: // 97 Lesabre Service No 16217058
                case 16252955:
                case 16252956:
                case 16252957:
                case 16252958:
                case 16252960:
                case 16252961:
                case 16252962:
                case 16252963:
                case 16252965:
                case 16252966:
                case 16253031:
                case 16253035:
                case 16254057:
                case 16254062:
                case 16254063:
                case 16254333:
                case 16254337:
                case 16254342:
                case 16254467:
                case 16254468:
                case 16254712:
                case 16254718:
                case 16254720:
                case 16255225:
                case 16255243:
                case 16255967:
                case 16257407:
                case 16257531:
                case 16257532:
                case 16257533:
                case 16257536: // Service number 16227797
                case 16257537:
                case 16257542:
                case 16257551:
                case 16257725:
                case 16257726:
                    PCMInfo(PcmType.P04_Early);
                    this.ImageSize = 512 * 1024;
                    this.Description = "P04 Early 512KiB Service No 16227797";
                    this.ServiceNumber = 16227797;
                    break;

                // P04 V6 Service number 9374997
                case 9355672:
                case 9356706:
                case 9357008:
                case 9357010:
                case 9363226:
                case 9365170:
                case 9365972:
                case 9365973:
                case 9365977:
                case 9365978:
                case 9365983:
                case 9365986:
                case 9366308:
                case 9366310:
                case 9366315:
                case 9366318:
                case 9367318:
                case 9367321:
                case 9367398:
                case 9367515:
                case 9367516:
                case 9367752:
                case 9367753:
                case 9367757:
                case 9367758:
                case 9367767:
                case 9367772:
                case 9368196:
                case 9369225:
                case 9369227:
                case 9369228:
                case 9369229:
                case 9369230:
                case 9369231:
                case 9369232:
                case 9369252:
                case 9369308:
                case 9369309:
                case 9369311:
                case 9369312:
                case 9369319:
                case 9369320:
                case 9369321:
                case 9369326:
                case 9369395:
                case 9369396:
                case 9370627:
                case 9370635:
                case 9370688:
                case 9370700:
                case 9371626:
                case 9371627:
                case 9371628:
                case 9372327:
                case 9372328:
                case 9372332:
                case 9372357:
                case 9372358:
                case 9372360:
                case 9372361:
                case 9372362:
                case 9372363:
                case 9372465:
                case 9372466:
                case 9372474:
                case 9372477:
                case 9373168:
                case 9373171:
                case 9373175:
                case 9373176:
                case 9373181:
                case 9373182:
                case 9373184:
                case 9373962:
                case 9374336:
                case 9374337:
                case 9374338:
                case 9374625:
                case 9374628:
                case 9375070:
                case 9375073:
                case 9375118:
                case 9376661:
                case 9376662:
                case 9376663:
                case 9377380:
                case 9377381:
                case 9377385:
                case 9377388:
                case 9377389:
                case 9377390:
                case 9377391:
                case 9377392:
                case 9377399:
                case 9377525:
                case 9377542:
                case 9377739:
                case 9378063:
                case 9378065:
                case 9378067:
                case 9378068:
                case 9378070:
                case 9378072:
                case 9378075:
                case 9378076:
                case 9378077:
                case 9379115:
                case 9379116:
                case 9379117:
                case 9379123:
                case 9379126:
                case 9379128:
                case 9379427:
                case 9379492:
                case 9379603:
                case 9379805:
                case 9379806:
                case 9382210:
                case 9382460:
                case 9382915:
                case 9382916:
                case 9382918:
                case 9382920:
                case 9382926:
                case 9382950:
                case 9382951:
                case 9382956:
                case 9383066:
                case 9383068:
                case 9383074:
                case 9383081:
                case 9383084:
                case 9383086:
                case 9383087:
                case 9383088:
                case 9383091:
                case 9386582:
                case 9386586:
                case 12214377:
                case 12214379:
                case 16241831:
                case 16241840:
                case 16242217:
                case 16242228:
                case 16242233:
                case 16242236:
                case 16243026:
                case 16255667:
                case 16255675:
                case 16255677:
                case 16255680:
                case 16255681:
                case 16255794:
                case 16256047:
                case 16257159:
                case 16257165:
                case 16257166: // this one found on a 9380717
                case 16257169:
                case 16257171:
                    PCMInfo(PcmType.P04);
                    this.KeyAlgorithm = 0x0E;
                    this.Description = "P04 512KiB Service No 9374997 or 9380717 (algo 14)";
                    this.ServiceNumber = 9374997;
                    break;

                // P04 V6 Service number 9380717
                case 9354406:
                case 9356245:
                case 9356247:
                case 9356248:
                case 9356251:
                case 9356252:
                case 9356253:
                case 9356256:
                case 9356258:
                case 9363607:
                case 9363608:
                case 9364123:
                case 9364125:
                case 9364126:
                case 9364127:
                case 9374398:
                case 9374402:
                case 9377336:
                case 9380138:
                case 9380140:
                case 9380718:
                case 9380973:
                case 9381748:
                case 9381752:
                case 9381754:
                case 9381776:
                case 9381796:
                case 9381797:
                case 9381798:
                case 9381815:
                case 9382558:
                case 9382562:
                case 9382566:
                case 9382572:
                case 9382728:
                case 9382729:
                case 9382730:
                case 9384010:
                case 9384011:
                case 9384012:
                case 9384013:
                case 9384015:
                case 9384017:
                case 9384018:
                case 9384020:
                case 9384022:
                case 9384023:
                case 9384027:
                case 9384028:
                case 9384033:
                case 9384035:
                case 9384036:
                case 9384042:
                case 9384043:
                case 9384046:
                case 9384047:
                case 9384048:
                case 9384050:
                case 9384051:
                case 9384052:
                case 9384053:
                case 9384073:
                case 9384075:
                case 9384434:
                case 9384436:
                case 9384437:
                case 9384438:
                case 9384441:
                case 9384442:
                case 9384457:
                case 9384458:
                case 9384462:
                case 9384464:
                case 9384465:
                case 9384467:
                case 9384471:
                case 9384473:
                case 9384477:
                case 9386283:
                case 9386285:
                case 9386286:
                case 9386287:
                case 9386288:
                case 9386289:
                case 9387045:
                case 9387047:
                case 9387048:
                case 9387112:
                case 9389253:
                case 9389256:
                case 9389257:
                case 9389258:
                case 9389259:
                case 9389260:
                case 9389282:
                case 9389283:
                case 9389339:
                case 9389341:
                case 9389343:
                case 9389346:
                case 9389348:
                case 9389349:
                case 9389352:
                case 9389356:
                case 9389397:
                case 9389666:
                case 9389667:
                case 9389668:
                case 9389670:
                case 9389676:
                case 9389679:
                case 9389687:
                case 9389688:
                case 9389692:
                case 9389695:
                case 9389708:
                case 9389750:
                case 9389752:
                case 9389759:
                case 9389760:
                case 9389761:
                case 9389766:
                case 9389767:
                case 9389769:
                case 9389770:
                case 9389909:
                case 9390172:
                case 9390758:
                case 9390763:
                case 9390765:
                case 9391248:
                case 9392594:
                case 9392748:
                case 9392787:
                case 9392790:
                case 9392791:
                case 9392794:
                case 9392797:
                case 9392798:
                case 9392800:
                case 9392801:
                case 9392802:
                case 9392804:
                case 9392807:
                case 9393295:
                case 9393297:
                case 9393300:
                case 9393302:
                case 9393307:
                case 9393309:
                case 9393313:
                case 9393315:
                case 9393580:
                case 9393581:
                case 9393598:
                case 9393608:
                case 9393613:
                case 9393832:
                case 9393898:
                case 9393901:
                case 10384529:
                case 12201457:
                case 12201458:
                case 12201460:
                case 12201461:
                case 12201462:
                case 12201463:
                case 12201465:
                case 12201466:
                case 12201467:
                case 12201468:
                case 12201687:
                case 12201772:
                case 12201779:
                case 12201782:
                case 12201783:
                case 12201785:
                case 12201786:
                case 12201787:
                case 12201788:
                case 12201791:
                case 12201792:
                case 12201793:
                case 12201795:
                case 12201796:
                case 12201797:
                case 12201803:
                case 12201822:
                case 12201829:
                case 12201830:
                case 12201840:
                case 12201850:
                case 12201862:
                case 12201863:
                case 12201865:
                case 12201866:
                case 12201867:
                case 12201868:
                case 12201875:
                case 12201876:
                case 12201877:
                case 12201878:
                case 12201879:
                case 12201881:
                case 12201885:
                case 12201886:
                case 12201887:
                case 12201888:
                case 12201889:
                case 12201891:
                case 12202127:
                case 12202129:
                case 12202133:
                case 12202135:
                case 12202155:
                case 12202881:
                case 12202882:
                case 12202885:
                case 12202941:
                case 12202942:
                case 12202945:
                case 12203016:
                case 12203657:
                case 12203659:
                case 12203661:
                case 12203792:
                case 12203793:
                case 12203795:
                case 12203796:
                case 12203797:
                case 12203798:
                case 12203799:
                case 12203800:
                case 12203801:
                case 12203802:
                case 12203803:
                case 12203805:
                case 12204282:
                case 12204287:
                case 12204288:
                case 12204290:
                case 12204437:
                case 12204438:
                case 12204439:
                case 12205378:
                case 12205379:
                case 12211882:
                case 12211883:
                case 12214055:
                case 12214056:
                case 12214057:
                case 12214058:
                case 12214381:
                case 12214391:
                case 12214436:
                case 12214710:
                case 12214711:
                case 12214712:
                case 12214713:
                case 12215038:
                case 12215040:
                case 12215321:
                case 12215452:
                case 12220113:
                case 12220115:
                case 12220117:
                case 12220118:
                case 12221090:
                case 12221092:
                case 12221101:
                case 12221112:
                case 12582150:
                case 12582151:
                case 12582152:
                case 12582153:
                case 12583164:
                case 16242202:
                case 16243034:
                case 16258875:
                    PCMInfo(PcmType.P04);
                    this.Description = "P04 Service No 9380717";
                    this.ServiceNumber = 9380717;
                    break;

                // P04 V6 Service number 12209624 This list contains a lot of P08, incorrectly. Need better data.
                case 9354438:
                case 9354966:
                case 9354967:
                case 9361336:
                case 9361343:
                case 9361346:
                case 9361352:
                case 9361362:
                case 9361387:
                case 9361393:
                case 9361397:
                case 9387282:
                case 9387290:
                case 9387291:
                case 9387292:
                case 9387295:
                case 9387297:
                case 9387301:
                case 9387307:
                case 9387318:
                case 9387320:
                case 9387322:
                case 9387324:
                case 9387327:
                case 9387333:
                case 9387337:
                case 9387341:
                case 9387343:
                case 9387346:
                case 9387555:
                case 9387884:
                case 9388994:
                case 9388997:
                case 9388999:
                case 9389017:
                case 9389020:
                case 9389022:
                case 9389026:
                case 9389028:
                case 9390701:
                case 9390703:
                case 12200755:
                case 12200756:
                case 12200757:
                case 12200760:
                case 12200775:
                case 12201137:
                case 12201205:
                case 12201206:
                case 12202879:
                case 12203285:
                case 12203945:
                case 12203948:
                case 12204628:
                case 12205246:
                case 12205248:
                case 12205275:
                case 12205278:
                case 12205280:
                case 12205283:
                case 12205310:
                case 12205311:
                case 12205317:
                case 12205325:
                case 12205330:
                case 12205333:
                case 12205335: // Could be service number 16217058. Have 1 recport of this.
                case 12205340:
                case 12205341:
                case 12205345:
                case 12205348:
                case 12205381:
                case 12205382:
                case 12205551:
                case 12205555:
                case 12205556:
                case 12205560:
                case 12205924:
                case 12206000:
                case 12206007:
                case 12206011:
                case 12206017:
                case 12206018:
                case 12206021:
                case 12206022:
                case 12206024:
                case 12206031:
                case 12206035:
                case 12206036:
                case 12206038:
                case 12206039:
                case 12206040:
                case 12206041:
                case 12206046:
                case 12206048:
                case 12206050:
                case 12206052:
                case 12206055:
                case 12206057:
                case 12206438:
                case 12207406:
                case 12207408:
                case 12207409:
                case 12207412:
                case 12207413:
                case 12207415:
                case 12207417:
                case 12207419:
                case 12207420:
                case 12207421:
                case 12207422:
                case 12207423:
                case 12207428:
                case 12207429:
                case 12207431:
                case 12207432:
                case 12207433:
                case 12207436:
                case 12207438:
                case 12207606:
                case 12207787:
                case 12207859:
                case 12207862:
                case 12207868:
                case 12207872:
                case 12207873:
                case 12207879:
                case 12208155:
                case 12208157:
                case 12208326:
                case 12208327:
                case 12208328:
                case 12208330:
                case 12208331:
                case 12208332:
                case 12208528:
                case 12208532:
                case 12208534:
                case 12208537:
                case 12208762:
                case 12208775:
                case 12209445:
                case 12209446:
                case 12209447:
                case 12209448:
                case 12209450:
                case 12210253:
                case 12210255:
                case 12210258:
                case 12210261:
                case 12211252:
                case 12211256:
                case 12211448:
                case 12211451:
                case 12211452:
                case 12211455:
                case 12211457:
                case 12211460:
                case 12211461:
                case 12211462:
                case 12211463:
                case 12211465:
                case 12211466:
                case 12211467:
                case 12211468:
                case 12211471:
                case 12211472:
                case 12211473:
                case 12211475:
                case 12211476:
                case 12211477:
                case 12211478:
                case 12211480:
                case 12211481:
                case 12211486:
                case 12211487:
                case 12211488:
                case 12211490:
                case 12211491:
                case 12211492:
                case 12211493:
                case 12211495:
                case 12211496:
                case 12211497:
                case 12211498:
                case 12211500:
                case 12211501:
                case 12211502:
                case 12211503:
                case 12211505:
                case 12211511:
                case 12211721:
                case 12211723:
                case 12212430:
                case 12213451:
                case 12213452:
                case 12213486:
                case 12213488:
                case 12213496:
                case 12213497:
                case 12213502:
                case 12214060:
                case 12214061:
                case 12214062:
                case 12214063:
                case 12214066:
                case 12214422:
                case 12214425:
                case 12214427:
                case 12215591:
                case 12215592:
                case 12215596:
                case 12215600:
                case 12215601:
                case 12215602:
                case 12215605:
                case 12215608:
                case 12215884:
                case 12215887:
                case 12216128:
                case 12216129:
                case 12216136:
                case 12216186:
                case 12216522:
                case 12216524:
                case 12216566:
                case 12216625:
                case 12216640:
                case 12217063:
                case 12217065:
                case 12217066:
                case 12217150:
                case 12217151:
                case 12217152:
                case 12217153:
                case 12217155:
                case 12217156:
                case 12217157:
                case 12217158:
                case 12217159:
                case 12217725:
                case 12217997:
                case 12217998:
                case 12217999:
                case 12218171:
                case 12218172:
                case 12218396:
                case 12218397:
                case 12218398:
                case 12218399:
                case 12218400:
                case 12218402:
                case 12218403:
                case 12218405:
                case 12218406:
                case 12218407:
                case 12218408:
                case 12218409:
                case 12218410:
                case 12218411:
                case 12218838:
                case 12218840:
                case 12218841:
                case 12218842:
                case 12218843:
                case 12218845:
                case 12218850:
                case 12218851:
                case 12218852:
                case 12218853:
                case 12218855:
                case 12218859:
                case 12218861:
                case 12218862:
                case 12218863:
                case 12218865:
                case 12218866:
                case 12219150:
                case 12219182:
                case 12219184:
                case 12219273:
                case 12219275:
                case 12219350:
                case 12219351:
                case 12221347:
                case 12221348:
                case 12221665:
                case 12221666:
                case 12221667:
                case 12221668:
                case 12221669:
                case 12221670:
                case 12221671:
                case 12221672:
                case 12221673:
                case 12221675:
                case 12221676:
                case 12221677:
                case 12221678:
                case 12221679:
                case 12221680:
                case 12221681:
                case 12221682:
                case 12221683:
                case 12221685:
                case 12221686:
                case 12221687:
                case 12221688:
                case 12221716:
                case 12221717:
                case 12221718:
                case 12221720:
                case 12221721:
                case 12221722:
                case 12221723:
                case 12221725:
                case 12221727:
                case 12221728:
                case 12221730:
                case 12221731:
                case 12221732:
                case 12221733:
                case 12222102:
                case 12222104:
                case 12222106:
                case 12222107:
                case 12222108:
                case 12222121:
                case 12222122:
                case 12222124:
                case 12222125:
                case 12222126:
                case 12222127:
                case 12222130:
                case 12222132:
                case 12222447:
                case 12223042:
                case 12223435:
                case 12223436:
                case 12223437:
                case 12223438:
                case 12223440:
                case 12223441:
                case 12223442:
                case 12223443:
                case 12223445:
                case 12223446:
                case 12223447:
                case 12223448:
                case 12223450:
                case 12223451:
                case 12223452:
                case 12223453:
                case 12223455:
                case 12223456:
                case 12223457:
                case 12223458:
                case 12223461:
                case 12223462:
                case 12223463:
                case 12223465:
                case 12223466:
                case 12223476:
                case 12223477:
                case 12223478:
                case 12223479:
                case 12223480:
                case 12223481:
                case 12223482:
                case 12223483:
                case 12223485:
                case 12223486:
                case 12223487:
                case 12223488:
                case 12223489:
                case 12223490:
                case 12223491:
                case 12223492:
                case 12223493:
                case 12223495:
                case 12223496:
                case 12223497:
                case 12224907:
                case 12224908:
                case 12224911:
                case 12224912:
                case 12225135:
                case 12225136:
                case 12225137:
                case 12225337:
                case 12225339:
                case 12225341:
                case 12225342:
                case 12225344:
                case 12226103:
                case 12226105:
                case 12226106:
                case 12226107:
                case 12226747:
                case 12226748:
                case 12227240:
                case 12227495:
                case 12227496:
                case 12227671:
                case 12228140:
                case 12228141:
                case 12228142:
                case 12248764:
                case 12571887:
                case 12571888:
                case 12571889:
                case 12571891:
                case 12571892:
                case 12571893:
                case 12571894:
                case 12576146:
                case 12576196:
                case 12578497:
                case 12578498:
                case 12578500:
                case 12578847:
                case 12578848:
                case 12578849:
                case 12578851:
                case 12578852:
                case 12578875:
                case 12578876:
                case 12578877:
                case 12578878:
                case 12578904:
                case 12578905:
                case 12579862:
                case 12580026:
                case 12580028:
                case 12580030:
                case 12580031:
                case 12580032:
                case 12580033:
                case 12580048:
                case 12580050:
                case 12580052:
                case 12580524:
                case 12580525:
                case 12580526:
                case 12581460:
                case 12581461:
                case 12581463:
                case 12581464:
                case 12581465:
                case 12581466:
                case 12581467:
                case 12581468:
                case 12581469:
                case 12581470:
                case 12581471:
                case 12581506:
                case 12583478:
                case 12583479:
                case 12583589:
                case 12583590:
                case 12583710:
                case 12583711:
                case 12583754:
                case 12583759:
                case 12583761:
                case 12583770:
                case 12583781:
                case 12583783:
                case 12583787:
                case 12584714:
                case 12584716:
                case 12584720:
                case 12586810:
                case 12586811:
                case 12586812:
                case 12586813:
                case 12586814:
                case 12586815:
                case 12586816:
                case 12586817:
                case 12586818:
                case 12586819:
                case 12586820:
                case 12586821:
                case 12586822:
                case 12586823:
                case 12586824:
                case 12586825:
                case 12586826:
                case 12586827:
                case 12586828:
                case 12586829:
                case 12586830:
                case 12586831:
                case 12586832:
                case 12586833:
                case 12586834:
                case 12586835:
                case 12586836:
                case 12586837:
                case 12586838:
                case 12588234:
                case 12588235:
                case 12588938:
                case 12588939:
                case 12588941:
                case 12589089:
                case 12589141:
                case 12589145:
                case 12589512:
                case 12589513:
                case 12589514:
                    PCMInfo(PcmType.P04);
                    this.Description = "P04 Service No 12209624";
                    this.ServiceNumber = 12209624;
                    break;

                // P04 V6 Service number 12583826
                case 12573213:
                case 12573215:
                case 12573219:
                case 12573286:
                case 12573290:
                case 12573292:
                case 12574547:
                case 12574550:
                case 12574552:
                case 12577455:
                case 12577456:
                case 12577457:
                case 12578504:
                case 12579928:
                case 12580118:
                case 12580122:
                case 12580123:
                case 12580124:
                case 12580125:
                case 12580146:
                case 12580147:
                case 12580149:
                case 12580150:
                case 12580151:
                case 12580152:
                case 12581386:
                case 12581387:
                case 12581388:
                case 12583340:
                case 12583341:
                case 12583343:
                case 12583370:
                case 12583373:
                case 12583374:
                case 12583375:
                case 12583379:
                case 12583380:
                case 12583381:
                case 12583382:
                case 12583394:
                case 12583396:
                case 12583431:
                case 12583432:
                case 12583433:
                case 12583434:
                case 12583435:
                case 12583436:
                case 12583441:
                case 12583442:
                case 12587897:
                case 12587899:
                case 12587900:
                case 12587901:
                case 12587902:
                case 12587903:
                case 12587904:
                case 12587905:
                case 12588072:
                case 12590901:
                case 12590902:
                case 12596205:
                case 12596206:
                case 12596207:
                case 12596208:
                case 12596209:
                case 12596210:
                case 12596211:
                case 12596212:
                case 12596213:
                case 12596260:
                case 12596261:
                case 12596441:
                case 12596957:
                case 12596959:
                case 12598451:
                case 12598452:
                case 12598453:
                case 12598587:
                case 12598588:
                case 12598589:
                    PCMInfo(PcmType.P04);
                    this.Description = "P04 Service No 12583826";
                    this.ServiceNumber = 12583826;
                    break;

                // P04 V6 Service number 12583827
                /*------------------------------
                https://pcmhacking.net/forums/viewtopic.php?f=42&t=8201&start=100#p122330
                2004 2004 Alero
                2004 2005 Aztek
                2004 2005 Bonneville
                2004 2004 Century
                2004 2005 Grand Am
                2003 2003 Grand Prix
                2004 2005 Impala
                2004 2005 Lesabre
                2004 2005 Lumina
                2004 2005 Montana
                2004 2005 Monte Carlo
                2004 2005 Park Avenue
                2004 2005 Regal
                2003 2005 Rendezvous
                2004 2004 Silhouette
                2004 2005 Transport
                2004 2005 Venture
                ------------------------------*/
                case 12207875:
                case 12579927:
                case 12579929:
                case 12579931:
                case 12580134:
                case 12580139:
                case 12580141:
                case 12580143:
                case 12580144:
                case 12581052:
                case 12583338:
                case 12583339:
                case 12583342:
                case 12583369:
                case 12584933:
                case 12584938:
                case 12584939:
                case 12585032:
                case 12585034:
                case 12585045:
                case 12585047:
                case 12585048:
                case 12585050:
                case 12585051:
                case 12585053:
                case 12585054:
                case 12585087:
                case 12585089:
                case 12585090:
                case 12585091:
                case 12585093:
                case 12585094:
                case 12585095:
                case 12585096:
                case 12585097:
                case 12585098:
                case 12585133:
                case 12585134:
                case 12586386:
                case 12586387:
                case 12586587:
                case 12586930:
                case 12586931:
                case 12586953:
                case 12586954:
                case 12587009:
                case 12587545:
                case 12587658:
                case 12587799:
                case 12587800:
                case 12587801:
                case 12587802:
                case 12587803:
                case 12587804:
                case 12587898:
                case 12588115:
                case 12588500:
                case 12589761:
                case 12589762:
                case 12589763:
                case 12590900:
                case 12592109:
                case 12592110:
                case 12592111:
                case 12592112:
                case 12592113:
                case 12592114:
                case 12592115:
                case 12592116:
                case 12592117:
                case 12592638:
                case 12592639:
                case 12592640:
                case 12593468:
                case 12593469:
                case 12593470:
                // case 12593523: could be a P11? https://pcmhacking.net/forums/viewtopic.php?p=120275#p120275
                case 12593819:
                case 12593820:
                case 12593821:
                case 12593822:
                case 12594004:
                case 12594005:
                case 12594008:
                case 12594017:
                case 12594020:
                case 12594194:
                case 12594195:
                case 12594196:
                case 12594314:
                case 12594316:
                case 12594382:
                case 12594385:
                case 12594386:
                case 12594527:
                case 12594528:
                case 12594529:
                case 12594532:
                case 12594535:
                case 12594541:
                case 12594542:
                case 12598601:
                case 12598602:
                case 12598603:
                case 12598604:
                case 12600188:
                case 12600189:
                case 12600190:
                case 12600191:
                case 12600192:
                case 12600193:
                case 12600777:
                case 12602858:
                case 12602859:
                case 12603032:
                case 12618852:
                case 12618855:
                case 12618856:
                case 12618857:
                case 12618860:
                case 12618861:
                case 12618862:
                case 12618864:
                case 12618865:
                case 12618867:
                case 12618868:
                case 12618869:
                case 12618873:
                case 12618875:
                case 12618876:
                case 12618878:
                case 12618879:
                case 12618883:
                case 12618885:
                case 12618892:
                case 12618893:
                case 12618894:
                case 12618895:
                case 15285380:
                case 15286085:
                case 15286245:
                case 15292691:
                    PCMInfo(PcmType.P04);
                    this.Description = "P04 Service No 12583827";
                    this.ServiceNumber = 12583827;
                    break;

                // P04 V6 Service number 16236757
                case 9351345:
                case 9351352:
                case 9351357:
                case 9351362:
                case 9351398:
                case 9351408:
                case 9351412:
                case 9352072:
                case 9352613:
                case 9352617:
                case 9352676:
                case 9352680:
                case 9352682:
                case 9352697:
                case 9352701:
                case 9352731:
                case 9352732:
                case 9352737:
                case 9352738:
                case 9352739:
                case 9352741:
                case 9352742:
                case 9352743:
                case 9352746:
                case 9352747:
                case 9352748:
                case 9352750:
                case 9352751:
                case 9352752:
                case 9352757:
                case 9352758:
                case 9352762:
                case 9352766:
                case 9352771:
                case 9352797:
                case 9352799:
                case 9352800:
                case 9352801:
                case 9352802:
                case 9352803:
                case 9352806:
                case 9352807:
                case 9352808:
                case 9352809:
                case 9352820:
                case 9352822:
                case 9352823:
                case 9352826:
                case 9352827:
                case 9352828:
                case 9353084:
                case 9353151:
                case 9353162:
                case 9353166:
                case 9353692:
                case 9353694:
                case 9353708:
                case 9353711:
                case 9353712:
                case 9353714:
                case 9353726:
                case 9353728:
                case 9353731:
                case 9354147:
                case 9356708:
                case 9357027:
                case 9357035:
                case 9357127:
                case 9357128:
                case 9357130:
                case 9357132:
                case 9357155:
                case 9357156:
                case 9361236:
                case 9361281:
                case 9361291:
                case 9361300:
                case 9364326:
                case 9364357:
                case 9364358:
                case 9364360:
                case 9364361:
                case 9364368:
                case 9364369:
                case 9364371:
                case 9365036:
                case 9365037:
                case 9367747:
                case 9369193:
                case 9369195:
                case 9369196:
                case 9369197:
                case 9369392:
                case 9369403:
                case 9369407:
                case 9369995:
                case 9370646:
                case 9370647:
                case 9370648:
                case 9370650:
                case 9373897:
                case 9374770:
                case 9374773:
                case 9374775:
                case 9374785:
                case 9374788:
                case 9374790:
                case 9374958:
                case 9374960:
                case 9374962:
                case 9374963:
                case 9374965:
                case 9374966:
                case 9376743:
                case 9376746:
                case 9376747:
                case 9376748:
                case 9376752:
                case 9377382:
                case 9377383:
                case 9377740:
                case 9378498:
                case 9379775:
                case 9379778:
                case 9379781:
                case 9379787:
                case 9379790:
                case 9379793:
                case 9379796:
                case 9379798:
                case 9379800:
                case 9379801:
                case 9379802:
                case 9379808:
                case 9379811:
                case 9379813:
                case 9382735:
                case 9382770:
                case 9384498:
                case 9384500:
                case 9384502:
                case 9384505:
                case 9384516:
                case 9384517:
                case 9384519:
                case 9384786:
                case 9384787:
                case 9386157:
                case 9386447:
                case 9386448:
                case 9386577:
                case 9386578:
                case 9386580:
                case 9386583:
                case 9389403:
                case 9389716:
                case 9389753:
                case 12201445:
                case 12201470:
                case 12202132:
                case 12210352:
                case 12210353:
                case 12214378:
                case 12214380:
                case 16236745:
                case 16236748:
                case 16236749:
                case 16237036:
                case 16237042:
                case 16237082:
                case 16237089:
                case 16237209:
                case 16242757:
                case 16265087:
                case 16265088:
                case 16265090:
                case 16265091:
                case 16266322:
                case 16266323:
                case 16266326:
                case 16266327:
                case 16266339:
                case 16266346:
                case 16266348:
                case 16266352:
                case 16266358:
                case 16266360:
                case 16266366:
                case 16266368:
                case 16266373:
                case 16266375:
                case 16266376:
                case 16266449:
                case 16267124:
                case 16267127:
                case 16267128:
                case 16267132:
                case 16267134:
                case 16267137:
                case 16267138:
                case 16267142:
                case 16267144:
                case 16267146:
                case 16267147:
                case 16267148:
                case 16267150:
                case 16267154:
                case 16267156:
                case 16267157:
                case 16267158:
                case 16267160:
                case 16267162:
                case 16268296:
                case 16268297:
                case 16268300:
                case 16268407:
                case 49807546:
                    PCMInfo(PcmType.P04);
                    this.Description = "P04 Service No 16236757";
                    this.ServiceNumber = 16236757;
                    break;

                case 12584056:
                case 12584057:
                case 12584058:
                case 12588931:
                case 12588932:
                case 12588933:
                case 12619740:
                case 12619742:
                case 12619744:
                    PCMInfo(PcmType.P05);
                    this.Description = "2004 P05 (VPW) Service No 12581501";
                    this.ServiceNumber = 12581501;
                    break;

                /*                
                case xxxxx:
                    PCMInfo(PcmType.P05);
                    this.Description = "2005 P05 (VPW) Service No 12581598";
                    this.ServiceNumber = 12581598;
                    break;
                */
                case 12597270: // tested on bench as dual protocol can + vpw on 12591279
                    PCMInfo(PcmType.P05);
                    this.Description = "2005 P05 (VPW) Service No 12591279";
                    this.ServiceNumber = 12591279;
                    break;

                // P05c (CAN only). OSID is DID 0xC1 (OS segment / Module 1), not 0xC9.
                case 12616867: // service no 12600930
                    PCMInfo(PcmType.P05c);
                    this.Description = "P05c (CAN) Service No 12600930";
                    this.ServiceNumber = 12600930;
                    break;

                // 2006-2009 P05 service number 12604962 is also CAN only (OSID not yet known)

                case 12603217:
                    PCMInfo(PcmType.P05);
                    this.Description = "2005 P05 (VPW+CAN) Service No 12604963";
                    this.ServiceNumber = 12604963;
                    break;

                // P05 service number unknown
                case 12592928:
                case 12596136:
                case 12596138:
                case 12600367:
                case 12603291:
                    PCMInfo(PcmType.P05);
                    this.Description = "P05";
                    this.ServiceNumber = 0;
                    break;

                // P05b service number unknown
                case 12585292:
                case 12585293:
                case 12585294:
                case 12589239:
                case 12594884:
                case 12596629:
                case 12596630:
                case 12596631:
                case 12596632:
                case 12596633:
                case 12598122:
                case 12598125:
                case 12598324:
                case 12598325:
                case 12599697:
                case 12600156:
                case 12600158:
                case 12600160:
                case 12603346:
                case 12603347:
                case 12603348:
                case 12608097:
                case 12608100:
                case 12612947:
                case 12612950:
                case 12619714:
                case 12619715:
                case 12619722:
                case 12619724:
                case 12626396:
                case 12626398:
                case 12635873:
                case 12635874:
                case 12635875:
                case 12635876:
                case 12635877:
                case 12635878:
                case 12635879:
                    PCMInfo(PcmType.P05b);
                    this.Description = "P05b";
                    this.ServiceNumber = 0;
                    break;

                // P08 Service Number 9356249
                case 9364970:
                case 9382954:  // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 9384480:  // via PM
                case 9387226:  // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 9392786:  // https://pcmhacking.net/forums/viewtopic.php?p=139820#p139820
                case 12202774: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12205537: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12205561: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12206029:
                case 12206030: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12206037: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 2000 Cavalier 2.2 manual
                case 12206044:
                case 12208154:
                case 12208156:
                case 12208767: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12208773:
                case 12216187: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12216489:
                case 12216571: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12221087: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 2000 Cavalier 2.2 auto
                case 12221096:
                case 12221111: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12222128: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 2000 Cavalier 2.4 auto
                case 12222134: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 grand am 2.4
                case 12222135: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 grand am 2.4
                case 12222446: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136163
                case 12225345: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 2000 Cavalier 2.2 manual
                case 12225346: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 2000 Cavalier 2.2 manual
                case 16257436:
                case 16259686:
                    PCMInfo(PcmType.P08);
                    this.Description = "P08 Service No 9356249";
                    this.ServiceNumber = 9356249;
                    break;

                // P08 Service Number 12202203
                case 9392792:
                case 9392795:
                case 9392796:
                case 12201233:
                case 12205552:
                case 12223041:
                case 12223044:
                case 12223046:
                case 12225338:
                case 12225340: // https://pcmhacking.net/forums/viewtopic.php?p=136159#p136159 2000 Cavalier 2.2 manual
                case 12571886:
                case 12580027:
                case 12580029:
                    PCMInfo(PcmType.P08);
                    this.Description = "P08 Service No 12202203";
                    this.ServiceNumber = 12202203;
                    break;

                // P08 Service No 16228016 (Part No 16245305)
                case 9351310:
                case 9353727:
                case 9364359:
                case 9364362:
                case 9364367:
                case 9364370:
                case 9364966:
                case 9364974:
                case 9364976:
                case 9365280:
                case 9365284:
                case 9365286:
                case 9365312:
                case 9365338:
                case 9374332:
                case 9374334:
                case 9383061:
                case 9383064:
                case 9383067:
                case 9383076:
                case 9387885:
                case 12222105:
                case 16254764:
                case 16259649:
                case 16259657:
                case 16259674:
                case 16259706:
                case 16259718:
                    PCMInfo(PcmType.P08);
                    this.Description = "P08 Service No 16228016";
                    this.ServiceNumber = 16228016;
                    break;

                // P08s of unknown service number (includes some P11) :(
                case 9351290:
                case 9351297:
                case 9351321:
                case 9353418:
                case 9353422:
                case 9353732:
                case 9353738:
                case 9354904:
                case 9354907:
                case 9354910:
                case 9354914:
                case 9355300:
                case 9355302:
                case 9356822:
                case 9364356:
                case 9365276:
                case 9365292:
                case 9365308:
                case 9365310:
                case 9365324:
                case 9367522:
                case 9368451:
                case 9368547:
                case 9372478:
                case 9373177:
                case 9374787:
                case 9382927:
                case 9382928:
                case 9382941:
                case 9382948:
                case 9382957:
                case 9383079:
                case 9383089:
                case 9387205:
                case 9387214:
                case 9387229:
                case 9387552:
                case 12201228:
                case 12201231:
                case 12201238:
                case 12205538:
                case 12205545:
                case 12206019:
                case 12206025:
                case 12206042:
                case 12206045:
                case 12206049:
                case 12208527:
                case 12208535:
                case 12216490:
                case 12216567:
                case 12216568:
                case 12217195:
                case 12221098:
                case 12222110:
                case 12222131:
                case 12223050:
                case 12225336:
                case 12571890:
                case 12578485:
                case 12580025:
                case 12583655:
                case 16253027:
                case 16267097:
                case 16267114:
                    PCMInfo(PcmType.P08);
                    this.Description = "P08";
                    this.ServiceNumber = 16228016; // unknown, use any for now.
                    break;

                // P10 Service No 12576463
                case 12213305:
                case 12571911:
                case 12575262:
                case 12579238:
                case 12587430:
                    PCMInfo(PcmType.P10);
                    this.Description = "P10 Service No 12576463";
                    this.ServiceNumber = 12576463;
                    break;

                //P10 Service No 12574976
                case 12577956:
                case 12579357:
                case 12584138:
                case 12584594:
                case 12587608:
                case 12588012:
                case 12589825:
                case 12590965:
                case 12595726:
                case 12597031:
                case 12623317:
                    PCMInfo(PcmType.P10);
                    this.Description = "P10 Service No 12574976";
                    this.ServiceNumber = 12574976;
                    break;

                // P11 service number unknown
                case 10384528:
                case 12215090:
                case 12215092:
                case 12217996:
                case 12218876:
                case 12218880:
                case 12218881:
                case 12218882:
                case 12218890:
                case 12218892:
                case 12219185:
                case 12219186:
                case 12224985:
                case 12226745:
                case 12229535:
                case 12235939:
                case 12243363:
                case 12571654:
                case 12571657:
                case 12578583:
                case 12578585:
                case 12579663:
                case 12579665:
                case 12579668:
                case 12579673:
                case 12580049:
                case 12580051:
                case 12582997:
                case 12583591:
                case 12583592:
                case 12583753:
                case 12583755:
                case 12583756:
                case 12583758:
                case 12583762:
                case 12583763:
                case 12583780:
                case 12583782:
                case 12583784:
                case 12583785:
                case 12583786:
                case 12584713:
                case 12584715:
                case 12584717:
                case 12584719:
                case 12585894:
                case 12587615:
                case 12587617:
                case 12593509:
                case 12593510:
                case 12593525:
                case 12593527:
                case 12593529:
                case 12594548:
                case 12594550:
                case 12596603:
                case 12596936:
                case 12597689:
                case 12597690:
                case 12598564:
                case 12598565:
                case 12598583:
                case 12598584:
                case 12598585:
                case 93802334:
                    PCMInfo(PcmType.P11);
                    this.Description = "P11";
                    this.ServiceNumber = 0;
                    break;
                case 12218878: // Antus' P11
                case 12593523: // https://pcmhacking.net/forums/viewtopic.php?p=120275#p120275
                    PCMInfo(PcmType.P11);
                    this.Description = "P11 Service No 12210553";
                    this.ServiceNumber = 0;
                    break;
                case 12586586: // Service number 12576162
                    PCMInfo(PcmType.P11);
                    this.Description = "P11 Service No 12576162";
                    this.ServiceNumber = 0;
                    break;

                // P12 1m
                case 12587007:
                case 12588651:
                case 12589166:
                case 12589312:
                case 12589586:
                case 12592070: //2004 Saturn Ion Redline 2.0L
                case 12593533:
                case 12596925: //2005 Saturn Ion Redline 2.0L
                case 12597778:
                case 12597978: //2004 Saturn Ion Redline 2.0L
                case 12598275: //2006 Chevy Cobalt SS 2.0L (Need to verify)
                case 12598284: //2006 Saturn Ion Redline 2.0L
                case 12601321: //2006 LX9
                case 12601774: //2005 Chevy Cobalt SS 2.0L
                case 12601904: //2005 Saturn Ion Redline 2.0L
                case 12604440: //LL8 - Atlas I6 (4200) P12
                case 12605256: //2006 Chevy Cobalt SS 2.0L
                case 12605261: //2006 Chevy Cobalt SS 2.0L
                case 12606374: //L52 - Atlas I5 (3500) P12
                case 12606375: //L52 - Atlas I5 (3500) P12
                case 12606400: //2006 Chevy Trailblazer 4.2L (Atlas I6)
                case 12610624: //2007 Chevy Cobalt SS 2.0L & 2007 Saturn Ion Redline 2.0L
                case 12610641: //GMS3 OS (04 Ion Redline GMS3)
                case 12610642: //GMS3 OS (05 Ion Redline GMS3)
                case 12610643: //GMS3 OS (06-07 Ion Redline GMS3)
                case 12610644: //GMS3 OS (05 Cobalt SS GMS3)
                case 12610645: //GMS3 OS (06-07 Cobalt SS GMS3)
                case 12623279:
                case 12627882:
                case 12627883:
                case 12627884:
                case 12627885: //2007 Trailblazer P12 (service number not confirmed, variant is)
                case 12631085:
                    this.Description = "P12 1Mb Service No 12597521";
                    this.ServiceNumber = 12597521;
                    PCMInfo(PcmType.P12);
                    break;

                // P12 2m - See: https://pcmhacking.net/forums/viewtopic.php?f=42&t=7742&start=470#p115747
                case 12609805:
                case 12611642:
                case 12613422: //2007 Chevy Trailblazer 4.2L
                case 12618164:
                    PCMInfo(PcmType.P12);
                    this.Description = "P12b (2Mb) Service No 12569773";
                    this.ImageSize = 2048 * 1024;
                    this.ServiceNumber = 12569773;
                    break;

                // P12 service number unknown
                case 5534509:
                case 12587080:
                case 12589734:
                case 12590492:
                case 12590652:
                case 12591649:
                case 12594432:
                case 12594447:
                case 12596301:
                case 12596785:
                case 12597424:
                case 12597635:
                case 12597756:
                case 12598555:
                case 12598559:
                case 12600819:
                case 12606370:
                case 12607148:
                case 12610647:
                case 12610648:
                case 12623277:
                case 12623278:
                case 19171603:
                    PCMInfo(PcmType.P12);
                    this.Description = "P12";
                    this.ServiceNumber = 0;
                    break;

                // E38
                case 12602922:
                case 12605732:
                case 12605898:
                case 12607218:
                case 12608677:
                case 12609099:
                case 12611833:
                case 12612281:
                case 12612291:
                case 12612381:
                case 12612739:
                case 12613889:
                case 12614088:
                case 12614676:
                case 12614682:
                case 12615493:
                case 12616478:
                case 12617175:
                case 12617569:
                case 12617631:
                case 12617980: // 2008 Impala Part No 12617174 Service No 12612384 OSID 12617980
                case 12618277:
                case 12619078:
                case 12622139:
                case 12622142:
                case 12622161:
                case 12624402:
                case 12628981:
                case 12628982:
                case 12628983:
                case 12628988:
                case 12628990:
                case 12630501:
                case 12633016:
                case 12633056:
                case 12635859:
                case 12635863:
                case 12636005:
                case 12636008:
                case 12637084:
                case 12639270:
                case 12639835:
                case 12644905:
                case 12647991:
                case 12649046:
                case 12653249:
                case 12653252:
                case 12653674:
                case 12654075:
                case 12656198:
                case 12656930:
                case 12658778:
                    PCMInfo(PcmType.E38);
                    break;

                default:
                    PCMInfo(PcmType.Undefined);
                    this.ServiceNumber = 0;
                    break;
            }
        }
    }
}
