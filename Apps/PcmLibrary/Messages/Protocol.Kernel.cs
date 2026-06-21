// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking
{
    /// <summary>
    /// Mode 3D was apparently not used for anything, so it's being taken
    /// for communications with the kernel.
    /// </summary>
    public partial class Protocol
    {
        /// <summary>
        /// Create a request for the kernel version.
        /// </summary>
        public Message CreateKernelVersionQuery()
        {
            return new Message(new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x3D, 0x00 });
        }

        internal Response<UInt64> ParseKernelVersion(Message responseMessage)
        {
            ResponseStatus status;
            byte[] expected = { Priority.Physical0, ToolId, TargetVpwId, 0x7D, 0x00 };
            if (!TryVerifyInitialBytes(responseMessage, expected, out status))
            {
                byte[] refused = { Priority.Physical0, ToolId, TargetVpwId, Mode.NegativeResponse, 0x3D, 0x00 };
                if (TryVerifyInitialBytes(responseMessage, refused, out status))
                    return Response.Create(ResponseStatus.Refused, (UInt64)0);
                return Response.Create(status, (UInt64)0);
            }
            byte[] responseBytes = responseMessage.GetBytes();
            if (responseBytes.Length < 9)
                return Response.Create(ResponseStatus.Truncated, (UInt64)0);
            UInt64 epoch =
                ((UInt64)responseBytes[5] << 24) |
                ((UInt64)responseBytes[6] << 16) |
                ((UInt64)responseBytes[7] <<  8) |
                responseBytes[8];
            byte pcmType = responseBytes.Length >= 10 ? responseBytes[9] : (byte)0x00;
            UInt64 value = (epoch << 8) | pcmType;
            return Response.Create(ResponseStatus.Success, value);
        }

        /// <summary>
        /// Create a request to get the operating system ID from the kernel.
        /// </summary>
        /// <remarks>
        /// It was tempting to just implement the same OS ID query as the PCM software,
        /// but then the app would need to do a kernel request of some type to determine 
        /// whether the PCM is running the GM OS or the kernel. However, in almost all 
        /// cases, the PCM will be running the GM OS, and that kernel request would slow
        /// down the most common usage scenario.
        ///
        /// So we keep the common scenario fast by having the standard OS ID request 
        /// succeed only when the standard operating system is running. When it fails,
        /// that puts the app into a slower path were it checks for a kernel and then 
        /// asks the kernel what OS is installed. Or it checks for recovery mode, loads
        /// the kernel, and then asks the kernel what OS is installed.
        /// </remarks>
        public Message CreateOperatingSystemIdKernelRequest()
        {
            return new Message(new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x3D, 0x03 });
        }

        /// <summary>
        /// Parse the operating system ID from a kernel response.
        /// </summary>
        public Response<UInt32> ParseOperatingSystemIdKernelResponse(Message message)
        {
            return ParseUInt32(message, 0x7D);
        }

        /// <summary>
        /// Create a request to identify the flash chip. 
        /// </summary>
        public Message CreateFlashMemoryTypeQuery()
        {
            return new Message(new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x3D, 0x01 });
        }

        internal Response<UInt32> ParseFlashMemoryType(Message responseMessage)
        {
            return ParseUInt32(responseMessage, 0x3D, 0x01);
        }

        /// <summary>
        /// Create a request to get the CRC of a byte range.
        /// </summary>
        public Message CreateCrcQuery(UInt32 address, UInt32 size)
        {
            byte[] requestBytes = new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x3D, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            requestBytes[5] = unchecked((byte)(size >> 16));
            requestBytes[6] = unchecked((byte)(size >> 8));
            requestBytes[7] = unchecked((byte)size);
            requestBytes[8] = unchecked((byte)(address >> 16));
            requestBytes[9] = unchecked((byte)(address >> 8));
            requestBytes[10] = unchecked((byte)address);
            return new Message(requestBytes);
        }

        /// <summary>
        /// Parse the response to a CRC query.
        /// </summary>
        internal Response<UInt32> ParseCrc(Message responseMessage, UInt32 address, UInt32 size)
        {
            ResponseStatus status;
            byte[] expected = new byte[]
            {
                Priority.Physical0,
                ToolId,
                TargetVpwId,
                0x7D,
                0x02,
                unchecked((byte)(size >> 16)),
                unchecked((byte)(size >> 8)),
                unchecked((byte)size),
                unchecked((byte)(address >> 16)),
                unchecked((byte)(address >> 8)),
                unchecked((byte)address),
            };

            if (!TryVerifyInitialBytes(responseMessage, expected, out status))
            {
                byte[] refused = {  Priority.Physical0, ToolId, TargetVpwId, Mode.NegativeResponse, 0x3D, 0x02 };
                if (TryVerifyInitialBytes(responseMessage, refused, out status))
                {
                    return Response.Create(ResponseStatus.Refused, (UInt32)0);
                }

                return Response.Create(status, (UInt32)0);
            }

            byte[] responseBytes = responseMessage.GetBytes();
            if (responseBytes.Length < 15)
            {
                return Response.Create(ResponseStatus.Truncated, (UInt32)0);
            }

            int crc =
                (responseBytes[11] << 24) |
                (responseBytes[12] << 16) |
                (responseBytes[13] << 8) |
                responseBytes[14];

            return Response.Create(ResponseStatus.Success, (UInt32)crc);
        }

        /// <summary>
        /// Ask the kernel to erase a block of flash memory.
        /// </summary>
        public Message CreateFlashEraseBlockRequest(UInt32 baseAddress)
        {
            return new Message(new byte[]
            {
                Priority.Physical0,
                TargetVpwId,
                ToolId,
                0x3D,
                0x05,
                (byte)(baseAddress >> 16),
                (byte)(baseAddress >> 8),
                (byte)baseAddress
            });
        }

        /// <summary>
        /// Find out whether an erase request was successful.
        /// </summary>
        internal Response<byte> ParseFlashEraseBlock(Message message)
        {
            return ParseByte(message, 0x3D, 0x05);
        }

        /// <summary>
        /// Create a request to ask the kernel whether the IAC (TPIC) driver chip is present.
        /// </summary>
        /// <remarks>
        /// Only the P01/P59 kernel implements this. The chip is probed live over the QSPI bus,
        /// so it reflects the actual hardware rather than anything stored in flash.
        /// </remarks>
        public Message CreateDetectIacQuery()
        {
            return new Message(new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x3D, 0x06 });
        }

        /// <summary>
        /// Parse the kernel's IAC driver chip detection response.
        /// </summary>
        /// <remarks>
        /// Returns the raw 16-bit QSPI receive word the kernel captured from the TPIC probe
        /// (high byte = responseBytes[6], low byte = responseBytes[7]). The present/absent
        /// decision is made by the caller so the criterion can be tuned without reflashing.
        /// </remarks>
        internal Response<ushort> ParseDetectIac(Message responseMessage)
        {
            ResponseStatus status;
            byte[] expected = { Priority.Physical0, ToolId, TargetVpwId, 0x7D, 0x06 };
            if (!TryVerifyInitialBytes(responseMessage, expected, out status))
            {
                byte[] refused = { Priority.Physical0, ToolId, TargetVpwId, Mode.NegativeResponse, 0x3D, 0x06 };
                if (TryVerifyInitialBytes(responseMessage, refused, out status))
                {
                    return Response.Create(ResponseStatus.Refused, (ushort)0);
                }

                return Response.Create(status, (ushort)0);
            }

            byte[] responseBytes = responseMessage.GetBytes();
            if (responseBytes.Length < 8)
            {
                return Response.Create(ResponseStatus.Truncated, (ushort)0);
            }

            ushort raw = (ushort)((responseBytes[6] << 8) | responseBytes[7]);
            return Response.Create(ResponseStatus.Success, raw);
        }

        /// <summary>
        /// Create a request for implementation details... for development use only.
        /// </summary>
        public Message CreateDebugQuery()
        {
            return new Message(new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x3D, 0xFF });
        }

        /// <summary>
        /// Create a message to tell the RAM-resident kernel to exit.
        /// </summary>
        public Message CreateExitKernel()
        {
            byte[] bytes = new byte[] { Priority.Physical0, TargetVpwId, ToolId, 0x20 };
            return new Message(bytes);
        }
    }
}
