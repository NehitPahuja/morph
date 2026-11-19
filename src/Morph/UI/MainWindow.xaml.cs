using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Morph.Engine;

namespace Morph.UI;

public partial class MainWindow : Window
{
    public MainViewModel Model { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Model;
        Model.AskForFiles = AskForFiles;
        Model.AskForFolder = AskForFolder;
        SourceInitialized += (_, _) => DarkTitleBar();
        // ffmpeg may have been installed while Morph was in the background.
        Activated += (_, _) => Model.RecheckTools();
        Closing += (_, e) =>
        {
            if (Model.Phase != Phase.Running) return;
            var answer = MessageBox.Show(this, "A conversion is running. Stop it and close Morph?", "Morph",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) { e.Cancel = true; return; }
            Model.CancelCommand.Execute(null);
        };
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        Model.Add(paths);
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    IEnumerable<string> AskForFiles()
    {
        var exts = string.Join(";", Formats.Extensions.OrderBy(e => e).Select(e => "*." + e));
        var dialog = new OpenFileDialog
        {
            Title = "Choose files to convert",
            Multiselect = true,
            Filter = $"Files Morph can convert|{exts}|All files|*.*",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileNames : [];
    }

    string? AskForFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Save converted files in…" };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) && Model.CanEditQueue ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) Model.Add(paths);
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>A dark caption in the window's own colour (Windows 11; ignored elsewhere).</summary>
    void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));       // DWMWA_USE_IMMERSIVE_DARK_MODE
        var caption = 0x00171413;                                   // COLORREF of #131417 (0x00BBGGRR)
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));  // DWMWA_CAPTION_COLOR
        var text = 0x00F5F3F3;
        DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));     // DWMWA_TEXT_COLOR
    }
}
