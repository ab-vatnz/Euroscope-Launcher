using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;

namespace EuroScopeLauncher;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AiracService _airac = new();
    private LauncherSettings _settings = new();
    private PluginCatalog? _catalog;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        EuroScopePathBox.Text = _settings.EuroScopeExePath;
        UpdateAiracStatus();
        AppUpdateText.Text = "Checks GitHub Releases for a newer launcher version. Updates are never installed without your confirmation.";
        try { await CheckAppUpdateAsync(silent: true); } catch { /* A network error must not block controller setup. */ }
    }

    private void UpdateAiracStatus()
    {
        var airac = AiracService.GetAiracDirectory(_settings.EuroScopeExePath);
        var legacy = _airac.FindLegacySkylineFolders(_settings.EuroScopeExePath);
        AiracStatusText.Text = $"Managed AIRAC folder: {airac}\nInstalled AIRAC: {_settings.AiracVersion ?? "not installed"}." +
            (legacy.Count > 0 ? $"\nLegacy SkyLine package found: {string.Join(", ", legacy.Select(Path.GetFileName))}. Back it up, then move any controller-specific files you want to keep into AIRAC. It will not be removed automatically." : "");
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "EuroScope.exe|EuroScope.exe|Programs (*.exe)|*.exe", FileName = "EuroScope.exe" };
        if (dialog.ShowDialog() != true) return;
        _settings.EuroScopeExePath = dialog.FileName;
        EuroScopePathBox.Text = dialog.FileName;
        await _settingsStore.SaveAsync(_settings);
        UpdateAiracStatus();
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists()) return;
        var airacDirectory = AiracService.GetAiracDirectory(_settings.EuroScopeExePath);
        if (Directory.Exists(airacDirectory) && Directory.EnumerateFileSystemEntries(airacDirectory).Any())
        {
            await RunAsync("Updating migrated AIRAC files…", async () =>
            {
                var vatnz = new VatnzService(_http);
                var package = await vatnz.GetCurrentPackageAsync(skyline: false);
                var zip = await vatnz.DownloadAsync(package.DownloadUri);
                try { await _airac.UpdateSectorFilesAsync(zip, airacDirectory, package, _settings); }
                finally { File.Delete(zip); }
                await _settingsStore.SaveAsync(_settings);
                UpdateAiracStatus();
                MessageBox.Show("Your existing AIRAC content was preserved. The current SCT2, ESE and RWY files have been installed. Select the SCT2 file from AIRAC in EuroScope.", "Migration update complete", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            return;
        }
        var profile = MessageBox.Show("Copy the packaged VATNZ.prf into AIRAC?\n\nChoose No to keep controller profiles entirely untouched. This choice is only offered during first-time setup; the launcher will never update PRF files later.", "VATNZ.prf", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        await RunAsync("Downloading SkyLine package…", async () =>
        {
            var vatnz = new VatnzService(_http);
            var package = await vatnz.GetCurrentPackageAsync(skyline: true);
            var zip = await vatnz.DownloadAsync(package.DownloadUri);
            try { await _airac.InstallSkylineAsync(zip, airacDirectory, profile, package, _settings); }
            finally { File.Delete(zip); }
            await _settingsStore.SaveAsync(_settings);
            UpdateAiracStatus();
            MessageBox.Show("SkyLine setup is complete. Before launching EuroScope, select the SCT2 file in the AIRAC folder.", "Setup complete", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void UpdateAirac_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists()) return;
        await RunAsync("Checking VATNZ sector files…", async () =>
        {
            var vatnz = new VatnzService(_http);
            var package = await vatnz.GetCurrentPackageAsync(skyline: false);
            if (package.Version.Equals(_settings.AiracVersion, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show($"AIRAC {package.Version} is already installed.", "AIRAC is current", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (MessageBox.Show($"Update AIRAC from {_settings.AiracVersion ?? "none"} to {package.Version}? Only SCT2, ESE and RWY files will be changed.", "AIRAC update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            var zip = await vatnz.DownloadAsync(package.DownloadUri);
            try { await _airac.UpdateSectorFilesAsync(zip, AiracService.GetAiracDirectory(_settings.EuroScopeExePath), package, _settings); }
            finally { File.Delete(zip); }
            await _settingsStore.SaveAsync(_settings);
            UpdateAiracStatus();
            MessageBox.Show("AIRAC updated. Select the new SCT2 file from AIRAC in EuroScope before launching.", "AIRAC updated", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void RefreshPlugins_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync("Loading plugin catalog…", async () =>
        {
            var github = new GitHubService(_http);
            _catalog = await new PluginService(_http, github).GetCatalogAsync();
            var rows = new List<PluginRow>();
            foreach (var plugin in _catalog.Plugins)
            {
                _settings.InstalledPlugins.TryGetValue(plugin.Id, out var installed);
                string latest;
                try { latest = (await github.GetLatestReleaseAsync(plugin.ReleaseApiUrl)).TagName; }
                catch { latest = "Unable to check"; }
                rows.Add(new PluginRow
                {
                    Definition = plugin,
                    InstalledVersion = installed ?? "Not installed",
                    LatestVersion = latest,
                    InstallAction = installed is null ? "Install" : "Update"
                });
            }
            PluginList.ItemsSource = rows;
        });
    }

    private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists() || ((sender as FrameworkElement)?.Tag is not PluginRow row)) return;
        var plugin = row.Definition;
        await RunAsync($"Installing {plugin.DisplayName}…", async () =>
        {
            var dll = await new PluginService(_http, new GitHubService(_http)).InstallAsync(plugin, AiracService.GetAiracDirectory(_settings.EuroScopeExePath), _settings);
            await _settingsStore.SaveAsync(_settings);
            MessageBox.Show($"{plugin.DisplayName} is installed.\n\nOpen EuroScope → Other SET → Plug-ins, load and enable:\n{dll}\n\n{plugin.PostInstallInstructions}", "Enable plugin", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void UninstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists() || ((sender as FrameworkElement)?.Tag is not PluginRow row)) return;
        var plugin = row.Definition;
        if (MessageBox.Show($"Uninstall {plugin.DisplayName}? This removes only AIRAC\\Plugins\\{plugin.DestinationFolder}.", "Uninstall plugin", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunAsync($"Uninstalling {plugin.DisplayName}…", async () =>
        {
            new PluginService(_http, new GitHubService(_http)).Uninstall(plugin, AiracService.GetAiracDirectory(_settings.EuroScopeExePath), _settings);
            await _settingsStore.SaveAsync(_settings);
            await RefreshPluginRowsAsync();
        });
    }

    private async Task RefreshPluginRowsAsync()
    {
        if (_catalog is null) return;
        var github = new GitHubService(_http);
        var rows = new List<PluginRow>();
        foreach (var plugin in _catalog.Plugins)
        {
            _settings.InstalledPlugins.TryGetValue(plugin.Id, out var installed);
            string latest;
            try { latest = (await github.GetLatestReleaseAsync(plugin.ReleaseApiUrl)).TagName; } catch { latest = "Unable to check"; }
            rows.Add(new PluginRow { Definition = plugin, InstalledVersion = installed ?? "Not installed", LatestVersion = latest, InstallAction = installed is null ? "Install" : "Update" });
        }
        PluginList.ItemsSource = rows;
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await CheckAppUpdateAsync(silent: false);

    private async Task CheckAppUpdateAsync(bool silent)
    {
        await RunAsync("Checking launcher updates…", async () =>
        {
            var release = await new GitHubService(_http).GetLatestReleaseAsync("https://api.github.com/repos/ab-vatnz/EuroScopeLauncher/releases/latest");
            var current = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            if (release.TagName.TrimStart('v').Equals(current, StringComparison.OrdinalIgnoreCase)) { if (!silent) MessageBox.Show("The launcher is current."); return; }
            AppUpdateText.Text = $"Update available: {release.Name}\n\n{release.Body}";
            if (MessageBox.Show($"Launcher update {release.Name} is available. Download its installer now?", "Launcher update", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                var installer = release.Assets.FirstOrDefault(a => a.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase));
                if (installer is null) throw new InvalidOperationException("The release does not include a Setup installer.");
                Process.Start(new ProcessStartInfo(installer.DownloadUri.ToString()) { UseShellExecute = true });
            }
        });
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists()) return;
        ProcessLauncher.LaunchEuroScope(_settings.EuroScopeExePath);
    }

    private bool EnsureEuroScopeExists()
    {
        if (File.Exists(_settings.EuroScopeExePath)) return true;
        MessageBox.Show("Select a valid EuroScope.exe before continuing.", "EuroScope not found", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private async Task RunAsync(string status, Func<Task> action)
    {
        StatusText.Text = status;
        Mouse.OverrideCursor = Cursors.Wait;
        try { await action(); StatusText.Text = "Ready."; }
        catch (UnauthorizedAccessException) { StatusText.Text = "Administrator permission is required to write under Program Files."; MessageBox.Show("Windows denied access to the EuroScope folder. Run EuroScope Launcher as administrator and try again.", "Permission required", MessageBoxButton.OK, MessageBoxImage.Warning); }
        catch (Exception ex) { StatusText.Text = "Operation failed."; MessageBox.Show(ex.Message, "EuroScope Launcher", MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { Mouse.OverrideCursor = null; }
    }
}
