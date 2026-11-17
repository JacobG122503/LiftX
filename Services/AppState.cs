using LiftX.Models;
using Microsoft.JSInterop;

namespace LiftX.Services;

public enum Tab { Lift, Exercises, Calendar, Stats, Settings }

/// <summary>
/// Everything the app knows lives here. Data is mirrored to localStorage on every change
/// (so closing the app never loses anything) and written to a file in Files on "Save to Files".
/// </summary>
public class AppState
{
    private const string KeyData = "liftx:data";
    private const string KeyActive = "liftx:active";
    private const string KeyMeta = "liftx:meta";
    private const string KeyTheme = "liftx:theme";

    private readonly IJSInProcessRuntime _js;

    public AppState(IJSRuntime js)
    {
        _js = (IJSInProcessRuntime)js;
    }

    public SaveFile Data { get; private set; } = new();
    public Workout? Active { get; private set; }
    public StoredMeta Meta { get; private set; } = new();
    public bool HasData { get; private set; }
    public string? LoadError { get; private set; }

    /// <summary>Bumped whenever history or the exercise library changes (stats cache key).</summary>
    public int Version { get; private set; }

    // ── UI state ─────────────────────────────────────────────────────────
    public Tab Tab { get; set; } = Tab.Lift;
    public bool WorkoutOpen { get; private set; }
    public Workout? Editing { get; private set; }
    public bool EditingIsNew { get; private set; }
    public Routine? EditingRoutine { get; private set; }
    public bool EditingRoutineIsNew { get; private set; }
    public string? ToastText { get; private set; }
    private int _toastSeq;

    /// <summary>Re-opened from Settings (first-run display is driven by Settings.SeenSaveIntro).</summary>
    public bool ShowSaveIntro { get; private set; }

    public event Action? Changed;
    public void Notify() => Changed?.Invoke();

    public void OpenSaveIntro()
    {
        ShowSaveIntro = true;
        Notify();
    }

    public void CloseSaveIntro()
    {
        ShowSaveIntro = false;
        if (!Data.Settings.SeenSaveIntro)
        {
            Data.Settings.SeenSaveIntro = true;
            // Not "dirty": this alone isn't worth nagging you to re-save; it rides along with your next save
            SaveData(markDirty: false);
        }
        Notify();
    }

    // ── Storage ──────────────────────────────────────────────────────────

    private string? Get(string key) => _js.Invoke<string?>("liftx.get", key);
    private void Set(string key, string value)
    {
        if (!_js.Invoke<bool>("liftx.set", key, value))
            Toast("⚠️ Couldn't save on this device — use Save to Files!");
    }
    private void Remove(string key) => _js.InvokeVoid("liftx.remove", key);

    public void Load()
    {
        try
        {
            var meta = Get(KeyMeta);
            if (!string.IsNullOrEmpty(meta)) Meta = Json.ReadMeta(meta) ?? new();

            var json = Get(KeyData);
            if (!string.IsNullOrEmpty(json))
            {
                Data = Json.ReadSave(json) ?? new SaveFile();
                HasData = true;
            }

            var active = Get(KeyActive);
            if (!string.IsNullOrEmpty(active)) Active = Json.ReadWorkout(active);
        }
        catch (Exception ex)
        {
            LoadError = "Your saved data couldn't be read (" + ex.Message + "). Import your save file to continue.";
            HasData = false;
        }

        Normalize(Data);
        RebuildIndexes();
        if (HasData) ApplyTheme();
        if (Active != null) WorkoutOpen = true;
        UpdateWakeLock();
        try { _js.InvokeVoid("liftx.requestPersist"); } catch { }
    }

    /// <summary>Writes the save file to localStorage. Call after any change to Data.</summary>
    public void SaveData(bool markDirty = true)
    {
        Data.SavedAt = DateTime.UtcNow;
        Set(KeyData, Json.Serialize(Data));
        if (markDirty) Meta.Dirty = true;
        SaveMeta();
        Version++;
        RebuildIndexes();
        Notify();
    }

