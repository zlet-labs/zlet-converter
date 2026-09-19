using Zlet.FolderConverter.Headless;

if (!HeadlessBatchCommand.TryParse(args, out var command, out var error) || command is null)
{
    Console.Error.WriteLine(error);
    return HeadlessBatchCommand.ExitUsage;
}
try
{
    return await HeadlessBatchRunner.CreateDefault().RunAsync(command, CancellationToken.None).ConfigureAwait(false);
}
catch (HeadlessBatchConfigurationException exception)
{
    Console.Error.WriteLine(exception.Message);
    return HeadlessBatchCommand.ExitUsage;
}
catch (OperationCanceledException)
{
    return HeadlessBatchCommand.ExitCancelled;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return HeadlessBatchCommand.ExitFatal;
}
