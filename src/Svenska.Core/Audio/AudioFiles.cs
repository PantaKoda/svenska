using System.Security.Cryptography;
using System.Text;
using Svenska.Core.Content;
using Svenska.Core.Exercises;

namespace Svenska.Core.Audio;

/// <summary>
/// Which Swedish lines have audio and what their files are called. tools/gen_audio.py gets
/// this list from `Svenska.Validator --audio-list`, so the names are computed in one place.
/// </summary>
public static class AudioFiles
{
    /// <summary>The Piper voice. Part of the file name: another voice gives other files.</summary>
    public const string Voice = "sv_SE-nst-medium";

    public static string FileName(string text, bool slow)
    {
        var key = $"{Voice}|{(slow ? "slow" : "normal")}|{text.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..20].ToLowerInvariant() + ".ogg";
    }

    /// <summary>
    /// Every line that is played: new words, model dialogues, the sentences of the listening
    /// exercises and what the learner hears in the final task.
    /// </summary>
    public static List<(string Text, bool Slow)> Lines(Course course)
    {
        var lines = new List<(string, bool)>();
        foreach (var unit in course.Units)
        {
            foreach (var word in unit.NewWords)
                lines.Add((word, false));

            Both(unit.ModelDialogue.Select(l => l.Sv).Concat(Heard(unit.Exercises)).Concat(unit.Phrases.Select(p => p.Sv)));
            if (unit.FinalTask != null)
                Both(DialogueTask.Steps(unit.FinalTask).SelectMany(s => s.Heard).Select(l => l.Sv));
        }
        // Unit drills and theme practice drills. A scene_click word is heard too.
        var generated = course.Generated.SelectMany(g => g.Items).ToList();
        Both(Heard(generated));
        foreach (var item in generated.Where(i => i.Type == ExerciseText.SceneClick && i.Lemma != null))
            lines.Add((item.Lemma!, false));
        return lines.Distinct().ToList();

        void Both(IEnumerable<string> sentences)
        {
            foreach (var sentence in sentences)
            {
                lines.Add((sentence.Trim(), false));
                lines.Add((sentence.Trim(), true));
            }
        }

        static IEnumerable<string> Heard(IEnumerable<ExerciseItem> items) =>
            items.Where(i => ExerciseText.Heard.Contains(i.Type) && !string.IsNullOrWhiteSpace(i.Sv)).Select(i => i.Sv!);
    }
}
