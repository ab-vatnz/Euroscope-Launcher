using System.Diagnostics;
using System.Security.Principal;

namespace EuroScopeLauncher;

public static class Elevation
{
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool RestartForWrite(Window owner)
    {
        if (IsAdministrator()) return true;
        if (MessageBox.Show(owner, "This action changes files under Program Files and requires administrator permission. Continue?", "Administrator permission required", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return false;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(owner, "Administrator permission was not granted. No files were changed.", "EuroScope Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
