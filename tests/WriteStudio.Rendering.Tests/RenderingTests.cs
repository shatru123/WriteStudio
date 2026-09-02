using FluentAssertions;
using WriteStudio.Core.Models;
using WriteStudio.Rendering;
using Xunit;

namespace WriteStudio.Rendering.Tests;

public class RenderingTests
{
    [Fact]
    public void FFmpegService_BuildEncodingArguments_GeneratesValidParameters()
    {
        var service = new FFmpegService();
        string args = service.BuildEncodingArguments(
            width: 1920,
            height: 1080,
            fps: 30,
            audioFilePath: null,
            outputFilePath: "output.mp4"
        );

        args.Should().Contain("-s 1920x1080");
        args.Should().Contain("-r 30");
        args.Should().Contain("pipe:0");
        args.Should().Contain("libx264");
        args.Should().Contain("output.mp4");
    }

    [Fact]
    public void TimelineWhiteboardReconstructor_ReconstructsStrokesAtTimestamp()
    {
        var session = new RecordingSession();
        var stroke1 = new DrawingStroke
        {
            Id = Guid.NewGuid(),
            PageIndex = 0,
            Points = new List<DrawingPoint>
            {
                DrawingPoint.Create(10, 10, 0.5f, TimeSpan.FromSeconds(1)),
                DrawingPoint.Create(20, 20, 0.5f, TimeSpan.FromSeconds(2)),
                DrawingPoint.Create(30, 30, 0.5f, TimeSpan.FromSeconds(3))
            }
        };

        session.Events.Add(new StrokeStartedTimelineEvent
        {
            Timestamp = TimeSpan.FromSeconds(1),
            Stroke = stroke1
        });

        var reconstructor = new TimelineWhiteboardReconstructor(session);

        // At 0.5s: stroke not yet started
        var stateBefore = reconstructor.ReconstructAt(TimeSpan.FromSeconds(0.5));
        stateBefore.VisibleStrokes.Should().BeEmpty();

        // At 1.5s: stroke started, only 1 point visible
        var stateMiddle = reconstructor.ReconstructAt(TimeSpan.FromSeconds(1.5));
        stateMiddle.VisibleStrokes.Should().HaveCount(1);
        stateMiddle.VisibleStrokes[0].Points.Should().HaveCount(1);

        // At 2.5s: 2 points visible
        var stateLater = reconstructor.ReconstructAt(TimeSpan.FromSeconds(2.5));
        stateLater.VisibleStrokes.Should().HaveCount(1);
        stateLater.VisibleStrokes[0].Points.Should().HaveCount(2);

        // At 4.0s: all 3 points visible
        var stateEnd = reconstructor.ReconstructAt(TimeSpan.FromSeconds(4.0));
        stateEnd.VisibleStrokes.Should().HaveCount(1);
        stateEnd.VisibleStrokes[0].Points.Should().HaveCount(3);
    }

    [Fact]
    public void SkiaFrameRenderer_RendersFrameBytesWithoutError()
    {
        using var renderer = new SkiaFrameRenderer(640, 360);
        var state = new WhiteboardRenderState(
            PageIndex: 0,
            Background: BackgroundStyle.Blackboard,
            VisibleStrokes: new List<DrawingStroke>
            {
                new DrawingStroke
                {
                    Color = ColorInfo.Yellow,
                    Thickness = 4.0,
                    Points = new List<DrawingPoint>
                    {
                        DrawingPoint.Create(10, 10),
                        DrawingPoint.Create(100, 100)
                    }
                }
            },
            CameraLayout: CameraLayout.FromPreset(CameraPositionPreset.BottomRight)
        );

        byte[] frameBytes = renderer.RenderFrame(state, 640, 360);
        frameBytes.Should().NotBeNull();
        frameBytes.Length.Should().Be(640 * 360 * 4); // BGRA 4 bytes per pixel
    }
}
