using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using PCMLoggerAvalonia.ViewModels;

namespace PCMLoggerAvalonia;

/// <summary>
/// Maps a view model to its view by naming convention (FooViewModel -> FooView / Foo),
/// the standard Avalonia MVVM pattern.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        string name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        Type? type = Type.GetType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
