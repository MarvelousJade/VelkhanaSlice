# Velkhana Slice — Setup

Unity 6000.4.11f1. Open the folder in Unity Hub; the editor generates `Library/` and the
`.meta` files on first import.

## What exists

| Script | Role |
|---|---|
| `Combat/AttackDefinition.cs` | ScriptableObject holding every attack's frame windows, damage, charge scaling, motion curve and follow-up links |
| `Combat/BodyPartHurtbox.cs` | One damageable section, with its own multiplier, break threshold and ice armour |
| `Combat/DamageResolver.cs` | The only place a hitbox turns into damage |
| `Hunter/HunterController.cs` | Movement, sheathing, charge levels, attack playback, roll |
| `Monster/VelkhanaBrain.cs` | Armour stages plus weighted, context-driven attack selection |
| `Tests/Editor/CombatMathTests.cs` | Frame-window and damage rules the acceptance criteria depend on |

The simulation runs at a fixed 60 Hz (`ProjectSettings/TimeManager.asset`). All gameplay logic is
in `FixedUpdate`, and code drives displacement rather than animation, so timings can be compared
frame by frame against reference footage.

## Wiring a scene

1. Hunter: empty GameObject with `CharacterController` + `HunterController`. Add a child transform
   for `bladePoint` and set `hurtboxLayers` to the monster's layer.
2. Camera: perspective, pitched 55–65 degrees, assigned to the hunter's `aimCamera`.
3. Velkhana: `VelkhanaBrain` with `hunter` pointing at the hunter transform. Add a child collider
   per body part with `BodyPartHurtbox`, and list the armoured ones in `armoredParts`.
4. Attacks: `Create > Velkhana > Attack Definition` per move. Fill frame counts from reference
   footage, then link `followUps` to build the combo graph.

Controls are read straight from `Gamepad.current` / `Keyboard.current` / `Mouse.current`. There is
no `.inputactions` asset yet, so bindings are fixed: left stick or WASD to move, right stick or
mouse to aim, west button or left mouse to charge, north button or right mouse for the secondary,
east button or space to roll, south button or F to sheathe.

## Tests

```
"C:\Program Files\Unity\Hub\Editor\6000.4.11f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml -logFile -
```

## Not built yet

`ArenaHazardManager`, pooled `IceWall` / `IceSpire`, `CombatTelemetryRecorder`, guard and sharpness,
lock-on, Slinger Burst, tail sever. See the plan document for where each belongs.
