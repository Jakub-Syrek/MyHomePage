using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Default <see cref="ICoachReportService"/>. Builds the prompt context
/// out of the existing <see cref="ITrainingStatsService"/> result plus a
/// compact list of the week's sessions, calls the AI assistant for a
/// structured response, then persists the report so the UI can render it
/// without another API call.
/// </summary>
public sealed class CoachReportService : ICoachReportService
{
    private const int MaxSessionsInContext = 25;
    private const int PriorWeeksContext = 4;

    private readonly IVideoService _videos;
    private readonly ITrainingStatsService _stats;
    private readonly IAiAssistantService _ai;
    private readonly ICoachReportRepository _reports;
    private readonly ILogger<CoachReportService> _logger;

    /// <summary>
    /// Initialises the orchestrator with its collaborators.
    /// </summary>
    /// <param name="videos">Source gallery items that carry training data.</param>
    /// <param name="stats">Pure stats aggregator.</param>
    /// <param name="ai">AI assistant for narrative generation.</param>
    /// <param name="reports">Persistence layer for generated reports.</param>
    /// <param name="logger">Structured logger for diagnostic events.</param>
    public CoachReportService(
        IVideoService videos,
        ITrainingStatsService stats,
        IAiAssistantService ai,
        ICoachReportRepository reports,
        ILogger<CoachReportService> logger)
    {
        _videos = videos;
        _stats = stats;
        _ai = ai;
        _reports = reports;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CoachReport>> ListAsync(
        CancellationToken cancellationToken = default) =>
        _reports.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<OperationResult<CoachReport>> GenerateForWeekAsync(
        DateTime anyDateInTheWeek,
        CancellationToken cancellationToken = default)
    {
        if (!_ai.IsEnabled)
            return OperationResult<CoachReport>.Failure(
                "AI assistant is not configured (ANTHROPIC_API_KEY missing).");

        var weekStart = GetWeekStart(anyDateInTheWeek);
        var weekEnd = weekStart.AddDays(7).AddTicks(-1);

        var allVideos = await _videos.GetAllVideosAsync();
        var videos = allVideos.ToList();

        var weekStats = _stats.Compute(videos, weekStart, weekEnd);
        if (weekStats.SessionCount == 0)
            return OperationResult<CoachReport>.Failure(
                $"No training sessions found for week {FormatIsoWeek(weekStart)}.");

        var priorStats = _stats.Compute(
            videos,
            weekStart.AddDays(-7 * PriorWeeksContext),
            weekStart.AddTicks(-1));

        var context = BuildContext(weekStart, weekEnd, weekStats, priorStats, videos);

        _logger.LogInformation(
            "Generating coach report for week {Week} ({Sessions} sessions, {ContextChars} chars context)",
            FormatIsoWeek(weekStart), weekStats.SessionCount, context.Length);

        var payload = await _ai.GenerateCoachReportAsync(context, cancellationToken);
        if (payload is null)
            return OperationResult<CoachReport>.Failure(
                "AI assistant could not generate a report. Check the API key or model availability.");

        var report = new CoachReport
        {
            IsoWeek = FormatIsoWeek(weekStart),
            WeekStartUtc = weekStart,
            WeekEndUtc = weekEnd,
            GeneratedAtUtc = DateTime.UtcNow,
            Model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")
                    ?? "claude-haiku-4-5-20251001",
            Headline = payload.Headline,
            Narrative = payload.Narrative,
            Highlights = payload.Highlights,
            Concerns = payload.Concerns,
            NextWeekFocus = payload.NextWeekFocus,
            KeyMetrics = BuildKeyMetrics(weekStats)
        };

        await _reports.SaveAsync(report, cancellationToken);
        return OperationResult<CoachReport>.Success(report,
            $"Coach report generated for week {report.IsoWeek}.");
    }

    private static DateTime GetWeekStart(DateTime utcDate)
    {
        var date = DateTime.SpecifyKind(utcDate.Date, DateTimeKind.Utc);
        var diff = ((int)date.DayOfWeek + 6) % 7; // Monday-anchored
        return date.AddDays(-diff);
    }

    private static string FormatIsoWeek(DateTime weekStart)
    {
        var year = ISOWeek.GetYear(weekStart);
        var week = ISOWeek.GetWeekOfYear(weekStart);
        return $"{year:D4}-W{week:D2}";
    }

    private static string BuildContext(
        DateTime weekStart,
        DateTime weekEnd,
        TrainingStats weekStats,
        TrainingStats priorStats,
        IReadOnlyList<Video> allVideos)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Week {FormatIsoWeek(weekStart)} " +
                      $"({weekStart:yyyy-MM-dd} → {weekEnd:yyyy-MM-dd})");
        sb.AppendLine();
        sb.AppendLine("=== This week ===");
        AppendStats(sb, weekStats);

        sb.AppendLine();
        sb.AppendLine($"=== Prior {PriorWeeksContext} weeks (combined) ===");
        AppendStats(sb, priorStats);

