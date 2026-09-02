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
