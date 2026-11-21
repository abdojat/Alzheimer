# Alzheimer — Card Game Rules

**Version:** 1.1 (working draft)  
**Updated:** 2026-11-21  
**Status:** Consolidated from the player's explanations. Later corrections override earlier draft rules. The five follow-up questions after v1.0 are resolved in this version.

> This is the custom **Alzheimer** card game described by the player, not a generic Cabo/Cambio ruleset. Later clarifications override earlier statements. In particular, reaction throws match **rank only, not suit**.

## 1. Objective and match structure

- Players try to **minimize the sum of their card values**.
- A **round** ends when either:
  1. A player **calls “Alzheimer”** after resolving a drawn card, or a player **automatically calls by reaching an empty hand**, and the required final turns finish; or
  2. The **draw pile is exhausted**, after the current card resolution and applicable reaction window finish, whether depletion came from a normal draw or a penalty draw.
- At the end of each round, players reveal their cards and calculate their round scores. If there is an Alzheimer caller, the **caller-specific doubling rule** applies; if the draw pile ends the round **without a caller, every player’s round score is doubled** (Section 11).
- **Round scores accumulate across rounds**. When **any player's cumulative score reaches or exceeds 100**, the **match ends**.
- The player with the **lowest cumulative score** wins the match. If multiple players are tied for the lowest cumulative score, **all tied players win**.

## 2. Players and decks

| Setting | Rule |
|---|---|
| Players | **2–10** |
| Decks | Host chooses **1–4 decks** |
| Cards per deck | **54**: 52 standard playing cards + 2 Jokers |
| Starting hand | **4 cards per player** |
| Normal-turn draw | **Mandatory** |
| Ability window | **30 seconds**, when activated |
| Reaction window | **30 seconds** after the drawn card is resolved |

- **The host starts every round**, and play always proceeds **clockwise**.
- All selected decks are combined and shuffled for play. **Before each new round, reset and reshuffle the selected decks and deal four fresh cards to each player.**
- There is **no fixed turn limit**. A round can continue for a long time if nobody calls Alzheimer and cards remain in the draw pile.
- Hands are **not limited to four cards** once play starts: penalties can add cards, while successful reaction throws and Queen abilities can remove them.

## 3. Initial deal and hidden information

1. Every player receives **four face-down cards**.
2. Each player is allowed to look at **two of their own cards** at the start.
3. The other two cards begin **unknown** to that player.
4. Players must remember what they have learned about their cards and positions.
5. Cards in a player's hand are kept **face down**, even when the owner knows their identities.
6. A card received through a **King swap is unknown** to the receiving player, even if they previously knew which card they gave away. They can learn the received card later via a Jack peek.
7. When a newly drawn card replaces a card in the player's hand, the **newly placed card is always face down**.
8. Any card placed onto the discard pile is **face up**.

*“Known” and “unknown” describe the information a particular player has, not whether the game engine knows the card. The game engine tracks every actual card.*

## 4. Card values

| Card rank | Score |
|---|---:|
| Joker | **−1** |
| Ace (A) | **1** |
| 2–9 | Printed number |
| 10 | **0** |
| Jack (J) | **20** |
| Queen (Q) | **20** |
| King (K) | **20** |

Suits do not affect card values.

## 5. Normal turn — mandatory draw and resolution

**Every normal turn must include drawing one card from the draw pile.** A player **cannot manually call Alzheimer before drawing or before fully resolving that card**.

1. The active player **draws one card** and **looks at it**.
2. They resolve the drawn card:
   - **Any drawn card, including J/Q/K, may be kept:** put it **face down** into a chosen hand position, and discard the replaced hand card **face up**.
   - **Any drawn card may instead be discarded directly**, face up, without replacing a hand card.
   - If the drawn card is a **Jack, Queen, or King**, the player may instead **activate its ability** (Section 6). If its ability has **no available target**, the player is asked whether to **keep** the drawn card or **discard** it; the ability does not activate.
3. **After the drawn card has been fully resolved**, but **before** starting the reaction window, the active player is asked: **“Call Alzheimer” or “End turn”**. This is the **only time for a voluntary Alzheimer call** (Section 9). It is not offered during the ability window or reaction window. If an Alzheimer call is already active, no new call is possible.
   - **Call Alzheimer:** the player's turn ends immediately; **do not start** the reaction window; enter the Alzheimer final phase.
   - **End turn:** start the normal **30-second reaction window** (Section 7), unless matching throws are already disabled by an earlier call.
