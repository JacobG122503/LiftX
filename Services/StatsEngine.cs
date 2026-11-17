using LiftX.Models;

namespace LiftX.Services;

/// <summary>One logged set, flattened (same shape as a row of the old Liftoff CSV).</summary>
public record FlatSet(DateTime Date, int? DurationSeconds, string WorkoutName, string Exercise, int SetOrder,
    double Weight, int Reps, double Distance, int Seconds, ExerciseType Type);

/// <summary>Per exercise, per day (ported from LiftoffVisualizer).</summary>
public class ExerciseSummary
{
    public string ExerciseName { get; set; } = "";
    public DateTime Date { get; set; }
    public double MaxWeight { get; set; }
    public double MinWeight { get; set; }
    public double TotalVolume { get; set; }
    public int TotalReps { get; set; }
    public int MaxReps { get; set; }
    public int TotalSets { get; set; }
    public double AvgWeight { get; set; }
    public double MaxEstimated1RM { get; set; }
    public double BestSet1RMWeight { get; set; }
    public int BestSet1RMReps { get; set; }
    public int TotalSeconds { get; set; }
    public int MaxSeconds { get; set; }
    public double TotalDistance { get; set; }
}

public class StatsSnapshot
{
    public int Version { get; init; }
    public List<FlatSet> Records { get; init; } = new();
    public List<string> ExerciseNames { get; init; } = new();
    public Dictionary<string, List<ExerciseSummary>> Summaries { get; init; } = new();
    public Dictionary<string, ExerciseType> Types { get; init; } = new();
    public Dictionary<string, int> MuscleGroups { get; init; } = new();

    public int TotalWorkouts;
    public double TotalVolume;
    public int TotalSets;
    public DateTime FirstDate;

    public int LongestStreak;
    public int LongestBreak;
    public string FavoriteExercise = "";
    public string MostImprovedExercise = "";
    public double MostImprovedGain;
    public string MostActiveWeekday = "";
    public string PeakMonth = "";
    public double AvgWorkoutsPerWeek;
    public int MaxSetsInSession;
    public DateTime MaxSetsDate;
    public int MaxRepsInSession;
    public string MaxRepsExercise = "";
    public DateTime MaxRepsDate;
    public double HeaviestLift;
    public string HeaviestExercise = "";
    public double MaxVolumeInSession;
    public DateTime MaxVolumeDate;
    public int MaxExercisesInSession;
    public DateTime MaxExercisesDate;
    public string FirstExercise = "";
    public int TotalRepsAllTime;
    public string MostConsistentMonth = "";
    public double AvgWorkoutDuration;
    public TimeSpan? EarliestWorkoutTime;
    public DateTime? EarliestWorkoutDate;
    public TimeSpan? LatestWorkoutTime;
    public DateTime? LatestWorkoutDate;
    public string MostFrequentWorkoutName = "-";
    public double MostUsedWeight;
    public double TotalDistance;
}

