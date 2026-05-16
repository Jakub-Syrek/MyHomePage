# My Mountain Adventures

[![.NET Build & Test](https://github.com/Jakub-Syrek/MyHomePage/workflows/.NET%20Build%20%26%20Test/badge.svg)](https://github.com/Jakub-Syrek/MyHomePage/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)
[![Tests: 43/43](https://img.shields.io/badge/Tests-43%2F43%20passing-brightgreen)](MyHomePage.Tests)

A personal website for storing and browsing mountain adventure videos — hiking, rock climbing, bouldering, and gym sessions. Built with **ASP.NET Core 8 + Blazor Server**, zero database, all storage on the local file system.

## Features

- **6 galleries** — Mountains, Rock Climbing, Bouldering, Indoor Climbing, Calisthenics, Running
- **Strava sync** — OAuth + webhook integration imports activities as gallery items with distance / pace / heart rate / elevation / GPS start point attached. Manual `Import` button on `/admin/strava` opens the editor with everything pre-filled so you can add photos / videos to the session.
- **Adventures map** — Leaflet map at `/map` shows every located item as a pin. Strava-imported activities use a distinct orange marker and a popup with distance / duration / avg HR and a deep link back to Strava.
- **Native social sharing** — per-item share bar for Facebook / WhatsApp / X / Web Share API plus a Download button so each photo or video can be re-uploaded as a native post when link previews aren't enough.
- **Open Graph for scrapers** — server-rendered `/og/{id}` endpoint + UA-aware middleware so Facebook / WhatsApp / Twitter scrapers see proper meta tags despite the SPA being Blazor Server.
- **Automatic video compression** — H.264 via FFmpeg (auto-downloaded on first run), configurable CRF / resolution / bitrate / preset.
- **Hover card expansion** — hover a video thumbnail to get a fullscreen overlay; close with X, background click, or Escape.
- **Admin panel** — login-protected upload, edit, delete, append-media, and Strava connection management.
- **Admin logs viewer** — `/admin/logs` page with real-time log filtering, search, and live auto-refresh (10s interval).
- **AI-assisted upload** — optional Claude integration suggests title / description / location from a few keywords (requires `ANTHROPIC_API_KEY`).
- **Centralised authorship policy** — GitHub Actions workflow scans every push for Claude / Anthropic / OpenAI / GPT attribution and fails the check, enforcing the clean-history rule from `memory/commit_authorship_clean.md`.
- **Comprehensive logging** — Serilog with CLEF format, all requests/responses/errors logged to disk.
- **SVG category icons** — custom vector illustrations in the header and home page cards.
- **No database** — metadata stored as `metadata.json` files alongside each video; Strava tokens stored as a single `strava-tokens.json` on the same volume.
- **SOLID architecture** — Repository, Strategy, Decorator, Result, Options, Factory, Adapter and more (see [ARCHITECTURE.md](ARCHITECTURE.md)).

## Strava integration

The app can pull activities from Strava and surface their metrics (distance,
pace, heart rate, elevation, route polyline) on each gallery item.

### One-time setup

1. Create an API application at <https://www.strava.com/settings/api>.
   - **Authorization Callback Domain**: the host where the app runs
     (e.g. `mountains.cruxbeta.net`; localhost is also allowed for dev).
2. Configure the following values via environment variables or
   `appsettings.Local.json` (never commit them):

   | Setting                       | Description                                          |
   |-------------------------------|------------------------------------------------------|
   | `Strava__ClientId`            | OAuth client id from the developer console.         |
   | `Strava__ClientSecret`        | OAuth client secret from the developer console.     |
   | `Strava__RedirectUri`         | Full URL of `/auth/strava/callback` on this host.   |
   | `Strava__WebhookVerifyToken`  | Shared secret echoed during webhook subscription.   |
   | `Strava__ImportPublicOnly`    | `true` (default) limits auto-import to public runs. |

3. Log in to the admin panel and open **Strava** in the header.
   Click *Connect Strava* and approve consent. Tokens are persisted as
   `strava-tokens.json` next to the video volume and refreshed automatically.

### Webhook subscription (optional, enables push imports)

```bash
curl -X POST https://www.strava.com/api/v3/push_subscriptions \
  -F client_id=$CLIENT_ID \
  -F client_secret=$CLIENT_SECRET \
  -F callback_url=https://<your-host>/api/strava/webhook \
  -F verify_token=$WEBHOOK_VERIFY_TOKEN
```

Strava issues a single `GET` to the callback URL with `hub.mode=subscribe`
and the configured `verify_token`; the app answers with the expected
`hub.challenge` echo. Once verified, every `create` / `update` activity
event triggers an import in the background.

### Manual attach

In the Strava admin page, each recent activity has an *Import* button that
creates a placeholder gallery item in the matching category. Photos / videos
can then be uploaded onto the same item from the standard upload flow.

## Testing

### Running Tests Locally

```bash
dotnet test
```

### Test Coverage

| Suite                          | Tests | Surface                                                              |
|--------------------------------|-------|----------------------------------------------------------------------|
| `VideoServiceTests`            | 10    | Upload validation, delete, update, category filter                   |
| `CredentialServiceTests`       | 3     | Login success, wrong password, missing / invalid file                |
| `LogReaderServiceTests`        | 2     | CLEF parsing, ordering, entry limits                                 |
| `StravaActivityMapperTests`    | 11    | Strava SportType → category mapping, training data, GPS, location    |
| `StravaSyncServiceTests`       | 6     | Token gating, fetch failure, privacy filter, dedup, prefill          |
| `JsonVideoRepositoryTests`     | 3     | Empty folder, malformed metadata, round-trip save                    |
| `OperationResultTests`         | 8     | Success / failure factories for both generic and non-generic results |

- **Testing framework** — NUnit 4.3.2 with NSubstitute 5.1.0 for mocking.
- **Patterns** — Arrange / Act / Assert; helper `[SetUp]` builds mocks; descriptive `MethodName_Scenario_Expected` naming.
- **CI gate** — `.NET Build & Test` workflow on every push and PR. Cannot merge to `master` while red.

## CI/CD Pipeline

### GitHub Actions Workflow

Every push and pull request triggers automated:
- **Build** — `dotnet build` with Release configuration
- **Tests** — `dotnet test` with detailed output logging
- **Requirements** — All tests must pass before merge

Workflow file: `.github/workflows/dotnet.yml`

To configure branch protection:
1. Go to repository Settings → Branches
2. Select the main branch
3. Enable "Require status checks to pass before merging"
4. Check ".NET Build & Test" as required status

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

| Route                          | Page                                                                                |
|--------------------------------|-------------------------------------------------------------------------------------|
| `/`                            | Home — category overview                                                            |
| `/gory`                        | Mountains gallery                                                                   |
| `/wspinaczka-skalowa`          | Rock Climbing gallery                                                               |
| `/bouldering`                  | Bouldering gallery                                                                  |
| `/prowadzeni-hala`             | Indoor Climbing gallery                                                             |
| `/calisthenics`                | Calisthenics gallery                                                                |
| `/running`                     | Running gallery                                                                     |
| `/item/{id}`                   | Single item view (photos / videos + training stats + share bar)                     |
| `/map`                         | Leaflet map with date filter and training pins                                      |
| `/edit-video/{id}`             | Edit metadata + append photos / videos                                              |
| `/login`                       | Admin login                                                                         |
| `/admin/logs`                  | Application logs (admin only) — filter by level, search, auto-refresh               |
| `/admin/strava`                | Strava connection state + recent activities + manual `Import`                       |
| `/auth/strava/login`           | OAuth entry point (admin only) — redirects to Strava authorize page                 |
| `/auth/strava/callback`        | OAuth callback — exchanges code for tokens, redirects to `/admin/strava`            |
| `/auth/strava/disconnect`      | Removes the persisted token set                                                     |
| `/api/strava/webhook`          | Strava push subscription endpoint (handshake + create / update activity events)    |
| `/api/strava/import/{id}`      | Authenticated POST that imports a single activity by id                             |
| `/api/strava/attach/{video}/{activity}` | Authenticated POST that attaches an activity to an existing gallery item   |
| `/og/{id}`                     | Server-rendered Open Graph preview for social-media scrapers                        |
| `/health`                      | Health probe (Railway / monitoring)                                                 |
| `/about`                       | About page                                                                          |

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
{VIDEO_STORAGE_ROOT}/               ← wwwroot/videos locally, /data/videos on Railway
├── strava-tokens.json              ← persisted Strava OAuth tokens (DPAPI alternative)
└── {id}/
    ├── video.mp4                   ← primary video (compressed H.264)
    ├── video-02.mp4                ← additional videos in upload order
    ├── photo-01.jpg                ← additional photos resized to long edge 2560 px
    ├── photo-02.jpg
    ├── thumbnail.jpg               ← auto-generated poster for the primary video
    └── metadata.json               ← title, description, location, GPS, training data, media list
```

### metadata.json

```json
{
  "Id": 1,
  "Title": "Morning Run",
  "Description": "Recovery loop after yesterday's intervals.",
  "FileName": "video.mp4",
  "Location": "Krakow, Poland",
  "Category": "Running",
  "UploadedAt": "2026-05-15T05:17:22Z",
  "FileSizeBytes": 52428800,
  "Latitude": 50.0614,
  "Longitude": 19.9366,
  "Media": [
    { "FileName": "video.mp4", "Type": "Video", "SizeBytes": 50000000, "Order": 0 },
    { "FileName": "photo-01.jpg", "Type": "Image", "SizeBytes": 2400000, "Order": 1 }
  ],
  "Training": {
    "Source": "Strava",
    "ExternalId": "15234112344",
    "ActivityType": "Run",
    "StartTimeUtc": "2026-05-15T05:17:22Z",
    "Duration": "00:32:14",
    "DistanceMeters": 6800,
    "AveragePaceSecondsPerKm": 284,
    "ElevationGainMeters": 38,
    "AverageHeartRate": 152,
    "MaxHeartRate": 174,
    "Calories": 540,
    "RoutePolyline": "_p~iF~ps|U_ulLnnqC...",
    "ExternalUrl": "https://www.strava.com/activities/15234112344"
  }
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
