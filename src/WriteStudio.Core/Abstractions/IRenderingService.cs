using WriteStudio.Core.Models;

namespace WriteStudio.Core.Abstractions;

public interface IRenderingService
{
    bool IsRendering { get; }

    event EventHandler<ExportProgressReport>? ProgressChanged;
    event EventHandler<Exception>? ErrorOccurred;

    Task<bool> RenderProjectAsync(
        RecordingSession session,
        string projectDirectory,
        ExportSettings settings,
        IProgress<ExportProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    Task CancelRenderingAsync();
}
