using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;
using Zap.Engine;

namespace Zap.App;

public sealed record PathItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path) is { Length: > 0 } n ? n : Path;
    public string Parent => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Glyph => Directory.Exists(Path) ? "" : "";
}

public partial class MainWindow : Window
{
    static readonly (Color A, Color B) CopyAccent = (Rgb(0x22D3EE), Rgb(0x8B5CF6));
    static readonly (Color A, Color B) MoveAccent = (Rgb(0xFBBF24), Rgb(0xF97316));
    static readonly (Color A, Color B) DeleteAccent = (Rgb(0xF472B6), Rgb(0xEF4444));
    static readonly Color Emerald = Rgb(0x34D399), Amber = Rgb(0xFBBF24), Neutral = Rgb(0xAEB9CA);

    enum Outcome { Success, Cancelled, Failed }

    readonly ObservableCollection<PathItem> _copyItems = [], _moveItems = [], _deleteItems = [];
    readonly Effect _ringGlow;
    CancellationTokenSource? _cts;
    bool _spinning;
    string _jobEyebrow = "", _jobHeadline = "";

    public MainWindow(IEnumerable<string> startupPaths)
    {
        InitializeComponent();
        _ringGlow = Ring.Effect;
        _copyItems.CollectionChanged += (_, _) => UpdateItemsState();
        _moveItems.CollectionChanged += (_, _) => UpdateItemsState();
        _deleteItems.CollectionChanged += (_, _) => UpdateItemsState();
        // "Zap.exe <paths>" pre-fills the copy list; "--move <paths>" / "--delete <paths>" the move or delete list.
        var args = startupPaths.ToList();
        var (list, mode) = args.FirstOrDefault() switch
        {
            "--move" => (_moveItems, MoveMode),
            "--delete" => (_deleteItems, DeleteMode),
            _ => (_copyItems, CopyMode),
        };
        foreach (var p in args.Skip(list == _copyItems ? 0 : 1)) AddPath(list, p);
        mode.IsChecked = true; // triggers ApplyMode

        SourceInitialized += (_, _) => UseWin11Frame();
        StateChanged += (_, _) =>
        {
            bool max = WindowState == WindowState.Maximized;
            Root.Margin = new Thickness(max ? 7 : 0); // WindowChrome overhangs the screen when maximized
            MaxButton.Content = max ? "" : "";
        };
    }

    bool DeleteModeOn => DeleteMode.IsChecked == true;
    bool MoveModeOn => MoveMode.IsChecked == true;
    ObservableCollection<PathItem> Items => DeleteModeOn ? _deleteItems : MoveModeOn ? _moveItems : _copyItems;

    // ---------- mode & accent ----------

    void Mode_Checked(object sender, RoutedEventArgs e) => ApplyMode();

    void ApplyMode()
    {
        bool del = DeleteModeOn, move = MoveModeOn;
        SetAccent(del ? DeleteAccent : move ? MoveAccent : CopyAccent);
        TitleText.Text = del ? "Delete files" : move ? "Move files" : "Copy files";
        SubtitleText.Foreground = (Brush)FindResource("MutedBrush");
        SubtitleText.Text = del ? "Parallel deleting, way faster than Explorer. You confirm before anything goes."
            : move ? "Instant on the same drive. Across drives it copies, then removes the originals."
            : "Parallel copying with live progress. Drop files or folders anywhere in the window.";
        EmptyHint.Text = del ? "or pick what you want gone" : move ? "or pick what you want to move" : "or pick what you want to copy";
        CopySide.Visibility = del ? Visibility.Collapsed : Visibility.Visible;
        DeleteSide.Visibility = del ? Visibility.Visible : Visibility.Collapsed;
        StartText.Text = move ? "Start move" : "Start copy";
        FilesCell.Header = move ? "ITEMS" : "FILES";
        ItemsView.ItemsSource = Items;
        UpdateItemsState();
    }

