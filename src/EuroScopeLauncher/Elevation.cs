using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EuroScopeLauncher;

public static class Elevation
{
    public static bool IsAdministrator()
    {
        const uint tokenQuery = 0x0008;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, tokenQuery, out var token)) return false;
        try
        {
            return GetTokenInformation(token, TokenInformationClass.TokenElevation, out TokenElevation elevation, Marshal.SizeOf<TokenElevation>(), out _) && elevation.TokenIsElevated != 0;
        }
        finally { CloseHandle(token); }
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

    private enum TokenInformationClass { TokenElevation = 20 }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation { public int TokenIsElevated; }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, TokenInformationClass informationClass, out TokenElevation tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