4. Once the reaction window ends, play advances to the next player, unless the round has ended. The reaction window also occurs when the **last draw-pile card** was drawn and resolved, provided no Alzheimer call has disabled reactions.

**Automatic exception:** If a player reaches **zero cards**, this counts as an Alzheimer call without requiring the voluntary call prompt (Section 9). If it happens during another player's turn, that active player's turn is not interrupted and the final-turn accounting follows the empty-hand rule. **If a Queen ability empties the player's hand, the automatic call immediately cancels the upcoming reaction window.**

The ability window and reaction window are **different phases**, each with its own 30-second timer. No ability window occurs if the player keeps or discards the ability card without activating it.

## 6. Special cards and the ability window

| Drawn card | Ability |
|---|---|
| **Jack** | Peek at **one of your own hand cards**. |
| **Queen** | Throw away **one of your own hand cards**. |
| **King** | **Blindly swap any one of your hand cards (known or unknown) with any one card belonging to an opponent.** Neither player looks at the exchanged cards as part of the swap. The card you receive is **unknown** until you use a Jack to peek at it. |

### Activating or declining an ability

- When a J/Q/K is drawn, its owner may **keep it, discard it without an effect, or activate its ability**.
- If the player activates the ability, a **30-second ability window** opens for them to complete it.
- If the ability is completed, the **used J/Q/K is discarded face up**.
- If the ability is **not completed within 30 seconds**, the ability **does not happen** and the J/Q/K is **discarded face up without effect**.
- **Unavailable target:** If the ability cannot be used because there is no valid target (e.g. no hand card to remove or no opponent card to swap), ask the player whether to **keep** or **discard** the drawn special card. There is no effect.
- After the card is resolved, the active player gets the **Call Alzheimer / End turn** decision if no call is already active. The **30-second reaction window** starts only if they end their turn without calling and reactions are still enabled.
- **Alzheimer cannot be called during an ability window.**
- **Only J/Q/K cards drawn during a normal turn can activate an ability.** A Jack, Queen, or King thrown from a player's hand during a reaction window activates **no ability**. Merely discarding a J/Q/K from the hand (including via a Queen) likewise **does not activate** it.

### Special Queen reaction rule

When a player **activates a Queen** and uses it to discard one of their own cards:

1. The **Queen** and the **selected hand card** are discarded face up.
2. During the ensuing reaction window, players may throw cards matching the **Queen's rank** or the **rank of the selected card**. For example, if the Queen throws a 7, **Queens and 7s** are eligible matches. **Both ranks remain valid throughout the entire reaction window**, regardless of other players' successful throws.
3. At the **end of that reaction window**, the **card discarded by the Queen's ability becomes the top card of the discard pile**.
4. If the Queen-discarded hand card is another J/Q/K, its ability **does not activate**.
5. **If the Queen ability leaves its user with an empty hand, this immediately counts as an Alzheimer call.** The upcoming reaction window is **canceled** (so the two-rank matching exception is not opened), and the final-call rules apply.


## 7. Reaction window — simultaneous matching throws

After a drawn card is resolved **and the active player chooses “End turn” instead of calling Alzheimer**, a **30-second reaction window** begins, unless a prior automatic call has disabled throws:

- **All players** can throw during this window, not just the active player.
- A player can throw **as many cards as they want**, provided each attempt is valid.
- A thrown card must match the applicable discard-card target by **rank only**. **Suit never counts** as a match.
- In an ordinary reaction window, the matching target is the **current top card of the face-up discard pile**. A successful throw ordinarily becomes the new top card.
- The **Queen ability exception** permits matching either the Queen or the card thrown with it **for the entire reaction window**. Neither valid rank changes when anyone throws. At the end of the window, the card thrown by the Queen's ability becomes the top of the discard pile (Section 6).
- Players may attempt to throw **cards whose identities they do not know**. Lucky correct guesses are valid; wrong guesses are penalized.
- Successful throws **do not restart** the 30-second timer.
- Each player may indicate **“Done.”** Once they do, **they cannot resume throwing during that reaction window**.
- The window ends when **all players have indicated Done** or **30 seconds have elapsed**, whichever occurs first.
- There is **no special gameplay rule for the order of simultaneous/conflicting throws**. The implementation must still resolve events consistently, but no particular player gets priority by rule.

