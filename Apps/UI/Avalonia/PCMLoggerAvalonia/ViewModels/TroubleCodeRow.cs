// SPDX-License-Identifier: GPL-3.0-only
namespace PCMLoggerAvalonia.ViewModels;

/// <summary>
/// One row in the Trouble Codes list: the code, what the module says about it, and the description.
/// Immutable - the list is rebuilt from the shared <see cref="PcmHacking.DiagnosticCode"/> results on
/// each read, so there is nothing to observe per row.
/// </summary>
/// <remarks>
/// Mirrors the WinForms PcmLogger Trouble Codes tab (Code / Status / Description), which is the
/// reference layout. The read/clear work itself lives in the shared PcmLibrary (Vehicle.ReadTroubleCodes
/// / Vehicle.ClearTroubleCodes); this is only what the list binds to.
/// </remarks>
public sealed class TroubleCodeRow
{
    public TroubleCodeRow(string code, string status, string description)
    {
        this.Code = code;
        this.Status = status;
        this.Description = description;
    }

    /// <summary>"P0143", "U2100", and so on.</summary>
    public string Code { get; }

    /// <summary>The module's status text, or which list (Stored/Pending/Permanent) it came from.</summary>
    public string Status { get; }

    /// <summary>What the code means, or "No description available".</summary>
    public string Description { get; }
}
