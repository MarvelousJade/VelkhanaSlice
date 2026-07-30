# Velkhana Slice — Setup

Unity 6000.4.11f1. Open the folder in Unity Hub; the editor generates `Library/` and the
`.meta` files on first import.

## What exists

| Script | Role |
|---|---|
| `Combat/AttackDefinition.cs` | ScriptableObject holding every attack's frame windows, damage, charge scaling, motion curve and follow-up links |
| `Combat/BodyPartHurtbox.cs` | One damageable section, with its own multiplier, break threshold and ice armour |
| `Combat/DamageResolver.cs` | The only place a hitbox turns into damage |
| `Combat/AttackHitbox.cs` | `IAttacker` plus the shared box query both sides use |
| `Combat/AttackTelegraph.cs` | Ground projection of the active hitbox, amber winding up, red on active frames |
| `Hunter/HunterController.cs` | Movement, sheathing, charge levels, attack playback, roll |
| `Hunter/HunterPresentation.cs` | Procedural graybox roll, sword draw/sheathe, charge pose and basic swing |
| `Hunter/HunterHealth.cs` | Applies roll invulnerability and hyper-armour reduction to incoming hits |
| `Monster/VelkhanaBrain.cs` | Observable combat states, direct range/angle repositioning, armour stages and weighted attack selection |
| `Monster/VelkhanaPresentation.cs` | Procedural body, wing, neck, tail, breath and phase poses for the placeholder monster |
| `CameraRig.cs` | Angled follow camera framing hunter and monster together |
| `Debug/ScriptedPlaythrough.cs` | Virtual gamepad that plays a scripted fight and screenshots each beat |
| `Debug/CombatHud.cs` | IMGUI readout of health, charge, attack frame and part damage |
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

To rebuild both the generated scene and Windows player in one step, use
**Velkhana > Rebuild Graybox + Windows Player**. It writes `Build/VelkhanaSlice.exe`
and ensures the player scene and script assembly are generated together.

Edit `Assets/Editor/GrayboxSceneBuilder.cs` and rebuild rather than editing the scene by hand, so
the arena setup stays reviewable as a diff. The builder creates the `Hurtbox` layer, the eleven
placeholder `AttackDefinition` assets under `Assets/Data/Attacks`, a hunter with its blade point
and camera, and Velkhana with nine body-part hurtboxes.

The frame counts in the builder are placeholders. They are the numbers to overwrite from
frame-stepped reference footage, and nothing else has to change when they do.

Velkhana's decision ranges use the decoded EM124 `Combat_Enter` thresholds: 8.5 m close and
17 m medium, treating the game's distance units as centimetres. `Combat_Main` also has distinct
enraged/non-enraged weighted branches, represented by `enraged` and each option's
`enragedWeightMultiplier`. The implementation is intentionally smaller than the full THK graph.

Velkhana has a separate, collider-free `VisualRoot` containing named torso, neck, head, wing, leg
and three-piece tail pivots. `VelkhanaPresentation` poses it for tail thrust, body check, straight
beam, sweeping breath and ice spires, plus idle/reposition movement and armour-phase glow. The nine
stationary gameplay volumes live under `GameplayHurtboxes`; procedural animation never moves them
or the solid `BodyBlocker`. Her extracted `em124_00..08.lmt`/`.mbd` files confirm where the original
animation banks live, but remain private reference files outside `Assets`.

The capsule hunter uses a presentation-only `VisualRoot` with hand and back sword sockets.
`HunterPresentation` poses that hierarchy from the combat state, so it never moves the
`CharacterController` or hitboxes. No rig or Blender file is needed for these graybox animations.
During a charge, the sword pulls behind the hunter and the hunter's emissive glow progresses from
white to yellow to red as the three charge thresholds are reached.
When final humanoid art arrives, replace this procedural component with an Animator while keeping
`HunterController` as the source of gameplay timing.

Controls are read straight from `Gamepad.current` / `Keyboard.current` / `Mouse.current`. There is
no `.inputactions` asset yet, so bindings are fixed: left stick or WASD to move, right stick or
mouse to aim, west button or left mouse to charge/attack, north button or right mouse for the
secondary, east button or space to roll, south button or F to manually draw/sheathe, and left-stick
click or either Shift key to run. Running automatically sheathes a drawn sword before accelerating;
attacking while sheathed automatically draws it with the draw slash.

## Tests

Edit-mode tests cover frame-window, damage and direct steering maths. Play-mode tests drive real
fixed frames, verify observable attack/recovery/reposition states, and check that the visual rig
cannot accidentally acquire gameplay colliders.

```
Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults edit.xml -logFile -
Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults play.xml -logFile -
```

Unity's exit code is 0 even when compilation fails, so check the log for `error CS` rather than
trusting the exit status.

## Visual check

The build can play itself. `-autoshots <dir>` makes `ScriptedPlaythrough` add a virtual gamepad,
run its beat list and write a screenshot per named beat:

```
VelkhanaSlice.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -autoshots shots
```

Files come out as `06_charged_swing_f026.png`. The component caps the frame rate to 60 so a beat
frame matches a simulation frame, otherwise captures land nowhere near the attack frame they are
named for. Edit the beat list on the component to check a different sequence.

Capture from inside the player rather than screenshotting the desktop: it cannot pick up anything
outside the game window and does not depend on window focus.

## Not built yet

`ArenaHazardManager`, pooled `IceWall` / `IceSpire`, `CombatTelemetryRecorder`, guard and sharpness,
lock-on, Slinger Burst, tail sever. See the plan document for where each belongs.

Velkhana's repositioning is direct graybox locomotion rather than obstacle-aware navigation, and
the breath beam is a presentation-only primitive rather than a final effect. Death is tracked on
`HunterHealth` but nothing reacts to it, so there is still no win or lose.

The hunter's own input path has no automated coverage: driving it needs `InputTestFixture` from
the Input System package, which needs `testables` in the manifest. Worth adding when combo-buffering
and cancel windows are tuned, since that is where the state machine gets subtle.
