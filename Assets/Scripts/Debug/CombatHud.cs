using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;

namespace VelkhanaSlice.DebugTools
{
    /// <summary>The four authored phases shown by the state debugger's attack timeline.</summary>
    public enum CombatDebugAttackPhase
    {
        None,
        Startup,
        Active,
        Recovery,
        Complete,
    }

    /// <summary>
    /// Playable-build state debugger for both sides of combat. F2 toggles the state panels,
    /// actor labels and AI spacing intent; F3 independently toggles exact combat volumes.
    /// This component only observes gameplay state and never advances an RNG or state machine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatHud : MonoBehaviour
    {
        public HunterHealth health;
        public HunterController hunterController;
        public VelkhanaBrain brain;

        [Header("Runtime toggle")]
        public bool visibleOnStart = true;
        public Key toggleKey = Key.F2;
        public bool showWorldLabels = true;
        public bool showAiSpacingIntent = true;

        [Header("World intent colours")]
        public Color inRangeColor = new Color(0.25f, 1f, 0.4f, 0.8f);
        public Color tooFarColor = new Color(1f, 0.65f, 0.12f, 0.8f);
        public Color tooCloseColor = new Color(0.75f, 0.35f, 1f, 0.8f);
        [Min(0.005f)] public float intentLineWidth = 0.045f;

        public bool Visible { get; private set; }

        CombatVolumeDebug _volumeDebug;
        BodyPartHurtbox[] _parts = System.Array.Empty<BodyPartHurtbox>();
        VelkhanaBrain _partsOwner;

        GUIStyle _title;
        GUIStyle _body;
        GUIStyle _small;
        GUIStyle _smallWrap;
        GUIStyle _spacing;
        GUIStyle _badge;
        GUIStyle _barText;
        GUIStyle _worldLabel;

        Material _worldLineMaterial;
        LineRenderer _targetLine;
        LineRenderer _desiredRangeRing;

        static readonly Color PanelColor = new Color(0.025f, 0.035f, 0.055f, 0.88f);
        static readonly Color TrackColor = new Color(0.07f, 0.09f, 0.12f, 0.96f);
        static readonly Color StartupColor = new Color(1f, 0.62f, 0.08f, 0.9f);
        static readonly Color ActiveColor = new Color(1f, 0.12f, 0.08f, 0.95f);
        static readonly Color RecoveryColor = new Color(0.25f, 0.55f, 0.95f, 0.9f);

        void Awake()
        {
            Visible = visibleOnStart;
            ResolveBindings();

            _volumeDebug = GetComponent<CombatVolumeDebug>();
            if (_volumeDebug == null) _volumeDebug = gameObject.AddComponent<CombatVolumeDebug>();
            _volumeDebug.Bind(health, hunterController, brain);

            CreateWorldGeometry();
        }

        void Update()
        {
            if (health == null || hunterController == null || brain == null)
                ResolveBindings();

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                Visible = !Visible;
        }

        void LateUpdate()
        {
            DrawWorldIntentGeometry();
        }

        void ResolveBindings()
        {
            hunterController ??= FindAnyObjectByType<HunterController>();
            health ??= hunterController != null
                ? hunterController.GetComponent<HunterHealth>()
                : FindAnyObjectByType<HunterHealth>();
            brain ??= FindAnyObjectByType<VelkhanaBrain>();

            if (brain != _partsOwner)
            {
                _partsOwner = brain;
                _parts = brain != null
                    ? brain.GetComponentsInChildren<BodyPartHurtbox>(true)
                    : System.Array.Empty<BodyPartHurtbox>();
            }
        }

        void OnGUI()
        {
            EnsureStyles();

            float uiScale = Mathf.Clamp(Screen.width / 1280f, 0.75f, 1.25f);
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.matrix = Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.identity,
                new Vector3(uiScale, uiScale, 1f));

            float canvasWidth = Screen.width / uiScale;
            string stateToggle = Visible ? "ON" : "OFF";
            string volumeToggle = _volumeDebug != null && _volumeDebug.Visible ? "ON" : "OFF";
            GUI.Label(
                new Rect(canvasWidth - 355f, 10f, 340f, 22f),
                $"F2 STATE DEBUG {stateToggle}    F3 VOLUMES {volumeToggle}",
                _small);

            if (Visible)
            {
                const float gutter = 16f;
                const float gap = 12f;
                float availableHalf = (canvasWidth - gutter * 2f - gap) * 0.5f;
                float playerWidth = Mathf.Min(430f, availableHalf);
                float monsterWidth = Mathf.Min(560f, availableHalf);

                DrawPlayerPanel(new Rect(gutter, 40f, playerWidth, 300f));
                DrawMonsterPanel(new Rect(
                    canvasWidth - gutter - monsterWidth,
                    40f,
                    monsterWidth,
                    382f));

                if (showWorldLabels) DrawWorldLabels(uiScale);
            }

            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        void DrawPlayerPanel(Rect panel)
        {
            DrawPanel(panel, new Color(0.2f, 0.85f, 0.35f, 1f));
            float x = panel.x + 12f;
            float width = panel.width - 24f;
            float y = panel.y + 9f;

            GUI.Label(new Rect(x, y, width, 22f), "PLAYER STATE", _title);
            y += 25f;

            float healthFraction = health != null && health.maxHealth > 0f
                ? health.Current / health.maxHealth
                : 0f;
            DrawBar(
                new Rect(x, y, width, 19f),
                healthFraction,
                Color.Lerp(new Color(0.85f, 0.12f, 0.1f), new Color(0.2f, 0.9f, 0.35f), healthFraction),
                health != null ? $"HP  {health.Current:0} / {health.maxHealth:0}" : "HP  -");
            y += 25f;

            if (hunterController == null)
            {
                GUI.Label(new Rect(x, y, width, 22f), "HunterController not bound", _body);
                return;
            }

            Color stateColor = PlayerStateColor(hunterController.CurrentState);
            DrawBadge(new Rect(x, y, 126f, 23f), hunterController.CurrentState.ToString(), stateColor);
            GUI.Label(
                new Rect(x + 136f, y + 2f, width - 136f, 21f),
                $"state frame {hunterController.StateFrame}    pos {FlatPosition(hunterController.transform.position)}",
                _small);
            y += 29f;

            int actionNumber = HunterController.ActionNumberFor(hunterController.CurrentNode);
            GUI.Label(
                new Rect(x, y, width, 20f),
                $"WP00  {hunterController.CurrentNode}    ActionNo {actionNumber}",
                _body);
            y += 21f;

            string weapon = hunterController.IsWeaponTransitioning
                ? hunterController.WeaponTransitionDrawn ? "drawing" : "sheathing"
                : hunterController.WeaponDrawn ? "drawn" : "sheathed";
            GUI.Label(
                new Rect(x, y, width, 20f),
                $"weapon {weapon}    charge {hunterController.CurrentChargeStage}/Lv{hunterController.ChargeLevel}" +
                $"    running {OnOff(hunterController.IsRunning)}",
                _small);
            y += 23f;

            if (hunterController.CurrentAttack != null)
            {
                int frame = DisplayedHunterAttackFrame();
                GUI.Label(
                    new Rect(x, y, width, 19f),
                    $"ACTION  {hunterController.CurrentAttack.name}",
                    _body);
                y += 20f;
                DrawAttackTimeline(
                    new Rect(x, y, width, 21f),
                    hunterController.CurrentAttack,
                    frame);
            }
            else if (hunterController.CurrentState == HunterController.State.Charging)
            {
                GUI.Label(new Rect(x, y, width, 19f), "ACTION  charge hold", _body);
                y += 20f;
                DrawChargeTimeline(new Rect(x, y, width, 21f));
            }
            else if (hunterController.CurrentState == HunterController.State.Rolling)
            {
                GUI.Label(new Rect(x, y, width, 19f), "ACTION  evade", _body);
                y += 20f;
                DrawSimpleTimeline(
                    new Rect(x, y, width, 21f),
                    hunterController.StateFrame,
                    hunterController.rollFrames,
                    stateColor,
                    $"roll  f{hunterController.StateFrame}/{hunterController.rollFrames}");
            }
            else if (hunterController.IsWeaponTransitioning)
            {
                GUI.Label(new Rect(x, y, width, 19f), $"ACTION  {weapon}", _body);
                y += 20f;
                DrawBar(
                    new Rect(x, y, width, 21f),
                    hunterController.WeaponTransitionProgress,
                    stateColor,
                    $"{hunterController.WeaponTransitionProgress:P0}");
            }
            else
            {
                GUI.Label(new Rect(x, y, width, 19f), "ACTION  -", _body);
                y += 20f;
                DrawSimpleTimeline(
                    new Rect(x, y, width, 21f),
                    0,
                    1,
                    stateColor,
                    "no authored action timeline");
            }
            y += 28f;

            GUI.Label(
                new Rect(x, y, width, 39f),
                $"INTENT  {DescribePlayerIntent()}",
                _smallWrap);
            y += 40f;

            GUI.Label(
                new Rect(x, y, width, 20f),
                $"DEFENSE  guard {OnOff(hunterController.IsGuarding)}    i-frames {OnOff(hunterController.IsInvulnerable)}" +
                $"    hyper armor {OnOff(hunterController.HasHyperArmor)}",
                _small);
        }

        void DrawMonsterPanel(Rect panel)
        {
            DrawPanel(panel, new Color(0.2f, 0.78f, 1f, 1f));
            float x = panel.x + 12f;
            float width = panel.width - 24f;
            float y = panel.y + 9f;

            GUI.Label(new Rect(x, y, width, 22f), "MONSTER AI STATE", _title);
            y += 25f;

            float healthFraction = brain != null ? brain.HealthFraction : 0f;
            DrawBar(
                new Rect(x, y, width, 19f),
                healthFraction,
                new Color(0.2f, 0.76f, 1f),
                brain != null ? $"BOSS HP  {brain.CurrentHealth:0} / {brain.maxHealth:0}" : "BOSS HP  -");
            y += 25f;

            if (brain == null)
            {
                GUI.Label(new Rect(x, y, width, 22f), "VelkhanaBrain not bound", _body);
                return;
            }

            Color stateColor = MonsterStateColor(brain.CurrentState);
            DrawBadge(new Rect(x, y, 126f, 23f), brain.CurrentState.ToString(), stateColor);
            GUI.Label(
                new Rect(x + 136f, y + 2f, width - 136f, 21f),
                $"{brain.CurrentContext}    state frame {brain.StateFrame}    airborne {OnOff(brain.IsAirborne)}",
                _small);
            y += 29f;

            float rageFill = brain.enraged ? 1f : brain.RageBuild;
            GUI.Label(
                new Rect(x, y, width * 0.57f, 20f),
                $"BUCKET  {brain.CombatMode}    ICE  {brain.stage}",
                _body);
            DrawBar(
                new Rect(x + width * 0.58f, y, width * 0.42f, 18f),
                rageFill,
                new Color(1f, 0.25f, 0.08f),
                brain.enraged ? "RAGE  ENRAGED" : $"RAGE  {brain.RageBuild:P0}");
            y += 24f;

            string spacing = MonsterSpacing(out float distance, out float angle, out Color spacingColor);
            _spacing.normal.textColor = spacingColor;
            GUI.Label(
                new Rect(x, y, width, 20f),
                $"TARGET  {distance:0.0}m  →  {brain.DesiredDistance:0.0}m {brain.DesiredBand}" +
                $"    facing {angle:0}°    {spacing}",
                _spacing);
            y += 23f;

            if (brain.CurrentAttack != null)
            {
                int frame = DisplayedMonsterAttackFrame();
                GUI.Label(
                    new Rect(x, y, width, 19f),
                    $"ACTION  {brain.CurrentAttack.name}    sequence {brain.SequenceStep + 1}/{brain.SequenceLength}",
                    _body);
                y += 20f;
                DrawAttackTimeline(new Rect(x, y, width, 21f), brain.CurrentAttack, frame);
            }
            else
            {
                GUI.Label(new Rect(x, y, width, 19f), "ACTION  -", _body);
                y += 20f;
                DrawMonsterStateTimeline(new Rect(x, y, width, 21f), stateColor);
            }
            y += 28f;

            GUI.Label(
                new Rect(x, y, width, 38f),
                $"INTENT  {DescribeMonsterIntent(distance)}",
                _smallWrap);
            y += 40f;

            string trace = string.IsNullOrEmpty(brain.CurrentThkTrace)
                ? "waiting for next table selection"
                : brain.CurrentThkTrace;
            GUI.Label(
                new Rect(x, y, width, 42f),
                $"THK TRACE  {trace}",
                _smallWrap);
            y += 45f;

            DrawPartState(x, y, width);
        }

        void DrawPartState(float x, float y, float width)
        {
            int activeCount = 0;
            for (int i = 0; i < _parts.Length; i++)
            {
                BodyPartHurtbox part = _parts[i];
                if (part == null) continue;
                float stagger = brain.GetAccumulatedStagger(part.part);
                if (part.IsBroken || part.AccumulatedDamage > 0f || stagger > 0f) activeCount++;
            }

            if (activeCount == 0)
            {
                GUI.Label(new Rect(x, y, width, 19f), "PARTS  no accumulated damage or stagger", _small);
                return;
            }

            GUI.Label(new Rect(x, y, width, 18f), $"PARTS  {activeCount} active gauge(s)", _small);
            y += 17f;
            int shown = 0;
            for (int i = 0; i < _parts.Length && shown < 3; i++)
            {
                BodyPartHurtbox part = _parts[i];
                if (part == null) continue;
                float stagger = brain.GetAccumulatedStagger(part.part);
                if (!part.IsBroken && part.AccumulatedDamage <= 0f && stagger <= 0f) continue;

                GUI.Label(
                    new Rect(x + 8f, y, width - 8f, 17f),
                    $"{part.name}: break {part.AccumulatedDamage:0}/{part.breakThreshold:0}" +
                    $"  shared {part.part} stagger {stagger:0}/{part.staggerThreshold:0}" +
                    (part.IsBroken ? "  BROKEN" : string.Empty),
                    _small);
                y += 17f;
                shown++;
            }

            if (activeCount > shown)
                GUI.Label(new Rect(x + 8f, y, width - 8f, 17f), $"+ {activeCount - shown} more", _small);
        }

        void DrawWorldLabels(float uiScale)
        {
            Camera camera = Camera.main;
            if (camera == null) return;

            if (hunterController != null)
            {
                string detail = hunterController.CurrentAttack != null
                    ? $"{hunterController.CurrentAttack.name} • {PhaseText(ClassifyAttackPhase(hunterController.CurrentAttack, DisplayedHunterAttackFrame()))}"
                    : $"WP00 {hunterController.CurrentNode}";
                DrawActorLabel(
                    camera,
                    hunterController.transform.position + Vector3.up * 2.15f,
                    224f,
                    $"PLAYER  •  {hunterController.CurrentState}\n{detail}",
                    PlayerStateColor(hunterController.CurrentState),
                    uiScale);
            }

            if (brain != null)
            {
                string detail = brain.CurrentAttack != null
                    ? $"{brain.CurrentAttack.name} • {PhaseText(ClassifyAttackPhase(brain.CurrentAttack, DisplayedMonsterAttackFrame()))}"
                    : brain.CurrentContext.ToString();
                DrawActorLabel(
                    camera,
                    brain.transform.position + Vector3.up * 5.25f,
                    260f,
                    $"AI  •  {brain.CurrentState}\n{detail}",
                    MonsterStateColor(brain.CurrentState),
                    uiScale);
            }
        }

        void DrawActorLabel(
            Camera camera,
            Vector3 worldPosition,
            float width,
            string text,
            Color accent,
            float uiScale)
        {
            Vector3 screen = camera.WorldToScreenPoint(worldPosition);
            if (screen.z <= 0f) return;

            float x = screen.x / uiScale - width * 0.5f;
            float y = (Screen.height - screen.y) / uiScale - 41f;
            Rect rect = new Rect(x, y, width, 39f);
            DrawSolid(rect, new Color(0.02f, 0.03f, 0.045f, 0.82f));
            DrawSolid(new Rect(rect.x, rect.y, 4f, rect.height), accent);
            GUI.Label(rect, text, _worldLabel);
        }

        void DrawAttackTimeline(Rect rect, AttackDefinition attack, int frame)
        {
            DrawSolid(rect, TrackColor);
            int total = Mathf.Max(1, attack.TotalFrames);
            float startupWidth = rect.width * attack.startupFrames / total;
            float activeWidth = rect.width * attack.activeFrames / total;
            float recoveryWidth = Mathf.Max(0f, rect.width - startupWidth - activeWidth);

            DrawSolid(new Rect(rect.x, rect.y, startupWidth, rect.height), StartupColor);
            DrawSolid(new Rect(rect.x + startupWidth, rect.y, activeWidth, rect.height), ActiveColor);
            DrawSolid(new Rect(rect.x + startupWidth + activeWidth, rect.y, recoveryWidth, rect.height), RecoveryColor);

            float playhead = rect.x + rect.width * Mathf.Clamp01((frame + 0.5f) / total);
            DrawSolid(new Rect(playhead - 1f, rect.y - 2f, 2f, rect.height + 4f), Color.white);

            CombatDebugAttackPhase phase = ClassifyAttackPhase(attack, frame);
            GUI.Label(
                rect,
                $"{PhaseText(phase).ToUpperInvariant()}   f{Mathf.Clamp(frame, 0, total)}/{total}",
                _barText);
        }

        void DrawChargeTimeline(Rect rect)
        {
            int[] thresholds = hunterController.chargeThresholds;
            int lastThreshold = thresholds != null && thresholds.Length > 0
                ? thresholds[thresholds.Length - 1]
                : 1;
            int total = Mathf.Max(1, lastThreshold + Mathf.Max(0, hunterController.overchargeFrames));
            float progress = Mathf.Clamp01((float)hunterController.ChargeFrames / total);

            DrawSolid(rect, TrackColor);
            DrawSolid(new Rect(rect.x, rect.y, rect.width * progress, rect.height), StartupColor);
            if (thresholds != null)
            {
                for (int i = 0; i < thresholds.Length; i++)
                {
                    float marker = rect.x + rect.width * Mathf.Clamp01((float)thresholds[i] / total);
                    DrawSolid(new Rect(marker, rect.y, 1f, rect.height), Color.white);
                }
            }

            GUI.Label(
                rect,
                $"HOLD f{hunterController.ChargeFrames}/{total}   POWER Lv{hunterController.ChargeLevel}",
                _barText);
        }

        void DrawMonsterStateTimeline(Rect rect, Color stateColor)
        {
            int duration = 0;
            switch (brain.CurrentState)
            {
                case VelkhanaState.Observe:
                    duration = CurrentMonsterPacingFrames();
                    break;
                case VelkhanaState.Reposition:
                    duration = brain.maxRepositionFrames;
                    break;
                case VelkhanaState.RageTransition:
                    duration = brain.rageTransitionFrames;
                    break;
                case VelkhanaState.Takeoff:
                    duration = brain.takeoffFrames;
                    break;
                case VelkhanaState.Landing:
                    duration = brain.landingFrames;
                    break;
                case VelkhanaState.Toppled:
                    duration = brain.ActiveToppleFrames;
                    break;
            }

            if (duration > 0)
            {
                DrawSimpleTimeline(
                    rect,
                    brain.StateFrame,
                    duration,
                    stateColor,
                    $"{brain.CurrentState.ToString().ToUpperInvariant()}  f{brain.StateFrame}/{duration}");
            }
            else
            {
                DrawSimpleTimeline(rect, 0, 1, stateColor, brain.CurrentState.ToString().ToUpperInvariant());
            }
        }

        void DrawSimpleTimeline(Rect rect, int frame, int duration, Color color, string label)
        {
            DrawSolid(rect, TrackColor);
            float fraction = duration <= 0 ? 0f : Mathf.Clamp01((float)frame / duration);
            DrawSolid(new Rect(rect.x, rect.y, rect.width * fraction, rect.height), color);
            GUI.Label(rect, label, _barText);
        }

        void DrawBar(Rect rect, float fraction, Color color, string label)
        {
            DrawSolid(rect, TrackColor);
            DrawSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
            GUI.Label(rect, label, _barText);
        }

        void DrawBadge(Rect rect, string label, Color color)
        {
            DrawSolid(rect, new Color(color.r, color.g, color.b, 0.8f));
            GUI.Label(rect, label.ToUpperInvariant(), _badge);
        }

        void DrawPanel(Rect panel, Color accent)
        {
            DrawSolid(panel, PanelColor);
            DrawSolid(new Rect(panel.x, panel.y, 4f, panel.height), accent);
            DrawSolid(new Rect(panel.x, panel.y, panel.width, 2f), new Color(accent.r, accent.g, accent.b, 0.8f));
        }

        static void DrawSolid(Rect rect, Color color)
        {
            if (rect.width <= 0f || rect.height <= 0f) return;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        string DescribePlayerIntent()
        {
            switch (hunterController.CurrentState)
            {
                case HunterController.State.Charging:
                    int nextThreshold = NextChargeThreshold();
                    return nextThreshold > 0
                        ? $"hold {hunterController.CurrentChargeStage} charge; next power threshold at f{nextThreshold}."
                        : "full charge reached; release before the overcharge downgrade.";
                case HunterController.State.Attacking:
                    int frame = DisplayedHunterAttackFrame();
                    string tracking = hunterController.CurrentAttack != null && hunterController.CurrentAttack.CanTrack(frame)
                        ? "tracking open"
                        : "direction committed";
                    HunterController.Wp00Node buffered = hunterController.BufferedNode;
                    string buffer = buffered == HunterController.Wp00Node.NoTransition
                        ? "no follow-up buffered"
                        : $"buffered → {buffered}";
                    return $"complete the authored action; {tracking}; {buffer}.";
                case HunterController.State.Guarding:
                    return "hold Great Sword guard; Triangle/primary routes to Kick (WP00 N024).";
                case HunterController.State.Rolling:
                    return hunterController.IsInvulnerable
                        ? "evade is inside its invulnerability window."
                        : "finish the evade; invulnerability is currently inactive.";
                case HunterController.State.Launched:
                    return hunterController.IsKnockedDown
                        ? "grounded knockdown recovery; input is locked."
                        : "airborne launch reaction; input is locked.";
                default:
                    if (hunterController.IsWeaponTransitioning)
                        return hunterController.WeaponTransitionDrawn
                            ? "draw weapon; attack input can enter the decoded draw route."
                            : "sheathe weapon; attack input can cancel into the draw route.";
                    if (hunterController.IsRunning) return "run while sheathed; combat input starts a draw route.";
                    return "free locomotion; waiting for a movement or combat input edge.";
            }
        }

        string DescribeMonsterIntent(float currentDistance)
        {
            switch (brain.CurrentState)
            {
                case VelkhanaState.Observe:
                    if (brain.IsAirborne)
                        return "dispatch Combat_Main.node_006's aerial family on the next fixed step.";
                    int remaining = Mathf.Max(0, CurrentMonsterPacingFrames() - brain.StateFrame);
                    return $"turn toward the hunter, then evaluate the ground THK tables in {remaining}f.";
                case VelkhanaState.Reposition:
                    string direction = currentDistance > brain.DesiredDistance + brain.repositionDistanceTolerance
                        ? "close distance"
                        : currentDistance < brain.DesiredDistance - brain.repositionDistanceTolerance
                            ? "create distance"
                            : "orbit while turning";
                    return $"{direction} toward the {brain.DesiredBand} option target ({brain.DesiredDistance:0.0}m).";
                case VelkhanaState.Attacking:
                    int frame = DisplayedMonsterAttackFrame();
                    string tracking = brain.CurrentAttack != null && brain.CurrentAttack.CanTrack(frame)
                        ? "tracking the hunter"
                        : "aim committed";
                    return $"finish this uncancellable THK action; {tracking}.";
                case VelkhanaState.Recovery:
                    return "finish the current action's authored recovery; no early reselection.";
                case VelkhanaState.RageTransition:
                    return "complete the rage roar transition before returning to combat selection.";
                case VelkhanaState.Takeoff:
                    return "finish takeoff, enter airborne context, then start the pending action or chooser.";
                case VelkhanaState.Landing:
                    return "finish landing before restoring the grounded combat table.";
                case VelkhanaState.Toppled:
                    return $"remain punishable for {brain.ToppleFramesRemaining}f ({brain.CurrentToppleCause}).";
                default:
                    return "observe target and await the next fixed-step decision.";
            }
        }

        int NextChargeThreshold()
        {
            int[] thresholds = hunterController.chargeThresholds;
            if (thresholds == null) return -1;
            for (int i = 0; i < thresholds.Length; i++)
                if (thresholds[i] > hunterController.ChargeFrames) return thresholds[i];
            return -1;
        }

        int CurrentMonsterPacingFrames()
        {
            return VelkhanaBrain.ProjectGroundResetPacingFrames(
                brain.neutralFrames,
                brain.stage != ArmorStage.Neutral,
                brain.enraged,
                brain.CurrentContext == VelkhanaContext.CriticalHealth,
                brain.poweredPacingMultiplier,
                brain.enragedPacingMultiplier,
                brain.criticalPacingMultiplier);
        }

        string MonsterSpacing(out float distance, out float angle, out Color color)
        {
            if (brain.hunter == null)
            {
                distance = 0f;
                angle = 0f;
                color = tooFarColor;
                return "NO TARGET";
            }

            Vector3 offset = brain.hunter.position - brain.transform.position;
            offset.y = 0f;
            distance = offset.magnitude;
            angle = VelkhanaBrain.AbsoluteFacingAngle(brain.transform.forward, offset);

            float tolerance = Mathf.Max(0f, brain.repositionDistanceTolerance);
            if (distance > brain.DesiredDistance + tolerance)
            {
                color = tooFarColor;
                return "TOO FAR";
            }
            if (distance < brain.DesiredDistance - tolerance)
            {
                color = tooCloseColor;
                return "TOO CLOSE";
            }

            color = inRangeColor;
            return "IN TARGET BAND";
        }

        int DisplayedHunterAttackFrame()
        {
            return CombatVolumeDebug.SimulatedFrameForDisplay(
                hunterController.AttackFrame,
                hunterController.LastSimulatedAttackFrame);
        }

        int DisplayedMonsterAttackFrame()
        {
            return CombatVolumeDebug.SimulatedFrameForDisplay(
                brain.AttackFrame,
                brain.LastSimulatedAttackFrame);
        }

        void CreateWorldGeometry()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null)
                _worldLineMaterial = new Material(shader) { name = "CombatStateIntentMaterial" };

            _targetLine = CreateWorldLine("AiTargetLine", false);
            _desiredRangeRing = CreateWorldLine("AiDesiredRange", true);
        }