    public void SaveActive()
    {
        if (Active == null) Remove(KeyActive);
        else Set(KeyActive, Json.Serialize(Active));
    }

    public void SaveMeta() => Set(KeyMeta, Json.Serialize(Meta));

    private static void Normalize(SaveFile d)
    {
        d.Settings ??= new AppSettings();
        d.Exercises ??= new();
        d.Routines ??= new();
        d.Workouts ??= new();
        foreach (var w in d.Workouts)
        {
            w.Exercises ??= new();
            foreach (var e in w.Exercises) e.Sets ??= new();
        }
        foreach (var r in d.Routines) r.Exercises ??= new();
        if (string.IsNullOrWhiteSpace(d.Settings.FileName)) d.Settings.FileName = Defaults.FileName;
        if (d.Settings.RestSeconds <= 0) d.Settings.RestSeconds = 90;
        d.Workouts.Sort((a, b) => a.Date.CompareTo(b.Date));
    }

    // ── Import / export ──────────────────────────────────────────────────

    /// <summary>Loads a LiftX save file (.json) or a Liftoff export (.csv). Replaces all data.</summary>
    public string Import(string text, string? fileName)
    {
        var trimmed = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        SaveFile data;
        bool fromCsv;

        if (trimmed.StartsWith('{'))
        {
            data = Json.ReadSave(trimmed) ?? throw new FormatException("That file is empty.");
            if (!string.Equals(data.App, "LiftX", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("That JSON file isn't a LiftX save file.");
            if (!string.IsNullOrWhiteSpace(fileName) && fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                data.Settings.FileName = fileName;
            fromCsv = false;
        }
        else if (CsvImporter.LooksLikeLiftoffCsv(trimmed))
        {
            data = CsvImporter.Import(trimmed);
            if (HasData) data.Settings = Data.Settings; // keep your theme etc. when re-importing
            fromCsv = true;
        }
        else
        {
            throw new FormatException("Pick a LiftX save file (.json) or a Liftoff export (.csv).");
        }

        Normalize(data);
        Data = data;
        HasData = true;
        LoadError = null;
        SaveData(markDirty: fromCsv);
        if (!fromCsv)
        {
            Meta.Dirty = false;
            Meta.LastExportUtc = null;
            SaveMeta();
        }
        ApplyTheme();
        Notify();
        return $"Loaded {data.Workouts.Count} workouts and {data.Exercises.Count(e => !e.Archived)} exercises";
    }

    public void StartFresh()
    {
        Data = new SaveFile();
        HasData = true;
        LoadError = null;
        SaveData();
        ApplyTheme();
    }

    public void EraseEverything()
    {
        Remove(KeyData);
        Remove(KeyActive);
        Remove(KeyMeta);
        Data = new SaveFile();
        Active = null;
        Meta = new StoredMeta();
        HasData = false;
        WorkoutOpen = false;
        Editing = null;
        EditingRoutine = null;
        Tab = Tab.Lift;
        Version++;
        RebuildIndexes();
        UpdateWakeLock();
        Notify();
    }

    /// <summary>
    /// Writes the save file out through the iOS share sheet ("Save to Files").
    /// Must be called directly from a tap handler with nothing awaited before it,
    /// otherwise iOS refuses to show the share sheet.
    /// </summary>
    public async Task<bool> ExportAsync()
    {
        var pending = _js.InvokeAsync<string>("liftx.saveFile", KeyData, Data.Settings.FileName);
        string result;
        try { result = await pending; }
        catch (Exception ex)
        {
            Toast("Couldn't save the file: " + ex.Message);
            return false;
        }

        if (result is "shared" or "downloaded")
        {
            Meta.Dirty = false;
            Meta.LastExportUtc = DateTime.UtcNow;
            SaveMeta();
            Toast(result == "shared" ? "Saved ✓" : "Downloaded " + Data.Settings.FileName);
            Notify();
            return true;
        }
        if (result != "cancelled") Toast("Couldn't save the file");
        return false;
    }

    // ── Exercise library ─────────────────────────────────────────────────

    private Dictionary<string, ExerciseDef> _defs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<(Workout W, WorkoutExercise E)>> _history = new(StringComparer.OrdinalIgnoreCase);

    private void RebuildIndexes()
    {
        _defs = new(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Data.Exercises) _defs[d.Name] = d;

        _history = new(StringComparer.OrdinalIgnoreCase);
        foreach (var w in Data.Workouts.OrderByDescending(w => w.Date))
            foreach (var e in w.Exercises)
            {
                if (!_history.TryGetValue(e.Name, out var list)) _history[e.Name] = list = new();
                list.Add((w, e));
            }
    }

    public ExerciseDef? FindDef(string name) => _defs.TryGetValue(name, out var d) ? d : null;
    public ExerciseType TypeOf(string name) => FindDef(name)?.Type ?? ExerciseInfo.GuessType(name);
    public int RestFor(string name) => FindDef(name)?.RestSeconds ?? Data.Settings.RestSeconds;

    public IEnumerable<ExerciseDef> Library => Data.Exercises
        .Where(e => !e.Archived)
        .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    public ExerciseDef AddExercise(string name, ExerciseType type)
    {
        name = name.Trim();
        var existing = FindDef(name);
        if (existing != null)
        {
            if (existing.Archived) { existing.Archived = false; existing.Type = type; SaveData(); }
            return existing;
        }
        var def = new ExerciseDef { Name = name, Type = type };
        Data.Exercises.Add(def);
        SaveData();
        return def;
    }

    /// <summary>Makes sure an exercise used in a lift exists in the library (keeps type info for stats).</summary>
    private void EnsureDef(string name)
    {
        if (FindDef(name) == null)
            Data.Exercises.Add(new ExerciseDef { Name = name, Type = ExerciseInfo.GuessType(name) });
    }

    public void RemoveExercise(ExerciseDef def)
    {
        if (SessionCount(def.Name) == 0 && !Data.Routines.Any(r => r.Exercises.Any(e => SameName(e.Name, def.Name))))
            Data.Exercises.Remove(def);
        else
            def.Archived = true; // keep it so old lifts still know how to display it
        foreach (var r in Data.Routines) r.Exercises.RemoveAll(e => SameName(e.Name, def.Name));
        SaveData();
    }

    /// <summary>Renames everywhere (history, routines, current lift). Merges if the new name already exists.</summary>
    public void RenameExercise(ExerciseDef def, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName == def.Name) return;
        var oldName = def.Name;
        var target = FindDef(newName);
        if (target != null && target != def)
        {
            target.Archived = false;
            target.Notes ??= def.Notes;
            target.RestSeconds ??= def.RestSeconds;
            Data.Exercises.Remove(def);
            newName = target.Name;
        }
        else def.Name = newName;

        foreach (var w in Data.Workouts)
            foreach (var e in w.Exercises.Where(e => SameName(e.Name, oldName))) e.Name = newName;
        foreach (var r in Data.Routines)
            foreach (var e in r.Exercises.Where(e => SameName(e.Name, oldName))) e.Name = newName;
        if (Active != null)
        {
            foreach (var e in Active.Exercises.Where(e => SameName(e.Name, oldName))) e.Name = newName;
            SaveActive();
        }
        SaveData();
    }

    public static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ── History lookups ──────────────────────────────────────────────────

    /// <summary>Sets from the most recent time this exercise was done before <paramref name="before"/>.</summary>
    public List<SetEntry>? PreviousSets(string name, DateTime before, string? excludeWorkoutId)
    {
        if (!_history.TryGetValue(name, out var list)) return null;
        foreach (var (w, e) in list)
        {
            if (w.Id == excludeWorkoutId || w.Date >= before) continue;
            if (e.Sets.Count > 0) return e.Sets;
        }
        return null;
    }

    public List<(Workout W, WorkoutExercise E)> HistoryOf(string name) =>
        _history.TryGetValue(name, out var list) ? list : new();

    public int SessionCount(string name) => _history.TryGetValue(name, out var l) ? l.Count : 0;

    public DateTime? LastDone(string name) => _history.TryGetValue(name, out var l) && l.Count > 0 ? l[0].W.Date : null;

    public List<string> RecentExerciseNames(int count)
    {
        var result = new List<string>();
        foreach (var w in Data.Workouts.OrderByDescending(w => w.Date))
            foreach (var e in w.Exercises)
            {
                var def = FindDef(e.Name);
                if (def is { Archived: true }) continue;
                if (!result.Any(r => SameName(r, e.Name))) result.Add(def?.Name ?? e.Name);
                if (result.Count >= count) return result;
            }
        return result;
    }

    // ── Live lift ────────────────────────────────────────────────────────

    public static DateTime LocalNow()
    {
        var n = DateTime.Now;
        return DateTime.SpecifyKind(new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, n.Second), DateTimeKind.Unspecified);
    }

    public void StartWorkout(Routine? routine)
    {
        var w = new Workout { Date = LocalNow(), Name = routine?.Name, RoutineId = routine?.Id };
        if (routine != null)
            foreach (var re in routine.Exercises)
                w.Exercises.Add(new WorkoutExercise
                {
                    Name = re.Name,
                    Sets = Enumerable.Range(0, Math.Max(1, re.Sets)).Select(_ => new SetEntry()).ToList()
                });
        Active = w;
        WorkoutOpen = true;
        SaveActive();
        UpdateWakeLock();
        Notify();
    }

    public void OpenWorkout()
    {
        if (Active == null) return;
        WorkoutOpen = true;
        UpdateWakeLock();
        Notify();
    }

    public void MinimizeWorkout()
    {
        WorkoutOpen = false;
        Notify();
    }

    public void DiscardActive()
    {
        Active = null;
        WorkoutOpen = false;
        SaveActive();
        UpdateWakeLock();
        Notify();
    }

    public record FinishPreview(int DoneSets, int UncheckedWithValues, int Exercises, double Volume, List<string> Prs, bool RoutineChanged, string? RoutineName);

    public FinishPreview PreviewFinish()
    {
        var w = Active!;
        int done = 0, unchecked_ = 0;
        double volume = 0;
        var prs = new List<string>();
        foreach (var e in w.Exercises)
        {
            var type = TypeOf(e.Name);
            foreach (var s in e.Sets)
            {
                if (s.Done)
                {
                    done++;
                    if (type == ExerciseType.Weight) volume += (s.Weight ?? 0) * (s.Reps ?? 0);
                }
                else if (s.HasValues) unchecked_++;
            }

            if (type != ExerciseType.Weight) continue;
            var doneSets = e.Sets.Where(s => s.Done && s.Weight > 0 && s.Reps > 0).ToList();
            var past = HistoryOf(e.Name).Where(h => h.W.Id != w.Id).SelectMany(h => h.E.Sets).Where(s => s.Weight > 0 && s.Reps > 0).ToList();
            if (doneSets.Count == 0 || past.Count == 0) continue;
            var best = doneSets.MaxBy(s => ExerciseInfo.Est1RM(s.Weight!.Value, s.Reps!.Value))!;
            var bestEst = ExerciseInfo.Est1RM(best.Weight!.Value, best.Reps!.Value);
            var pastEst = past.Max(s => ExerciseInfo.Est1RM(s.Weight!.Value, s.Reps!.Value));
            var pastHeaviest = past.Max(s => s.Weight!.Value);
            var heaviest = doneSets.Max(s => s.Weight!.Value);
            if (heaviest > pastHeaviest)
                prs.Add($"{e.Name}: heaviest ever, {ExerciseInfo.Num(heaviest)} lbs");
            else if (bestEst > pastEst + 0.05)
                prs.Add($"{e.Name}: best set, {ExerciseInfo.Num(best.Weight)} × {best.Reps} (est. 1RM {bestEst:0})");
        }

        var routine = w.RoutineId == null ? null : Data.Routines.FirstOrDefault(r => r.Id == w.RoutineId);
        bool changed = routine != null && !RoutineMatches(routine, w);
        return new FinishPreview(done, unchecked_, w.Exercises.Count(e => e.Sets.Any(s => s.Done)), volume, prs, changed, routine?.Name);
    }

    /// <summary>Same exercises, same order, same number of set rows?</summary>
    private static bool RoutineMatches(Routine r, Workout w)
    {
        if (r.Exercises.Count != w.Exercises.Count) return false;
        for (int i = 0; i < r.Exercises.Count; i++)
            if (!SameName(r.Exercises[i].Name, w.Exercises[i].Name) || r.Exercises[i].Sets != w.Exercises[i].Sets.Count)
                return false;
        return true;
    }

    /// <summary>
    /// Moves the live lift into history. <paramref name="updateRoutine"/> (null = not asked) makes the routine
    /// match this lift's exercises, order and set rows.
    /// Weights/reps never live in the routine: next time always pre-fills with what you did last.
    /// Returns false if there was nothing to save.
    /// </summary>
    public bool FinishActive(bool includeUnchecked, bool? updateRoutine)
    {
        if (Active == null) return false;
        var w = Json.Clone(Active);
        foreach (var e in w.Exercises)
            e.Sets = e.Sets.Where(s => s.Done || (includeUnchecked && s.HasValues)).Select(s => s.Values()).ToList();
        w.Exercises.RemoveAll(e => e.Sets.Count == 0);
        if (string.IsNullOrWhiteSpace(w.Notes)) w.Notes = null;
        if (w.Exercises.Count == 0) return false;

        if (updateRoutine is bool update && Active.RoutineId != null && Data.Routines.FirstOrDefault(r => r.Id == Active.RoutineId) is { } routine)
        {
            // Set rows, not checked sets: skipping a set one day doesn't shrink the routine
            if (update)
                routine.Exercises = Active.Exercises
                    .Select(e => new RoutineExercise { Name = e.Name, Sets = Math.Max(1, e.Sets.Count) })
                    .ToList();
        }

        foreach (var e in w.Exercises) EnsureDef(e.Name);
        Data.Workouts.Add(w);
        Data.Workouts.Sort((a, b) => a.Date.CompareTo(b.Date));
        Active = null;
        WorkoutOpen = false;
        SaveActive();
        UpdateWakeLock();
        SaveData();
        return true;
    }

    // ── Stats ────────────────────────────────────────────────────────────

    private StatsSnapshot? _stats;

    /// <summary>Stats for all finished lifts, rebuilt only when the data changes.</summary>
    public StatsSnapshot Stats => _stats is { } st && st.Version == Version
        ? st
        : _stats = StatsEngine.Build(Data.Workouts, TypeOf, Version);

    // ── Editing past lifts ───────────────────────────────────────────────

    public void BeginEdit(Workout w)
    {
        Editing = Json.Clone(w);
        EditingIsNew = false;
        Notify();
    }

    public void BeginNewPast(DateOnly day)
    {
        Editing = new Workout { Date = day.ToDateTime(new TimeOnly(12, 0)) };
        EditingIsNew = true;
        Notify();
    }

    public void CancelEdit()
    {
        Editing = null;
        Notify();
    }

    public bool SaveEdit()
    {
        if (Editing == null) return false;
        var w = Editing;
        foreach (var e in w.Exercises)
        {
            e.Sets.RemoveAll(s => !s.HasValues);
            foreach (var s in e.Sets) s.Done = false;
        }
        w.Exercises.RemoveAll(e => e.Sets.Count == 0);
        if (string.IsNullOrWhiteSpace(w.Notes)) w.Notes = null;
        if (string.IsNullOrWhiteSpace(w.Name)) w.Name = null;

        if (EditingIsNew)
        {
            if (w.Exercises.Count == 0) { Toast("Add at least one set first"); return false; }
            Data.Workouts.Add(w);
        }
        else
        {
            var idx = Data.Workouts.FindIndex(x => x.Id == w.Id);
            if (w.Exercises.Count == 0)
            {
                if (idx >= 0) Data.Workouts.RemoveAt(idx);
            }
            else if (idx >= 0) Data.Workouts[idx] = w;
            else Data.Workouts.Add(w);
        }
        foreach (var e in w.Exercises) EnsureDef(e.Name);
        Data.Workouts.Sort((a, b) => a.Date.CompareTo(b.Date));
        Editing = null;
        SaveData();
        return true;
    }

    public void DeleteWorkout(Workout w)
    {
        Data.Workouts.RemoveAll(x => x.Id == w.Id);
        SaveData();
    }

    // ── Routines ─────────────────────────────────────────────────────────

    private static Routine CloneRoutine(Routine r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Exercises = r.Exercises.Select(e => new RoutineExercise
        {
            Name = e.Name,
            Sets = e.Sets
        }).ToList()
    };

    public void BeginEditRoutine(Routine? r)
    {
        EditingRoutine = r == null ? new Routine() : CloneRoutine(r);
        EditingRoutineIsNew = r == null;
        Notify();
    }

    public void CancelEditRoutine()
    {
        EditingRoutine = null;
        Notify();
    }

    public void SaveRoutine()
    {
        if (EditingRoutine == null) return;
        var r = EditingRoutine;
        r.Name = string.IsNullOrWhiteSpace(r.Name) ? "My Routine" : r.Name.Trim();
        var idx = Data.Routines.FindIndex(x => x.Id == r.Id);
        if (idx >= 0) Data.Routines[idx] = r; else Data.Routines.Add(r);
        foreach (var e in r.Exercises) EnsureDef(e.Name);
        EditingRoutine = null;
        SaveData();
    }

    public void DeleteRoutine(Routine r)
    {
        Data.Routines.RemoveAll(x => x.Id == r.Id);
        SaveData();
    }

    public void DuplicateRoutine(Routine r)
    {
        var copy = CloneRoutine(r);
        copy.Id = Ids.New();
        copy.Name = r.Name + " (copy)";
        Data.Routines.Insert(Data.Routines.IndexOf(r) + 1, copy);
        SaveData();
    }

    public void MoveRoutine(Routine r, int delta)
    {
        var i = Data.Routines.IndexOf(r);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Data.Routines.Count) return;
        (Data.Routines[i], Data.Routines[j]) = (Data.Routines[j], Data.Routines[i]);
        SaveData();
    }

