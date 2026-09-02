namespace WriteStudio.Core.Abstractions;

public record SlidePageInfo(int PageNumber, string Title, byte[]? ThumbnailBytes = null);

public interface ISlideService
{
    bool HasDocument { get; }
    string? DocumentPath { get; }
    int CurrentSlideIndex { get; }
    int TotalSlides { get; }
    double ZoomLevel { get; set; }

    event EventHandler<int>? SlideChanged;
    event EventHandler<double>? ZoomChanged;
    event EventHandler? DocumentLoaded;
    event EventHandler? DocumentClosed;

    Task<bool> LoadDocumentAsync(string filePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SlidePageInfo>> GetSlideThumbnailsAsync(CancellationToken cancellationToken = default);
    Task<byte[]?> RenderSlideImageAsync(int slideIndex, int targetWidth, int targetHeight, CancellationToken cancellationToken = default);
    void NextSlide();
    void PreviousSlide();
    void GoToSlide(int slideIndex);
    void SetZoom(double zoom);
    void CloseDocument();
}