        LineRenderer CreateWorldLine(string objectName, bool loop)
        {
            var lineObject = new GameObject(objectName);
            lineObject.transform.SetParent(transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = intentLineWidth;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = 90;
            if (_worldLineMaterial != null) line.sharedMaterial = _worldLineMaterial;
            line.enabled = false;
            return line;
        }

        void DrawWorldIntentGeometry()
        {
            bool canDraw = Visible && showAiSpacingIntent && brain != null && brain.hunter != null;
            if (!canDraw)
            {
                if (_targetLine != null) _targetLine.enabled = false;
                if (_desiredRangeRing != null) _desiredRangeRing.enabled = false;
                return;
            }

            MonsterSpacing(out float distance, out _, out Color color);
            Vector3 monsterPoint = brain.transform.position + Vector3.up * 0.18f;
            Vector3 hunterPoint = brain.hunter.position + Vector3.up * 0.18f;
            _targetLine.positionCount = 2;
            _targetLine.SetPosition(0, monsterPoint);
            _targetLine.SetPosition(1, hunterPoint);
            _targetLine.startColor = _targetLine.endColor = color;
            _targetLine.enabled = true;

            bool showRing = brain.CurrentState == VelkhanaState.Reposition && brain.DesiredDistance > 0f;
            if (!showRing)
            {
                _desiredRangeRing.enabled = false;
                return;
            }

            const int segments = 64;
            float groundY = Mathf.Min(brain.transform.position.y, brain.hunter.position.y) + 0.08f;
            Vector3 center = brain.hunter.position;
            center.y = groundY;
            _desiredRangeRing.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float radians = i * Mathf.PI * 2f / segments;
                _desiredRangeRing.SetPosition(
                    i,
                    center + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * brain.DesiredDistance);
            }
            Color ringColor = new Color(color.r, color.g, color.b, color.a * 0.65f);
            _desiredRangeRing.startColor = _desiredRangeRing.endColor = ringColor;
            _desiredRangeRing.enabled = true;
        }

