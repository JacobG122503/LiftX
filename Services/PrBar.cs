using LiftX.Models;

namespace LiftX.Services;

/// <summary>
/// The one PR rule, used for gold sets in a lift and gold days on the calendar.
/// Start it from your best before a lift, then feed it that lift's sets in the order they were done.
/// Weight lifts: a set is a PR when its estimated 1-rep max beats the best so far.
/// Bodyweight: more reps than ever. Timed: longer than ever.
/// The first time you ever do an exercise nothing is a PR (there's nothing to beat).
/// </summary>
public sealed class PrBar
{
    public double Est1RM { get; private set; }
    /// <summary>The set behind <see cref="Est1RM"/>.</summary>
    public double Weight { get; private set; }
    public int WeightReps { get; private set; }
    public int Reps { get; private set; }
    public int Seconds { get; private set; }

    // Reps/time only count when there was a previous best to beat (same as before this was shared)
    private readonly bool _hadReps, _hadSeconds;

    public PrBar(double est1Rm, double weight, int weightReps, int reps, int seconds)
    {
        Est1RM = est1Rm; Weight = weight; WeightReps = weightReps; Reps = reps; Seconds = seconds;
        _hadReps = reps > 0;
        _hadSeconds = seconds > 0;
    }

    /// <summary>Best before a lift, from all the sets you did before it. Null when there are none.</summary>
    public static PrBar? From(IEnumerable<SetEntry> pastSets)
    {
        var sets = pastSets as IList<SetEntry> ?? pastSets.ToList();
        if (sets.Count == 0) return null;
        var top = sets.Where(s => s.Weight > 0 && s.Reps > 0).MaxBy(s => ExerciseInfo.Est1RM(s.Weight!.Value, s.Reps!.Value));
        return new PrBar(
            top == null ? 0 : ExerciseInfo.Est1RM(top.Weight!.Value, top.Reps!.Value),
            top?.Weight ?? 0,
            top?.Reps ?? 0,
            sets.Select(s => s.Reps ?? 0).DefaultIfEmpty(0).Max(),
            sets.Select(s => s.Seconds ?? 0).DefaultIfEmpty(0).Max());
    }

    /// <summary>The better of two bests (as if all their sets were done together).</summary>
    public static PrBar Merge(PrBar? a, PrBar b)
    {
        if (a == null) return b;
        var top = b.Est1RM > a.Est1RM ? b : a;
        return new PrBar(top.Est1RM, top.Weight, top.WeightReps, Math.Max(a.Reps, b.Reps), Math.Max(a.Seconds, b.Seconds));
    }

    /// <summary>True if this set is a new PR. Raises the bar so the next set has to beat it.</summary>
    public bool Beat(SetEntry s, ExerciseType type)
    {
        switch (type)
        {
            case ExerciseType.Weight when s.Weight > 0 && s.Reps > 0:
                var e1 = ExerciseInfo.Est1RM(s.Weight!.Value, s.Reps!.Value);
                if (e1 > Est1RM + 0.05)
                {
                    Est1RM = e1; Weight = s.Weight.Value; WeightReps = s.Reps.Value;
                    return true;
                }
                return false;
            case ExerciseType.Reps when _hadReps:
                var r = s.Reps ?? 0;
                if (r > Reps) { Reps = r; return true; }
                return false;
            case ExerciseType.Duration when _hadSeconds:
                var t = s.Seconds ?? 0;
                if (t > Seconds) { Seconds = t; return true; }
                return false;
            default:
                return false;
        }
    }

    /// <summary>Order sets were done in: the order they were checked, else top to bottom.</summary>
    public static IEnumerable<SetEntry> InDoneOrder(IEnumerable<SetEntry> sets) => sets.OrderBy(s => s.DoneOrder ?? int.MaxValue);
}
