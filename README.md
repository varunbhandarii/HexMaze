# HexMaze

HexMaze is a Unity VR escape game set inside a procedural hexagonal maze. The player explores rooms, tracks progress through a wrist-mounted HUD, avoids patrolling guards, and races the timer to reach the exit.

The project also includes an optional LLM-driven maze controller that can read the current maze state and apply validated commands, allowing the maze to shift while the player moves through it.

## Features

- Runtime-generated hex room grid with locked and unlocked door states
- OpenXR / XR Interaction Toolkit support for VR input and interactions
- Guard AI with detection, chase behavior, footstep/audio cues, and NavMesh rebaking
- Wrist HUD with timer, visited room count, marker count, status text, and detection indicator
- Player tools such as breadcrumb markers, barricades, crouch detection, and locomotion mode handling
- Configurable difficulty tuning for maze, guards, timer, and player affordances
- Optional OpenAI-powered maze logic using a local ignored config file

## Tech Stack

- Unity `6000.0.62f1`
- C#
- OpenXR
- XR Interaction Toolkit `3.0.10`
- Unity AI Navigation `2.0.9`
- Unity Input System `1.14.2`

## Project Structure

```text
Assets/
  Scenes/              Main Unity scenes
  Scripts/
    Game/              Game loop, timer, win/loss/restart flow, difficulty tuning
    Guards/            Guard spawning, detection, audio, movement, NavMesh updates
    LLM/               API config, request client, response validation, command dispatch
    Maze/              Hex grid generation, doors, rooms, markers, ambience
    Player/            HUD, room tracking, barricades, crouch and locomotion helpers
  StreamingAssets/     Prompt and example API configuration
Packages/              Unity package manifest and lock file
ProjectSettings/       Unity project configuration
Report/                Project report and screenshots
```

## Setup

1. Install Unity Hub and Unity Editor `6000.0.62f1`.
2. Clone this repository.
3. Open the project folder in Unity Hub.
4. Let Unity restore packages from `Packages/manifest.json`.
5. Open `Assets/Scenes/MazeScene.unity`.
6. Enter Play Mode or build for an OpenXR-compatible headset.

## Optional LLM Setup

The real API config is intentionally ignored by Git.

1. Copy `Assets/StreamingAssets/config.example.json`.
2. Rename the copy to `Assets/StreamingAssets/config.json`.
3. Put your API key and model settings in the local file.

```json
{
  "openai_api_key": "sk-your-key-here",
  "model": "gpt-4o-mini",
  "max_tokens": 300,
  "temperature": 0.4
}
```

Do not commit `config.json`; it is listed in `.gitignore`.

## Gameplay

- Press the primary XR button to start the run.
- Navigate from the entrance room to the green exit marker.
- Avoid guards and watch the wrist HUD for timer, room count, markers, and detection level.
- Use breadcrumb markers and barricades to help plan or defend your route.
- Reach the exit before time runs out.

## Repository Notes

This repository tracks the Unity source files, scenes, prefabs, materials, scripts, package manifest, and project settings. Unity-generated folders such as `Library`, `Logs`, `UserSettings`, temporary files, and local secrets are excluded.
