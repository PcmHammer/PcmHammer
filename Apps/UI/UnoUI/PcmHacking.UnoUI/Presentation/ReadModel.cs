namespace PcmHacking.UnoUI.Presentation;


public partial record ReadModel(Entity readModeEntity)
{
    public string Title { get { return "Read PCM"; } }
}
