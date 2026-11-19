using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Morph.Engine;

namespace Morph.UI;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        // Buttons ask their command whether it can run only after user input; state changes made by
        // code (files arriving from Explorer, a conversion finishing) must ask again.
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
}

public sealed class Command(Action<object?> run, Func<bool>? canRun = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    public bool CanExecute(object? parameter) => canRun?.Invoke() ?? true;
    public void Execute(object? parameter) => run(parameter);
}

public enum ItemState { Queued, Working, Done, Failed, Skipped }

/// <summary>One file in the queue on the left.</summary>
public sealed class FileItem : Observable
{
    public FileItem(string path, SourceKind kind)
    {
        Path = path;
        Kind = kind;
        Name = System.IO.Path.GetFileName(path);
        try { size = Describe(new FileInfo(path).Length); } catch (Exception) { size = ""; }
        detail = size;
    }

    public string Path { get; }
    public SourceKind Kind { get; }
    public string Name { get; }
    readonly string size;

    public string Glyph => Kind switch
    {
        SourceKind.Audio => "",
        SourceKind.Video => "",
        SourceKind.Image => "",
        _ => "",
    };

    ItemState state;
    public ItemState State
    {
        get => state;
        set
        {
            if (!Set(ref state, value)) return;
            Raise(nameof(IsWorking));
            Raise(nameof(StateGlyph));
            Raise(nameof(StateBrush));
            Raise(nameof(HasStateGlyph));
        }
    }

    double progress;
    public double Progress { get => progress; set => Set(ref progress, value); }

    string detail;
    /// <summary>Second line: size while queued, then where it went or what went wrong.</summary>
    public string Detail { get => detail; set => Set(ref detail, value); }

    bool removable = true;
    public bool Removable { get => removable; set => Set(ref removable, value); }

    public string? Output { get; set; }

    public bool IsWorking => State == ItemState.Working;
    public bool HasStateGlyph => State is ItemState.Done or ItemState.Failed or ItemState.Skipped;
    public string StateGlyph => State switch
    {
        ItemState.Done => "",
        ItemState.Failed => "",
        ItemState.Skipped => "",
        _ => "",
    };
    public Brush StateBrush => State switch
    {
        ItemState.Done => (Brush)System.Windows.Application.Current.Resources["Good"],
        ItemState.Failed => (Brush)System.Windows.Application.Current.Resources["Bad"],
        _ => (Brush)System.Windows.Application.Current.Resources["Dim"],
    };

    public void Reset()
    {
        State = ItemState.Queued;
        Progress = 0;
        Detail = size;
        Output = null;
    }

    static string Describe(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double v = bytes;
        var u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes} B" : $"{v:0.#} {units[u]}";
    }
}

/// <summary>A tile in the format grid.</summary>
public sealed class FormatTile(OutputFormat format, bool available)
{
    public OutputFormat Format { get; } = format;
    public bool Available { get; } = available;
    public string Badge => "." + Format.Ext.ToUpperInvariant();
    public string Label => Format.Label;
    public string Blurb => Available ? Format.Blurb : "Needs a newer ffmpeg";
}

public sealed record Choice<T>(T Value, string Label);
