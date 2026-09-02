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

// Disable inotify FileSystemWatcher for restricted Linux container environments (Render/Kubernetes)
Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");
Environment.SetEnvironmentVariable("DOTNET_SYSTEM_IO_DISABLEFILEWATCHING", "true");

var builderOptions = new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
};

var builder = WebApplication.CreateBuilder(builderOptions);

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
        appVersion = "1.0.0",
        status = "Healthy"
    });
});

// API: Export recording session to MP4 via SkiaSharp & FFmpeg
app.MapPost("/api/export", async (
    HttpRequest request,
    [FromServices] IRenderingService rendering,
    [FromServices] IProjectStorageService storage,
    [FromServices] IFFmpegService ffmpeg,
    ILogger<Program> logger) =>
{
    RecordingSession? session = null;
    IFormFile? audioFile = null;
    IFormFile? cameraFile = null;

    string exportId = Guid.NewGuid().ToString("N");
    string tempDir = Path.Combine(Path.GetTempPath(), "WriteStudio_WebExport_" + exportId);
    Directory.CreateDirectory(tempDir);
    Directory.CreateDirectory(Path.Combine(tempDir, "audio"));
    Directory.CreateDirectory(Path.Combine(tempDir, "video"));

    try
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            string? sessionJson = form["session"];
            if (!string.IsNullOrEmpty(sessionJson))
            {
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true), new JsonStringEnumConverter() }
                };
                session = JsonSerializer.Deserialize<RecordingSession>(sessionJson, opts);
            }

            audioFile = form.Files.GetFile("audioFile");
            cameraFile = form.Files.GetFile("cameraFile");

            if (audioFile != null && audioFile.Length > 0)
            {
                string audioExt = Path.GetExtension(audioFile.FileName);
                if (string.IsNullOrEmpty(audioExt)) audioExt = ".webm";
                string targetAudio = Path.Combine(tempDir, "audio", "recording" + audioExt);
                using var stream = File.Create(targetAudio);
                await audioFile.CopyToAsync(stream);
                logger.LogInformation("Saved recorded microphone audio: {Path} ({Bytes} bytes)", targetAudio, audioFile.Length);
            }

            if (cameraFile != null && cameraFile.Length > 0)
            {
                string camExt = Path.GetExtension(cameraFile.FileName);
                if (string.IsNullOrEmpty(camExt)) camExt = ".webm";
                string targetCam = Path.Combine(tempDir, "video", "webcam" + camExt);
                using var stream = File.Create(targetCam);
                await cameraFile.CopyToAsync(stream);
                logger.LogInformation("Saved recorded webcam video: {Path} ({Bytes} bytes)", targetCam, cameraFile.Length);
            }
        }
        else
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true), new JsonStringEnumConverter() }
            };
            session = await request.ReadFromJsonAsync<RecordingSession>(opts);
        }

        if (session == null)
        {
            return Results.BadRequest("Invalid session payload.");
        }

        logger.LogInformation("Processing export for session {SessionId}. Pages: {Pages}, Events: {Events}, AudioFile: {HasAudio}, CameraFile: {HasCam}",
            session.SessionId, session.Pages.Count, session.Events.Count, audioFile != null, cameraFile != null);

        // Auto-synthesize timeline events if user drew directly without clicking record
        if (session.Events.Count == 0 && session.Pages.Any(p => p.Strokes.Count > 0))
        {
            double simulatedTimeSec = 0.5;
            foreach (var page in session.Pages)
            {
                foreach (var stroke in page.Strokes)
                {
                    stroke.StartTime = TimeSpan.FromSeconds(simulatedTimeSec);
                    stroke.EndTime = TimeSpan.FromSeconds(simulatedTimeSec + 0.3);

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
        }

        if (session.Metadata.Duration < TimeSpan.FromSeconds(1))
        {
            session.Metadata.Duration = TimeSpan.FromSeconds(3);
        }

        string outputMp4 = Path.Combine(tempDir, $"WriteStudio_Lesson_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        var settings = new ExportSettings(
            OutputFilePath: outputMp4,
            Width: session.Metadata.CanvasWidth > 0 ? session.Metadata.CanvasWidth : 1920,
            Height: session.Metadata.CanvasHeight > 0 ? session.Metadata.CanvasHeight : 1080,
            FrameRate: session.Metadata.TargetFps > 0 ? session.Metadata.TargetFps : 30,
            VideoBitrateKbps: 4000,
            IncludeWebcam: cameraFile != null || session.Events.Any(e => e is CameraLayoutChangedTimelineEvent)
        );

        // Save session structure
        await storage.SaveProjectAsync(session, tempDir);

        logger.LogInformation("Executing SkiaSharp + FFmpeg rendering...");
        bool success = await rendering.RenderProjectAsync(session, tempDir, settings);

        if (success && File.Exists(outputMp4))
        {
            byte[] videoBytes = await File.ReadAllBytesAsync(outputMp4);
            logger.LogInformation("Export succeeded. Output size: {Bytes} bytes", videoBytes.Length);
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

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
Console.WriteLine("===================================================================");
Console.WriteLine("  WriteStudio Web Server Running!");
Console.WriteLine($"  Listening on: http://0.0.0.0:{port}");
Console.WriteLine("===================================================================");

app.Run($"http://0.0.0.0:{port}");
