using System.Text.Json;

namespace EuroScopeLauncher;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EuroScopeLauncher", "settings.json");

    public async Task<LauncherSettings> LoadAsync()
    {
        if (!File.Exists(_path)) return new LauncherSettings();
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions) ?? new LauncherSettings();
    }

    public async Task SaveAsync(LauncherSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".new";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _path, true);
    }
}
