using System.Collections.ObjectModel;
using System.Windows.Input;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;

namespace WriteStudio.App.ViewModels;

public class SlidesViewModel : ObservableObject
{
    private readonly ISlideService _slideService;
    private bool _hasDocument;
    private string _documentTitle = "No Reference Document Loaded";
    private int _currentSlideIndex = -1;
    private int _totalSlides = 0;
    private double _zoomLevel = 1.0;
    private string _slideIndicator = "0 / 0";
    private byte[]? _currentSlideImage;

    public bool HasDocument
    {
        get => _hasDocument;
        private set => SetProperty(ref _hasDocument, value);
    }

    public string DocumentTitle
    {
        get => _documentTitle;
        private set => SetProperty(ref _documentTitle, value);
    }

    public int CurrentSlideIndex
    {
        get => _currentSlideIndex;
        private set
        {
            if (SetProperty(ref _currentSlideIndex, value))
            {
                UpdateSlideIndicator();
                _ = LoadCurrentSlideImageAsync();
            }
        }
    }

    public int TotalSlides
    {
        get => _totalSlides;
        private set
        {
            if (SetProperty(ref _totalSlides, value))
            {
                UpdateSlideIndicator();
            }
        }
    }

    public double ZoomLevel
    {
        get => _zoomLevel;
        set
        {
            if (SetProperty(ref _zoomLevel, value))
            {
                _slideService.SetZoom(value);
            }
        }
    }

    public string SlideIndicator
    {
        get => _slideIndicator;
        private set => SetProperty(ref _slideIndicator, value);
    }

    public byte[]? CurrentSlideImage
    {
        get => _currentSlideImage;
        private set => SetProperty(ref _currentSlideImage, value);
    }

    public ObservableCollection<SlidePageInfo> Thumbnails { get; } = new();

    public ICommand NextSlideCommand { get; }
    public ICommand PreviousSlideCommand { get; }
    public ICommand ZoomInCommand { get; }
    public ICommand ZoomOutCommand { get; }
    public ICommand ResetZoomCommand { get; }
    public ICommand CloseDocumentCommand { get; }
    public ICommand SelectSlideCommand { get; }

    public SlidesViewModel(ISlideService slideService)
    {
        _slideService = slideService ?? throw new ArgumentNullException(nameof(slideService));

        _slideService.SlideChanged += (s, idx) => CurrentSlideIndex = idx;
        _slideService.ZoomChanged += (s, z) => _zoomLevel = z;
        _slideService.DocumentLoaded += async (s, e) =>
        {
            HasDocument = _slideService.HasDocument;
            TotalSlides = _slideService.TotalSlides;
            CurrentSlideIndex = _slideService.CurrentSlideIndex;
            DocumentTitle = Path.GetFileName(_slideService.DocumentPath ?? "Reference Document");

            Thumbnails.Clear();
            var thumbs = await _slideService.GetSlideThumbnailsAsync();
            foreach (var t in thumbs) Thumbnails.Add(t);

            await LoadCurrentSlideImageAsync();
        };

        _slideService.DocumentClosed += (s, e) =>
        {
            HasDocument = false;
            TotalSlides = 0;
            CurrentSlideIndex = -1;
            DocumentTitle = "No Reference Document Loaded";
            CurrentSlideImage = null;
            Thumbnails.Clear();
        };

        NextSlideCommand = new RelayCommand(() => _slideService.NextSlide(), () => HasDocument && CurrentSlideIndex < TotalSlides - 1);
        PreviousSlideCommand = new RelayCommand(() => _slideService.PreviousSlide(), () => HasDocument && CurrentSlideIndex > 0);
        ZoomInCommand = new RelayCommand(() => ZoomLevel = Math.Min(4.0, ZoomLevel + 0.25), () => HasDocument);
        ZoomOutCommand = new RelayCommand(() => ZoomLevel = Math.Max(0.25, ZoomLevel - 0.25), () => HasDocument);
        ResetZoomCommand = new RelayCommand(() => ZoomLevel = 1.0, () => HasDocument);
        CloseDocumentCommand = new RelayCommand(() => _slideService.CloseDocument(), () => HasDocument);

        SelectSlideCommand = new RelayCommand(p =>
        {
            if (p is int idx) _slideService.GoToSlide(idx);
            else if (p is SlidePageInfo info) _slideService.GoToSlide(info.PageNumber - 1);
        });

        UpdateSlideIndicator();
    }

    public async Task<bool> OpenDocumentAsync(string filePath)
    {
        return await _slideService.LoadDocumentAsync(filePath);
    }

    private async Task LoadCurrentSlideImageAsync()
    {
        if (CurrentSlideIndex >= 0)
        {
            CurrentSlideImage = await _slideService.RenderSlideImageAsync(CurrentSlideIndex, 1280, 720);
        }
        else
        {
            CurrentSlideImage = null;
        }
    }

    private void UpdateSlideIndicator()
    {
        SlideIndicator = TotalSlides > 0 ? $"{CurrentSlideIndex + 1} / {TotalSlides}" : "0 / 0";
    }
}
