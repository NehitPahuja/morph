using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Morph.Engine;

namespace Morph.UI;

public enum Phase { Empty, Picking, Running, Finished }

public sealed class MainViewModel : Observable
{
    readonly Settings settings = Settings.Load();
    CancellationTokenSource? cts;

    public ObservableCollection<FileItem> Files { get; } = [];
    public ObservableCollection<Choice<Category>> Categories { get; } = [];
    public ObservableCollection<FormatTile> Tiles { get; } = [];
    public IReadOnlyList<Choice<Quality>> Qualities { get; } =
        [new(Quality.Small, "Smaller"), new(Quality.Balanced, "Balanced"), new(Quality.Best, "Best")];
    public IReadOnlyList<Choice<bool>> Destinations { get; } =
        [new(false, "Next to originals"), new(true, "Pick a folder")];

    public Command BrowseCommand { get; }
    public Command ClearCommand { get; }
    public Command RemoveCommand { get; }
    public Command ConvertCommand { get; }
    public Command CancelCommand { get; }
    public Command OpenFolderCommand { get; }
    public Command AgainCommand { get; }
    public Command NewBatchCommand { get; }
    public Command PickFolderCommand { get; }

    /// <summary>Set by the window: shows the open-files dialog and returns the picked paths.</summary>
    public Func<IEnumerable<string>>? AskForFiles { get; set; }
    /// <summary>Set by the window: shows the folder picker.</summary>
    public Func<string?>? AskForFolder { get; set; }

    public MainViewModel()
    {
        BrowseCommand = new(_ => { if (AskForFiles != null) Add(AskForFiles()); }, () => Phase != Phase.Running);
        ClearCommand = new(_ => NewBatch(), () => Phase != Phase.Running);
        RemoveCommand = new(p => { if (p is FileItem f && Phase == Phase.Picking) { Files.Remove(f); Refresh(); } });
        ConvertCommand = new(_ => _ = RunAsync(), () => CanConvert);
        CancelCommand = new(_ => cts?.Cancel(), () => Phase == Phase.Running);
        OpenFolderCommand = new(_ => OpenResult());
        AgainCommand = new(_ => { foreach (var f in Files) f.Reset(); Phase = Phase.Picking; Refresh(); });
        NewBatchCommand = new(_ => NewBatch());
        PickFolderCommand = new(_ => PickFolder());
        useCustomFolder = settings.UseCustomFolder && Directory.Exists(settings.CustomFolder ?? "");
        destination = Destinations[useCustomFolder ? 1 : 0];
        openWhenDone = settings.OpenFolderWhenDone;
        menuEnabled = ExplorerMenu.IsRegistered;
    }

    // ---------------------------------------------------------------- state

    Phase phase = Phase.Empty;
    public Phase Phase
    {
        get => phase;
        private set
        {
            if (!Set(ref phase, value)) return;
            Raise(nameof(ShowEmpty)); Raise(nameof(ShowPicker)); Raise(nameof(ShowRunning)); Raise(nameof(ShowFinished));
            Raise(nameof(CanEditQueue));
            foreach (var f in Files) f.Removable = value == Phase.Picking;
        }
    }
    public bool ShowEmpty => Phase == Phase.Empty;
    public bool ShowPicker => Phase == Phase.Picking;
    public bool ShowRunning => Phase == Phase.Running;
    public bool ShowFinished => Phase == Phase.Finished;
    public bool CanEditQueue => Phase != Phase.Running;
    public bool HasFiles => Files.Count > 0;

    string queueTitle = "No files yet";
    public string QueueTitle { get => queueTitle; private set => Set(ref queueTitle, value); }

    string heading = "";
    public string Heading { get => heading; private set => Set(ref heading, value); }

    string notice = "";
    public string Notice { get => notice; private set { if (Set(ref notice, value)) Raise(nameof(HasNotice)); } }
    public bool HasNotice => Notice.Length > 0;

    string skippedText = "";
    public string SkippedText { get => skippedText; private set => Set(ref skippedText, value); }

    public bool ShowCategories => Categories.Count > 1;

    Choice<Category>? category;
    public Choice<Category>? SelectedCategory
    {
        get => category;
        set
        {
            if (value == null || !Set(ref category, value)) return;
            BuildTiles();
            quality = Qualities.First(c => c.Value == settings.Quality.GetValueOrDefault(value.Value.ToString(), Quality.Balanced));
            Raise(nameof(SelectedQuality));
            Raise(nameof(QualityHint));
        }
    }

