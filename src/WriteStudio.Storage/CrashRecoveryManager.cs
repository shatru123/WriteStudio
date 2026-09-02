using System.Text.Json;
using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.Storage;

public class CrashRecoveryManager : IRecoveryService
{
    private readonly ILogger<CrashRecoveryManager>? _logger;
    private readonly IProjectStorageService _projectStorage;
    private readonly string _recoveryRootDirectory;

    public CrashRecoveryManager(
        IProjectStorageService projectStorage,
        ILogger<CrashRecoveryManager>? logger = null)
    {
        _projectStorage = projectStorage ?? throw new ArgumentNullException(nameof(projectStorage));
        _logger = logger;
        _recoveryRootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WriteStudio",
            "Recovery"
        );

        if (!Directory.Exists(_recoveryRootDirectory))
        {
            Directory.CreateDirectory(_recoveryRootDirectory);
        }
    }

    public Task<IReadOnlyList<RecoverableSession>> CheckForRecoverableSessionsAsync()
    {
        var recoverable = new List<RecoverableSession>();

        try
        {
            if (!Directory.Exists(_recoveryRootDirectory))
                return Task.FromResult<IReadOnlyList<RecoverableSession>>(recoverable);

            var sessionDirs = Directory.GetDirectories(_recoveryRootDirectory);
            foreach (var dir in sessionDirs)
            {
                string projectJson = Path.Combine(dir, "project.json");
                string timelineJson = Path.Combine(dir, "timeline.json");

                if (File.Exists(projectJson) || File.Exists(timelineJson))
                {
                    var lastWrite = Directory.GetLastWriteTimeUtc(dir);
                    int eventCount = 0;
                    if (File.Exists(timelineJson))
                    {
                        try
                        {
                            string text = File.ReadAllText(timelineJson);
                            var events = JsonSerializer.Deserialize<List<TimelineEvent>>(text);
                            eventCount = events?.Count ?? 0;
                        }
                        catch { }
                    }

                    recoverable.Add(new RecoverableSession(
                        SessionDirectory: dir,
                        LastModifiedUtc: lastWrite,
                        EventCount: eventCount,
                        ApproximateDuration: TimeSpan.FromSeconds(eventCount * 0.5)
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking for recoverable sessions.");
        }

        return Task.FromResult<IReadOnlyList<RecoverableSession>>(recoverable);
    }

    public async Task<RecordingSession?> RecoverSessionAsync(string sessionDirectory)
    {
        try
        {
            _logger?.LogInformation("Attempting session recovery from {Path}", sessionDirectory);
            return await _projectStorage.LoadProjectAsync(sessionDirectory);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to recover session from {Path}", sessionDirectory);
            return null;
        }
    }

    public Task DiscardRecoverableSessionAsync(string sessionDirectory)
    {
        try
        {
            if (Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
                _logger?.LogInformation("Discarded recoverable session: {Path}", sessionDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error discarding recovery session {Path}", sessionDirectory);
        }

        return Task.CompletedTask;
    }

    public async Task SaveRecoverySnapshotAsync(RecordingSession session, string sessionDirectory)
    {
        try
        {
            await _projectStorage.SaveProjectAsync(session, sessionDirectory);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save recovery snapshot to {Path}", sessionDirectory);
        }
    }
}
