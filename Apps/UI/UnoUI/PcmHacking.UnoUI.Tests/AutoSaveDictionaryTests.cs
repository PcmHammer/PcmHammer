using System;
using System.IO;
using System.Reflection;
using PcmHacking.UnoUI.Presentation;

namespace PcmHacking.UnoUI.Tests;

[NonParallelizable]
public class AutoSaveDictionaryTests
{
    private string _settingsFilePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _settingsFilePath = GetSettingsFilePath();
        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }
    }

    [Test]
    public void AutoSaveDictionary_PersistsIntegerValues()
    {
        const int expected = 42;
        string key = Guid.NewGuid().ToString();
        AutoSaveDictionary dictionary = new();

        dictionary[key] = expected;

        AutoSaveDictionary rehydrated = AutoSaveDictionary.Load();
        rehydrated[key].Should().Be(expected);
    }

    [Test]
    public void AutoSaveDictionary_PersistsBooleanValues()
    {
        const bool expected = true;
        string key = Guid.NewGuid().ToString();
        AutoSaveDictionary dictionary = new();

        dictionary[key] = expected;

        AutoSaveDictionary rehydrated = AutoSaveDictionary.Load();
        rehydrated[key].Should().Be(expected);
    }

    [Test]
    public void AutoSaveDictionary_PersistsStringValues()
    {
        const string expected = "Uno";
        string key = Guid.NewGuid().ToString();
        AutoSaveDictionary dictionary = new();

        dictionary[key] = expected;

        AutoSaveDictionary rehydrated = AutoSaveDictionary.Load();
        rehydrated[key].Should().Be(expected);
    }

    [Test]
    public void AutoSaveDictionary_PersistsStringArrayValues()
    {
        string[] expected = new[] { "Uno", "Dos", "Tres" };
        string key = Guid.NewGuid().ToString();
        AutoSaveDictionary dictionary = new();

        dictionary[key] = expected;

        AutoSaveDictionary rehydrated = AutoSaveDictionary.Load();
        rehydrated[key].Should().BeEquivalentTo(expected);
    }

    [Test]
    public void AutoSaveDictionary_PersistsConnectionSettings()
    {
        CurrentSettings expected = new("Serial", "COM9", "OBDX", "JDevice", true, "CAN0");
        string key = Guid.NewGuid().ToString();
        AutoSaveDictionary dictionary = new();

        dictionary[key] = expected;

        AutoSaveDictionary rehydrated = AutoSaveDictionary.Load();
        rehydrated[key].Should().BeOfType<CurrentSettings>();
        rehydrated[key].Should().Be(expected);
    }

    private static string GetSettingsFilePath()
    {
        MethodInfo? methodInfo = typeof(AutoSaveDictionary).GetMethod("getFilePath", BindingFlags.Static | BindingFlags.NonPublic);
        methodInfo.Should().NotBeNull();
        return (string)methodInfo!.Invoke(null, null)!;
    }
}
