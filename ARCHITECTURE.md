# Architecture — My Mountain Adventures

## High-level overview

```
Browser (HTML5 / CSS / JS)
        │
        │  SignalR (Blazor Server)
        ▼
┌───────────────────────────────────────────────┐
│              Blazor Components                │
│                                               │
│  Pages/           Components/                 │
│  Index.razor      CategoryGallery.razor       │
│  Gory.razor  ───► (thin wrapper)              │
│  Bouldering.razor                             │
│  ...                                          │
│                   VideoUploadCategory.razor   │
│                   VideoEditor.razor           │
│                   VideoGallery.razor          │
└──────────────────────┬────────────────────────┘
                       │ IVideoService
                       ▼
┌───────────────────────────────────────────────┐
│              VideoService                     │
│   Orchestrates: validation, storage,          │
│   compression, persistence                   │
└──────┬─────────────┬────────────────┬─────────┘
       │             │                │
  IVideoRepository  IFileStorageService  ICompressionStrategy
       │             │                │
       ▼             ▼                ▼
LoggingVideo-   FileStorage-    H264Compression-
Repository      Service         Strategy
(Decorator)     (file I/O)      (FFmpeg/libx264)
       │
       ▼
JsonVideoRepository
(JSON files on disk)
```

## Folder structure

```
MyHomePage/
├── Abstractions/                  ← all interfaces (Dependency Inversion)
│   ├── IVideoRepository.cs
│   ├── IVideoService.cs
│   ├── ICompressionStrategy.cs
│   ├── IFileStorageService.cs
│   └── ICredentialRepository.cs
│
├── Models/
│   ├── Video.cs                   ← domain model + static factory method
│   ├── OperationResult.cs         ← Result pattern (replaces bool+string tuples)
│   └── VideoUploadRequest.cs      ← Value object / DTO (sealed record)
│
├── Options/
│   └── VideoStorageOptions.cs     ← Options pattern (IOptions<T>, appsettings.json)
│
├── Services/
│   ├── VideoService.cs            ← orchestration, depends only on interfaces
│   ├── JsonVideoRepository.cs     ← Repository pattern, JSON persistence
│   ├── LoggingVideoRepository.cs  ← Decorator pattern, transparent logging
│   ├── FileStorageService.cs      ← file-system operations (SRP)
│   ├── H264CompressionStrategy.cs ← Strategy pattern, FFmpeg H.264
│   └── CredentialService.cs       ← implements ICredentialRepository
│
├── Components/
│   ├── CategoryGallery.razor      ← reusable gallery (DRY, Template pattern)
│   ├── VideoUploadCategory.razor
│   ├── VideoUpload.razor
│   ├── VideoEditor.razor
│   └── VideoGallery.razor
│
├── Pages/
│   ├── Index.razor                ← home, category cards
│   ├── Gory.razor                 ← thin wrapper → CategoryGallery
│   ├── Bouldering.razor           ← thin wrapper → CategoryGallery
│   ├── WspinaczkaSkalowa.razor    ← thin wrapper → CategoryGallery
│   ├── ProwadzieniHala.razor      ← thin wrapper → CategoryGallery
│   ├── About.razor
│   ├── Login.cshtml / .cs
│   ├── Logout.cshtml / .cs
│   └── _Host.cshtml
│
├── Shared/
│   └── MainLayout.razor           ← nav, footer, video overlay JS
│
├── wwwroot/
│   ├── videos/{id}/               ← video.mp4 + metadata.json
│   ├── images/
│   │   └── icons/                 ← SVG category icons (in git)
│   └── css/site.css
│
├── Program.cs                     ← DI wiring, middleware pipeline
├── appsettings.json               ← VideoStorage section
└── MyHomePage.csproj
```

## Design patterns

### Repository
`IVideoRepository` → `JsonVideoRepository`  
Decouples persistence (JSON files) from business logic. Swapping to a database requires only a new implementation — nothing else changes.

### Decorator
`LoggingVideoRepository` wraps any `IVideoRepository` and adds structured logging. Registered in `Program.cs` via manual wrapping so the inner class stays unaware of logging.

```csharp
builder.Services.AddScoped<JsonVideoRepository>();
builder.Services.AddScoped<IVideoRepository>(sp =>
    new LoggingVideoRepository(
        sp.GetRequiredService<JsonVideoRepository>(),
        sp.GetRequiredService<ILogger<LoggingVideoRepository>>()));
```

### Strategy
`ICompressionStrategy` → `H264CompressionStrategy`  
VideoService never imports FFmpeg directly. To switch to H.265 or AV1, implement `ICompressionStrategy` and change one line in `Program.cs`.

### Result pattern
`OperationResult` / `OperationResult<T>` replaces raw `(bool, string, int?)` tuples throughout the service layer. Makes success/failure handling explicit and type-safe.

