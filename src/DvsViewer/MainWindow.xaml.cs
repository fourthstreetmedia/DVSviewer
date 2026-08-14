using System.IO;
using System.Windows;
using System.Windows.Controls;
using DvsViewer.Core;

namespace DvsViewer;

public partial class MainWindow : Window
{
    private readonly Ffmpeg? _ffmpeg;
    private DvsFile? _file;
    private readonly List<TrackEntry> _tracks = new();

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? fileToOpen)
    {
        InitializeComponent();
        _ffmpeg = Ffmpeg.Find();

        if (!string.IsNullOrWhiteSpace(fileToOpen))
        {
            if (File.Exists(fileToOpen))
                _ = LoadFileAsync(fileToOpen);
            else
                FileLabel.Text = $"file not found: {fileToOpen}";
        }
    }

    

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open DVSS recording",
            Filter = "DVSS recordings (*.dvs)|*.dvs|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        await LoadFileAsync(dlg.FileName);
    }

    

    private void OpenButton_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDvsFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OpenButton_Drop(object sender, DragEventArgs e)
    {
        if (GetFirstDvs(e.Data) is { } path)
            _ = LoadFileAsync(path);
        e.Handled = true;
    }

    private static bool HasDvsFile(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
        return data.GetData(DataFormats.FileDrop) is string[] files &&
               files.Any(f => f.EndsWith(".dvs", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetFirstDvs(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] files) return null;
        return files.FirstOrDefault(f => f.EndsWith(".dvs", StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadFileAsync(string path)
    {
        SetBusy(true);
        LogLine($"Parsing {Path.GetFileName(path)}...");
        try
        {
            var file = await Task.Run(() => DvsParser.Parse(path,
                msg => Dispatcher.BeginInvoke(() => LogLine(msg))));
            _file = file;
            Title = $"DvsViewer - {Path.GetFileName(path)}";
            FileLabel.Text = Path.GetFullPath(path);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
                OutDirBox.Text = dir;
            BuildTracks(file);
            UpdateInfo(file);
            LogLine($"Parsed: {file.Frames.Count} frames, {file.Gps.Count} GPS fixes, " +
                    $"{file.AudioSources.Count} audio sources.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Failed to open file", MessageBoxButton.OK, MessageBoxImage.Error);
            LogLine($"Open failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
            UpdateExportEnabled();
        }
    }

    private void UpdateInfo(DvsFile file)
    {
        var time = file.StartUtc is { } s && file.EndUtc is { } e
            ? $"{DvsParser.DtToStr(s)} -> {DvsParser.DtToStr(e)}"
            : "(unknown)";
        InfoText.Text = string.Join("  |  ",
            $"Vehicle: {(file.Vehicle.Length > 0 ? file.Vehicle : "unknown")}",
            $"Time: {time}",
            $"GPS fixes: {file.Gps.Count}",
            $"Video frames: {file.Frames.Count}",
            $"Cameras: {file.Channels.Count}",
            $"Audio sources: {file.AudioSources.Count}");
    }

    private void BuildTracks(DvsFile file)
    {
        TracksPanel.Children.Clear();
        _tracks.Clear();

        var audioLinkedChannels = file.AudioSources
            .Where(s => file.Channels.Any(c => string.Equals(c.Name, s.Label, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(s => s.Label, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var ch in file.Channels)
        {
            var audioSrc = ch.AudioSrc is { } src
                ? file.AudioSources.FirstOrDefault(s => s.Src == src)
                : (audioLinkedChannels.TryGetValue(ch.Name, out var matched) ? matched : null);
            var label = $"{ch.Name}  (video, {ch.Frames.Count} frames)";
            if (audioSrc is not null) label += $"  |  audio: {audioSrc.PacketCount} packets";
            AddTrack(label, isVideo: true, channelName: ch.Name, src: audioSrc?.Src);
        }

        var usedNames = new HashSet<string>(file.Channels.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var src in file.AudioSources)
        {
            if (usedNames.Contains(src.Label)) continue;
            AddTrack($"{src.Label}  (audio only, {src.Packets.Count} packets)", isVideo: false, channelName: null, src: src.Src);
        }

        if (_tracks.Count == 0)
            AddTrack("(no video or audio tracks found)", isVideo: false, channelName: null, src: null);
    }

    private void AddTrack(string label, bool isVideo, string? channelName, int? src)
    {
        var entry = new TrackEntry { IsVideo = isVideo, ChannelName = channelName, Src = src };
        var box = new CheckBox
        {
            Content = label,
            IsChecked = true,
            Margin = new Thickness(0, 2, 0, 2),
            Tag = entry,
        };
        box.Checked += (_, _) => entry.Selected = true;
        box.Unchecked += (_, _) => entry.Selected = false;
        _tracks.Add(entry);
        TracksPanel.Children.Add(box);
    }

    

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SetAllChecked(true);

    private void SelectNone_Click(object sender, RoutedEventArgs e) => SetAllChecked(false);

    private void SetAllChecked(bool value)
    {
        foreach (var child in TracksPanel.Children)
            if (child is CheckBox box) box.IsChecked = value;
    }

    

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose output folder" };
        if (dlg.ShowDialog(this) != true) return;
        OutDirBox.Text = dlg.FolderName;
    }

    private void OutDirBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateExportEnabled();

    private void UpdateExportEnabled()
        => ExportButton.IsEnabled = _file is not null && OutDirBox.Text.Trim().Length > 0;

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_file is null) return;

        var outDir = OutDirBox.Text.Trim();
        if (outDir.Length == 0)
        {
            MessageBox.Show(this, "Choose an output folder first.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedVideo = SelectedVideoChannels();
        var selectedSrcs = SelectedAudioSrcs();
        var wantVideo = OptVideo.IsChecked == true;
        var wantAudio = OptAudio.IsChecked == true;
        var wantMux = OptMux.IsChecked == true;
        var wantGps = OptGps.IsChecked == true;

        var doVideo = wantVideo && selectedVideo.Count > 0;
        var doAudio = (wantAudio || (wantMux && selectedVideo.Count > 0)) && selectedSrcs.Count > 0;
        var doGps = wantGps && _file.Gps.Count > 0;

        if (!doVideo && !doAudio && !doGps)
        {
            MessageBox.Show(this, "Nothing is selected to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var opt = new ExportOptions
        {
            Video = doVideo,
            Audio = doAudio,
            Gps = doGps,
            Frames = false,
            Report = false,
            Mux = wantMux,
            WriteRawBlocks = false,
        };
        if (selectedVideo.Count > 0)
            opt.Channels = selectedVideo.Count < _file.Channels.Count ? selectedVideo : null;
        if (selectedSrcs.Count > 0)
            opt.Srcs = selectedSrcs;

        var file = _file;
        var ff = _ffmpeg;

        SetBusy(true);
        ProgressBar.IsIndeterminate = true;
        LogBox.Clear();
        LogLine($"Exporting to {outDir}...");
        try
        {
            var result = await Task.Run(() =>
                new ExportService(ff).ExportEverything(file, outDir, opt,
                    msg => Dispatcher.BeginInvoke(() => LogLine(msg))));

            LogLine($"Done. {result.Files.Count} files written.");
            foreach (var (src, ch, muxed, reason) in result.Mux)
                LogLine($"  mux src{src} -> {ch}: {(muxed ? "ok" : $"skipped ({reason})")}");
            MessageBox.Show(this, $"Exported {result.Files.Count} files to\n{outDir}", "Export complete");
        }
        catch (Exception ex)
        {
            LogLine($"Export failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ProgressBar.IsIndeterminate = false;
            SetBusy(false);
        }
    }

    private List<string> SelectedVideoChannels()
        => _tracks.Where(t => t.IsVideo && t.Selected && t.ChannelName is not null)
                  .Select(t => t.ChannelName!)
                  .ToList();

    private List<int> SelectedAudioSrcs()
    {
        var srcs = new List<int>();
        foreach (var t in _tracks)
        {
            if (!t.Selected || t.Src is not { } src) continue;
            if (!srcs.Contains(src)) srcs.Add(src);
        }
        return srcs;
    }

    

    private void LogLine(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private void SetBusy(bool busy)
    {
        OpenButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _file is not null && OutDirBox.Text.Trim().Length > 0;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }
}

internal sealed class TrackEntry
{
    public bool IsVideo { get; init; }
    public string? ChannelName { get; init; }
    public int? Src { get; init; }
    public bool Selected { get; set; } = true;
}