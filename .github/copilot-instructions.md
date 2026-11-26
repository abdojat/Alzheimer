# GitHub Copilot instructions — Alzheimer

Before any change, read `AGENTS.md`, `docs/Alzheimer_Card_Game_Rules_v1.1.md` for rules work, and `docs/DEVELOPMENT_ROADMAP.md`.

**Project:** Custom Alzheimer multiplayer card game, Android-first. Unity 6 client, pure .NET 10 C# core, ASP.NET Core 10 + SignalR authoritative gameplay server. P3A is server-authoritative; P3B-1 Unity networking/lobby/basic gameplay source is present but is not yet verified in the Unity Editor, on Android IL2CPP, or with two Unity clients.

- Preserve the custom rules exactly. Never infer game behavior from other card games. Reaction matching is **rank only**; Queen has a persistent two-rank exception.
- Server authoritative; no leaking hidden card identities or using client timers to decide game state.
- Keep P3A SignalR commands versioned and allowlisted; map the actor from room connection membership and send private projections only to that player's connection. Active-match disconnect abandons the room; there is no reconnect support.
- Keep pure game rules out of Unity MonoBehaviours. Implement and test deterministic state transitions, final turns, scoring, and match progression in `core`; call them from `server`, and render only authorized projections in Unity.
- Make one well-scoped working change at a time; include tests and verify them. Report what was *actually* run, what was not run, and why.
- Do not fabricate a Unity build pass, Android APK, SignalR integration, or gameplay completion.
- Prefer minimal, dependency-conscious changes. Protect Unity `.meta` files, asset references, project settings, and scenes.
- Follow the scoped guidance in `.github/instructions/` for Unity vs server/rules code.
- The owner reports a successful Android build/install/launch on a physical phone. This is not evidence that Unity automated tests, repeated scene navigation, or touch-input validation passed; check `docs/DEVELOPMENT_ROADMAP.md` and report those separately.
- Update documentation and checklists as milestones are verified. Keep the canonical rulebook under source control.
