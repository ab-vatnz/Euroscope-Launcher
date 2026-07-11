using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EuroScopeLauncher;

public sealed class VatnzService(HttpClient http)
{
    public const string SectorFilesPage = "https://www.vatnz.net/airspace/sector_files/";

    public async Task<VatnzPackage> GetCurrentPackageAsync(bool skyline, CancellationToken cancellationToken = default)
    {
        var html = await http.GetStringAsync(SectorFilesPage, cancellationToken);
        var section = skyline ? "SkyLine Package for EuroScope" : "Sector Files for EuroScope";
        var start = html.IndexOf(section, StringComparison.OrdinalIgnoreCase);
        if (start < 0) throw new InvalidOperationException($"VATNZ page does not contain the {section} section.");
        var nextSection = html.IndexOf("<h2", start + section.Length, StringComparison.OrdinalIgnoreCase);
        var fragment = html[start..(nextSection < 0 ? html.Length : nextSection)];
        var packageName = skyline ? "VATNZ-SKYLINE" : "VATNZ-NZZC";
        var match = Regex.Match(fragment, $"href=[\"'](?<url>[^\"']*{packageName}_(?<version>[0-9a-zA-Z]+)\\.zip)[\"']", RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException($"VATNZ did not publish a current {packageName} ZIP.");
        var url = System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value);
        return new VatnzPackage(match.Groups["version"].Value, new Uri(new Uri(SectorFilesPage), url), skyline);
    }

    public async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Path.GetTempPath(), $"EuroScopeLauncher-{Guid.NewGuid():N}.zip");
        await using var input = await http.GetStreamAsync(uri, cancellationToken);
        await using var output = File.Create(path);
        await input.CopyToAsync(output, cancellationToken);
        return path;
    }
}

public sealed class AiracService
{
    public static string GetAiracDirectory(string euroScopeExe) => Path.Combine(Path.GetDirectoryName(euroScopeExe) ?? throw new ArgumentException("EuroScope executable has no folder."), "AIRAC");

    public IReadOnlyList<string> FindLegacySkylineFolders(string euroScopeExe)
    {
        var basePath = Path.GetDirectoryName(euroScopeExe) ?? "";
        return Directory.Exists(basePath)
            ? Directory.EnumerateDirectories(basePath, "VATNZ-SKYLINE_*", SearchOption.TopDirectoryOnly).ToList()
            : [];
    }

