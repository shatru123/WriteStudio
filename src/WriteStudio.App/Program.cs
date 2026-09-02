using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WriteStudio.App.ViewModels;
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
using WriteStudio.Whiteboard.Geometry;
using WriteStudio.Whiteboard.UndoRedo;

#if WINDOWS
using WriteStudio.App.Views;
#endif

namespace WriteStudio.App;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

#if WINDOWS
        var app = new App(host);
        app.InitializeComponent();
        app.Run();
#else
        Console.WriteLine("===================================================================");
        Console.WriteLine("  WriteStudio — .NET Teaching & Handwritten Video Studio");
        Console.WriteLine("===================================================================");
        Console.WriteLine();
        Console.WriteLine("Initializing WriteStudio Core & Dependency Injection Services...");

        await host.StartAsync();

        var sp = host.Services;
        var clock = sp.GetRequiredService<IRecordingClock>();
        var wb = sp.GetRequiredService<IWhiteboardService>();
        var audio = sp.GetRequiredService<IAudioCaptureService>();
        var camera = sp.GetRequiredService<ICameraService>();
        var recording = sp.GetRequiredService<IRecordingService>();
        var storage = sp.GetRequiredService<IProjectStorageService>();
        var rendering = sp.GetRequiredService<IRenderingService>();
        var ffmpeg = sp.GetRequiredService<IFFmpegService>();

        // 1. Probe FFmpeg
        Console.WriteLine("\n[1/5] Probing Video Encoder (FFmpeg)...");
        bool hasFfmpeg = await ffmpeg.ProbeFFmpegAsync();
        Console.WriteLine(hasFfmpeg 
            ? $"  ✓ FFmpeg discovered at: {ffmpeg.FFmpegPath}" 
            : "  ✗ FFmpeg not found on PATH. (Video export will require FFmpeg)");

        // 2. Discover Devices
        Console.WriteLine("\n[2/5] Enumerating Hardware Devices...");
        var audioDevices = await audio.EnumerateDevicesAsync();
        foreach (var dev in audioDevices)
        {
            Console.WriteLine($"  ✓ Audio: {dev.Name} (USB: {dev.IsUsbDevice}, Default: {dev.IsDefault})");
        }

        var camDevices = await camera.EnumerateDevicesAsync();
        foreach (var cam in camDevices)
        {
            Console.WriteLine($"  ✓ Camera: {cam.Name} (Default: {cam.IsDefault})");
        }

        // 3. Start Studio Recording Session
        string projectDir = Path.Combine(Directory.GetCurrentDirectory(), "exports", $"DemoLesson_{DateTime.Now:yyyyMMdd_HHmmss}");
        Console.WriteLine($"\n[3/5] Starting Synchronized Studio Recording in: {projectDir}");

        wb.SetBackground(BackgroundStyle.Blackboard);
        camera.SetPreset(CameraPositionPreset.BottomRight);

        await recording.StartRecordingAsync(projectDir);
        Console.WriteLine("  ● RECORDING ACTIVE (Clock running, Audio stream capturing, Webcam PiP enabled)...");

        // 4. Simulate Live Handwritten Lesson
        Console.WriteLine("\n[4/5] Teaching & Drawing on Virtual Whiteboard Canvas...");

        // Title text
        wb.ActiveTool = StrokeToolType.Text;
        wb.ActiveColor = ColorInfo.Yellow;
        var titleStroke = wb.StartStroke(120, 100, 1.0f, clock.ElapsedTime);
        titleStroke.TextContent = "WriteStudio: Solving 2x² + 5x - 3 = 0";
        titleStroke.FontSize = 36;
        wb.CompleteStroke(titleStroke.Id);
        Console.WriteLine("  ✍ Drew Title: 'WriteStudio: Solving 2x² + 5x - 3 = 0'");

        await Task.Delay(400);

        // Draw Formula Step 1
        wb.ActiveTool = StrokeToolType.Pen;
        wb.ActiveColor = ColorInfo.White;
        wb.ActiveThickness = 4.0;
        DrawSimulatedStroke(wb, clock, 120, 200, 450, 200);
        Console.WriteLine("  ✍ Wrote Quadratic Formula equation: 2x² + 5x - 3 = 0");

        await Task.Delay(400);

        // Highlight with semi-transparent marker
        wb.ActiveTool = StrokeToolType.Highlighter;
        wb.ActiveColor = ColorInfo.HighlighterYellow;
        wb.ActiveThickness = 24.0;
        DrawSimulatedStroke(wb, clock, 110, 200, 460, 200);
        Console.WriteLine("  ✨ Highlighted key formula with semi-transparent marker");

        await Task.Delay(400);

        // Step 2: Discriminant calculation
        wb.ActiveTool = StrokeToolType.Pen;
        wb.ActiveColor = ColorInfo.Cyan;
        wb.ActiveThickness = 3.5;
        DrawSimulatedStroke(wb, clock, 120, 300, 600, 300);
        Console.WriteLine("  ✍ Step 2: D = b² - 4ac = 25 - 4(2)(-3) = 49");

        await Task.Delay(400);

        // Switch camera PiP position during lecture
        camera.SetPreset(CameraPositionPreset.TopRight);
        Console.WriteLine("  📹 Presenter repositioned camera PiP to Top-Right");

        await Task.Delay(400);

        // Step 3: Roots
        wb.ActiveColor = ColorInfo.Green;
        DrawSimulatedStroke(wb, clock, 120, 400, 520, 400);
        Console.WriteLine("  ✍ Step 3: x = (-5 ± 7)/4  =>  x = 1/2 or x = -3");

        // Solution Box (Rectangle)
        wb.ActiveTool = StrokeToolType.Rectangle;
        wb.ActiveColor = ColorInfo.Orange;
        wb.ActiveThickness = 3.0;
        var boxPoints = StrokeGeometryHelper.GenerateRectanglePoints(100, 360, 560, 440, clock.ElapsedTime);
        var boxStroke = new DrawingStroke
        {
            PageIndex = 0,
            ToolType = StrokeToolType.Rectangle,
            Color = ColorInfo.Orange,
            Thickness = 3.0,
            Points = boxPoints,
            StartTime = clock.ElapsedTime,
            EndTime = clock.ElapsedTime
        };
        wb.AddStroke(boxStroke);
        Console.WriteLine("  📐 Drew solution highlight bounding box");

        // Arrow
        var arrowPoints = StrokeGeometryHelper.GenerateArrowPoints(600, 400, 565, 400, clock.ElapsedTime);
        var arrowStroke = new DrawingStroke
        {
            PageIndex = 0,
            ToolType = StrokeToolType.Arrow,
            Color = ColorInfo.Red,
            Thickness = 4.0,
            Points = arrowPoints,
            StartTime = clock.ElapsedTime,
            EndTime = clock.ElapsedTime
        };
        wb.AddStroke(arrowStroke);
        Console.WriteLine("  ➔ Drew arrow indicator");

        await Task.Delay(500);

        // Test Pause/Resume
        Console.WriteLine("  ❚❚ Presenter paused recording...");
        await recording.PauseRecordingAsync();
        await Task.Delay(600);
        Console.WriteLine("  ▶ Presenter resumed recording...");
        await recording.ResumeRecordingAsync();

        await Task.Delay(500);

        // Stop Recording
        var session = await recording.StopRecordingAsync();
        await storage.SaveProjectAsync(session, projectDir);
        Console.WriteLine($"  ◼ RECORDING STOPPED. Total duration: {session.Metadata.Duration.TotalSeconds:F2}s, {session.Events.Count} timeline events.");

        // 5. Render Video via Skia + FFmpeg
        if (hasFfmpeg)
        {
            string outputMp4 = Path.Combine(projectDir, "DemoLesson_1080p.mp4");
            Console.WriteLine($"\n[5/5] Rendering Final MP4 Video (1080p @ 30 FPS) with FFmpeg...");
            Console.WriteLine($"  Output Path: {outputMp4}");

            var progress = new Progress<ExportProgressReport>(report =>
            {
                Console.Write($"\r  ⏳ {report.StatusMessage} [{report.Percentage:F1}%] ({report.CurrentFps:F1} FPS)   ");
            });

            var settings = new ExportSettings(
                OutputFilePath: outputMp4,
                Width: 1920,
                Height: 1080,
                FrameRate: 30,
                VideoBitrateKbps: 4000
            );

            bool success = await rendering.RenderProjectAsync(session, projectDir, settings, progress);
            Console.WriteLine();
            if (success && File.Exists(outputMp4))
            {
                var fileInfo = new FileInfo(outputMp4);
                Console.WriteLine($"\n🎉 SUCCESS! Exported master video: {outputMp4} ({fileInfo.Length / 1024} KB)");
            }
            else
            {
                Console.WriteLine("\n⚠️ Rendering did not produce output file.");
            }
        }
        else
        {
            Console.WriteLine("\n[5/5] Skipped FFmpeg render step since FFmpeg is not installed on PATH.");
        }

        Console.WriteLine("\n===================================================================");
        Console.WriteLine("  WriteStudio verification complete!");
        Console.WriteLine("===================================================================");

        await host.StopAsync();
