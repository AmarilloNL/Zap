using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Zap.Engine;

namespace Zap.App;

public partial class MainWindow : Window
{
    CancellationTokenSource? _cts;

    public MainWindow() => InitializeComponent();

    // ---------- picking items ----------

    static void AddItems(ListBox list, IEnumerable<string> paths)
    {
        foreach (var p in paths)
            if (!list.Items.Contains(p)) list.Items.Add(p);
    }

    void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true };
        if (dlg.ShowDialog(this) == true) AddItems((ListBox)((Button)sender).Tag, dlg.FileNames);
    }

    void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Multiselect = true };
        if (dlg.ShowDialog(this) == true) AddItems((ListBox)((Button)sender).Tag, dlg.FolderNames);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        var list = (ListBox)((Button)sender).Tag;
        foreach (var item in list.SelectedItems.Cast<object>().ToList()) list.Items.Remove(item);
    }

    void Clear_Click(object sender, RoutedEventArgs e) => ((ListBox)((Button)sender).Tag).Items.Clear();

    void Drop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void List_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddItems((ListBox)sender, paths);
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

    async void StartCopy_Click(object sender, RoutedEventArgs e)
    {
        var sources = CopyList.Items.Cast<string>().ToList();
        var dest = DestBox.Text.Trim();
        if (sources.Count == 0 || dest == "") { StatusText.Text = "Add something to copy and pick a destination folder."; return; }
        var mode = (OverwriteMode)OverwriteBox.SelectedIndex;

        var p = new JobProgress();
        var elapsed = await RunJob(p, ct => Copier.Run(sources, dest, mode, p, ct));
        if (elapsed is { } t)
            ShowSummary(p, $"Copied {Format.Count(p.FilesDone - p.FilesSkipped - p.FilesFailed)} files " +
                           $"({Format.Bytes(p.BytesDone - p.BytesSkipped)}) in {Format.Time(t)}");
    }

    // ---------- delete ----------

    async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var items = DeleteList.Items.Cast<string>().ToList();
        if (items.Count == 0) { StatusText.Text = "Add something to delete."; return; }
        foreach (var item in items)
            if (ProtectedPaths.Check(item) is { } reason)
            {
                MessageBox.Show(this, reason, "Zap won't delete this", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

        var p = new JobProgress();
        ScanResult? scan = null;
        if (await RunJob(p, ct => scan = Scanner.Scan(items, p, ct)) is null || scan is null) return;
        StatusText.Text = "Waiting for confirmation…";

        var dialog = new DeleteConfirmDialog(
            $"Delete {Format.Count(scan.Files.Count)} files ({Format.Bytes(scan.TotalBytes)}) in {items.Count} item(s)?")
            { Owner = this };
        if (dialog.ShowDialog() != true) { StatusText.Text = "Delete cancelled."; return; }

        TimeSpan? elapsed;
        string verb;
        if (dialog.Choice == DeleteChoice.Permanent)
        {
            elapsed = await RunJob(p, ct => Deleter.DeletePermanent(scan, p, ct));
            verb = "Deleted";
        }
        else
        {
            elapsed = await RunJob(p, _ => Deleter.Recycle(items), cancellable: false);
            verb = "Moved to Recycle Bin:";
        }

        foreach (var item in items.Where(i => !Path.Exists(i))) DeleteList.Items.Remove(item);
        if (elapsed is { } t)
            ShowSummary(p, $"{verb} {Format.Count(scan.Files.Count - p.FilesFailed)} files ({Format.Bytes(scan.TotalBytes)}) in {Format.Time(t)}");
    }

    // ---------- running jobs ----------

    /// <summary>Runs work in the background with live progress. Returns elapsed time, or null if cancelled/failed.</summary>
    async Task<TimeSpan?> RunJob(JobProgress p, Action<CancellationToken> work, bool cancellable = true)
    {
        _cts = new CancellationTokenSource();
        SetBusy(true, cancellable);
        ErrorsExpander.Visibility = Visibility.Collapsed;
        var clock = Stopwatch.StartNew();
        var speed = new SpeedMeter();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => ShowProgress(p, clock.Elapsed, speed, indeterminate: !cancellable);
        timer.Start();
        try
        {
            await Task.Run(() => work(_cts.Token));
            return clock.Elapsed;
        }
        catch (OperationCanceledException)
        {
            ShowSummary(p, "Cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            ShowSummary(p, ex.Message);
            return null;
        }
        finally
        {
            timer.Stop();
            SetBusy(false, false);
        }
    }

    void ShowProgress(JobProgress p, TimeSpan elapsed, SpeedMeter speed, bool indeterminate)
    {
        bool scanning = p.TotalFiles == 0 && p.FilesDone == 0;
        Bar.IsIndeterminate = indeterminate || scanning;
        if (indeterminate) // only Recycle Bin runs without progress
        {
            StatusText.Text = "Moving to Recycle Bin…";
            DetailText.Text = Format.Time(elapsed);
            return;
        }
        if (scanning)
        {
            StatusText.Text = $"Scanning {p.CurrentItem}";
            DetailText.Text = Format.Time(elapsed);
            return;
        }
        Bar.Value = p.TotalBytes > 0 ? (double)p.BytesDone / p.TotalBytes : (double)p.FilesDone / Math.Max(1, p.TotalFiles);
        StatusText.Text = p.CurrentItem ?? "";
        double rate = speed.Update(p.BytesDone - p.BytesSkipped, elapsed);
        var left = rate > 0 ? $" · {Format.Time(TimeSpan.FromSeconds((p.TotalBytes - p.BytesDone) / rate))} left" : "";
        DetailText.Text = $"{Format.Count(p.FilesDone)} / {Format.Count(p.TotalFiles)} files · " +
                          $"{Format.Bytes(p.BytesDone)} / {Format.Bytes(p.TotalBytes)} · {Format.Bytes(rate)}/s{left}";
    }

    void ShowSummary(JobProgress p, string headline)
    {
        Bar.IsIndeterminate = false;
        var extras = new List<string>();
        if (p.FilesSkipped > 0) extras.Add($"{Format.Count(p.FilesSkipped)} skipped");
        if (p.Errors.Count > 0) extras.Add($"{Format.Count(p.Errors.Count)} errors");
        StatusText.Text = string.Join(" · ", [headline, .. extras]);
        DetailText.Text = "";
        ErrorList.ItemsSource = p.Errors.Select(err => $"{err.Path} — {err.Message}").ToList();
        ErrorsExpander.Header = $"Errors ({p.Errors.Count})";
        ErrorsExpander.Visibility = p.Errors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorsExpander.IsExpanded = p.Errors.Count > 0;
    }

    void SetBusy(bool busy, bool cancellable)
    {
        Tabs.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && cancellable;
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

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
