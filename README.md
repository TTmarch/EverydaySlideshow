# Everyday Slideshow

**English** | [日本語](README.ja.md)

Everyday Slideshow is a local-first Windows 11 desktop app for enjoying photos and videos from folders you already own. It is not a cloud album, an ad platform, or a heavy photo manager. Register folders, press play, and let your favorite memories flow quietly while you work, relax, or lightly organize what you see.

## Features

- Register multiple photo/video folders with display names, subfolder scanning, and safe-mode privacy flags.
- Play everything, a single folder, favorites, watch-later items, recently unseen items, or playlists made from any combination of registered folders.
- Smart shuffle avoids repeats until a round is complete, reduces recent repeats, and gently mixes folders, dates, names, and favorites.
- Sequential playback, loop, pause/resume, previous/next, first item, and resume from the previous position.
- Speeds: 1s, 2s, 3s, 5s, 10s, 30s, 1 min, 5 min, and custom seconds.
- Mixed photo/video playback. Videos advance after the media ends when Windows can play the format.
- Mood modes: Default, Work, Bedtime, Safe Mode, and user-defined custom modes.
- Windowed home screen with fullscreen/borderless playback, monitor selection, always-on-top, dark mode, brightness, quiet display, and restored window size.
- During playback, the menu is hidden and controls appear only when the mouse moves.
- Mark favorites, hide/unhide, mark/unmark delete candidates, and add/remove Watch later.
- Delete candidates are moved to the Windows Recycle Bin only after confirmation.
- Local SQLite database for folders, metadata, tags, history, shuffle state, and settings.
- Background scanning, thumbnail/metadata caching, preload-friendly playback, and skip-on-error handling.
- Optional Windows startup, auto-play when this screen is idle, and resume after wake.
- English UI by default, with Japanese available from the Language menu.

## Install

Download a release from GitHub Releases.

- Portable EXE: `EverydaySlideshow-<version>-portable-win-x64.exe`
- Portable ZIP: `EverydaySlideshow-<version>-portable-win-x64.zip`
- Installer EXE: `EverydaySlideshow-<version>-setup-win-x64.exe`
- Installer ZIP: `EverydaySlideshow-<version>-setup-win-x64.zip`

Portable builds keep the app self-contained and can be placed anywhere. The installer build installs into Program Files and creates normal Windows shortcuts.

## Usage

1. Start the app.
2. Choose **Add folder** and select a folder containing photos or videos.
3. Pick a mood mode if desired.
4. Start playback from **Shuffle all**, a registered folder, Favorites, Watch later, Recently unseen, or a playlist.
5. Move the mouse during playback to show controls.
6. Use the menu bar on the home screen for folders, playlists, moods, display settings, language, update checks, and app settings.

## Keyboard Shortcuts

- `Space`: pause/resume
- `Left` / `Right`: previous / next
- `Home`: first item
- `F11` or `F`: toggle fullscreen
- `Esc`: leave fullscreen/borderless, or return home in normal window mode
- `S` or `+`: favorite
- `H`: hide/unhide
- `Delete`: mark/unmark delete candidate

## Supported Formats

Images:

- Required: `jpg`, `jpeg`, `png`, `bmp`, `gif`, `tiff`, `webp`
- Best effort: `heic`, `heif`, `avif`
- RAW preview/thumbnail best effort: `dng`, `cr2`, `cr3`, `nef`, `arw`, `orf`, `rw2`, `raf`, `pef`, `srw`

Videos:

- `mp4`, `m4v`, `mov`, `webm`, `wmv`, `avi`, `mkv`

Actual decoding depends on Windows WIC and Media Foundation codecs. HEIC, AVIF, RAW, and some video files may require Microsoft Store extensions or camera/vendor codecs.

## Updates

The app does not check the internet automatically. Use **Help > Check for updates...** when you want to check manually.

When you choose that menu item, the app contacts the GitHub Releases latest API for this repository. If a newer release exists, it opens the GitHub Releases page in your default browser. The app does not download or install updates automatically.

## Privacy

- No account is required.
- No ads, tracking, cloud sync, or telemetry are included.
- The app does not send photos, videos, metadata, folders, or tags to any external service.
- Network access is only used when you manually choose **Check for updates...**.

Local data:

- Database: `%LOCALAPPDATA%\EverydaySlideshow\slideshow.db`
- Cache: `%LOCALAPPDATA%\EverydaySlideshow\Cache`
- Thumbnails: `%LOCALAPPDATA%\EverydaySlideshow\Cache\Thumbnails`

Reset:

1. Exit the app.
2. Delete `%LOCALAPPDATA%\EverydaySlideshow`.
3. If Windows startup was enabled, disable it in the app first or remove `EverydaySlideshow` from `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

## License

This project is licensed under the Creative Commons Attribution-NonCommercial 4.0 International License (`CC BY-NC 4.0`).

Commercial use is not permitted without separate permission. See [LICENSE](LICENSE) for details.

## Known Limitations

- Video playback uses WPF `MediaElement`, so format support depends on Windows codecs.
- HEIC/AVIF/RAW support depends on installed Windows codecs. RAW support is preview/thumbnail oriented, not full raw development.
- Animated GIF behavior depends on the Windows decoder and may show only the first frame.