    void SetAccent((Color A, Color B) accent)
    {
        var (a, b) = accent;
        Resources["Accent1"] = a;
        Resources["AccentBrush"] = Frozen(new SolidColorBrush(a));
        Resources["AccentSoft"] = Frozen(new SolidColorBrush(WithAlpha(a, 0x1F)));
        Resources["AccentGradient"] = Frozen(new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1)));
        Resources["AccentFade"] = Frozen(new LinearGradientBrush(WithAlpha(a, 0x59), WithAlpha(a, 0), 90));
        Resources["BlobABrush"] = Frozen(new RadialGradientBrush(WithAlpha(a, 0x4D), WithAlpha(a, 0)));
    }

    void ShowHint(string message)
    {
        SubtitleText.Foreground = new SolidColorBrush(Amber);
        SubtitleText.Text = message;
    }

    // ---------- picking items ----------

    static void AddPath(ObservableCollection<PathItem> list, string path)
    {
        if (!Path.Exists(path)) return;
        path = PathUtil.Normalize(path);
        if (!list.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) list.Add(new PathItem(path));
    }

    void UpdateItemsState()
    {
        int n = Items.Count;
        EmptyState.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        ListState.Visibility = n == 0 ? Visibility.Collapsed : Visibility.Visible;
        ItemsEyebrow.Content = n == 1 ? "1 ITEM" : $"{n} ITEMS";
        SetDropHighlight(false);
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true };
        if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FileNames) AddPath(Items, f);
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Multiselect = true };
        if (dlg.ShowDialog(this) == true) foreach (var f in dlg.FolderNames) AddPath(Items, f);
    }

    void RemoveItem_Click(object sender, RoutedEventArgs e) => Items.Remove((PathItem)((Button)sender).Tag);
    void Clear_Click(object sender, RoutedEventArgs e) => Items.Clear();

    void SetDropHighlight(bool on)
    {
        DropOutline.Stroke = on ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("LineBrightBrush");
        DropOutline.Fill = on ? (Brush)FindResource("AccentSoft") : Brushes.Transparent;
        DropOutline.Opacity = on || Items.Count == 0 ? 1 : 0;
    }

    void Window_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop) && IdleView.Visibility == Visibility.Visible;
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        SetDropHighlight(ok);
        e.Handled = true;
    }

    void Window_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only react when the pointer left the window.
        var pos = e.GetPosition(this);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight) SetDropHighlight(false);
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (IdleView.Visibility == Visibility.Visible && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            foreach (var p in paths) AddPath(Items, p);
        SetDropHighlight(false);
    }

    void Dest_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void Dest_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths && Directory.Exists(paths[0]))
            DestBox.Text = paths[0];
        e.Handled = true;
    }

    void BrowseDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog(this) == true) DestBox.Text = dlg.FolderName;
    }

    // ---------- copy ----------

    // Shared by Copy and Move: both take sources, a destination and an overwrite mode.
    async void StartCopy_Click(object sender, RoutedEventArgs e)
    {
        bool move = MoveModeOn;
        var list = move ? _moveItems : _copyItems;
        var sources = list.Select(i => i.Path).ToList();
        var dest = DestBox.Text.Trim();
        if (sources.Count == 0) { ShowHint(move ? "Add something to move first." : "Add something to copy first."); return; }
        if (dest == "") { ShowHint("Pick a destination folder first."); return; }
        var mode = OverSkip.IsChecked == true ? OverwriteMode.SkipIdentical
                 : OverAlways.IsChecked == true ? OverwriteMode.Always : OverwriteMode.Never;
        var destName = Path.GetFileName(PathUtil.Normalize(dest)) is { Length: > 0 } name ? name : dest;

        var p = new JobProgress();
        if (!move)
        {
            var elapsed = await RunJob(p, "COPYING", $"Copying to {destName}", ct => Copier.Run(sources, dest, mode, p, ct));
            if (elapsed is { } t)
                ShowDone(p, Outcome.Success, "COPY COMPLETE",
                    $"Copied {Format.Count(p.FilesDone - p.FilesSkipped - p.FilesFailed)} files " +
                    $"({Format.Bytes(p.BytesDone - p.BytesSkipped)}) in {Format.Time(t)}", t);
        }
        else
        {
            var elapsed = await RunJob(p, "MOVING", $"Moving to {destName}", ct => Mover.Run(sources, dest, mode, p, ct));
            foreach (var gone in _moveItems.Where(i => !Path.Exists(i.Path)).ToList()) _moveItems.Remove(gone);
            if (elapsed is { } t)
                ShowDone(p, Outcome.Success, "MOVE COMPLETE",
                    $"Moved {Format.Count(p.FilesDone - p.FilesSkipped - p.FilesFailed)} items in {Format.Time(t)}", t);
        }
    }

    // ---------- delete ----------

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var items = _deleteItems.Select(i => i.Path).ToList();
        if (items.Count == 0) { ShowHint("Add something to delete first."); return; }
        foreach (var item in items)
            if (ProtectedPaths.Check(item) is { } reason) { ShowHint($"Zap won't delete this: {reason}"); return; }

        var p = new JobProgress();
        ScanResult? scan = null;
        if (await RunJob(p, "SCANNING", "Scanning files before deleting…", ct => scan = Scanner.Scan(items, p, ct)) is null || scan is null)
            return;

        var dialog = new DeleteConfirmDialog(
            $"Delete {Format.Count(scan.Files.Count)} files ({Format.Bytes(scan.TotalBytes)})?",
            items.Count == 1 ? items[0] : $"From {items.Count} items") { Owner = this };
        if (dialog.ShowDialog() != true) { ShowView(IdleView); return; }

        TimeSpan? elapsed;
        string verb;
        if (dialog.Choice == DeleteChoice.Permanent)
        {
            elapsed = await RunJob(p, "DELETING", $"Deleting {Format.Count(scan.Files.Count)} files",
                ct => Deleter.DeletePermanent(scan, p, ct));
            verb = "Deleted";
        }
        else
        {
            elapsed = await RunJob(p, "RECYCLING", "Moving to the Recycle Bin…", _ => Deleter.Recycle(items), cancellable: false);
            verb = "Recycled";
        }

        foreach (var gone in _deleteItems.Where(i => !Path.Exists(i.Path)).ToList()) _deleteItems.Remove(gone);
        if (elapsed is { } t)
            ShowDone(p, Outcome.Success, "DELETE COMPLETE",
                $"{verb} {Format.Count(scan.Files.Count - p.FilesFailed)} files ({Format.Bytes(scan.TotalBytes)}) in {Format.Time(t)}", t);
    }

    // ---------- running jobs ----------

    /// <summary>Runs work in the background with live progress. Returns elapsed time, or null if cancelled/failed.</summary>
    async Task<TimeSpan?> RunJob(JobProgress p, string eyebrow, string headline, Action<CancellationToken> work, bool cancellable = true)
    {
        _cts = new CancellationTokenSource();
        ResetJobView(eyebrow, headline, cancellable);
        ShowView(JobView);
        ModeHost.IsEnabled = false;
        var clock = Stopwatch.StartNew();
        SpeedMeter speed = new(), processed = new(), fileSpeed = new();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdateJob(p, clock.Elapsed, speed, processed, fileSpeed, cancellable);
        timer.Start();
        try
        {
            await Task.Run(() => work(_cts.Token));
            return clock.Elapsed;
        }
        catch (OperationCanceledException)
        {
            ShowDone(p, Outcome.Cancelled, "CANCELLED", "Stopped before finishing.", clock.Elapsed);
            return null;
        }
        catch (Exception ex)
        {
            ShowDone(p, Outcome.Failed, "COULDN'T FINISH", ex.Message, clock.Elapsed);
            return null;
        }
        finally
        {
            timer.Stop();
            ModeHost.IsEnabled = true;
        }
    }

    void ResetJobView(string eyebrow, string headline, bool cancellable)
    {
        (_jobEyebrow, _jobHeadline) = (eyebrow, headline);
        JobEyebrow.Content = eyebrow;
        JobHeadline.Text = headline;
        CurrentText.Text = "";
        RingText.Text = "0%";
        RingText.Visibility = Visibility.Visible;
        RingGlyph.Visibility = Visibility.Collapsed;
        RingSub.Text = eyebrow;
        Ring.SetResourceReference(ProgressRing.StrokeProperty, "AccentGradient");
        Ring.Effect = _ringGlow;
        Ring.BeginAnimation(ProgressRing.ProgressProperty, null);
        Ring.Progress = 0;
        SpeedText.Text = TimeText.Text = FilesText.Text = BytesText.Text = "—";
        TimeCell.Header = "TIME LEFT";
        SpeedCell.Header = "SPEED";
        Graph.Clear();
        Graph.Visibility = Visibility.Visible;
        ErrorsPanel.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = cancellable;
        DoneButton.Visibility = Visibility.Collapsed;
    }

    void UpdateJob(JobProgress p, TimeSpan elapsed, SpeedMeter speed, SpeedMeter processed, SpeedMeter fileSpeed, bool cancellable)
    {
        bool scanning = p.TotalFiles == 0 && p.FilesDone == 0;
        if (!cancellable || scanning) // no measurable progress yet
        {
            SetRingSpinning(true);
            RingText.Text = "…";
            CurrentText.Text = p.CurrentItem ?? "";
            TimeCell.Header = "ELAPSED";
            TimeText.Text = Format.Time(elapsed);
            if (cancellable) // counting files before the real work starts
            {
                JobEyebrow.Content = RingSub.Text = "SCANNING";
                JobHeadline.Text = DeleteModeOn ? "Scanning files before deleting…" : MoveModeOn ? "Scanning files before moving…" : "Scanning files before copying…";
                FilesText.Text = $"{Format.Count(p.ScannedFiles)} found";
                BytesText.Text = $"{Format.Bytes(p.ScannedBytes)} found";
            }
            return;
        }

        SetRingSpinning(false);
        JobEyebrow.Content = RingSub.Text = _jobEyebrow;
        JobHeadline.Text = _jobHeadline;
        TimeCell.Header = "TIME LEFT";
        SpeedCell.Header = p.Parallelism == 1 ? "SPEED · HDD MODE" : "SPEED";
        // Big files are bound by bytes, piles of small files by file count: weigh both.
        double byBytes = p.TotalBytes > 0 ? (double)p.BytesDone / p.TotalBytes : 1;
        double byFiles = (double)p.FilesDone / Math.Max(1, p.TotalFiles);
        bool countBound = DeleteModeOn || MoveModeOn; // file count, not data volume, sets the pace
        double fraction = MoveModeOn ? byFiles : (byBytes + byFiles) / 2; // whole-folder renames carry no byte count
        Ring.BeginAnimation(ProgressRing.ProgressProperty, new DoubleAnimation(fraction, TimeSpan.FromMilliseconds(300)));
        RingText.Text = $"{Math.Floor(fraction * 100):0}%";

        double rate = speed.Update(p.BytesDone - p.BytesSkipped, elapsed);      // data really transferred (shown)
        double processedRate = processed.Update(p.BytesDone, elapsed);          // incl. skipped files (for time left)
        double fileRate = fileSpeed.Update(p.FilesDone, elapsed);
        // Deleting and same-drive moves are bound by file count, so GB/s would be meaningless there.
        Graph.Add(countBound ? fileRate : rate);
        SpeedText.Text = DeleteModeOn ? $"{Format.Count((long)fileRate)} files/s"
            : MoveModeOn ? $"{Format.Count((long)fileRate)} items/s" : $"{Format.Bytes(rate)}/s";
        double filesLeft = fileRate > 0 ? (p.TotalFiles - p.FilesDone) / fileRate : 0;
        double bytesLeft = processedRate > 0 ? (p.TotalBytes - p.BytesDone) / processedRate : 0;
        double secondsLeft = countBound ? filesLeft : Math.Max(filesLeft, bytesLeft);
        TimeText.Text = fileRate > 0 || processedRate > 0 ? Format.Time(TimeSpan.FromSeconds(secondsLeft)) : "—";
        FilesText.Text = $"{Format.Count(p.FilesDone)} / {Format.Count(p.TotalFiles)}";
        BytesText.Text = $"{Format.Bytes(p.BytesDone)} / {Format.Bytes(p.TotalBytes)}";
        CurrentText.Text = p.CurrentItem ?? "";
    }

    void ShowDone(JobProgress p, Outcome outcome, string eyebrow, string headline, TimeSpan elapsed)
    {
        SetRingSpinning(false);
        bool errors = p.Errors.Count > 0;
        var (color, glyph, label) = outcome switch
        {
            Outcome.Success when !errors => (Emerald, "", "DONE"),
            Outcome.Success => (Amber, "", "DONE WITH ERRORS"),
            Outcome.Cancelled => (Neutral, "", "CANCELLED"),
            _ => (Amber, "", "FAILED"),
        };
        var brush = Frozen(new SolidColorBrush(color));
        Ring.BeginAnimation(ProgressRing.ProgressProperty, outcome == Outcome.Success
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(400)) : null);
        Ring.Stroke = brush;
        Ring.Effect = new DropShadowEffect { Color = color, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 };
        RingText.Visibility = Visibility.Collapsed;
        RingGlyph.Text = glyph;
        RingGlyph.Foreground = brush;
        RingGlyph.Visibility = Visibility.Visible;
        RingSub.Text = label;

        JobEyebrow.Content = eyebrow;
        JobHeadline.Text = headline;
        var extras = new List<string>();
        if (p.FilesSkipped > 0) extras.Add($"{Format.Count(p.FilesSkipped)} skipped (already there)");
        if (errors) extras.Add($"{Format.Count(p.Errors.Count)} errors");
        CurrentText.Text = string.Join(" · ", extras);
        if (elapsed.TotalSeconds > 0 && (DeleteModeOn || MoveModeOn) && p.FilesDone > 0)
            SpeedText.Text = $"{Format.Count((long)(p.FilesDone / elapsed.TotalSeconds))} {(MoveModeOn ? "items" : "files")}/s avg";
        else if (elapsed.TotalSeconds > 0 && p.BytesDone > p.BytesSkipped)
            SpeedText.Text = $"{Format.Bytes((p.BytesDone - p.BytesSkipped) / elapsed.TotalSeconds)}/s avg";
        TimeCell.Header = "TOOK";
        TimeText.Text = Format.Time(elapsed);
        if (p.TotalFiles > 0)
        {
            FilesText.Text = $"{Format.Count(p.FilesDone)} / {Format.Count(p.TotalFiles)}";
            BytesText.Text = $"{Format.Bytes(p.BytesDone)} / {Format.Bytes(p.TotalBytes)}";
        }

        ErrorList.ItemsSource = p.Errors.ToList();
        ErrorsEyebrow.Content = $"ERRORS ({p.Errors.Count})";
        ErrorsPanel.Visibility = errors ? Visibility.Visible : Visibility.Collapsed;
        Graph.Visibility = errors ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = Visibility.Collapsed;
        DoneButton.Visibility = Visibility.Visible;
    }

    void SetRingSpinning(bool on)
    {
        if (on == _spinning) return;
        _spinning = on;
        var rotate = (RotateTransform)Ring.RenderTransform;
        Ring.BeginAnimation(ProgressRing.ProgressProperty, null);
        if (on)
        {
            Ring.Progress = 0.28;
            rotate.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = 0;
            Ring.Progress = 0;
        }
    }

    void ShowView(FrameworkElement view)
    {
        var other = view == JobView ? IdleView : JobView;
        other.Visibility = Visibility.Collapsed;
        if (view.Visibility == Visibility.Visible) return;
        view.Visibility = Visibility.Visible;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        ((TranslateTransform)view.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
    void Done_Click(object sender, RoutedEventArgs e) => ShowView(IdleView);

    // ---------- window chrome ----------

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    void UseWin11Frame()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1, round = 2; // DWMWA_USE_IMMERSIVE_DARK_MODE, DWMWA_WINDOW_CORNER_PREFERENCE = round
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
    }

    // ---------- helpers ----------

    static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Smoothed bytes/second.</summary>
    sealed class SpeedMeter
    {
        long _lastBytes;
        TimeSpan _lastTime;
        double _rate;

        public double Update(long bytes, TimeSpan now)
        {
            var dt = (now - _lastTime).TotalSeconds;
            if (dt <= 0) return _rate;
            var instant = (bytes - _lastBytes) / dt;
            _rate = _rate == 0 ? instant : _rate * 0.8 + instant * 0.2;
            (_lastBytes, _lastTime) = (bytes, now);
            return _rate;
        }
    }
}
