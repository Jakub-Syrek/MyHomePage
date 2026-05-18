using System.Globalization;
using System.Text;
using MyHomePage.Abstractions;
using MyHomePage.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MyHomePage.Services;

/// <summary>
/// Implements <see cref="ICollectionMergeService"/>. Combines several gallery
/// items into a single new one, aggregates their <see cref="TrainingData"/>
/// rows into a multi-sport summary, writes a Markdown summary file, and
/// removes the source items afterwards.
///
/// Failure handling: media files are <em>copied</em> into the new collection
/// before the source rows are deleted, so a crash mid-merge leaves sources
/// intact (the new directory may need cleanup but no data is lost).
/// </summary>
public sealed class CollectionMergeService : ICollectionMergeService
{
    /// <summary>Name of the synthesised Markdown summary written into the merged folder.</summary>
    internal const string SummaryFileName = "summary.md";

    /// <summary>
    /// File name used by Strava stumps for the auto-seeded category placeholder
    /// image. Treated as "not user media" — the merge skips it when collecting
    /// user uploads, but it is still considered a candidate when assembling
    /// the synthetic multi-sport mosaic cover.
    /// </summary>
    internal const string StumpCoverFileName = "cover.jpg";

    /// <summary>
    /// File name written into the master's directory as the auto-generated
    /// mosaic cover (1-9 source thumbnails composed into a single image).
    /// </summary>
    internal const string MosaicCoverFileName = "multisport-cover.jpg";

    /// <summary>Maximum number of source thumbnails composed into one mosaic.</summary>
    private const int MaxMosaicTiles = 9;

    /// <summary>Per-tile pixel size inside the mosaic.</summary>
    private const int MosaicTileSize = 512;

    private readonly IVideoRepository _repository;
    private readonly IFileStorageService _storage;
    private readonly ILogger<CollectionMergeService> _logger;

