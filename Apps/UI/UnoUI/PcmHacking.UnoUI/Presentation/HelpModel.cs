namespace PcmHacking.UnoUI.Presentation;

public partial record HelpModel()
{
    IState<string> Text => State<string>.Value(this, () => "TODO: Add help text here.");
}
