namespace Zlet.FolderConverter.Core.Models;

public sealed record ConversionResult(
    PlannedOperation Operation,
    OperationStatus Status,
    string Message,
    ConversionDiagnostic? Diagnostic = null,
    string? CompanionDirectoryPath = null,
    IReadOnlyList<string>? CompanionFiles = null);