    /// <summary>Creates a new merge service.</summary>
    public CollectionMergeService(
        IVideoRepository repository,
        IFileStorageService storage,
        ILogger<CollectionMergeService> logger)
    {
        _repository = repository;
        _storage = storage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult<int>> MergeAsync(
        IReadOnlyList<int> sourceIds,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateInputs(sourceIds, title);
        if (!validation.IsSuccess)
        {
            return OperationResult<int>.Failure(validation.Message);
        }

        var sources = await LoadSourcesAsync(sourceIds);
        if (sources.Count < 2)
        {
            return OperationResult<int>.Failure(
                $"Need at least 2 existing collections to merge; resolved {sources.Count}.");
        }

        var newId = _repository.GenerateNextId();
        _storage.EnsureVideoDirectoryExists(newId);

        try
        {
            var (mergedMedia, totalSize) = CopyMediaFromSources(sources, newId);
            var trainingAggregate = AggregateTraining(sources);
            await WriteSummaryFileAsync(newId, title, description, sources, trainingAggregate, cancellationToken);

            var merged = BuildMergedVideo(
                newId, title, description, sources, mergedMedia, totalSize, trainingAggregate);
            await _repository.SaveAsync(merged);
            await GenerateMergedOgPreviewAsync(merged);

            _logger.LogInformation(
                "Created multi-sport collection {NewId} aggregating {SourceCount} sources [{SourceIds}] — sources retained",
                newId, sources.Count, string.Join(",", sources.Select(s => s.Id)));
            return OperationResult<int>.Success(newId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Merge failed for source ids {SourceIds}; cleaning up partial directory {NewId}",
                string.Join(",", sourceIds), newId);
            await _storage.DeleteVideoDirectoryAsync(newId);
            return OperationResult<int>.Failure($"Merge failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Renders the Facebook / Open Graph preview for a freshly-merged
    /// multi-sport master. Picks the first image media file as the
    /// source crop, layers the aggregated stats overlay (distance, time,
    /// pace, kcal, elevation) on top. Best-effort: failures stay silent
    /// so a transient ImageSharp / font problem can't roll back the
    /// merge that already succeeded.
    /// </summary>
    private async Task GenerateMergedOgPreviewAsync(Video merged)
    {
        try
        {
            var firstImage = merged.GetAllMedia()
                .FirstOrDefault(m => m.Type == MediaType.Image);
            if (firstImage is null) return;

            var dir = _storage.GetVideoDirectoryPath(merged.Id);
            var sourcePath = Path.Combine(dir, firstImage.FileName);
            if (!File.Exists(sourcePath)) return;

            var ogPath = Path.Combine(dir, "og.jpg");
            var overlay = merged.ToOgOverlay();
            await _storage.GenerateOgImageAsync(sourcePath, ogPath, overlay: overlay);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not render OG preview for merged collection {Id}", merged.Id);
        }
    }

    private static OperationResult ValidateInputs(IReadOnlyList<int> sourceIds, string title)
    {
        if (sourceIds is null || sourceIds.Count < 2)
        {
            return OperationResult.Failure("Select at least two collections to merge.");
        }
        if (sourceIds.Distinct().Count() != sourceIds.Count)
        {
            return OperationResult.Failure("Duplicate ids in selection.");
        }
        if (string.IsNullOrWhiteSpace(title))
        {
            return OperationResult.Failure("Title is required for the merged collection.");
        }
        return OperationResult.Success();
    }

    private async Task<List<Video>> LoadSourcesAsync(IReadOnlyList<int> sourceIds)
    {
        // Selection order from the UI is meaningful (the user's "first chosen"
        // wins the placeholder fallback below) so the original sequence is
        // preserved here instead of re-sorting by upload date.
        var loaded = new List<Video>(sourceIds.Count);
        foreach (var id in sourceIds)
        {
            var v = await _repository.GetByIdAsync(id);
            if (v is not null)
            {
                loaded.Add(v);
            }
        }
        return loaded;
    }

    private (List<MediaItem> media, long totalSize) CopyMediaFromSources(
        IReadOnlyList<Video> sources,
        int newId)
    {
        var destinationDir = _storage.GetVideoDirectoryPath(newId);
        var merged = new List<MediaItem>();
        long total = 0;
        var order = 0;

        // Mosaic cover always sits at Order = 0 so the gallery card shows
        // the combined view first, even when user uploads exist below.
        var mosaic = BuildMosaicCoverFromSources(sources, destinationDir);
        if (mosaic is not null)
        {
            merged.Add(mosaic);
            total += mosaic.SizeBytes;
            order = 1;
        }

        foreach (var source in sources)
        {
            var sourceDir = _storage.GetVideoDirectoryPath(source.Id);
            foreach (var media in source.GetAllMedia())
            {
                if (string.IsNullOrWhiteSpace(media.FileName) || IsStumpPlaceholder(media))
                {
                    continue;
                }

                var sourcePath = Path.Combine(sourceDir, media.FileName);
                if (!File.Exists(sourcePath))
                {
                    _logger.LogWarning(
                        "Skipping missing media file {Path} during merge of collection {Id}",
                        sourcePath, source.Id);
                    continue;
                }

                var prefixedName = $"s{source.Id}-{media.FileName}";
                var targetPath = Path.Combine(destinationDir, prefixedName);
                File.Copy(sourcePath, targetPath, overwrite: true);
                var size = new FileInfo(targetPath).Length;
                total += size;

                merged.Add(MediaItem.Create(prefixedName, media.Type, size, order++));
            }
        }

        return (merged, total);
    }

    private static bool IsStumpPlaceholder(MediaItem media) =>
        string.Equals(media.FileName, StumpCoverFileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Picks one representative thumbnail per source (the stump cover if
    /// present, otherwise the source's first image) and composes them into a
    /// single square-ish mosaic image written as
    /// <see cref="MosaicCoverFileName"/> in the master directory.
    /// </summary>
    /// <returns>The new <see cref="MediaItem"/> referencing the mosaic, or
    /// <c>null</c> when no source supplied a usable thumbnail.</returns>
    private MediaItem? BuildMosaicCoverFromSources(
        IReadOnlyList<Video> sources,
        string destinationDir)
    {
        var tiles = new List<string>();
        foreach (var source in sources)
        {
            var dir = _storage.GetVideoDirectoryPath(source.Id);
            var cover = ResolveCoverCandidate(source, dir);
            if (cover is not null)
            {
                tiles.Add(cover);
            }
        }

        if (tiles.Count == 0)
        {
            return null;
        }

        var targetPath = Path.Combine(destinationDir, MosaicCoverFileName);
        try
        {
            ComposeMosaic(tiles, targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Mosaic composition failed for new collection — copying first source thumbnail instead");
            File.Copy(tiles[0], targetPath, overwrite: true);
        }

        var size = new FileInfo(targetPath).Length;
        _logger.LogInformation(
            "Generated multi-sport mosaic cover from {TileCount} source thumbnail(s)",
            Math.Min(tiles.Count, MaxMosaicTiles));
        return MediaItem.Create(MosaicCoverFileName, MediaType.Image, size, 0);
    }

    private static string? ResolveCoverCandidate(Video source, string sourceDir)
    {
        var cover = Path.Combine(sourceDir, StumpCoverFileName);
        if (File.Exists(cover))
        {
            return cover;
        }

        foreach (var media in source.GetAllMedia())
        {
            if (media.Type != MediaType.Image || string.IsNullOrWhiteSpace(media.FileName))
            {
                continue;
            }
            var path = Path.Combine(sourceDir, media.FileName);
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Lays the supplied thumbnails out in a square-ish grid
    /// (cols = ceil(sqrt(n))) and writes the composite as JPEG to
    /// <paramref name="outputPath"/>. Sources beyond
    /// <see cref="MaxMosaicTiles"/> are ignored to keep the file size sane.
    /// </summary>
    private static void ComposeMosaic(IReadOnlyList<string> imagePaths, string outputPath)
    {
        var count = Math.Min(imagePaths.Count, MaxMosaicTiles);
        var cols = (int)Math.Ceiling(Math.Sqrt(count));
        var rows = (int)Math.Ceiling((double)count / cols);
        var canvasWidth = cols * MosaicTileSize;
        var canvasHeight = rows * MosaicTileSize;

        using var canvas = new Image<Rgba32>(canvasWidth, canvasHeight, Color.Black);
        for (var i = 0; i < count; i++)
        {
            using var tile = Image.Load<Rgba32>(imagePaths[i]);
            tile.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Crop,
                Size = new Size(MosaicTileSize, MosaicTileSize),
            }));
            var col = i % cols;
            var row = i / cols;
            var origin = new Point(col * MosaicTileSize, row * MosaicTileSize);
            canvas.Mutate(c => c.DrawImage(tile, origin, 1f));
        }

        var encoder = new JpegEncoder { Quality = 85 };
        canvas.SaveAsJpeg(outputPath, encoder);
    }

    private static TrainingData? AggregateTraining(IReadOnlyList<Video> sources)
    {
        var trainings = sources
            .Where(v => v.Training is not null)
            .Select(v => v.Training!)
            .ToList();

        if (trainings.Count == 0)
        {
            return null;
        }

        var totalDuration = TimeSpan.FromSeconds(trainings.Sum(t => t.Duration.TotalSeconds));
        var weightedHrSum = trainings
            .Where(t => t.AverageHeartRate is > 0)
            .Sum(t => t.AverageHeartRate!.Value * t.Duration.TotalSeconds);
        var weightedHrWeight = trainings
            .Where(t => t.AverageHeartRate is > 0)
            .Sum(t => t.Duration.TotalSeconds);

        return new TrainingData
        {
            Source = trainings.All(t => t.Source == trainings[0].Source) ? trainings[0].Source : TrainingSource.Strava,
            ExternalId = string.Empty,
            ActivityType = "Multi-sport",
            StartTimeUtc = trainings.Min(t => t.StartTimeUtc),
            Duration = totalDuration,
            DistanceMeters = SumNullable(trainings.Select(t => t.DistanceMeters)),
            ElevationGainMeters = SumNullable(trainings.Select(t => t.ElevationGainMeters)),
            Calories = SumNullableInt(trainings.Select(t => t.Calories)),
            AverageHeartRate = weightedHrWeight > 0
                ? (int)Math.Round(weightedHrSum / weightedHrWeight)
                : null,
            MaxHeartRate = MaxNullable(trainings.Select(t => t.MaxHeartRate)),
            SufferScore = SumNullableInt(trainings.Select(t => t.SufferScore)),
            AchievementCount = SumNullableInt(trainings.Select(t => t.AchievementCount)),
            PersonalRecordCount = SumNullableInt(trainings.Select(t => t.PersonalRecordCount)),
            KudosCount = SumNullableInt(trainings.Select(t => t.KudosCount)),
            SubActivities = trainings.Select(ToSubActivityLink).ToList(),
        };
    }

    private static SubActivityLink ToSubActivityLink(TrainingData t) => new(
        Source: t.Source,
        ExternalId: t.ExternalId,
        ExternalUrl: t.ExternalUrl,
        ActivityType: t.ActivityType,
        StartTimeUtc: t.StartTimeUtc,
        Duration: t.Duration,
        DistanceMeters: t.DistanceMeters,
        Calories: t.Calories,
        AverageHeartRate: t.AverageHeartRate,
        SufferScore: t.SufferScore);

    private static double? SumNullable(IEnumerable<double?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    private static int? SumNullableInt(IEnumerable<int?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    private static int? MaxNullable(IEnumerable<int?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : present.Max();
    }

    private async Task WriteSummaryFileAsync(
        int newId,
        string title,
        string description,
        IReadOnlyList<Video> sources,
        TrainingData? aggregate,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_storage.GetVideoDirectoryPath(newId), SummaryFileName);
        var content = SummaryMarkdown.Render(title, description, sources, aggregate);
        await File.WriteAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
    }

    private static Video BuildMergedVideo(
        int newId,
        string title,
        string description,
        IReadOnlyList<Video> sources,
        IReadOnlyList<MediaItem> media,
        long totalSize,
        TrainingData? training)
    {
        var primary = media.FirstOrDefault();
        var category = PickCategory(sources);

        return new Video
        {
            Id = newId,
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            FileName = primary?.FileName ?? string.Empty,
            Location = sources.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Location))?.Location,
            Category = category,
            UploadedAt = DateTime.UtcNow,
            FileSizeBytes = totalSize,
            Media = media.ToList(),
            Latitude = sources.FirstOrDefault(s => s.Latitude.HasValue)?.Latitude,
            Longitude = sources.FirstOrDefault(s => s.Longitude.HasValue)?.Longitude,
            Training = training,
        };
    }

    // Every merged collection lives in the dedicated "Multi-sport" bucket,
    // surfaced under its own /multisport subview. Sources are unchanged and
    // keep showing up on their original category pages.
    private static string PickCategory(IReadOnlyList<Video> sources) =>
        VideoCategories.MultiSport;
}

/// <summary>Renders the human-readable Markdown summary for a merged collection.</summary>
internal static class SummaryMarkdown
{
    /// <summary>Returns the Markdown text for <c>summary.md</c>.</summary>
    public static string Render(
        string title,
        string description,
        IReadOnlyList<Video> sources,
        TrainingData? aggregate)
    {
        var sb = new StringBuilder();
        sb.Append("# Multi-sport — ").AppendLine(title);
        sb.AppendLine();
        sb.Append("_Generated: ")
            .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture))
            .AppendLine("_");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine(description.Trim());
            sb.AppendLine();
        }

        if (aggregate is null)
        {
            sb.AppendLine("_No training data attached to the merged collections._");
            sb.AppendLine();
        }
        else
        {
            AppendTotals(sb, aggregate);
        }

        AppendActivityTable(sb, sources, aggregate);
        AppendSourceList(sb, sources);
        return sb.ToString();
    }

    private static void AppendTotals(StringBuilder sb, TrainingData a)
    {
        sb.AppendLine("## Totals");
        sb.AppendLine();
        sb.Append("- **Duration:** ").AppendLine(FormatDuration(a.Duration));
        if (a.DistanceMeters is > 0)
        {
            sb.Append("- **Distance:** ").AppendLine(FormatDistance(a.DistanceMeters.Value));
        }
        if (a.ElevationGainMeters is > 0)
        {
            sb.Append("- **Elevation gain:** ↑ ")
                .AppendFormat(CultureInfo.InvariantCulture, "{0:F0} m", a.ElevationGainMeters.Value)
                .AppendLine();
        }
        if (a.Calories is > 0)
        {
            sb.Append("- **Calories:** ").Append(a.Calories.Value).AppendLine(" kcal");
        }
        if (a.AverageHeartRate is > 0)
        {
            sb.Append("- **Avg heart rate:** ").Append(a.AverageHeartRate.Value).AppendLine(" bpm");
        }
        if (a.MaxHeartRate is > 0)
        {
            sb.Append("- **Max heart rate:** ").Append(a.MaxHeartRate.Value).AppendLine(" bpm");
        }
        if (a.SufferScore is > 0)
        {
            sb.Append("- **Total effort (Strava Relative Effort sum):** ")
                .AppendLine(a.SufferScore.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (a.AchievementCount is > 0)
        {
            sb.Append("- **Achievements:** ").AppendLine(a.AchievementCount.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (a.PersonalRecordCount is > 0)
        {
            sb.Append("- **Personal records:** ").AppendLine(a.PersonalRecordCount.Value.ToString(CultureInfo.InvariantCulture));
        }
        sb.AppendLine();
    }

    private static void AppendActivityTable(
        StringBuilder sb,
        IReadOnlyList<Video> sources,
        TrainingData? aggregate)
    {
        var rows = aggregate?.SubActivities;
        if (rows is null || rows.Count == 0)
        {
            return;
        }

        sb.AppendLine("## Activities");
        sb.AppendLine();
        sb.AppendLine("| When | Type | Duration | Distance | Calories | HR avg | Effort | Strava |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in rows)
        {
            sb.Append("| ").Append(row.StartTimeUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
              .Append(" | ").Append(string.IsNullOrWhiteSpace(row.ActivityType) ? "—" : row.ActivityType)
              .Append(" | ").Append(FormatDuration(row.Duration))
              .Append(" | ").Append(row.DistanceMeters is > 0 ? FormatDistance(row.DistanceMeters.Value) : "—")
              .Append(" | ").Append(row.Calories is > 0 ? $"{row.Calories} kcal" : "—")
              .Append(" | ").Append(row.AverageHeartRate is > 0 ? $"{row.AverageHeartRate} bpm" : "—")
              .Append(" | ").Append(row.SufferScore is > 0 ? row.SufferScore.Value.ToString(CultureInfo.InvariantCulture) : "—")
              .Append(" | ").Append(FormatStravaLink(row))
              .AppendLine(" |");
        }
        sb.AppendLine();
    }

    private static void AppendSourceList(StringBuilder sb, IReadOnlyList<Video> sources)
    {
        sb.AppendLine("## Merged collections");
        sb.AppendLine();
        foreach (var s in sources)
        {
            sb.Append("- ").Append(s.UploadedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
              .Append(" — ").Append(string.IsNullOrWhiteSpace(s.Title) ? $"#{s.Id}" : s.Title)
              .Append(" _(").Append(string.IsNullOrWhiteSpace(s.Category) ? "uncategorised" : s.Category)
              .AppendLine(")_");
        }
    }

    private static string FormatStravaLink(SubActivityLink row)
    {
        if (string.IsNullOrWhiteSpace(row.ExternalUrl))
        {
            return "—";
        }
        var label = string.IsNullOrWhiteSpace(row.ExternalId) ? "open" : $"#{row.ExternalId}";
        return $"[{label}]({row.ExternalUrl})";
    }

    private static string FormatDuration(TimeSpan d) =>
        d.TotalHours >= 1
            ? $"{(int)d.TotalHours}h {d.Minutes:00}m"
            : $"{d.Minutes}m {d.Seconds:00}s";

    private static string FormatDistance(double meters) =>
        meters >= 1000
            ? string.Format(CultureInfo.InvariantCulture, "{0:F2} km", meters / 1000)
            : string.Format(CultureInfo.InvariantCulture, "{0:F0} m", meters);
}
