# Ricochet-RL

A 1v1 adversarial grenade-combat reinforcement learning environment built in Unity and trained using Unity ML-Agents. Two autonomous agents compete in continuous 3D space to eliminate each other using trajectory pitching, grenade cooking, and obstacle positioning.

Trained for ~50 million steps across 16 parallel arenas using self-play.

---


## Overview

Unlike standard grid-world or projectile environments, **Ricochet-RL** couples physics-driven locomotion with continuous, physics-simulated ordnance:
- **Trajectory & Cooking:** Agents control throw force, pitch elevation, and fuse cooking time.
- **Anti-Exploit Dynamics:** Reward shaping prevents back-corner stalling, perimeter donut looping, and mutual suicide pacts.
- **Zero Heavy Assets:** All sound effects (explosions, bonks, throws) are procedurally generated via raw PCM buffers at runtime, paired with procedural dynamic lighting and bloom.

---

https://github.com/user-attachments/assets/cc7e37fc-5f1e-43d7-8403-27ffabb812a2

## Core Mechanics

### 1. Ordnance Physics (`Grenade.cs`)
* **Fuse Mechanics:** Hard 2.5-second fuse that ticks down continuously whether held in hand or airborne.
* **Damage Calculation:** Non-linear quadratic falloff ($Damage = MaxDamage \times (1 - \frac{dist}{radius})^2$) with obstacle line-of-sight raycasts to prevent wall-penetrating damage.
* **Impact "Bonks":** Grenades thrown at high velocities deal direct physical collision damage (5–30 HP scaled by relative velocity) on impact prior to detonation.
* **Dynamic Knockback:** Blast radius applies an upward and outward physics impulse vector, allowing agents to stagger or displace opponents.

### 2. Agent Controller & Anti-Camping (`PlayerController.cs`)
* **Anti-Camp Anchor:** Tracks spatial anchors over sliding windows; lingering inside a 3-meter zone for over 5 seconds bleeds reward continuously.
* **Stalemate Penalty:** Rounds hitting the step limit punish both agents equally to incentivize aggressive engagements.
* **Spawning:** Randomized dynamic respawns preventing duplicate spawn point collisions.

---

## Observation & Action Space

### Vector Observations (`CollectObservations`)
Total continuous observations per agent include:
- **Self State:** Normalized local velocity (3), health ratio (1), ground contact flag (1), throw cooldown ratio (1), fuse timer ratio of held grenade (1).
- **Opponent State:** Relative position vector (3), relative velocity (3), normalized health (1).
- **Threat Detection:** Live grenade presence flag (1), relative position of nearest unexploded grenade (3), incoming velocity vector of nearest grenade (3), remaining fuse ratio (1).

### Continuous Action Space (`OnActionReceived`)
The policy outputs 7 continuous actions mapped to physics execution:
1. `moveX`: Strafe movement (A / D)
2. `moveY`: Forward / Backward movement (W / S)
3. `turn`: Yaw rotation rate
4. `jump`: Vertical impulse
5. `throwAction`: Pin pulled / cook state (> 0.5 cooks, <= 0.5 throws)
6. `pitchAction`: Throw angle (-10° down to 90° high arc)
7. `forceAction`: Throw impulse force (5 to 25 units)

---

## Project Structure

```text
Assets/
├── Materials/          # HDR emissive materials and arena shaders
├── Prefabs/
│   ├── Grenade.prefab  # Dynamic grenade asset with physics & blast triggers
│   ├── Player.prefab   # Agent prefab with Rigidbody & Behavior Parameters
│   └── VFX/            # Expanding glowing sphere shockwaves & damage popups
├── Scripts/
│   ├── CameraShake.cs      # Screen rumble on detonation
│   ├── DamagePopup.cs      # Floating billboarded combat text
│   ├── ExplosionVFX.cs     # Procedural scaling shockwave mesh
│   ├── Grenade.cs          # Fuse logic, line-of-sight checks, and blast impulse
│   ├── PlayerController.cs # Agent observations, actions, and reward signals
│   ├── ProceduralAudio.cs  # Synthesized 8-bit PCM audio clips
│   └── ScoreManager.cs     # Rich-text persistent match scoreboard
```

---

## Getting Started

### Prerequisites
* Unity 2022.3 LTS or newer
* Universal Render Pipeline (URP)
* Unity ML-Agents Package (v2.0+)
* Python 3.9–3.10 with `mlagents` installed

### Installation
1. Clone the repository:
   ```bash
   git clone [https://github.com/aon-ob/Ricochet-RL.git](https://github.com/aon-ob/Ricochet-RL.git)
   cd Ricochet-RL
   ```
2. Open the project inside Unity Hub.
3. Allow Unity to restore packages and compile scripts.

---

## Running Inference (Testing the Model)

1. Open `Assets/Scenes/FinalBattle.unity`.
2. Locate the `Player` instances in the scene hierarchy.
3. In the Inspector under **Behavior Parameters**:
   * Set **Model** to the trained `.onnx` file located in `Assets/Models/`.
   * Set **Behavior Type** to `Inference Only`.
4. (Optional) To play against the AI:
   * On Player 1, set **Behavior Type** to `Heuristic Only`.
   * Use `WASD` to move, `Space` to jump, `Left-Click` to cook and throw, `Arrow Keys` to adjust pitch, and `Left Shift / Ctrl` to modify throw force.
5. Press **Play** in the Unity Editor.

---

## Training Setup

To train the policy from scratch using parallel environments:

```bash
mlagents-learn config/trainer_config.yaml --run-id=Ricochet_Run01
```

Press **Play** in Unity when prompted by the terminal.

---

## License
MIT License. Free to use, adapt, and build upon.
