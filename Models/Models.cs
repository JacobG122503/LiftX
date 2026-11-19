using System.Text.Json.Serialization;

namespace LiftX.Models;

/// <summary>How an exercise is measured. Drives the input columns and stats.</summary>
public enum ExerciseType
{
    Weight,    // LBS x REPS
    Reps,      // REPS only (bodyweight)
    Assisted,  // -LBS x REPS (assistance weight)
    Duration,  // TIME only (plank)
    Cardio,    // MI + TIME (treadmill, bike)
    Stairs     // FLOORS + TIME (stair machine)
}

/// <summary>The whole save file. This is exactly what gets written to Files.</summary>
public class SaveFile
{
    public string App { get; set; } = "LiftX";
    public int Version { get; set; } = 1;
    public DateTime SavedAt { get; set; }
    public AppSettings Settings { get; set; } = new();
    public List<ExerciseDef> Exercises { get; set; } = new();
    public List<Routine> Routines { get; set; } = new();
    public List<Workout> Workouts { get; set; } = new();
}

public class AppSettings
{
    public string Background { get; set; } = Defaults.Background;
    public string Accent { get; set; } = Defaults.Accent;
    public int RestSeconds { get; set; } = 90;
    public bool RestTimerEnabled { get; set; } = true;
    public bool TimerSound { get; set; } = true;
    public bool KeepAwake { get; set; } = true;
    /// <summary>Small "Beat PR" line above each exercise's sets during a lift.</summary>
    public bool ShowPrTargets { get; set; } = true;
    public string FileName { get; set; } = Defaults.FileName;
    /// <summary>The "how saving works" popup has been read and closed.</summary>
    public bool SeenSaveIntro { get; set; }
}

public static class Defaults
{
    public const string Background = "#22252a,#0b0c0e"; // Graphite: grey to near-black gradient (top,bottom)
    public const string Accent = "#3CB371"; // mediumseagreen
    public const string FileName = "LiftX-data.json";
}

public class ExerciseDef
{
    public string Name { get; set; } = "";
    public ExerciseType Type { get; set; }
    /// <summary>Sticky note shown every time you do this exercise (seat settings, cues...).</summary>
    public string? Notes { get; set; }
    /// <summary>Per-exercise rest override; null = use the default.</summary>
    public int? RestSeconds { get; set; }
    /// <summary>Removed from the exercise list. History is kept so stats still work.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Archived { get; set; }
}

public class Routine
{
    public string Id { get; set; } = Ids.New();
    public string Name { get; set; } = "";
    public List<RoutineExercise> Exercises { get; set; } = new();
}

public class RoutineExercise
{
    public string Name { get; set; } = "";
    public int Sets { get; set; } = 3;
}

public class Workout
{
    public string Id { get; set; } = Ids.New();
    /// <summary>Local wall-clock time the lift started (no timezone, same as the Liftoff export).</summary>
    public DateTime Date { get; set; }
    public string? Name { get; set; }
    public string? Notes { get; set; }
    /// <summary>Only present on workouts imported from Liftoff. LiftX does not time lifts.</summary>
    public int? DurationSeconds { get; set; }
    public string? RoutineId { get; set; }
    public List<WorkoutExercise> Exercises { get; set; } = new();
}

public class WorkoutExercise
{
    public string Name { get; set; } = "";
    public List<SetEntry> Sets { get; set; } = new();
}

public class SetEntry
{
    [JsonPropertyName("w")] public double? Weight { get; set; }
    [JsonPropertyName("r")] public int? Reps { get; set; }
    /// <summary>Miles for cardio, floors for stairs.</summary>
    [JsonPropertyName("d")] public double? Distance { get; set; }
    [JsonPropertyName("t")] public int? Seconds { get; set; }

    /// <summary>Only used while a lift is in progress.</summary>
    [JsonPropertyName("done")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Done { get; set; }

    /// <summary>Order sets were checked in during a lift (1, 2, 3…). Decides which set is the PR.</summary>
    [JsonPropertyName("n")]
    public int? DoneOrder { get; set; }

    [JsonIgnore]
    public bool HasValues => Weight != null || Reps != null || Distance != null || Seconds != null;

    public SetEntry Clone() => new() { Weight = Weight, Reps = Reps, Distance = Distance, Seconds = Seconds, Done = Done, DoneOrder = DoneOrder };
    /// <summary>Copy of just the numbers (no "done" flag).</summary>
    public SetEntry Values() => new() { Weight = Weight, Reps = Reps, Distance = Distance, Seconds = Seconds };
}

/// <summary>Small bookkeeping stored next to the data (not part of the save file).</summary>
public class StoredMeta
{
    public bool Dirty { get; set; }
    public DateTime? LastExportUtc { get; set; }
    public DateTime? RestEndsUtc { get; set; }
    public int RestTotal { get; set; }
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
