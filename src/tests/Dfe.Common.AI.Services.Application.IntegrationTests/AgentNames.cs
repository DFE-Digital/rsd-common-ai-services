namespace Dfe.Common.AI.Services.Application.IntegrationTests;

/// <summary>
/// Contains the names of the agents that are provisioned and tested in this project.
/// </summary>
internal static class AgentNames
{
    public const string Establishment = "rsd-establishment-agent";
    public const string Ofsted = "rsd-ofsted-agent";
    public const string Trust = "rsd-trust-agent";

    public static IReadOnlyList<string> All { get; } = [Establishment, Ofsted, Trust];
}
