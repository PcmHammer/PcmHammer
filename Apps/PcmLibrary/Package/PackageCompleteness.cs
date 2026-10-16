// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>
    /// The invariant that a .phz is only written when complete - a main image plus, for a slave-bearing
    /// PCM like the E38, its slave modules. An incomplete document may only be saved as a raw .bin.
    /// </summary>
    public static class PackageCompleteness
    {
        /// <summary>
        /// True when every controller carries a main image and every slave module its platform declares.
        /// On false, <paramref name="reason"/> is a short user-facing explanation of what is missing.
        /// </summary>
        public static bool IsComplete(PcmPackage package, out string reason)
        {
            reason = string.Empty;
            if (package == null || package.Controllers.Count == 0)
            {
                reason = "The package has no controllers.";
                return false;
            }

            foreach (PackageController controller in package.Controllers)
            {
                if (controller.Image("main") == null)
                {
                    reason = ControllerName(controller) + " has no main image.";
                    return false;
                }

                foreach (string requiredTarget in RequiredSlaveTargets(controller))
                {
                    if (controller.Image(requiredTarget) == null)
                    {
                        reason = string.Format(
                            "{0} is missing its \"{1}\" module. A main image on its own is not a complete {2} package.",
                            ControllerName(controller), requiredTarget, controller.ModuleType ?? "PCM");
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// True when a raw .bin is the natural save format: the package holds a single image, the main
        /// (master CPU) image of a controller with no slave CPU. Any further image - another controller,
        /// or a slave CPU or EEPROM image within one - needs a .phz, and an unidentified controller keeps
        /// the .phz default.
        /// </summary>
        public static bool PrefersBin(PcmPackage package)
        {
            if (package == null || package.Controllers.Sum(c => c.Images.Count) != 1)
            {
                return false;
            }

            PackageController controller = package.Controllers.First(c => c.Images.Count == 1);
            if (controller.Image("main") == null)
            {
                return false;
            }

            OSIDInfo? info = ResolvePlatform(controller);
            return info != null && !info.HardwareSlaveCPU;
        }

        /// <summary>The slave CPU image targets the controller requires (empty when it has no slave CPU).</summary>
        public static IEnumerable<string> RequiredSlaveTargets(PackageController controller)
        {
            OSIDInfo? info = ResolvePlatform(controller);
            if (info == null || !info.HardwareSlaveCPU)
            {
                return Enumerable.Empty<string>();
            }

            return info.SlaveModules.Select(m => m.Target).ToList();
        }

        /// <summary>Resolve the controller's platform from its ModuleType (e.g. "E38"), or null if unknown.</summary>
        public static OSIDInfo? ResolvePlatform(PackageController controller)
        {
            if (string.IsNullOrEmpty(controller.ModuleType))
            {
                return null;
            }

            if (Enum.TryParse(controller.ModuleType, ignoreCase: true, out PcmType type) && type != PcmType.Undefined)
            {
                return new OSIDInfo(type);
            }

            return null;
        }

        private static string ControllerName(PackageController controller) =>
            controller.ModuleType ?? controller.Type ?? ("Controller " + controller.Id);
    }
}
