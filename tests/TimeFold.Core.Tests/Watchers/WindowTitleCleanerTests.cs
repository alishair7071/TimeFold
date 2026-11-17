using TimeFold.Core.Watchers;
using Xunit;

namespace TimeFold.Core.Tests.Watchers;

/// <summary>
/// Tests for the window title cleaner.
///
/// Getting this wrong is cheap in one direction and expensive in the other:
/// failing to strip a counter creates junk records, but stripping too much
/// quietly damages real titles you will want to read months later. So there
/// are tests for both.
/// </summary>
public class WindowTitleCleanerTests
{
    [Theory]
    [InlineData("(490) Inbox - Gmail - Google Chrome", "Inbox - Gmail - Google Chrome")]
    [InlineData("(1) WhatsApp", "WhatsApp")]
    [InlineData("(12) Slack | general", "Slack | general")]
    [InlineData("(999999) Something", "Something")]
    public void RemovesALeadingUnreadCounter(string input, string expected)
    {
        Assert.Equal(expected, WindowTitleCleaner.Clean(input));
    }

    [Fact]
    public void TwoTitlesThatDifferOnlyByTheCounterBecomeIdentical()
    {
        // This is the whole point of the class: these two must stop looking
        // like two different windows.
        string before = WindowTitleCleaner.Clean("(20) WhatsApp");
        string after = WindowTitleCleaner.Clean("(21) WhatsApp");

        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("(Draft) My document", "(Draft) My document")]
    [InlineData("Notes (3)", "Notes (3)")]
    [InlineData("Report (2024) final", "Report (2024) final")]
    [InlineData("Plain title", "Plain title")]
    [InlineData("(123)", "(123)")]
    public void LeavesEverythingElseAlone(string input, string expected)
    {
        Assert.Equal(expected, WindowTitleCleaner.Clean(input));
    }

    [Fact]
    public void HandlesMoreThanOneCounter()
    {
        Assert.Equal("Chat", WindowTitleCleaner.Clean("(2) (5) Chat"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void HandlesEmptyInputWithoutCrashing(string input, string expected)
    {
        Assert.Equal(expected, WindowTitleCleaner.Clean(input));
    }

    [Fact]
    public void TrimsSurroundingSpaces()
    {
        Assert.Equal("Notepad", WindowTitleCleaner.Clean("  Notepad  "));
    }

    [Fact]
    public void KeepsNonEnglishCharactersAndEmoji()
    {
        // The Win32 calls use the Unicode versions on purpose. If cleaning
        // damaged those characters, that effort would be wasted.
        string input = "(3) مرحبا \U0001F3B5 YouTube";
        string expected = "مرحبا \U0001F3B5 YouTube";

        Assert.Equal(expected, WindowTitleCleaner.Clean(input));
    }
}
