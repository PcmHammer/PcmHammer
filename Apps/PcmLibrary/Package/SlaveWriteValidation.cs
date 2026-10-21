// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace PcmHacking
{
    /// <summary>Resolve and validate the entire slave set before uploading any code to the PCM.</summary>
    public static class SlaveWriteValidation
    {
        public static IList<byte[]> Resolve(PackageController controller, OSIDInfo info)
        {
            var images = controller.Images.Where(i => i.Target != null
                && i.Target.StartsWith("slave", StringComparison.OrdinalIgnoreCase)).ToList();
            if (images.Count != info.SlaveModules.Count
                || images.Any(i => !info.SlaveModules.Any(s => string.Equals(s.Target, i.Target, StringComparison.OrdinalIgnoreCase))))
                throw new PackageException("The package must contain exactly the required slave OS and calibration modules.");

            var result = new List<byte[]>();
            foreach (SlaveModuleId required in info.SlaveModules)
            {
                var matching = images.Where(i => string.Equals(i.Target, required.Target, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matching.Count != 1)
                    throw new PackageException("Missing or duplicate " + required.Target + " module.");
                PackageImage image = matching[0];
                byte[]? data = image.Data ?? SlaveLibrary.Resolve(image.FileName);
                if (data == null) throw new PackageException("Missing slave image: " + image.FileName);
                if (!FlashModuleBuilder.TryGetSlaveModulePartNumber(data, out uint partNumber))
                    throw new PackageException("Invalid part number in " + required.Target + ".");
                if (image.PartNumber.HasValue && image.PartNumber.Value != partNumber)
                    throw new PackageException("The " + required.Target + " part number does not match the package reference.");
                string stem = System.IO.Path.GetFileNameWithoutExtension(image.FileName ?? string.Empty);
                if (uint.TryParse(stem, out uint namedPart) && namedPart != partNumber)
                    throw new PackageException("The slave file name does not match its embedded part number.");
                result.Add(data);
            }

            // Includes structural checks and the E38 factory-image integrity check. Resolve must
            // not return bytes until ALL modules pass, even if their IDs already match the ECU.
            FlashModuleBuilder.BuildSlaveModules(result, info);
            return result;
        }
    }
}
