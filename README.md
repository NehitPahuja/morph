# Morph

A local file converter for Windows. Drop files on the window, or right-click them in Explorer, pick a
format, and the converted files land next to the originals. Nothing is uploaded, and originals are never
touched or overwritten (`photo.png` → `photo.webp`, then `photo (2).webp`, …).

<p align="center">
  <a href="media/morph-opener.mp4"><img src="media/morph-opener-preview.webp" width="800" alt="Morph intro: a pile of files in the wrong format is converted in the Morph window and from Explorer's right-click menu"></a>
  <br>
  <sub>▶ <a href="media/morph-opener.mp4">Watch the 30-second intro with sound</a> (MP4, 1080p). The music is an original track composed for it.</sub>
</p>

| You give it | You get |
|---|---|
| Audio (mp3, wav, flac, m4a, ogg, opus, wma, aiff, …) | MP3, AAC (m4a), Opus, OGG, FLAC, WAV |
| Video (mp4, mov, mkv, webm, avi, wmv, ts, …) | MP4, WebM, MKV, MOV (H.264 / VP9), animated GIF, **or any audio format** (extracts the sound) |
| Images (jpg, png, gif, bmp, tiff, webp, heic, avif, ico) | JPG, PNG, WebP, AVIF, BMP, TIFF, GIF, ICO |
| PDF | PNG or JPG, one image per page, in a new folder |

Every format has one **Quality** choice — *Smaller*, *Balanced* or *Best* — and the window says what
it means for that kind of file (e.g. video *Smaller* = up to 720p). Choices are remembered.

## Download

Get **Morph.exe** from the [latest release](../../releases/latest). It's a single file with everything
it needs; put it anywhere (for example `%LOCALAPPDATA%\Programs\Morph`) and run it. Windows 10 (2004) or
11, 64-bit.

- Audio, video, WebP, AVIF and ICO need **ffmpeg**: `winget install Gyan.FFmpeg`. Morph finds it on its
  own, even while open. JPG, PNG, BMP, TIFF, GIF and PDF pages work without it.
- Turn on **Show in Explorer's right-click menu** at the bottom left of the window to add
  **Convert with Morph** to Explorer. If you move the exe later, switch it off and on again.
- Morph.exe isn't code-signed, so SmartScreen may say "Windows protected your PC" the first time:
  click **More info → Run anyway**.

## Build from source

`.\scripts\build-exe.ps1` makes `dist\Morph.exe`.

To install the build like an app instead, you need Windows 10 (2004) or 11, the .NET 10 SDK and ffmpeg:

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Gyan.FFmpeg
.\scripts\install.ps1
```

This installs for your account only (no admin rights): the app in `%LOCALAPPDATA%\Programs\Morph`, a
Start menu entry, **Send to → Morph**, and **Convert with Morph** in Explorer's right-click menu.

On Windows 11 the menu entry is under **Show more options** (or select files and press **Shift+F10**).
The compact top-level menu only accepts entries from signed app packages, which needs a code-signing
certificate this project doesn't have. Selecting more than 15 files hides Explorer's entry (a Windows
rule); **Send to → Morph** has no limit.

The switch at the bottom left of the window adds or removes the Explorer entry too.

`.\scripts\uninstall.ps1` removes everything (`-KeepSettings` keeps your remembered choices).

## How it works

| Part | File |
|---|---|
| Formats, file kinds, quality hints | `src/Morph/Engine/Formats.cs` |
| Finding ffmpeg (PATH, winget's folders, a `tools\` folder next to the exe) | `Engine/Tools.cs` |
| Running ffmpeg with progress and cancel | `Engine/Ffmpeg.cs` |
| Audio and video settings | `Engine/MediaConverter.cs` |
| Images: Windows Imaging (WIC), plus ffmpeg for WebP / AVIF / ICO | `Engine/ImageConverter.cs` |
| PDF pages: Windows' built-in PDF renderer | `Engine/PdfPages.cs` |
| Where output goes, unique names | `Engine/Output.cs` |
| Explorer menu (per-user registry) | `Engine/ExplorerMenu.cs` |
| Window | `src/Morph/UI/` |

Each file Explorer hands over starts Morph once; extra copies pass their file to the open window through
a named pipe and exit, so a multi-file right-click becomes one batch.

If the original's folder can't be written to, output goes to `Downloads\Morph`. A failed or stopped
conversion deletes whatever it had started writing.

### Command line

```powershell
Morph.exe --formats                                    # list format ids
Morph.exe --convert webp --quality best photo.png      # convert without the window
Morph.exe --convert mp3 --out D:\music *.wav
```

### Tests

`scripts\smoke-test.ps1` generates sample audio, video (with odd dimensions), images (with transparency)
and a PDF, converts each to every format offered for it, and checks the results.
