using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.Storage;

public class ProjectStorageService : IProjectStorageService
{
    private readonly ILogger<ProjectStorageService>? _logger;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string DefaultProjectsDirectory { get; }

    public ProjectStorageService(ILogger<ProjectStorageService>? logger = null)
    {
        _logger = logger;
        DefaultProjectsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WriteStudio",
            "Projects"
        );
    }

    public Task<RecordingSession> CreateNewProjectAsync(string title, string? directory = null)
    {
        string targetDir = directory ?? Path.Combine(DefaultProjectsDirectory, $"{title}_{DateTime.Now:yyyyMMdd_HHmmss}");
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var session = new RecordingSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Metadata = new ProjectMetadata
            {
                ProjectId = Guid.NewGuid().ToString("N"),
                Title = title,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow
            },
            Pages = new List<WhiteboardPage> { new() { Index = 0, Title = "Page 1", Background = BackgroundStyle.White } }
        };

        return Task.FromResult(session);
    }

    public async Task SaveProjectAsync(RecordingSession session, string projectDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        if (!Directory.Exists(projectDirectory))
        {
            Directory.CreateDirectory(projectDirectory);
        }

        string strokesDir = Path.Combine(projectDirectory, "strokes");
        Directory.CreateDirectory(strokesDir);

        session.Metadata.ModifiedAt = DateTime.UtcNow;
        session.Metadata.TotalPages = session.Pages.Count;

        // 1. Write project.json
        string projectJsonPath = Path.Combine(projectDirectory, "project.json");
        string tempProjectJsonPath = projectJsonPath + ".tmp";
        string projectJson = JsonSerializer.Serialize(session.Metadata, JsonOpts);
        await File.WriteAllTextAsync(tempProjectJsonPath, projectJson, cancellationToken);
        File.Move(tempProjectJsonPath, projectJsonPath, overwrite: true);

        // 2. Write timeline.json
        string timelineJsonPath = Path.Combine(projectDirectory, "timeline.json");
        string tempTimelineJsonPath = timelineJsonPath + ".tmp";
        string timelineJson = JsonSerializer.Serialize(session.Events, JsonOpts);
        await File.WriteAllTextAsync(tempTimelineJsonPath, timelineJson, cancellationToken);
        File.Move(tempTimelineJsonPath, timelineJsonPath, overwrite: true);

        // 3. Write strokes per page
        for (int i = 0; i < session.Pages.Count; i++)
        {
            var page = session.Pages[i];
            string pageJsonPath = Path.Combine(strokesDir, $"page_{page.Index}.json");
            string tempPageJsonPath = pageJsonPath + ".tmp";
            string pageJson = JsonSerializer.Serialize(page, JsonOpts);
            await File.WriteAllTextAsync(tempPageJsonPath, pageJson, cancellationToken);
            File.Move(tempPageJsonPath, pageJsonPath, overwrite: true);
        }

        _logger?.LogInformation("Project saved successfully to {Path}", projectDirectory);
    }

    public async Task<RecordingSession> LoadProjectAsync(string projectDirectoryOrFile, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectoryOrFile);

        string workingDir = projectDirectoryOrFile;

        // Handle .wstudio zip file bundle
        if (File.Exists(projectDirectoryOrFile) && (projectDirectoryOrFile.EndsWith(".wstudio") || projectDirectoryOrFile.EndsWith(".zip")))
        {
            string extractTemp = Path.Combine(Path.GetTempPath(), "WriteStudio_Open_" + Guid.NewGuid().ToString("N"));
            ZipFile.ExtractToDirectory(projectDirectoryOrFile, extractTemp, overwriteFiles: true);
            workingDir = extractTemp;
        }

        if (!Directory.Exists(workingDir))
        {
            throw new DirectoryNotFoundException($"Project directory not found: {workingDir}");
        }

        string projectJsonPath = Path.Combine(workingDir, "project.json");
        if (!File.Exists(projectJsonPath))
        {
            throw new FileNotFoundException($"Invalid WriteStudio project: missing project.json at {projectJsonPath}");
        }

        string projectJson = await File.ReadAllTextAsync(projectJsonPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<ProjectMetadata>(projectJson, JsonOpts) 
            ?? new ProjectMetadata();

        var session = new RecordingSession
        {
            Metadata = metadata
        };

        // Load timeline events
        string timelineJsonPath = Path.Combine(workingDir, "timeline.json");
        if (File.Exists(timelineJsonPath))
        {
            string timelineJson = await File.ReadAllTextAsync(timelineJsonPath, cancellationToken);
            var events = JsonSerializer.Deserialize<List<TimelineEvent>>(timelineJson, JsonOpts);
            if (events != null)
            {
                session.Events = events;
            }
        }

        // Load pages and strokes
        string strokesDir = Path.Combine(workingDir, "strokes");
        session.Pages.Clear();

        if (Directory.Exists(strokesDir))
        {
            var pageFiles = Directory.GetFiles(strokesDir, "page_*.json").OrderBy(f => f);
            foreach (var pageFile in pageFiles)
            {
                string pageJson = await File.ReadAllTextAsync(pageFile, cancellationToken);
                var page = JsonSerializer.Deserialize<WhiteboardPage>(pageJson, JsonOpts);
                if (page != null)
                {
                    session.Pages.Add(page);
                }
            }
        }

        if (session.Pages.Count == 0)
        {
            session.Pages.Add(new WhiteboardPage { Index = 0, Title = "Page 1", Background = BackgroundStyle.White });
        }

        _logger?.LogInformation("Project loaded successfully from {Path} ({Pages} pages, {Events} events)", workingDir, session.Pages.Count, session.Events.Count);
        return session;
    }

    public Task ExportProjectPackageAsync(string projectDirectory, string outputZipPath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(projectDirectory))
            throw new DirectoryNotFoundException($"Project directory not found: {projectDirectory}");

        string? outDir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        if (File.Exists(outputZipPath))
        {
            File.Delete(outputZipPath);
        }

        ZipFile.CreateFromDirectory(projectDirectory, outputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        _logger?.LogInformation("Project package created: {Path}", outputZipPath);
        return Task.CompletedTask;
    }

    public async Task<RecordingSession> ImportProjectPackageAsync(string zipPath, string targetDirectory, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException($"Package zip not found: {zipPath}");

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        ZipFile.ExtractToDirectory(zipPath, targetDirectory, overwriteFiles: true);
        return await LoadProjectAsync(targetDirectory, cancellationToken);
    }
}
