using System.Globalization;
using System.Text.Json;
using Dfe.Common.AI.Services.Application;
using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.Quality;
using GovUK.Dfe.AI.Agents.Quality.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
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

    public const string GuardrailName = "test-guardrail";

    public const string GuardrailSubscription = "00000000-0000-0000-0000-000000000000";

    public const string GuardrailResourceGroup = "tests";

    /// <summary>The settings the agents library needs to start, with telemetry optional as in local development.</summary>
    public static Dictionary<string, string?> RequiredSettings(string? judgeModel = null) => new()
    {
        ["AiAgents:Foundry:Endpoint"] = "https://tests.services.ai.azure.com/api/projects/tests",
        ["AiAgents:Foundry:DefaultModel"] = "tests/gpt-4o",
        ["AiAgents:Authentication:TenantId"] = "tenant",
        ["AiAgents:Authentication:ClientId"] = "client",
        ["AiAgents:Authentication:ClientSecret"] = "secret",
        ["AiAgents:RequireTokenUsageTelemetry"] = "false",
        ["AiAgents:Guardrails:SubscriptionId"] = GuardrailSubscription,
        ["AiAgents:Guardrails:ResourceGroup"] = GuardrailResourceGroup,
        ["AiAgents:Guardrails:Name"] = GuardrailName,
        ["AiAgents:Guardrails:Deployments:0"] = "gpt-5.1",
        ["AiAgents:Evaluation:JudgeModel"] = judgeModel,
        ["AiAgents:Evaluation:SampleRate"] = "0",
    };

    public ProvisioningTestHost()
    {
        Guardrails.ApplyAsync(default).ReturnsForAnyArgs(new GuardrailReport(GuardrailName, []));
        Agents.ProvisionAsync(default!, default).ReturnsForAnyArgs(call =>
            [.. call.Arg<IReadOnlyCollection<AgentDefinition>>().Select(d => new AgentReference(d.Name, d.Name, "1"))]);
    }

    public IAgentService Agents { get; } = Substitute.For<IAgentService>();

    public IAgentRunEvaluator Judge { get; } = Substitute.For<IAgentRunEvaluator>();

    public IAgentRuntimeService Runtime { get; } = Substitute.For<IAgentRuntimeService>();

    public IFoundryGuardrailsService Guardrails { get; } = Substitute.For<IFoundryGuardrailsService>();

    public string ReportsDirectory => Path.Combine(_root, "reports");

    private string TestCasesDirectory => Path.Combine(_root, "cases");

    private string BaselinesDirectory => Path.Combine(_root, "baselines");

    public void AnswerWith(string output) =>
        Agents.RunAsync(default!, default!, default, default).ReturnsForAnyArgs(call =>
            new AgentResult { AgentName = call.Arg<AgentDefinition>().Name, Output = output });

    public void JudgeScores(double score) =>
        Judge.EvaluateAsync(default!, default).ReturnsForAnyArgs(JudgeMetrics.All.ToDictionary(metric => metric, _ => score));

    public void AddCase(string agentName, string[]? mustMention = null, string[]? mustNotMention = null, string caseName = "case",
        string prompt = "How many pupils are on roll?", bool guardrailMayBlock = false, string? group = null)
    {
        var directory = Directory.CreateDirectory(Path.Combine(TestCasesDirectory, agentName));
        File.WriteAllText(Path.Combine(directory.FullName, $"{caseName}.json"), JsonSerializer.Serialize(new
        {
            prompt,
            evidence = "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
            mustMention = mustMention ?? [],
            mustNotMention = mustNotMention ?? [],
            guardrailMayBlock,
            group,
        }));
    }

    public void AddCaseForEveryAgent(string[]? mustMention = null, string[]? mustNotMention = null, string caseName = "case",
        string prompt = "How many pupils are on roll?", bool guardrailMayBlock = false, string? group = null)
    {
        foreach (var agent in CommonAgents.All)
        {
            AddCase(agent.Name, mustMention, mustNotMention, caseName, prompt, guardrailMayBlock, group);
        }
    }

    /// <summary>Saves a baseline report for every agent, in which each judge metric averaged <paramref name="score"/>.</summary>
    public void SaveBaselineForEveryAgent(double score, string caseName = "case")
    {
        Directory.CreateDirectory(BaselinesDirectory);
        foreach (var agent in CommonAgents.All)
        {
            var report = new AgentEvaluationReport(agent.Name,
                [new AgentTestResult(caseName, "398 pupils are on roll [Evidence 1].", [], JudgeMetrics.All.ToDictionary(m => m, _ => score))]);
            File.WriteAllText(Path.Combine(BaselinesDirectory, $"{agent.Name}.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    /// <summary>Writes text that isn't an evaluation report as an agent's baseline.</summary>
    public void SaveUnreadableBaseline(string agentName)
    {
        Directory.CreateDirectory(BaselinesDirectory);
        File.WriteAllText(Path.Combine(BaselinesDirectory, $"{agentName}.json"), "{ not json");
    }

    /// <summary>
    /// Everything logged, after the Serilog levels have filtered it. Serilog quotes text values in messages, so quotes are
    /// removed to keep assertions readable.
    /// </summary>
    public IReadOnlyList<LogLine> Logs =>
        [.. _provider!.GetFakeLogCollector().GetSnapshot().Select(r => new LogLine(r.Level, r.Message.Replace("\"", "")))];

    /// <param name="judgeModel">The judge model, or null for no judge.</param>
    /// <param name="applicationLogLevel">
    /// Sets <c>Serilog:MinimumLevel:Default</c>, which the app's appsettings.json uses for its own and the library's logs.
    /// </param>
    /// <param name="maxParallelTestRuns">Sets <c>AgentQuality:MaxParallelTestRuns</c>.</param>
    /// <param name="repeats">Sets <c>AgentQuality:Repeats</c>.</param>
    /// <param name="maxGroupGap">Sets <c>AgentQuality:MaxGroupGap</c>, or leaves the group check off when null.</param>
    public IAgentProvisioningService Build(string? judgeModel = null, string applicationLogLevel = "Information",
        int maxParallelTestRuns = 4, int repeats = 1, double? maxGroupGap = null)
    {
        var settings = RequiredSettings(judgeModel);
        settings["Serilog:MinimumLevel:Default"] = applicationLogLevel;
        settings["AgentQuality:TestCasesDirectory"] = TestCasesDirectory;
        settings["AgentQuality:ReportsDirectory"] = ReportsDirectory;
        settings["AgentQuality:MaxParallelTestRuns"] = maxParallelTestRuns.ToString(CultureInfo.InvariantCulture);
        settings["AgentQuality:Repeats"] = repeats.ToString(CultureInfo.InvariantCulture);
        settings["AgentQuality:BaselinesDirectory"] = BaselinesDirectory;
        settings["AgentQuality:MaxGroupGap"] = maxGroupGap?.ToString(CultureInfo.InvariantCulture);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        // The fake logger stands in for Application Insights: added after Serilog, which passes every event to it.
        var services = new ServiceCollection()
            .AddAgentProvisioning(configuration)
            .AddLogging(logging => logging.AddFakeLogging());
        services.AddSingleton(Agents);
        services.AddSingleton(Runtime);
        services.AddSingleton(Guardrails);
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

internal sealed record LogLine(LogLevel Level, string Message);
