using System.Text;

namespace TimeFold.Core.Watchers;

/// <summary>
/// Tidies up a window title before it is compared or stored.
///
/// Why this exists:
///
/// Chrome and many chat apps put an unread counter at the front of the window
/// title, like this:
///
///     (490) Inbox - Gmail - Google Chrome
///     (491) Inbox - Gmail - Google Chrome
///
/// To a person that is the same window. To a plain string comparison those are
/// two different titles, so TimeFold would close one record and start another
/// every time a message arrived. On a busy day that is hundreds of junk records
/// for a tab you never touched.
///
/// So we remove the counter. We do this for BOTH comparing and storing - there
/// is no value in remembering that you had 490 unread emails at that moment,
/// and a clean title is easier to read and to search later.
///
/// This is a plain function: same text in, same text out. No files, no state.
/// </summary>
public static class WindowTitleCleaner
{
    /// <summary>
    /// Returns the title with any leading unread counter removed, and with
    /// surrounding spaces trimmed.
    ///
    /// Examples:
    ///     "(490) Inbox - Gmail"   ->  "Inbox - Gmail"
    ///     "(1) WhatsApp"          ->  "WhatsApp"
    ///     "(Draft) My document"   ->  "(Draft) My document"   (not a number)
    ///     "Notes (3)"             ->  "Notes (3)"             (not at the front)
    /// </summary>
    public static string Clean(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        string working = title.Trim();

        // A title could in theory start with more than one counter, so keep
        // removing them until there are none left.
        while (true)
        {
            string shorter = RemoveOneLeadingCounter(working);

            if (shorter == working)
            {
                break;
            }

            working = shorter;
        }

        return working;
    }

    /// <summary>
    /// Removes ONE leading "(number)" if there is one. Returns the text
    /// unchanged if there is not.
    ///
    /// Written as a simple left-to-right walk rather than a regular expression,
    /// so that it is obvious what it does.
    /// </summary>
    private static string RemoveOneLeadingCounter(string text)
    {
        if (text.Length < 3 || text[0] != '(')
        {
            return text;
        }

        int position = 1;
        StringBuilder digits = new StringBuilder();

        while (position < text.Length && char.IsDigit(text[position]))
        {
            digits.Append(text[position]);
            position++;
        }

        // "()" or "(Draft)" - there was no number, so leave it alone.
        if (digits.Length == 0)
        {
            return text;
        }

        // The number must be followed immediately by a closing bracket.
        if (position >= text.Length || text[position] != ')')
        {
            return text;
        }

        // Skip the ')' and any spaces after it.
        position++;

        string rest = text.Substring(position).TrimStart();

        // "(123)" on its own is the whole title - that is all we have, so keep
        // it rather than returning nothing.
        if (rest.Length == 0)
        {
            return text;
        }

        return rest;
    }
}
