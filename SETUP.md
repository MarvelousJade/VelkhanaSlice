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

## The graybox scene

`Assets/Scenes/Graybox.unity` is generated, not hand-authored. Rebuild it from
**Velkhana > Rebuild Graybox Scene**, or in batch mode:

```
Unity.exe -batchmode -quit -projectPath . -executeMethod VelkhanaSlice.EditorTools.GrayboxSceneBuilder.Build -logFile -
```

Edit `Assets/Editor/GrayboxSceneBuilder.cs` and rebuild rather than editing the scene by hand, so
the arena setup stays reviewable as a diff. The builder creates the `Hurtbox` layer, the eleven
placeholder `AttackDefinition` assets under `Assets/Data/Attacks`, a hunter with its blade point
and camera, and Velkhana with nine body-part hurtboxes.

The frame counts in the builder are placeholders. They are the numbers to overwrite from
frame-stepped reference footage, and nothing else has to change when they do.

Controls are read straight from `Gamepad.current` / `Keyboard.current` / `Mouse.current`. There is
no `.inputactions` asset yet, so bindings are fixed: left stick or WASD to move, right stick or
mouse to aim, west button or left mouse to charge, north button or right mouse for the secondary,
east button or space to roll, south button or F to sheathe.

## Tests

Edit-mode tests cover the frame-window and damage maths. Play-mode tests drive real fixed frames
and check that the graybox scene is wired.

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults edit.xml -logFile -
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults play.xml -logFile -
```

Unity's exit code is 0 even when compilation fails, so check the log for `error CS` rather than
trusting the exit status.

## Not built yet

`ArenaHazardManager`, pooled `IceWall` / `IceSpire`, `CombatTelemetryRecorder`, guard and sharpness,
lock-on, Slinger Burst, tail sever. See the plan document for where each belongs.

The hunter's own input path has no automated coverage: driving it needs `InputTestFixture` from
the Input System package, which needs `testables` in the manifest. Worth adding when combo-buffering
and cancel windows are tuned, since that is where the state machine gets subtle.
