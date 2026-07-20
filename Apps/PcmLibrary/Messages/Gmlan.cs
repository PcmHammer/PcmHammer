// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// Builds and parses GMLAN diagnostic messages as naked payloads over ISO-TP; the device adds the
    /// CAN framing. Build/parse only - all I/O goes through Query<T> in the command layer.
    ///
    /// GM CAN PCMs speak ISO-TP over CAN but deviate from ISO 14229 in places:
    ///   - Service 0x1A (ReadDataByIdentifier) instead of 0x22; positive response 0x5A.
    ///   - 0xA5 ProgrammingMode service before RequestDownload (positive response 0xE5).
    ///   - RequestDownload (0x34) carries a 3-byte size, not 4.
    ///   - Custom TransferData (0x36 0x00/0x80) with explicit addresses, not the standard block-counter
    ///     form.
    ///   - Kernel memory read uses 0x35 (tool->PCM, acked 0x75) / 0x36 (PCM->tool data).
    ///   - Kernel queries reuse OBD2 mode 0x3D (response 0x7D).
    /// </summary>
    public class Gmlan
    {
        // Service / response identifiers used when parsing.
        public const byte ReadDataByIdentifier = 0x1A;
        public const byte ReadDataByIdentifierResponse = 0x5A;
        public const byte WriteDataByIdentifier = 0x3B;
        public const byte WriteDataByIdentifierResponse = 0x7B;
        public const byte VinDataIdentifier = 0x90;
        public const byte SecurityAccessResponse = 0x67;
        public const byte ProgrammingModeResponse = 0xE5;
        public const byte RequestDownloadResponse = 0x74;
        public const byte ExecChunkAck = 0x99;        // kernel running
        public const byte NonExecChunkAck = 0x76;
        public const byte MemoryReadAck = 0x75;       // ack to 0x35 before the 0x36 data block
        public const byte MemoryBlockResponse = 0x36;
        public const byte Mode3DResponse = 0x7D;

        /// <summary>Negative response service id; full form is [0x7F, requestSid, nrc].</summary>
        public const byte NegativeResponse = 0x7F;

        /// <summary>NRC 0x37: required time delay not expired (security access still locked out).</summary>
        public const byte NrcSecurityDelay = 0x37;

        /// <summary>DID 0xC9: operating system id (OSID), returned as a big-endian uint32.</summary>
        public const byte OperatingSystemDid = 0xC9;

        /// <summary>
        /// DIDs probed for the OSID during CAN detection, in order; the first that answers with a
        /// usable value (see <see cref="IsUsableOsid"/>) is taken as the OSID. 0xC1 (Module 1) holds
        /// the OS segment part number and is the reliable source across families, including P05c,
        /// which does not answer 0xC9 at all. 0xC9 is the fallback for modules that leave 0xC1 empty.
        /// </summary>
        public static readonly byte[] OperatingSystemDids = { 0xC1, OperatingSystemDid };

        /// <summary>
        /// True when a DID's value can serve as the OSID. An unpopulated slot reads back as all
        /// zeroes or all ones, which must not be taken as an OS id or the next candidate is skipped.
        /// </summary>
        public static bool IsUsableOsid(uint value) => value != 0x00000000 && value != 0xFFFFFFFF;

        /// <summary>Fixed kernel memory-read block size (bytes the kernel returns per 0x35 request).</summary>
        public const int KernelBlockSize = 0x400;

        // ---- Session / communication control ------------------------------------------------------

        // Single-byte 0x28 (no sub-function). GM CAN PCMs answer 0x68/0x60; a 0x28 0x01 form is
        // rejected with NRC 0x12 (subFunctionNotSupported).
        public Message CreateDisableNormalCommunicationRequest() => new Message(new byte[] { 0x28 });

        public Message CreateExtendedSessionRequest() => new Message(new byte[] { 0x10, 0x03 });

        public Message CreateProgrammingSessionRequest() => new Message(new byte[] { 0x10, 0x02 });

        public Message CreateTesterPresentRequest() => new Message(new byte[] { 0x3E });

        // ECUReset (hard reset): return the PCM to its stock OS from the kernel.
        public Message CreateEcuResetRequest() => new Message(new byte[] { 0x11, 0x01 });

        // ReturnToNormalMode (0x20): exit programming/kernel mode. Positive response is 0x60.
        public Message CreateReturnToNormalRequest() => new Message(new byte[] { 0x20 });

        // OBD-II ClearDiagnosticInformation (mode 0x04): clears DTCs set during the kernel session.
        public Message CreateClearDiagnosticsRequest() => new Message(new byte[] { 0x04 });

        // ---- Security access (0x27) ---------------------------------------------------------------

        public Message CreateSeedRequest() => new Message(new byte[] { 0x27, 0x01 });

        public Message CreateUnlockRequest(ushort key)
            => new Message(new byte[] { 0x27, 0x02, (byte)(key >> 8), (byte)key });

        public Response<ushort> ParseSeed(Message message)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length >= 4 && bytes[0] == SecurityAccessResponse && bytes[1] == 0x01)
                return Response.Create(ResponseStatus.Success, (ushort)((bytes[2] << 8) | bytes[3]));
            // Negative response is a hard failure; anything else keeps reading (stray frame).
            if (bytes.Length >= 1 && bytes[0] == NegativeResponse)
                return Response.Create(ResponseStatus.Error, (ushort)0);
            return Response.Create(ResponseStatus.UnexpectedResponse, (ushort)0);
        }

        public Response<bool> ParseUnlockResponse(Message message)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length < 2) return Response.Create(ResponseStatus.Error, false);
            if (bytes[0] == SecurityAccessResponse && bytes[1] == 0x02)
                return Response.Create(ResponseStatus.Success, true);
            if (bytes[0] == NegativeResponse)
                return Response.Create(ResponseStatus.Error, false);
            return Response.Create(ResponseStatus.Refused, false);
        }

        public bool IsSecurityDelayActive(Message message)
        {
            byte[] bytes = GetBytes(message);
            return bytes.Length >= 3 && bytes[0] == NegativeResponse && bytes[2] == NrcSecurityDelay;
        }

        // ---- Programming mode (0xA5) --------------------------------------------------------------

        public Message CreateProgrammingModeRequest() => new Message(new byte[] { 0xA5, 0x01 });

        public Message CreateProgrammingModeStep3Request() => new Message(new byte[] { 0xA5, 0x03 });

        public Response<bool> ParseProgrammingModeResponse(Message message)
        {
            // The PCM answers 0xA5/01 and 0xA5/03 with a single 0xE5 byte (no sub-function echo),
            // so match on the response SID alone rather than requiring 0xE5 0x01.
            byte[] bytes = GetBytes(message);
            if (bytes.Length < 1) return Response.Create(ResponseStatus.Error, false);
            if (bytes[0] == ProgrammingModeResponse) return Response.Create(ResponseStatus.Success, true);
            if (bytes[0] == NegativeResponse) return Response.Create(ResponseStatus.Error, false);
            return Response.Create(ResponseStatus.Refused, false);
        }

        // ---- RequestDownload (0x34) ---------------------------------------------------------------

        /// <summary>
        /// GM CAN PCM RequestDownload: 0x34 0x00 [size, big-endian]. The size width is the boot loader's
        /// dialect - 3 bytes on E38, 2 bytes on P05c - so it is supplied by the caller.
        /// </summary>
        public Message CreateRequestDownloadRequest(int totalBytes, int sizeBytes = 3)
        {
            byte[] msg = new byte[2 + sizeBytes];
            msg[0] = 0x34;
            msg[1] = 0x00;
            for (int i = 0; i < sizeBytes; i++)
            {
                msg[2 + i] = (byte)((totalBytes >> (8 * (sizeBytes - 1 - i))) & 0xFF);
            }
            return new Message(msg);
        }

        public Response<bool> ParseRequestDownloadResponse(Message message)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length < 1) return Response.Create(ResponseStatus.Error, false);
            if (bytes[0] == RequestDownloadResponse) return Response.Create(ResponseStatus.Success, true);
            return Response.Create(ResponseStatus.Refused, false);
        }

        // ---- Transfer chunks (0x36) ---------------------------------------------------------------

        /// <summary>Non-executing upload chunk: 0x36 0x00 [load_addr 4 bytes] [code bytes].</summary>
        public Message CreateNonExecChunkMessage(byte[] code, uint loadAddr)
        {
            byte[] msg = new byte[6 + code.Length];
            msg[0] = 0x36;
            msg[1] = 0x00;
            msg[2] = (byte)(loadAddr >> 24);
            msg[3] = (byte)(loadAddr >> 16);
            msg[4] = (byte)(loadAddr >> 8);
            msg[5] = (byte)loadAddr;
            Buffer.BlockCopy(code, 0, msg, 6, code.Length);
            return new Message(msg);
        }

        /// <summary>
        /// Executing upload chunk: 0x36 0x80 [load_addr 4 bytes] [run_addr 4 bytes] [code bytes].
        /// Expected ack is 0x99 (kernel running).
        /// </summary>
        public Message CreateExecChunkMessage(byte[] code, uint loadAddr, uint runAddr)
        {
            byte[] msg = new byte[10 + code.Length];
            msg[0] = 0x36;
            msg[1] = 0x80;
            msg[2] = (byte)(loadAddr >> 24);
            msg[3] = (byte)(loadAddr >> 16);
            msg[4] = (byte)(loadAddr >> 8);
            msg[5] = (byte)loadAddr;
            msg[6] = (byte)(runAddr >> 24);
            msg[7] = (byte)(runAddr >> 16);
            msg[8] = (byte)(runAddr >> 8);
            msg[9] = (byte)runAddr;
            Buffer.BlockCopy(code, 0, msg, 10, code.Length);
            return new Message(msg);
        }

        /// <summary>
        /// Bare execute frame: 0x36 0x80 [address 4 bytes], with no run-address and no code. The boot
        /// loader jumps to the address once the whole image is already in RAM (P05c-style). Acked 0x76.
        /// </summary>
        public Message CreateExecuteMessage(uint address)
            => new Message(new byte[]
            {
                0x36, 0x80,
                (byte)(address >> 24),
                (byte)(address >> 16),
                (byte)(address >> 8),
                (byte)address,
            });

        public bool IsChunkAck(Message message, bool isExec)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length == 0) return false;
            return isExec ? bytes[0] == ExecChunkAck : bytes[0] == NonExecChunkAck;
        }

        // ---- Kernel memory-read protocol (0x35 / 0x36) --------------------------------------------

        /// <summary>
        /// Ask the running kernel to read <paramref name="length"/> bytes starting at <paramref name="address"/>.
        /// Format: 0x35 [len_hi] [len_mid] [len_lo] [addr_hi] [addr_mid] [addr_lo].
        /// </summary>
        public Message CreateMemoryReadRequest(uint address, int length = KernelBlockSize)
            => new Message(new byte[]
            {
                0x35,
                (byte)(length >> 16),
                (byte)(length >> 8),
                (byte)length,
                (byte)(address >> 16),
                (byte)(address >> 8),
                (byte)address,
            });

        public bool IsMemoryReadAck(Message message)
        {
            byte[] bytes = GetBytes(message);
            return bytes.Length >= 1 && bytes[0] == MemoryReadAck;
        }

        /// <summary>
        /// Parse a 0x36 kernel memory block response: 0x36 0x00 [addr 3 bytes] [0x400 bytes data].
        /// </summary>
        public Response<byte[]> ParseMemoryBlock(Message message, int length = KernelBlockSize)
        {
            const int headerLen = 5; // 36 00 addr[3]
            byte[] bytes = GetBytes(message);
            if (bytes.Length < headerLen + length)
                return Response.Create(ResponseStatus.Error, Array.Empty<byte>());
            if (bytes[0] != MemoryBlockResponse || bytes[1] != 0x00)
                return Response.Create(ResponseStatus.Refused, Array.Empty<byte>());

            byte[] data = new byte[length];
            Buffer.BlockCopy(bytes, headerLen, data, 0, length);
            return Response.Create(ResponseStatus.Success, data);
        }

        // ---- Kernel queries (OBD2 mode 0x3D, response 0x7D) ---------------------------------------
        // Sub-functions: 3D00 version, 3D01 flash chip id, 3D02 range CRC32, 3D04 CAN error/status.
        // Request [0x3D, sub(, operands)]; response [0x7D, sub, b3, b2, b1, b0].

        public Message CreateKernelVersionRequest() => new Message(new byte[] { 0x3D, 0x00 });

        public Response<uint> ParseKernelVersionResponse(Message message) => ParseMode3DUInt32(message, 0x00);

        public Message CreateFlashIdRequest() => new Message(new byte[] { 0x3D, 0x01 });

        public Response<uint> ParseFlashIdResponse(Message message) => ParseMode3DUInt32(message, 0x01);

        public Message CreateCanStatusRequest() => new Message(new byte[] { 0x3D, 0x04 });

        public Response<uint> ParseCanStatusResponse(Message message) => ParseMode3DUInt32(message, 0x04);

        /// <summary>
        /// CRC-32 of a memory range: 0x3D 0x02 [size 3 bytes] [addr 3 bytes]; reply 0x7D 0x02 [crc 4
        /// bytes, big-endian]. Note size precedes address.
        /// </summary>
        public Message CreateCrcRequest(uint address, uint length)
            => new Message(new byte[]
            {
                0x3D, 0x02,
                (byte)(length >> 16), (byte)(length >> 8), (byte)length,
                (byte)(address >> 16), (byte)(address >> 8), (byte)address,
            });

        public Response<uint> ParseCrcResponse(Message message) => ParseMode3DUInt32(message, 0x02);

        /// <summary>
        /// Compute the same CRC-32 the kernel uses (polynomial 0x04C11DB7, initial 0, MSB-first, no
        /// reflection) over a host-side buffer, for verifying a read against the on-device CRC.
        /// </summary>
        public static uint ComputeCrc32(byte[] data, int offset, int length)
        {
            const uint polynomial = 0x04C11DB7;
            uint remainder = 0;
            for (int i = 0; i < length; i++)
            {
                remainder ^= (uint)data[offset + i] << 24;
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((remainder & 0x80000000) != 0)
                        remainder = (remainder << 1) ^ polynomial;
                    else
                        remainder <<= 1;
                }
            }
            return remainder;
        }

        private static Response<uint> ParseMode3DUInt32(Message message, byte subFunction)
        {
            byte[] bytes = GetBytes(message);

            // Negative response aborts. Response-pending (7F..78) keepalives are handled generically
            // in Device.ReceiveMessage and never reach here.
            if (bytes.Length >= 1 && bytes[0] == NegativeResponse)
            {
                return Response.Create(ResponseStatus.Error, 0u);
            }

            // Any other short or non-matching frame (echo, stray ack) keeps reading rather than aborting.
            if (bytes.Length < 6 || bytes[0] != Mode3DResponse || bytes[1] != subFunction)
            {
                return Response.Create(ResponseStatus.Refused, 0u);
            }

            uint value = (uint)((bytes[2] << 24) | (bytes[3] << 16) | (bytes[4] << 8) | bytes[5]);
            return Response.Create(ResponseStatus.Success, value);
        }

        // ---- Flash erase / write (kernel) ---------------------------------------------------------
        //   - Erase one sector: 0x3D 0x05 [addr 3 bytes] -> 0x7D 0x05 [status] (0x00 = success).
        //   - Write a block: 0x36 [copyType] [len 2 bytes] [addr 3 bytes] [data...] [sum 2 bytes].
        //     copyType 0x00 = copy/write, 0x44 = test write (validate the transfer, no erase/program).
        //     The kernel checks the trailing 16-bit data sum and replies 0x76 ok / 0x7F 0x36 nrc.

        /// <summary>Erase the flash sector that contains <paramref name="address"/> (0x3D 0x05).</summary>
        public Message CreateFlashEraseRequest(uint address)
            => new Message(new byte[]
            {
                0x3D, 0x05,
                (byte)(address >> 16), (byte)(address >> 8), (byte)address,
            });

        /// <summary>Parse the erase response 0x7D 0x05 [status]; Success carries the status byte (0x00 = ok).</summary>
        public Response<byte> ParseFlashEraseResponse(Message message)
        {
            byte[] bytes = GetBytes(message);
            // The kernel's erase keepalive (7F 36 78, a response-pending against its flash primitive's
            // 0x36 service) is handled generically in Device.ReceiveMessage and never reaches here, so
            // any 7F seen here is a real erase failure.
            if (bytes.Length < 3 || bytes[0] != Mode3DResponse || bytes[1] != 0x05)
            {
                if (bytes.Length >= 1 && bytes[0] == NegativeResponse) return Response.Create(ResponseStatus.Error, (byte)0xFF);
                return Response.Create(ResponseStatus.Refused, (byte)0xFF);
            }
            return Response.Create(ResponseStatus.Success, bytes[2]);
        }

        /// <summary>Bytes before a write block's data: 0x36 + copyType + len(2) + addr(3) = 7.</summary>
        public const int WriteBlockHeaderLength = 7;

        /// <summary>
        /// Build a flash write block: 0x36 [copyType] [len hi,lo] [addr hi,mid,lo] [data...] [sum hi,lo].
        /// The trailing 16-bit sum is the unsigned sum of the data bytes (the test-write path validates
        /// it without touching flash).
        /// </summary>
        public Message CreateWriteBlockMessage(byte[] image, int offset, int length, uint address, BlockCopyType copyType)
        {
            byte[] msg = new byte[WriteBlockHeaderLength + length + 2];
            msg[0] = MemoryBlockResponse;          // 0x36
            msg[1] = (byte)copyType;               // 0x00 write, 0x44 test
            msg[2] = (byte)(length >> 8);
            msg[3] = (byte)length;
            msg[4] = (byte)(address >> 16);
            msg[5] = (byte)(address >> 8);
            msg[6] = (byte)address;
            Buffer.BlockCopy(image, offset, msg, WriteBlockHeaderLength, length);
            ushort sum = 0;
            for (int i = 0; i < length; i++) sum += image[offset + i];
            msg[WriteBlockHeaderLength + length] = (byte)(sum >> 8);
            msg[WriteBlockHeaderLength + length + 1] = (byte)sum;
            return new Message(msg);
        }

        /// <summary>Parse a write-block response: 0x76 = success, 0x7F = failure. (Response-pending
        /// 7F..78 keepalives are handled generically in Device.ReceiveMessage.)</summary>
        public Response<bool> ParseWriteBlockResponse(Message message)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length >= 1 && bytes[0] == NonExecChunkAck) // 0x76
                return Response.Create(ResponseStatus.Success, true);
            if (bytes.Length >= 1 && bytes[0] == NegativeResponse)
                return Response.Create(ResponseStatus.Error, false);
            return Response.Create(ResponseStatus.Refused, false);
        }

        // ---- ReadDataByIdentifier (service 0x1A, not standard 0x22) -------------------------------

        /// <summary>Build a ReadDataByIdentifier request for one DID: [0x1A, did].</summary>
        public Message CreateReadByIdRequest(byte did) => new Message(new byte[] { ReadDataByIdentifier, did });

        /// <summary>
        /// Parse a ReadDataByIdentifier response [0x5A, did, data...] and return the data bytes.
        /// Error on a negative response (0x7F), UnexpectedResponse if the sid/did don't match.
        /// </summary>
        public Response<byte[]> ParseReadByIdResponse(Message message, byte did)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length < 2)
            {
                return Response.Create(ResponseStatus.Truncated, Array.Empty<byte>());
            }
            if (bytes[0] == NegativeResponse)
            {
                return Response.Create(ResponseStatus.Error, Array.Empty<byte>());
            }
            if (bytes[0] != ReadDataByIdentifierResponse || bytes[1] != did)
            {
                return Response.Create(ResponseStatus.UnexpectedResponse, Array.Empty<byte>());
            }

            byte[] data = new byte[bytes.Length - 2];
            Array.Copy(bytes, 2, data, 0, data.Length);
            return Response.Create(ResponseStatus.Success, data);
        }

        // ---- WriteDataByIdentifier (service 0x3B) -------------------------------------------------

        /// <summary>Build a WriteDataByIdentifier request: [0x3B, did, data...] (e.g. 3B 90 + VIN).</summary>
        public Message CreateWriteByIdRequest(byte did, byte[] data)
        {
            byte[] msg = new byte[2 + data.Length];
            msg[0] = WriteDataByIdentifier;
            msg[1] = did;
            Buffer.BlockCopy(data, 0, msg, 2, data.Length);
            return new Message(msg);
        }

        /// <summary>
        /// Parse a WriteDataByIdentifier response: positive is [0x7B, did]. Error on a negative
        /// response (0x7F), UnexpectedResponse otherwise.
        /// </summary>
        public Response<bool> ParseWriteByIdResponse(Message message, byte did)
        {
            byte[] bytes = GetBytes(message);
            if (bytes.Length >= 2 && bytes[0] == WriteDataByIdentifierResponse && bytes[1] == did)
            {
                return Response.Create(ResponseStatus.Success, true);
            }
            if (bytes.Length >= 1 && bytes[0] == NegativeResponse)
            {
                return Response.Create(ResponseStatus.Error, false);
            }
            return Response.Create(ResponseStatus.UnexpectedResponse, false);
        }

        private static byte[] GetBytes(Message message) => message?.GetBytes() ?? Array.Empty<byte>();
    }
}
