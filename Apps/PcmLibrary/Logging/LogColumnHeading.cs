// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    /// <summary>
    /// The heading one column gets in a log file.
    /// </summary>
    /// <remarks>
    /// Shaped as "PID 000C: Engine Speed (RPM)" - where the value came from first, then what it is.
    /// Leading rather than trailing so both a reader and a person can take the address off the front
    /// without reading to the end of a long name, and a column with no address is simply the name.
    ///
    /// In one place because three producers used to build it separately, and had already drifted:
    /// the broadcast list wrote "Name(Units)" where the others wrote "Name (Units)".
    /// </remarks>
    public static class LogColumnHeading
    {
        public static string For(Parameter parameter, string units)
        {
            string name = parameter.Name + " (" + units + ")";
            string? address = AddressOf(parameter);

            return address == null ? name : address + ": " + name;
        }

        /// <summary>
        /// How this parameter is addressed, or null when it has no address worth recording: a math
        /// column is computed rather than read, and a RAM address varies by operating system, so
        /// neither identifies anything on its own.
        /// </summary>
        private static string? AddressOf(Parameter parameter)
        {
            if (parameter is PidParameter pid)
            {
                return "PID " + pid.PID.ToString("X4");
            }

            if (parameter is BusParameter bus)
            {
                return "MSG " + bus.MessageId.ToString("X8");
            }

            return null;
        }
    }
}
