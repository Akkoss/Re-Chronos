# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Re-Chronos is a Unity 6 (6000.5.0f1) game project built on the Universal Render Pipeline (URP). The name suggests time-manipulation mechanics. The project is in early development — the engine skeleton is configured but custom game code is largely empty.

## Build & Development

This is a Unity project with no external build tooling. All operations go through the Unity Editor or CLI.

**Open the project:** Open Unity Hub → Add project → select this folder.

**Build from CLI (Unity batch mode):**
```bash
# Build Windows standalone
"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" -batchmode -quit -projectPath . -buildTarget StandaloneWindows64 -executeMethod BuildScript.Build
```

**Run tests (Unity Test Framework):**
```bash
"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" -batchmode -runTests -projectPath . -testResults results.xml -testPlatform EditMode
```

**C# compilation** is handled by the Unity Editor (not dotnet CLI). Use Rider or Visual Studio via `Re-Chronos.sln`.

## Architecture

### Code Organization

All custom game code lives under `Assets/_Project/`:
- `Scripts/` — MonoBehaviours and game logic (currently empty)
- `Prefabs/` — reusable GameObjects (currently empty)
- `Items/` — game item assets (currently empty)

Unity boilerplate (tutorial assets, URP readme) lives under `Assets/TutorialInfo/` — avoid editing those.

### Rendering

Two URP render pipeline assets are configured for platform scaling:
- `Assets/Settings/PC_RPAsset.asset` + `PC_Renderer.asset` — high-quality PC
- `Assets/Settings/Mobile_RPAsset.asset` + `Mobile_Renderer.asset` — optimized mobile

Post-processing via `Assets/Settings/DefaultVolumeProfile.asset`. Shader effects should use Shader Graph (v17.5.0), not legacy shaders.

### Input System

The project uses Unity's **new Input System** (v1.19.0), not the legacy `Input` class. Action bindings are defined in `Assets/InputSystem_Actions.inputactions`.

Defined action maps and actions:
- **Player:** Move (Vector2), Look (Vector2), Attack, Interact (hold), Crouch, Jump, Previous, Next

Access input in scripts via the generated C# class or `InputActionAsset` references — do not use `Input.GetKey()`.

### Assembly Structure

- `Assembly-CSharp` — runtime game code (MonoBehaviours, etc.)
- `Assembly-CSharp-Editor` — editor-only scripts (custom inspectors, tools)

Editor scripts must go in folders named `Editor/` to be excluded from runtime builds.

### AI Navigation

`AI Navigation` package (v2.0.13) is included. NavMesh baking and agent configuration use the built-in NavMesh workflow (bake in the Editor under Window → AI → Navigation).

## Key Packages

| Package | Version | Purpose |
|---|---|---|
| URP | 17.5.0 | Render pipeline |
| Input System | 1.19.0 | Player input |
| Timeline | 1.8.12 | Cinematic sequences |
| AI Navigation | 2.0.13 | Pathfinding |
| Visual Scripting | 1.9.11 | Node-based logic |
| Test Framework | 1.7.0 | Unit/integration tests |
