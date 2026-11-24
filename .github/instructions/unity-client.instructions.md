---
applyTo: "client/Alzheimer.Client/**"
---
# Unity Android client instructions

Target the exact editor version in `ProjectSettings/ProjectVersion.txt` (checked-in version is `6000.6.10f1`, Unity 6.6), not an older version mentioned in the README. Android is the primary target; portrait UI; uGUI Canvas Scaler 1080x1920; safe-area-aware and touch-friendly; URP 2D is already provisioned.

## P0 issues to investigate and fix

1. `Packages/manifest.json` currently does not declare `com.unity.inputsystem`; `Assets/Scripts/UI/SceneBootstrap.cs` explicitly adds `StandaloneInputModule` after destroying all existing input modules. Inspect `ProjectSettings/ProjectSettings.asset` and choose **one valid input configuration**. Preferred long-term: install a Unity-compatible Input System package and use `InputSystemUIInputModule` with correct UI actions; update any runtime bootstraps and editor scene generators. Do not merely suppress errors or set `Both` without understanding dependencies.
2. `SceneBootstrap` builds UI in a one-time `RuntimeInitializeOnLoadMethod(AfterSceneLoad)` callback. Confirm whether it recreates the appropriate UI on later `SceneManager.LoadScene` calls; if not, use a scene-loaded lifecycle subscription with safe unsubscription / deduplication. Ensure `MainMenu -> GameTable -> MainMenu` works repeatedly and exactly one active EventSystem and scene UI canvas exist.
3. Keep `MainMenu` and `GameTable` scenes and their `.meta` GUIDs intact. Avoid uncontrolled regenerations that discard scene edits.
4. The prototype currently displays four selectable card placeholders; do not imply full game actions are implemented. `NetworkManager.Connect` reports that transport is not configured. Do not implement pretend multiplayer.
5. Preserve Android build configuration: IL2CPP, ARM64, portrait orientation, min API 26. Do not claim Play Store readiness from an unsigned development APK. Confirm the active graphics/quality URP 2D assets if touched.

## Implementation standards

- View models should contain only the hidden-card information authorized for the local player. Never store/distribute the full deck in client-visible network state.
- Keep UI rendering, user interactions, network transport and game state contracts separate; no authoritative validation inside UI button handlers.
- Avoid `UnityEngine.Input` legacy calls if migrating to the new Input System. The UI must respond to Editor mouse and Android touch.
- Use Unity Editor APIs for generating/modifying scenes, rather than manually constructing `.unity` YAML. Preserve `.meta` files.
- Verify portrait 9:16, layout safe areas, MainMenu Play/Back/navigation, card selection and meaningful UI statuses.
- Use Android-compatible libraries (IL2CPP/AOT constraints); don't assume ASP.NET's full SignalR client works in Unity without validating.
- Test in Editor and build APK if Unity is available; if not, list exact manual Unity checks without claiming success.
