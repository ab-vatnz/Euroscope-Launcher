using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace EuroScopeLauncher;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new();
    private readonly SettingsStore _settingsStore = new();
    private readonly AiracService _airac = new();
    private readonly DispatcherTimer _pluginRefreshTimer = new() { Interval = TimeSpan.FromMinutes(15) };
    private LauncherSettings _settings = new();
    private PluginCatalog? _catalog;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitializeAsync();
        _pluginRefreshTimer.Tick += async (_, _) => await LoadPluginsAsync(showErrors: false);
    }

    private async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        EuroScopePathBox.Text = _settings.EuroScopeExePath;
        LoadEuroScopeIcon();
        VersionText.Text = $"Version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}";
        UpdateAiracStatus();
        if (!_settings.SetupWizardCompleted)
        {
            var wizard = new SetupWizard(_settings.EuroScopeExePath) { Owner = this };
            if (wizard.ShowDialog() == true)
            {
                _settings.EuroScopeExePath = wizard.EuroScopePath;
                _settings.SetupWizardCompleted = true;
                EuroScopePathBox.Text = wizard.EuroScopePath;
                await _settingsStore.SaveAsync(_settings);
                UpdateAiracStatus();
            }
        }
        await LoadPluginsAsync(showErrors: false);
        await RefreshProfileAvailabilityAsync();
        _pluginRefreshTimer.Start();
        try { await CheckAppUpdateAsync(silent: true); } catch { /* A network error must not block controller setup. */ }
    }

    private void LoadEuroScopeIcon()
    {
        if (!File.Exists(_settings.EuroScopeExePath)) return;
        var largeIcons = new[] { IntPtr.Zero };
        if (ExtractIconEx(_settings.EuroScopeExePath, 0, largeIcons, null, 1) == 0 || largeIcons[0] == IntPtr.Zero) return;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(largeIcons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            Icon = image;
            EuroScopeLogo.Source = image;
        }
        finally { DestroyIcon(largeIcons[0]); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string fileName, int iconIndex, IntPtr[]? largeIcons, IntPtr[]? smallIcons, uint iconCount);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);

    private void UpdateAiracStatus()
    {
        var airac = AiracService.GetAiracDirectory(_settings.EuroScopeExePath);
        var legacy = _airac.FindLegacySkylineFolders(_settings.EuroScopeExePath);
        AiracStatusText.Text = $"Managed AIRAC folder: {airac}\nInstalled AIRAC: {_settings.AiracVersion ?? "not installed"}." +
            (legacy.Count > 0 ? $"\nLegacy SkyLine package found: {string.Join(", ", legacy.Select(Path.GetFileName))}. Back it up, then move its Settings folder and any other controller-specific files you want to keep into AIRAC before setup. The launcher will never replace or update AIRAC\\Settings, and it will not remove the old package." : "");
        var version = _settings.AiracVersion ?? "Not installed";
        HomeAiracText.Text = $"AIRAC {version}";
        ProfileVersionText.Text = $"Installed\n{version}";
    }

    private async void Nav_Click(object sender, RoutedEventArgs e)
    {
        var page = (sender as FrameworkElement)?.Tag?.ToString();
        HomeView.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        ProfilesView.Visibility = page == "Profiles" ? Visibility.Visible : Visibility.Collapsed;
        PluginsView.Visibility = page == "Plugins" ? Visibility.Visible : Visibility.Collapsed;
        SetupView.Visibility = page == "Setup" ? Visibility.Visible : Visibility.Collapsed;
        if (page == "Plugins") await LoadPluginsAsync(showErrors: false);
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "EuroScope.exe|EuroScope.exe|Programs (*.exe)|*.exe", FileName = "EuroScope.exe" };
        if (dialog.ShowDialog() != true) return;
        _settings.EuroScopeExePath = dialog.FileName;
        EuroScopePathBox.Text = dialog.FileName;
        LoadEuroScopeIcon();
        await _settingsStore.SaveAsync(_settings);
        UpdateAiracStatus();
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists()) return;
        var airacDirectory = AiracService.GetAiracDirectory(_settings.EuroScopeExePath);
        var legacyFolders = _airac.FindLegacySkylineFolders(_settings.EuroScopeExePath);
        if (!Directory.Exists(airacDirectory) && legacyFolders.Count > 0)
        {
            await RunAsync("Preparing AIRAC migration folder…", () =>
            {
                Directory.CreateDirectory(airacDirectory);
                MessageBox.Show($"AIRAC has been created at:\n{airacDirectory}\n\nBefore continuing, back up your old SkyLine package and move its Settings folder plus any controller-specific files you want to retain into AIRAC. Click First-time SkyLine setup again afterwards; the launcher will preserve those files and only install current sector files.", "Move your controller settings", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateAiracStatus();
                return Task.CompletedTask;
            });
            return;
        }
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
            await RefreshProfileAvailabilityAsync();
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
            await RefreshProfileAvailabilityAsync();
            MessageBox.Show("AIRAC updated. Select the new SCT2 file from AIRAC in EuroScope before launching.", "AIRAC updated", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void RefreshPlugins_Click(object sender, RoutedEventArgs e)
    {
        await LoadPluginsAsync(showErrors: true);
    }

    private async void InstallPlugin_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureEuroScopeExists() || ((sender as FrameworkElement)?.Tag is not PluginRow row)) return;
        var plugin = row.Definition;
        await RunAsync($"Installing {plugin.DisplayName}…", async () =>
        {
            var dll = await new PluginService(_http, new GitHubService(_http)).InstallAsync(plugin, AiracService.GetAiracDirectory(_settings.EuroScopeExePath), _settings);
            await _settingsStore.SaveAsync(_settings);
            await RefreshPluginRowsAsync();
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
            rows.Add(new PluginRow
            {
                Definition = plugin,
                InstalledVersion = installed ?? "Not installed",
                LatestVersion = latest,
                InstallAction = installed is null ? "Install" : installed.Equals(latest, StringComparison.OrdinalIgnoreCase) ? "Current" : "Update"
            });
        }
        PluginList.ItemsSource = rows;
    }

    private async Task LoadPluginsAsync(bool showErrors)
    {
        try
        {
            StatusText.Text = "Refreshing plugins…";
            var github = new GitHubService(_http);
            _catalog = await new PluginService(_http, github).GetCatalogAsync();
            await RefreshPluginRowsAsync();
            StatusText.Text = "Ready.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Plugin refresh unavailable.";
            if (showErrors) MessageBox.Show(ex.Message, "Plugin refresh", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshProfileAvailabilityAsync()
    {
        try
        {
            var latest = await new VatnzService(_http).GetCurrentPackageAsync(skyline: false);
            var installed = _settings.AiracVersion;
            ProfileVersionText.Text = $"Installed\n{installed ?? "None"}\nLatest\n{latest.Version}";
            ProfileUpdateButton.IsEnabled = installed is null || !installed.Equals(latest.Version, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // A failed version check must never prevent a manual update.
            ProfileUpdateButton.IsEnabled = true;
        }
    }

    private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e) => await CheckAppUpdateAsync(silent: false);

    private async Task CheckAppUpdateAsync(bool silent)
    {
        await RunAsync("Checking launcher updates…", async () =>
        {
            GitHubRelease release;
            try
            {
                release = await new GitHubService(_http).GetLatestReleaseAsync("https://api.github.com/repos/ab-vatnz/Euroscope-Launcher/releases/latest");
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                if (!silent) MessageBox.Show("No launcher release has been published yet.", "Launcher updates", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var current = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            if (release.TagName.TrimStart('v').Equals(current, StringComparison.OrdinalIgnoreCase)) { if (!silent) MessageBox.Show("The launcher is current."); return; }
            StatusText.Text = $"Launcher update available: {release.Name}";
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
