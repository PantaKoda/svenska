using Svenska.Core.Content;

namespace Svenska.Core.Exercises;

/// <summary>Chooses the items of one round of exercises.</summary>
public static class ExerciseSession
{
    public const int DefaultSize = 20;

    /// <summary>The unit's authored and generated items that the player can run.</summary>
    public static List<ExerciseItem> Pool(Course course, Unit unit) =>
        unit.Exercises
            .Concat(course.Generated.Where(g => g.Unit == unit.Id).SelectMany(g => g.Items))
            .Where(i => ExerciseText.Playable.Contains(i.Type))
            .ToList();

    /// <summary>
    /// A random draw from a pool. Items the learner has not yet answered right come first,
    /// so every round brings something new until the pool is done.
    /// </summary>
    public static List<ExerciseItem> Pick(IEnumerable<ExerciseItem> pool, Random random, int size = DefaultSize,
        Func<ExerciseItem, bool>? isDone = null)
    {
        var items = pool.ToList();
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
        if (isDone != null)
            items = [.. items.Where(i => !isDone(i)), .. items.Where(isDone)];
        return items.Take(size).ToList();
    }
}
