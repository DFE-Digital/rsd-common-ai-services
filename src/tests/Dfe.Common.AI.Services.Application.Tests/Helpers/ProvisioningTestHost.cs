using System.Text.Json;
using Dfe.Common.AI.Services.Application.Agents;
using Dfe.Common.AI.Services.Application.Agents.Interfaces;
using GovUK.Dfe.AI.Agents.Diagnostics.Interfaces;
using GovUK.Dfe.AI.Agents.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.Services.Interfaces;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.Quality.Interfaces;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using GovUK.Dfe.AI.Agents.Evaluation.ResponsibleAI.Enums;
using GovUK.Dfe.AI.Agents.Evaluation.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Tests.Helpers;

/// <summary>
/// Provisioning a temporary agent host for integration tests, with mocked dependencies and a temporary file system for test cases and reports.
/// </summary>
internal sealed class ProvisioningTestHost : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agent-provisioning-tests-").FullName;
    private ServiceProvider? _provider;

    public const string GuardrailName = "test-guardrail";

    /// <summary>The <c>AiAgents:ApplicationName</c> every metric is tagged with.</summary>
    public const string ApplicationName = "bt-test-app";

    public const string GuardrailSubscription = "00000000-0000-0000-0000-000000000000";

    public const string GuardrailResourceGroup = "tests";

    /// <summary>The quality gate's checks that the test cases are complete enough to release on.</summary>
    public static readonly string[] DatasetCoverageChecks = ["accountability.evaluation-dataset", "fairness.group-sample-size"];

    /// <summary>The settings the agents library needs to start, with telemetry optional as in local development.</summary>
    public static Dictionary<string, string?> RequiredSettings(string? judgeModel = null) => new()
    {
        ["AiAgents:ApplicationName"] = ApplicationName,
        ["AiAgents:Foundry:Endpoint"] = "https://tests.services.ai.azure.com/api/projects/tests",
        ["AiAgents:Foundry:DefaultModel"] = "tests/gpt-4o",
        ["AiAgents:Authentication:TenantId"] = "tenant",
        ["AiAgents:Authentication:ClientId"] = "client",
        ["AiAgents:Authentication:ClientSecret"] = "secret",
        ["AiAgents:RequireTokenUsageTelemetry"] = "false",
        ["AiAgents:Guardrails:SubscriptionId"] = GuardrailSubscription,
        ["AiAgents:Guardrails:ResourceGroup"] = GuardrailResourceGroup,
        ["AiAgents:Guardrails:Name"] = GuardrailName,
        ["AiAgents:Guardrails:Targets"] = "Agents",
        ["AiAgents:Evaluation:JudgeModel"] = judgeModel,
        ["AiAgents:Evaluation:SampleRate"] = "0",
    };

    public ProvisioningTestHost()
    {
        Guardrails.CheckAsync(default).ReturnsForAnyArgs(new GuardrailReport(GuardrailName, []));
        Agents.ProvisionAsync(default!, default).ReturnsForAnyArgs(call =>
            [.. call.Arg<IReadOnlyCollection<AgentDefinition>>().Select(d => new AgentReference(d.Name, d.Name, "1"))]);
    }

    public IAgentService Agents { get; } = Substitute.For<IAgentService>();

    public IAgentRunEvaluator Judge { get; } = Substitute.For<IAgentRunEvaluator>();

    public IAgentRuntimeService Runtime { get; } = Substitute.For<IAgentRuntimeService>();

    public IFoundryGuardrailsService Guardrails { get; } = Substitute.For<IFoundryGuardrailsService>();

    /// <summary>The app's <see cref="IAgentMetrics"/>, once <see cref="Build"/> has run.</summary>
    public IAgentMetrics Metrics => _provider!.GetRequiredService<IAgentMetrics>();

    public string ReportsDirectory => Path.Combine(_root, "reports");

    private string TestCasesDirectory => Path.Combine(_root, "cases");

    private string BaselinesDirectory => Path.Combine(_root, "baselines");

    public void AnswerWith(string output) =>
        Agents.RunAsync(default!, default!, default, default).ReturnsForAnyArgs(call =>
            new AgentResult { AgentName = call.Arg<AgentDefinition>().Name, Output = output });

    public void JudgeScores(double score) =>
        Judge.EvaluateAsync(default!, default).ReturnsForAnyArgs(JudgeMetrics.All.ToDictionary(metric => metric, _ => score));

    /// <summary>
    /// Adds an evaluation case in a file of its own, as the library loads them: its name is
    /// <c>evaluation/reliability-and-safety/&lt;id&gt;</c>.
    /// </summary>
    public void AddCase(string agentName, string[]? mustMention = null, string[]? mustNotMention = null, string id = "case",
        string prompt = "How many pupils are on roll?")
    {
        var file = Path.Combine(TestCasesDirectory, agentName, $"{id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(new
        {
            type = "reliability-and-safety",
            evaluation = new[]
            {
                new
                {
                    id,
                    description = "Gets the number of pupils on roll wrong.",
                    prompt,
                    evidence = "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
                    // Every case needs a pass condition; by default, the fact in the evidence.
                    mustMention = mustMention ?? (mustNotMention is null ? ["398"] : []),
                    mustNotMention = mustNotMention ?? [],
                },
            },
        }));
    }

    public void AddCaseForEveryAgent(string[]? mustMention = null, string[]? mustNotMention = null, string id = "case",
        string prompt = "How many pupils are on roll?")
    {
        foreach (var agent in CommonAgents.All)
        {
            AddCase(agent.Name, mustMention, mustNotMention, id, prompt);
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
        [.. _provider!.GetFakeLogCollector().GetSnapshot().Select(r => new LogLine(r.Level, r.Message.Replace("\"", ""), r.Exception))];

    /// <param name="judgeModel">The judge model, or null for no judge.</param>
    /// <param name="applicationLogLevel">
    /// Sets <c>Serilog:MinimumLevel:Default</c>, which the app's appsettings.json uses for its own and the library's logs.
    /// </param>
    /// <param name="checkDatasetCoverage">
    /// Whether the quality gate checks the test cases cover every attack, group and expectation it needs. These tests add
    /// a case or two to exercise the job, so it's off unless a test is about it; the real cases are checked in
    /// <c>TestCaseFileTests</c>.
    /// </param>
    public IAgentProvisioningService Build(string? judgeModel = null, string applicationLogLevel = "Information",
        bool checkDatasetCoverage = false)
    {
        var settings = RequiredSettings(judgeModel);
        settings["Serilog:MinimumLevel:Default"] = applicationLogLevel;
        foreach (var agent in CommonAgents.All)
        {
            settings[$"AiAgents:PromptFiles:SystemPrompts:{agent.SystemPromptKey}"] =
                Path.Combine(AppContext.BaseDirectory, "Prompts", $"{agent.SystemPromptKey}.md");
        }

        foreach (var check in DatasetCoverageChecks)
        {
            settings[$"AiAgents:Evaluation:QualityGate:Checks:{check}:Enabled"] = checkDatasetCoverage.ToString();
        }

        if (!checkDatasetCoverage)
        {
            // These cases name no user group, so nothing can evaluate fairness.
            var principles = Enum.GetNames<ResponsibleAiPrinciple>().Where(p => p != nameof(ResponsibleAiPrinciple.Fairness)).ToList();
            for (var i = 0; i < principles.Count; i++)
            {
                settings[$"AiAgents:Evaluation:QualityGate:Principles:{i}"] = principles[i];
            }
        }
        settings["AiAgents:Evaluation:ReleaseGate:TestCasesDirectory"] = TestCasesDirectory;
        settings["AiAgents:Evaluation:ReleaseGate:ReportsDirectory"] = ReportsDirectory;
        settings["AiAgents:Evaluation:ReleaseGate:BaselinesDirectory"] = BaselinesDirectory;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        // The fake logger stands in for the console: added after Serilog, which passes every event to it.
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

internal sealed record LogLine(LogLevel Level, string Message, Exception? Exception = null);
