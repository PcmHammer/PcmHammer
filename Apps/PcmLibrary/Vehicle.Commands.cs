// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// The VPW side of <see cref="IPcmCommands"/>. Most members forward to the operations Vehicle
    /// already exposes; they are implemented explicitly so the existing signatures, which callers all
    /// over the app depend on, stay exactly as they are.
    /// </summary>
    public partial class Vehicle : IPcmCommands
    {
        /// <summary>
        /// Read one 0x3C block. The VIN and serial reads in Vehicle.Properties are three blocks
        /// stitched into a string; this is the single-block read, which is what identifying a module
        /// or sweeping the block ids needs. Named for the CAN equivalent rather than for VPW's "read
        /// block", which would be too easily confused with a memory read.
        /// </summary>
        public async Task<Response<byte[]>> ReadDataByIdentifier(byte did, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            Query<byte[]> query = this.CreateQuery(
                () => this.protocol.CreateReadRequest(did),
                message => ParseBlockResponse(message, did),
                cancellationToken);

            return await query.Execute();
        }

        /// <summary>
        /// Keep the answer and the refusal, both as Success, and drop anything that is neither so the
        /// query keeps listening past unrelated bus traffic.
        /// </summary>
        private static Response<byte[]> ParseBlockResponse(Message message, byte blockId)
        {
            byte[] bytes = message?.GetBytes() ?? Array.Empty<byte>();
            if (bytes.Length < 5)
            {
                return Response.Create(ResponseStatus.Truncated, Array.Empty<byte>());
            }

            bool answered = bytes[3] == Mode.ReadBlock + Mode.Response && bytes[4] == blockId;
            bool refused = bytes[3] == Mode.NegativeResponse && bytes[4] == Mode.ReadBlock;
            if (!answered && !refused)
            {
                return Response.Create(ResponseStatus.UnexpectedResponse, Array.Empty<byte>());
            }

            // Drop the priority/target/source header so the payload starts at the mode byte, matching
            // what the CAN implementation returns.
            byte[] payload = new byte[bytes.Length - 3];
            Buffer.BlockCopy(bytes, 3, payload, 0, payload.Length);
            return Response.Create(ResponseStatus.Success, payload);
        }

        Task<Response<uint>> IPcmCommands.GetFlashId(CancellationToken cancellationToken)
            => this.QueryFlashChipId(cancellationToken);

        async Task<Response<ulong>> IPcmCommands.GetKernelVersion(CancellationToken cancellationToken)
        {
            // The VPW query reports "nobody answered" as version zero; the interface reports it as an
            // error, as the CAN implementation does.
            UInt64 version = await this.GetKernelVersion(cancellationToken);
            return version == 0
                ? Response.Create(ResponseStatus.Error, version)
                : Response.Create(ResponseStatus.Success, version);
        }

        async Task<bool> IPcmCommands.Reboot(CancellationToken cancellationToken, bool announce)
        {
            if (announce)
            {
                this.logger.AddUserMessage("Returning to normal mode.");
            }

            await this.ExitKernel();
            return true;
        }

        Task IPcmCommands.ClearDiagnosticCodes(CancellationToken cancellationToken, bool announce)
            => this.ClearTroubleCodes(announce);

        async Task IPcmCommands.Cleanup(CancellationToken cancellationToken)
        {
            try
            {
                await this.Cleanup();
            }
            catch (Exception exception)
            {
                this.logger.AddDebugMessage("Cleanup failed: " + exception.Message);
            }
        }
    }
}