public static class StatsEngine
{
    public static StatsSnapshot Build(IReadOnlyList<Workout> workouts, Func<string, ExerciseType> typeOf, int version)
    {
        var records = new List<FlatSet>();
        var types = new Dictionary<string, ExerciseType>();
        foreach (var w in workouts)
            foreach (var e in w.Exercises)
            {
                if (!types.TryGetValue(e.Name, out var t)) types[e.Name] = t = typeOf(e.Name);
                for (int i = 0; i < e.Sets.Count; i++)
                {
                    var s = e.Sets[i];
                    records.Add(new FlatSet(w.Date, w.DurationSeconds, w.Name ?? "", e.Name, i,
                        s.Weight ?? 0, s.Reps ?? 0, s.Distance ?? 0, s.Seconds ?? 0, t));
                }
            }

        var names = records.Select(r => r.Exercise).Distinct().OrderBy(n => n).ToList();
        var summaries = new Dictionary<string, List<ExerciseSummary>>();
        foreach (var name in names)
            summaries[name] = Summarize(name, records.Where(r => r.Exercise == name));

        var snap = new StatsSnapshot
        {
            Version = version,
            Records = records,
            ExerciseNames = names,
            Summaries = summaries,
            Types = types,
            MuscleGroups = names.GroupBy(ExerciseInfo.MuscleGroup).ToDictionary(g => g.Key, g => g.Count()),
        };
        if (records.Count == 0) return snap;

        // Lifting-only records: cardio/assisted weights don't count as "lbs lifted"
        var lifts = records.Where(r => r.Type == ExerciseType.Weight).ToList();

        snap.TotalWorkouts = records.Select(r => r.Date.Date).Distinct().Count();
        snap.TotalVolume = lifts.Sum(r => r.Weight * r.Reps);
        snap.TotalSets = records.Count;
        snap.FirstDate = records.Min(r => r.Date);

        var workoutDays = records.Select(r => r.Date.Date).Distinct().OrderBy(d => d).ToList();

        // Streak (up to 2 rest days between lifts) & longest break
        int streak = 1, maxStreak = 1, maxBreak = 0;
        for (int i = 1; i < workoutDays.Count; i++)
        {
            var gap = (workoutDays[i] - workoutDays[i - 1]).Days;
            if (gap <= 3) { streak++; maxStreak = Math.Max(maxStreak, streak); }
            else streak = 1;
            if (gap > 1) maxBreak = Math.Max(maxBreak, gap - 1);
        }
        snap.LongestStreak = maxStreak;
        snap.LongestBreak = maxBreak;

        snap.FavoriteExercise = summaries.OrderByDescending(kv => kv.Value.Count).Select(kv => kv.Key).FirstOrDefault() ?? "";

        var improved = summaries
            .Where(kv => types[kv.Key] == ExerciseType.Weight && kv.Value.Any(s => s.MaxWeight > 0))
            .Select(kv => new { Name = kv.Key, Gain = kv.Value.Max(s => s.MaxWeight) - kv.Value.First(s => s.MaxWeight > 0).MaxWeight })
            .OrderByDescending(x => x.Gain).FirstOrDefault();
        snap.MostImprovedExercise = improved?.Name ?? "-";
        snap.MostImprovedGain = improved?.Gain ?? 0;

        snap.MostActiveWeekday = workoutDays.GroupBy(d => d.DayOfWeek).OrderByDescending(g => g.Count())
            .Select(g => g.Key + "s").FirstOrDefault() ?? "";

        snap.PeakMonth = workoutDays.GroupBy(d => new { d.Year, d.Month }).OrderByDescending(g => g.Count())
            .Select(g => new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy")).FirstOrDefault() ?? "";

        var totalWeeks = Math.Max(1, (DateTime.Now - snap.FirstDate).TotalDays / 7.0);
        snap.AvgWorkoutsPerWeek = Math.Round(snap.TotalWorkouts / totalWeeks, 1);

        var byDay = records.GroupBy(r => r.Date.Date).ToList();

        var maxSets = byDay.OrderByDescending(g => g.Count()).First();
        snap.MaxSetsInSession = maxSets.Count();
        snap.MaxSetsDate = maxSets.Key;

        var maxReps = records.GroupBy(r => new { r.Date.Date, r.Exercise }).OrderByDescending(g => g.Sum(r => r.Reps)).First();
        snap.MaxRepsInSession = maxReps.Sum(r => r.Reps);
        snap.MaxRepsExercise = maxReps.Key.Exercise;
        snap.MaxRepsDate = maxReps.Key.Date;

        var heaviest = lifts.Where(r => r.Weight > 0).OrderByDescending(r => r.Weight).FirstOrDefault();
        snap.HeaviestLift = heaviest?.Weight ?? 0;
        snap.HeaviestExercise = heaviest?.Exercise ?? "-";

        var maxVol = byDay.OrderByDescending(g => g.Where(r => r.Type == ExerciseType.Weight).Sum(r => r.Weight * r.Reps)).First();
        snap.MaxVolumeInSession = maxVol.Where(r => r.Type == ExerciseType.Weight).Sum(r => r.Weight * r.Reps);
        snap.MaxVolumeDate = maxVol.Key;

        var maxEx = byDay.OrderByDescending(g => g.Select(r => r.Exercise).Distinct().Count()).First();
        snap.MaxExercisesInSession = maxEx.Select(r => r.Exercise).Distinct().Count();
        snap.MaxExercisesDate = maxEx.Key;

        snap.FirstExercise = records.OrderBy(r => r.Date).Select(r => r.Exercise).FirstOrDefault() ?? "";
        snap.TotalRepsAllTime = records.Sum(r => r.Reps);

        var consistent = workoutDays.GroupBy(d => new { d.Year, d.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Pct = (double)g.Count() / DateTime.DaysInMonth(g.Key.Year, g.Key.Month) })
            .OrderByDescending(x => x.Pct).FirstOrDefault();
        snap.MostConsistentMonth = consistent != null ? new DateTime(consistent.Year, consistent.Month, 1).ToString("MMM yyyy") : "";

        var durations = workouts.Where(w => w.DurationSeconds > 0)
            .GroupBy(w => w.Date.Date).Select(g => g.Max(w => w.DurationSeconds!.Value) / 60.0).ToList();
        snap.AvgWorkoutDuration = durations.Count > 0 ? durations.Average() : 0;

        var earliest = records.OrderBy(r => r.Date.TimeOfDay).First();
        snap.EarliestWorkoutTime = earliest.Date.TimeOfDay;
        snap.EarliestWorkoutDate = earliest.Date.Date;
        var latest = records.OrderByDescending(r => r.Date.TimeOfDay).First();
        snap.LatestWorkoutTime = latest.Date.TimeOfDay;
        snap.LatestWorkoutDate = latest.Date.Date;

        snap.MostFrequentWorkoutName = workouts.Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .GroupBy(w => w.Name!).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "-";

        snap.MostUsedWeight = lifts.Where(r => r.Weight > 0).GroupBy(r => r.Weight).OrderByDescending(g => g.Count())
            .Select(g => g.Key).FirstOrDefault();

        snap.TotalDistance = records.Where(r => r.Type == ExerciseType.Cardio && r.Distance > 0).Sum(r => r.Distance);
        return snap;
    }

    private static List<ExerciseSummary> Summarize(string name, IEnumerable<FlatSet> sets) => sets
        .GroupBy(r => r.Date.Date)
        .Select(g =>
        {
            var best = g.OrderByDescending(r => ExerciseInfo.Est1RM(r.Weight, r.Reps)).First();
            var weighted = g.Where(r => r.Weight > 0).ToList();
            return new ExerciseSummary
            {
                ExerciseName = name,
                Date = g.Key,
                MaxWeight = g.Max(r => r.Weight),
                MinWeight = weighted.Count > 0 ? weighted.Min(r => r.Weight) : 0,
                TotalVolume = g.Sum(r => r.Weight * r.Reps),
                TotalReps = g.Sum(r => r.Reps),
                MaxReps = g.Max(r => r.Reps),
                TotalSets = g.Count(),
                AvgWeight = g.Average(r => r.Weight),
                MaxEstimated1RM = g.Max(r => ExerciseInfo.Est1RM(r.Weight, r.Reps)),
                BestSet1RMWeight = best.Weight,
                BestSet1RMReps = best.Reps,
                TotalSeconds = g.Sum(r => r.Seconds),
                MaxSeconds = g.Max(r => r.Seconds),
                TotalDistance = g.Sum(r => r.Distance),
            };
        })
        .OrderBy(s => s.Date)
        .ToList();

    // ── Metrics ──────────────────────────────────────────────────────────
    // The global selector offers Est. 1RM / Max Weight / Volume / Total Reps. For exercises where
    // those don't make sense (cardio, planks, bodyweight) we chart the closest useful thing instead.

    public static string EffectiveMetric(ExerciseType t, string metric, List<ExerciseSummary> data)
    {
        bool totals = metric is "volume" or "reps";
        return t switch
        {
            ExerciseType.Weight => metric,
            ExerciseType.Assisted => totals ? "reps" : "assist",
            ExerciseType.Reps => totals ? "reps" : "maxreps",
            ExerciseType.Duration => totals ? "time" : "maxtime",
            ExerciseType.Cardio or ExerciseType.Stairs => totals || !data.Any(d => d.TotalDistance > 0) ? "time" : "distance",
            _ => metric
        };
    }

    public static double[] Values(List<ExerciseSummary> data, string metric) => metric switch
    {
        "volume" => data.Select(d => d.TotalVolume).ToArray(),
        "reps" => data.Select(d => (double)d.TotalReps).ToArray(),
        "maxreps" => data.Select(d => (double)d.MaxReps).ToArray(),
        "est1rm" => data.Select(d => Math.Round(d.MaxEstimated1RM, 1)).ToArray(),
        "assist" => data.Select(d => -d.MinWeight).ToArray(),
        "time" => data.Select(d => Math.Round(d.TotalSeconds / 60.0, 1)).ToArray(),
        "maxtime" => data.Select(d => (double)d.MaxSeconds).ToArray(),
        "distance" => data.Select(d => Math.Round(d.TotalDistance, 2)).ToArray(),
        _ => data.Select(d => d.MaxWeight).ToArray()
    };

    public static string MetricLabel(string metric, ExerciseType t) => metric switch
    {
        "volume" => "Total Volume (lbs)",
        "reps" => "Total Reps",
        "maxreps" => "Best Set (reps)",
        "est1rm" => "Est. 1RM (lbs)",
        "assist" => "Assistance (−lbs, higher = less help)",
        "time" => "Time (min)",
        "maxtime" => "Best Hold (sec)",
        "distance" => t == ExerciseType.Stairs ? "Floors" : "Distance (mi)",
        _ => "Max Weight (lbs)"
    };

    public static double[] TrendLine(double[] values)
    {
        int n = values.Length;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++) { sumX += i; sumY += values[i]; sumXY += i * values[i]; sumX2 += (double)i * i; }
        double denom = n * sumX2 - sumX * sumX;
        if (denom == 0) return values.Select(_ => Math.Round(sumY / Math.Max(1, n), 1)).ToArray();
        double slope = (n * sumXY - sumX * sumY) / denom;
        double intercept = (sumY - slope * sumX) / n;
        return Enumerable.Range(0, n).Select(i => Math.Round(intercept + slope * i, 1)).ToArray();
    }

    /// <summary>"up" / "down" / "neutral" using a 1% threshold (same as the Visualizer).</summary>
    public static string TrendDirection(double[] values)
    {
        if (values.Length < 2) return "neutral";
        var trend = TrendLine(values);
        double change = trend[^1] - trend[0];
        double avg = Math.Abs(values.Average());
        if (avg == 0) return "neutral";
        double rel = change / avg;
        if (rel > 0.01) return "up";
        if (rel < -0.01) return "down";
        return "neutral";
    }
}
