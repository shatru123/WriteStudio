using WriteStudio.Core.Models;

namespace WriteStudio.Core.Abstractions;

public interface IProjectStorageService
{
    string DefaultProjectsDirectory { get; }

    Task<RecordingSession> CreateNewProjectAsync(string title, string? directory = null);
    Task SaveProjectAsync(RecordingSession session, string projectDirectory, CancellationToken cancellationToken = default);
    Task<RecordingSession> LoadProjectAsync(string projectDirectoryOrFile, CancellationToken cancellationToken = default);
    Task ExportProjectPackageAsync(string projectDirectory, string outputZipPath, CancellationToken cancellationToken = default);
    Task<RecordingSession> ImportProjectPackageAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken = default);
}

public record RecoverableSession(string SessionDirectory, DateTime LastModifiedUtc, int EventCount, TimeSpan ApproximateDuration);

public interface IRecoveryService
{
    Task<IReadOnlyList<RecoverableSession>> CheckForRecoverableSessionsAsync();
    Task<RecordingSession?> RecoverSessionAsync(string sessionDirectory);
    Task DiscardRecoverableSessionAsync(string sessionDirectory);
    Task SaveRecoverySnapshotAsync(RecordingSession session, string sessionDirectory);
}
