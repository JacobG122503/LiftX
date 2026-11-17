using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LiftX.Models;

namespace LiftX.Services;

/// <summary>
/// Turns a Liftoff "Export Workouts" CSV into a LiftX save file.
/// Columns: Date,Duration,Workout Name,Exercise Name,Set Order,Weight,Reps,Distance,Seconds,RPE,Notes
/// </summary>
public static partial class CsvImporter
{
    public static bool LooksLikeLiftoffCsv(string text)
    {
        var firstLine = text.TrimStart('﻿').Split('\n').FirstOrDefault() ?? "";
        return firstLine.Contains("Exercise Name") && firstLine.Contains("Date");
    }

    public static SaveFile Import(string csv)
    {
        var rows = ParseCsv(csv.TrimStart('﻿'));
        if (rows.Count < 2) throw new FormatException("The CSV file is empty.");

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Col(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int cDate = Col("Date"), cDur = Col("Duration"), cWName = Col("Workout Name"), cEx = Col("Exercise Name"),
            cOrder = Col("Set Order"), cW = Col("Weight"), cR = Col("Reps"), cD = Col("Distance"), cS = Col("Seconds"), cNotes = Col("Notes");
        if (cDate < 0 || cEx < 0) throw new FormatException("This doesn't look like a Liftoff export (missing Date / Exercise Name columns).");

        string Get(List<string> r, int i) => i >= 0 && i < r.Count ? r[i] : "";
        double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int)Math.Round(D(s));

        var parsed = new List<Row>();
        foreach (var r in rows.Skip(1))
        {
            var ex = Get(r, cEx).Trim();
            if (string.IsNullOrWhiteSpace(ex)) continue;
            var dateText = Get(r, cDate).Trim();
            if (!DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            parsed.Add(new Row(dateText, DateTime.SpecifyKind(date, DateTimeKind.Unspecified), Get(r, cDur), Get(r, cWName).Trim(), ex,
                I(Get(r, cOrder)), D(Get(r, cW)), I(Get(r, cR)), D(Get(r, cD)), I(Get(r, cS)), Get(r, cNotes).Trim()));
        }
        if (parsed.Count == 0) throw new FormatException("No workout rows found in the CSV.");

        // Work out each exercise's type from how it was logged
        var types = new Dictionary<string, ExerciseType>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in parsed.GroupBy(p => p.Exercise, StringComparer.OrdinalIgnoreCase))
            types[g.Key] = InferType(g.Key, g.ToList());

        // One workout per Liftoff session (identified by its exact start timestamp)
        var workouts = new List<Workout>();
        foreach (var session in parsed.GroupBy(p => p.DateText))
        {
            var first = session.First();
            var w = new Workout
            {
                Date = first.Date,
                Name = string.IsNullOrWhiteSpace(first.WorkoutName) ? null : first.WorkoutName,
                DurationSeconds = ParseDurationSeconds(first.Duration) is var d && d > 0 ? d : null,
            };
            foreach (var exGroup in session.GroupBy(p => p.Exercise, StringComparer.OrdinalIgnoreCase))
            {
                var type = types[exGroup.Key];
                var we = new WorkoutExercise { Name = exGroup.First().Exercise };
                foreach (var p in exGroup.OrderBy(p => p.SetOrder))
                    we.Sets.Add(ToSet(p, type));
                w.Exercises.Add(we);
            }
            workouts.Add(w);
        }
        workouts.Sort((a, b) => a.Date.CompareTo(b.Date));

        // Exercise library, with each exercise's latest note carried over
        var defs = new List<ExerciseDef>();
        foreach (var g in parsed.GroupBy(p => p.Exercise, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var note = g.Where(p => !string.IsNullOrWhiteSpace(p.Notes)).OrderByDescending(p => p.Date).Select(p => p.Notes).FirstOrDefault();
            defs.Add(new ExerciseDef { Name = g.First().Exercise, Type = types[g.Key], Notes = note });
        }

        return new SaveFile { Exercises = defs, Workouts = workouts };
    }

    private static ExerciseType InferType(string name, List<Row> rows)
    {
        bool anyW = rows.Any(r => r.Weight > 0), anyR = rows.Any(r => r.Reps > 0);
        bool anyD = rows.Any(r => r.Distance > 0), anyS = rows.Any(r => r.Seconds > 0);
        var guess = ExerciseInfo.GuessType(name);

        if (!anyW && !anyR && (anyD || anyS))
        {
            if (guess == ExerciseType.Stairs) return ExerciseType.Stairs;
            if (anyD || guess == ExerciseType.Cardio) return ExerciseType.Cardio;
            return ExerciseType.Duration;
        }
        if (guess == ExerciseType.Assisted) return ExerciseType.Assisted;
        if (!anyW && anyR) return ExerciseType.Reps;
        if (!anyW && !anyR && guess != ExerciseType.Weight) return guess;
        return ExerciseType.Weight;
    }

    private static SetEntry ToSet(Row p, ExerciseType type)
    {
        var s = new SetEntry();
        if (ExerciseInfo.UsesWeight(type)) { s.Weight = p.Weight; s.Reps = p.Reps; }
        else
        {
            if (p.Weight > 0) s.Weight = p.Weight;
            if (p.Reps > 0) s.Reps = p.Reps;
        }
        if (p.Distance > 0) s.Distance = p.Distance;
        if (p.Seconds > 0) s.Seconds = p.Seconds;
        return s;
    }

    public static int ParseDurationSeconds(string duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return 0;
        var m = DurationRegex().Match(duration);
        if (!m.Success) return 0;
        int h = m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 0;
        int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        int s = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return h * 3600 + min * 60 + s;
    }

    [GeneratedRegex(@"(?:(\d+)\s*hours?)?\s*(?:(\d+)\s*minutes?)?\s*(?:(\d+)\s*seconds?)?", RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();

    /// <summary>RFC 4180-ish parser: handles quoted fields, "" escapes and newlines inside quotes.</summary>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private record Row(string DateText, DateTime Date, string Duration, string WorkoutName, string Exercise,
        int SetOrder, double Weight, int Reps, double Distance, int Seconds, string Notes);
}
