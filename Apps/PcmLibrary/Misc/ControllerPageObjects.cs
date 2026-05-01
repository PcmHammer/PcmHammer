using PcmHacking.ECU;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking;

public class ECUActionArguments
{
    /// <summary>
    /// Sets the primary function of the pending action.
    /// </summary>
    public ControllerActions SelectedAction = ControllerActions.Undefined;

    /// <summary>
    /// Specifies the type of write operation to perform.
    /// </summary>
    public WriteType WriteType = WriteType.None;

    /// <summary>
    /// Specified controller type.
    /// </summary>
    public PcmType HardwareType = PcmType.Undefined; 

    /// <summary>
    /// Final  whether high-speed mode is enabled.
    /// </summary>
    public bool UseHighSpeed = false;

    /// <summary>
    /// Indicates whether debug information is displayed.
    /// </summary>
    public bool ShowDebug = false;

    /// <summary>
    /// This bool allows us to skip the pre-flight checks still found in the Read/Write manager classes.
    /// </summary>
    public bool PreFlightChecksRequired = false;

    // Custom key to use in this action.
    public uint CustomKey = 0;

    /// <summary>
    /// This MemoryStream is used both to hold file contents to be written,
    /// as well as returning what was read from a controller.
    /// </summary>
    public MemoryStream? ContentStream;

    /// <summary>
    /// This base object's purpose is to hold a refence to a StorageFile type used by File pickers in Uno.
    /// Android cannot refence files by a path, using a content URI instead that doesn't refer to the file directly.
    /// Use of the 'object' type allows proper handling into and out of the .NetStandard lib.
    /// </summary>
    public object? StorageFileObject;
}

// Just a collection of the tasks passed to Read/WriteManager.
public class ControllerPageObjects
{
    public Func<Action, Task> Invoke;
    public Func<Task<string>> PromptForSavePath;
    public Func<Task<UInt32>> PromptForHardwareType;
    public Func<string, string, Task> ShowAlert;
    public Func<string, string, Task<bool>> PromptYesOrNo;
}
