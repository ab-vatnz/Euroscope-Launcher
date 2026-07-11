using Microsoft.Win32;

namespace EuroScopeLauncher;

public partial class SetupWizard : Window
{
    private readonly AiracService _airac = new();
    private int _step;
    public string EuroScopePath { get; private set; }

    public SetupWizard(string euroScopePath)
    {
        EuroScopePath = euroScopePath;
        InitializeComponent();
        PathBox.Text = euroScopePath;
        ShowStep();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "EuroScope.exe|EuroScope.exe|Programs (*.exe)|*.exe", FileName = "EuroScope.exe" };
        if (dialog.ShowDialog() != true) return;
        EuroScopePath = dialog.FileName;
        PathBox.Text = EuroScopePath;
        ShowStep();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            EuroScopePath = PathBox.Text;
            if (!File.Exists(EuroScopePath)) { MessageBox.Show("Select a valid EuroScope.exe before continuing."); return; }
            Directory.CreateDirectory(AiracService.GetAiracDirectory(EuroScopePath));
        }
        if (_step == 2)
        {
            var answer = MessageBox.Show(
                "Have you copied your old Settings folder, VATNZ.prf (if used), and any personal files into AIRAC?\n\nChoosing No keeps you on this page so you can check first. EuroScope Launcher never deletes your old SkyLine folder or personal files.",
                "Confirm controller settings migration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }
        if (_step == 3) { DialogResult = true; return; }
        _step++;
        ShowStep();
    }

    private void ShowStep()
    {
        WelcomePage.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetectPage.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        MigratePage.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ReadyPage.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        StepText.Text = $"Step {_step + 1} of 4";
        NextButton.Content = _step switch { 0 => "Get started", 1 => "Continue", 2 => "I’ve moved my files", _ => "Open launcher" };
        var legacy = _airac.FindLegacySkylineFolders(EuroScopePath);
        DetectionText.Text = legacy.Count > 0
            ? $"Existing SkyLine package detected: {string.Join(", ", legacy.Select(Path.GetFileName))}."
            : "No existing SkyLine package was detected. A new AIRAC folder will be created.";
        MigrationText.Text = $"AIRAC will be created here:\n{AiracService.GetAiracDirectory(EuroScopePath)}\n\nIf you have an old SkyLine package, copy its Settings folder, VATNZ.prf (if you use one), and any other personal files into AIRAC now. Then continue.";
    }
}