    FormatTile? tile;
    public FormatTile? SelectedTile
    {
        get => tile;
        set
        {
            if (!Set(ref tile, value)) return;
            if (value != null && category != null)
            {
                settings.LastFormat[category.Value.ToString()] = value.Format.Id;
                settings.Save();
            }
            Raise(nameof(CanConvert));
            Raise(nameof(ConvertLabel));
        }
    }

    Choice<Quality> quality = null!;
    public Choice<Quality> SelectedQuality
    {
        get => quality;
        set
        {
            if (value == null || !Set(ref quality, value)) return;
            if (category != null) { settings.Quality[category.Value.ToString()] = value.Value; settings.Save(); }
            Raise(nameof(QualityHint));
        }
    }
    public string QualityHint => category == null || quality == null ? "" : Formats.QualityHint(category.Value, quality.Value);

    bool useCustomFolder;
    Choice<bool> destination;
    public Choice<bool> SelectedDestination
    {
        get => destination;
        set
        {
            if (value == null || !Set(ref destination, value)) return;
            if (value.Value && !Directory.Exists(settings.CustomFolder ?? ""))
            {
                // Picking "Pick a folder" asks for one straight away; cancelling goes back.
                if (!PickFolder())
                {
                    destination = Destinations[0];
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Raise(nameof(SelectedDestination)));
                    return;
                }
            }
            useCustomFolder = value.Value;
            settings.UseCustomFolder = useCustomFolder;
            settings.Save();
            Raise(nameof(FolderText));
        }
    }
    public string FolderText => useCustomFolder ? settings.CustomFolder ?? "" : "Each file is saved beside its original";

    bool openWhenDone;
    public bool OpenWhenDone
    {
        get => openWhenDone;
        set { if (Set(ref openWhenDone, value)) { settings.OpenFolderWhenDone = value; settings.Save(); } }
    }

    bool menuEnabled;
    public bool MenuEnabled
    {
        get => menuEnabled;
        set
        {
            try
            {
                if (value) ExplorerMenu.Register(Environment.ProcessPath!);
                else ExplorerMenu.Unregister();
            }
            catch (Exception e)
            {
                Notice = "Couldn't change the Explorer menu: " + e.Message;
            }
            Set(ref menuEnabled, ExplorerMenu.IsRegistered);
        }
    }

    public bool CanConvert => Phase == Phase.Picking && SelectedTile is { Available: true } && Files.Count > 0;
    public string ConvertLabel => SelectedTile == null ? "Convert" :
        Files.Count == 1 ? $"Convert to {SelectedTile.Label}" : $"Convert {Files.Count} files to {SelectedTile.Label}";

    // Running / finished
    double overall;
    public double Overall { get => overall; private set { if (Set(ref overall, value)) Raise(nameof(Percent)); } }
    public string Percent => $"{Math.Round(Overall * 100)}%";

    string current = "";
    public string CurrentName { get => current; private set => Set(ref current, value); }

    string counter = "";
    public string Counter { get => counter; private set => Set(ref counter, value); }

    string summary = "";
    public string Summary { get => summary; private set => Set(ref summary, value); }
    string summaryDetail = "";
    public string SummaryDetail { get => summaryDetail; private set => Set(ref summaryDetail, value); }
    bool allGood;
    public bool AllGood { get => allGood; private set { if (Set(ref allGood, value)) Raise(nameof(SomeFailed)); } }
    public bool SomeFailed => !AllGood;
    public bool HasOutputs => Files.Any(f => f.Output != null);

    // ---------------------------------------------------------------- actions

    /// <summary>Adds files (folders are opened one level deep). Unsupported files are counted, not added.</summary>
    public void Add(IEnumerable<string> paths)
    {
        if (Phase == Phase.Running) return;
        if (Phase == Phase.Finished) NewBatch();
        var skipped = 0;
        foreach (var raw in Expand(paths))
        {
            string full;
            try { full = Path.GetFullPath(raw); } catch (Exception) { skipped++; continue; }
            if (Files.Any(f => string.Equals(f.Path, full, StringComparison.OrdinalIgnoreCase))) continue;
            var kind = Formats.KindOf(full);
            if (kind == null || !File.Exists(full)) { skipped++; continue; }
            Files.Add(new FileItem(full, kind.Value));
        }
        SkippedText = skipped == 0 ? "" : skipped == 1 ? "1 file was skipped: Morph can't convert it" : $"{skipped} files were skipped: Morph can't convert them";
        Refresh();
    }

    static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                IEnumerable<string> inside;
                try { inside = Directory.EnumerateFiles(p).Take(1000).ToList(); } catch (Exception) { continue; }
                foreach (var f in inside) yield return f;
            }
            else yield return p;
        }
    }

    void NewBatch()
    {
        Files.Clear();
        SkippedText = "";
        Phase = Phase.Empty;
        Refresh();
    }

    /// <summary>Recomputes everything that depends on the queue.</summary>
    void Refresh()
    {
        Raise(nameof(HasFiles));
        if (Files.Count == 0)
        {
            if (Phase != Phase.Running) Phase = Phase.Empty;
            QueueTitle = "No files yet";
            Categories.Clear();
            Tiles.Clear();
            Raise(nameof(ShowCategories));
            Notice = "";
            return;
        }
        if (Phase == Phase.Empty) Phase = Phase.Picking;

        var kinds = Files.Select(f => f.Kind).Distinct().ToList();
        QueueTitle = Files.Count == 1 ? "1 file" : $"{Files.Count} files";
        var noun = kinds.Count == 1
            ? kinds[0] switch
            {
                SourceKind.Audio => Files.Count == 1 ? "audio file" : "audio files",
                SourceKind.Video => Files.Count == 1 ? "video" : "videos",
                SourceKind.Image => Files.Count == 1 ? "image" : "images",
                _ => Files.Count == 1 ? "PDF" : "PDFs",
            }
            : "files";
        Heading = Files.Count == 1 ? $"Convert this {noun} to" : $"Convert {Files.Count} {noun} to";

        var cats = Formats.CategoriesFor(kinds);
        var previous = SelectedCategory?.Value;
        Categories.Clear();
        foreach (var c in cats) Categories.Add(new Choice<Category>(c, Formats.CategoryName(c)));
        Raise(nameof(ShowCategories));

        if (cats.Count == 0)
        {
            Tiles.Clear();
            category = null;
            Raise(nameof(SelectedCategory));
            Notice = "These files have no format in common. Convert them in separate batches.";
            Raise(nameof(CanConvert));
            return;
        }
        Notice = "";
        var pick = Categories.FirstOrDefault(c => c.Value == previous) ?? Categories[0];
        category = null; // force a rebuild
        SelectedCategory = pick;
        Raise(nameof(ConvertLabel));
        Raise(nameof(CanConvert));
    }

    void BuildTiles()
    {
        Tiles.Clear();
        if (category == null) return;
        var needsFfmpeg = category.Value is Category.Audio or Category.Video;
        var hasFfmpeg = Tools.FFmpeg != null;
        foreach (var f in Formats.All.Where(f => f.Category == category.Value))
        {
            var available = Formats.IsAvailable(f);
            // Image formats written by ffmpeg are simply left out when it's missing.
            if (!available && !needsFfmpeg && !hasFfmpeg) continue;
            Tiles.Add(new FormatTile(f, available));
        }
        Notice = needsFfmpeg && !hasFfmpeg
            ? "Audio and video need ffmpeg. Install it with  winget install Gyan.FFmpeg  — Morph picks it up without a restart."
            : "";

        var sourceExts = Files.Select(f => Path.GetExtension(f.Path).TrimStart('.').ToLowerInvariant()).ToHashSet();
        var last = settings.LastFormat.GetValueOrDefault(category.Value.ToString());
        SelectedTile = Tiles.FirstOrDefault(t => t.Available && t.Format.Id == last && !sourceExts.Contains(t.Format.Ext))
            ?? Tiles.FirstOrDefault(t => t.Available && !sourceExts.Contains(t.Format.Ext))
            ?? Tiles.FirstOrDefault(t => t.Available);
    }

    bool PickFolder()
    {
        var picked = AskForFolder?.Invoke();
        if (string.IsNullOrEmpty(picked)) return false;
        settings.CustomFolder = picked;
        settings.UseCustomFolder = useCustomFolder = true;
        settings.Save();
        destination = Destinations[1];
        Raise(nameof(SelectedDestination));
        Raise(nameof(FolderText));
        return true;
    }

    /// <summary>Re-checks for ffmpeg, e.g. when the window comes back to the front.</summary>
    public void RecheckTools()
    {
        if (Phase != Phase.Picking) return;
        var id = SelectedTile?.Format.Id;
        BuildTiles();
        SelectedTile = Tiles.FirstOrDefault(t => t.Format.Id == id && t.Available) ?? SelectedTile;
    }

    async Task RunAsync()
    {
        if (!CanConvert || SelectedTile == null) return;
        var format = SelectedTile.Format;
        var q = SelectedQuality.Value;
        var folder = useCustomFolder ? settings.CustomFolder : null;
        var list = Files.ToList();
        foreach (var f in list) f.Reset();

        cts = new CancellationTokenSource();
        Phase = Phase.Running;
        Overall = 0;
        var n = list.Count;
        int ok = 0, failed = 0;
        string? firstError = null;

        for (var i = 0; i < n; i++)
        {
            var item = list[i];
            if (cts.IsCancellationRequested) { item.State = ItemState.Skipped; item.Detail = "Not converted"; continue; }
            var index = i;
            CurrentName = item.Name;
            Counter = $"File {i + 1} of {n}";
            item.State = ItemState.Working;
            item.Detail = "Converting…";
            var progress = new Progress<double>(p =>
            {
                item.Progress = p;
                Overall = (index + p) / n;
            });
            try
            {
                var output = await Converter.ConvertAsync(item.Path, format, q, folder, progress, cts.Token);
                item.Output = output;
                item.State = ItemState.Done;
                item.Detail = "→ " + Path.GetFileName(output);
                ok++;
            }
            catch (OperationCanceledException)
            {
                item.State = ItemState.Skipped;
                item.Detail = "Cancelled";
            }
            catch (Exception e)
            {
                item.State = ItemState.Failed;
                item.Detail = e.Message;
                firstError ??= $"{item.Name}: {e.Message}";
                failed++;
            }
            Overall = (i + 1.0) / n;
        }

        var cancelled = cts.IsCancellationRequested;
        cts.Dispose();
        cts = null;

        AllGood = failed == 0 && !cancelled;
        Summary = cancelled ? "Stopped" :
            failed == 0 ? (ok == 1 ? "Done" : $"All {ok} files converted") :
            ok == 0 ? "Nothing was converted" : $"{ok} converted, {failed} failed";
        SummaryDetail = cancelled ? $"{ok} of {n} finished before you stopped. Unfinished files were removed."
            : failed > 0 ? firstError ?? "" : DescribeWhere(list);
        Raise(nameof(HasOutputs));
        Phase = Phase.Finished;

        if (OpenWhenDone && ok > 0 && !cancelled) OpenResult();
    }

    string DescribeWhere(List<FileItem> list)
    {
        var dirs = list.Where(f => f.Output != null).Select(f => Path.GetDirectoryName(f.Output!)!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (dirs.Count == 1 && list.All(f => f.Output == null || string.Equals(Path.GetDirectoryName(f.Path), dirs[0], StringComparison.OrdinalIgnoreCase)))
            return "Saved next to the originals.";
        return dirs.Count == 1 ? $"Saved in {dirs[0]}" : "Saved next to the originals.";
    }

    /// <summary>For screenshots only: shows the running or finished screen without converting.</summary>
    internal void Preview(string state)
    {
        if (Files.Count == 0) return;
        if (state == "running")
        {
            Files[0].State = ItemState.Done; Files[0].Detail = "→ " + Path.ChangeExtension(Files[0].Name, SelectedTile?.Format.Ext);
            if (Files.Count > 1) { Files[1].State = ItemState.Working; Files[1].Detail = "Converting…"; Files[1].Progress = 0.6; }
            Overall = (1 + 0.6) / Files.Count; CurrentName = Files.Count > 1 ? Files[1].Name : Files[0].Name;
            Counter = $"File {Math.Min(2, Files.Count)} of {Files.Count}";
            Phase = Phase.Running;
        }
        else
        {
            foreach (var f in Files) { f.State = ItemState.Done; f.Detail = "→ " + Path.ChangeExtension(f.Name, SelectedTile?.Format.Ext); }
            if (Files.Count > 2) { Files[2].State = ItemState.Failed; Files[2].Detail = "This file has no sound to convert"; }
            AllGood = Files.Count <= 2;
            Summary = AllGood ? $"All {Files.Count} files converted" : $"{Files.Count - 1} converted, 1 failed";
            SummaryDetail = AllGood ? "Saved next to the originals." : $"{Files[2].Name}: This file has no sound to convert";
            Phase = Phase.Finished;
        }
    }

    void OpenResult()
    {
        var first = Files.FirstOrDefault(f => f.Output != null)?.Output;
        if (first == null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{first}\"") { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
