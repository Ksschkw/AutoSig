# 🤖 AutoSig — Autonomous Multi-Agent Treasury on Solana

> **DeFi Developer Challenge: Agentic Wallets for AI Agents** | Superteam Nigeria  
> Built with .NET 10, MediatR, Solnet, and OpenRouter AI

AutoSig is a **production-grade autonomous trading system** where AI agents manage a Solana treasury wallet through a multi-agent consensus pipeline. Every trade passes through a **3-phase risk evaluation** before touching the blockchain.

**No human intervention. No hardcoded fake data. Real blockchain. Real AI. Real guardrails.**

---

## 🏗️ Architecture

```
┌────────────────────────────────────────────────────────────────┐
│                    CONSENSUS LOOP (30s)                        │
│                                                                │
│  ┌──────────┐    ┌─────────────┐    ┌──────────────┐    ┌────────────┐
│  │  SCOUT   │ →  │ STRATEGIST  │ →  │ RISK MANAGER │ →  │  EXECUTOR  │
│  │  Agent   │    │   Agent     │    │    Agent     │    │   Agent    │
│  │          │    │             │    │              │    │            │
│  │ Live RPC │    │ LLM-powered │    │ 3-Phase Gate │    │ Signs &    │
│  │ Market   │    │ Proposal    │    │              │    │ Submits to │
│  │ Data     │    │ Generator   │    │ Hard→Policy  │    │ Devnet     │
│  │          │    │             │    │ →AI          │    │            │
│  └──────────┘    └─────────────┘    └──────────────┘    └────────────┘
│                                                                │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │           🔐 SIGNER ENCLAVE (Private Key Vault)         │   │
│  │    Key loaded once at boot. Never serialized or logged. │   │
│  └─────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────┘
```

### Agent Pipeline
| Agent | Role | Data Source |
|-------|------|-------------|
| **Scout** 🔭 | Scans Solana Devnet via RPC for real-time market data | `GetSlot`, `GetBalance`, `GetPerformanceSamples` |
| **Strategist** 🧠 | Feeds live on-chain data to LLM, generates typed trade proposals | OpenRouter (Llama 3.3 70B) |
| **Risk Manager** 🛡️ | 3-phase evaluation: Hard → Policy → AI guardrails | C# code + independent LLM |
| **Executor** 🚀 | Signs and submits approved transactions to Solana Devnet | Solnet + Ed25519 |

---

## 🛡️ Security Model — 3-Phase Risk Evaluation

This is not a toy. Every proposal passes through **three independent layers** of security:

### Phase 1: Hard Guardrails (Immutable C# Code)
- ❌ Max 0.5 SOL per transaction — **cannot be changed at runtime**
- ❌ Blocked destination addresses — System Program blacklisted

### Phase 2: Policy Guardrails (Deterministic Logic)
- ⏱️ **Velocity Limits**: Max 10 trades/hour, 15s cooldown between trades
- 📉 **Drawdown Protection**: Auto-halt if daily loss exceeds 5%
- 💰 **Reserve Floor**: Never drain below 0.05 SOL
- 📊 **Real-time Balance Check**: Queries chain before every approval

### Phase 3: AI Soft Guardrails (Independent LLM Review)
- 🤖 Separate LLM instance evaluates the Strategist's proposal
- 🔍 Detects hallucinated addresses, prompt injection, logic flaws
- 📊 Risk scoring 0.0–1.0 with auto-reject at ≥0.6

**Key Insight**: Even if the AI is compromised, Phases 1 and 2 are pure C# code that cannot be bypassed by any LLM output.

---

## 🚀 Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An [OpenRouter](https://openrouter.ai/) API key (free tier available)

### 1. Clone & Setup
```bash
git clone https://github.com/Ksschkw/AutoSig.git
cd AutoSig
```

### 2. Generate a Wallet
```bash
dotnet run --project src/AutoSig.Console -- --generate-key
```
This generates a new Solana keypair. Copy the output to your `.env` file.

