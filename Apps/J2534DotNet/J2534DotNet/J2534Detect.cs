#region Copyright (c) 2010, Michael Kelly
/*
 * Copyright (c) 2010, Michael Kelly
 * michael.e.kelly@gmail.com
 * http://michael-kelly.com/
 *
 * All rights reserved.
 * Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:
 * Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
 * Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.
 * Neither the name of the organization nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
 * A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *
 */
#endregion License
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace J2534DotNet
{
    static public class J2534Detect
    {
        private const string PASSTHRU_REGISTRY_PATH = "Software\\PassThruSupport.04.04";

        /// <summary>
        /// List every installed J2534 driver, from both registry views.
        /// </summary>
        /// <remarks>
        /// 32-bit and 64-bit drivers register under separate views of the same key, and the registry
        /// redirects a process to the view matching its own bitness. Reading only that one view hides
        /// every driver installed with the other bitness - which on a 64-bit process is usually most
        /// of them, since J2534 drivers are typically 32-bit. The native view is read first so that a
        /// driver registered in both views is taken from the view whose DLL this process can load.
        /// </remarks>
        static public List<J2534Device> ListDevices()
        {
            List<J2534Device> j2534Devices = new List<J2534Device>();
            HashSet<string> namesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            RegistryView nativeView = Environment.Is64BitProcess ? RegistryView.Registry64 : RegistryView.Registry32;
            RegistryView otherView = Environment.Is64BitProcess ? RegistryView.Registry32 : RegistryView.Registry64;

            AddDevicesFromView(j2534Devices, namesSeen, nativeView);
            AddDevicesFromView(j2534Devices, namesSeen, otherView);

            return j2534Devices;
        }

        /// <summary>
        /// Append the drivers registered in one registry view, skipping names already listed.
        /// </summary>
        private static void AddDevicesFromView(List<J2534Device> j2534Devices, HashSet<string> namesSeen, RegistryView view)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (RegistryKey? myKey = baseKey.OpenSubKey(PASSTHRU_REGISTRY_PATH, false))
            {
                if (myKey == null)
                {
                    return;
                }

                foreach (string device in myKey.GetSubKeyNames())
                {
                    using (RegistryKey? deviceKey = myKey.OpenSubKey(device))
                    {
                        if (deviceKey == null)
                            continue;

                        J2534Device tempDevice = new J2534Device();
                        tempDevice.Vendor = deviceKey.GetValue("Vendor", "") as string ?? string.Empty;
                        tempDevice.Name = deviceKey.GetValue("Name", "") as string ?? string.Empty;
                        tempDevice.ConfigApplication = deviceKey.GetValue("ConfigApplication", "") as string ?? string.Empty;
                        tempDevice.FunctionLibrary = deviceKey.GetValue("FunctionLibrary", "") as string ?? string.Empty;

                        tempDevice.CANChannels = (int)(deviceKey.GetValue("CAN", 0) ?? 0);
                        tempDevice.ISO15765Channels = (int)(deviceKey.GetValue("ISO15765", 0) ?? 0);
                        tempDevice.J1850PWMChannels = (int)(deviceKey.GetValue("J1850PWM", 0) ?? 0);
                        tempDevice.J1850VPWChannels = (int)(deviceKey.GetValue("J1850VPW", 0) ?? 0);
                        tempDevice.ISO9141Channels = (int)(deviceKey.GetValue("ISO9141", 0) ?? 0);
                        tempDevice.ISO14230Channels = (int)(deviceKey.GetValue("ISO14230", 0) ?? 0);
                        tempDevice.SCI_A_ENGINEChannels = (int)(deviceKey.GetValue("SCI_A_ENGINE", 0) ?? 0);
                        tempDevice.SCI_A_TRANSChannels = (int)(deviceKey.GetValue("SCI_A_TRANS", 0) ?? 0);
                        tempDevice.SCI_B_ENGINEChannels = (int)(deviceKey.GetValue("SCI_B_ENGINE", 0) ?? 0);
                        tempDevice.SCI_B_TRANSChannels = (int)(deviceKey.GetValue("SCI_B_TRANS", 0) ?? 0);

                        // The device is chosen by name elsewhere, so a name listed twice would be
                        // ambiguous. The native view was read first, so this keeps the loadable one.
                        if (!namesSeen.Add(tempDevice.Name))
                            continue;

                        j2534Devices.Add(tempDevice);
                    }
                }
            }
        }
    }
}
