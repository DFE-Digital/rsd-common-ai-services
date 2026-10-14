using GovUK.Dfe.AI.Agents.Evaluation.Quality;
using GovUK.Dfe.AI.Agents.Quality.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfe.Common.AI.Services.Application.Tests;

/// <summary>
/// The release gate scores answers only through the library's judge, so it must be registered exactly when a judge
/// model is set.
/// </summary>
public sealed class FoundryJudgeTests
{
    private const string JudgeModel = "gpt-5.1";

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

    private static IConfiguration Configuration(string judgeModel) =>
        new ConfigurationBuilder().AddInMemoryCollection(ProvisioningTestHost.RequiredSettings(judgeModel)).Build();
}
