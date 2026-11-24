---
applyTo: "tests/**"
---
# Testing instructions

Use deterministic, easy-to-run tests. Keep the existing no-external-test-framework smoke runner passing while adding stronger scenario coverage (an isolated test project is acceptable if justified).

Required rule scenario matrix as engine functionality is added:

- Every deck count 1–4; 54 cards per deck, two Jokers per deck, unique physical IDs; rooms 2–10 players.
- Host leads clockwise; initial four cards each, first two revealed only to owner.
- Mandatory draw; keep/replace/discard for all card ranks, including J/Q/K; newly placed hand cards face down; correct private knowledge.
- Jack peek, Queen discard, King blind swap; ability timeout, unavailable target, ability-card disposal and non-activation during reactions.
- Post-resolution manual call only, no second caller, no call during abilities/reaction; automatic empty-hand call from own turn and another player's turn.
- Ordinary rank-only reactions; successful chains, unknown-card attempts, simultaneous requests, irreversible Done, 30-second cutoff.
- Queen persistent two-rank window and final top card; Queen-caused empty hand cancels reaction window.
- Invalid reaction: attempted card returns and becomes known, immediate unknown penalty card; +10 if no draw; depletion mid-window does not cancel remaining reactions.
- Final normal draw/penalty draw exhaustion; eligible last-card Alzheimer call; final reaction window; caller vs no-caller score doubling.
- Penalties before doubling, caller unique lowest vs tied lowest vs loser, accumulating totals across rounds, stop at >=100, tied lowest match winners, deck reset.
- Unauthorized attempts (another player's hand, stale phase, duplicate actions), private-state redaction, and exact timing at phase deadlines.

For each completed milestone report tests **added**, **run**, **passed/failed**, and scenarios still untested.
