using FluentAssertions;
using WriteStudio.Core.Models;
using WriteStudio.Storage;
using Xunit;

namespace WriteStudio.Storage.Tests;

public class ProjectStorageTests
{
    [Fact]
    public async Task ProjectStorageService_SaveAndLoad_RoundtripsMetadataPagesAndTimeline()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WriteStudio_StorageTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new ProjectStorageService();
            var session = new RecordingSession
            {
                SessionId = "test_session_1",
                Metadata = new ProjectMetadata
                {
                    Title = "Calculus Lecture 1",
                    Author = "Prof. Euler",
                    CanvasWidth = 1920,
                    CanvasHeight = 1080,
                    Duration = TimeSpan.FromMinutes(45)
                },
                Pages = new List<WhiteboardPage>
                {
                    new()
                    {
                        Index = 0,
                        Title = "Introduction",
                        Background = BackgroundStyle.Blackboard,
                        Strokes = new List<DrawingStroke>
                        {
                            new()
                            {
                                Color = ColorInfo.Yellow,
                                Thickness = 3.5,
                                Points = new List<DrawingPoint>
                                {
                                    DrawingPoint.Create(50, 50, 0.6f, TimeSpan.FromSeconds(2)),
                                    DrawingPoint.Create(150, 150, 0.7f, TimeSpan.FromSeconds(3))
                                }
                            }
                        }
                    }
                },
                Events = new List<TimelineEvent>
                {
                    new PageChangedTimelineEvent { Timestamp = TimeSpan.Zero, PreviousPageIndex = 0, NewPageIndex = 0 },
                    new BackgroundChangedTimelineEvent { Timestamp = TimeSpan.FromSeconds(1), PageIndex = 0, NewBackground = BackgroundStyle.Blackboard }
                }
            };

            await storage.SaveProjectAsync(session, tempDir);

            File.Exists(Path.Combine(tempDir, "project.json")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "timeline.json")).Should().BeTrue();
            File.Exists(Path.Combine(tempDir, "strokes", "page_0.json")).Should().BeTrue();

            var loaded = await storage.LoadProjectAsync(tempDir);
            loaded.Metadata.Title.Should().Be("Calculus Lecture 1");
            loaded.Metadata.Author.Should().Be("Prof. Euler");
            loaded.Pages.Should().HaveCount(1);
            loaded.Pages[0].Background.Should().Be(BackgroundStyle.Blackboard);
            loaded.Pages[0].Strokes.Should().HaveCount(1);
            loaded.Events.Should().HaveCount(2);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task ProjectStorageService_ZipPackage_ExportsAndImportsSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "WriteStudio_ZipSrc_" + Guid.NewGuid().ToString("N"));
        var tempZip = Path.Combine(Path.GetTempPath(), "WriteStudio_Package_" + Guid.NewGuid().ToString("N") + ".wstudio");
        var tempDest = Path.Combine(Path.GetTempPath(), "WriteStudio_ZipDst_" + Guid.NewGuid().ToString("N"));

        try
        {
            var storage = new ProjectStorageService();
            var session = await storage.CreateNewProjectAsync("Physics 101", tempDir);
            await storage.SaveProjectAsync(session, tempDir);

            await storage.ExportProjectPackageAsync(tempDir, tempZip);
            File.Exists(tempZip).Should().BeTrue();

            var imported = await storage.ImportProjectPackageAsync(tempZip, tempDest);
            imported.Metadata.Title.Should().Be("Physics 101");
        }
        finally
        {
            if (Directory.Exists(tempDir)) try { Directory.Delete(tempDir, recursive: true); } catch { }
            if (Directory.Exists(tempDest)) try { Directory.Delete(tempDest, recursive: true); } catch { }
            if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
        }
    }
}
