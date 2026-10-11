using Svenska.Core.Content;

namespace Svenska.Core.Exercises;

/// <summary>Chooses the items of one round of exercises for a unit.</summary>
public static class ExerciseSession
{
    public const int DefaultSize = 20;

    /// <summary>The unit's authored and generated items that the player can run.</summary>
    public static List<ExerciseItem> Pool(Course course, Unit unit) =>
        unit.Exercises
            .Concat(course.Generated.Where(g => g.Unit == unit.Id).SelectMany(g => g.Items))
            .Where(i => ExerciseText.Playable.Contains(i.Type))
            .ToList();

    /// <summary>A random draw from the pool, authored and generated items mixed.</summary>
    public static List<ExerciseItem> Pick(Course course, Unit unit, Random random, int size = DefaultSize)
    {
        var pool = Pool(course, unit);
        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(size).ToList();
    }
}
