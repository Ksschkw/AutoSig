# AutoSig Agent Skills

This file describes the capabilities of each AI agent in the AutoSig multi-agent treasury system.
Agents use this document to understand the roles and boundaries of their peers.

---

## 🔭 Scout Agent
**Role**: Market Intelligence Gatherer  
**Skills**:
- Queries live Solana Devnet state via RPC (slot height, TPS, recent transactions)
- Monitors treasury wallet balance in real-time
- Derives market sentiment (Bullish, Bearish, Neutral, HighActivity) from on-chain activity patterns
- Supports `AUTOSIG_ENABLE_EXPLORATORY_TRADES` environment variable to pause trading in Neutral markets
- Generates data-driven opportunity descriptions based on actual network state

**Inputs**: Timer-triggered scan interval  
**Outputs**: `MarketOpportunityFoundEvent` with live `MarketContext`

---

## 🧠 Strategist Agent
**Role**: Trade Proposal Generator  
**Skills**:
- Analyzes real market context from the Scout Agent
- Uses LLM to generate structured, typed trade proposals
- Supports SolTransfer, SplTokenTransfer, and SplTokenMint proposal types
- Invents novel token tickers (e.g. `MEME`, `DOGE`) when market conditions are Bullish
- Enforces proportional sizing (max 25% of current treasury balance)
- Self-assesses risk on every proposal

**Inputs**: `MarketOpportunityFoundEvent` with live on-chain data  
**Outputs**: `ProposalGeneratedEvent` with typed `TradeProposal`

---

## 🛡️ Risk Manager Agent
**Role**: Security Gatekeeper (3-Phase Evaluation)  
**Skills**:

### Phase 1: Hard Guardrails (Immutable C# Code)
- Maximum single transaction limit (0.5 SOL)
- Blocked destination address detection

### Phase 2: Policy Guardrails (Deterministic C# Logic)
- Velocity limiting: max trades per hour, minimum cooldown between trades
- Daily drawdown protection: auto-halts if portfolio drops >5%
- Reserve floor: never allows balance to drop below 0.05 SOL
- Real-time balance checking via RPC before approving

### Phase 3: AI Soft Guardrails (Independent LLM Review)
- Independent LLM evaluates proposal for semantic risks
- Detects hallucinated addresses, unreasonable rationales, prompt injection
- Risk scoring (0.0–1.0) with automatic reject at ≥0.6

**Inputs**: `ProposalGeneratedEvent`  
**Outputs**: `ProposalApprovedEvent` or `ProposalRejectedEvent`

---

## 🚀 Executor Agent
**Role**: Blockchain Transaction Submitter  
**Skills**:
- Builds Solana transactions using the Signer Enclave
- Signs transactions with the treasury keypair (key never leaves enclave)
- Submits signed transactions to Solana Devnet
- Returns Solana Explorer links for transaction verification

**Inputs**: `ProposalApprovedEvent` (only fires after consensus)  
**Outputs**: `TransactionCompletedEvent` with signature hash

---

## 🔐 Signer Enclave
**Role**: Cryptographic Key Vault  
**Skills**:
- Stores private key in-memory only (never serialized, logged, or exposed)
- Builds SOL transfer and real SPL Token creation/mint transactions
- Signs transactions using Ed25519
- Requests Devnet airdrops for demo funding

---

## 📊 Market Data Service
**Role**: On-Chain Data Provider  
**Skills**:
- Parallel RPC calls for performance (GetSlot, GetBalance, GetBlockhash, GetPerformanceSamples)
- Integrates with CoinGecko Public API (with Binance fallback) for free, reliable, live SOL/USD prices
- Uses `confirmed` commitment level for optimal speed/safety balance
- Sentiment derivation from TPS and balance patterns
- Graceful degradation when individual RPC calls fail
