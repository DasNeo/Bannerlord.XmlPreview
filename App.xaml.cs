using Bannerlord.XmlPreview.Services;
using Bannerlord.XmlPreview.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Core.Abstractions;
using Bannerlord.XmlPreview.Adapters.Bannerlord;
using Bannerlord.XmlPreview.Preview.Bannerlord;
using Bannerlord.XmlPreview.Models;
using System.Text;
using System.Windows.Threading;
using System.IO;

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
            var startupOptions = ParseStartupOptions(e.Args);

            if (ShouldRunHeadlessDiagnosticsExport(startupOptions))
            {
                RunHeadlessDiagnosticsExport(startupOptions);
                Shutdown(0);
                return;
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            if (mainWindow.DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ApplyStartupOptions(startupOptions.BannerlordInstallRoot, startupOptions.PrefabPath);
                if (!string.IsNullOrWhiteSpace(startupOptions.DiagnosticsOutputPath))
                {
                    viewModel.ExportDiagnosticsSnapshot(startupOptions.DiagnosticsOutputPath);
                }
            }

            if (startupOptions.ExitAfterLoad)
            {
                Shutdown(0);
                return;
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
        services.AddSingleton<IBannerlordWidgetReflectionService, BannerlordWidgetReflectionService>();
        services.AddSingleton<IPrefabReferenceResolver, BannerlordPrefabReferenceResolver>();
        services.AddSingleton<IWidgetPropertyResolver, BannerlordWidgetPropertyResolver>();
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

    private void RunHeadlessDiagnosticsExport(StartupOptions startupOptions)
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException("Service provider is not initialized.");
        }

        if (string.IsNullOrWhiteSpace(startupOptions.PrefabPath))
        {
            throw new InvalidOperationException("Headless diagnostics export requires a prefab path.");
        }

        if (string.IsNullOrWhiteSpace(startupOptions.DiagnosticsOutputPath))
        {
            throw new InvalidOperationException("Headless diagnostics export requires a diagnostics output path.");
        }

        var outputPath = startupOptions.DiagnosticsOutputPath;
        var appSettingsService = _serviceProvider.GetRequiredService<IAppSettingsService>();
        var effectiveBannerlordRoot = startupOptions.BannerlordInstallRoot;
        WriteDiagnosticsSnapshot(
            outputPath,
            startupOptions.PrefabPath,
            effectiveBannerlordRoot ?? appSettingsService.BannerlordInstallRoot,
            "Starting headless diagnostics export.",
            "Starting",
            diagnosticsText: null);

        try
        {
            if (!string.IsNullOrWhiteSpace(effectiveBannerlordRoot))
            {
                if (!appSettingsService.TrySetBannerlordInstallRoot(
                        effectiveBannerlordRoot,
                        out var normalizedRoot,
                        out var errorMessage))
                {
                    throw new InvalidOperationException(errorMessage);
                }

                effectiveBannerlordRoot = normalizedRoot;
            }

            var diagnostics = new PreviewDiagnostics();
            using var diagnosticsTimingScope = diagnostics.BeginTimingScope();
            var prefabLoader = _serviceProvider.GetRequiredService<IPrefabLoader>();
            var prefabRenderer = _serviceProvider.GetRequiredService<IPrefabRenderer>();
            var loadStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var document = prefabLoader.Load(startupOptions.PrefabPath, diagnostics);
            loadStopwatch.Stop();
            WriteDiagnosticsSnapshot(
                outputPath,
                document.SourcePath,
                effectiveBannerlordRoot ?? appSettingsService.BannerlordInstallRoot,
                $"Loaded {Path.GetFileName(document.SourcePath)}. Starting render.",
                "Loaded",
                loadDurationMs: loadStopwatch.ElapsedMilliseconds,
                diagnosticsText: null);

            var renderStopwatch = System.Diagnostics.Stopwatch.StartNew();
            prefabRenderer.Render(document, diagnostics);
            renderStopwatch.Stop();
            WriteDiagnosticsSnapshot(
                outputPath,
                document.SourcePath,
                effectiveBannerlordRoot ?? appSettingsService.BannerlordInstallRoot,
                $"Loaded {Path.GetFileName(document.SourcePath)}",
                "Completed",
                loadDurationMs: loadStopwatch.ElapsedMilliseconds,
                renderDurationMs: renderStopwatch.ElapsedMilliseconds,
                diagnosticsText: diagnostics.ToDisplayText());
        }
        catch (Exception exception)
        {
            WriteDiagnosticsSnapshot(
                outputPath,
                startupOptions.PrefabPath,
                effectiveBannerlordRoot ?? appSettingsService.BannerlordInstallRoot,
                $"Headless diagnostics export failed: {exception.Message}",
                "Failed",
                diagnosticsText: exception.ToString());
            throw;
        }
    }

    private static bool ShouldRunHeadlessDiagnosticsExport(StartupOptions startupOptions)
    {
        return startupOptions.ExitAfterLoad &&
               !string.IsNullOrWhiteSpace(startupOptions.DiagnosticsOutputPath);
    }

    private static void WriteDiagnosticsSnapshot(
        string outputPath,
        string prefabPath,
        string bannerlordInstallRoot,
        string statusText,
        string stage,
        long? loadDurationMs = null,
        long? renderDurationMs = null,
        string? diagnosticsText = null)
    {
        var directoryPath = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var lines = new List<string>
        {
            $"GeneratedAtUtc: {DateTime.UtcNow:O}",
            $"Stage: {stage}",
            $"Status: {statusText}",
            $"Prefab: {prefabPath}"
        };

        if (!string.IsNullOrWhiteSpace(bannerlordInstallRoot))
        {
            lines.Add($"BannerlordRoot: {bannerlordInstallRoot}");
        }

        if (loadDurationMs.HasValue)
        {
            lines.Add($"LoadDurationMs: {loadDurationMs.Value}");
        }

        if (renderDurationMs.HasValue)
        {
            lines.Add($"RenderDurationMs: {renderDurationMs.Value}");
        }

        lines.Add(string.Empty);
        lines.Add(string.IsNullOrWhiteSpace(diagnosticsText)
            ? "No diagnostics were generated."
            : diagnosticsText);

        File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines), new UTF8Encoding(false));
    }

    private static StartupOptions ParseStartupOptions(IReadOnlyList<string> args)
    {
        string? prefabPath = null;
        string? bannerlordInstallRoot = null;
        string? diagnosticsOutputPath = null;
        var exitAfterLoad = false;

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

            if (string.Equals(argument, "--diagnostics-out", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Count)
            {
                diagnosticsOutputPath = args[++index];
                continue;
            }

            if (string.Equals(argument, "--exit-after-load", StringComparison.OrdinalIgnoreCase))
            {
                exitAfterLoad = true;
                continue;
            }

            if (!argument.StartsWith("-", StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(prefabPath))
            {
                prefabPath = argument;
            }
        }

        return new StartupOptions(prefabPath, bannerlordInstallRoot, diagnosticsOutputPath, exitAfterLoad);
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

    private sealed record StartupOptions(
        string? PrefabPath,
        string? BannerlordInstallRoot,
        string? DiagnosticsOutputPath,
        bool ExitAfterLoad);
}
