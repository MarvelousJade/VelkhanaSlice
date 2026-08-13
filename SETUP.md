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
| `Hunter/HunterController.cs` | Movement plus the readable ground core of decoded `cFSMPl_W00`, including separate charge tiers and hold power |
| `Hunter/HunterPresentation.cs` | Procedural graybox roll, guard, draw/sheathe, three charge stances and semantic Great Sword swings |
| `Hunter/HunterHealth.cs` | Applies roll invulnerability, Great Sword guard and hyper-armour reduction to incoming hits |
| `Monster/VelkhanaBrain.cs` | Observable combat states, direct range/angle repositioning, armour stages and weighted attack selection |
| `Monster/VelkhanaPresentation.cs` | Procedural body, wing, neck, tail, breath and phase poses for the placeholder monster |
| `CameraRig.cs` | Angled follow camera framing hunter and monster together |
| `Debug/ScriptedPlaythrough.cs` | Virtual gamepad that plays a scripted fight and screenshots each beat |
| `Debug/CombatHud.cs` | F2 state debugger with player/monster panels, action timelines, world labels and AI spacing intent |
| `Automation/GameplayAutomationBridge.cs` | Loopback state/input API, exact frame stepping, event stream, reset, capture and JSONL telemetry |
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
the arena setup stays reviewable as a diff. The builder creates the `Hurtbox` layer, the 34
placeholder `AttackDefinition` assets under `Assets/Data/Attacks`, a hunter with its blade point
and camera, and Velkhana with nine body-part hurtboxes.

The frame counts in the builder are placeholders. They are the numbers to overwrite from
frame-stepped reference footage, and nothing else has to change when they do.

Velkhana's context gate uses the decoded EM124 `Combat_Enter` thresholds: 8.5 m horizontal with a
7.5 m vertical gate, then a 17 m 3D fallback, treating the game's distance units as centimetres.
Action options reproduce the important 2–28 m tiers and front/flank/rear sectors seen in
`Global.node_093..101`. Each option carries its source THK node name, normal/enraged weights,
critical-health weighting and an explicit Mode0/1/2 mask.

Mode0/1/2 preserve the three table buckets selected by unresolved `function#101`. The demo maps
them to the visible neutral/ice-stage presentation, but deliberately does not rename the predicate
or claim its engine meaning has been proven. The HUD displays context, mode, source node, range
target and sequence step so the decision can be audited while playing.

Visible walking is a clearly labelled project-authored pacing layer around that decoded selector.
After every two completed grounded action sequences since her last reposition, Velkhana enters
`Reposition` and normally walks for at least 48 frames (0.8 seconds), moving toward a useful attack
distance or orbiting when already in band. Ordinary range-recovery walking also resets this cadence. While the minimum pacing floor is active, no THK selection RNG call is made, and
the walk never interrupts startup/active/recovery. Its movement can still change the distance and
cooldown context of later selections, so it does not promise identical future outcomes for the same
seed. Rage and topple reactions are hard interrupts: they cancel the current pacing bout rather
than resuming its remaining frames. Setting `groundSequencesPerReposition` to zero disables the cadence. Ordinary
out-of-range recovery still uses the same `Reposition` state and retains its original first-frame,
interval, and timeout decisions without the pacing-only 48-frame floor.

The scoped close opener preserves the source-order `Global.node_105/106/108` gateway and
`Global.node_087` distance/random tables. After the selected 004/006/009 opener finishes its full
startup, active and recovery timeline, its post-motion distance drives `Global.node_090/089/088`
respectively. Those continuations retain the decoded `Global.node_076` call as an explicit no-op
hook because the focused arena is non-AT and does not model target 44, helpless-target predicates,
or the no-argument `function#101()`. `Global.node_079` targets `arenaCenter` before comparing its
5 m distance/clockwise sector, then retargets the sole hunter; only its near-center random block
consumes another RNG value.

Multi-action patterns are data sequences. For example the aerial `Global.node_063` placeholder
takes off, plays vertical breath and ice-wave steps with a target/arena interrupt pass between
them, dives with a tail sting, then lands. Damage fills a rage threshold, entering a visible roar
transition and the separate enraged weighting table. Completed sequences escalate the ice-armour
stage; breaking enough armour suppresses the rebuild cycle temporarily. Selection uses a fixed
seed by default so captures and technical-demo reviews are reproducible.

