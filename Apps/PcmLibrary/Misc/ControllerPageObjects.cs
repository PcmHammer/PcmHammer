using System;
using System.Collections.Generic;
using System.Text;

namespace PcmHacking;

// Just a collection of the tasks passed to Read/WriteManager.
public class ControllerPageObjects
{
    public Func<Action, Task> Invoke;
    public Func<Task<string>> PromptForSavePath;
    public Func<Task<UInt32>> PromptForHardwareType;
    public Func<string, string, Task> ShowAlert;
    public Func<string, string, Task<bool>> PromptYesOrNo;
}
