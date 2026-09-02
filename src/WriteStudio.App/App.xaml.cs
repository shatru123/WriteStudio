using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WriteStudio.App.ViewModels;
using WriteStudio.App.Views;

#if WINDOWS
using System.Windows;
#endif

namespace WriteStudio.App;

#if WINDOWS
public partial class App : Application
{
    private readonly IHost _host;

    public App(IHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
#endif
