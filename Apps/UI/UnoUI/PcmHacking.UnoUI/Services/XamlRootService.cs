using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PcmHacking.UnoUI.Services;

public class XamlRootService
{
    private static XamlRoot? xamlRoot;

    public static void Initialize(XamlRoot xamlRoot)
    {
        XamlRootService.xamlRoot = xamlRoot;
    }

    public static XamlRoot GetXamlRoot()
    {
        if (xamlRoot == null)
        {
            throw new InvalidOperationException("XamlRootService.Initialize() must be called before XamlRootService.GetXamlRoot().");
        }
        return xamlRoot;
    }
}