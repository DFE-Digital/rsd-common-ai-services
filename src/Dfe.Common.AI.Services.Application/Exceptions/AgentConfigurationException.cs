namespace Dfe.Common.AI.Services.Application.Exceptions;

/// <summary>
/// Thrown when the agents can't be set up because settings are missing or invalid, such as the Foundry endpoint,
/// model or credentials.
/// </summary>
/// <param name="message">What is missing or invalid.</param>
/// <param name="innerException">The agents library's validation error.</param>
public sealed class AgentConfigurationException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
