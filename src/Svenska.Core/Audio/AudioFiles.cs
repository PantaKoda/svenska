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

    /// <summary>Every line that is played: new words, model dialogues and dictation sentences.</summary>
    public static List<(string Text, bool Slow)> Lines(Course course)
    {
        var lines = new List<(string, bool)>();
        foreach (var unit in course.Units)
        {
            foreach (var word in unit.NewWords)
                lines.Add((word, false));

            var generated = course.Generated.Where(g => g.Unit == unit.Id).SelectMany(g => g.Items);
            var sentences = unit.ModelDialogue.Select(l => l.Sv)
                .Concat(unit.Exercises.Concat(generated)
                    .Where(i => i.Type == ExerciseText.Dictation && !string.IsNullOrWhiteSpace(i.Sv))
                    .Select(i => i.Sv!));
            foreach (var sentence in sentences)
            {
                lines.Add((sentence.Trim(), false));
                lines.Add((sentence.Trim(), true));
            }
        }
        return lines.Distinct().ToList();
    }
}
