namespace AutoSig.Domain.Interfaces;

/// <summary>
/// Marker interface for the LLM instance dedicated to the Strategist Agent.
/// Uses a deep reasoning model to craft well-thought-out trade proposals.
/// Configure via AUTOSIG_STRATEGIST_MODEL env var.
/// </summary>
public interface IStrategistLlmProvider : ILlmProvider { }

/// <summary>
/// Marker interface for the LLM instance dedicated to the Risk Manager Agent.
/// Uses a fast, efficient model to evaluate proposals with low latency.
/// Configure via AUTOSIG_RISK_MODEL env var.
/// </summary>
public interface IRiskManagerLlmProvider : ILlmProvider { }
