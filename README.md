# mSlide

A simple Windows app that turns PNG pictures into a PowerPoint slideshow.
Drop in your pictures, put them in order, and save a `.pptx` with one full-screen picture per slide.

## Installing

There is nothing to install. Copy **`dist/mSlide.exe`** to the Windows computer (USB stick, shared drive, OneDrive, etc.) and double-click it.

- Works on 64-bit Windows 10 and 11.
- It's a single file, and nothing else (such as .NET) is needed.
- **First launch:** the app isn't code-signed, so Windows may show a "Windows protected your PC" screen. Click **More info**, then **Run anyway**. Windows only asks once.
- **Emailing it:** most email services block `.exe` attachments. Zip it first, or use a USB stick or cloud drive.

## Using mSlide

1. **Add pictures.** Drag PNG files (or a whole folder of them) onto the window, or click **Add Pictures…**. Dropped files are sorted by name, so `2.png` comes before `10.png`. You can also drop PNGs straight onto the `mSlide.exe` icon.
2. **Put them in order.** Drag thumbnails to where you want them; a line shows where they'll land. You can select several and move them together.
3. **Save.** Click **Save as PowerPoint…**, choose where to save, and open it when it asks.

Every picture is stretched to fill a 16:9 widescreen slide, and the thumbnails show how it will look.

### Buttons and shortcuts

| Action | Button | Shortcut |
|---|---|---|
| Add pictures | Add Pictures… | Ctrl+O |
| Move selected earlier / later | ◀ Move Earlier / Move Later ▶ | Ctrl+← / Ctrl+→ |
| Sort by file name | Sort by Name | |
| Remove selected | Remove | Delete |
| Select all | | Ctrl+A |
| Remove everything | Clear All | |
| Save the presentation | Save as PowerPoint… | Ctrl+S |

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). It builds on macOS, Linux, or Windows:

```bash
./build.sh
```

On Windows, without a bash shell:

```bash
dotnet publish mSlide/mSlide.csproj -c Release -o dist
```

The output is `dist/mSlide.exe`, a self-contained single-file Windows x64 app.

Note: when built on macOS or Linux, the exe uses the default Windows icon. A custom icon can only be embedded when building on Windows.

## Project layout

| Path | What it is |
|---|---|
| `mSlide/MainForm.cs` | The window: drag & drop, thumbnails, reordering, saving |
| `mSlide/PptxWriter.cs` | Writes the `.pptx` and stretches each picture to 16:9 |
| `mSlide/Program.cs` | App entry point |
| `mSlide/mSlide.csproj` | Project settings (single-file, self-contained exe) |
| `build.sh` | Build script |
| `dist/mSlide.exe` | The built app |
