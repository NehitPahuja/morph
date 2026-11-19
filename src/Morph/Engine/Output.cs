using System;
using System.IO;

namespace Morph.Engine;

/// <summary>Decides where converted files go and what they are called. Nothing is ever overwritten.</summary>
public static class Output
{
    /// <summary>Used when the chosen folder can't be written to (a read-only drive, a locked share).</summary>
    public static string Fallback
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("MORPH_FALLBACK_DIR");
            if (!string.IsNullOrWhiteSpace(env)) return env;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Morph");
        }
    }

    /// <summary>The folder for a conversion of <paramref name="input"/>: the chosen one, or the file's own.</summary>
    public static string FolderFor(string input, string? chosen)
    {
        var dir = !string.IsNullOrWhiteSpace(chosen) ? chosen! : Path.GetDirectoryName(Path.GetFullPath(input))!;
        if (CanWrite(dir)) return dir;
        Directory.CreateDirectory(Fallback);
        if (CanWrite(Fallback)) return Fallback;
        throw new MorphException($"Can't write to \"{dir}\" or to \"{Fallback}\"");
    }

    /// <summary>Asks the folder itself: permissions and attributes don't know about read-only media or share rules.</summary>
    public static bool CanWrite(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return false;
            var probe = Path.Combine(dir, $".morph-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>"photo.png", or "photo (2).png", "photo (3).png", … if taken.</summary>
    public static string FreeFile(string dir, string baseName, string ext)
    {
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? $"{baseName}.{ext}" : $"{baseName} ({n}).{ext}";
            var path = Path.Combine(dir, name);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    public static string FreeFolder(string dir, string baseName)
    {
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? baseName : $"{baseName} ({n})";
            var path = Path.Combine(dir, name);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception) { }
    }
}