    public void RoutineFromWorkout(Workout w, string name)
    {
        Data.Routines.Add(new Routine
        {
            Name = name.Trim(),
            Exercises = w.Exercises.Select(e => new RoutineExercise
            {
                Name = e.Name,
                Sets = Math.Max(1, e.Sets.Count)
            }).ToList()
        });
        SaveData();
    }

    // ── Settings / theme / device ────────────────────────────────────────

    public void SaveSettings()
    {
        ApplyTheme();
        UpdateWakeLock();
        SaveData();
    }

    public void ApplyTheme()
    {
        var s = Data.Settings;
        try
        {
            _js.InvokeVoid("liftx.applyTheme", s.Background, s.Accent);
            Set(KeyTheme, Json.Serialize(new ThemeColors(s.Background, s.Accent)));
        }
        catch { }
    }

    /// <summary>Applies colors without saving (live preview while dragging a color picker).</summary>
    public void PreviewTheme(string bg, string accent)
    {
        try { _js.InvokeVoid("liftx.applyTheme", bg, accent); } catch { }
    }

    public void UpdateWakeLock()
    {
        try { _js.InvokeVoid("liftx.setWakeLock", Active != null && Data.Settings.KeepAwake); } catch { }
    }

    public void UnlockAudio()
    {
        try { _js.InvokeVoid("liftx.unlockAudio"); } catch { }
    }

    public void Toast(string message)
    {
        ToastText = message;
        var seq = ++_toastSeq;
        Notify();
        _ = Task.Delay(2600).ContinueWith(_ =>
        {
            if (seq != _toastSeq) return;
            ToastText = null;
            Notify();
        });
    }

    public void ScrollTop()
    {
        try { _js.InvokeVoid("liftx.scrollTop"); } catch { }
    }
}
