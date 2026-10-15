// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace PcmHacking
{
    /// <summary>
    /// Which interface to use, as a value: the category and whatever that category needs to identify
    /// one piece of hardware.
    /// </summary>
    /// <remarks>
    /// Named because there is now more than one slot to fill. Previously there was a single implicit
    /// "the device" spread across saved settings, which is why a second one was awkward - there was
    /// nowhere to put it and no way to compare two.
    ///
    /// Comparing is the point: two slots must not hold the same interface, because the Device layer
    /// gives each instance its own driver handle and channel. Equality here is what that rule is
    /// written in terms of.
    ///
    /// In PcmLibrary rather than beside the WinForms settings so every front end can use it - the
    /// WPF and Uno pickers currently re-derive these same rules separately.
    /// </remarks>
    public sealed class DeviceSelection : IEquatable<DeviceSelection>
    {
        /// <summary>Nothing selected. The default for a slot nobody has filled in.</summary>
        public static readonly DeviceSelection None =
            new DeviceSelection(DeviceConstants.DeviceCategoryNone, string.Empty, string.Empty, string.Empty);

        public DeviceSelection(
            string? category, string? j2534DeviceType, string? serialPort, string? serialPortDeviceType)
        {
            this.Category = string.IsNullOrWhiteSpace(category)
                ? DeviceConstants.DeviceCategoryNone
                : category!;

            this.J2534DeviceType = j2534DeviceType ?? string.Empty;
            this.SerialPort = serialPort ?? string.Empty;
            this.SerialPortDeviceType = serialPortDeviceType ?? string.Empty;
        }

        public string Category { get; }

        public string J2534DeviceType { get; }

        public string SerialPort { get; }

        public string SerialPortDeviceType { get; }

        /// <summary>Whether this names an interface, as opposed to meaning "none chosen".</summary>
        public bool IsSelected => DeviceConstants.IsDeviceSelected(this.Category);

        /// <summary>
        /// Whether this selection is complete enough to open. A category with nothing to identify
        /// the hardware is a half-filled form, not a device.
        /// </summary>
        public bool IsUsable
        {
            get
            {
                if (this.Category == DeviceConstants.DeviceCategorySerial)
                {
                    return !string.IsNullOrEmpty(this.SerialPort)
                        && !string.IsNullOrEmpty(this.SerialPortDeviceType);
                }

                if (this.Category == DeviceConstants.DeviceCategoryJ2534)
                {
                    return !string.IsNullOrEmpty(this.J2534DeviceType);
                }

                return false;
            }
        }

        /// <summary>
        /// Whether two selections would open the same piece of hardware. Two empty slots are not a
        /// clash - there is nothing to clash over.
        /// </summary>
        public bool ConflictsWith(DeviceSelection? other)
        {
            if (other == null || !this.IsSelected || !other.IsSelected)
            {
                return false;
            }

            return this.Equals(other);
        }

        /// <summary>How the interface should be described to the user.</summary>
        public string Describe()
        {
            if (this.Category == DeviceConstants.DeviceCategorySerial)
            {
                return string.IsNullOrEmpty(this.SerialPort)
                    ? "No interface selected"
                    : this.SerialPortDeviceType + " on " + this.SerialPort;
            }

            if (this.Category == DeviceConstants.DeviceCategoryJ2534)
            {
                return string.IsNullOrEmpty(this.J2534DeviceType)
                    ? "No interface selected"
                    : this.J2534DeviceType;
            }

            return "No interface selected";
        }

        public bool Equals(DeviceSelection? other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(this.Category, other.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(this.J2534DeviceType, other.J2534DeviceType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(this.SerialPort, other.SerialPort, StringComparison.OrdinalIgnoreCase)
                && string.Equals(this.SerialPortDeviceType, other.SerialPortDeviceType, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj) => this.Equals(obj as DeviceSelection);

        public override int GetHashCode()
        {
            // Category alone is a poor hash, but these live in tiny collections - a slot each - so
            // the cost is nil and the implementation stays obvious.
            return StringComparer.OrdinalIgnoreCase.GetHashCode(this.Category);
        }

        public override string ToString() => this.Describe();
    }
}
