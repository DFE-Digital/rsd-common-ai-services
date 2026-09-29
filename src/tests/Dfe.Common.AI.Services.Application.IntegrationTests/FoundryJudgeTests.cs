using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Azure.AI.Projects;
using Azure.Core;
using Dfe.Common.AI.Services.Application.QualityGate;
using GovUK.Dfe.CoreLibs.AiAgents.Quality;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

/// <summary>
/// Guards the library's Foundry judge, which the release gate depends on, through the real Foundry SDK against a
/// fake HTTP endpoint, so no request leaves the machine.
/// </summary>
public sealed class FoundryJudgeTests
{
    private const string JudgeModel = "gpt-5.1";

    private static readonly AgentRunSample Sample = new(AgentNames.Establishment, "1", JudgeModel,
        "How many pupils are on roll?", "--- establishment_index Evidence 1 ---\nnumber_on_roll: 398",
        "398 pupils are on roll [Evidence 1].");

    [Theory]
    [InlineData(JudgeModel, true)]
    [InlineData("", false)]
    public void Registers_the_library_judge_only_when_a_judge_model_is_set(string judgeModel, bool expected)
    {
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddAgentProvisioning(Configuration(judgeModel))
            .BuildServiceProvider();

        Assert.Equal(expected, provider.GetService<IAgentRunEvaluator>() is ExtensionsAiEvaluator);
    }

    [Fact]
    public async Task Scores_through_the_Foundry_Responses_API_without_sampling_settings()
    {
        var foundry = new FakeFoundry(HttpStatusCode.OK, "<S0>Checked.</S0><S1>Grounded.</S1><S2>4</S2>");

        var scores = await Evaluator(foundry, new CapturingLogger()).EvaluateAsync(Sample);

        foreach (var metric in JudgeMetrics.All)
        {
            Assert.Equal(4, scores[metric]);
        }

        Assert.NotEmpty(foundry.Requests);
        foreach (var request in foundry.Requests)
        {
            Assert.Equal(JudgeModel, (string?)request["model"]);
            Assert.False(request.ContainsKey("temperature"));
            Assert.False(request.ContainsKey("top_p"));
            Assert.False(request.ContainsKey("max_output_tokens"));
        }
    }

    [Fact]
    public async Task Logs_why_when_the_judge_call_fails()
    {
        var foundry = new FakeFoundry(HttpStatusCode.NotFound, "The model 'gpt-5.1' does not exist.");
        var logger = new CapturingLogger();

        var scores = await Evaluator(foundry, logger).EvaluateAsync(Sample);

        Assert.Empty(scores);
        foreach (var metric in JudgeMetrics.All)
        {
            Assert.Contains(logger.Warnings, w => w.Contains(metric) && w.Contains("404") && !w.Contains("   at "));
        }
    }

    private static ExtensionsAiEvaluator Evaluator(FakeFoundry foundry, ILogger<ExtensionsAiEvaluator> logger) =>
        new(new CompositeEvaluator(new GroundednessEvaluator(), new RelevanceEvaluator()),
            new ChatConfiguration(new FoundryJudgeChatClient(foundry.Client(), JudgeModel)),
            logger);

    private static IConfiguration Configuration(string judgeModel) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AiAgents:Foundry:Endpoint"] = "https://localhost/api/projects/tests",
            ["AiAgents:Foundry:DefaultModel"] = "tests/gpt-4o",
            ["AiAgents:Authentication:TenantId"] = "tenant",
            ["AiAgents:Authentication:ClientId"] = "client",
            ["AiAgents:Authentication:ClientSecret"] = "secret",
            ["AiAgents:RequireTokenUsageTelemetry"] = "false",
            ["AgentQuality:JudgeModel"] = judgeModel,
        }).Build();

    /// <summary>Answers every Responses API call with the given status: the judge's reply, or an error.</summary>
    private sealed class FakeFoundry(HttpStatusCode status, string text) : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];

        public Azure.AI.Extensions.OpenAI.ProjectOpenAIClient Client()
        {
            var options = new AIProjectClientOptions
            {
                Transport = new HttpClientPipelineTransport(new HttpClient(this)),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            };
            var credential = DelegatedTokenCredential.Create((_, _) => new AccessToken("token", DateTimeOffset.UtcNow.AddHours(1)));
            return new AIProjectClient(new Uri("https://localhost/api/projects/tests"), credential, options)
                .ProjectOpenAIClient;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject());

            var body = status == HttpStatusCode.OK ? Reply(text) : Error(text);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }

        private static JsonObject Error(string message) => new()
        {
            ["error"] = new JsonObject { ["code"] = "DeploymentNotFound", ["message"] = message },
        };

        private static JsonObject Reply(string judgeReply) => new()
        {
            ["id"] = "resp_1",
            ["object"] = "response",
            ["created_at"] = 1,
            ["status"] = "completed",
            ["model"] = JudgeModel,
            ["output"] = new JsonArray(new JsonObject
            {
                ["type"] = "message",
                ["id"] = "msg_1",
                ["status"] = "completed",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "output_text",
                    ["text"] = judgeReply,
                    ["annotations"] = new JsonArray(),
                }),
            }),
        };
    }

    private sealed class CapturingLogger : ILogger<ExtensionsAiEvaluator>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
