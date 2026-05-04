# My Mountain Adventures - Home Page

Nowoczesna strona do hostowania filmików z wycieczek górskich, zbudowana z ASP.NET Core i Blazor.

## Cechy

✨ **Nowoczesny design** — responsywny interfejs z animacjami
📹 **Galerka wideo** — przesyłanie i zarządzanie filmami
🎨 **Piękny UI** — gradientu, karty, animacje CSS
🔐 **Edycja zawartości** — tytuł, opis, lokalizacja każdego wideo
📁 **Przechowywanie w folderze** — żadna baza danych, wszystko w plikach JSON
⚡ **Szybkie działanie** — bez zależności od SQL Server

## Wymagania

- .NET 8.0 lub nowszy
- Windows/Linux/macOS

## Instalacja i uruchomienie

```bash
# Klonuj repo
git clone https://github.com/Jakub-Syrek/MyHomePage.git
cd MyHomePage

# Przywróć pakiety
dotnet restore

# Uruchom serwer
dotnet run
```

Otwórz http://localhost:5000 (lub https://localhost:5001) w przeglądarce.

## Struktura folderów

```
wwwroot/
├── videos/              # Wideo organizowane w podfoldery
│   ├── 1/              # Video ID
│   │   ├── video.mp4   # Plik wideo
│   │   └── metadata.json
│   ├── 2/
│   └── 3/
├── css/
│   └── site.css        # Globalne style
└── ...
```

## Struktura metadata.json

```json
{
  "Id": 1,
  "Title": "Summer Hike",
  "Description": "Amazing day in mountains...",
  "FileName": "video.mp4",
  "Location": "Tatra Mountains",
  "UploadedAt": "2026-05-04T12:30:00Z",
  "FileSizeBytes": 524288000
}
```

## Technologia

- **Frontend:** Blazor Server, HTML, CSS z animacjami
- **Backend:** ASP.NET Core 8.0
- **Storage:** Pliki JSON + foldery (bez bazy danych)
- **Wzorce:** Dependency Injection, Service Pattern

## API Endpoints

Komponenty Blazor komunikują się z `VideoService`:

- `GetAllVideosAsync()` — lista wszystkich wideo
- `GetVideoByIdAsync(id)` — szczegóły wideo
- `UploadVideoAsync(file, title, desc, location)` — upload
- `UpdateVideoAsync(id, title, desc, location)` — edycja
- `DeleteVideoAsync(id)` — usunięcie

## Komponenty

### VideoGallery.razor
Główna galerka wyświetlająca wszystkie wideo w siatce z możliwością edycji.

### VideoUpload.razor
Panel do przesyłania nowych filmów z walidacją.

### VideoEditor.razor
Strona do edycji metadanych istniejącego wideo.

## Limitacje

- Max 500 MB na plik
- Obsługiwane formaty: MP4, WebM, MKV, AVI
- Przechowywanie lokalne (nie cloud)

## Desarrollador

Jakub Syrek

---

💡 Chcesz dodać logowanie? Możemy dodać ASP.NET Core Identity!