### Options pattern
`VideoStorageOptions` is registered via `IOptions<T>` and bound to the `"VideoStorage"` section of `appsettings.json`. All codec settings (CRF, bitrate, resolution, frame rate) are configurable without recompiling.

### Factory method
`Video.Create(...)` static factory on the `Video` class. Centralises construction of new video instances so callers never write object initialisers manually.

### Value object / DTO
`VideoUploadRequest` is a `sealed record`. It replaces a long raw parameter list and enforces immutability.

### Template / DRY (CategoryGallery)
`CategoryGallery.razor` holds all gallery markup and logic. The four category pages (`Gory`, `Bouldering`, etc.) are now 6-line wrappers — adding a new category requires only a new page and a new constant in `VideoCategories`.

## SOLID checklist

| Principle | How it's applied |
|---|---|
| **S** — Single Responsibility | `VideoService` only orchestrates. `JsonVideoRepository` only persists. `FileStorageService` only handles files. `H264CompressionStrategy` only encodes. |
| **O** — Open/Closed | New compression codec → new class implementing `ICompressionStrategy`. New category → new page + constant. Zero existing code modified. |
| **L** — Liskov Substitution | `LoggingVideoRepository` is a fully valid drop-in for `JsonVideoRepository` through `IVideoRepository`. |
| **I** — Interface Segregation | Five small focused interfaces instead of one large service class. |
| **D** — Dependency Inversion | Every component, page, and service injects interfaces. No concrete class is imported from outside its own layer. |

## Request flows

### Display gallery
```
Browser → CategoryGallery.razor
              ↓ IVideoService.GetVideosByCategoryAsync()
          VideoService
              ↓ IVideoRepository.GetAllAsync()
          LoggingVideoRepository  [logs timing]
              ↓
          JsonVideoRepository     [reads metadata.json files]
              ↓
          List<Video> → Blazor renders grid
```

### Upload video
```
Browser → VideoUploadCategory.razor (builds VideoUploadRequest)
              ↓ IVideoService.UploadVideoAsync(request)
          VideoService
              ├─ Validate (extension, size)          [OperationResult]
              ├─ IFileStorageService.SaveUploadedFileAsync()
              ├─ ICompressionStrategy.CompressAsync()  [H.264/FFmpeg]
              └─ IVideoRepository.SaveAsync(video)   [writes metadata.json]
```

### Edit video
```
Browser → VideoEditor.razor (/edit-video/{id})
              ↓ IVideoService.GetVideoByIdAsync(id)
              ↓ [user edits form]
              ↓ IVideoService.UpdateVideoAsync(...)
          VideoService → IVideoRepository.SaveAsync()
              ↓ OperationResult returned → feedback shown
```

## Storage format

```
wwwroot/videos/
└── {id}/
    ├── video.mp4
    └── metadata.json
```

```json
{
  "Id": 1,
  "Title": "Summer Hike",
  "Description": "...",
  "FileName": "video.mp4",
  "Location": "Tatra Mountains",
  "Category": "Mountains",
  "UploadedAt": "2026-05-06T10:00:00Z",
  "FileSizeBytes": 52428800
}
```

## Testing

### Unit tests (25 total)

**MyHomePage.Tests** — NUnit framework with NSubstitute for mocking.

- **Models** (8 tests)
  - `OperationResult` — success/failure with/without messages
  - `OperationResult<T>` — data carrying with different outcomes

- **Services** (15 tests)
  - `VideoService` (10 tests) — deletion, updates, filtering, retrieval
  - `CredentialService` (3 tests) — email/password validation, file handling
  - `LogReaderService` (2 tests) — directory handling, entry limits

- **Repositories** (3 tests)
  - `JsonVideoRepository` — ID generation, basic repository contract

### Test organization

Tests follow the **Arrange-Act-Assert (AAA)** pattern:
```csharp
[Test]
public async Task DeleteVideoAsync_VideoExists_ReturnsSuccess()
{
    // Arrange — set up mocks and test data
    _mockRepository.DeleteAsync(1).Returns(true);

    // Act — call the method under test
    var result = await _service.DeleteVideoAsync(1);

    // Assert — verify outcomes
    Assert.That(result.IsSuccess, Is.True);
}
```

### Running tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter OperationResultTests
```

### CI/CD integration

GitHub Actions workflow (`.github/workflows/dotnet.yml`) runs:
1. Build (Release configuration)
2. Tests (with detailed logging)
3. Requires all tests pass before merge

## Dependencies

| Package | Purpose |
|---|---|
| `Xabe.FFmpeg 5.2.6` | FFmpeg process wrapper |
| `Xabe.FFmpeg.Downloader 5.2.6` | Auto-download FFmpeg binaries on first run |
| `NUnit 4.3.2` | Unit testing framework |
| `NSubstitute 5.1.0` | Mocking library for tests |
| ASP.NET Core 8.0 (built-in) | Blazor Server, Cookie Auth, Razor Pages, DI, Options |

---

**Author:** Jakub Syrek — <jakubvonsyrek@gmail.com>
