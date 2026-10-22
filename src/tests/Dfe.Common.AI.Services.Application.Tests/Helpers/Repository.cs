namespace Dfe.Common.AI.Services.Application.Tests.Helpers;

/// <summary>Files in the repository itself, such as the App's appsettings.json.</summary>
internal static class Repository
{
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Dfe.Common.Ai.Services.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Couldn't find the repository root.");
    }
}
