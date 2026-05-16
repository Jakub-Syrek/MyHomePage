namespace MyHomePage.Abstractions;

/// <summary>
/// Reads the "date taken" timestamp from a media file: EXIF
/// <c>DateTimeOriginal</c> for photos, QuickTime/MP4 <c>CreationDate</c>
/// for videos. Returns <c>null</c> when no usable date is present so
/// callers can fall back to <see cref="DateTime.UtcNow"/>.
///
/// Interface Segregation: a deliberately small contract so callers
/// that only need the capture date don't pull in the GPS extractor.
/// </summary>
public interface IDateTakenExtractor
{
    /// <summary>
    /// Reads the capture timestamp from the file at the given path.
    /// </summary>
    /// <param name="filePath">Absolute path to the media file.</param>
    /// <returns>The capture time as a UTC <see cref="DateTime"/>, or null when not embedded.</returns>
    DateTime? TryExtract(string filePath);
}
