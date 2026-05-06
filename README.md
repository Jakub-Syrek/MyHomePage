# My Mountain Adventures

A personal website for storing and browsing mountain adventure videos — hiking, rock climbing, bouldering, and gym sessions. Built with **ASP.NET Core 8 + Blazor Server**, zero database, all storage on the local file system.

## Features

- **4 video galleries** — Mountains, Rock Climbing, Bouldering, Indoor Climbing
- **Automatic video compression** — H.264 via FFmpeg (auto-downloaded on first run), ~720p/CRF-30, configurable
- **Hover card expansion** — hover a video thumbnail to get a fullscreen overlay; close with X, background click, or Escape
- **Admin panel** — login-protected upload, edit, and delete
- **SVG category icons** — custom vector illustrations in the header and home page cards
- **No database** — metadata stored as `metadata.json` files alongside each video
- **SOLID architecture** — Repository, Strategy, Decorator, Result, Options, Factory, and more (see [ARCHITECTURE.md](ARCHITECTURE.md))

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Windows / Linux / macOS
- FFmpeg is downloaded automatically on first run (no manual install needed)

## Getting started

```bash
git clone https://github.com/Jakub-Syrek/MyHomePage.git
cd MyHomePage

dotnet restore
dotnet run
```

Open <http://localhost:5000> in your browser.

## Configuration

All tuneable values live under the `"VideoStorage"` section in `appsettings.json` — no recompile needed:

```json
{
  "VideoStorage": {
    "VideosFolder": "videos",
    "MaxFileSizeBytes": 2147483648,
    "AllowedExtensions": [".mp4", ".webm", ".mkv", ".avi"],
    "CompressionCrf": 30,
    "MaxWidthPixels": 1280,
    "MaxHeightPixels": 720,
    "MaxFrameRate": 30,
    "MaxBitrateKbps": 2500,
    "AudioBitrateKbps": 96
  }
}
```

## Pages

| Route | Page |
|---|---|
| `/` | Home — category overview |
| `/gory` | Mountains gallery |
| `/wspinaczka-skalowa` | Rock Climbing gallery |
| `/bouldering` | Bouldering gallery |
| `/prowadzeni-hala` | Indoor Climbing gallery |
| `/edit-video/{id}` | Edit video metadata |
| `/login` | Admin login |
| `/about` | About page |

## Storage layout

```
wwwroot/videos/
└── {id}/
    ├── video.mp4        ← compressed output (H.264)
    └── metadata.json    ← title, description, location, category, date, size
```

### metadata.json

```json
{
  "Id": 1,
  "Title": "Summer hike",
  "Description": "...",
  "FileName": "video.mp4",
  "Location": "Tatra Mountains",
  "Category": "Mountains",
  "UploadedAt": "2026-05-06T10:00:00Z",
  "FileSizeBytes": 52428800
}
```

## Technology stack

| Layer | Technology |
|---|---|
| UI | Blazor Server, HTML5, CSS3 (glassmorphism, gradients, CSS transitions) |
| Backend | ASP.NET Core 8.0 |
| Video encoding | Xabe.FFmpeg 5.2.6 (wraps FFmpeg) |
| Storage | JSON files + local file system |
| Auth | Cookie authentication (`credentials.json`) |
| Architecture | SOLID + Repository, Strategy, Decorator, Result, Options, Factory patterns |

## Author

Jakub Syrek — <jakubvonsyrek@gmail.com>  
Repository: <https://github.com/Jakub-Syrek/MyHomePage>
