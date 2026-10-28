# Viggi Slideshow

A Windows screensaver that shows the photos and videos in a folder and all its subfolders,
in random or folder order.

## Install

```powershell
.\install.ps1
```

This builds a self-contained release (no .NET install needed), puts it in
`%LOCALAPPDATA%\Programs\ViggiSlideshow`, makes it the screensaver and opens Screen Saver
Settings, where **Settings…** picks the folder and **Preview** tries it. `-NoActivate` installs
without switching screensavers.

## Keys while it runs

| Key | Does |
|---|---|
| Right / Down / N | Next |
| Left / Up / B | Previous (goes back through what this run has shown) |
| Space / P | Pause or resume |
| F | Show or hide the file name |
| I | Show or hide file info: size, date taken, camera, files in the folder |
| V | Photos and videos → photos only → videos only |
| M | Sound on or off |
| R | Random or folder order (folder order carries on from the picture on screen) |
| 1-5, 6, 7, 8, 9 | Seconds per photo: 1-5, 10, 20, 30, 60; 0 for the default (5) |
| S / D / H | Show settings / debug info / this list |
| X | Hide this file from the show for good (see below) |
| Enter / O | Open the file's folder in Explorer and exit |
| Esc / Q | Exit |

Moving the mouse or clicking also exits. Toggles are saved for next time. Other keys do nothing,
so a stray key press doesn't end the show.

## How it works

- **WPF on .NET 9**, one window per monitor. Each monitor runs its own slideshow from a shared
  library, so they show different things; only the primary monitor plays sound.
- **Videos play through VLC** (LibVLCSharp), which handles everything in the library, from
  old camera AVI/MPG to 4K HEVC from GoPros and drones, with hardware decoding. VLC decodes into
  memory and WPF draws the frames, so videos crossfade and carry labels like photos do.
- **Nothing can stall it.** A file that won't decode, a video VLC rejects, one that never starts,
  stalls, or plays slower than real time: each is logged, skipped for the rest of the run, and the
  show moves on. VLC is never called from its own callbacks, and players are stopped on a
  background thread. Nothing shows a dialog; errors go to `%LOCALAPPDATA%\ViggiSlideshow\log.txt`.
- **Random order** is a shuffle: nothing repeats until everything has been shown. The folder is
  scanned in the background in random folder order, so the show starts at once on a large library.
- **Folder order** follows Explorer's sort (natural numbers, files before subfolders) and resumes
  where the last run left off.
- **Hidden files** (the X key) are listed in `%LOCALAPPDATA%\ViggiSlideshow\hidden.txt`, one full
  path per line. The scan and folder order leave them out from then on. To bring one back, delete
  its line; blank lines and lines starting with `#` are ignored, and case doesn't matter.
- **Settings** live in `HKCU\SOFTWARE\ViggiSlideshow`, the same values the old WinForms version used.

## Development

```
dotnet test
dotnet run --project src/ViggiSlideshow                # runs the screensaver full screen
dotnet run --project src/ViggiSlideshow -- /c          # settings window
```

`ViggiSlideshow.exe /test <folder> <count> <outdir> [mixed|photos|videos] [video seconds]` is a soak
test on real files: it runs the slideshow in an off-screen window, saves a snapshot of each file
shown to `<outdir>`, and writes `summary.txt`. Any 30 seconds with nothing shown or skipped counts as
a hang. From Git Bash, prefix it with `MSYS_NO_PATHCONV=1`, or `/test` is rewritten into a path
and the real screensaver starts instead.

The old WinForms / Windows Media Player version is in history at commit `3635f7c`.