        void EnsureStyles()
        {
            if (_title != null) return;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            _body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.95f, 1f) },
            };
            _small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.76f, 0.82f, 0.9f) },
            };
            _smallWrap = new GUIStyle(_small)
            {
                wordWrap = true,
                clipping = TextClipping.Clip,
            };
            _spacing = new GUIStyle(_small);
            _badge = new GUIStyle(_body)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
            };
            _barText = new GUIStyle(_small)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            _worldLabel = new GUIStyle(_small)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
        }

        static Color PlayerStateColor(HunterController.State state)
        {
            switch (state)
            {
                case HunterController.State.Charging: return StartupColor;
                case HunterController.State.Attacking: return ActiveColor;
                case HunterController.State.Guarding: return new Color(0.25f, 0.55f, 1f);
                case HunterController.State.Rolling: return new Color(0.15f, 0.9f, 1f);
                case HunterController.State.Launched: return new Color(0.75f, 0.35f, 1f);
                default: return new Color(0.2f, 0.85f, 0.35f);
            }
        }

        static Color MonsterStateColor(VelkhanaState state)
        {
            switch (state)
            {
                case VelkhanaState.Reposition: return new Color(0.15f, 0.9f, 1f);
                case VelkhanaState.Attacking: return ActiveColor;
                case VelkhanaState.Recovery: return RecoveryColor;
                case VelkhanaState.RageTransition: return new Color(1f, 0.3f, 0.08f);
                case VelkhanaState.Takeoff:
                case VelkhanaState.Landing: return new Color(0.35f, 0.65f, 1f);
                case VelkhanaState.Toppled: return new Color(0.25f, 1f, 0.4f);
                default: return new Color(0.55f, 0.64f, 0.75f);
            }
        }

        /// <summary>
        /// Uses the same half-open startup/active/recovery boundaries as AttackDefinition. Kept
        /// public so edit-mode tests can protect the debugger from displaying a phase one frame early.
        /// </summary>
        public static CombatDebugAttackPhase ClassifyAttackPhase(AttackDefinition attack, int frame)
        {
            if (attack == null) return CombatDebugAttackPhase.None;
            if (frame < attack.startupFrames) return CombatDebugAttackPhase.Startup;
            if (frame < attack.startupFrames + attack.activeFrames) return CombatDebugAttackPhase.Active;
            if (frame < attack.TotalFrames) return CombatDebugAttackPhase.Recovery;
            return CombatDebugAttackPhase.Complete;
        }

        static string PhaseText(CombatDebugAttackPhase phase)
        {
            switch (phase)
            {
                case CombatDebugAttackPhase.Startup: return "Startup";
                case CombatDebugAttackPhase.Active: return "Active";
                case CombatDebugAttackPhase.Recovery: return "Recovery";
                case CombatDebugAttackPhase.Complete: return "Complete";
                default: return "None";
            }
        }

        static string FlatPosition(Vector3 position)
        {
            return $"({position.x:0.0}, {position.z:0.0})";
        }

        static string OnOff(bool value)
        {
            return value ? "ON" : "off";
        }

        void OnDestroy()
        {
            if (_worldLineMaterial != null) Destroy(_worldLineMaterial);
        }
    }
}
