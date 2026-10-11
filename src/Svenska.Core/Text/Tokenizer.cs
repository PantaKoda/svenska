using System.Text.RegularExpressions;

namespace Svenska.Core.Text;

public static partial class Tokenizer
{
    // A word (letters, may contain a hyphen or apostrophe inside) or a number (25, 12:30, 19,90).
    [GeneratedRegex(@"\p{L}+(?:[-'’]\p{L}+)*|\d+(?:[.,:]\d+)*")]
    private static partial Regex Token();

    /// <summary>Words and numbers of a text, in order, without punctuation.</summary>
    public static List<string> Tokens(string text) => Token().Matches(text).Select(m => m.Value).ToList();

    public static bool IsNumber(string token) => token.Length > 0 && char.IsDigit(token[0]);
}
