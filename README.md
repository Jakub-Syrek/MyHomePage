# My Mountain Adventures - Home Page

A modern website for hosting mountain adventure videos, built with ASP.NET Core and Blazor.

## Features

✨ **Modern Design** — Responsive interface with animations  
📹 **Video Gallery** — Upload and manage videos  
🎨 **Beautiful UI** — Gradients, cards, CSS animations, glassmorphism effects  
🔐 **Content Editing** — Edit title, description, location for each video  
📁 **File Storage** — No database, everything stored in JSON files  
⚡ **Fast Performance** — No SQL Server dependency  
🎬 **Interactive Cards** — Hover expansion with close options (X button, background click, Escape key)

## Requirements

- .NET 8.0 or newer
- Windows/Linux/macOS

## Installation and Running

```bash
# Clone the repository
git clone https://github.com/Jakub-Syrek/MyHomePage.git
cd MyHomePage

# Restore packages
dotnet restore

# Run the server
dotnet run
```

Open http://localhost:5000 (or https://localhost:5001) in your browser.

## Folder Structure

```
wwwroot/
├── videos/              # Videos organized in subfolders
│   ├── 1/              # Video ID
│   │   ├── video.mp4   # Video file
│   │   └── metadata.json
│   ├── 2/
│   └── 3/
├── css/
│   └── site.css        # Global styles
├── images/
│   └── *.jpg           # Background images
└── js/
    └── video-handlers.js
```

## metadata.json Structure

```json
{
  "Id": 1,
  "Title": "Summer Hike",
  "Description": "Amazing day in mountains...",
  "FileName": "video.mp4",
  "Location": "Tatra Mountains",
  "UploadedAt": "2026-05-04T12:30:00Z",
  "Category": "Mountains",
  "FileSizeBytes": 524288000
}
```

## Technology Stack

- **Frontend:** Blazor Server, HTML, CSS with animations and glassmorphism
- **Backend:** ASP.NET Core 8.0
- **Storage:** JSON files + folders (no database)
- **Patterns:** Dependency Injection, Service Pattern, Event Delegation
- **UI Effects:** Hover expansion overlays, backdrop filters, CSS transitions

## Features

### Video Expansion
- Hover over a video card to expand it (2x size)
- Close with: X button, background click, or Escape key
- Dark overlay background for focus
- Original card stays in grid layout

### Video Gallery Pages
- **Home** (/) — Category overview
- **Mountains** (/gory) — Mountain hiking videos
- **Rock Climbing** (/wspinaczka-skalowa) — Rock climbing videos
- **Bouldering** (/bouldering) — Bouldering problem videos
- **Indoor Climbing** (/prowadzeni-hala) — Gym climbing videos

## Components

### Pages
- **Index.razor** — Home page with category cards
- **Gory.razor** — Mountains gallery
- **WspinaczkaSkalowa.razor** — Rock climbing gallery
- **Bouldering.razor** — Bouldering gallery
- **ProwadzieniHala.razor** — Indoor climbing gallery

### Shared
- **MainLayout.razor** — Main layout with navigation, video expansion JavaScript
- **VideoUploadCategory.razor** — Upload component with category support
- **VideoEditForm.razor** — Edit video metadata form

## Services

### VideoService
Core service for video operations:
- `GetVideosByCategoryAsync(category)` — Get videos by category
- `GetVideoByIdAsync(id)` — Get specific video details
- `UploadVideoAsync(file, title, description, location, category)` — Upload new video
- `UpdateVideoAsync(id, title, description, location)` — Update video metadata
- `DeleteVideoAsync(id)` — Delete video

## Styling Features

- **Glassmorphism** — Semi-transparent cards with backdrop blur
- **Gradient backgrounds** — Purple/blue gradients
- **Responsive grid** — Auto-fill grid layout
- **Smooth transitions** — CSS animations on all interactive elements
- **Dark overlay** — Focus effect on expanded cards

## Limitations

- Max 2 GB per file
- Supported formats: MP4, WebM, MKV, AVI (check VideoService for exact list)
- Local file storage only (not cloud-based)
- Videos stored in wwwroot/videos/ folder

## Performance Tips

- Compress large video files before upload (recommended: 50-200 MB)
- Use H.264 or H.265 codec for better compatibility
- Target bitrate: 2-5 Mbps for good quality/size balance

## Developer

Jakub Syrek

---

💡 Want to add authentication? We can add ASP.NET Core Identity!  
📱 Want mobile app? Mobile-first responsive design included!
