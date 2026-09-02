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
    [FromServices] IProjectStorageService storage) =>
{
    if (session == null || session.Events.Count == 0 && session.Pages.All(p => p.Strokes.Count == 0))
    {
        return Results.BadRequest("Session is empty.");
    }

    string exportId = Guid.NewGuid().ToString("N");
    string tempDir = Path.Combine(Path.GetTempPath(), "WriteStudio_WebExport_" + exportId);
    Directory.CreateDirectory(tempDir);

    string outputMp4 = Path.Combine(tempDir, $"WriteStudio_Lesson_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

    var settings = new ExportSettings(
        OutputFilePath: outputMp4,
        Width: 1920,
        Height: 1080,
        FrameRate: 30,
        VideoBitrateKbps: 4000
    );

    try
    {
        // Save session structure
        await storage.SaveProjectAsync(session, tempDir);

        bool success = await rendering.RenderProjectAsync(session, tempDir, settings);
        if (success && File.Exists(outputMp4))
        {
            byte[] videoBytes = await File.ReadAllBytesAsync(outputMp4);
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            return Results.File(videoBytes, "video/mp4", Path.GetFileName(outputMp4));
        }

        return Results.Problem("Rendering failed to generate video output.");
    }
    catch (Exception ex)
    {
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