### Examples

- Top card **7♥**: **7♣** is a valid throw; **9♥** is not.
- Top card **K♠**: **K♥** is valid, regardless of suit.
- Queen ability discarded a **7**: during that entire special reaction window, **Q** and **7** are both valid reaction ranks, no matter which cards others throw; when the window closes, the Queen-discarded **7** becomes the pile's top card.

## 8. Incorrect reaction throw and penalty

If a player attempts to throw a hand card that does **not** match the permitted target:

1. The invalid card **returns to that player's hand**.
2. The player now **knows that card's identity/value**.
3. The player **immediately draws one extra penalty card** from the draw pile.
4. The penalty card is added to their hand **face down and unknown** to them.
5. The entire penalty is **resolved immediately**, even while the reaction timer is still running.

An incorrect throw therefore generally increases the player's hand size by one.

**If a penalty card is required but the draw pile is empty, no penalty card can be drawn; instead, add +10 penalty points to that player's score for the round.** This bonus penalty is separate from the values of cards remaining in their hand.

**If a penalty draw takes the last card from the draw pile during a reaction window, the reaction window continues normally** (players can keep throwing until the timer ends or everyone marks Done). The round ends once the window closes and any immediate penalties are resolved.

## 9. Calling “Alzheimer” and the final phase

### 9.1 Voluntary call — exact timing

- **Only the active player** may voluntarily call Alzheimer.
- They must **draw and fully resolve their mandatory card first** (keep it, discard it, replace a hand card, or complete/timeout its special ability).
- **Immediately after card resolution and before the reaction window**, the game explicitly asks: **“Call Alzheimer” or “End turn.”**
- Choosing **“Call Alzheimer”** ends the caller's turn immediately and **skips that turn's reaction window**.
- Choosing **“End turn”** starts the normal 30-second reaction window. **They cannot call later during that reaction window.**
- Voluntary calls are **not allowed before drawing, while deciding/resolving the card, during the 30-second ability window, during a reaction window, or during another player's turn**.

### 9.2 Automatic call — empty hand

- **Reaching zero hand cards automatically counts as an Alzheimer call**, even if it happens during **another player's** turn. No manual call prompt is needed. **If the last card is discarded using an activated Queen, the upcoming reaction window is canceled immediately.**
- An empty-hand call **does not immediately interrupt the other player's turn** if that player is currently active. Matching-card throwing is disabled from the time the empty-hand call takes effect.
- **Every other player must play one turn after the empty-hand event**, excluding the empty-handed caller. The active player's already-in-progress turn is not considered their subsequent turn if the empty-hand event occurs during it.
- The round ends **once each other player has had that subsequent turn**, or earlier if the draw pile ends under the depletion rule. The empty-handed player does not take another turn.
- If an empty-hand event happens after somebody else has already called Alzheimer, **it does not create a second caller**.

**Example — empty hand outside one's turn:** In turn order **A → B → C → D**, if **B** empties their hand during **A's** reaction phase, **A's current turn is not interrupted**. Reaction throws stop. After the event, **C**, **D**, and **A** each receive their final normal turn; **B** is skipped. Then the round ends. This ensures every non-caller gets a turn *after* the empty-hand event.

### 9.3 General final-phase restrictions

- **There is only one Alzheimer caller per round.** After the first manual or automatic call, no subsequent player may call, and subsequent empty hands do not replace the original caller.
- The caller **receives no more turns** that round.
- **Matching-card reaction throws stop immediately** and remain disabled for the rest of the round.
- **J/Q/K special abilities remain available** to players on their remaining mandatory-draw turns (with their ordinary 30-second ability window where applicable).
- **If the active player voluntarily calls at their post-resolution prompt**, their turn is immediately over, and the other players take their remaining final turns until play would return to that caller.
- **If an empty-hand call happens during someone else's turn**, follow Section 9.2's explicit requirement that *each* other player plays one subsequent turn; do not just stop at the empty-handed player's next seat.
- **If the draw pile runs out earlier**, apply the round-ending timing in Section 10; scoring still follows the caller-specific rule when a caller exists (Section 11).

**Example — voluntary call:** With **A → B → C → D → A**, A draws and resolves a card, then chooses **Call Alzheimer** instead of starting the reaction window. **B**, **C**, and **D** each take one last turn (no matching reactions), then the round ends before A plays again.

