namespace Bannerlord.XmlPreview.VisualRegression;

/// <summary>
/// Compares a prefab in a persistent Preview Scenario against its reference.
/// Comparison never accepts or modifies a baseline. Cancellation propagates to the caller.
/// Scenario validation, capture, comparison and artifact writing belong to the implementation.
/// </summary>
public interface IVisualRegression
{
    Task<VisualRegressionReport> CompareAsync(
        VisualRegressionRequest request,
        CancellationToken cancellationToken = default);
}
