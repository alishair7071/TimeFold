using System.Text;

namespace TimeFold.Core.Storage;

/// <summary>
/// Turns what you typed into something FTS5 can safely run.
///
/// This exists for two reasons, and the second one is the important one.
///
/// 1. FTS5 has its own little query language. Characters like " * ( ) : - ^
///    mean something in it, so typing a normal sentence containing them is
///    either an error or a search for something you did not ask for.
///
/// 2. That query language is a language, and user text must never be pasted
///    straight into a language. SQLite parameters protect the SQL itself, but
///    the MATCH text is parsed separately by FTS5 - so it needs its own
///    handling. Everything here works by taking words OUT rather than escaping
///    them in: anything that is not a letter or a digit is simply dropped.
///
/// The result is a search that behaves the way people expect: type two words
/// and you get records containing both, and the last word matches as you type.
/// </summary>
public static class SearchQuery
{
    /// <summary>
    /// Builds an FTS5 MATCH expression, or null when there is nothing to search
    /// for. Null means "do not search", never "match everything".
    /// </summary>
    public static string Build(string typedText)
    {
        if (string.IsNullOrWhiteSpace(typedText))
        {
            return null;
        }

        List<string> words = SplitIntoWords(typedText);

        if (words.Count == 0)
        {
            return null;
        }

        StringBuilder query = new StringBuilder();

        for (int i = 0; i < words.Count; i++)
        {
            if (i > 0)
            {
                // Every word must appear. Searching "jaeger pull" should find
                // the pull request, not everything mentioning either word.
                query.Append(" AND ");
            }

            // Each word is wrapped in double quotes so FTS5 treats it as plain
            // text rather than as part of its query language.
            query.Append('"');
            query.Append(words[i]);
            query.Append('"');

            bool isLastWord = i == words.Count - 1;

            if (isLastWord)
            {
                // The last word matches on its beginning, so results appear
                // while you are still typing: "jaeg" already finds "jaeger".
                query.Append('*');
            }
        }

        return query.ToString();
    }

    /// <summary>
    /// Keeps letters and digits, throws everything else away.
    ///
    /// Done this way round on purpose. A list of characters to remove would
    /// have to be complete to be safe, and there is always one more. A list of
    /// characters to keep is safe even when something unexpected arrives.
    /// </summary>
    private static List<string> SplitIntoWords(string text)
    {
        List<string> words = new List<string>();
        StringBuilder current = new StringBuilder();

        foreach (char letter in text)
        {
            // char.IsLetterOrDigit is true for every alphabet, not only
            // English, so searching in Urdu or Arabic works the same way.
            if (char.IsLetterOrDigit(letter))
            {
                current.Append(letter);
            }
            else if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            words.Add(current.ToString());
        }

        return words;
    }
}
