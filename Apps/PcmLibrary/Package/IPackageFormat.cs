// SPDX-License-Identifier: GPL-3.0-only
using System.IO;

namespace PcmHacking
{
    /// <summary>
    /// A file format a <see cref="PcmPackage"/> can be read from and written to. Pure bytes/model logic;
    /// register new formats with <see cref="PackageStore.Register"/>.
    /// </summary>
    public interface IPackageFormat
    {
        string Name { get; }

        /// <summary>Extension this format owns, including the dot, lower-case (e.g. ".phz").</summary>
        string Extension { get; }

        bool CanLoad(string path);

        /// <summary><paramref name="path"/> is for messages only.</summary>
        PcmPackage Load(Stream stream, string path);

        void Save(Stream stream, PcmPackage package);
    }
}
