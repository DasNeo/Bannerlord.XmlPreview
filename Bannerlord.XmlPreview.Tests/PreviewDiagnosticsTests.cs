using Bannerlord.XmlPreview.Models;
using Xunit;

namespace Bannerlord.XmlPreview.Tests;

public sealed class PreviewDiagnosticsTests
{
    [Fact]
    public void NewDiagnosticsReportCompleteStatus()
    {
        var diagnostics = new PreviewDiagnostics();

        Assert.Equal(RenderCompletionStatus.Complete, diagnostics.CompletionStatus);
        Assert.Contains("CompletionStatus: Complete", diagnostics.ToDisplayText());
        Assert.Contains("TruncationReason: None", diagnostics.ToDisplayText());
    }

    [Fact]
    public void TruncationReportsPartialStatusAndStructuredReason()
    {
        var diagnostics = new PreviewDiagnostics();

        diagnostics.MarkTruncated("A subtree was skipped.");

        Assert.Equal(RenderCompletionStatus.Partial, diagnostics.CompletionStatus);
        Assert.Equal(["A subtree was skipped."], diagnostics.TruncationReasons);
        Assert.Contains("CompletionStatus: Partial", diagnostics.ToDisplayText());
        Assert.Contains("TruncationReason: A subtree was skipped.", diagnostics.ToDisplayText());
    }

    [Fact]
    public void FailureOverridesPartialStatus()
    {
        var diagnostics = new PreviewDiagnostics();
        diagnostics.MarkTruncated("A subtree was skipped.");

        diagnostics.MarkFailed("Rendering failed.");

        Assert.Equal(RenderCompletionStatus.Failed, diagnostics.CompletionStatus);
        Assert.Contains("Rendering failed.", diagnostics.Messages);
    }
}
