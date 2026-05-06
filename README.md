# My Mountain Adventures

A personal website for storing and browsing mountain adventure videos — hiking, rock climbing, bouldering, and gym sessions. Built with **ASP.NET Core 8 + Blazor Server**, zero database, all storage on the local file system.

## Features

- **4 video galleries** — Mountains, Rock Climbing, Bouldering, Indoor Climbing
- **Automatic video compression** — H.264 via FFmpeg (auto-downloaded on first run), ~720p/CRF-30, configurable
- **Hover card expansion** — hover a video thumbnail to get a fullscreen overlay; close with X, background click, or Escape
- **Admin panel** — login-protected upload, edit, and delete
- **Admin logs viewer** — `/admin/logs` page with real-time log filtering, search, and live auto-refresh (10s interval)
- **Comprehensive logging** — Serilog with CLEF format, all requests/responses/errors logged to disk
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
| `/admin/logs` | Application logs (admin only) — filter by level, search, auto-refresh |
| `/about` | About page |

## Authentication & Credentials

Create a `credentials.json` file in the project root with admin login credentials:

```json
{
    "users": [
        {
            "email": "admin@mountains.com",
            "password": "Admin123!Mountain"
        }
    ]
}
```

Login redirects to the home page and sets a persistent 7-day cookie session.

## Logging & Admin Panel

All application events (requests, responses, errors, video operations) are logged to disk:

- **Log format**: CLEF (Compact Log Event Format) — JSON-based, structured logging
- **Log location**: `logs/app-{date}.clef` (daily rolling, 14 days retained)
- **Log levels**: Debug, Information, Warning, Error, Fatal
- **View logs**: Visit `/admin/logs` (requires login) to:
  - Filter by log level (Debug, Info, Warning, Error, Fatal)
  - Search by message text or source
  - Toggle auto-refresh (10-second interval)
  - See real-time statistics (counts by level)
  - Expand exceptions to see full stack traces

**What's logged**:
- All HTTP requests (method, path, IP, response time)
- Login attempts (success/failure with IP)
- Video operations (upload, edit, delete)
- Errors and exceptions with full context

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
| Logging | Serilog with CLEF format (structured, file-based, daily rolling) |
| Architecture | SOLID + Repository, Strategy, Decorator, Result, Options, Factory patterns |

## Design patterns

| Pattern | Where |
|---|---|
| **Repository** | `IVideoRepository` / `JsonVideoRepository` — decouples persistence from business logic |
| **Decorator** | `LoggingVideoRepository` — wraps any `IVideoRepository` with transparent structured logging |
| **Strategy** | `ICompressionStrategy` / `H264CompressionStrategy` — swap codec with one line in `Program.cs` |
| **Result** | `OperationResult<T>` — replaces raw `(bool, string, int?)` tuples with type-safe outcomes |
| **Options** | `VideoStorageOptions` via `IOptions<T>` — all codec settings configurable from `appsettings.json` |
| **Factory Method** | `Video.Create(...)` — centralises construction of new `Video` instances |
| **Value Object / DTO** | `VideoUploadRequest` sealed record — immutable parameter group for uploads |
| **Template / DRY** | `CategoryGallery.razor` — one component behind all four category pages |

## SOLID

| Principle | How it's applied |
|---|---|
| **S** Single Responsibility | `VideoService` orchestrates; `JsonVideoRepository` persists; `FileStorageService` handles files; `H264CompressionStrategy` encodes |
| **O** Open/Closed | New codec → new class implementing `ICompressionStrategy`. New category → new page + one constant. Zero existing code touched. |
| **L** Liskov Substitution | `LoggingVideoRepository` is a fully valid drop-in for `JsonVideoRepository` through `IVideoRepository` |
| **I** Interface Segregation | Five small focused interfaces (`IVideoRepository`, `IVideoService`, `ICompressionStrategy`, `IFileStorageService`, `ICredentialRepository`) |
| **D** Dependency Inversion | Every component and service injects interfaces — no concrete class crosses layer boundaries |

## Author

Jakub Syrek — <jakubvonsyrek@gmail.com>  
Repository: <https://github.com/Jakub-Syrek/MyHomePage>
