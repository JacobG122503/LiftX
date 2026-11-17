using System.Globalization;
using System.Text.RegularExpressions;
using LiftX.Models;

namespace LiftX.Services;

/// <summary>Formatting, parsing and name-based guesses for exercises.</summary>
public static class ExerciseInfo
{
    public static readonly ExerciseType[] AllTypes =
        { ExerciseType.Weight, ExerciseType.Reps, ExerciseType.Assisted, ExerciseType.Duration, ExerciseType.Cardio, ExerciseType.Stairs };

    public static string TypeLabel(ExerciseType t) => t switch
    {
        ExerciseType.Weight => "Weight & reps",
        ExerciseType.Reps => "Reps only (bodyweight)",
        ExerciseType.Assisted => "Assisted (−lbs & reps)",
        ExerciseType.Duration => "Time only (holds)",
        ExerciseType.Cardio => "Cardio (miles & time)",
        ExerciseType.Stairs => "Stairs (floors & time)",
        _ => t.ToString()
    };

    public static string ShortTypeLabel(ExerciseType t) => t switch
    {
        ExerciseType.Weight => "Weight",
        ExerciseType.Reps => "Bodyweight",
        ExerciseType.Assisted => "Assisted",
        ExerciseType.Duration => "Timed",
        ExerciseType.Cardio => "Cardio",
        ExerciseType.Stairs => "Stairs",
        _ => t.ToString()
    };

    /// <summary>Column headers for the two value columns. Null first column = single wide input.</summary>
    public static (string? First, string Second) Columns(ExerciseType t) => t switch
    {
        ExerciseType.Weight => ("LBS", "REPS"),
        ExerciseType.Assisted => ("-LBS", "REPS"),
        ExerciseType.Reps => (null, "REPS"),
        ExerciseType.Duration => (null, "TIME"),
        ExerciseType.Cardio => ("MI", "TIME"),
        ExerciseType.Stairs => ("FLOORS", "TIME"),
        _ => ("LBS", "REPS")
    };

    public static bool UsesWeight(ExerciseType t) => t is ExerciseType.Weight or ExerciseType.Assisted;
    public static bool UsesTime(ExerciseType t) => t is ExerciseType.Duration or ExerciseType.Cardio or ExerciseType.Stairs;

    // ── Name-based guesses ───────────────────────────────────────────────

    /// <summary>Lowercase, with punctuation turned into spaces ("Pull-Ups" → "pull ups").</summary>
    private static string Normalize(string name) =>
        " " + Regex.Replace((name ?? "").ToLowerInvariant().Replace("'", ""), "[^a-z0-9]+", " ").Trim() + " ";

    /// <summary>True if any key appears at the start of a word ("run" matches "running" but not "crunch").</summary>
    private static bool HasWord(string normalized, params string[] keys) =>
        keys.Any(k => normalized.Contains(" " + k));

    public static ExerciseType GuessType(string name)
    {
        var n = Normalize(name);
        if (HasWord(n, "stair", "stepper", "step mill", "stepmill")) return ExerciseType.Stairs;
        if (HasWord(n, "treadmill", "cycling", "bike", "elliptical", "run", "jog", "walk ", "walking", "rowing machine", "rower ", "swim", "hike", "hiking")
            && !HasWord(n, "farmer", "lunge"))
            return ExerciseType.Cardio;
        if (HasWord(n, "plank", "hold ", "dead hang", "wall sit", "l sit")) return ExerciseType.Duration;
        if (HasWord(n, "assisted")) return ExerciseType.Assisted;
        if (!HasWord(n, "machine", "cable", "weighted", "seated", "smith", "barbell", "dumbbell"))
        {
            if (HasWord(n, "push up", "pushup", "pull up", "pullup", "chin up", "chinup", "sit up", "situp",
                    "crunch", "leg raise", "knee raise", "burpee", "dips ", "dip "))
                return ExerciseType.Reps;
        }
        return ExerciseType.Weight;
    }

