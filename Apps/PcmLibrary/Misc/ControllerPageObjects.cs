using PcmHacking.ECU;
using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking;

public class ECUActionArguments
{
    public ControllerActions SelectedAction = ControllerActions.Undefined;
    public WriteType WriteType = WriteType.None;
    public PcmType HardwareType = PcmType.Undefined; 
    public bool UseHighSpeed = false;
    public bool ShowDebug = false;
    public uint CustomKey = 0;
    public MemoryStream ContentStream;
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
