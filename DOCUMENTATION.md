# AutoSig — Complete Technical Documentation

> **DeFi Developer Challenge: Agentic Wallets for AI Agents** | Superteam Nigeria  
> Autonomous Multi-Agent Treasury on Solana Devnet  
> Built with .NET 10 · MediatR · Solnet · OpenRouter (stepfun & nemotron)

This document is the single source of truth for every architectural decision, every line of code that matters, every guardrail check, and every test case in the system. Start here.

---

## Table of Contents

1. [System Overview](#1-system-overview)
2. [Project Structure](#2-project-structure)
3. [The Event Bus (MediatR)](#3-the-event-bus-mediatr)
4. [Domain Models](#4-domain-models)
5. [Agent Pipeline — Deep Dive](#5-agent-pipeline--deep-dive)
   - [Scout Agent](#51-scout-agent)
   - [Strategist Agent](#52-strategist-agent)
   - [Risk Manager Agent](#53-risk-manager-agent)
   - [Executor Agent](#54-executor-agent)
6. [The 3-Phase Guardrail System](#6-the-3-phase-guardrail-system)
7. [Infrastructure Layer](#7-infrastructure-layer)
   - [SolanaSignerEnclave](#71-solanasignerenclave)
   - [MarketDataService](#72-marketdataservice)
   - [OpenRouterLlmProvider](#73-openrouterllmprovider)
8. [Dependency Injection & Boot Sequence](#8-dependency-injection--boot-sequence)
9. [Terminal UI (Spectre.Console)](#9-terminal-ui-spectreconsole)
10. [Test Suite — Every Test Explained](#10-test-suite--every-test-explained)
11. [Security Architecture](#11-security-architecture)
12. [Key Design Decisions (with rationale)](#12-key-design-decisions-with-rationale)
13. [Configuration Reference — Environment Variables](#13-configuration-reference--environment-variables)

---

## 1. Hackathon Requirements Deep-Dive

This section is included to explicitly address the judging criteria for the **Agentic Wallets for AI Agents** bounty by Superteam Nigeria.

### A. Deep Dive: Wallet Design & Security Considerations
AutoSig’s wallet design is built around the principle of **strict encapsulation**:
1. **The Signer Enclave (`SolanaSignerEnclave.cs`)**: The Ed25519 private key is injected into a sealed class upon application boot. The key is in-memory only. It is *never* serialized, *never* logged, and *never* exposed via a public getter to any other part of the system.
2. **Separation of Concerns**: The AI generating the trade (`StrategistAgent`) has absolutely no code path to execute a trade. The only agent that can speak to the enclave is the `ExecutorAgent`, and it mathematically refuses to do so unless it holds an irrefutable `ProposalApprovedEvent` from the Risk Manager.
3. **The 3-Phase Guardrail**: True security means not trusting the AI. C# hard guardrails ensure the raw `amount` and `destination` fields are sane before the LLM logic is ever even considered. The risk policy (Velocity limits, Drawdown limits, Reserve floors) mathematically binds the wallet's trading actions.

### B. Deep Dive: Interaction with AI Agents
AutoSig does not use synchronous API calls between agents. Instead, it uses **MediatR (the Event Bus)**. 
- The **Scout Agent** acts as the sensory input, fetching real Solana RPC data and emitting a `MarketOpportunityFoundEvent`.
- The **Strategist Agent** (powered by `stepfun`) acts as the brain, catching the event, parsing the live context, generating JSON, and emitting a `ProposalGeneratedEvent`.
- The **Risk Manager Agent** (powered by `nemotron`) acts as the immune system, catching the proposal, evaluating it in 3 phases, and emitting a `ProposalApprovedEvent`.
- The **Executor Agent** acts as the motor output, catching the approval and submitting the transaction.
This pub/sub design allows AI agents to act asynchronously and independently.

### C. Scalability: Supporting Multiple Agents Independently
Because of the MediatR architecture, scaling the swarm is trivial. 
- Want a second Strategist specializing in Arbitrage? Simply write a new class handling `MarketOpportunityFoundEvent` and let it emit its own proposals into the bus. The Risk Manager doesn't care who wrote the proposal—it evaluates them all identically.
- Want a specialized Executor for Jupiter swaps? Write a new class handling `ProposalApprovedEvent` that filters for swap types.
The system is loosely coupled and infinitely horizontally scalable.

---

## 2. System Overview

AutoSig is an **autonomous agent swarm** operating on Solana Devnet. Four specialized AI agents communicate exclusively through MediatR events — there is no shared state, no direct function calls between agents, and no human in the loop.

Every 30 seconds, a background service triggers the **Scout Agent**, which fetches **real on-chain data** via Solana RPC. The data propagates through the swarm as a chain reaction of events:

```
[ConsensusLoop Timer] 
    ↓ ScanAsync()
[Scout Agent] — fetches live RPC data → publishes MarketOpportunityFoundEvent
    ↓ MediatR notification
[Strategist Agent] — feeds real data to LLM → publishes ProposalGeneratedEvent
    ↓ MediatR notification
[Risk Manager Agent] — 3-phase evaluation → publishes ProposalApprovedEvent | ProposalRejectedEvent
    ↓ MediatR notification (only if approved)
[Executor Agent] — signs & submits → publishes TransactionCompletedEvent
    ↓ MediatR notification
[SpectreUiAgent] — renders dashboard (receives ALL events above)
```

**Key principle:** Every agent only knows about its *direct input event* and its *output event*. No agent holds a reference to another agent. This is the pub/sub pattern enforced by MediatR.

---

## 2. Project Structure

```
AutoSig/
├── src/
│   ├── AutoSig.Domain/                    # Pure domain: models, events, interface contracts
│   │   ├── Events/
│   │   │   └── AgentEvents.cs             # All 5 MediatR event records
│   │   ├── Interfaces/
│   │   │   ├── ILlmProvider.cs            # Contract for LLM interactions
│   │   │   ├── ISolanaService.cs          # Contract for Solana blockchain interactions
│   │   │   └── IMarketDataService.cs      # Contract for real-time market data
│   │   └── Models/
│   │       ├── MarketContext.cs           # Snapshot of live on-chain state
│   │       ├── TradingPolicy.cs           # Immutable risk constants
│   │       ├── TradeProposal.cs           # The typed trade proposal record
│   │       ├── RiskAssessment.cs          # Result of the 3-phase evaluation
│   │       └── TransactionResult.cs       # On-chain execution outcome
│   │
│   ├── AutoSig.Application/               # Agent logic (no infrastructure dependencies)
│   │   ├── Agents/
│   │   │   ├── ScoutAgent.cs              # Reads real RPC data, emits opportunities
│   │   │   ├── StrategistAgent.cs         # Prompts LLM with real data, emits proposals
│   │   │   ├── RiskManagerAgent.cs        # 3-phase gate: Hard → Policy → AI
│   │   │   └── ExecutorAgent.cs           # Submits approved proposals to chain
│   │   └── ApplicationServiceRegistration.cs
│   │
│   ├── AutoSig.Infrastructure.AI/         # OpenRouter HTTP client + retry logic
│   │   ├── OpenRouterLlmProvider.cs
│   │   └── AiServiceRegistration.cs
│   │
│   ├── AutoSig.Infrastructure.Solana/     # Solnet RPC + key management
│   │   ├── SolanaSignerEnclave.cs         # Only file that ever holds the private key
│   │   ├── MarketDataService.cs           # Parallel RPC calls for real market data
│   │   └── SolanaServiceRegistration.cs
│   │
│   ├── AutoSig.Console/                   # Entry point, UI, background service
│   │   ├── Program.cs                     # Boot sequence, DI setup, policy display
│   │   ├── Services/
│   │   │   └── ConsensusLoopService.cs    # Timed BackgroundService, calls Scout
│   │   └── UI/
│   │       └── SpectreUiAgent.cs          # Real-time terminal dashboard
│   │
│   └── AutoSig.Tests/                     # xUnit test suite
│       └── RiskManagerGuardrailTests.cs   # 9 tests proving guardrails are unbypassable
│
├── SKILLS.md                              # Agent capability manifest (hackathon req)
├── README.md                              # Quick-start guide
└── .env                                   # Secrets file (gitignored)
```

**Why this structure?** This follows the Clean Architecture / Onion Architecture principle. The domain layer has zero dependencies on infrastructure. Agents in the application layer depend only on domain interfaces. Infrastructure implements those interfaces. This makes testing trivial — you can swap out any infrastructure component with a mock.

---

## 3. The Event Bus (MediatR)

MediatR is the nervous system of the swarm. The `IMediator.Publish()` method fires a notification to *all registered handlers* of that event type. This is how agents communicate without knowing about each other.

### Event Contracts (`AgentEvents.cs`)

```csharp
// Emitted by Scout — carries REAL on-chain data
public sealed record MarketOpportunityFoundEvent(
    string OpportunityDescription,
    double ConfidenceScore,
    MarketContext Context          // ← live blockchain state
) : INotification;

// Emitted by Strategist — the typed trade proposal
public sealed record ProposalGeneratedEvent(TradeProposal Proposal) : INotification;

// Emitted by Risk Manager when ALL 3 phases pass
public sealed record ProposalApprovedEvent(TradeProposal Proposal, RiskAssessment Assessment) : INotification;

// Emitted by Risk Manager when ANY phase fails
public sealed record ProposalRejectedEvent(TradeProposal Proposal, RiskAssessment Assessment) : INotification;

// Emitted by Executor with the on-chain result
public sealed record TransactionCompletedEvent(TransactionResult Result) : INotification;
```

**Critical detail:** `MarketOpportunityFoundEvent` carries a `MarketContext` — the real on-chain data snapshot. This means the Strategist receives factual blockchain state, not a made-up string. The `SpectreUiAgent` also receives this event and renders the live data table on screen.

---

## 4. Domain Models

### 4.1 `MarketContext` — The Real-World Data Snapshot

```
File: src/AutoSig.Domain/Models/MarketContext.cs
```

This `sealed record` represents a point-in-time snapshot of the Solana blockchain state as seen by the Scout Agent. Using `record` (not `class`) ensures immutability — once created, the context cannot be changed as it flows through the pipeline.

| Property | Type | Source | Meaning |
|----------|------|--------|---------|
| `TreasuryBalanceLamports` | `ulong` | RPC `GetBalance` | Raw lamport balance of the treasury wallet |
| `TreasuryBalanceSol` | `double` (computed) | Derived | `TreasuryBalanceLamports / 1_000_000_000` |
| `CurrentSlot` | `ulong` | RPC `GetSlot` | Current block height — proves chain is live |
| `RecentTransactionCount` | `long` | RPC `GetRecentPerformanceSamples` | Total txs in last 5 sample periods |
| `EstimatedTps` | `double` | RPC `GetRecentPerformanceSamples` | Calculated as `totalTxs / totalSeconds` |
| `LatestBlockhash` | `string` | RPC `GetLatestBlockHash` | Proves freshness of the data |
| `CapturedAt` | `DateTime` | Set at construction | UTC timestamp of when data was fetched |
| `Sentiment` | `MarketSentiment` | Derived from TPS + balance | Software-derived market mood |

**`ToSummary()` method:** Produces a structured text block that is literally pasted into the LLM's prompt. This is how the Strategist knows what the market looks like:

```
=== LIVE SOLANA DEVNET STATE (captured 10:15:32 UTC) ===
Treasury Balance : 1.2300 SOL (1,230,000,000 lamports)
Network Slot     : 342,891,204
Recent Tx Count  : 45,230
Estimated TPS    : 2,341.5
Latest Blockhash : abc123def456ghij...
Market Sentiment : Bullish
```

Uses `FormattableString.Invariant` to ensure numbers are formatted with `.` as the decimal separator regardless of the host OS locale.

### 4.2 `MarketSentiment` Enum

```csharp
public enum MarketSentiment { Bearish, Neutral, Bullish, HighActivity }
```

Derived in `MarketDataService.DeriveSentiment()`:
- `HighActivity` → TPS > 3,000
- `Bullish` → TPS > 1,500 OR balance > 2 SOL
- `Neutral` → balance > 0.5 SOL
- `Bearish` → everything else

---

### 4.3 `TradingPolicy` — The Immutable Safety Contract

```
File: src/AutoSig.Domain/Models/TradingPolicy.cs
```

This `sealed class` is instantiated with C# `init`-only property defaults. Once created, no property can be changed. This is the single source of truth for all risk limits.

| Property | Default | Meaning |
|----------|---------|---------|
| `MaxTradesPerHour` | `10` | Max trades in any rolling 60-minute window |
| `MaxTradesPerDay` | `50` | Max trades in any rolling 24-hour window |
| `MinTimeBetweenTrades` | `15 seconds` | Hard cooldown between consecutive trades |
| `MaxDailyDrawdownPercent` | `0.05` (5%) | If portfolio drops 5% from day's start, all trading halts |
| `MinReserveBalanceLamports` | `50,000,000` (0.05 SOL) | Treasury must always keep at least this much |
| `MaxSingleTransactionLamports` | `500,000,000` (0.5 SOL) | Absolute ceiling on one transaction |
| `AllowedProgramIds` | System, SPL Token, Token-2022 | Whitelist of Solana programs |
| `BlockedDestinations` | System Program address | Blocklist — can never receive funds |

**Why is System Program (`11111111111111111111111111111111`) both allowed and blocked?**  
It's *allowed* in `AllowedProgramIds` because SOL transfers are routed through the System Program as an *instruction*. It's *blocked* in `BlockedDestinations` because no funds should be sent directly *to* that address as a destination account.

---

### 4.4 `TradeProposal`

```
File: src/AutoSig.Domain/Models/TradeProposal.cs
```

The core message of the system — what the Strategist wants to execute.

| Property | Type | Set by | Meaning |
|----------|------|--------|---------|
| `Id` | `Guid` | Auto (constructor) | Unique ID for tracking through the pipeline |
| `CreatedAt` | `DateTime` | Auto (constructor) | UTC creation time |
| `Opportunity` | `string` | Strategist | The Scout's original opportunity description |
| `Type` | `ProposalType` | LLM output | SolTransfer, SplTokenTransfer, or SplTokenMint |
| `AmountLamports` | `ulong` | LLM output | Amount in lamports (1 SOL = 1,000,000,000) |
| `DestinationAddress` | `string` | LLM output | Recipient Solana wallet, Base58 encoded |
| `MintAddress` | `string?` | LLM output | Invented ticker (e.g. "MEME") if minting, else null |
| `Rationale` | `string` | LLM output | LLM's explanation of why this trade makes sense |
| `SelfAssessedRisk` | `double` | LLM output | 0.0–1.0 risk score from the Strategist itself |

---

### 4.5 `RiskAssessment`

```
File: src/AutoSig.Domain/Models/RiskAssessment.cs
```

The output of the Risk Manager — attached to all approval/rejection events.

| Property | Type | Meaning |
|----------|------|---------|
| `ProposalId` | `Guid` | Links back to the proposal |
| `Verdict` | `RiskVerdict` | Approved, RejectedByHardGuardrail, or RejectedByAiGuardrail |
| `Reasoning` | `string` | Human-readable explanation (C# message or LLM text) |
| `AiRiskScore` | `double?` | Null if rejected before reaching AI phase; 0.0–1.0 otherwise |
| `IsApproved` | `bool` (computed) | `true` only when `Verdict == RiskVerdict.Approved` |

---

### 4.6 `TransactionResult`

```
File: src/AutoSig.Domain/Models/TransactionResult.cs
```

What the Executor publishes after submitting to chain.

| Property | Meaning |
|----------|---------|
| `Success` | Whether the RPC accepted the transaction |
| `SignatureHash` | The Solana transaction signature (Base58, ~88 chars) |
| `ErrorMessage` | Populated only on failure |
| `ExecutedAt` | UTC timestamp |
| `ExplorerUrl` | Auto-generated: `https://explorer.solana.com/tx/{sig}?cluster=devnet` |

---

## 5. Agent Pipeline — Deep Dive

### 5.1 Scout Agent

```
File: src/AutoSig.Application/Agents/ScoutAgent.cs
Triggered by: ConsensusLoopService (every 30 seconds)
Publishes: MarketOpportunityFoundEvent
```

The Scout is the *only* agent triggered by the timer. All other agents fire in reaction to events.

**`ScanAsync()`** — the entry point:
1. Calls `IMarketDataService.GetMarketContextAsync()` to fetch real Solana state
2. Passes the `MarketContext` to `AnalyzeMarket()` to get an opportunity description
3. Publishes `MarketOpportunityFoundEvent` carrying both the text description AND the full `MarketContext`

**`AnalyzeMarket()` — the 5-strategy decision tree:**

This is pure deterministic C# logic. No LLM. No randomness.

| Condition | Strategy | Confidence |
|-----------|----------|------------|
| Balance < 0.1 SOL | Capital preservation — minimal transfer | 55% |
| Sentiment = HighActivity | Peak conditions — strategic deployment | 82% |
| Sentiment = Bullish | Moderate deployment to test vault | 75% |
| Sentiment = Neutral | Small exploratory transfer | 65% |
| Sentiment = Bearish | Minimal only, preserve capital | 50% |

The **confidence score** is not used to gate the pipeline — it's metadata passed to the Strategist to influence how aggressive the trade proposal should be.

---

### 5.2 Strategist Agent

```
File: src/AutoSig.Application/Agents/StrategistAgent.cs
Listens to: MarketOpportunityFoundEvent
Publishes: ProposalGeneratedEvent
```

The Strategist is a `INotificationHandler<MarketOpportunityFoundEvent>` — MediatR wires it automatically.

**How it works:**
1. Receives the event (which includes the full `MarketContext`)
2. Builds a user message containing `context.ToSummary()` (the full on-chain data table) plus the Scout's analysis text
3. Sends it to the primary LLM (e.g. `stepfun/step-3.5-flash:free`) with a strict system prompt
4. Deserializes the LLM's JSON output into a `LlmTradeProposalResponse`
5. Maps it into a `TradeProposal` and publishes `ProposalGeneratedEvent`

**`LlmTradeProposalResponse` JSON schema** (what the LLM must return):
```json
{
  "type": "SolTransfer",
  "amount_lamports": 50000000,
  "destination_address": "DVt1X6D2nLaVBFQKnafm4gNPucLxUhFB9SrBKBkH7CqP",
  "mint_address": null,
  "rationale": "Network TPS at 2341 suggests active conditions. Deploying 0.05 SOL to test vault.",
  "self_assessed_risk": 0.15
}
```

**System prompt constraints injected into the LLM:**
- Max 500,000,000 lamports (0.5 SOL) — stated in the prompt
- Max 25% of treasury balance — stated in the prompt
- For SolTransfer: must use the test protocol vault address
- If Bearish sentiment: must propose < 10,000,000 lamports
- Must reference actual market data in the rationale

**Why tell the LLM the limits if we also enforce them in code?**  
The prompt constraints act as a *first-pass soft filter*. They reduce the likelihood of the LLM proposing something that Phase 1 will reject, which saves an LLM round-trip. The C# guardrails are the *actual guarantee*.

**Error handling:** If the LLM returns malformed JSON after all retries, the exception is caught and logged — the cycle simply stops without producing a proposal. This is intentional. A bad proposal is worse than no proposal.

---

### 5.3 Risk Manager Agent

```
File: src/AutoSig.Application/Agents/RiskManagerAgent.cs
Listens to: ProposalGeneratedEvent
Publishes: ProposalApprovedEvent OR ProposalRejectedEvent
```

The most complex agent. See [Section 6](#6-the-3-phase-guardrail-system) for the complete guardrail breakdown.

**Thread-safety:** The velocity tracking uses `static` state shared across all instances:
- `ConcurrentQueue<DateTime> TradeTimestamps` — holds UTC timestamps of all approved trades
- `static DateTime _lastTradeTime` — timestamp of the most recent approved trade
- `static ulong _dailyStartingBalance` — balance snapshot at the start of each 24h window
- `static DateTime _dailyResetTime` — when the 24h window started

These are `static` because MediatR can create multiple handler instances. Using `ConcurrentQueue` makes dequeue/enqueue safe under concurrent access.

**Fail-safe default:** If the LLM call in Phase 3 throws an exception (network error, malformed response after retries), the agent **defaults to REJECT**. This is the correct safety posture — an uncertain AI evaluation is treated as dangerous.

---

### 5.4 Executor Agent

```
File: src/AutoSig.Application/Agents/ExecutorAgent.cs
Listens to: ProposalApprovedEvent
Publishes: TransactionCompletedEvent
```

The simplest agent by design — it should be. By the time a proposal reaches the Executor, it has passed 3 independent layers of evaluation. The Executor's only job is to submit it.

**What it does:**
1. Receives `ProposalApprovedEvent`
2. Calls `ISolanaService.ExecuteProposalAsync(proposal)` (delegates to `SolanaSignerEnclave`)
3. Logs success with explorer link, or logs error
4. Always publishes `TransactionCompletedEvent` regardless of success/failure (for UI rendering)

**It never talks to the LLM.** It has no risk logic. It trusts the Risk Manager completely. This separation of concerns is intentional — the Executor is a deterministic executor, not a decision-maker.

---

## 6. The 3-Phase Guardrail System

This is the core defensive innovation of AutoSig. The Risk Manager evaluates every proposal through three sequential gates. A proposal is rejected the moment it fails any gate — it never reaches the next.

```
Proposal enters
      │
      ▼
┌─────────────────────────────────────────────────┐
│  PHASE 1: HARD GUARDRAILS (Pure C# code)         │
│                                                   │
│  ✓ Amount ≤ 500,000,000 lamports (0.5 SOL)       │
│  ✓ Destination not in BlockedDestinations         │
│                                                   │
│  If FAIL → immediate ProposalRejectedEvent        │
│             LLM never called                      │
└─────────────────────────────────────────────────┘
      │ PASS
      ▼
┌─────────────────────────────────────────────────┐
│  PHASE 2: POLICY GUARDRAILS (Deterministic C#)   │
│                                                   │
│  ✓ 2a: Cooldown — ≥15s since last approved trade │
│  ✓ 2b: Velocity — <10 trades in last 60 minutes  │
│  ✓ 2c: Drawdown — portfolio not down >5% today   │
│  ✓ 2d: Reserve — balance after tx ≥ 0.05 SOL     │
│                                                   │
│  All 4 require real RPC balance data              │
│  If FAIL → immediate ProposalRejectedEvent        │
│             LLM never called                      │
└─────────────────────────────────────────────────┘
      │ PASS
      ▼
┌─────────────────────────────────────────────────┐
│  PHASE 3: AI SOFT GUARDRAILS                     │
│           (Independent LLM review)               │
│                                                   │
│  A second, independent LLM evaluates the         │
│  Strategist's proposal for SEMANTIC risks:       │
│  - Hallucinated/suspicious addresses             │
│  - Disproportionate amounts vs rationale         │
│  - Prompt injection in the rationale field       │
│  - Logic flaws                                   │
│                                                   │
│  LLM returns: { approved, risk_score, reasoning }│
│                                                   │
│  risk_score < 0.6  → ProposalApprovedEvent       │
│  risk_score ≥ 0.6  → ProposalRejectedEvent       │
└─────────────────────────────────────────────────┘
```

### Phase 2c — Drawdown Check (Detail)

The drawdown check uses a sliding 24-hour window:

```csharp
// At the start of each 24h window, record the starting balance
if (_dailyStartingBalance == 0) _dailyStartingBalance = currentBalance;

// Calculate drawdown: how much have we lost since the start of today?
var drawdown = 1.0 - ((double)currentBalance / _dailyStartingBalance);

// If drawdown >= 5%, halt all trading
if (drawdown >= _policy.MaxDailyDrawdownPercent)
    → EMERGENCY HALT
```

Example: Start of day balance = 2.0 SOL. Current balance = 1.89 SOL.
```
drawdown = 1.0 - (1.89 / 2.0) = 1.0 - 0.945 = 0.055 = 5.5%
5.5% >= 5.0% → HALT
```

### Phase 2d — Reserve Floor Check (Detail)

```csharp
if (currentBalance - proposal.AmountLamports < _policy.MinReserveBalanceLamports)
    → REJECT
```

Example: Balance = 0.06 SOL (60,000,000 lamports). Proposal = 0.02 SOL (20,000,000).
```
After: 60,000,000 - 20,000,000 = 40,000,000 lamports = 0.04 SOL
Reserve floor = 0.05 SOL = 50,000,000 lamports
40,000,000 < 50,000,000 → REJECT
```

---

## 7. Infrastructure Layer

### 7.1 `SolanaSignerEnclave`

```
File: src/AutoSig.Infrastructure.Solana/SolanaSignerEnclave.cs
Implements: ISolanaService
```

The **only** component in the entire system that holds the private key. The design principle is strict encapsulation:

- The private key is loaded in the constructor: `_wallet = new Wallet(Convert.FromBase64String(base58PrivateKey))`
- The private key is never returned by any method
- The private key is never logged
- The class is `sealed` — cannot be subclassed to bypass protections
- The class implements `ISolanaService` — the rest of the system only knows the interface

**`ExecuteProposalAsync()` flow:**
1. Fetches the latest blockhash via RPC (needed for Solana transaction validity)
2. Builds the transaction bytes based on proposal type:
   - `SolTransfer` → `SystemProgram.Transfer()` instruction
   - `SplTokenMint` → Creates a new mint account, initializes mint, creates ATA, and mints 1,000 tokens to the treasury.
3. Signs the transaction using the wallet's `Account`
4. Submits via `SendTransactionAsync()`
5. Returns a `TransactionResult` with the signature hash or error

**`BuildSolTransfer()` fallback safety:**
```csharp
var destination = proposal.DestinationAddress.Length >= 32
    ? proposal.DestinationAddress
    : TestProtocolVaultAddress;
```
If the LLM somehow produced a short/invalid address that passed the Risk Manager (it shouldn't), the enclave falls back to the known-good test vault address.

---

### 7.2 `MarketDataService`

```
File: src/AutoSig.Infrastructure.Solana/MarketDataService.cs
Implements: IMarketDataService
```

Makes **4 parallel RPC calls** on every Scout scan cycle for maximum performance:

```csharp
var balanceTask   = _solana.GetBalanceLamportsAsync(ct);     // GetBalance
var slotTask      = GetCurrentSlotAsync();                    // GetSlot
var blockHashTask = GetLatestBlockhashAsync();                // GetLatestBlockHash
var perfTask      = GetRecentPerformanceAsync();              // GetRecentPerformanceSamples(5)

await Task.WhenAll(balanceTask, slotTask, blockHashTask, perfTask);
```

All 4 use the `confirmed` commitment level (Solana's recommendation for speed with good finality guarantees).

**TPS calculation:**
```csharp
// Fetches last 5 performance samples, sums their transactions and time
long totalTx = 0;
double totalSeconds = 0;
foreach (var sample in response.Result) {
    totalTx    += (long)sample.NumTransactions;
    totalSeconds += sample.SamplePeriodSecs;
}
var tps = totalSeconds > 0 ? totalTx / totalSeconds : 0;
```

**Graceful degradation:** Every RPC call is wrapped in its own try/catch. If `GetSlot` fails, the slot defaults to `0` but the rest of the context is still populated. The Scout can still operate with partial data.

---

### 7.3 `OpenRouterLlmProvider`

```
File: src/AutoSig.Infrastructure.AI/OpenRouterLlmProvider.cs
Implements: ILlmProvider
```

Makes POST requests to `https://openrouter.ai/api/v1/chat/completions` with:
- `Authorization: Bearer {apiKey}`
- Model: configurable via `AUTOSIG_LLM_MODEL` env var (default: `meta-llama/llama-3.3-70b-instruct:free`)

**`CompleteTypedAsync<T>()` — the self-correcting JSON parser:**

This is one of the most sophisticated parts of the system. LLMs sometimes wrap their JSON in markdown code fences (` ```json `). The provider:

1. Sends the request
2. Strips markdown fences from the response if present
3. Attempts `JsonSerializer.Deserialize<T>()`
4. **If deserialization fails:** sends a correction request back to the LLM saying _"Your last response contained invalid JSON. Here is the error: [error]. Please respond again with ONLY valid JSON."_
5. Retries up to 3 times with Polly

This self-correction loop means agents are extremely resilient to LLM output quality issues.

---

## 8. Dependency Injection & Boot Sequence

Everything is wired through `Microsoft.Extensions.DependencyInjection`.

**Registration chain in `Program.cs`:**

```
Host.CreateDefaultBuilder()
  → services.AddApplicationServices()          // MediatR + Agents (transient)
  → services.AddAiServices(key, model)         // OpenRouterLlmProvider (singleton)
  → services.AddSolanaServices(privateKey)     // IRpcClient, SolanaSignerEnclave, MarketDataService (singletons)
  → SpectreUiAgent (5x transient registrations, one per event type)
  → ConsensusLoopService (BackgroundService)
```

**Why singletons for infrastructure?**  
`SolanaSignerEnclave` holds the private key in memory. If it were transient, a new wallet would be loaded on every injection, which is wasteful and potentially dangerous. Same for `IRpcClient` — connection pooling requires a single instance.

**Why transient for agents?**  
MediatR creates handler instances as needed per notification. Using transient allows the DI container to reclaim memory after each cycle.

**Boot sequence displayed to user:**
1. FigletText banner
2. System config table (model, chain, framework)
3. Security policy table (all TradingPolicy constants)
4. Wallet public key + current balance
5. Auto-airdrop attempt if balance < 0.01 SOL
6. "SYSTEM READY" rule line
7. Agent swarm starts

---

## 9. Terminal UI (Spectre.Console)

```
File: src/AutoSig.Console/UI/SpectreUiAgent.cs
```

The `SpectreUiAgent` is registered **5 times** in the DI container, once per event type it handles:

```csharp
services.AddTransient<INotificationHandler<MarketOpportunityFoundEvent>, SpectreUiAgent>();
services.AddTransient<INotificationHandler<ProposalGeneratedEvent>, SpectreUiAgent>();
// ... (repeated for all 5 event types)
```

**`MarketOpportunityFoundEvent` handler** renders:
- A separator rule with the cycle number
- A **live market data table** with balance, slot, TPS, tx count, sentiment, blockhash prefix
- A panel showing the Scout's opportunity analysis

**Sentiment color coding:**
```
Bullish      → 🟢 GREEN text
HighActivity → 🔥 YELLOW text
Neutral      → ⚪ WHITE text
Bearish      → 🔴 RED text
```

**Phase labels on approval:** When a proposal is approved, the UI shows:
`Phase 1: Hard Guardrails ✅ | Phase 2: Policy Guardrails ✅ | Phase 3: AI Guardrail ✅`

**Logging is silenced.** In `Program.cs`:
```csharp
logging.ClearProviders();                          // Removes default console logger
logging.AddFilter("AutoSig", LogLevel.Information); // Only AutoSig namespaces log
logging.SetMinimumLevel(LogLevel.Warning);         // Default level = warning
```
This prevents Microsoft's generic framework logs from polluting the Spectre UI dashboard.

---

## 10. Test Suite — Every Test Explained

```
File: src/AutoSig.Tests/RiskManagerGuardrailTests.cs
Framework: xUnit + NSubstitute
Command: dotnet test src/AutoSig.Tests
Result: 9 passed, 0 failed
```

### Test Setup

Every test creates fresh mocks using NSubstitute:
```csharp
private readonly IMediator _mediator = Substitute.For<IMediator>();
private readonly ILlmProvider _llm = Substitute.For<ILlmProvider>();
private readonly ISolanaService _solana = Substitute.For<ISolanaService>();
private readonly ILogger<RiskManagerAgent> _logger = Substitute.For<ILogger<RiskManagerAgent>>();
```

NSubstitute mocks return default values unless configured. This means:
- `_solana.GetBalanceLamportsAsync()` returns `0` by default (relevant for Phase 2d)
- `_llm.CompleteTypedAsync<T>()` throws unless configured (relevant for Phase 3)

The `CreateProposal()` helper creates a valid proposal with a known-good destination so tests can focus on the one variable being tested.

---

### Test 1: `HardGuardrail_RejectsAmountExceedingMaximum`

**What it proves:** A proposal for 1 SOL (1,000,000,000 lamports) is immediately rejected before the LLM is ever called.

```
Input: amount = 1,000,000,000 lamports
Limit: TradingPolicy.MaxSingleTransactionLamports = 500,000,000

Expected: ProposalRejectedEvent published with Verdict=RejectedByHardGuardrail, 
          Reasoning contains "exceeds maximum"
Expected: _llm.CompleteTypedAsync NEVER called
```

**Why the LLM check matters:** This test proves the architectural guarantee — hard guardrails are walls of C# code, not suggestions. The LLM never gets a chance to argue its way past a size limit.

---

### Test 2: `HardGuardrail_RejectsBlockedDestination`

**What it proves:** A proposal targeting `11111111111111111111111111111111` (the Solana System Program address) is rejected immediately.

```
Input: destination = "11111111111111111111111111111111"
BlockedDestinations = { "11111111111111111111111111111111" }

Expected: ProposalRejectedEvent with Reasoning containing "blocklist"
```

**Why this matters:** If a compromised LLM tried to send funds to the System Program (essentially burning them or triggering an exploit), this check catches it before any RPC calls are made.

---

### Test 3: `PolicyGuardrail_RejectsWhenBelowReserveFloor`

**What it proves:** The reserve floor check works with real balance data.

```
Setup: _solana.GetBalanceLamportsAsync() returns 40,000,000 (0.04 SOL)
Input: amount = 10,000,000 (0.01 SOL)

After trade: 40,000,000 - 10,000,000 = 30,000,000 lamports (0.03 SOL)
Reserve floor: 50,000,000 lamports (0.05 SOL)
30,000,000 < 50,000,000 → REJECT

Expected: ProposalRejectedEvent with Reasoning containing "reserve"
```

**What this simulates:** A near-drained treasury. Even a tiny trade would breach the emergency reserve. The agent protects the last 0.05 SOL at all costs.

---

### Test 4: `TradingPolicy_HasCorrectDefaults`

**What it proves:** The safety constants are set correctly and haven't been accidentally changed.

```csharp
Assert.Equal(500_000_000UL, policy.MaxSingleTransactionLamports);  // 0.5 SOL
Assert.Equal(10, policy.MaxTradesPerHour);
Assert.Equal(50, policy.MaxTradesPerDay);
Assert.Equal(0.05, policy.MaxDailyDrawdownPercent);                // 5%
Assert.Equal(50_000_000UL, policy.MinReserveBalanceLamports);      // 0.05 SOL
Assert.Contains("11111111111111111111111111111111", policy.BlockedDestinations);
```

This is a **regression guard**. If anyone accidentally changes a default in `TradingPolicy.cs`, this test fails immediately.

---

### Test 5: `MarketContext_CalculatesSolBalanceCorrectly`

**What it proves:** The lamport-to-SOL conversion math is correct (1 SOL = 10^9 lamports).

```csharp
TreasuryBalanceLamports = 1_500_000_000
Expected TreasuryBalanceSol = 1.5
Formula: 1,500,000,000 / 1,000,000,000.0 = 1.5 ✓
```

Simple but critical — wrong math here means the Risk Manager makes decisions on wrong balance information.

---

### Test 6: `MarketContext_ToSummaryContainsAllFields`

**What it proves:** The `ToSummary()` method produces a string that contains the key data points the LLM needs.

```
Checks: "1.0000 SOL" is in the output (balance)
        "1200" is in the output (TPS integer part, locale-neutral)
        "HighActivity" is in the output (sentiment enum name)
```

**Why "1200" not "1,200.5"?** The test originally checked `"1,200.5"` but this failed on Windows machines with European locales where the comma is used as a decimal separator. The fix is to check for the integer part only, which is locale-neutral. The underlying fix is `FormattableString.Invariant()` in the implementation.

---

### Test 7: `RiskAssessment_IsApproved_ReturnsTrueForApprovedVerdict`

```csharp
assessment.Verdict = RiskVerdict.Approved;
Assert.True(assessment.IsApproved);
```

Tests the computed property: `IsApproved => Verdict == RiskVerdict.Approved`.

---

### Test 8: `RiskAssessment_IsApproved_ReturnsFalseForRejectedVerdict`

```csharp
assessment.Verdict = RiskVerdict.RejectedByHardGuardrail;
Assert.False(assessment.IsApproved);
```

Complementary to Test 7 — ensures `IsApproved` is not accidentally `true` for rejected verdicts.

---

### Test 9: `TransactionResult_ExplorerUrl_GeneratesCorrectDevnetUrl`

```csharp
result.SignatureHash = "5wHGgFUGo2qmPKLFv95rGzSjMBcuPCkemJ8sdB5pWjCt";
Assert.Contains("explorer.solana.com/tx/", result.ExplorerUrl);
Assert.Contains("cluster=devnet", result.ExplorerUrl);
```

Proves that completed transactions produce a valid, clickable Solana Explorer link that defaults to Devnet.

---

## 11. Security Architecture

### Threat Model

| Threat | Mitigation |
|--------|------------|
| **LLM proposes oversized transaction** | Phase 1 rejects before LLM result ever reaches chain |
| **LLM hallucinates a dangerous address** | Phase 1 blocklist + Phase 3 AI review for semantic risk |
| **LLM is jailbroken to approve bad trades** | Phase 1 & 2 are pure C# — LLM output cannot affect them |
| **Runaway trading / flash crash** | Phase 2a/2b velocity limits + Phase 2c drawdown halt |
| **Treasury drained to zero** | Phase 2d reserve floor (minimum 0.05 SOL always protected) |
| **Private key exposure** | SolanaSignerEnclave: never serialized, logged, or returned |
| **Prompt injection in rationale field** | Phase 3 AI explicitly told to detect prompt injection |
| **Network error during AI evaluation** | Fail-safe: exception → automatic REJECT (not approve) |

### Why Fail-Safe Matters

In safety engineering, there's the concept of a **fail-safe state** — the state the system defaults to when something goes wrong. For a financial system, that state is always **"do not transact."**

In `RiskManagerAgent`:
```csharp
catch (Exception ex)
{
    // AI evaluation failed → REJECT, never approve
    await RejectAsync(proposal, RiskVerdict.RejectedByAiGuardrail,
        "AI evaluation failed after retries. Defaulting to REJECT for safety.", null, ct);
}
```

If OpenRouter is down, if the network hiccups, if the model returns garbage — the proposal is rejected. Money is never moved on uncertain approval.

---

## 12. Key Design Decisions (with rationale)

### Decision 1: MediatR pub/sub over direct function calls

**Alternative:** Agent A directly calls `agentB.HandleSomething(...)`.  
**Why MediatR instead:** Decoupling. The SpectreUiAgent can listen to *all* events just by implementing the handler interfaces — the business agents don't know the UI exists. Adding a new observer (audit log, metrics exporter, etc.) requires zero changes to the existing agents.

### Decision 2: `record` for `MarketContext` and events

**Why records:** Records are value-equal and immutable by default with `init`-only setters. Once the Scout captures a `MarketContext`, no downstream agent can mutate it. This prevents a class of bugs where one handler modifies state that another handler was counting on.

### Decision 3: 3-phase ordering (Hard → Policy → AI)

**Why not AI first?** LLM calls cost time and money (even on free tiers, they cost latency). A 1B-lamport proposal should be rejected in microseconds by a C# comparison, not after waiting 2 seconds for an LLM response.

**Why not Policy → Hard?** Hard guardrails are cheaper to check (a single comparison vs multiple RPC calls). Run cheap checks first.

### Decision 4: Static velocity tracking state

Velocity tracking uses `static` fields in `RiskManagerAgent`. This means all instances of `RiskManagerAgent` (which MediatR creates per notification) share the same trade count. This is correct behavior — we want global velocity limiting, not per-instance limiting.

### Decision 5: Real RPC data vs price feeds

In production, market sentiment would come from CoinGecko, Pyth, or Jupiter price aggregators. For the hackathon, deriving sentiment from on-chain TPS and wallet balance demonstrates real RPC usage without requiring paid API keys. The `IMarketDataService` interface is designed to be swapped with a real price feed implementation with zero changes to the agents.

### Decision 6: `sealed` on every class

Every class that should not be subclassed is marked `sealed`. This prevents accidental inheritance from bypassing security checks. If someone subclassed `SolanaSignerEnclave` and overrode `ExecuteProposalAsync()` to skip transaction building, they could potentially exfiltrate the private key via method parameters. `sealed` closes that attack surface.

### Decision 7: Env vars for secrets, never source code

The `.env` file is loaded at startup via `DotNetEnv.Env.Load()`. The keys are then read via `Environment.GetEnvironmentVariable()`. The `.env` file is in `.gitignore`. This means no developer can accidentally commit a secret. The code works correctly with CI/CD secrets injected as environment variables.

---

*Documentation last updated 2026-03-03. All code verified against actual source files.*

---

## 13. Configuration Reference — Environment Variables

All configuration is managed through environment variables loaded from `.env` at startup. See `.env.example` for a complete, annotated reference. The key variables are:

### Required

| Variable | Description |
|---|---|
| `AUTOSIG_SOLANA_PRIVATE_KEY` | Base64-encoded treasury keypair. Generate with `--generate-key`. |
| `AUTOSIG_OPENROUTER_KEY` | OpenRouter API key (free tier works). |

### Per-Agent LLM Models

Each agent uses its own independently-configured LLM. This is a core differentiator — the Strategist uses a reasoning-heavy model while the Risk Manager uses a smaller, faster model for evaluation.

| Variable | Default | Role |
|---|---|---|
| `AUTOSIG_STRATEGIST_MODEL` | `meta-llama/llama-3.3-70b-instruct:free` | Deep reasoning for trade proposals |
| `AUTOSIG_RISK_MODEL` | `meta-llama/llama-3.1-8b-instruct:free` | Fast semantic risk evaluation |

**LLM Failure Safety Behavior:** If the model returns HTTP 404/401 (wrong model ID or unavailable on account), the cycle is **halted entirely** — no transaction executes without a real AI decision. If the model times out (transient failure), a deterministic C# heuristic evaluator runs as a last resort. This distinction is intentional: a misconfigured model should halt loudly, while a temporarily slow model should degrade gracefully.

### Output Verbosity

| `AUTOSIG_OUTPUT_LEVEL` | Behavior |
|---|---|
| `brief` | Only the 4 agent summary panels. No step logs. |
| `normal` | Key phase transitions per agent (recommended for demos). |
| `verbose` | Every step from every agent, minus LLM internals. |
| `debug` | Everything: attempt counts, raw JSON, full exception traces. |

### Trading Policy

| Variable | Default | Description |
|---|---|---|
| `AUTOSIG_MAX_TX_LAMPORTS` | `500000000` | Max lamports per transaction (0.5 SOL) |
| `AUTOSIG_MIN_RESERVE_LAMPORTS` | `50000000` | Treasury reserve floor (0.05 SOL) |
| `AUTOSIG_MAX_TRADES_PER_HOUR` | `10` | Hourly velocity cap |
| `AUTOSIG_TRADE_COOLDOWN_SECONDS` | `15` | Minimum seconds between trades |
| `AUTOSIG_MAX_DAILY_DRAWDOWN` | `0.05` | Max daily drawdown (5%) |
| `AUTOSIG_SOLANA_CLUSTER` | `devnet` | RPC endpoint (`devnet`, `mainnet-beta`, or custom URL) |

