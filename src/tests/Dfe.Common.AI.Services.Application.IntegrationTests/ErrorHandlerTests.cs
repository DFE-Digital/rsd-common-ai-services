using Azure;
using Azure.Identity;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfe.Common.AI.Services.Application.IntegrationTests;

public sealed class ErrorHandlerTests
{
    private const string FoundryFailure = $"Failed to get or create Foundry agent '{AgentNames.Ofsted}'.";

    public static TheoryData<Exception, ExitCode> Failures => new()
    {
        { new AgentReleaseBlockedException([new AgentGateResult(AgentNames.Ofsted, ["Failed."])]), ExitCode.ReleaseBlocked },
        { new OperationCanceledException(), ExitCode.Cancelled },
        { new InvalidOperationException(FoundryFailure, new AuthenticationFailedException("Invalid client secret.")), ExitCode.InvalidConfiguration },
        { new InvalidOperationException(FoundryFailure, new RequestFailedException(403, "Forbidden.")), ExitCode.AzureRequestFailed },
        { new AggregateException(new RequestFailedException(403, "Forbidden.")), ExitCode.AzureRequestFailed },
        { new NullReferenceException(), ExitCode.Failed },
    };

    [Theory]
    [MemberData(nameof(Failures), DisableDiscoveryEnumeration = true)]
    public void Maps_each_failure_to_its_exit_code(Exception exception, ExitCode expected) =>
        Assert.Equal(expected, ErrorHandler.Handle(exception, NullLogger.Instance));

    [Fact]
    public void An_unreadable_settings_file_is_reported_as_invalid_configuration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appsettings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{ "Logging": { "FormatterName": "simple", "temporary" "copy" } }""");
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => new ConfigurationBuilder().AddJsonFile(path).Build());

            Assert.Equal(ExitCode.InvalidConfiguration, ErrorHandler.Handle(ex, NullLogger.Instance));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_settings_are_reported_as_invalid_configuration()
    {
        var ex = Assert.Throws<AgentConfigurationException>(() =>
            new ServiceCollection().AddAgentProvisioning(new ConfigurationBuilder().Build()));

        Assert.Contains("AiAgents:Foundry:Endpoint", ex.Message);
        Assert.Equal(ExitCode.InvalidConfiguration, ErrorHandler.Handle(ex, NullLogger.Instance));
    }
}
