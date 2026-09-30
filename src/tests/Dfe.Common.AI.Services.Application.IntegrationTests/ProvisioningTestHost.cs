using System.Globalization;
using System.Text.Json;
using Dfe.Common.AI.Services.Application;
using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.CoreLibs.AiAgents.Agents.Interfaces;
using GovUK.Dfe.CoreLibs.AiAgents.Quality;
using GovUK.Dfe.CoreLibs.AiAgents.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

/// <summary>
/// Provisioning a temporary agent host for integration tests, with mocked dependencies and a temporary file system for test cases and reports.
/// </summary>
internal sealed class ProvisioningTestHost : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agent-provisioning-tests-").FullName;
    private ServiceProvider? _provider;

    public ProvisioningTestHost()
    {
        Agents.ProvisionAsync(default!, default).ReturnsForAnyArgs(call =>
            [.. call.Arg<IReadOnlyCollection<AgentDefinition>>().Select(d => new AgentReference(d.Name, d.Name, "1"))]);
    }

    public IAgentService Agents { get; } = Substitute.For<IAgentService>();

    public IAgentRunEvaluator Judge { get; } = Substitute.For<IAgentRunEvaluator>();

    public IAgentRuntime Runtime { get; } = Substitute.For<IAgentRuntime>();

    public string ReportsDirectory => Path.Combine(_root, "reports");

    private string TestCasesDirectory => Path.Combine(_root, "cases");

    public void AnswerWith(string output) =>
        Agents.RunAsync(default!, default!, default, default).ReturnsForAnyArgs(call =>
            new AgentResult(call.Arg<AgentDefinition>().Name, output, TotalTokens: 0));

    public void JudgeScores(double score) =>
        Judge.EvaluateAsync(default!, default).ReturnsForAnyArgs(JudgeMetrics.All.ToDictionary(metric => metric, _ => score));

    public void AddCase(string agentName, string[]? mustMention = null, string[]? mustNotMention = null, string caseName = "case",
        string prompt = "How many pupils are on roll?")
    {
        var directory = Directory.CreateDirectory(Path.Combine(TestCasesDirectory, agentName));
        File.WriteAllText(Path.Combine(directory.FullName, $"{caseName}.json"), JsonSerializer.Serialize(new
        {
            prompt,
            evidence = "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
            mustMention = mustMention ?? [],
            mustNotMention = mustNotMention ?? [],
        }));
    }

    public void AddCaseForEveryAgent(string[]? mustMention = null, string[]? mustNotMention = null, string caseName = "case",
        string prompt = "How many pupils are on roll?")
    {
        foreach (var agent in CommonAgents.All)
        {
            AddCase(agent.Name, mustMention, mustNotMention, caseName, prompt);
        }
    }

    /// <summary>Everything logged, after the configured log levels have filtered it.</summary>
    public IReadOnlyList<FakeLogRecord> Logs => _provider!.GetFakeLogCollector().GetSnapshot();

    /// <param name="judgeModel">The judge model, or null for no judge.</param>
    /// <param name="applicationLogLevel">
    /// Sets <c>Logging:LogLevel:Dfe.Common.AI.Services.Application</c>, as the app's appsettings.json does.
    /// </param>
    /// <param name="maxParallelTestRuns">Sets <c>AgentQuality:MaxParallelTestRuns</c>.</param>
    public IAgentProvisioningService Build(string? judgeModel = null, string applicationLogLevel = "Information",
        int maxParallelTestRuns = 4)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Logging:LogLevel:Default"] = "Warning",
            ["Logging:LogLevel:Dfe.Common.AI.Services.Application"] = applicationLogLevel,
            ["AiAgents:Foundry:Endpoint"] = "https://localhost/api/projects/tests",
            ["AiAgents:Foundry:DefaultModel"] = "tests/gpt-4o",
            ["AiAgents:Authentication:TenantId"] = "tenant",
            ["AiAgents:Authentication:ClientId"] = "client",
            ["AiAgents:Authentication:ClientSecret"] = "secret",
            ["AiAgents:RequireTokenUsageTelemetry"] = "false",
            ["AgentQuality:JudgeModel"] = judgeModel,
            ["AgentQuality:TestCasesDirectory"] = TestCasesDirectory,
            ["AgentQuality:ReportsDirectory"] = ReportsDirectory,
            ["AgentQuality:MaxParallelTestRuns"] = maxParallelTestRuns.ToString(CultureInfo.InvariantCulture),
        }).Build();

        var services = new ServiceCollection()
            .AddLogging(logging => logging
                .AddConfiguration(configuration.GetSection("Logging"))
                .AddFakeLogging())
            .AddAgentProvisioning(configuration);
        services.AddSingleton(Agents);
        services.AddSingleton(Runtime);
        if (judgeModel is not null)
        {
            services.AddSingleton(Judge);
        }

        _provider = services.BuildServiceProvider();
        return _provider.GetRequiredService<IAgentProvisioningService>();
    }

    public void Dispose()
    {
        _provider?.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