### 3. Configure Environment
Create a `.env` file in the project root:
```env
AUTOSIG_SOLANA_PRIVATE_KEY=<your base64 private key>
AUTOSIG_OPENROUTER_KEY=<your openrouter api key>
AUTOSIG_LLM_MODEL=meta-llama/llama-3.3-70b-instruct:free
```

### 4. Fund Your Wallet (Free — Devnet)
Go to [faucet.solana.com](https://faucet.solana.com/) and airdrop Devnet SOL to your public key. AutoSig also auto-airdrops if your balance is low.

### 5. Run
```bash
dotnet run --project src/AutoSig.Console
```

### 6. Run Tests
```bash
dotnet test src/AutoSig.Tests
```

---

## 📁 Project Structure

```
AutoSig/
├── src/
│   ├── AutoSig.Domain/              # Core models, events, interfaces
│   │   ├── Events/AgentEvents.cs     # MediatR event contracts
│   │   ├── Interfaces/              
│   │   │   ├── ILlmProvider.cs       # AI provider contract
│   │   │   ├── ISolanaService.cs     # Blockchain service contract
│   │   │   └── IMarketDataService.cs # Market data contract
│   │   └── Models/
│   │       ├── MarketContext.cs       # Live on-chain state snapshot
│   │       ├── TradingPolicy.cs       # Velocity/drawdown/reserve limits
│   │       ├── TradeProposal.cs       # Typed trade proposal
│   │       ├── RiskAssessment.cs      # Risk evaluation result
│   │       └── TransactionResult.cs   # On-chain execution result
│   │
│   ├── AutoSig.Application/          # Agent logic (Clean Architecture)
│   │   └── Agents/
│   │       ├── ScoutAgent.cs          # Real-time RPC market scanner
│   │       ├── StrategistAgent.cs     # LLM-powered trade generator
│   │       ├── RiskManagerAgent.cs    # 3-phase risk gatekeeper
│   │       └── ExecutorAgent.cs       # Solana transaction submitter
│   │
│   ├── AutoSig.Infrastructure.AI/     # OpenRouter LLM integration
│   ├── AutoSig.Infrastructure.Solana/ # Solnet RPC + Signer Enclave
│   │   ├── SolanaSignerEnclave.cs     # Private key vault
│   │   └── MarketDataService.cs       # Live RPC data fetcher
│   │
│   ├── AutoSig.Console/              # Terminal UI (Spectre.Console)
│   │   ├── Program.cs                # Boot sequence + policy display
│   │   ├── Services/ConsensusLoopService.cs
│   │   └── UI/SpectreUiAgent.cs       # Real-time dashboard
│   │
│   └── AutoSig.Tests/                # xUnit test suite
│       └── RiskManagerGuardrailTests.cs
│
├── SKILLS.md                          # Agent capability manifest
├── .env                               # Environment secrets (gitignored)
└── AutoSig.slnx                       # Solution file
```

---

## 🔬 Testing

The test suite **proves** the guardrails cannot be bypassed:

| Test | What It Proves |
|------|---------------|
| `HardGuardrail_RejectsAmountExceedingMaximum` | LLM cannot approve >0.5 SOL |
| `HardGuardrail_RejectsBlockedDestination` | LLM cannot send to blacklisted addresses |
| `PolicyGuardrail_RejectsWhenBelowReserveFloor` | Treasury always keeps minimum reserve |
| `TradingPolicy_HasCorrectDefaults` | Policy constants are correct |
| `MarketContext_CalculatesSolBalanceCorrectly` | SOL math is accurate |
| `RiskAssessment_IsApproved_*` | Verdict logic works correctly |

---

## 🧰 Technologies

| Component | Technology |
|-----------|------------|
| Runtime | .NET 10 |
| Blockchain | Solana (Devnet) via Solnet |
| AI/LLM | OpenRouter (Llama 3.3 70B — free tier) |
| Event Pipeline | MediatR (pub/sub notifications) |
| Terminal UI | Spectre.Console |
| HTTP Resilience | Polly (self-healing JSON retry) |
| Testing | xUnit + NSubstitute |
| Key Management | In-memory Ed25519 (Signer Enclave) |

---

## 📜 License

MIT License. Built for the Superteam Nigeria DeFi Developer Challenge.