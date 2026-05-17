using System.Globalization;
using MyHomePage.Abstractions;
using MyHomePage.Models;

namespace MyHomePage.Services;

/// <summary>
/// Default <see cref="ITrainingStatsService"/> implementation. Pure
/// LINQ — no I/O, no logging — so the result is deterministic and unit
/// tests need no fixtures beyond the input list.
/// </summary>
public sealed class TrainingStatsService : ITrainingStatsService
{
    private const int MaxBestEffortHighlights = 10;

    /// <inheritdoc />
    public TrainingStats Compute(
        IReadOnlyList<Video> videos,
        DateTime fromUtc,
        DateTime toUtc)
    {
        ArgumentNullException.ThrowIfNull(videos);

        var fromUtcDay = DateTime.SpecifyKind(fromUtc.Date, DateTimeKind.Utc);
        var toUtcEnd = DateTime.SpecifyKind(
            toUtc.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

        var inWindow = videos
            .Where(v => v.Training is not null
                        && v.Training.StartTimeUtc >= fromUtcDay
                        && v.Training.StartTimeUtc <= toUtcEnd)
            .ToList();

        if (inWindow.Count == 0)
        {
            return new TrainingStats
            {
                FromUtc = fromUtcDay,
                ToUtc = toUtcEnd
            };
        }

        var distance = inWindow.Sum(v => v.Training!.DistanceMeters ?? 0);
        var duration = TimeSpan.FromTicks(inWindow.Sum(v => v.Training!.Duration.Ticks));
        var elevation = inWindow.Sum(v => v.Training!.ElevationGainMeters ?? 0);
        var calories = inWindow.Sum(v => v.Training!.Calories ?? 0);
        var sufferTotal = inWindow.Sum(v => v.Training!.SufferScore ?? 0);
        var prCount = inWindow.Sum(v => v.Training!.PersonalRecordCount ?? 0);
        var achievements = inWindow.Sum(v => v.Training!.AchievementCount ?? 0);

        var byCategoryCount = inWindow
            .GroupBy(v => v.Category)
            .ToDictionary(g => g.Key, g => g.Count());
        var byCategoryDistance = inWindow
            .GroupBy(v => v.Category)
            .ToDictionary(g => g.Key, g => g.Sum(v => v.Training!.DistanceMeters ?? 0));

        return new TrainingStats
        {
            FromUtc = fromUtcDay,
            ToUtc = toUtcEnd,
            SessionCount = inWindow.Count,
            TotalDistanceMeters = distance,
            TotalDuration = duration,
            TotalElevationGainMeters = elevation,
            TotalCalories = calories,
            TotalSufferScore = sufferTotal,
            TotalPersonalRecords = prCount,
            TotalAchievements = achievements,
            SessionsByCategory = byCategoryCount,
            DistanceByCategory = byCategoryDistance,
            WeeklyBuckets = BuildWeeklyBuckets(inWindow, fromUtcDay, toUtcEnd),
            RecentRecords = ExtractRecords(inWindow),
            LongestStreakDays = ComputeLongestStreak(inWindow),
            CurrentStreakDays = ComputeCurrentStreak(inWindow)
        };
    }

    private static IReadOnlyList<WeeklyTrainingBucket> BuildWeeklyBuckets(
        IReadOnlyList<Video> sessions,
        DateTime fromUtc,
        DateTime toUtc)
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        var grouped = sessions
            .GroupBy(v => GetWeekStart(v.Training!.StartTimeUtc))
            .OrderBy(g => g.Key)
            .ToList();

        if (grouped.Count == 0) return Array.Empty<WeeklyTrainingBucket>();

        // Fill in zero-rows so the chart x-axis is continuous even when a
        // week had no recorded sessions.
        var first = grouped.First().Key;
        var last = grouped.Last().Key;
        var weekStart = first;
        var lookup = grouped.ToDictionary(g => g.Key, g => g.ToList());

        var buckets = new List<WeeklyTrainingBucket>();
        while (weekStart <= last)
        {
            if (lookup.TryGetValue(weekStart, out var weekSessions))
            {
                buckets.Add(new WeeklyTrainingBucket
                {
                    WeekStartUtc = weekStart,
                    Label = FormatWeekLabel(weekStart, calendar),
                    DistanceMeters = weekSessions.Sum(v => v.Training!.DistanceMeters ?? 0),
                    Duration = TimeSpan.FromTicks(weekSessions.Sum(v => v.Training!.Duration.Ticks)),
                    ElevationGainMeters = weekSessions.Sum(v => v.Training!.ElevationGainMeters ?? 0),
                    SessionCount = weekSessions.Count
                });
            }
            else
            {
                buckets.Add(new WeeklyTrainingBucket
                {
                    WeekStartUtc = weekStart,
                    Label = FormatWeekLabel(weekStart, calendar),
                    DistanceMeters = 0,
                    Duration = TimeSpan.Zero,
                    ElevationGainMeters = 0,
                    SessionCount = 0
                });
            }
            weekStart = weekStart.AddDays(7);
        }
        return buckets;
    }

    private static DateTime GetWeekStart(DateTime utcDate)
    {
        var local = DateTime.SpecifyKind(utcDate.Date, DateTimeKind.Utc);
        var diff = ((int)local.DayOfWeek + 6) % 7; // Monday-anchored
        return local.AddDays(-diff);
    }

    private static string FormatWeekLabel(DateTime weekStart, Calendar calendar)
    {
        var year = calendar.GetYear(weekStart);
        var week = ISOWeek.GetWeekOfYear(weekStart);
        return $"{year:D4}-W{week:D2}";
    }

    private static IReadOnlyList<TrainingBestEffortHighlight> ExtractRecords(
        IReadOnlyList<Video> sessions) =>
        sessions
            .Where(v => v.Training!.BestEfforts.Any(b => b.PersonalRecordRank == 1))
            .SelectMany(v => v.Training!.BestEfforts
                .Where(b => b.PersonalRecordRank == 1)
                .Select(b => new TrainingBestEffortHighlight
                {
                    Name = b.Name,
                    Duration = b.Duration,
                    AchievedAtUtc = v.Training!.StartTimeUtc,
                    VideoId = v.Id,
                    SessionTitle = v.Title
                }))
            .OrderByDescending(h => h.AchievedAtUtc)
            .Take(MaxBestEffortHighlights)
            .ToList();

    private static int ComputeLongestStreak(IReadOnlyList<Video> sessions)
    {
        var days = sessions
            .Select(v => v.Training!.StartTimeUtc.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (days.Count == 0) return 0;

        var longest = 1;
        var current = 1;
        for (var i = 1; i < days.Count; i++)
        {
            if (days[i] == days[i - 1].AddDays(1))
            {
                current++;
                if (current > longest) longest = current;
            }
            else
            {
                current = 1;
            }
        }
        return longest;
    }

    private static int ComputeCurrentStreak(IReadOnlyList<Video> sessions)
    {
        var trainingDays = sessions
            .Select(v => v.Training!.StartTimeUtc.Date)
            .ToHashSet();
        if (trainingDays.Count == 0) return 0;

        var today = DateTime.UtcNow.Date;
        var streak = 0;
        var cursor = today;
        while (trainingDays.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }
        return streak;
    }
}
