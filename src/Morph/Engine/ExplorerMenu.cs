using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Morph.Engine;

/// <summary>
/// The "Convert with Morph" entry in Explorer's right-click menu, for the current user only
/// (HKCU, no admin rights). One verb per supported extension, so it only shows on files Morph can open.
/// On Windows 11 it is listed under "Show more options" (Shift+F10 / Shift+right-click).
/// </summary>
public static class ExplorerMenu
{
    const string Verb = "MorphConvert";
    const string Root = @"Software\Classes\SystemFileAssociations";

    public static bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\.mp3\shell\{Verb}\command");
            return key != null;
        }
    }

    /// <summary>The exe the menu currently points at, or null.</summary>
    public static string? RegisteredCommand
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\.mp3\shell\{Verb}\command");
            return key?.GetValue("") as string;
        }
    }

    public static void Register(string exe)
    {
        foreach (var ext in Formats.Extensions)
        {
            using var verb = Registry.CurrentUser.CreateSubKey($@"{Root}\.{ext}\shell\{Verb}");
            verb.SetValue("", "Convert with Morph");
            verb.SetValue("Icon", $"\"{exe}\",0");
            // Each selected file starts Morph once; the copies hand their file to the first one.
            verb.SetValue("MultiSelectModel", "Document");
            using var cmd = verb.CreateSubKey("command");
            cmd.SetValue("", $"\"{exe}\" --add \"%1\"");
        }
        Notify();
    }

    public static void Unregister()
    {
        foreach (var ext in Formats.Extensions)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"{Root}\.{ext}\shell\{Verb}", throwOnMissingSubKey: false); }
            catch (Exception) { }
        }
        Notify();
    }

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    static void Notify() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
}
