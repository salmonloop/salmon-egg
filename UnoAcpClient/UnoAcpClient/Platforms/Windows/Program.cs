using System;
using Microsoft.UI.Xaml;

namespace UnoAcpClient;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ => new App());
    }
}

