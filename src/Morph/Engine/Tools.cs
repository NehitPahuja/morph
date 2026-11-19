using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Morph.Engine;

/// <summary>
/// Finds ffmpeg and ffprobe. Looked up on every use, so installing ffmpeg while Morph is open works
/// without a restart.
/// </summary>
public static class Tools
{
    public static string? FFmpeg => Find("ffmpeg.exe");
    public static string? FFprobe => Find("ffprobe.exe");

    static string? Find(string exe)
    {
        foreach (var dir in Folders())
        {
            try
            {
                var path = Path.Combine(dir, exe);
                if (File.Exists(path)) return path;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    static IEnumerable<string> Folders()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "tools");
        // The PATH this process started with, then the PATH as it is now: winget changes the stored
        // PATH, which a program that is already running doesn't see.
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        };
        foreach (var entry in paths.SelectMany(p => (p ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)))
        {
            var expanded = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
            // A relative entry would mean "the current folder", which may be the folder being converted.
            if (Path.IsPathFullyQualified(expanded)) yield return expanded;
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links");
        var packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(packages))
        {
            foreach (var pkg in Directory.EnumerateDirectories(packages, "*FFmpeg*"))
                foreach (var bin in SafeDirs(pkg, "bin")) yield return bin;
        }
    }

    static IEnumerable<string> SafeDirs(string root, string name)
    {
        try { return Directory.EnumerateDirectories(root, name, SearchOption.AllDirectories).Take(4).ToList(); }
        catch (Exception) { return []; }
    }

    static readonly object Gate = new();
    static string? cachedFor;
    static HashSet<string> cachedEncoders = [];

    /// <summary>Whether the installed ffmpeg has this encoder. Asked once per ffmpeg build.</summary>
    public static bool HasEncoder(string name)
    {
        var ff = FFmpeg;
        if (ff == null) return false;
        var info = new FileInfo(ff);
        var stamp = $"{ff}|{(info.Exists ? info.Length : 0)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}";
        lock (Gate)
        {
            if (cachedFor != stamp)
            {
                cachedEncoders = ReadEncoders(ff);
                cachedFor = stamp;
            }
            return cachedEncoders.Contains(name);
        }
    }

    static HashSet<string> ReadEncoders(string ff)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var psi = new ProcessStartInfo(ff)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");
            using var p = Process.Start(psi)!;
            p.ErrorDataReceived += (_, _) => { };
            p.BeginErrorReadLine();
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);
            // Lines look like " V....D libx264   libx264 H.264 ..."; the list starts after " ------".
            var started = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("------")) { started = true; continue; }
                if (!started) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) set.Add(parts[1]);
            }
        }
        catch (Exception) { }
        return set;
    }
}
