namespace EuroScopeLauncher;

public sealed class LauncherSettings
{
    public string EuroScopeExePath { get; set; } = @"C:\Program Files (x86)\EuroScope\EuroScope.exe";
    public string? AiracVersion { get; set; }
    public string? AiracSourceUrl { get; set; }
    public DateTimeOffset? AiracInstalledAt { get; set; }
    public bool SetupWizardCompleted { get; set; }
    public Dictionary<string, string> InstalledPlugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record VatnzPackage(string Version, Uri DownloadUri, bool IsSkyline);

public sealed class PluginCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public List<PluginDefinition> Plugins { get; set; } = [];
}

public sealed class PluginDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ReleaseApiUrl { get; set; } = "";
    public string AssetNamePattern { get; set; } = "";
    public string DestinationFolder { get; set; } = "";
    public string PrimaryDll { get; set; } = "";
    public string PostInstallInstructions { get; set; } = "";
    public string VersionStrategy { get; set; } = "github-release-tag";
}

public sealed class PluginRow
{
    public required PluginDefinition Definition { get; init; }
    public string DisplayName => Definition.DisplayName;
    public string PostInstallInstructions => Definition.PostInstallInstructions;
    public string InstalledVersion { get; init; } = "Not installed";
    public string LatestVersion { get; init; } = "Unknown";
    public string InstallAction { get; init; } = "Install";
    public bool IsInstalled => InstalledVersion != "Not installed";
}

public sealed record GitHubRelease(string TagName, string Name, string Body, IReadOnlyList<GitHubAsset> Assets);
public sealed record GitHubAsset(string Name, Uri DownloadUri);
