using System.Globalization;
using LiftX.Models;
using LiftX.Services;

namespace LiftX.Components;

public enum SetField { Weight, Reps, Distance, Time }

/// <summary>
/// State for LiftX's own number keypad on the lift screen (replaces the iOS keyboard).
/// Tapping a box selects its number; the first key replaces it, later keys append.
/// Leaving a box you cleared sets it to 0. Next moves weight → reps; on the last box it checks the set.
/// </summary>
public class KeypadState
{
    public SetEntry? Set { get; private set; }
    public SetField Field { get; private set; }
    public IReadOnlyList<SetField> Fields { get; private set; } = Array.Empty<SetField>();
    public string Buffer { get; private set; } = "";
    /// <summary>Nothing typed yet since the box was tapped: the shown number is "selected" and the first key replaces it.</summary>
    public bool Fresh { get; private set; }
    public bool IsOpen => Set != null;
    /// <summary>Bumped whenever a different box becomes active (used to scroll it into view).</summary>
    public int Activation { get; private set; }

    private Func<Task>? _onCommit;
    private Func<Task>? _onLastNext;

    public event Action? Changed;

    public bool IsEditing(SetEntry s, SetField f) => Set == s && Field == f;
    public bool AllowsDecimal => Field is SetField.Weight or SetField.Distance;
    public bool OnLastField => Fields.Count == 0 || Field == Fields[^1];

    public async Task Open(SetEntry set, SetField field, IReadOnlyList<SetField> fields, Func<Task> onCommit, Func<Task>? onLastNext)
    {
        if (Set == set && Field == field) return;
        await Commit();
        Set = set;
        Field = field;
        Fields = fields;
        _onCommit = onCommit;
        _onLastNext = onLastNext;
        Buffer = "";
        Fresh = true;
        Activation++;
        Changed?.Invoke();
    }

    public void Key(char c)
    {
        if (Set == null) return;
        if (c == '.' && !AllowsDecimal) return;
        var b = Fresh ? "" : Buffer;
        Fresh = false;
        if (c == '.' && b.Contains('.')) { Buffer = b; Changed?.Invoke(); return; }
        if (b.Replace(".", "").Length >= (Field == SetField.Time ? 6 : 5)) { Buffer = b; Changed?.Invoke(); return; }
        if (c == '.' && b.Length == 0) b = "0";
        if (b == "0" && c != '.') b = ""; // no leading zeros
        Buffer = b + c;
        Changed?.Invoke();
    }

    public void Backspace()
    {
        if (Set == null) return;
        if (Fresh) { Buffer = ""; Fresh = false; }
        else if (Buffer.Length > 0) Buffer = Buffer[..^1];
        Changed?.Invoke();
    }

    /// <summary>Writes what was typed into the set. Nothing typed = no change.</summary>
    public async Task Commit()
    {
        if (Set == null || Fresh) return;
        switch (Field)
        {
            case SetField.Weight:
                Set.Weight = ParseNumber(Buffer);
                break;
            case SetField.Reps:
                Set.Reps = int.TryParse(Buffer, out var r) ? r : 0;
                break;
            case SetField.Distance:
                Set.Distance = ParseNumber(Buffer);
                break;
            case SetField.Time:
                Set.Seconds = ExerciseInfo.ParseTime(Buffer) ?? 0;
                break;
        }
        Fresh = true;
        if (_onCommit != null) await _onCommit();
    }

    public async Task Next()
    {
        if (Set == null) return;
        await Commit();
        var i = Fields.ToList().IndexOf(Field);
        if (i >= 0 && i < Fields.Count - 1)
        {
            Field = Fields[i + 1];
            Buffer = "";
            Fresh = true;
            Activation++;
            Changed?.Invoke();
            return;
        }
        var last = _onLastNext;
        Close();
        if (last != null) await last();
    }

    public async Task CloseAsync()
    {
        await Commit();
        Close();
    }

    private void Close()
    {
        if (Set == null) return;
        Set = null;
        _onCommit = null;
        _onLastNext = null;
        Changed?.Invoke();
    }

    /// <summary>What a box shows while being typed into.</summary>
    public string TypedText => Field == SetField.Time
        ? (Buffer.Length == 0 ? "" : ExerciseInfo.FormatTime(ExerciseInfo.ParseTime(Buffer)))
        : Buffer;

    private static double ParseNumber(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
