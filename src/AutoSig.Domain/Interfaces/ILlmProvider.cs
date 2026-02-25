using AutoSig.Domain.Models;

namespace AutoSig.Domain.Interfaces;

/// <summary>
/// Contract for the LLM provider. The Strategist and Risk Manager use this
/// to call the AI, keeping Infrastructure implementation details out of the Application layer.
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Sends a prompt to the LLM and returns the raw response text.
    /// Includes self-healing retry logic for invalid JSON responses.
    /// </summary>
    Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken ct = default);

    /// <summary>
    /// Sends a prompt and deserializes the response directly into a strongly-typed object.
    /// Uses Polly retry policies with self-correction on JSON parse failures.
    /// </summary>
    Task<T> CompleteTypedAsync<T>(string systemPrompt, string userMessage, CancellationToken ct = default);
}