        sb.AppendLine();
        sb.AppendLine("=== Sessions this week ===");
        var sessions = allVideos
            .Where(v => v.Training is not null
                        && v.Training.StartTimeUtc >= weekStart
                        && v.Training.StartTimeUtc <= weekEnd)
            .OrderBy(v => v.Training!.StartTimeUtc)
            .Take(MaxSessionsInContext)
            .ToList();
        foreach (var session in sessions)
        {
            AppendSession(sb, session);
        }
        return sb.ToString();
    }

    private static void AppendStats(StringBuilder sb, TrainingStats stats)
    {
        sb.AppendLine($"Sessions: {stats.SessionCount}");
        sb.AppendLine($"Distance: {FormatKm(stats.TotalDistanceMeters)} km");
        sb.AppendLine($"Moving time: {FormatHours(stats.TotalDuration)} h");
        sb.AppendLine($"Elevation gain: {stats.TotalElevationGainMeters:F0} m");
        if (stats.TotalCalories > 0)
            sb.AppendLine($"Calories: {stats.TotalCalories}");
        if (stats.TotalSufferScore > 0)
            sb.AppendLine($"Suffer score (sum): {stats.TotalSufferScore}");
        if (stats.TotalPersonalRecords > 0)
            sb.AppendLine($"Personal records: {stats.TotalPersonalRecords}");
        if (stats.TotalAchievements > 0)
            sb.AppendLine($"Achievements: {stats.TotalAchievements}");
        sb.AppendLine($"Streak (current / longest): " +
                      $"{stats.CurrentStreakDays} / {stats.LongestStreakDays} day(s)");
        if (stats.SessionsByCategory.Count > 0)
        {
            sb.AppendLine("Category mix:");
            foreach (var entry in stats.SessionsByCategory.OrderByDescending(e => e.Value))
            {
                var distance = stats.DistanceByCategory.TryGetValue(entry.Key, out var d) ? d : 0;
                sb.AppendLine($"  - {entry.Key}: {entry.Value} session(s), {FormatKm(distance)} km");
            }
        }
    }

    private static void AppendSession(StringBuilder sb, Video video)
    {
        var training = video.Training!;
        sb.Append($"- {training.StartTimeUtc:yyyy-MM-dd HH:mm} | {video.Category} | ");
        sb.Append($"{training.ActivityType} | {video.Title}");
        sb.Append(" | ");
        var parts = new List<string>();
        if (training.DistanceMeters is double dist && dist > 0)
            parts.Add($"{FormatKm(dist)} km");
        parts.Add($"{FormatMinutes(training.Duration)} min");
        if (training.AveragePaceSecondsPerKm is double pace)
            parts.Add($"pace {FormatPace(pace)}");
        if (training.AverageHeartRate is int hr) parts.Add($"avgHR {hr}");
        if (training.MaxHeartRate is int hrMax) parts.Add($"maxHR {hrMax}");
        if (training.SufferScore is int suffer) parts.Add($"effort {suffer}");
        if (training.PersonalRecordCount is int prs && prs > 0) parts.Add($"PRs {prs}");
        if (training.ElevationGainMeters is double elev && elev > 0)
            parts.Add($"+{elev:F0} m");
        sb.AppendLine(string.Join(" · ", parts));
    }

    private static IReadOnlyList<CoachKeyMetric> BuildKeyMetrics(TrainingStats stats)
    {
        var metrics = new List<CoachKeyMetric>
        {
            new() { Label = "Sessions", Value = stats.SessionCount.ToString() },
            new() { Label = "Distance", Value = $"{FormatKm(stats.TotalDistanceMeters)} km" },
            new() { Label = "Moving time", Value = $"{FormatHours(stats.TotalDuration)} h" },
            new() { Label = "Elevation", Value = $"{stats.TotalElevationGainMeters:F0} m" }
        };
        if (stats.TotalSufferScore > 0)
            metrics.Add(new() { Label = "Effort", Value = stats.TotalSufferScore.ToString() });
        if (stats.TotalPersonalRecords > 0)
            metrics.Add(new() { Label = "PRs", Value = stats.TotalPersonalRecords.ToString() });
        return metrics;
    }

    private static string FormatKm(double meters) =>
        (meters / 1000.0).ToString("F1", CultureInfo.InvariantCulture);

    private static string FormatHours(TimeSpan duration) =>
        duration.TotalHours.ToString("F1", CultureInfo.InvariantCulture);

    private static string FormatMinutes(TimeSpan duration) =>
        Math.Round(duration.TotalMinutes).ToString(CultureInfo.InvariantCulture);

    private static string FormatPace(double secondsPerKm)
    {
        var minutes = (int)(secondsPerKm / 60);
        var seconds = (int)Math.Round(secondsPerKm - (minutes * 60));
        return $"{minutes}:{seconds:00}/km";
    }
}
