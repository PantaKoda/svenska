using Svenska.Core.Content;

namespace Svenska.Core.Exercises;

/// <summary>
/// One step of a dialogue task: what the learner hears first, then the reply to give.
/// </summary>
/// <param name="Index">The position of the learner's turn among all turns.</param>
/// <param name="Item">The reply as an exercise: chosen from options, or typed.</param>
public sealed record DialogueStep(int Index, IReadOnlyList<DialogueLine> Heard, ExerciseItem Item);

/// <summary>
/// The scripted final task of a unit (type: dialogue). The turns of "du" are the learner's;
/// each has an instruction in `en` and an `answer`, typed or chosen from `options`.
/// The other turns are heard.
/// </summary>
public static class DialogueTask
{
    public const string Learner = "du";

    public static bool IsLearnerTurn(ExerciseItem turn) => turn.Speaker == Learner;

    /// <summary>The learner's turns in order, each with the lines heard just before it.</summary>
    public static List<DialogueStep> Steps(ExerciseItem task)
    {
        var steps = new List<DialogueStep>();
        var heard = new List<DialogueLine>();
        var turns = task.Turns ?? [];
        for (var i = 0; i < turns.Count; i++)
        {
            var turn = turns[i];
            if (!IsLearnerTurn(turn))
            {
                heard.Add(new DialogueLine { Speaker = turn.Speaker ?? "", Sv = turn.Sv ?? "", En = turn.En ?? "" });
                continue;
            }

            var chosen = turn.Options.Count > 0;
            steps.Add(new DialogueStep(i, heard, new ExerciseItem
            {
                Type = chosen ? ExerciseText.ListenChoose : ExerciseText.Translate,
                En = turn.En,
                Sv = chosen ? null : turn.Answer,
                Answer = turn.Answer,
                Accept = [.. turn.Accept],
                Options = [.. turn.Options],
                Grammar = [.. turn.Grammar],
                Review = turn.Review,
            }));
            heard = [];
        }
        return steps;
    }
}
