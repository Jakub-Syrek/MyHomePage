# My Mountain Adventures - Architektura Rozwiązania

## 📐 Przegląd systemu

```
┌─────────────────────────────────────────────────┐
│         Browser (HTML5/CSS/JavaScript)          │
├─────────────────────────────────────────────────┤
│          Blazor Server Components               │
│  ┌──────────────┐  ┌──────────────┐            │
│  │VideoGallery  │  │VideoUpload   │            │
│  │VideoEditor   │  │MainLayout    │            │
│  └──────────────┘  └──────────────┘            │
├─────────────────────────────────────────────────┤
│          ASP.NET Core 8.0 Backend              │
│  ┌───────────────────────────────┐             │
│  │      VideoService             │             │
│  │  - GetAllVideosAsync()        │             │
│  │  - UploadVideoAsync()         │             │
│  │  - UpdateVideoAsync()         │             │
│  │  - DeleteVideoAsync()         │             │
│  └───────────────────────────────┘             │
├─────────────────────────────────────────────────┤
│        File System Storage                     │
│  ┌──────────────────────────────┐              │
│  │ wwwroot/videos/              │              │
│  │ ├── 1/                       │              │
│  │ │   ├── video.mp4           │              │
│  │ │   └── metadata.json       │              │
│  │ ├── 2/                       │              │
│  │ └── 3/                       │              │
│  └──────────────────────────────┘              │
└─────────────────────────────────────────────────┘
```

## 📁 Struktura projektu

```
MyHomePage/
├── Components/              # Komponenty Blazor
│   ├── VideoGallery.razor    # Galerka wideo
│   ├── VideoUpload.razor     # Upload wideo
│   └── VideoEditor.razor     # Edycja metadanych
├── Models/                  # Modele danych
│   ├── Video.cs              # Model wideo
│   └── VideoMetadata.cs      # (helper)
├── Services/                # Logika biznesowa
│   └── VideoService.cs       # Zarządzanie wideo
├── Pages/                   # Strony
│   ├── Index.razor           # Główna strona (galerka)
│   ├── About.razor           # O projekcie
│   ├── Error.cshtml          # Błędy
│   └── _Host.cshtml          # Host
├── Shared/                  # Komponenty wspólne
│   └── MainLayout.razor      # Layout
├── wwwroot/                 # Zasoby statyczne
│   ├── videos/               # Wideo (folder-based storage)
│   ├── css/
│   │   └── site.css          # Globalne style
│   └── favicon.png
├── App.razor                # Root komponent
├── Program.cs               # Konfiguracja
├── appsettings.json         # Konfiguracja
└── MyHomePage.csproj        # Plik projektu
```

## 🔄 Flow aplikacji

### 1. **Wyświetlanie galerki**
```
User → Browser → VideoGallery.razor 
                    ↓
                VideoService.GetAllVideosAsync()
                    ↓
                Odczyt folderów wwwroot/videos/
                    ↓
                Deserializacja metadata.json
                    ↓
                Wyświetl galerę HTML
```

### 2. **Upload wideo**
```
User → Wybierz plik → VideoUpload.razor
           ↓
       Walidacja (format, rozmiar)
           ↓
       VideoService.UploadVideoAsync()
           ↓
       Stwórz folder wwwroot/videos/{id}/
           ↓
       Zapisz plik video + metadata.json
           ↓
       Refresh galerki
```

### 3. **Edycja wideo**
```
User → Klik "Edit" → VideoEditor.razor
           ↓
       VideoService.GetVideoByIdAsync(id)
           ↓
       Odczyt metadata.json
           ↓
       Wyświetl formularz
           ↓
       VideoService.UpdateVideoAsync()
           ↓
       Zaktualizuj metadata.json
```

## 💾 Format przechowywania

### Struktura folderu
```
wwwroot/videos/
└── 1/
    ├── video.mp4
    └── metadata.json
```

### metadata.json
```json
{
  "Id": 1,
  "Title": "Summer Hike to Mountain Peak",
  "Description": "Amazing adventure...",
  "FileName": "video.mp4",
  "Location": "Tatra Mountains",
  "UploadedAt": "2026-05-04T12:30:00Z",
  "FileSizeBytes": 524288000
}
```

## 🎨 Frontend - Komponenty Blazor

### VideoGallery.razor
- Główny komponent wyświetlający galerę
- Grid responsive (auto-fill minmax)
- Animacje hover
- Przyciski edycji/usunięcia

### VideoUpload.razor
- Formularz z polami: tytuł, opis, lokalizacja, plik
- Walidacja po stronie klienta
- Feedback użytkownikowi (success/error)
- Spinner podczas uploadu

### VideoEditor.razor
- Strona `/edit-video/{id}`
- Preview wideo
- Edycja metadanych
- Przycisk powrotu

## 🔧 Backend - VideoService

```csharp
public class VideoService
{
    // Publiczne metody
    GetAllVideosAsync()      // Zwraca listę wszystkich wideo
    GetVideoByIdAsync(id)    // Zwraca jedno wideo
    UploadVideoAsync(...)    // Upload + zapis metadanych
    UpdateVideoAsync(...)    // Aktualizacja metadata.json
    DeleteVideoAsync(id)     // Usunięcie folderu

    // Prywatne helpery
    GetVideosPath()          // Ścieżka do folderów
    GetVideoPath(id)         // Ścieżka konkretnego wideo
    GetMetadataPath(id)      // Ścieżka metadata.json
    GenerateVideoId()        // Auto-increment ID
}
```

## 📦 Zależności

```xml
<PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="8.0.0" />
<!-- Inne pakiety ASP.NET Core są wbudowane w framework -->
```

**Brak zależności na:**
- Entity Framework (używamy plików JSON)
- SQL Server (file-based storage)
- Identity (logowanie)
- Bootstrap (custom CSS)

## 🚀 Performance

### Optymalizacje
- **Lazy loading wideo** — <video> controls HTML5
- **JSON cache** — Odczyt z dysku (fast I/O)
- **CSS animations** — Hardware-accelerated
- **Streaming upload** — Buffer na stream

### Limity
- Max 500 MB na plik
- Formaty: MP4, WebM, MKV, AVI
- Przechowywanie lokalne (nie cloud)

## 🔐 Security

- ✅ Walidacja formatu pliku (whitelist)
- ✅ Limit rozmiaru (500 MB)
- ✅ Katalog zakazany (bin, obj w .gitignore)
- ⚠️ TODO: Dodać logowanie do edycji
- ⚠️ TODO: CSRF protection
- ⚠️ TODO: Rate limiting

## 📈 Rozwój przyszły

### Faza 1 (Low effort, high value)
- [ ] Dodaj ASP.NET Identity (logowanie)
- [ ] Thumbnail preview
- [ ] Search/filter
- [ ] Sortowanie (data, tytuł)

### Faza 2 (Medium)
- [ ] Dodaj kategorie
- [ ] Komentarze
- [ ] Ratings/likes
- [ ] Email notifications

### Faza 3 (High effort)
- [ ] Transcoding video (FFmpeg)
- [ ] Cloud storage (Azure Blob)
- [ ] Video streaming optimization
- [ ] Mobile app (Flutter/React Native)

## 🧪 Testing

```bash
# Unit Tests
dotnet test

# Integration Tests
# TODO: Dodać testy dla VideoService

# E2E Tests
# TODO: Dodać Selenium/Playwright
```

## 📝 Commit history

- `7d6207f` - Initial Blazor project with file-based video gallery

---

**Autor:** Jakub Syrek  
**Email:** jakubvonsyrek@gmail.com  
**Repo:** https://github.com/Jakub-Syrek/MyHomePage
