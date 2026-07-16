using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

/// <summary>
/// WinUI 3 unpackaged entry point.
/// </summary>
public static class Program {
    [global::System.Runtime.InteropServices.DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    /// <summary>
    /// Application main entry point.
    /// </summary>
    [global::System.STAThread]
    public static void Main(string[] args) {
        StartupLog.Write("Program.Main enter");
        XamlCheckProcessRequirements();
        StartupLog.Write("XamlCheckProcessRequirements ok");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        StartupLog.Write("ComWrappersSupport.InitializeComWrappers ok");

        Application.Start(p => {
            StartupLog.Write("Application.Start callback");
            var dispatcher = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("The WinUI dispatcher queue is unavailable.");
            SynchronizationContext.SetSynchronizationContext(new WinUiDispatcherSynchronizationContext(dispatcher));
            StartupLog.Write("WinUI synchronization context installed");
            _ = new App();
            StartupLog.Write("App constructed");
        });
    }
}
