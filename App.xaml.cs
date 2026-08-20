using Bannerlord.XmlPreview.Services;
using Bannerlord.XmlPreview.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Preview.Bannerlord;
using System.Text;
using System.Windows.Threading;

namespace Bannerlord.XmlPreview;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var services = new ServiceCollection();
            ConfigureServices(services);

            _serviceProvider = services.BuildServiceProvider();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            var startupOptions = ParseStartupOptions(e.Args);
            if (mainWindow.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ApplyStartupOptions(startupOptions.BannerlordInstallRoot, startupOptions.PrefabPath);
            }

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            ShowFatalError("Startup failure", ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IAssetCatalog, BannerlordAssetCatalog>();
        services.AddSingleton<IBrushMetricsProvider, BannerlordBrushMetricsProvider>();
        services.AddSingleton<IBrushOverlayFeatureHandler, BannerlordBrushOverlayFeatureHandler>();
        services.AddSingleton<IPrefabReferenceResolver, BannerlordPrefabReferenceResolver>();
        services.AddSingleton<IWidgetReferenceResolver, WidgetReferenceResolver>();
        services.AddSingleton<IWidgetNodeFactory, BannerlordWidgetNodeFactory>();
        services.AddSingleton<IPreviewDataSourceProvider, BannerlordPreviewDataSourceProvider>();
        services.AddSingleton<IRootLayoutHints, BannerlordRootLayoutHints>();
        services.AddSingleton<IWidgetTextSourceProvider, BannerlordWidgetTextSourceProvider>();
        services.AddSingleton<IWidgetDebugMetadataProvider, BannerlordWidgetDebugMetadataProvider>();
        services.AddSingleton<IPrefabLoader, PrefabLoader>();
        services.AddSingleton<IPrefabRenderer, PrefabRenderer>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IFileWatcherService, FileWatcherService>();
        
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private static StartupOptions ParseStartupOptions(IReadOnlyList<string> args)
    {
        string? prefabPath = null;
        string? bannerlordInstallRoot = null;

        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (string.Equals(argument, "--prefab", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Count)
            {
                prefabPath = args[++index];
                continue;
            }

            if ((string.Equals(argument, "--bannerlord-root", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(argument, "--bannerlord-install", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < args.Count)
            {
                bannerlordInstallRoot = args[++index];
                continue;
            }

            if (!argument.StartsWith("-", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(prefabPath))
            {
                prefabPath = argument;
            }
        }

        return new StartupOptions(prefabPath, bannerlordInstallRoot);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError("Unhandled UI exception", e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            ShowFatalError("Unhandled AppDomain exception", exception);
            return;
        }

        MessageBox.Show(
            $"Unhandled non-Exception error object:{Environment.NewLine}{e.ExceptionObject}",
            "Fatal error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ShowFatalError("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void ShowFatalError(string title, Exception exception)
    {
        var message = new StringBuilder()
            .AppendLine(exception.Message)
            .AppendLine()
            .AppendLine(exception.ToString())
            .ToString();

        Console.WriteLine(message);
    }

    private sealed record StartupOptions(string? PrefabPath, string? BannerlordInstallRoot);
}
