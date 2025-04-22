namespace PcmHacking.UnoUI.Presentation;

public partial record DataLoggingEditModel()
{
    IState<string> Text => State<string>.Value(this, () => "TODO: Add help text here.");
}
