using FluentAssertions;
using WriteStudio.Audio;
using WriteStudio.Camera;
using WriteStudio.Core.Models;
using WriteStudio.Core.Time;
using WriteStudio.Recording;
using WriteStudio.Whiteboard;
using WriteStudio.Whiteboard.UndoRedo;
using Xunit;

namespace WriteStudio.Recording.Tests;

public class RecordingServiceTests
{
    [Fact]
    public async Task RecordingService_Lifecycle_StartsPausesResumesAndStopsCleanly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WriteStudioTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new RecordingClock();
            var undoRedo = new UndoRedoManager();
            var wb = new WhiteboardService(undoRedo);
            var audio = new AudioCaptureService();
            var cam = new CameraService();

            var service = new RecordingService(clock, wb, audio, cam);

            service.State.Should().Be(RecordingState.Stopped);

            await service.StartRecordingAsync(tempDir);
            service.State.Should().Be(RecordingState.Recording);

            // Draw a stroke during recording
            var stroke = wb.StartStroke(10, 10, 0.5f, clock.ElapsedTime);
            wb.AppendPoint(stroke.Id, 20, 20, 0.5f, clock.ElapsedTime);
            wb.CompleteStroke(stroke.Id);

            await Task.Delay(50);
            await service.PauseRecordingAsync();
            service.State.Should().Be(RecordingState.Paused);

            await Task.Delay(50);
            await service.ResumeRecordingAsync();
            service.State.Should().Be(RecordingState.Recording);

            var session = await service.StopRecordingAsync();
            service.State.Should().Be(RecordingState.Stopped);

            session.Should().NotBeNull();
            session.Events.Should().NotBeEmpty();
            session.Events.Should().Contain(e => e is StrokeStartedTimelineEvent);
            session.Events.Should().Contain(e => e is StrokeCompletedTimelineEvent);
            session.Events.Should().Contain(e => e is RecordingStateChangedTimelineEvent);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
