# AutoSig Production Upgrade Tasks

- [ ] **Phase 1: Real-World Data Integration (The "Eyes")**
  - [ ] Update `ScoutAgent` to fetch real Solana on-chain data via RPC (token balances, recent activity).
  - [ ] Integrate a mock or real pricing/news feed so the Strategist bases decisions on actual market context.
- [ ] **Phase 2: Kora Paymaster & Advanced Wallet (The "Hands")**
  - [ ] Refactor `SolanaSignerEnclave` to support sponsored transactions (gasless execution) conceptually aligned with Kora.
  - [ ] Implement explicit Commitment level configurations based on Solana RPC docs (e.g., `confirmed` vs `finalized`).
- [ ] **Phase 3: Ethics & Hardened Guardrails (The "Shield")**
  - [ ] Add explicit Daily Drawdown limits and Time-based throttling to `RiskManagerAgent`.
  - [ ] Define a strict whitelist of programs the agent is allowed to interact with.
- [ ] **Phase 4: SDLC & Testing (The "Proof")**
  - [ ] Create an `xUnit` test project to mathematically prove the guardrails cannot be bypassed.
  - [ ] Ensure end-to-end flow executes a real transaction on Devnet.
  - [ ] Write a comprehensive technical `README.md` and `SKILLS.md`.
