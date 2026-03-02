# AutoSig: Production-Grade Agentic Wallet Architecture Plan

To win this hackathon and build something engineers respect, we are moving from a "simulation" to a **Real-World Agentic Wallet**. We will maximize the provided documentation (Kora, RPC, Wallets) and apply strict Software Development Life Cycle (SDLC) principles.

## Proposed Changes

### 1. Integration of Real Market Data & RPC Optimization (The Scout & Strategist)
Currently, `ScoutAgent` uses random strings. We will upgrade it to read actual blockchain state.
- **Solana RPC Integration**: Implementing the `solana.com/docs/rpc` guidelines.
  - The Scout will query recent blocks and token balances using `confirmed` commitment for speed.
  - The Agent will use external data (e.g., fetching real token prices or extracting testnet state) to give the Strategist actual context to make *logical* trades rather than random ones.

### 2. Kora Paymaster & Gasless Transactions (The Executor)
Agentic wallets shouldn't need to manage tiny amounts of SOL just to pay gas fees.
- **Kora Integration**: Following `launch.solana.com/docs/kora/operators`.
  - We will redesign the `ExecutorAgent` and `SolanaSignerEnclave` to format transactions that can be signed by a Paymaster.
  - The agent will act as the "user", while a configured Kora Paymaster (or a local relayer simulation) will sponsor the fee. This demonstrates **Feeless Transactions** as highlighted in the SDK docs.

### 3. Ethics, AI Safety & Hard Guardrails (The Risk Manager)
We will expand the `RiskManagerAgent` beyond just a max lamport check to include enterprise-grade safety parameters:
- **Velocity Limits**: The bot cannot trade more than X times per hour.
- **Drawdown Limits**: If the portfolio drops by >5% in a day, the system enters an emergency `Halt` state.
- **Semantic Whitelisting**: The agent can only interact with verified protocols (e.g., a specific Devnet DEX or Vault).

### 4. SDLC: Testing & CI (The Proof)
An engineer's work is proven by tests.
- **Unit Testing**: We will create an automated test suite (`AutoSig.Tests`) that actively tries to execute malicious transactions (e.g., trying to drain the wallet, exceeding the velocity limit) to prove that the C# Hard Guardrails successfully block the LLM.

## Verification Plan

### Automated Tests
- Run `dotnet test` to execute the Unit Test suite verifying the Risk Manager's logic.
- Verify `SolanaSignerEnclave` correctly builds transactions with the required signatures.

### Manual Verification
- Run the console application in "Live Devnet" mode.
- Observe the agent reading real network state, analyzing market conditions, and autonomously executing a trade.
- Verify the transaction on the Solscan Devnet Block Explorer, checking that the Commitment levels and Paymaster logic behave as expected.
