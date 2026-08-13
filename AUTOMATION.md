# Gameplay automation API

The graybox scene contains `GameplayAutomationBridge`, a loopback-only API for deterministic
combat iteration. It reads and changes Unity objects only on the main thread; the socket worker
only queues commands and serves immutable JSON snapshots.

## Start it

In a standalone player the bridge is inert unless `-automation` or `-telemetry` is supplied:

```powershell
Build\VelkhanaSlice.exe -automation -automation-port 47777 `
  -telemetry artifacts\fixed-frames.jsonl
```

An explicit `-automation` launch starts paused at simulation frame 0. `-telemetry` writes one full
state snapshot per fixed frame. It includes transition/damage events raised on that frame and is
flushed every 60 frames and during a graceful `POST /quit`.

In non-batch Unity Editor Play Mode the server starts automatically on port 47777 without pausing.
Call `POST /pause` before exact stepping. The server binds only `127.0.0.1` and uses `TcpListener`,
so Windows does not need an `HttpListener` URL ACL or administrator privileges.

Check readiness and discover endpoints:

```powershell
curl.exe http://127.0.0.1:47777/health
curl.exe http://127.0.0.1:47777/schema
```

## Deterministic loop

Reset reloads the generated scene, resets the API simulation-frame counter, reapplies the AI seed,
and optionally teleports both actors before unpausing:

```powershell
curl.exe -X POST http://127.0.0.1:47777/reset `
  -H "Content-Type: application/json" `
  -d '{"seed":124,"paused":true,"setPositions":true,"hunterX":0,"hunterY":1,"hunterZ":-5,"hunterYaw":0,"monsterX":0,"monsterY":0,"monsterZ":6,"monsterYaw":180}'
```

Input bodies are the complete persistent **held** state. Omitted fields become zero/false. Button
presses are rising edges, so send `{}` to release a button before pressing it again. Axes are clamped
to unit magnitude.

```powershell
# Hold forward + primary, advance exactly 40 FixedUpdates, then release.
curl.exe -X POST http://127.0.0.1:47777/input -H "Content-Type: application/json" `
  -d '{"moveY":1,"primary":true}'
curl.exe -X POST http://127.0.0.1:47777/step -H "Content-Type: application/json" `
  -d '{"frames":40}'
curl.exe -X POST http://127.0.0.1:47777/input -H "Content-Type: application/json" -d '{}'
```

`POST /step` requires a paused simulation. Its response is the snapshot after the final requested
fixed frame, with the game paused again. This makes action windows directly comparable to 60 Hz
reference footage.

## Endpoints

| Endpoint | Body / result |
|---|---|
| `GET /state` | Complete hunter, monster, relative-position, attack, THK and part-gauge snapshot |
| `GET /events?after=N` | Ring-buffered state/action/decision/damage events after sequence N |
| `POST /input` | `{moveX,moveY,aimX,aimY,primary,secondary,dodge,sheathe,run,guard}` |
| `POST /pause` | `{"paused":true}` or `{"paused":false}` |
| `POST /step` | `{"frames":1}`; 1–36000 fixed frames |
| `POST /reset` | Seed, pause state, and optional actor positions as shown above |
| `POST /actors` | Teleport one or both actors using `setHunter` / `setMonster` and XYZ/yaw fields |
| `POST /ai` | `{"enabled":true,"deterministic":true,"seed":124}` |
| `POST /capture` | `{"path":"artifacts/frame.png"}`; renders the main camera even while paused |
| `POST /quit` | Flush and gracefully close a standalone automation player |

Important state fields include:

- actor position, Euler rotation, forward, velocity, 3D/horizontal/vertical separation and facing
- hunter controller state/frame, WP00 node, `ActionNo`, buffered node, charge stage/power, defensive
  flags, input override and exact attack phase/frame
- monster state/frame, context, unresolved Mode bucket, armour/rage/topple/air state, spacing target,
  sequence step, selection-roll count, THK node/trace and exact attack phase/frame
- every hurtbox's body-part group, damage, shared stagger gauge, break threshold/status and ice armour

## Scenario runner

`Tools/run_automation.py` is a dependency-free client. It can attach to an Editor/player or launch a
player itself:

```powershell
python Tools\run_automation.py Tools\example_scenario.json `
  --exe Build\VelkhanaSlice.exe `
  --telemetry artifacts\fixed-frames.jsonl `
  --output artifacts\scenario-states.jsonl
```

Each scenario step supplies a held input state, a fixed-frame count and an optional capture path.
The client records a labelled post-step state; player telemetry retains every intermediate frame.
