using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.QuickTime;
using Microsoft.Extensions.Logging;
using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// Reads the capture timestamp from media files via the same
/// <c>MetadataExtractor</c> library used for GPS extraction:
///   - JPEG / HEIC → EXIF SubIfd <c>DateTimeOriginal</c> (preferred),
///     falling back to <c>DateTime</c> on the main Ifd0 directory.
///   - MP4 / MOV / M4V → QuickTime movie-header <c>Created</c>.
///
/// Stateless beyond the injected logger; safe to register as a singleton
/// once the project conventions allow (currently scoped to match the GPS
/// extractor for consistency).
/// </summary>
public sealed class MetadataDateTakenExtractor : IDateTakenExtractor
{
    private readonly ILogger<MetadataDateTakenExtractor> _logger;

    /// <summary>Creates a new extractor with the supplied logger.</summary>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public MetadataDateTakenExtractor(ILogger<MetadataDateTakenExtractor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public DateTime? TryExtract(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;

            IReadOnlyList<MetadataExtractor.Directory> dirs;
            try
            {
                dirs = ImageMetadataReader.ReadMetadata(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read metadata for {File}", filePath);
                return null;
            }

            // 1. EXIF SubIfd → DateTimeOriginal (cameras / phones).
            var subIfd = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is not null
                && subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var exifOriginal))
            {
                _logger.LogInformation(
                    "Date from EXIF DateTimeOriginal for {File}: {Date:o}",
                    Path.GetFileName(filePath), exifOriginal);
                return DateTime.SpecifyKind(exifOriginal, DateTimeKind.Utc);
            }

            // 2. EXIF Ifd0 → DateTime (older photos / scanned images).
            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null
                && ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var exifDate))
            {
                _logger.LogInformation(
                    "Date from EXIF Ifd0 DateTime for {File}: {Date:o}",
                    Path.GetFileName(filePath), exifDate);
                return DateTime.SpecifyKind(exifDate, DateTimeKind.Utc);
            }

            // 3. QuickTime movie header → Created (MP4 / MOV / M4V).
            var movieHeader = dirs.OfType<QuickTimeMovieHeaderDirectory>().FirstOrDefault();
            if (movieHeader is not null
                && movieHeader.TryGetDateTime(QuickTimeMovieHeaderDirectory.TagCreated, out var qtCreated))
            {
                _logger.LogInformation(
                    "Date from QuickTime Created for {File}: {Date:o}",
                    Path.GetFileName(filePath), qtCreated);
                return DateTime.SpecifyKind(qtCreated, DateTimeKind.Utc);
            }

            _logger.LogDebug("No embedded date found for {File}", Path.GetFileName(filePath));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Date extraction failed for {File}", filePath);
            return null;
        }
    }
}
