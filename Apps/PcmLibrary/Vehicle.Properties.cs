// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>
    /// From the application's perspective, this class is the API to the vehicle.
    /// </summary>
    /// <remarks>
    /// Methods in this class are high-level operations like "get the VIN," or "read the contents of the EEPROM."
    /// </remarks>
    public partial class Vehicle : IDisposable
    {
        /// <summary>
        /// Query the PCM's VIN.
        /// </summary>
        public async Task<Response<string>> QueryVin()
        {
            return await this.ReadBlockSequence(
                new Func<Message>[]
                {
                    this.protocol.CreateVinRequest1,
                    this.protocol.CreateVinRequest2,
                    this.protocol.CreateVinRequest3,
                },
                r => this.protocol.ParseVinResponses(r[0].GetBytes(), r[1].GetBytes(), r[2].GetBytes()));
        }

        /// <summary>
        /// Query the PCM's Serial Number.
        /// </summary>
        public async Task<Response<string>> QuerySerial()
        {
            return await this.ReadBlockSequence(
                new Func<Message>[]
                {
                    this.protocol.CreateSerialRequest1,
                    this.protocol.CreateSerialRequest2,
                    this.protocol.CreateSerialRequest3,
                },
                r => this.protocol.ParseSerialResponses(r[0], r[1], r[2]));
        }

        /// <summary>
        /// Query the PCM's Broad Cast Code.
        /// </summary>
        public async Task<Response<string>> QueryBCC()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.protocol.CreateBCCRequest,
                this.protocol.ParseBCCresponse, 
                CancellationToken.None);

            return await query.Execute();
        }

        /// <summary>
        /// Query the PCM's Manufacturer Enable Counter (MEC)
        /// </summary>
        public async Task<Response<string>> QueryMEC()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.protocol.CreateMECRequest,
                this.protocol.ParseMECresponse,
                CancellationToken.None);

            return await query.Execute();
        }

        /// <summary>
        /// Query the PCM's voltage PID.
        /// </summary>
        public async Task<Response<string>> QueryVoltage()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                ()=>this.protocol.CreatePidRequest(0x1141),
                this.protocol.ParsePidResponse,
                CancellationToken.None);

            Response<int> intResponse = await query.Execute();
            double voltage = intResponse.Value / 10.0;
            return new Response<string>(intResponse.Status, voltage.ToString());
        }

        /// <summary>
        /// Update the PCM's VIN
        /// </summary>
        /// <remarks>
        /// Requires that the PCM is already unlocked
        /// </remarks>
        public async Task<Response<bool>> UpdateVin(string vin)
        {
            this.device.ClearMessageQueue();

            if (vin.Length != 17) // should never happen, but....
            {
                logger.AddUserMessage("VIN " + vin + " is not 17 characters long!");
                return Response.Create(ResponseStatus.Error, false);
            }

            logger.AddUserMessage("Changing VIN to " + vin);

            byte[] bvin = Encoding.ASCII.GetBytes(vin);
            byte[] vin1 = new byte[6] { 0x00, bvin[0], bvin[1], bvin[2], bvin[3], bvin[4] };
            byte[] vin2 = new byte[6] { bvin[5], bvin[6], bvin[7], bvin[8], bvin[9], bvin[10] };
            byte[] vin3 = new byte[6] { bvin[11], bvin[12], bvin[13], bvin[14], bvin[15], bvin[16] };

            logger.AddUserMessage("Block 1");
            Response<bool> block1 = await WriteBlock(BlockId.Vin1, vin1);
            if (block1.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);
            logger.AddUserMessage("Block 2");
            Response<bool> block2 = await WriteBlock(BlockId.Vin2, vin2);
            if (block2.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);
            logger.AddUserMessage("Block 3");
            Response<bool> block3 = await WriteBlock(BlockId.Vin3, vin3);
            if (block3.Status != ResponseStatus.Success) return Response.Create(ResponseStatus.Error, false);

            return Response.Create(ResponseStatus.Success, true);
        }

        /// <summary>
        /// Query the PCM's operating system ID.
        /// </summary>
        /// <returns></returns>
        public async Task<Response<UInt32>> QueryOperatingSystemId(CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            Response<UInt32> response = await this.QueryUnsignedValue(this.protocol.CreateOperatingSystemIdReadRequest, cancellationToken);
            if (response.Status != ResponseStatus.Success || response.Value != 0xFFFFFFFF)
            {
                return response;
            }

            logger.AddDebugMessage("OSID query returned 0xFFFFFFFF for 3C 0A. Retrying 3C 0B.");

            var fallbackQuery = this.CreateQuery(
                this.protocol.CreateEngineCalIDReadRequest,
                this.protocol.ParseUInt32FromBlockReadResponse,
                cancellationToken);

            Response<UInt32> fallbackResponse = await fallbackQuery.Execute();
            if (fallbackResponse.Status == ResponseStatus.Success)
            {
                return fallbackResponse;
            }
            return response;
        }

        /// <summary>
        /// Query the PCM's Hardware ID.
        /// </summary>
        /// <remarks>
        /// Note that this is a software variable and my not match the hardware at all of the software runs.
        /// </remarks>
        public async Task<Response<UInt32>> QueryHardwareId()
        {
            return await this.QueryUnsignedValue(this.protocol.CreateHardwareIdReadRequest, CancellationToken.None);
        }

        /// <summary>
        /// Query the PCM's calibration ID.
        /// </summary>
        public async Task<Response<UInt32>> QueryCalibrationId()
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(
                this.protocol.CreateCalibrationIdReadRequest,
                this.protocol.ParseUInt32FromBlockReadResponse,
                CancellationToken.None);
            return await query.Execute();
        }

        /// <summary>
        /// Helper function for queries that return unsigned 32-bit integers.
        /// </summary>
        private async Task<Response<UInt32>> QueryUnsignedValue(Func<Message> generator, CancellationToken cancellationToken)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);

            var query = this.CreateQuery(generator, this.protocol.ParseUInt32FromBlockReadResponse, cancellationToken);
            return await query.Execute();
        }

        /// <summary>
        /// Helper for properties the PCM returns as a fixed sequence of VPW blocks (VIN and
        /// serial are each three blocks). Sends each block request in turn, collects the
        /// replies under a single inbound-filter scope, then hands the raw responses to the
        /// caller's parser.
        /// </summary>
        /// <remarks>
        /// This multi-block shape is VPW-specific: on CAN the same data is a single
        /// ReadDataByIdentifier, so this stays a VPW-local helper rather than a cross-protocol
        /// abstraction. The CAN path fans out separately at the command layer.
        /// </remarks>
        private async Task<Response<string>> ReadBlockSequence(
            Func<Message>[] requestFactories,
            Func<Message[], Response<string>> parse)
        {
            await this.device.SetTimeout(TimeoutScenario.ReadProperty);
            this.device.ClearMessageQueue();

            Message[] responses = new Message[requestFactories.Length];
            Message first = requestFactories[0]();

            // The whole sequence is one logical read from the PCM, so filter to its replies
            // for the duration. All blocks target the same module, so the first request's
            // predicate covers them all.
            using (this.device.FilterInbound(MessageFilters.RepliesFrom(first)))
            {
                for (int i = 0; i < requestFactories.Length; i++)
                {
                    Message request = (i == 0) ? first : requestFactories[i]();
                    if (!await this.device.SendMessage(request))
                    {
                        return Response.Create(ResponseStatus.Timeout, $"Unknown. Request for block {i + 1} failed.");
                    }

                    responses[i] = await this.device.ReceiveMessage();
                    if (responses[i] == null)
                    {
                        return Response.Create(ResponseStatus.Timeout, $"Unknown. No response to request for block {i + 1}.");
                    }
                }
            }

            return parse(responses);
        }
    }
}
