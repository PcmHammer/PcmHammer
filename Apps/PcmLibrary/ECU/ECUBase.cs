using PcmHacking.ECU.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PcmHacking.ECU {
    public enum PcmType
    {
        Undefined = 0, // required for failed osid test on binary file
        Unsupported, // required for failed osid test on binary file
        P01,
        P59,
        P04_Early,
        P04_Early_512k,
        P04,
        P05,
        P08,
        P10,
        P11,
        P12,
        P12_2M,
        E54, //E54 (01-04 LB7 Duramax) 
        E60, //E60 (04-05 LLY Duramax)
        BlackBox
    }

    public class PreFlightCheckResult
    {
        public string? PromptMessage;
        public bool ShouldPrompt;
        public bool CanProceed;
    }

    public abstract class ECUBase {
        private ECUStates _ecuState;

        public ECUStates ECUState
        {
            get
            {
                return _ecuState;
            }
            set
            {
                _ecuState = value;
            }
        }
        public List<OSInfo> KnownOperatingSystems { get; set; }

        private OSInfo currentOS { get; set; }

        public bool HardwareTypeOverridden { get; set; }

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
        /// 

        public uint ServiceNumber
        {
            get
            {
                return (uint)currentOS.ServiceNumber;
            }
        }

        /// <summary>
        /// Does it have a slave CPU?
        /// </summary>
        public bool HardwareSlaveCPU { get; set; }

        /// <summary>
        /// Name of the kernel file to use.
        /// </summary>
        public string KernelFileName
        {
            get
            {
                if(BaseHardwareType == PcmType.E60 || HardwareType <= PcmType.Unsupported)
                {
                    return "";
                }
                return $"Kernel-{BaseHardwareType}.bin";
            }

        }

        /// <summary>
        /// Base address to begin writing the kernel to.
        /// </summary>
        public int KernelBaseAddress { get; set; }

        /// <summary>
        /// Name of the kernel loader file to use.
        /// </summary>
        public string LoaderFileName
        {
            get
            {
                if (LoaderRequired)
                {
                    if (HardwareType <= PcmType.Unsupported)
                    {
                        return "";
                    }
                    if (HardwareType == PcmType.P04_Early || HardwareType == PcmType.P04_Early_512k) 
                    {
                        return $"Loader-{PcmType.P04}.bin";
                    }
                    return $"Loader-{BaseHardwareType}.bin";
                }
                return "";
            }
        }

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

        private int _keyAlgorithm = 0;
 
        public int KeyAlgorithm { 
            get
            {
                return currentOS?.KeyAlgorithm ?? _keyAlgorithm;
            }
            set
            {
                _keyAlgorithm = value;
            }
        }

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

        public PcmType BaseHardwareType { get; set; }

        public bool IsCustomOS
        {
            get
            {
                if (currentOS == null) return false;
                return currentOS.Manufacturer != Manufacturer;
            }
        }


        public ECUBase()
        {
            KnownOperatingSystems = [];
            currentOS = new();
            Initialize();
        }

        public ECUBase(int osid)
        {
            KnownOperatingSystems = [];
            currentOS = new();
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
            HardwareSlaveCPU = false;
            KernelBaseAddress = 0x0;
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
            currentOS = KnownOperatingSystems.FirstOrDefault(x => x.OSID == osid);
            if (currentOS == null)
            {
                currentOS = new OSInfo("Undefined ECU", osid, 0, KeyAlgorithm);
            }
        }

        public PreFlightCheckResult GetPreCheckResults(ControllerActions selectedAction, WriteType writeType = WriteType.None)
        {
            PreFlightCheckResult result = new();
            result.CanProceed = true;
            result.ShouldPrompt = false;
            StringBuilder builder = new();
            builder.AppendLine();
            if((currentOS == null || currentOS.ServiceNumber == -1) && !(currentOS?.IdOverridePresent ?? false))
            {
                result.CanProceed = false;
                builder.AppendLine("An unsupported OSID was detected.\r\n");
            }
            while (true)
            {
                if (ECUState == ECUStates.Recovery)
                {
                    builder.AppendLine("This controller is in Recovery mode!");
                    if(selectedAction == ControllerActions.Write)
                    {
                        builder.AppendLine("PCM Hammer will attempt to recover the controller\r\n" +
                        "with the supplied file. If this file is not a valid\r\n" +
                        "match to this hardware type, the unit may brick!\r\n");
                    }
                    else
                    {
                        builder.AppendLine("Reading from a controller in recovery mode\r\n" +
                            "is currently an unsupported operation. Abort!\r\n");
                        result.CanProceed = false;
                    }
                    break;
                }
                if (HardwareType == PcmType.Undefined)
                {
                    result.CanProceed = false;
                    builder.AppendLine(
                        "Unable to determine PCM hardware type.\r\n" +
                        "If you know the hardware type, please specify it\r\n" +
                        "manually with the -hw flag and try again!");
                    break;
                }
                if (!IsSupported)
                {
                    result.CanProceed = false;
                    builder.AppendLine("An unsupported controller was detected.\r\n");
                    break;
                }
                if (!IsSupportedRead && selectedAction == ControllerActions.Read)
                {
                    builder.AppendLine("This controller currently does not support reading.\r\n");
                    result.CanProceed = false;
                }
                if (IsUnderDevelopment)
                {
                    builder.AppendLine($"WARNING: {HardwareType.ToString()} Support is still in development.\r\nThere is additional brick risk in this operation\r\n");
                }
                if (selectedAction == ControllerActions.Write)
                {
                    if (!IsSupportedWrite)
                    {
                        builder.AppendLine("This controller currently does not support writing.\r\n");
                        result.CanProceed = false;
                    }
                    if (!IsSupportedWriteBootSector && writeType >= WriteType.OsPlusCalibrationPlusBoot)
                    {
                        builder.AppendLine(
                            "This controller currently does not support writing\r\n" +
                            " to boot sector. Calibration write only!\r\n");
                        result.CanProceed = false;
                    }
                    if (HardwareSlaveCPU && !IsSupportedWriteSlaveCPU && writeType >= WriteType.OsPlusCalibrationPlusBoot)
                    {
                        builder.AppendLine("This controller currently does not support slave CPU writing.\r\n" +
                            "Flashing an incompatible OS can leave ETC inoperable!\r\n" +
                            "Before you proceed, a backup is highly recommended!\r\n" +
                            "Flashing the original OS will likely restore functionality.\r\n");
                    }
                    if (!IsSupportedWriteBySegment && writeType < WriteType.Full)
                    {
                        builder.AppendLine("This controller does not support section writes. Full flash only!\r\n");
                        result.CanProceed = false;
                    }
                }
                break;
            }
            if (!string.IsNullOrWhiteSpace(builder.ToString()))
            {
                builder.AppendLine();
                builder.AppendLine("**********************\r\n");
                builder.AppendLine(result.CanProceed ? "Considering the message(s) above, do you wish to proceed?" : "Due to the above conditions, the requested operation cannot be performed!");
                builder.Insert(0, "\r\n**********************\r\n");
                result.PromptMessage = builder.ToString();
                result.ShouldPrompt = true;
            }
            return result;
        }

        public uint GetCurrentOSID() => currentOS.OSID;

        public void SetOverriddenState() => currentOS.SetOverridePresent();

        public abstract ECUBase Clone();

        public override string ToString()
        {
            string suffix = IsCustomOS ? "COS" : "OEM";
            string servNo = ServiceNumber != 0 ? $"{ServiceNumber}." : string.Empty;
            string servString = $"{servNo}{suffix}";
            if (currentOS == null || currentOS.ServiceNumber == -1)
            {
                return "Undefined ECU";
            }
            string format = "{0}_{1} - {2} {3}K";
            return string.Format(format, Manufacturer, HardwareType, servString, ImageSize / 1024);
        }
    }
}
