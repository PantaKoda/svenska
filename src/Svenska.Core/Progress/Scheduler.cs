namespace Svenska.Core.Progress;

/// <summary>Where a review card stands: its box, when it is due, and its history.</summary>
public sealed record CardState(int Box, DateTime Due, int Reviews, int Lapses);

/// <summary>
/// Decides when a card comes back. The review screen only talks to this interface, so
/// another scheduler (FSRS) can replace <see cref="LeitnerScheduler"/> later.
/// </summary>
public interface IScheduler
{
    /// <summary>The state of a card that has just been created.</summary>
    CardState New(DateTime now, bool dueNow);

    /// <summary>The state after the learner answered the card.</summary>
    CardState Review(CardState state, bool correct, DateTime now);
}

/// <summary>
/// Leitner boxes: a right answer moves the card one box up and it rests longer;
/// a wrong answer sends it back to box 1, due again at once.
/// </summary>
public sealed class LeitnerScheduler : IScheduler
{
    /// <summary>Days of rest after landing in box 1 … 5.</summary>
    public static readonly int[] RestDays = [1, 2, 4, 9, 21];

    public CardState New(DateTime now, bool dueNow) =>
        new(1, dueNow ? now : now.AddDays(RestDays[0]), 0, 0);

    public CardState Review(CardState state, bool correct, DateTime now)
    {
        if (!correct)
            return new CardState(1, now, state.Reviews + 1, state.Lapses + 1);
        var box = Math.Min(state.Box + 1, RestDays.Length);
        return new CardState(box, now.AddDays(RestDays[box - 1]), state.Reviews + 1, state.Lapses);
    }
}
