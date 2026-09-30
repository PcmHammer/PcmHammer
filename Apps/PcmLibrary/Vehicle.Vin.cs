// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PcmHacking
{
    /// <summary>A VIN and where it came from, so a following write need not probe again.</summary>
    public sealed class VinReadResult
    {
        public string Vin { get; }
        public BusProtocol Bus { get; }
        public OSIDInfo PcmInfo { get; }

        public VinReadResult(string vin, BusProtocol bus, OSIDInfo pcmInfo)
        {
            this.Vin = vin;
            this.Bus = bus;
            this.PcmInfo = pcmInfo;
        }
    }

    public partial class Vehicle
    {
        /// <summary>
        /// Read the VIN from whichever bus the PCM answers on: VPW reads the three 0x3C blocks, CAN
        /// reads GMLAN data identifier 0x90. Null if no PCM answered or the read failed.
        /// </summary>
        public async Task<VinReadResult?> ReadVin(CancellationToken cancellationToken)
        {
            DetectedModule? pcm = await this.DetectAndSelectPcm(cancellationToken);
            if (pcm == null)
            {
                this.logger.AddUserMessage("No PCM detected.");
                return null;
            }

            if (pcm.Bus == BusProtocol.Can500k)
            {
                CanCommands commands = this.CreateCanCommands();
                Response<byte[]> response = await commands.ReadDataByIdentifier(
                    Gmlan.VinDataIdentifier, cancellationToken);
                string? vin = DecodeCanVin(response);
                if (vin == null)
                {
                    this.logger.AddUserMessage("VIN query failed: " + response.Status.ToString());
                    return null;
                }

                return new VinReadResult(vin, pcm.Bus, pcm.Info);
            }

            Response<string> vpwResponse = await this.QueryVin();
            if (vpwResponse.Status != ResponseStatus.Success)
            {
                this.logger.AddUserMessage("VIN query failed: " + vpwResponse.Status.ToString());
                return null;
            }

            return new VinReadResult(vpwResponse.Value, pcm.Bus, pcm.Info);
        }

        /// <summary>
        /// Write a VIN over whichever bus the PCM is on, unlocking it first. VPW writes the three 0x3C
        /// blocks; CAN writes GMLAN data identifier 0x90 as 17 ASCII bytes. Pass bus and pcmInfo from a
        /// preceding <see cref="ReadVin"/>, or null to detect them here.
        /// </summary>
        public async Task<bool> WriteVin(
            string vin, BusProtocol? bus, OSIDInfo? pcmInfo, CancellationToken cancellationToken)
        {
            vin = vin.Trim().ToUpperInvariant();
            if (vin.Length != 17)
            {
                this.logger.AddUserMessage("VIN " + vin + " is not 17 characters long!");
                return false;
            }

            if (bus == null || pcmInfo == null)
            {
                DetectedModule? pcm = await this.DetectAndSelectPcm(cancellationToken);
                if (pcm == null)
                {
                    this.logger.AddUserMessage("No PCM detected.");
                    return false;
                }

                bus = pcm.Bus;
                pcmInfo = pcm.Info;
            }

            if (bus == BusProtocol.Can500k)
            {
                CanCommands commands = this.CreateCanCommands();

                // The CAN PCMs in scope share the E38 algorithm; use it when the OSID did not resolve.
                OSIDInfo unlockInfo = pcmInfo.IsSupported ? pcmInfo : new OSIDInfo(PcmType.E38);

                this.logger.AddUserMessage("Unlocking PCM...");
                if (!await commands.Unlock(unlockInfo, cancellationToken))
                {
                    this.logger.AddUserMessage("Unable to unlock PCM.");
                    return false;
                }

                this.logger.AddUserMessage("Changing VIN to " + vin);
                byte[] vinBytes = Encoding.ASCII.GetBytes(vin);
                if (!await commands.WriteDataByIdentifier(Gmlan.VinDataIdentifier, vinBytes, cancellationToken))
                {
                    this.logger.AddUserMessage("Unable to change the VIN to " + vin + ".");
                    return false;
                }

                this.logger.AddUserMessage("VIN successfully updated to " + vin);
                return true;
            }

            this.logger.AddUserMessage("Unlocking PCM...");
            if (!await this.UnlockEcu(pcmInfo.KeyAlgorithm, cancellationToken))
            {
                this.logger.AddUserMessage("Unable to unlock PCM.");
                return false;
            }

            Response<bool> written = await this.UpdateVin(vin);
            if (written.Status != ResponseStatus.Success || !written.Value)
            {
                this.logger.AddUserMessage("Unable to change the VIN to " + vin + ". Error: " + written.Status);
                return false;
            }

            this.logger.AddUserMessage("VIN successfully updated to " + vin);
            return true;
        }

        /// <summary>The change only reaches flash if the module shuts down normally.</summary>
        public const string VinWriteFollowUp =
            "Turn the ignition off while leaving power connected for a few seconds to finish the save to flash.";

        /// <summary>Decode the VIN from a GMLAN 1A 90 response [5A 90 ...]; null if it is not one.</summary>
        private static string? DecodeCanVin(Response<byte[]> response)
        {
            if (response.Status != ResponseStatus.Success)
            {
                return null;
            }

            byte[] bytes = response.Value;
            if (bytes == null
                || bytes.Length < 3
                || bytes[0] != Gmlan.ReadDataByIdentifierResponse
                || bytes[1] != Gmlan.VinDataIdentifier)
            {
                return null;
            }

            string text = Encoding.ASCII.GetString(bytes.Skip(2).ToArray());
            text = new string(text.ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());
            if (text.Length > 17)
            {
                text = text.Substring(0, 17);
            }

            return text;
        }
    }
}