Velkhana has a separate, collider-free `VisualRoot` containing named torso, neck, head, wing, leg
and three-piece tail pivots. `VelkhanaPresentation` poses bite, rush/back-step, tail strings,
straight/90/180/freeze breaths, ice control, rage, takeoff, aerial attacks and landing. The nine
stationary gameplay volumes live under `GameplayHurtboxes`; procedural animation never moves them
or the solid `BodyBlocker`. Her extracted `em124_00..08.lmt`/`.mbd` files confirm where the original
animation banks live, but remain private reference files outside `Assets`.

The capsule hunter uses a presentation-only `VisualRoot` with hand and back sword sockets.
`HunterPresentation` poses that hierarchy from the combat state, so it never moves the
`CharacterController` or hitboxes. No rig or Blender file is needed for these graybox animations.
The retained decoded Great Sword graph separates the Basic, Strong and True combo charge stages
from each stage's Lv0-Lv3 hold power. It includes the two different tackle shortcuts,
wide/strong-wide/leaping-wide branches, post-strong side/rising attacks, guard-to-kick, and the
two-part True Charged Slash route: a miss keeps ActionNo 78's normal second hit, while a connected
opening selects the level-specific FinishEx action. Source WP00 node IDs and ActionNo values remain
visible in the HUD/controller for traceability. Airborne, slinger, ledge and clutch branches are
still outside this focused ground-combat demo.

During every charge stage, the sword pulls progressively farther behind the hunter and the hunter's
emissive glow progresses from white to yellow to red as the hold thresholds are reached.
When final humanoid art arrives, replace this procedural component with an Animator while keeping
`HunterController` as the source of gameplay timing.

Controls are read straight from `Gamepad.current` / `Keyboard.current` / `Mouse.current`. There is
no `.inputactions` asset yet, so bindings are fixed: left stick or WASD to move, right stick or
mouse to aim, west button or left mouse for Triangle/primary, north button or right mouse for
Circle/secondary, right trigger or R/left Ctrl to guard, east button or space to roll, south button
or F to manually draw/sheathe, and left-stick click or either Shift key to run. Running automatically
sheathes a drawn sword before accelerating; attacking during that transition cancels it into the
correct stationary or moving draw route while preserving the decoded source-node identity. A held
moving draw stays in N021/ActionNo7 while charging, then releases through compressed N031 into N001;
the stationary N022 route remains distinct and uses compressed N023 to enter N003.

Press **F2** to toggle the state debugger. Its left panel explains the player's current controller
state, WP00 node/ActionNo, charge or attack phase, buffered follow-up and defensive flags. The right
panel shows Velkhana's high-level state, context, unresolved Mode bucket, ice/rage state, spacing
target, current THK trace and part gauges. Labels above both actors mirror their live state. The
line between them changes colour for too close/in band/too far; while the AI is repositioning, a
ring around the hunter shows the desired separation. Press **F3** independently to toggle exact
hurtbox and attack-volume outlines.

The generated scene also contains a loopback gameplay automation bridge. A standalone player
started with `-automation` exposes exact input, reset, actor placement, AI configuration, fixed-frame
step, state/event and camera-capture endpoints on `127.0.0.1:47777`. `-telemetry <path>` records one
JSON state per fixed frame. See [AUTOMATION.md](AUTOMATION.md) for the protocol and scenario runner.

## Tests

Edit-mode tests cover frame-window, damage, steering, precise EM124 condition gates and mode
mapping. Play-mode tests drive real fixed frames, verify attack/recovery/reposition, sequence
boundaries, rage, takeoff/aerial/landing contexts, and check that the visual rig cannot accidentally
acquire gameplay colliders.

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

`ArenaHazardManager`, pooled `IceWall` / `IceSpire`, guard and sharpness, lock-on, Slinger Burst,
tail sever. See the plan document for where each belongs.

Velkhana's repositioning is direct arena locomotion rather than obstacle-aware navigation, and
the fixed walking cadence is a project readability choice rather than a decoded EM124 probability.
The breath beam is a presentation-only primitive rather than a final effect. This is a semantic
reconstruction of the highest-value combat paths, not a literal execution of all 453 decoded nodes;
Palico targeting, blinded/mount/turf-war/area-change tables and unresolved engine predicates remain
outside the demo. Death is tracked on `HunterHealth` but nothing reacts to it, so there is still no
win or lose.

The hunter input path has both Input System play tests and a device-independent automation override.
Combo-buffering and cancel-window routes should continue to gain scenario coverage as their timings
are tuned.
