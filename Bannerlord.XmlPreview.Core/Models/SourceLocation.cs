namespace Bannerlord.XmlPreview.Models;

public sealed record SourceLocation(string FilePath, int Line, int Column = 1);
