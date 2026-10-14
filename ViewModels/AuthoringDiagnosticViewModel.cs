using System.Windows.Input;
using Bannerlord.XmlPreview.Infrastructure;
using Bannerlord.XmlPreview.Interfaces;
using Bannerlord.XmlPreview.Models;

namespace Bannerlord.XmlPreview.ViewModels;

public sealed class AuthoringDiagnosticViewModel(string message, SourceLocation source, ISourceNavigator navigator, Action<Action> execute)
{
    public string Message { get; } = message;
    public SourceLocation Source { get; } = source;
    public string Label => $"{Message}\n{Source.FilePath}:{Source.Line}";
    public ICommand OpenCommand { get; } = new RelayCommand(() => execute(() => navigator.Open(source)));
}
