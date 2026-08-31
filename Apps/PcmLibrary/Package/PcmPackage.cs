// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace PcmHacking
{
    /// <summary>
    /// In-memory model of a PcmHammer package (one or more controllers, each with one or more images),
    /// and the manifest.json schema. See Docs/pcmhammer-file-format.md.
    /// </summary>
    public class PcmPackage
    {
        [JsonProperty("format")] public string Format { get; set; } = "pcmhammer/package";
        [JsonProperty("formatVersion")] public int FormatVersion { get; set; } = 1;
        [JsonProperty("generator")] public string? Generator { get; set; }
        [JsonProperty("created")] public string? Created { get; set; }
        [JsonProperty("vehicle", NullValueHandling = NullValueHandling.Ignore)] public PackageVehicle? Vehicle { get; set; }
        [JsonProperty("notes", NullValueHandling = NullValueHandling.Ignore)] public string? Notes { get; set; }
        [JsonProperty("controllers")] public List<PackageController> Controllers { get; set; } = new List<PackageController>();

        /// <summary>Every image across every controller (convenience for verification/enumeration).</summary>
        [JsonIgnore] public IEnumerable<PackageImage> AllImages => this.Controllers.SelectMany(c => c.Images);
    }

    public class PackageVehicle
    {
        [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)] public string? Description { get; set; }
        [JsonProperty("vin", NullValueHandling = NullValueHandling.Ignore)] public string? Vin { get; set; }
    }

    public class PackageController
    {
        /// <summary>Stable key within the package; also the name of the folder holding this controller's images.</summary>
        [JsonProperty("id")] public int Id { get; set; }

        /// <summary>Broad category (e.g. "PCM", "TCM"). Informational/for future use.</summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)] public string? Type { get; set; }

        /// <summary>The specific module - the key into <see cref="OSIDInfo"/> (e.g. "E38", "T42").</summary>
        [JsonProperty("moduleType", NullValueHandling = NullValueHandling.Ignore)] public string? ModuleType { get; set; }

        /// <summary>Diagnostic id on the bus (e.g. "0x7E0"). For future use / non-default topologies.</summary>
        [JsonProperty("busId", NullValueHandling = NullValueHandling.Ignore)] public string? BusId { get; set; }

        [JsonProperty("images")] public List<PackageImage> Images { get; set; } = new List<PackageImage>();

        /// <summary>The image with the given target, or null.</summary>
        public PackageImage? Image(string target) =>
            this.Images.FirstOrDefault(i => string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase));
    }

    public class PackageImage
    {
        /// <summary>What this image is / where it goes (e.g. "main", "slave-os", "slave-calibration").
        /// The address/size/algorithm are derived from (moduleType, target) via the PCM config, not stored here.</summary>
        [JsonProperty("target")] public string? Target { get; set; }

        /// <summary>File name within the controller's folder (e.g. "main.bin", "12625892.bin").
        /// The archive path is "<controller.Id>/<FileName>".</summary>
        [JsonProperty("file")] public string? FileName { get; set; }

        [JsonProperty("size")] public long Size { get; set; }
        [JsonProperty("sha256")] public string? Sha256 { get; set; }

        [JsonProperty("osid", NullValueHandling = NullValueHandling.Ignore)] public uint? Osid { get; set; }
        [JsonProperty("partNumber", NullValueHandling = NullValueHandling.Ignore)] public uint? PartNumber { get; set; }

        /// <summary>Image bytes, or null for a reference. Not in the manifest; stored as a separate archive entry.</summary>
        [JsonIgnore] public byte[]? Data { get; set; }

        /// <summary>
        /// A reference is declared by name/part number but carries no bytes (an E38 slave can be identified
        /// but not read); the writer resolves it from the local library, so it omits size/checksum.
        /// </summary>
        [JsonIgnore] public bool IsReference => this.Data == null;

        // The serializer calls these: only an embedded image carries a size and checksum in the manifest.
        public bool ShouldSerializeSize() => !this.IsReference;
        public bool ShouldSerializeSha256() => !this.IsReference;

        /// <summary>Create a reference image (declared by name/part number, bytes resolved from the library on write).</summary>
        public static PackageImage Reference(string target, string fileName, uint? partNumber = null) =>
            new PackageImage { Target = target, FileName = fileName, PartNumber = partNumber };

        /// <summary>Recompute <see cref="Size"/> and <see cref="Sha256"/> from <see cref="Data"/> (called on save).</summary>
        public void RefreshHash()
        {
            this.Size = this.Data?.LongLength ?? 0;
            this.Sha256 = ComputeSha256(this.Data ?? Array.Empty<byte>());
        }

        /// <summary>Throw if <see cref="Data"/> doesn't match the manifest's <see cref="Size"/>/<see cref="Sha256"/>.
        /// This is what catches a truncated or edited image so it can't silently half-work.</summary>
        public void Verify(int controllerId)
        {
            long actualSize = this.Data?.LongLength ?? 0;
            if (actualSize != this.Size)
            {
                throw new PackageException(string.Format(
                    "Image {0}/{1} is {2} bytes but the manifest says {3} - the file is incomplete or altered.",
                    controllerId, this.FileName, actualSize, this.Size));
            }
            string actual = ComputeSha256(this.Data ?? Array.Empty<byte>());
            if (!string.IsNullOrEmpty(this.Sha256) && !string.Equals(actual, this.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageException(string.Format(
                    "Image {0}/{1} failed its checksum (manifest {2}, actual {3}) - it is corrupt or altered.",
                    controllerId, this.FileName, this.Sha256, actual));
            }
        }

        public static string ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var sb = new System.Text.StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>Raised for any package load/save problem (bad format, failed integrity check, unwritable data).</summary>
    public class PackageException : Exception
    {
        public PackageException(string message) : base(message) { }
        public PackageException(string message, Exception inner) : base(message, inner) { }
    }
}
