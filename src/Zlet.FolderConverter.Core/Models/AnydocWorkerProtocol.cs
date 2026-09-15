namespace Zlet.FolderConverter.Core.Models;

public sealed record AnydocWorkerRequest(
    string Id,
    string SourcePath,
    string OutputPath,
    SourceFormat SourceFormat,
    string? AssetDir = null);

public sealed record AnydocWorkerResponse(
    string Id = "",
    bool Success = false,
    string ErrorCode = "",
    string ErrorMessage = "",
    bool HasExtractedText = false);

public sealed record AnydocWorkerExecutionResult(
    bool Success,
    string ErrorCode = "",
    string ErrorMessage = "",
    bool TimedOut = false,
    int? ExitCode = null,
    bool HasStandardOutput = false,
    bool HasStandardError = false,
    bool HasExtractedText = false);

public sealed record AnydocHandshakeResponse(
    bool Ready = false,
    string Version = "",
    string AnydocVersion = "",
    string AnydocRevision = "",
    string ErrorCode = "",
    string ErrorMessage = "");
