using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Morph.Engine;
using Morph.UI;

namespace Morph;

/// <summary>
/// Startup. Command lines:
///   Morph.exe [files…]                     open the window (with these files)
///   Morph.exe --add &lt;file&gt;                  what the Explorer menu runs; joins a window that is already open
///   Morph.exe --convert &lt;format&gt; [--quality small|balanced|best] [--out &lt;dir&gt;] files…   no window; prints results
///   Morph.exe --formats                    lists format ids
///   Morph.exe --register-menu | --unregister-menu
/// </summary>
public partial class App : Application
{
    static readonly string Id = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    static readonly string PipeName = $"Morph-{Id}";
    Mutex? single;
    MainWindow? window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var first = args.FirstOrDefault()?.ToLowerInvariant();

        switch (first)
        {
            case "--convert":
                Console.OutputEncoding = Encoding.UTF8;
                Shutdown(await Cli.ConvertAsync(args.Skip(1).ToArray()));
                return;
            case "--formats":
                foreach (var f in Formats.All) Console.WriteLine($"{f.Id,-10} {Formats.CategoryName(f.Category),-12} {f.Label} (.{f.Ext})");
                Shutdown(0);
                return;
            case "--register-menu":
                ExplorerMenu.Register(Environment.ProcessPath!);
                Shutdown(0);
                return;
            case "--unregister-menu":
                ExplorerMenu.Unregister();
                Shutdown(0);
                return;
            case "--screenshot":
                // Developer aid: renders the window (with these files) to a PNG and exits.
                await Screenshot.TakeAsync(args[1], args.Skip(2).ToList());
                Shutdown(0);
                return;
        }

        var files = args.Where(a => a != "--add").ToList();

        // One window per user: a second launch (each file Explorer hands over starts one) passes its
        // files to the first and exits.
        single = new Mutex(true, $@"Local\Morph-{Id}", out var isFirst);
        if (!isFirst)
        {
            if (await SendToRunningAsync(files)) { Shutdown(0); return; }
            // The first copy is gone or stuck; carry on as the window.
        }

        window = new MainWindow();
        window.Closed += (_, _) => Shutdown(0);
        window.Show();
        if (files.Count > 0) window.AddFiles(files);
        _ = Task.Run(ListenAsync);
    }

    async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync();
                var paths = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                await Dispatcher.InvokeAsync(() => window?.AddFiles(paths));
            }
            catch (Exception)
            {
                await Task.Delay(200);
            }
        }
    }

    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    static async Task<bool> SendToRunningAsync(List<string> files)
    {
        // Let the running copy bring its window to the front.
        AllowSetForegroundWindow(-1);
        var full = files.Select(f => { try { return Path.GetFullPath(f); } catch (Exception) { return f; } });
        // Many copies may start at once; the first one may still be opening its window.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(500);
                var bytes = Encoding.UTF8.GetBytes(string.Join('\n', full) + "\n");
                await client.WriteAsync(bytes);
                await client.FlushAsync();
                return true;
            }
            catch (Exception)
            {
                await Task.Delay(150);
            }
        }
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        single?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>The window-less mode used by the smoke test and by scripts.</summary>
static class Cli
{
    public static async Task<int> ConvertAsync(string[] args)
    {
        if (args.Length == 0 || Formats.ById(args[0]) is not { } format)
        {
            Console.WriteLine("usage: Morph --convert <format> [--quality small|balanced|best] [--out <dir>] files…");
            Console.WriteLine("formats: " + string.Join(" ", Formats.All.Select(f => f.Id)));
            return 2;
        }
        var quality = Quality.Balanced;
        string? outDir = null;
        var files = new List<string>();
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--quality" && i + 1 < args.Length)
            {
                if (!Enum.TryParse(args[++i], true, out quality)) { Console.WriteLine($"unknown quality {args[i]}"); return 2; }
            }
            else if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            else files.Add(args[i]);
        }

        var failed = false;
        foreach (var file in files)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var output = await Converter.ConvertAsync(file, format, quality, outDir, null, CancellationToken.None);
                Console.WriteLine($"OK   {Path.GetFileName(file)} -> {output} ({watch.Elapsed.TotalSeconds:0.00}s)");
            }
            catch (Exception ex)
            {
                failed = true;
                Console.WriteLine($"FAIL {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        Console.Out.Flush();
        return failed ? 1 : 0;
    }
}
