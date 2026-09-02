using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WriteStudio.App.ViewModels;
using WriteStudio.Audio;
using WriteStudio.Camera;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Time;
using WriteStudio.Recording;
using WriteStudio.Rendering;
using WriteStudio.Slides;
using WriteStudio.Storage;
using WriteStudio.Whiteboard;
using WriteStudio.Whiteboard.UndoRedo;

#if WINDOWS
using WriteStudio.App.Views;
#endif

namespace WriteStudio.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

#if WINDOWS
        var app = new App(host);
        app.InitializeComponent();
        app.Run();
#else
        Console.WriteLine("WriteStudio Engine initialized successfully.");
        Console.WriteLine("Note: Full WPF UI launches on Windows desktop. Core services, audio, recording, storage, and video rendering are active.");
#endif
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();
                logging.SetMinimumLevel(LogLevel.Information);
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