    // Ordered: first match wins, so more specific keys come first. Keys match at word starts;
    // a trailing space means "whole word only".
    private static readonly (string Key, string Group)[] MuscleMap =
    {
        ("stair", "Cardio"), ("treadmill", "Cardio"), ("cycling", "Cardio"), ("bike", "Cardio"), ("elliptical", "Cardio"),
        ("rowing machine", "Cardio"), ("run", "Cardio"), ("jog", "Cardio"), ("battle rope", "Cardio"), ("basketball", "Cardio"),
        ("calf", "Calves"), ("calves", "Calves"),
        ("wrist", "Forearms/Traps"), ("shrug", "Forearms/Traps"), ("forearm", "Forearms/Traps"), ("farmer", "Forearms/Traps"),
        ("reverse fly", "Shoulders"), ("rear delt", "Shoulders"), ("face pull", "Shoulders"), ("upright row", "Shoulders"),
        ("lateral raise", "Shoulders"), ("front raise", "Shoulders"), ("shoulder", "Shoulders"), ("overhead press", "Shoulders"),
        ("military press", "Shoulders"), ("arnold", "Shoulders"),
        ("leg curl", "Legs"), ("leg extension", "Legs"), ("leg press", "Legs"), ("squat", "Legs"), ("deadlift", "Legs"), ("rdl ", "Legs"),
        ("lunge", "Legs"), ("hip thrust", "Legs"), ("glute", "Legs"), ("adductor", "Legs"), ("abductor", "Legs"), ("hamstring", "Legs"),
        ("crunch", "Abs"), ("plank", "Abs"), ("sit up", "Abs"), ("leg raise", "Abs"), ("knee raise", "Abs"), ("ab ", "Abs"), ("abs ", "Abs"), ("oblique", "Abs"),
        ("tricep", "Triceps"), ("pushdown", "Triceps"), ("skull", "Triceps"), ("dip", "Triceps"), ("kickback", "Triceps"),
        ("curl", "Biceps"), ("bicep", "Biceps"),
        ("bench press", "Chest"), ("chest", "Chest"), ("fly", "Chest"), ("flye", "Chest"), ("pec ", "Chest"), ("push up", "Chest"), ("pushup", "Chest"),
        ("row", "Back"), ("pulldown", "Back"), ("pull up", "Back"), ("pullup", "Back"), ("chin up", "Back"), ("chinup", "Back"),
        ("lat ", "Back"), ("lats ", "Back"), ("pullover", "Back"),
    };

    public static string MuscleGroup(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Other";
        var n = Normalize(name);
        foreach (var (key, group) in MuscleMap)
            if (n.Contains(" " + key)) return group;
        return "Other";
    }

    // ── Formatting ───────────────────────────────────────────────────────

    public static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Num(double? v) => v.HasValue ? Num(v.Value) : "";

    public static string FormatTime(int? seconds)
    {
        if (seconds is null) return "";
        var s = Math.Max(0, seconds.Value);
        var h = s / 3600; var m = s % 3600 / 60; var sec = s % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    /// <summary>"1m 30s" style, used for rest timers.</summary>
    public static string FormatRest(int seconds)
    {
        var m = seconds / 60; var s = seconds % 60;
        if (m == 0) return $"{s}s";
        return s == 0 ? $"{m}m" : $"{m}m {s}s";
    }

    /// <summary>
    /// Parses a time typed on the iPhone number pad. "12:30" works, and so does
    /// microwave style: the last two digits are seconds ("1230" = 12:30, "45" = 0:45, "3000" = 30:00).
    /// </summary>
    public static int? ParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Contains(':'))
        {
            var parts = t.Split(':');
            int total = 0;
            foreach (var p in parts)
            {
                if (!int.TryParse(p.Trim(), out var v)) return null;
                total = total * 60 + v;
            }
            return total;
        }
        var digits = new string(t.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        if (t.Contains('.') && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var mins))
            return (int)Math.Round(mins * 60); // "12.5" = 12.5 minutes
        if (digits.Length > 6) digits = digits[^6..];
        var n = int.Parse(digits);
        int secs = n % 100, rest = n / 100;
        int minutes = rest % 100, hours = rest / 100;
        return hours * 3600 + minutes * 60 + secs;
    }

    public static string FormatSet(SetEntry s, ExerciseType t)
    {
        switch (t)
        {
            case ExerciseType.Weight:
                return $"{Num(s.Weight ?? 0)} × {s.Reps ?? 0}";
            case ExerciseType.Assisted:
                return $"-{Num(s.Weight ?? 0)} × {s.Reps ?? 0}";
            case ExerciseType.Reps:
                return s.Weight > 0 ? $"+{Num(s.Weight)} × {s.Reps ?? 0}" : $"{s.Reps ?? 0} reps";
            case ExerciseType.Duration:
                return FormatTime(s.Seconds ?? 0);
            case ExerciseType.Cardio:
            {
                var parts = new List<string>();
                if (s.Distance > 0) parts.Add($"{Num(s.Distance)} mi");
                if (s.Seconds > 0) parts.Add(FormatTime(s.Seconds));
                return parts.Count == 0 ? "-" : string.Join(" · ", parts);
            }
            case ExerciseType.Stairs:
            {
                var parts = new List<string>();
                if (s.Distance > 0) parts.Add($"{Num(s.Distance)} fl");
                if (s.Seconds > 0) parts.Add(FormatTime(s.Seconds));
                return parts.Count == 0 ? "-" : string.Join(" · ", parts);
            }
        }
        return "-";
    }

    public static string Initials(string name)
    {
        var words = name.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => char.IsLetterOrDigit(w[0]))
            .ToList();
        if (words.Count == 0) return "?";
        if (words.Count == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
        return (char.ToUpperInvariant(words[0][0]).ToString() + char.ToUpperInvariant(words[^1][0]));
    }

    /// <summary>Epley estimated one-rep max.</summary>
    public static double Est1RM(double weight, int reps) => weight > 0 && reps > 0 ? weight * (1 + reps / 30.0) : weight;
}
