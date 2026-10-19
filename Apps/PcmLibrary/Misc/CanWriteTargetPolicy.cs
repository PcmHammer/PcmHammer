// SPDX-License-Identifier: GPL-3.0-only
namespace PcmHacking
{
    public static class CanWriteTargetPolicy
    {
        public static bool ForcedTypeMatches(PcmType fileType, PcmType selectedType, bool allowCrossFlashing) =>
            selectedType != PcmType.Undefined && (fileType == selectedType || allowCrossFlashing);
    }
}
