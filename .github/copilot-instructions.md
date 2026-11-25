# GitHub Copilot instructions — Alzheimer

Before any change, read `AGENTS.md`, `docs/Alzheimer_Card_Game_Rules_v1.1.md` for rules work, and `docs/DEVELOPMENT_ROADMAP.md`.

**Project:** Custom Alzheimer multiplayer card game, Android-first. Unity 6 client, pure .NET 10 C# core, ASP.NET Core 10 + SignalR server. The server currently implements only lobbies, and the Unity client is a UI preview, not an online game.

- Preserve the custom rules exactly. Never infer game behavior from other card games. Reaction matching is **rank only**; Queen has a persistent two-rank exception.
- Server authoritative; no leaking hidden card identities or using client timers to decide game state.
- Keep pure game rules out of Unity MonoBehaviours. Implement and test deterministic state transitions in `core`, call them from `server`, and render only authorized projections in Unity.
- Make one well-scoped working change at a time; include tests and verify them. Report what was *actually* run, what was not run, and why.
- Do not fabricate a Unity build pass, Android APK, SignalR integration, or gameplay completion.
- Prefer minimal, dependency-conscious changes. Protect Unity `.meta` files, asset references, project settings, and scenes.
- Follow the scoped guidance in `.github/instructions/` for Unity vs server/rules code.
- The owner reports a successful Android build/install/launch on a physical phone. This is not evidence that Unity automated tests, repeated scene navigation, or touch-input validation passed; check `docs/DEVELOPMENT_ROADMAP.md` and report those separately.
- Update documentation and checklists as milestones are verified. Keep the canonical rulebook under source control.