## 10. Empty draw pile — round ends

- When a player **draws the last card** in the draw pile, they **resolve that drawn card first** (discard, replace, or activate its ability as allowed), then get the ordinary post-resolution **Call Alzheimer / End turn** prompt if there is no caller yet. **If they call Alzheimer at this prompt, caller-specific scoring applies, not the all-players-doubled no-caller depletion rule.**
- The draw pile can also become empty because an **incorrect reaction throw requires the final available penalty card**.
- After a last-card normal draw, the **30-second reaction window still occurs if the player chooses “End turn” and no call has disabled it**, subject to the usual early ending when everyone marks Done. If the final card is taken as a **penalty** during an already-running reaction window, **that window continues**; players can keep throwing. If the player calls Alzheimer at the prompt, matching reactions stop immediately.
- **After the applicable reaction window closes (or immediately after card resolution if reactions are disabled), the round ends** when the draw pile is exhausted. This can end a round before the caller-specific final turns finish.
- At round end, all players' card values and any applicable **+10 undrawn-penalty points** are included in that round's scoring. **If there is no Alzheimer caller, all players' base round scores are doubled; if there is already a caller (including one who called after drawing the last card), use the caller-specific doubling rule.**
- If an incorrect reaction throw during this final window requires a penalty card, the draw pile is already empty, so the player receives **+10 points** instead of a card.

## 11. Scoring across rounds and Alzheimer doubling

1. At the end of a round, reveal every player's hand.
2. Compute each player's **base round score** as the sum of their remaining card values plus any **+10 undrawn-penalty points** incurred when the draw pile was empty. **An empty hand contributes 0 card points**, but any separate +10 penalties still apply.
3. **If there is an Alzheimer caller** (manual call or automatic empty-hand call), apply the following rule to the **entire base round scores, including any +10 penalty points**:
   - **Caller is the unique lowest-scoring player:** Double **every other player's** round score; the caller's round score is unchanged.
   - **Caller is not the unique lowest-scoring player** (including a **tie for lowest**): Double **the caller's** round score; all other players' round scores are unchanged.
4. **If there is no Alzheimer caller and the round ends because the draw pile is exhausted:** Double **every player's base round score**, including any +10 undrawn-penalty points.
5. Add the resulting adjusted round scores to each player's **cumulative match score**.
6. If **any cumulative score reaches or exceeds 100**, the match ends.
7. The player (or all players tied) with the **lowest cumulative score** wins the match.
8. Otherwise, reset and reshuffle the chosen decks and begin the next round.

**Example A — caller wins uniquely:** A calls Alzheimer. Base round scores are A = 3, B = 8, C = 12. Adjusted round scores are **A = 3, B = 16, C = 24**.

**Example B — caller does not win uniquely:** A calls Alzheimer. Base round scores are A = 7, B = 7, C = 15. A tied for lowest, so the caller is **not** uniquely lowest. Adjusted round scores are **A = 14, B = 7, C = 15**.

**Example C — match tie:** After adding adjusted round scores, cumulative scores are A = 105, B = 72, C = 72, D = 90. The match ends and **B and C both win**.

**Example D — draw pile ends, no caller:** Base round scores are A = 3, B = 8, C = 12. All players are doubled: **A = 6, B = 16, C = 24**.

## 12. Clarifications closed in v1.1

Five further implementation questions have been answered:

1. **Special cards thrown during reactions:** Their abilities do **not** activate. Abilities may be activated only on J/Q/K cards drawn in a normal turn.
2. **King swap:** The player may choose any of their own cards, known or unknown, and any opponent card. The incoming card is **unknown** to its recipient until revealed by Jack.
3. **Queen causing an empty hand:** The automatic Alzheimer call **cancels the upcoming reaction window** immediately.
4. **Last draw + manual Alzheimer call:** **Caller-specific scoring** applies, not the no-caller depletion doubling rule.
5. **Turn order:** **Host starts each round; turns always proceed clockwise.**

Earlier v1.0 clarifications remain in force: a voluntary call is offered only after resolving the drawn card and before reactions; empty-hand calls are automatic; only one caller may exist; penalty points are counted before doubling; and empty hands have zero card points aside from applicable penalties.

---

*Draft v1.1. Confirmed rules only; further edge cases can be documented as they arise.*
