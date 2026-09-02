using FluentAssertions;
using WriteStudio.Core.Models;
using WriteStudio.Core.Time;
using Xunit;

namespace WriteStudio.Core.Tests;

public class RecordingClockTests
{
    [Fact]
    public void Clock_StartsAndTracksElapsedTime()
    {
        var clock = new RecordingClock();
        clock.State.Should().Be(RecordingState.Stopped);
        clock.ElapsedTime.Should().Be(TimeSpan.Zero);

        clock.Start();
        clock.State.Should().Be(RecordingState.Recording);

        Thread.Sleep(60);
        clock.ElapsedTime.Should().BeGreaterThan(TimeSpan.FromMilliseconds(40));

        clock.Stop();
        clock.State.Should().Be(RecordingState.Stopped);
    }

    [Fact]
    public void Clock_PauseAndResume_DeductsPauseDurationAccurately()
    {
        var clock = new RecordingClock();
        clock.Start();

        Thread.Sleep(50);
        clock.Pause();
        clock.State.Should().Be(RecordingState.Paused);
        var elapsedAtPause = clock.ElapsedTime;

        // In paused state, elapsed time should remain constant
        Thread.Sleep(100);
        clock.ElapsedTime.Should().Be(elapsedAtPause);

        clock.Resume();
        clock.State.Should().Be(RecordingState.Recording);
        
        Thread.Sleep(50);
        var elapsedAfterResume = clock.ElapsedTime;
        clock.Stop();

        elapsedAfterResume.Should().BeLessThan(TimeSpan.FromMilliseconds(180));
        clock.PauseIntervals.Should().HaveCount(1);
        clock.PauseIntervals[0].Duration.Should().BeGreaterThan(TimeSpan.FromMilliseconds(70));
    }

    [Fact]
    public void Clock_CalculateEffectiveTime_CalculatesCorrectlyAcrossPauseIntervals()
    {
        var clock = new RecordingClock();
        var start = DateTime.UtcNow;

        clock.Start();
        Thread.Sleep(50);
        clock.Pause();
        Thread.Sleep(80);
        clock.Resume();
        Thread.Sleep(50);
        clock.Stop();

        var effectiveAtNow = clock.CalculateEffectiveTime(DateTime.UtcNow);
        effectiveAtNow.Should().BeGreaterThan(TimeSpan.FromMilliseconds(70));
        effectiveAtNow.Should().BeLessThan(TimeSpan.FromMilliseconds(180));
    }
}