#endif
    }

    private static void DrawSimulatedStroke(IWhiteboardService wb, IRecordingClock clock, double x1, double y1, double x2, double y2)
    {
        var stroke = wb.StartStroke(x1, y1, 0.5f, clock.ElapsedTime);
        int steps = 10;
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = x1 + (x2 - x1) * t;
            double y = y1 + (y2 - y1) * t + Math.Sin(t * Math.PI) * 4.0; // Subtle handwritten curve
            float pressure = (float)(0.4 + 0.5 * Math.Sin(t * Math.PI));
            wb.AppendPoint(stroke.Id, x, y, pressure, clock.ElapsedTime);
        }
        wb.CompleteStroke(stroke.Id);
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Warning); // Quiet logging for clean studio output
            })
            .ConfigureServices((context, services) =>
            {
                // Core Timeline & State Services
                services.AddSingleton<IRecordingClock, RecordingClock>();
                services.AddSingleton<IUndoRedoManager, UndoRedoManager>();
                services.AddSingleton<IWhiteboardService, WhiteboardService>();
                services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
                services.AddSingleton<ICameraService, CameraService>();
                services.AddSingleton<ISlideService, SlideService>();
                services.AddSingleton<IRecordingService, RecordingService>();
                services.AddSingleton<IFFmpegService, FFmpegService>();
                services.AddSingleton<IRenderingService, RenderingService>();
                services.AddSingleton<IProjectStorageService, ProjectStorageService>();
                services.AddSingleton<IRecoveryService, CrashRecoveryManager>();

                // MVVM ViewModels
                services.AddSingleton<WhiteboardViewModel>();
                services.AddSingleton<SlidesViewModel>();
                services.AddSingleton<AudioViewModel>();
                services.AddSingleton<CameraViewModel>();
                services.AddSingleton<RecordingViewModel>();
                services.AddSingleton<ExportViewModel>();
                services.AddSingleton<MainViewModel>();

#if WINDOWS
                // UI Views
                services.AddSingleton<MainWindow>();
#endif
            });
}