    public async Task InstallSkylineAsync(string zipPath, string airacDirectory, bool includeProfile, VatnzPackage package, LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var stage = Path.Combine(Path.GetTempPath(), "EuroScopeLauncher", Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zipPath, stage);
            var source = LocatePackageRoot(stage);
            if (!Directory.EnumerateFiles(source, "*.sct2", SearchOption.TopDirectoryOnly).Any() ||
                !Directory.EnumerateFiles(source, "*.ese", SearchOption.TopDirectoryOnly).Any() ||
                !Directory.EnumerateFiles(source, "*.rwy", SearchOption.TopDirectoryOnly).Any())
                throw new InvalidDataException("The SkyLine ZIP did not contain SCT2, ESE, and RWY files.");
            Directory.CreateDirectory(airacDirectory);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file);
                if (!includeProfile && Path.GetExtension(file).Equals(".prf", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Path.Combine(airacDirectory, relative);
                // A controller's Settings folder is never replaced once it exists.
                if (relative.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
                    relative.StartsWith("Settings" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var existingSettings = Path.Combine(airacDirectory, "Settings");
                    if (Directory.Exists(existingSettings)) continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, true);
            }
            await RecordInstallAsync(airacDirectory, package, settings, cancellationToken);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    public async Task UpdateSectorFilesAsync(string zipPath, string airacDirectory, VatnzPackage package, LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(airacDirectory);
        using var archive = ZipFile.OpenRead(zipPath);
        var required = new[] { ".sct2", ".ese", ".rwy" };
        var entries = archive.Entries.Where(e => required.Contains(Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase)).ToList();
        if (required.Any(ext => entries.All(e => !Path.GetExtension(e.Name).Equals(ext, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("The sector ZIP must contain SCT2, ESE, and RWY files.");
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(airacDirectory, Path.GetFileName(entry.Name));
            var temp = target + ".download";
            await using (var source = entry.Open())
            await using (var destination = File.Create(temp)) await source.CopyToAsync(destination, cancellationToken);
            File.Move(temp, target, true);
        }
        await RecordInstallAsync(airacDirectory, package, settings, cancellationToken);
    }

    private static string LocatePackageRoot(string stage)
    {
        var directories = Directory.GetDirectories(stage);
        return directories.Length == 1 && !Directory.EnumerateFiles(stage).Any() ? directories[0] : stage;
    }

    private static async Task RecordInstallAsync(string airacDirectory, VatnzPackage package, LauncherSettings settings, CancellationToken cancellationToken)
    {
        settings.AiracVersion = package.Version;
        settings.AiracSourceUrl = package.DownloadUri.ToString();
        settings.AiracInstalledAt = DateTimeOffset.UtcNow;
        var marker = $"EuroScope Launcher AIRAC\r\nVersion: {package.Version}\r\nSource: {package.DownloadUri}\r\nInstalled UTC: {settings.AiracInstalledAt:O}\r\nManaged files: *.sct2, *.ese, *.rwy\r\nProfiles (.prf): never updated after first setup\r\n";
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(airacDirectory)!, "euroscopelauncher-airac.txt"), marker, cancellationToken);
    }
}

public sealed class GitHubService(HttpClient http)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public async Task<GitHubRelease> GetLatestReleaseAsync(string apiUrl, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EuroScopeLauncher", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, Options, cancellationToken) ?? throw new InvalidDataException("Invalid GitHub release response.");
        return new GitHubRelease(release.TagName, release.Name ?? release.TagName, release.Body ?? "", release.Assets.Select(a => new GitHubAsset(a.Name, new Uri(a.BrowserDownloadUrl))).ToList());
    }

    private sealed class GitHubReleaseDto { public string TagName { get; set; } = ""; public string? Name { get; set; } public string? Body { get; set; } public List<AssetDto> Assets { get; set; } = []; }
    private sealed class AssetDto { public string Name { get; set; } = ""; public string BrowserDownloadUrl { get; set; } = ""; }
}

public sealed class PluginService(HttpClient http, GitHubService github)
{
    public const string CatalogUrl = "https://raw.githubusercontent.com/ab-vatnz/Euroscope-Launcher/main/plugin-catalog.json";
    public async Task<PluginCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await http.GetStringAsync(CatalogUrl, cancellationToken);
            return DeserializeCatalog(json);
        }
        catch (HttpRequestException)
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "plugin-catalog.json");
            if (!File.Exists(bundled)) throw;
            return DeserializeCatalog(await File.ReadAllTextAsync(bundled, cancellationToken));
        }
    }

    public async Task<string> InstallAsync(PluginDefinition plugin, string airacDirectory, LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        var release = await github.GetLatestReleaseAsync(plugin.ReleaseApiUrl, cancellationToken);
        var asset = release.Assets.FirstOrDefault(a => Regex.IsMatch(a.Name, plugin.AssetNamePattern, RegexOptions.IgnoreCase)) ?? throw new InvalidOperationException($"No release asset matches {plugin.AssetNamePattern}.");
        var zipPath = Path.Combine(Path.GetTempPath(), $"EuroScopeLauncher-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var input = await http.GetStreamAsync(asset.DownloadUri, cancellationToken))
            await using (var output = File.Create(zipPath)) await input.CopyToAsync(output, cancellationToken);
            var destination = GetPluginDestination(airacDirectory, plugin);
            Directory.CreateDirectory(destination);
            ZipFile.ExtractToDirectory(zipPath, destination, true);
            var dll = Directory.EnumerateFiles(destination, plugin.PrimaryDll, SearchOption.AllDirectories).FirstOrDefault() ?? throw new InvalidDataException($"The plugin ZIP did not contain {plugin.PrimaryDll}.");
            settings.InstalledPlugins[plugin.Id] = release.TagName;
            return dll;
        }
        finally { if (File.Exists(zipPath)) File.Delete(zipPath); }
    }

    public void Uninstall(PluginDefinition plugin, string airacDirectory, LauncherSettings settings)
    {
        var destination = GetPluginDestination(airacDirectory, plugin);
        if (Directory.Exists(destination)) Directory.Delete(destination, true);
        settings.InstalledPlugins.Remove(plugin.Id);
    }

    public static string GetPluginDestination(string airacDirectory, PluginDefinition plugin)
    {
        var folder = Path.GetFileName(plugin.DestinationFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folder) || !folder.Equals(plugin.DestinationFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.Ordinal))
            throw new InvalidDataException($"Plugin {plugin.Id} has an unsafe destination folder.");
        return Path.Combine(airacDirectory, "Plugins", folder);
    }

    private static PluginCatalog DeserializeCatalog(string json) => JsonSerializer.Deserialize<PluginCatalog>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Plugin catalog is invalid.");
}

public static class ProcessLauncher
{
    public static void LaunchEuroScope(string executable) => Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
}
