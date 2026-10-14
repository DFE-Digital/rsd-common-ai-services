using Azure;
using Azure.Identity;
using Dfe.Common.AI.Services.Application.Constants;
using Dfe.Common.AI.Services.Application.ErrorHandling;
using Dfe.Common.AI.Services.Application.Exceptions;
using Dfe.Common.AI.Services.Application.ValueObjects;
using GovUK.Dfe.AI.Agents.Guardrails.ValueObjects;
using GovUK.Dfe.AI.Agents.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfe.Common.AI.Services.Application.Tests;

public sealed class ErrorHandlerTests
{
    private const string FoundryFailure = $"Failed to get or create Foundry agent '{AgentNames.Ofsted}'.";

    public static TheoryData<Exception, ExitCode> Failures => new()
    {
        { new AgentReleaseBlockedException([new AgentGateResult(AgentNames.Ofsted, ["Failed."])]), ExitCode.ReleaseBlocked },
        {
            new GuardrailsNotAppliedException(new GuardrailReport("rsd-guardrail", ["Deployment 'gpt-5.1' wasn't found"])),
            ExitCode.InvalidConfiguration
        },
        { new OperationCanceledException(), ExitCode.Cancelled },
        { new InvalidOperationException(FoundryFailure, new AuthenticationFailedException("Invalid client secret.")), ExitCode.InvalidConfiguration },
        { new InvalidOperationException(FoundryFailure, new RequestFailedException(403, "Forbidden.")), ExitCode.AzureRequestFailed },
        { new AggregateException(new RequestFailedException(403, "Forbidden.")), ExitCode.AzureRequestFailed },
        { new NullReferenceException(), ExitCode.Failed },
        {
            new AgentProvisioningIncompleteException([new AgentReference(AgentNames.Ofsted, AgentNames.Ofsted, "2")], [AgentNames.Trust],
                new RequestFailedException(403, "Forbidden.")),
            ExitCode.AzureRequestFailed
        },
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

    [Fact]
    public void Summarises_a_failure_in_one_line_with_its_cause()
    {
        var ex = new InvalidOperationException(FoundryFailure, new RequestFailedException(403, "Forbidden."));

        Assert.Equal("Failed with exit code 4 (AzureRequestFailed): Forbidden.", ErrorHandler.Summarise(ExitCode.AzureRequestFailed, ex));
    }

    [Fact]
    public void Summarises_partial_provisioning_with_the_new_versions_and_the_cause()
    {
        var ex = new AgentProvisioningIncompleteException([new AgentReference(AgentNames.Ofsted, AgentNames.Ofsted, "2")],
            [AgentNames.Trust], new RequestFailedException(403, "Forbidden."));

        var summary = ErrorHandler.Summarise(ExitCode.AzureRequestFailed, ex);

        Assert.Contains($"Foundry has new versions of {AgentNames.Ofsted} 2", summary);
        Assert.Contains($"{AgentNames.Trust} weren't provisioned", summary);
        Assert.EndsWith("Cause: Forbidden.", summary);
    }
}
