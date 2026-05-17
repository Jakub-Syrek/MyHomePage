# My Mountain Adventures

[![.NET Build & Test](https://github.com/Jakub-Syrek/MyHomePage/workflows/.NET%20Build%20%26%20Test/badge.svg)](https://github.com/Jakub-Syrek/MyHomePage/actions)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/download)

A personal website for storing and browsing mountain adventure videos — hiking, climbing, bouldering, calisthenics, running, cycling, gym sessions. Built with **ASP.NET Core 8 + Blazor Server**, zero database, all storage on the local file system.

## Features

- **Eight galleries** — Mountains, Rock Climbing, Bouldering, Indoor Climbing, Calisthenics, Running, Bicycle, plus a **Multi-sport** aggregate view.
- **Strava sync** — OAuth + webhook integration imports activities as gallery items with distance / pace / heart rate / elevation / effort / GPS start point attached. Manual `Import` button on `/admin/strava` opens the editor pre-filled so you can add photos / videos to the session. Checkboxes next to each activity let you merge several into one multi-sport collection in a single click.
- **Collection merge (multi-sport)** — pick several gallery items (from a category page or directly from the Strava activity list) and merge them into one aggregate "multi-sport" collection. The new master keeps:
  - aggregate `TrainingData` (summed duration / distance / calories / effort, duration-weighted average heart rate, max HR, achievements, PRs);
  - clickable Strava deep link for every source activity;
  - generated `summary.md` file with totals + per-activity table;
  - mosaic cover composed of up to 9 source thumbnails laid out in an NxN grid (1, 1×2, 2×2, 3×2, 3×3) via `SixLabors.ImageSharp`;
  - user-uploaded media (`cover.jpg` placeholders are skipped — only real photos / videos ride along, with fallback to first selected source's cover when no user media is present).
  Source collections stay intact on their original sport pages; merging only adds an aggregate view at `/multisport`.
- **Workout stats strip on every card** — gallery feed cards render a compact pill row with activity type, moving time, calories, average HR, Strava Relative Effort, and "🔗 N activities" indicator for multi-sport masters.
- **Adventures map** — Leaflet map at `/map` shows every located item as a pin. Strava-imported activities use a distinct orange marker and a popup with distance / duration / avg HR and a deep link back to Strava.
- **Open Graph for scrapers** — server-rendered `/og/{id}` endpoint + UA-aware middleware so Facebook / WhatsApp / Twitter scrapers see proper meta tags despite the SPA being Blazor Server.
- **Automatic video compression** — H.264 via FFmpeg (auto-downloaded on first run), configurable CRF / resolution / bitrate / preset.
- **Hover card expansion** — hover a video thumbnail to get a fullscreen overlay; close with X, background click, or Escape.
- **Admin panel** — login-protected upload, edit, delete, append-media, Strava connection management, and per-device passkey settings at `/settings/passkeys`.
- **Admin logs viewer** — `/admin/logs` page with real-time log filtering, search, and live auto-refresh (10s interval).
- **AI-assisted upload** — optional Claude integration suggests title / description / location from a few keywords (requires `ANTHROPIC_API_KEY`).
- **Centralised authorship policy** — GitHub Actions workflow scans every push for Claude / Anthropic / OpenAI / GPT attribution and fails the check, enforcing the clean-history rule from `memory/commit_authorship_clean.md`.
- **Comprehensive logging** — Serilog with CLEF format, all requests/responses/errors logged to disk.
- **No database** — metadata stored as `metadata.json` files alongside each video; Strava tokens, registered passkeys and the multi-sport `summary.md` files all sit on the same volume.
- **SOLID architecture** — Repository, Strategy, Decorator, Result, Options, Factory, Adapter and more (see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)).

## Security model

The site is public read-only — anyone can browse galleries, watch media, see training stats, browse the map and view the multi-sport aggregate. Mutating actions (upload, edit, delete, share, download, copy-link, social-media post, Strava admin, log viewer, passkey management) are reserved for the signed-in admin.

| Action | Anonymous | Signed-in admin |
| --- | --- | --- |
| Browse galleries, watch media, view stats, view map, view multi-sport | ✅ | ✅ |
| Item detail page (title / description / GPS / training panel) | ✅ | ✅ |
| Lightbox `Open` / `Download` direct file link | ✅ | ✅ |
| Lightbox `Copy link`, share-bar (FB / WA / X / native), `Download-for-FB-post` | ❌ | ✅ |
| Edit / Delete on item cards and detail pages | ❌ | ✅ |
| Upload, edit-video, append-media | ❌ | ✅ |
| `/admin/strava`, `/admin/logs`, `/coach` | ❌ | ✅ |
| `/settings/passkeys` and the passkey login button on `/login` | ❌ | ✅ |
| `/multisport` subview + merge mode toggle on category pages | view only | view + create / merge |

Cookie auth (7-day sliding) is the underlying primitive. `<AuthorizeRouteView>` in `App.razor` makes `@attribute [Authorize]` on Blazor pages actually enforce — `RouteView` silently ignored the attribute, so this fix is load-bearing.

### Passkey / fingerprint sign-in (WebAuthn)

A FIDO2 (`Fido2.AspNet 4.0.1`) passkey layer sits on top of the password login. Once you've signed in with email / password, open `/settings/passkeys`, give the credential a label and click **+ Add this device** — Windows Hello / Touch ID / Google Password Manager / a hardware security key registers a public key with the server. The next sign-in only needs the 🔑 button on `/login`.

| Authenticator | Detected as |
| --- | --- |
| Windows Hello / Touch ID / Face ID / on-device PIN | 🫆 Fingerprint / face / PIN |
| Cross-device QR (caBLE / hybrid) | 📱 Phone / cross-device |
| Hardware security key (USB / NFC / Bluetooth / SmartCard) | 🔑 Security key (...) |

Endpoints (`/auth/passkey/{register,login,list,delete}`) live in `Endpoints/PasskeyEndpoints.cs`; passkeys persist as `passkeys.json` on the same volume as Strava tokens. Configure the relying party via the `WebAuthn` section in `appsettings.json` (`RpId`, `RpName`, `Origins`).

## Strava integration

The app can pull activities from Strava and surface their metrics on each gallery item.

### One-time setup

1. Create an API application at <https://www.strava.com/settings/api>.
   - **Authorization Callback Domain**: the host where the app runs (e.g. `mountains.cruxbeta.net`; localhost is also allowed for dev).
2. Configure the following values via environment variables or `appsettings.Local.json` (never commit them):

   | Setting                       | Description                                          |
   |-------------------------------|------------------------------------------------------|
   | `Strava__ClientId`            | OAuth client id from the developer console.         |
   | `Strava__ClientSecret`        | OAuth client secret from the developer console.     |
   | `Strava__RedirectUri`         | Full URL of `/auth/strava/callback` on this host.   |
   | `Strava__WebhookVerifyToken`  | Shared secret echoed during webhook subscription.   |
   | `Strava__ImportPublicOnly`    | `true` (default) limits auto-import to public runs. |

3. Log in to the admin panel and open **Strava** in the header. Click *Connect Strava* and approve consent. Tokens are persisted as `strava-tokens.json` next to the video volume and refreshed automatically.

### Webhook subscription (optional, enables push imports)

```bash
curl -X POST https://www.strava.com/api/v3/push_subscriptions \
  -F client_id=$CLIENT_ID \
  -F client_secret=$CLIENT_SECRET \
  -F callback_url=https://<your-host>/api/strava/webhook \
  -F verify_token=$WEBHOOK_VERIFY_TOKEN
```

Strava issues a single `GET` to the callback URL with `hub.mode=subscribe` and the configured `verify_token`; the app answers with the expected `hub.challenge` echo. Once verified, every `create` / `update` activity event triggers an import in the background.

### Manual attach + merge

On `/admin/strava`:

- **Import as gallery item** (per row) — creates a single stump in the matching category, then opens the editor so you can attach photos / videos. Idempotent: re-importing an existing activity refreshes the stump's training data instead of duplicating it.
- **📥 Re-import recent (last 30)** — sweeps the 30 most recent activities, dedupes against existing stumps, and reports how many were imported / skipped / failed.
- **🖼️ Refresh stump covers** — re-seeds the category placeholder image on pure stumps when a category mapping changes.
- **⇄ Merge N into one** — tick the round Strava-orange checkbox next to two or more activities, then click the floating CTA. Each activity is imported (or matched to an existing stump), the resulting Video ids are handed to `ICollectionMergeService.MergeAsync`, and the new aggregate collection opens in the editor with the mosaic cover already generated.

## Multi-sport collections

Merged collections live at `/multisport` and have a richer detail surface:

- **`TrainingData.SubActivities[]`** — every source activity is preserved as a `SubActivityLink` (Source / ExternalId / ExternalUrl / ActivityType / StartTimeUtc / Duration / DistanceMeters / Calories / AverageHeartRate / SufferScore).
- **Mosaic cover** — `multisport-cover.jpg` is auto-generated by composing up to nine source thumbnails into an NxN grid (cropped square + resized to 512px tiles, JPEG q=85). Sits at `Media[0]`; user-uploaded media follows.
- **`summary.md`** — Markdown report written into the master's directory: totals (duration, distance, elevation gain, calories, avg/max HR, total effort, PRs), per-activity table with clickable Strava links, and the list of merged source collections.
- **Detail page** — `TrainingPanel` on `/item/{id}` renders a "🔗 Source activities (N)" table with one row per source and a Strava-orange pill that opens the original activity in a new tab.
- **Multi-sport subview** — `/multisport` shows the masters newest-first; underneath each master a `<details>` panel collapses the source stumps as nested `CollectionRow`s for one-click drill-down.
- **Sources stay independent** — they keep showing on their original sport page (Running / Calisthenics / …) and remain re-importable from Strava regardless of how many masters reference them. Deleting a master never deletes its sources.

## Testing

```bash
dotnet test
```

| Suite                                  | Surface |
| --- | --- |
| `VideoServiceTests`                    | Upload validation, delete, update, category filter |
| `CredentialServiceTests`               | Login success, wrong password, missing / invalid file |
| `LogReaderServiceTests`                | CLEF parsing, ordering, entry limits |
| `StravaActivityMapperTests`            | Strava SportType → category mapping, training data, GPS, location |
| `StravaSyncServiceTests`               | Token gating, fetch failure, privacy filter, dedup, prefill, multi-sport reimport |
| `JsonVideoRepositoryTests`             | Empty folder, malformed metadata, round-trip save |
| `OperationResultTests`                 | Success / failure factories for both generic and non-generic results |
| `JsonStravaTokenStoreTests`            | Strava token round-trip, atomic overwrite, corrupt-file recovery |
| `JsonPasskeyStoreTests`                | Passkey CRUD, duplicate rejection, by-email / by-credential / by-handle lookups |
| `PasskeyTypeFormatterTests`            | Transports → friendly label mapping (fingerprint / phone / security key) |
| `CollectionMergeServiceTests`          | Aggregator math, source retention, mosaic-cover generation, summary.md, fallback paths |
| `ScraperRewriteMiddlewareTests`        | UA detection, `/item/{id}` → `/og/{id}` rewrite |

- **Testing framework** — NUnit 4 with NSubstitute for mocking.
- **Patterns** — Arrange / Act / Assert; helper `[SetUp]` builds mocks; descriptive `MethodName_Scenario_Expected` naming.
- **CI gate** — `.NET Build & Test` workflow on every push and PR. Cannot merge to `master` while red.

## CI/CD Pipeline

Every push and pull request triggers automated:

- **Build** — `dotnet build` with Release configuration
- **Tests** — `dotnet test` with detailed output logging
- **Authorship guard** — fails the workflow when a commit message contains `Co-Authored-By: Claude` / OpenAI / GPT attribution

Workflow file: `.github/workflows/dotnet.yml`

To configure branch protection:
1. Go to repository Settings → Branches
2. Select the `master` branch
3. Enable "Require status checks to pass before merging"
4. Check ".NET Build & Test" as required status

## Requirements

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)
- Windows / Linux / macOS
- FFmpeg is downloaded automatically on first run (no manual install needed)
- A modern browser (Chrome, Edge, Firefox, Safari) for the WebAuthn passkey flow

## Getting started

```bash
git clone https://github.com/Jakub-Syrek/MyHomePage.git
cd MyHomePage

dotnet restore
dotnet run --project ./MyHomePage.csproj
```

Open <http://localhost:5132> in your browser (or whichever port Kestrel logs at startup).

> **Heads-up:** on Windows, if a previous run is still holding the binary, kill it with `Get-Process MyHomePage -ErrorAction SilentlyContinue | Stop-Process -Force` before rerunning `dotnet run` — otherwise the build fails with `MSB3027` because the exe is locked.

## Configuration

All tuneable values live under sections of `appsettings.json` — no recompile needed:

```jsonc
{
  "VideoStorage": {
    "VideosFolder": "videos",
    "MaxFileSizeBytes": 5368709120,
    "AllowedExtensions": [".mp4", ".webm", ".mkv", ".avi", ".mov", ".m4v",
                          ".jpg", ".jpeg", ".png", ".webp", ".heic"],
    "CompressionCrf": 23,
    "MaxWidthPixels": 1920,
    "MaxHeightPixels": 1080,
    "MaxFrameRate": 30,
    "MaxBitrateKbps": 6000,
    "AudioBitrateKbps": 160,
    "Preset": "slow",
    "Tune": "film",
    "KeyframeIntervalSeconds": 2
  },
  "Strava": {
    "ClientId": "...",
    "ClientSecret": "...",
    "RedirectUri": "https://mountains.cruxbeta.net/auth/strava/callback",
    "Scope": "read,activity:read_all",
    "WebhookVerifyToken": "...",
    "ImportPublicOnly": true
  },
  "WebAuthn": {
    "RpId": "mountains.cruxbeta.net",
    "RpName": "My Mountain Adventures",
    "Origins": [ "https://mountains.cruxbeta.net" ],
    "TimestampDriftToleranceMs": 300000
  }
}
```

## Pages

| Route                              | Page |
| ---------------------------------- | ---- |
| `/`                                | Home — category overview |
| `/gory`                            | Mountains gallery |
| `/wspinaczka-skalowa`              | Rock Climbing gallery |
| `/bouldering`                      | Bouldering gallery |
| `/prowadzeni-hala`                 | Indoor Climbing gallery |
| `/calisthenics`                    | Calisthenics gallery |
| `/running`                         | Running gallery |
| `/bicycle`                         | Bicycle gallery |
| `/multisport`                      | Multi-sport subview — aggregate masters with nested source rows |
| `/item/{id}`                       | Single item view (photos / videos + training stats + admin share bar) |
| `/map`                             | Leaflet map with date filter and training pins |
| `/stats`                           | Per-session statistics |
| `/coach`                           | Weekly coach reports (admin) |
| `/edit-video/{id}`                 | Edit metadata + append photos / videos (admin) |
| `/settings/passkeys`               | Per-device passkey management (admin) |
| `/login`                           | Admin login — supports password + 🔑 passkey button |
| `/logout`                          | Sign out |
| `/admin/logs`                      | Application logs (admin) — filter by level, search, auto-refresh |
| `/admin/strava`                    | Strava connection state + recent activities + `Import` + ⇄ merge |
| `/auth/strava/login`               | OAuth entry point (admin) — redirects to Strava authorize page |
| `/auth/strava/callback`            | OAuth callback — exchanges code for tokens, redirects to `/admin/strava` |
| `/auth/strava/disconnect`          | Removes the persisted token set |
| `/auth/passkey/register/{begin,complete}` | WebAuthn registration ceremony (admin) |
| `/auth/passkey/login/{begin,complete}`    | WebAuthn assertion ceremony (anonymous → cookie sign-in) |
| `/auth/passkey/list`               | Returns the signed-in admin's passkey descriptors |
| `/auth/passkey/{credentialId}` (DELETE) | Removes one of the admin's passkeys |
| `/api/strava/webhook`              | Strava push subscription endpoint (handshake + create / update events) |
| `/api/strava/import/{id}`          | Authenticated POST that imports a single activity by id |
| `/api/strava/attach/{video}/{activity}` | Authenticated POST that attaches an activity to an existing gallery item |
| `/api/collections/merge`           | Authenticated POST — merges N collection ids into one master multi-sport |
| `/og/{id}`                         | Server-rendered Open Graph preview for social-media scrapers |
| `/health`                          | Health probe (Railway / monitoring) |
| `/about`                           | About page |

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

Alternatively, set `ADMIN_USERS` (JSON array `[{"email": "...", "password": "..."}]`) or `ADMIN_EMAIL` + `ADMIN_PASSWORD` as environment variables — these take precedence over the JSON file and are easier to manage on Railway.

Login redirects to the home page and sets a persistent 7-day cookie session. Once signed in, register a passkey at `/settings/passkeys` so future sign-ins only need the 🔑 button on `/login`.

## Logging & Admin Panel

All application events (requests, responses, errors, video operations, passkey ceremonies, merge runs) are logged to disk:

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
- Login attempts (success / failure with IP)
- Passkey register / login ceremonies and assertion errors
- Video operations (upload, edit, delete)
- Strava sync runs (imported / skipped / failed counters)
- Multi-sport merge runs (source ids + resulting master id)
- Errors and exceptions with full context

## Storage layout

```
{VIDEO_STORAGE_ROOT}/                ← wwwroot/videos locally, /data/videos on Railway
├── strava-tokens.json               ← persisted Strava OAuth tokens (refreshed on use)
├── passkeys.json                    ← registered WebAuthn credentials per admin email
└── {id}/
    ├── video.mp4                    ← primary video (compressed H.264)
    ├── video-02.mp4                 ← additional videos in upload order
    ├── photo-01.jpg                 ← additional photos resized to long edge 2560 px
    ├── photo-02.jpg
    ├── thumbnail.jpg                ← auto-generated poster for the primary video
    ├── cover.jpg                    ← (Strava stump only) category placeholder
    ├── multisport-cover.jpg         ← (multi-sport master only) generated mosaic of source thumbs
    ├── summary.md                   ← (multi-sport master only) human-readable totals + Strava links
    ├── s{sourceId}-*.mp4 / *.jpg    ← (multi-sport master only) user media copied from each source, prefixed by source id
    └── metadata.json                ← title, description, location, GPS, training data (with SubActivities[] for masters), media list
```

### metadata.json (regular Strava stump)

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
    "SufferScore": 67,
    "RoutePolyline": "_p~iF~ps|U_ulLnnqC...",
    "ExternalUrl": "https://www.strava.com/activities/15234112344"
  }
}
```

### metadata.json (multi-sport master)

```json
{
  "Id": 42,
  "Title": "Hybrid Sunday",
  "Description": "Easy run + barbell + mobility.",
  "FileName": "multisport-cover.jpg",
  "Category": "Multi-sport",
  "UploadedAt": "2026-05-17T17:30:00Z",
  "Media": [
    { "FileName": "multisport-cover.jpg", "Type": "Image", "SizeBytes": 184000, "Order": 0 }
  ],
  "Training": {
    "Source": "Strava",
    "ExternalId": "",
    "ActivityType": "Multi-sport",
    "Duration": "01:15:00",
    "Calories": 720,
    "AverageHeartRate": 138,
    "SufferScore": 105,
    "SubActivities": [
      {
        "Source": "Strava",
        "ExternalId": "15234112344",
        "ExternalUrl": "https://www.strava.com/activities/15234112344",
        "ActivityType": "Run",
        "StartTimeUtc": "2026-05-17T07:00:00Z",
        "Duration": "00:32:14",
        "DistanceMeters": 6800,
        "Calories": 420,
        "AverageHeartRate": 152,
        "SufferScore": 67
      },
      {
        "Source": "Strava",
        "ExternalId": "15234112999",
        "ExternalUrl": "https://www.strava.com/activities/15234112999",
        "ActivityType": "WeightTraining",
        "StartTimeUtc": "2026-05-17T13:00:00Z",
        "Duration": "00:42:46",
        "Calories": 300,
        "AverageHeartRate": 124,
        "SufferScore": 38
      }
    ]
  }
}
```

## Technology stack

| Layer | Technology |
|---|---|
| UI | Blazor Server, HTML5, CSS3 (glassmorphism, gradients, CSS transitions) |
| Backend | ASP.NET Core 8.0 |
| Video encoding | Xabe.FFmpeg 5.2.6 (wraps FFmpeg) |
| Image processing (covers, mosaic, resize) | SixLabors.ImageSharp 3.1.7 |
| Storage | JSON files + local file system |
| Auth | Cookie authentication (`credentials.json` / env vars) + WebAuthn passkeys (`Fido2.AspNet 4.0.1`) |
| Logging | Serilog with CLEF format (structured, file-based, daily rolling) |
| Architecture | SOLID + Repository, Strategy, Decorator, Result, Options, Factory patterns |

## Design patterns

| Pattern | Where |
|---|---|
| **Repository** | `IVideoRepository` / `JsonVideoRepository`, `IPasskeyStore` / `JsonPasskeyStore`, `IStravaTokenStore` / `JsonStravaTokenStore` — decouples persistence from business logic |
| **Decorator** | `LoggingVideoRepository` — wraps any `IVideoRepository` with transparent structured logging |
| **Strategy** | `ICompressionStrategy` / `H264CompressionStrategy` — swap codec with one line in `Program.cs` |
| **Result** | `OperationResult<T>` — replaces raw `(bool, string, int?)` tuples with type-safe outcomes |
| **Options** | `VideoStorageOptions` / `StravaOptions` / `WebAuthnOptions` via `IOptions<T>` — all tuneable settings configurable from `appsettings.json` |
| **Factory Method** | `Video.Create(...)` — centralises construction of new `Video` instances |
| **Value Object / DTO** | `VideoUploadRequest`, `SubActivityLink`, `PasskeyDescriptor` — immutable parameter groups |
| **Template / DRY** | `CategoryGallery.razor` — one component behind all sport pages and the `/multisport` subview |
| **Service Aggregator** | `CollectionMergeService` — orchestrates repository + storage + ImageSharp to produce one master from N sources |

## SOLID

| Principle | How it's applied |
|---|---|
| **S** Single Responsibility | `VideoService` orchestrates; `JsonVideoRepository` persists; `FileStorageService` handles files; `H264CompressionStrategy` encodes; `CollectionMergeService` merges; `StravaSyncService` syncs |
| **O** Open/Closed | New codec → new class implementing `ICompressionStrategy`. New category → new page + one constant. New authenticator type → new branch in `PasskeyTypeFormatter`. Zero existing code touched. |
| **L** Liskov Substitution | `LoggingVideoRepository` is a fully valid drop-in for `JsonVideoRepository` through `IVideoRepository` |
| **I** Interface Segregation | Small focused interfaces — `IVideoRepository`, `IVideoService`, `ICompressionStrategy`, `IFileStorageService`, `ICredentialRepository`, `IPasskeyStore`, `ICollectionMergeService`, `IStravaApiClient`, `IStravaSyncService`, `IStravaTokenStore` |
| **D** Dependency Inversion | Every component and service injects interfaces — no concrete class crosses layer boundaries |

## Author

Jakub Syrek — <jakubvonsyrek@gmail.com>  
Repository: <https://github.com/Jakub-Syrek/MyHomePage>
