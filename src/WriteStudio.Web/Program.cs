using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using WriteStudio.Audio;
using WriteStudio.Camera;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;
using WriteStudio.Core.Time;
using WriteStudio.Recording;
using WriteStudio.Rendering;
using WriteStudio.Slides;
using WriteStudio.Storage;
using WriteStudio.Whiteboard;
using WriteStudio.Whiteboard.UndoRedo;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to handle enum strings and polymorphic timeline events
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true));
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Register WriteStudio Core Services
builder.Services.AddSingleton<IRecordingClock, RecordingClock>();
builder.Services.AddSingleton<IUndoRedoManager, UndoRedoManager>();
builder.Services.AddSingleton<IWhiteboardService, WhiteboardService>();
builder.Services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
builder.Services.AddSingleton<ICameraService, CameraService>();
builder.Services.AddSingleton<ISlideService, SlideService>();
builder.Services.AddSingleton<IRecordingService, RecordingService>();
builder.Services.AddSingleton<IFFmpegService, FFmpegService>();
builder.Services.AddSingleton<IRenderingService, RenderingService>();
builder.Services.AddSingleton<IProjectStorageService, ProjectStorageService>();
builder.Services.AddSingleton<IRecoveryService, CrashRecoveryManager>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// API: Check FFmpeg and system status
app.MapGet("/api/status", async (IFFmpegService ffmpeg) =>
{
    bool available = await ffmpeg.ProbeFFmpegAsync();
    return Results.Ok(new
    {
        ffmpegAvailable = available,
        ffmpegPath = ffmpeg.FFmpegPath,
        serverTime = DateTime.UtcNow,
        appVersion = "1.0.0"
    });
});

// API: Export recording session to MP4 via SkiaSharp & FFmpeg
app.MapPost("/api/export", async (
    [FromBody] RecordingSession session,
    [FromServices] IRenderingService rendering,
    [FromServices] IProjectStorageService storage,
    [FromServices] IFFmpegService ffmpeg,
    ILogger<Program> logger) =>
{
    if (session == null)
    {
        return Results.BadRequest("Invalid session payload.");
    }

    try
    {
        logger.LogInformation("Received export request for session {SessionId}. Pages: {Pages}, Events: {Events}",
            session.SessionId, session.Pages.Count, session.Events.Count);

        // If the user drew on the whiteboard directly without clicking "Record",
        // automatically synthesize timeline events from the page strokes so the drawing is fully rendered!
        if (session.Events.Count == 0 && session.Pages.Any(p => p.Strokes.Count > 0))
        {
            double simulatedTimeSec = 0.5;
            foreach (var page in session.Pages)
            {
                foreach (var stroke in page.Strokes)
                {
                    stroke.StartTime = TimeSpan.FromSeconds(simulatedTimeSec);
                    stroke.EndTime = TimeSpan.FromSeconds(simulatedTimeSec + 0.3);

                    // Ensure points have timestamps
                    for (int i = 0; i < stroke.Points.Count; i++)
                    {
                        double ptTime = simulatedTimeSec + (0.3 * i / Math.Max(1, stroke.Points.Count - 1));
                        stroke.Points[i].Timestamp = TimeSpan.FromSeconds(ptTime);
                    }

                    session.Events.Add(new StrokeStartedTimelineEvent
                    {
                        Timestamp = stroke.StartTime,
                        Stroke = stroke
                    });
                    session.Events.Add(new StrokeCompletedTimelineEvent
                    {
                        Timestamp = stroke.EndTime,
                        StrokeId = stroke.Id
                    });

                    simulatedTimeSec += 0.4;
                }
            }

            session.Metadata.Duration = TimeSpan.FromSeconds(Math.Max(3.0, simulatedTimeSec + 1.0));
            logger.LogInformation("Synthesized {EventCount} timeline events for offline canvas export.", session.Events.Count);
        }

        // Ensure minimum video duration
        if (session.Metadata.Duration < TimeSpan.FromSeconds(1))
        {
            session.Metadata.Duration = TimeSpan.FromSeconds(3);
        }

        string exportId = Guid.NewGuid().ToString("N");
        string tempDir = Path.Combine(Path.GetTempPath(), "WriteStudio_WebExport_" + exportId);
        Directory.CreateDirectory(tempDir);

        string outputMp4 = Path.Combine(tempDir, $"WriteStudio_Lesson_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        var settings = new ExportSettings(
            OutputFilePath: outputMp4,
            Width: session.Metadata.CanvasWidth > 0 ? session.Metadata.CanvasWidth : 1920,
            Height: session.Metadata.CanvasHeight > 0 ? session.Metadata.CanvasHeight : 1080,
            FrameRate: session.Metadata.TargetFps > 0 ? session.Metadata.TargetFps : 30,
            VideoBitrateKbps: 4000
        );

        // Save session structure
        await storage.SaveProjectAsync(session, tempDir);

        logger.LogInformation("Starting SkiaSharp + FFmpeg render pipeline to {Path}...", outputMp4);
        bool success = await rendering.RenderProjectAsync(session, tempDir, settings);

        if (success && File.Exists(outputMp4))
        {
            byte[] videoBytes = await File.ReadAllBytesAsync(outputMp4);
            logger.LogInformation("Export succeeded. File size: {Bytes} bytes", videoBytes.Length);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return Results.File(videoBytes, "video/mp4", Path.GetFileName(outputMp4));
        }

        return Results.Problem("Rendering pipeline did not produce output video file.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Exception during video export.");
        return Results.Problem($"Export Error: {ex.Message}");
    }
});

// API: Save project package
app.MapPost("/api/projects/save", async (
    [FromBody] RecordingSession session,
    [FromServices] IProjectStorageService storage) =>
{
    string targetDir = Path.Combine(storage.DefaultProjectsDirectory, $"{session.Metadata.Title}_{DateTime.Now:yyyyMMdd_HHmmss}");
    await storage.SaveProjectAsync(session, targetDir);
    return Results.Ok(new { success = true, path = targetDir });
});

Console.WriteLine("===================================================================");
Console.WriteLine("  WriteStudio Web Server Running!");
Console.WriteLine("  Open: http://localhost:5000 in your browser to start recording");
Console.WriteLine("===================================================================");

app.Run("http://localhost:5000");
