using WriteStudio.App.ViewModels;
using WriteStudio.Core.Abstractions;

#if WINDOWS
using System.Windows;
using Microsoft.Win32;
#endif

namespace WriteStudio.App.Views;

#if WINDOWS
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly IWhiteboardService _whiteboardService;

    public IWhiteboardService WhiteboardService => _whiteboardService;

    public MainWindow(MainViewModel viewModel, IWhiteboardService whiteboardService)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _whiteboardService = whiteboardService ?? throw new ArgumentNullException(nameof(whiteboardService));
        DataContext = _viewModel;

        Loaded += async (s, e) => await _viewModel.InitializeAsync();
    }

    private async void OnLoadSlidesClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Reference Slides or Document",
            Filter = "Presenter Documents (*.pdf;*.png;*.jpg;*.jpeg)|*.pdf;*.png;*.jpg;*.jpeg|PDF Documents (*.pdf)|*.pdf|Images (*.png;*.jpg)|*.png;*.jpg|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.Slides.OpenDocumentAsync(dialog.FileName);
        }
    }

    private void OnMenuExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMenuAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "WriteStudio — .NET Teaching & Handwritten Video Recording Studio\n\n" +
            "A professional studio for educators, trainers, and engineers to record handwritten instructional videos with private presenter reference material.\n\n" +
            "Version: 1.0.0\n" +
            "Built with .NET 10, SkiaSharp & FFmpeg.",
            "About WriteStudio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
#endif
