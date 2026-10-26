using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using ViggiSlideshow.Core;

namespace ViggiSlideshow;

public partial class ConfigWindow : Window
{
    private readonly RegistrySettingsStore _store = new();
    private readonly Settings _settings;

    public ConfigWindow()
    {
        InitializeComponent();
        _settings = Settings.Load(_store);

        FolderBox.Text = _settings.Folder;
        ModeBox.SelectedIndex = (int)_settings.VideoMode;
        OrderBox.SelectedIndex = _settings.Random ? 0 : 1;
        IntervalBox.Text = (_settings.IntervalMs / 1000.0).ToString("0.#", CultureInfo.CurrentCulture);
        VideoMaxBox.Text = _settings.VideoMaxSeconds.ToString(CultureInfo.CurrentCulture);
        FadeBox.Text = _settings.FadeMs.ToString(CultureInfo.CurrentCulture);
        SoundBox.IsChecked = !_settings.Mute;
        FileNameBox.IsChecked = _settings.ShowFileName;
        InfoBox.IsChecked = _settings.ShowInfo;
        KeysText.Text = LabelText.HelpText().Split('\n', 2)[1];
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Folder to show", Multiselect = false };
        if (Directory.Exists(FolderBox.Text))
            dialog.InitialDirectory = FolderBox.Text;
        if (dialog.ShowDialog(this) == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string folder = FolderBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            ShowError($"Can't find the folder {folder}");
            return;
        }
        if (!double.TryParse(IntervalBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double seconds) || seconds < 0.5 || seconds > 3600)
        {
            ShowError("Seconds per photo should be a number from 0.5 to 3600.");
            return;
        }
        if (!int.TryParse(VideoMaxBox.Text, out int videoMax) || videoMax < 5 || videoMax > 3600)
        {
            ShowError("The longest video should be from 5 to 3600 seconds.");
            return;
        }
        if (!int.TryParse(FadeBox.Text, out int fade) || fade < 0 || fade > 5000)
        {
            ShowError("The fade should be from 0 (none) to 5000 milliseconds.");
            return;
        }

        if (!string.Equals(folder, _settings.Folder, StringComparison.OrdinalIgnoreCase))
            _settings.LastFile = null;
        _settings.Folder = folder;
        _settings.VideoMode = (VideoMode)Math.Max(0, ModeBox.SelectedIndex);
        _settings.Random = OrderBox.SelectedIndex != 1;
        _settings.IntervalMs = (int)Math.Round(seconds * 1000);
        _settings.VideoMaxSeconds = videoMax;
        _settings.FadeMs = fade;
        _settings.Mute = SoundBox.IsChecked != true;
        _settings.ShowFileName = FileNameBox.IsChecked == true;
        _settings.ShowInfo = InfoBox.IsChecked == true;
        _settings.Save(_store);
        Close();
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Log.Folder);
            string target = File.Exists(Log.FilePath) ? Log.FilePath : Log.Folder;
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't open the log: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
