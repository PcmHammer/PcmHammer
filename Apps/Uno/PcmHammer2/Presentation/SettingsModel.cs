namespace UnoExperiment1.Presentation;

public class FakePort
{
    private string name;

    public FakePort(string name)
    {
        this.name = name;
    }

    public override string ToString()
    {
        return this.name;
    }
}

public partial record SettingsModel()
{
    private FakePort[] ports = new FakePort[] { new FakePort("None"), new FakePort("COM1"), new FakePort("COM2"), new FakePort("COM3") };
    private string[] deviceTypes = new string[] { "OBDX", "ObdLink Or AllPro", "AVT", };

    public string Title { get { return "Settings"; } }

    public FakePort[] Ports
    {
        get { return ports; }
    }

    public string[] DeviceTypes
    {
        get { return this.deviceTypes; }
    }
}
