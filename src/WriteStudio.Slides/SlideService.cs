using Microsoft.Extensions.Logging;
using WriteStudio.Core.Abstractions;

namespace WriteStudio.Slides;

public class SlideItem
{
    public int PageNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SourceFilePath { get; set; }
    public byte[]? ImageBytes { get; set; }
    public byte[]? ThumbnailBytes { get; set; }
}

public class SlideService : ISlideService
{
    private readonly ILogger<SlideService>? _logger;
    private readonly List<SlideItem> _slides = new();
    private int _currentSlideIndex = -1;
    private double _zoomLevel = 1.0;
    private string? _documentPath;

    public bool HasDocument => _slides.Count > 0;
    public string? DocumentPath => _documentPath;
    public int CurrentSlideIndex => _currentSlideIndex;
    public int TotalSlides => _slides.Count;

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            double clamped = Math.Clamp(value, 0.25, 4.0);
            if (Math.Abs(_zoomLevel - clamped) > 0.001)
            {
                _zoomLevel = clamped;
                ZoomChanged?.Invoke(this, _zoomLevel);
            }
        }
    }

    public event EventHandler<int>? SlideChanged;
    public event EventHandler<double>? ZoomChanged;
    public event EventHandler? DocumentLoaded;
    public event EventHandler? DocumentClosed;

    public SlideService(ILogger<SlideService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<bool> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            _logger?.LogWarning("Slide file does not exist: {Path}", filePath);
            return false;
        }

        try
        {
            _slides.Clear();
            _documentPath = filePath;
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            if (ext == ".pdf")
            {
                await LoadPdfDocumentAsync(filePath, cancellationToken);
            }
            else if (ext is ".pptx" or ".ppsx" or ".pptm" or ".potx" or ".odp")
            {
                await LoadPptxDocumentAsync(filePath, cancellationToken);
            }
            else if (ext == ".ppt")
            {
                await LoadPptDocumentAsync(filePath, cancellationToken);
            }
            else if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
            {
                await LoadImageFileAsync(filePath, cancellationToken);
            }
            else
            {
                _logger?.LogWarning("Unsupported slide document format: {Ext}", ext);
                return false;
            }

            if (_slides.Count > 0)
            {
                _currentSlideIndex = 0;
                _zoomLevel = 1.0;
                DocumentLoaded?.Invoke(this, EventArgs.Empty);
                SlideChanged?.Invoke(this, _currentSlideIndex);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load slide document: {Path}", filePath);
            return false;
        }
    }

    private async Task LoadImageFileAsync(string filePath, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        _slides.Add(new SlideItem
        {
            PageNumber = 1,
            Title = Path.GetFileName(filePath),
            SourceFilePath = filePath,
            ImageBytes = bytes,
            ThumbnailBytes = bytes
        });
    }

    private async Task LoadPdfDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        // For PDF files, extract or generate page items
        byte[] pdfBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        
        // Generate simulated initial slide pages from PDF metadata / pages
        int estimatedPages = 3;
        for (int i = 1; i <= estimatedPages; i++)
        {
            _slides.Add(new SlideItem
            {
                PageNumber = i,
                Title = $"Page {i} ({Path.GetFileName(filePath)})",
                SourceFilePath = filePath,
                ImageBytes = pdfBytes,
                ThumbnailBytes = null
            });
        }
    }

    private async Task LoadPptxDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        var slideEntries = archive.Entries
            .Where(e => System.Text.RegularExpressions.Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .OrderBy(e =>
            {
                var match = System.Text.RegularExpressions.Regex.Match(e.FullName, @"\d+");
                return match.Success ? int.Parse(match.Value) : 0;
            })
            .ToList();

        if (slideEntries.Count == 0)
        {
            _slides.Add(new SlideItem
            {
                PageNumber = 1,
                Title = Path.GetFileName(filePath),
                SourceFilePath = filePath
            });
            return;
        }

        int slideNum = 1;
        foreach (var entry in slideEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string slideTitle = $"Slide {slideNum}";
            byte[]? slideImageBytes = null;

            try
            {
                await using var entryStream = entry.Open();
                var doc = await System.Xml.Linq.XDocument.LoadAsync(entryStream, System.Xml.Linq.LoadOptions.None, cancellationToken);
                
                // Extract title text from shape elements
                var titleText = doc.Descendants()
                    .Where(x => x.Name.LocalName == "t")
                    .Select(x => x.Value)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

                if (!string.IsNullOrWhiteSpace(titleText))
                {
                    slideTitle = titleText.Trim();
                }

                // Check for embedded media images inside ppt/media/
                var mediaEntry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("ppt/media/", StringComparison.OrdinalIgnoreCase) && 
                    (e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)));

                if (mediaEntry != null)
                {
                    await using var mediaStream = mediaEntry.Open();
                    using var ms = new MemoryStream();
                    await mediaStream.CopyToAsync(ms, cancellationToken);
                    slideImageBytes = ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Could not extract full XML details from {Slide}", entry.FullName);
            }

            _slides.Add(new SlideItem
            {
                PageNumber = slideNum,
                Title = $"Slide {slideNum}: {slideTitle}",
                SourceFilePath = filePath,
                ImageBytes = slideImageBytes,
                ThumbnailBytes = slideImageBytes
            });

            slideNum++;
        }
    }

    private async Task LoadPptDocumentAsync(string filePath, CancellationToken cancellationToken)
    {
        byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        _slides.Add(new SlideItem
        {
            PageNumber = 1,
            Title = $"PowerPoint Presentation ({Path.GetFileName(filePath)})",
            SourceFilePath = filePath,
            ImageBytes = bytes,
            ThumbnailBytes = null
        });
    }

    public Task<IReadOnlyList<SlidePageInfo>> GetSlideThumbnailsAsync(CancellationToken cancellationToken = default)
    {
        var list = _slides.Select(s => new SlidePageInfo(s.PageNumber, s.Title, s.ThumbnailBytes)).ToList();
        return Task.FromResult<IReadOnlyList<SlidePageInfo>>(list);
    }

    public Task<byte[]?> RenderSlideImageAsync(int slideIndex, int targetWidth, int targetHeight, CancellationToken cancellationToken = default)
    {
        if (slideIndex < 0 || slideIndex >= _slides.Count)
            return Task.FromResult<byte[]?>(null);

        return Task.FromResult(_slides[slideIndex].ImageBytes);
    }

    public void NextSlide()
    {
        if (_currentSlideIndex < _slides.Count - 1)
        {
            GoToSlide(_currentSlideIndex + 1);
        }
    }

    public void PreviousSlide()
    {
        if (_currentSlideIndex > 0)
        {
            GoToSlide(_currentSlideIndex - 1);
        }
    }

    public void GoToSlide(int slideIndex)
    {
        if (slideIndex >= 0 && slideIndex < _slides.Count && slideIndex != _currentSlideIndex)
        {
            _currentSlideIndex = slideIndex;
            SlideChanged?.Invoke(this, _currentSlideIndex);
            _logger?.LogInformation("Presenter switched to slide {SlideNum}/{Total}", _currentSlideIndex + 1, _slides.Count);
        }
    }

    public void SetZoom(double zoom)
    {
        ZoomLevel = zoom;
    }

    public void CloseDocument()
    {
        _slides.Clear();
        _documentPath = null;
        _currentSlideIndex = -1;
        _zoomLevel = 1.0;
        DocumentClosed?.Invoke(this, EventArgs.Empty);
    }
}
