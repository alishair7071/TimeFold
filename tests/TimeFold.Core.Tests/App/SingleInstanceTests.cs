using System.Threading;
using TimeFold.App.Composition;
using TimeFold.Core.Logging;
using Xunit;

namespace TimeFold.Core.Tests.App;

/// <summary>
/// Tests for "only one copy runs, and a second one makes the first show itself".
///
/// Each test uses its own pair of names, so that running the tests on a machine
/// where TimeFold is genuinely running does not have the test fighting the real
/// app over the same two system-wide objects.
/// </summary>
public class SingleInstanceTests
{
    private static string UniqueSuffix()
    {
        return ".test." + Guid.NewGuid().ToString("N");
    }

    [Fact]
    public void TheFirstCopyGetsTheLock()
    {
        string suffix = UniqueSuffix();

        using (SingleInstance first = new SingleInstance(suffix))
        {
            Assert.True(first.TryClaim());
            Assert.True(first.IsFirstCopy);
        }
    }

    [Fact]
    public void ASecondCopyDoesNotGetTheLock()
    {
        // Two recorders running at once would both be writing records.
        string suffix = UniqueSuffix();

        using (SingleInstance first = new SingleInstance(suffix))
        using (SingleInstance second = new SingleInstance(suffix))
        {
            Assert.True(first.TryClaim());
            Assert.False(second.TryClaim());
            Assert.False(second.IsFirstCopy);
        }
    }

    [Fact]
    public void TheLockIsFreedWhenTheFirstCopyExits()
    {
        // Otherwise TimeFold could never be started again after a crash without
        // a reboot.
        string suffix = UniqueSuffix();

        using (SingleInstance first = new SingleInstance(suffix))
        {
            Assert.True(first.TryClaim());
        }

        using (SingleInstance afterwards = new SingleInstance(suffix))
        {
            Assert.True(afterwards.TryClaim());
        }
    }

    // ---- the doorbell ------------------------------------------------------

    [Fact]
    public void ASecondCopyMakesTheFirstOneOpenTheInterface()
    {
        // The point of the whole class. Double-clicking the desktop shortcut
        // while TimeFold is already running must open the interface, not send
        // you hunting for a hidden tray icon.
        string suffix = UniqueSuffix();

        using (ManualResetEvent wasAsked = new ManualResetEvent(false))
        using (SingleInstance first = new SingleInstance(suffix))
        {
            Assert.True(first.TryClaim());

            first.StartListeningForOpenRequests(
                new NullLogger(),
                delegate { wasAsked.Set(); });

            Assert.True(SingleInstance.AskTheRunningCopyToOpen(suffix));

            Assert.True(
                wasAsked.WaitOne(TimeSpan.FromSeconds(5)),
                "The running copy was never asked to open the interface.");
        }
    }

    [Fact]
    public void TheDoorbellCanBeRungMoreThanOnce()
    {
        // You will double-click that shortcut many times. The doorbell has to
        // un-press itself after each ring, or only the first would ever work.
        string suffix = UniqueSuffix();

        int timesAsked = 0;

        using (ManualResetEvent askedTwice = new ManualResetEvent(false))
        using (SingleInstance first = new SingleInstance(suffix))
        {
            Assert.True(first.TryClaim());

            first.StartListeningForOpenRequests(
                new NullLogger(),
                delegate
                {
                    if (Interlocked.Increment(ref timesAsked) == 2)
                    {
                        askedTwice.Set();
                    }
                });

            SingleInstance.AskTheRunningCopyToOpen(suffix);
            SingleInstance.AskTheRunningCopyToOpen(suffix);

            Assert.True(
                askedTwice.WaitOne(TimeSpan.FromSeconds(5)),
                "The second ring of the doorbell was never answered.");
        }
    }

    [Fact]
    public void RingingWhenNobodyIsHomeSaysSo()
    {
        // The caller shows the old "look in the system tray" message in this
        // case, rather than silently doing nothing.
        Assert.False(SingleInstance.AskTheRunningCopyToOpen(UniqueSuffix()));
    }

    [Fact]
    public void AFailureWhileOpeningDoesNotStopLaterRings()
    {
        // If the first attempt threw and killed the listening thread, the
        // shortcut would quietly stop working from then on - the worst kind of
        // bug, because nothing visibly breaks.
        string suffix = UniqueSuffix();

        int timesAsked = 0;

        using (ManualResetEvent askedAgain = new ManualResetEvent(false))
        using (SingleInstance first = new SingleInstance(suffix))
        {
            Assert.True(first.TryClaim());

            first.StartListeningForOpenRequests(
                new NullLogger(),
                delegate
                {
                    int count = Interlocked.Increment(ref timesAsked);

                    if (count == 1)
                    {
                        throw new InvalidOperationException("Pretend the browser failed to open.");
                    }

                    askedAgain.Set();
                });

            SingleInstance.AskTheRunningCopyToOpen(suffix);

            // Give the first ring time to fail before the second one.
            Thread.Sleep(100);

            SingleInstance.AskTheRunningCopyToOpen(suffix);

            Assert.True(
                askedAgain.WaitOne(TimeSpan.FromSeconds(5)),
                "A failure while opening killed the listening thread.");
        }
    }

    private class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Error(string message, Exception error) { }
    }
}
