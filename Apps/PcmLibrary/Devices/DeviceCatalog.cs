// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// A selectable serial device type: its stable key (used by DeviceFactory and saved settings),
    /// the buses it can use, and - for the recommended interface - a purchase link. The display name
    /// is generated (base name + protocols in brackets), so the protocol list is stated once here and
    /// nowhere else. <see cref="Protocols"/> is also the authority for the per-device protocol gate,
    /// so the label and what the device will actually accept cannot drift apart.
    /// </summary>
    public sealed class DeviceDescriptor
    {
        public string Key { get; }
        public string BaseName { get; }
        public IReadOnlyList<BusProtocol> Protocols { get; }
        public bool Recommended { get; }
        public string? PurchaseUrl { get; }

        public DeviceDescriptor(
            string key, string baseName, BusProtocol[] protocols, bool recommended = false, string? purchaseUrl = null)
        {
            this.Key = key;
            this.BaseName = baseName;
            this.Protocols = protocols;
            this.Recommended = recommended;
            this.PurchaseUrl = purchaseUrl;
        }

        /// <summary>Name shown in the picker, e.g. "OBDX Pro (VPW, CAN)" or "SLCAN (CAN)".</summary>
        public string DisplayName => $"{this.BaseName} {DeviceCatalog.FormatProtocols(this.Protocols)}";

        public bool Supports(BusProtocol protocol) => this.Protocols.Contains(protocol);

        /// <summary>The display name, so a WinForms ComboBox holding descriptors renders it directly.</summary>
        public override string ToString() => this.DisplayName;
    }

    /// <summary>
    /// The set of interfaces the app offers, described once for every front end. UIs build their
    /// device pickers from this instead of hard-coding names, so the list, the protocol labels, the
    /// recommended-device link and the J2534 platform gate all live in one place.
    /// </summary>
    public static class DeviceCatalog
    {
        /// <summary>The referral link for the recommended interface; buying through it supports the project.</summary>
        public const string ObdxProPurchaseUrl = "https://obdxpro.com/?ref=pcmhacking";

        /// <summary>
        /// Formats a protocol set as the bracketed suffix a device picker appends to a name, e.g.
        /// "(VPW, CAN)". Both the serial catalog and the J2534 listing use this so the labels match.
        /// </summary>
        public static string FormatProtocols(IEnumerable<BusProtocol> protocols) =>
            "(" + string.Join(", ", protocols.Select(ProtocolLabel)) + ")";

        private static string ProtocolLabel(BusProtocol protocol) => protocol switch
        {
            BusProtocol.Vpw => "VPW",
            BusProtocol.Can500k => "CAN",
            _ => protocol.ToString(),
        };

        /// <summary>
        /// Serial device types, in picker order. Protocols here drive both the display name and the
        /// runtime protocol gate (see the device SetProtocol overrides).
        /// </summary>
        public static IReadOnlyList<DeviceDescriptor> SerialDevices { get; } = new[]
        {
            new DeviceDescriptor(ElmDevice.DeviceType,     "ObdLink or AllPro", new[] { BusProtocol.Vpw }),
            new DeviceDescriptor(AvtDevice.DeviceType838,  "AVT 838",           new[] { BusProtocol.Vpw }),
            new DeviceDescriptor(AvtDevice.DeviceType,     "AVT 842/852",       new[] { BusProtocol.Vpw, BusProtocol.Can500k }),
            new DeviceDescriptor(OBDXProDevice.DeviceType, "OBDX Pro",          new[] { BusProtocol.Vpw, BusProtocol.Can500k },
                                 recommended: true, purchaseUrl: ObdxProPurchaseUrl),
            new DeviceDescriptor(SlcanDevice.DeviceType,   "SLCAN",             new[] { BusProtocol.Can500k }),
        };

        /// <summary>The recommended interface (for the "buy one" prompt), or null if none is flagged.</summary>
        public static DeviceDescriptor? Recommended => SerialDevices.FirstOrDefault(d => d.Recommended);

        /// <summary>
        /// Whether J2534 is usable on this platform. J2534 is Windows-only (the driver model lives in
        /// PcmLibraryWindowsApi), so the Windows front ends set this true at startup and other
        /// platforms leave it false, which hides the J2534 option in the picker.
        /// </summary>
        public static bool J2534Available { get; set; }

        /// <summary>The descriptor for a device-type key, or null if it is not a catalog serial device.</summary>
        public static DeviceDescriptor? Find(string? key) =>
            key == null ? null : SerialDevices.FirstOrDefault(d => d.Key == key);

        /// <summary>
        /// Whether a device type advertises a protocol - the single source the device SetProtocol
        /// overrides gate on. Unknown device types default to VPW-only.
        /// </summary>
        public static bool Supports(string? key, BusProtocol protocol)
        {
            DeviceDescriptor? descriptor = Find(key);
            return descriptor != null ? descriptor.Supports(protocol) : protocol == BusProtocol.Vpw;
        }
    }
}
