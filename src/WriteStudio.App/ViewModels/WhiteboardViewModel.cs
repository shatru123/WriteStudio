using System.Collections.ObjectModel;
using System.Windows.Input;
using WriteStudio.App.Mvvm;
using WriteStudio.Core.Abstractions;
using WriteStudio.Core.Models;

namespace WriteStudio.App.ViewModels;

public class WhiteboardViewModel : ObservableObject
{
    private readonly IWhiteboardService _whiteboardService;
    private readonly IUndoRedoManager _undoRedoManager;

    private StrokeToolType _activeTool = StrokeToolType.Pen;
    private ColorInfo _activeColor = ColorInfo.Black;
    private double _activeThickness = 3.0;
    private double _activeOpacity = 1.0;
    private BackgroundStyle _activeBackground = BackgroundStyle.White;
    private int _currentPageIndex = 0;
    private int _totalPages = 1;
    private string _pageIndicator = "Page 1 of 1";

    public StrokeToolType ActiveTool
    {
        get => _activeTool;
        set
        {
            if (SetProperty(ref _activeTool, value))
            {
                _whiteboardService.ActiveTool = value;
                OnPropertyChanged(nameof(IsPenActive));
                OnPropertyChanged(nameof(IsPencilActive));
                OnPropertyChanged(nameof(IsHighlighterActive));
                OnPropertyChanged(nameof(IsEraserActive));
                OnPropertyChanged(nameof(IsLineActive));
                OnPropertyChanged(nameof(IsRectangleActive));
                OnPropertyChanged(nameof(IsCircleActive));
                OnPropertyChanged(nameof(IsArrowActive));
                OnPropertyChanged(nameof(IsTextActive));
            }
        }
    }

    public bool IsPenActive => ActiveTool == StrokeToolType.Pen;
    public bool IsPencilActive => ActiveTool == StrokeToolType.Pencil;
    public bool IsHighlighterActive => ActiveTool == StrokeToolType.Highlighter;
    public bool IsEraserActive => ActiveTool == StrokeToolType.Eraser;
    public bool IsLineActive => ActiveTool == StrokeToolType.Line;
    public bool IsRectangleActive => ActiveTool == StrokeToolType.Rectangle;
    public bool IsCircleActive => ActiveTool == StrokeToolType.Circle;
    public bool IsArrowActive => ActiveTool == StrokeToolType.Arrow;
    public bool IsTextActive => ActiveTool == StrokeToolType.Text;

    public ColorInfo ActiveColor
    {
        get => _activeColor;
        set
        {
            if (SetProperty(ref _activeColor, value))
            {
                _whiteboardService.ActiveColor = value;
            }
        }
    }

    public double ActiveThickness
    {
        get => _activeThickness;
        set
        {
            if (SetProperty(ref _activeThickness, value))
            {
                _whiteboardService.ActiveThickness = value;
            }
        }
    }

    public double ActiveOpacity
    {
        get => _activeOpacity;
        set
        {
            if (SetProperty(ref _activeOpacity, value))
            {
                _whiteboardService.ActiveOpacity = value;
            }
        }
    }

    public BackgroundStyle ActiveBackground
    {
        get => _activeBackground;
        set
        {
            if (SetProperty(ref _activeBackground, value))
            {
                _whiteboardService.SetBackground(value);
            }
        }
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        private set
        {
            if (SetProperty(ref _currentPageIndex, value))
            {
                UpdatePageIndicator();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                UpdatePageIndicator();
            }
        }
    }

    public string PageIndicator
    {
        get => _pageIndicator;
        private set => SetProperty(ref _pageIndicator, value);
    }

    public bool CanUndo => _undoRedoManager.CanUndo;
    public bool CanRedo => _undoRedoManager.CanRedo;

    public ObservableCollection<ColorInfo> PaletteColors { get; } = new()
    {
        ColorInfo.Black,
        ColorInfo.White,
        ColorInfo.Red,
        ColorInfo.Blue,
        ColorInfo.Green,
        ColorInfo.Yellow,
        ColorInfo.Orange,
        ColorInfo.Purple,
        ColorInfo.Cyan
    };

    public ICommand SelectToolCommand { get; }
    public ICommand SelectColorCommand { get; }
    public ICommand SelectBackgroundCommand { get; }
    public ICommand AddPageCommand { get; }
    public ICommand DeletePageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand ClearPageCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand RedoCommand { get; }

    public WhiteboardViewModel(IWhiteboardService whiteboardService, IUndoRedoManager undoRedoManager)
    {
        _whiteboardService = whiteboardService ?? throw new ArgumentNullException(nameof(whiteboardService));
        _undoRedoManager = undoRedoManager ?? throw new ArgumentNullException(nameof(undoRedoManager));

        _undoRedoManager.StateChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
        };

        _whiteboardService.PageChanged += (s, newIdx) =>
        {
            CurrentPageIndex = newIdx;
            TotalPages = _whiteboardService.Pages.Count;
            ActiveBackground = _whiteboardService.CurrentPage.Background;
        };

        _whiteboardService.BackgroundChanged += (s, bg) =>
        {
            _activeBackground = bg;
            OnPropertyChanged(nameof(ActiveBackground));
        };

        SelectToolCommand = new RelayCommand(p =>
        {
            if (p is StrokeToolType tool) ActiveTool = tool;
            else if (p is string toolStr && Enum.TryParse<StrokeToolType>(toolStr, true, out var parsed)) ActiveTool = parsed;
        });

        SelectColorCommand = new RelayCommand(p =>
        {
            if (p is ColorInfo c) ActiveColor = c;
            else if (p is string hex) ActiveColor = ColorInfo.FromHex(hex);
        });

        SelectBackgroundCommand = new RelayCommand(p =>
        {
            if (p is BackgroundStyle bg) ActiveBackground = bg;
            else if (p is string bgStr && Enum.TryParse<BackgroundStyle>(bgStr, true, out var parsed)) ActiveBackground = parsed;
        });

        AddPageCommand = new RelayCommand(() =>
        {
            _whiteboardService.AddPage(ActiveBackground);
            TotalPages = _whiteboardService.Pages.Count;
            CurrentPageIndex = _whiteboardService.CurrentPageIndex;
        });

        DeletePageCommand = new RelayCommand(() =>
        {
            if (_whiteboardService.Pages.Count > 1)
            {
                _whiteboardService.RemovePage(_whiteboardService.CurrentPageIndex);
                TotalPages = _whiteboardService.Pages.Count;
                CurrentPageIndex = _whiteboardService.CurrentPageIndex;
            }
        }, () => _whiteboardService.Pages.Count > 1);

        NextPageCommand = new RelayCommand(() =>
        {
            if (CurrentPageIndex < TotalPages - 1)
            {
                _whiteboardService.SetActivePage(CurrentPageIndex + 1);
            }
        });

        PreviousPageCommand = new RelayCommand(() =>
        {
            if (CurrentPageIndex > 0)
            {
                _whiteboardService.SetActivePage(CurrentPageIndex - 1);
            }
        });

        ClearPageCommand = new RelayCommand(() => _whiteboardService.ClearCurrentPage());
        UndoCommand = new RelayCommand(() => _undoRedoManager.Undo(), () => CanUndo);
        RedoCommand = new RelayCommand(() => _undoRedoManager.Redo(), () => CanRedo);

        UpdatePageIndicator();
    }

    private void UpdatePageIndicator()
    {
        PageIndicator = $"Page {CurrentPageIndex + 1} of {TotalPages}";
    }
}
