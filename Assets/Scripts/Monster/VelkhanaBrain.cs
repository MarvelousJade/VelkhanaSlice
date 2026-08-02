using System;
using System.Collections.Generic;
using UnityEngine;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;

namespace VelkhanaSlice.Monster
{
    public enum ArmorStage
    {
        Neutral = 0,
        IceArmorStage1 = 1,
        IceArmorStage2 = 2,
        Ultimate = 3,
    }

    public enum RangeBand
    {
        Close,
        Medium,
        Far,
    }

    /// <summary>
    /// Readable high-level states around EM124's THK action tables. Multi-step attacks remain
    /// individual AttackDefinitions so every interrupt boundary is visible in the HUD and tests.
    /// </summary>
    public enum VelkhanaState
    {
        Observe,
        Reposition,
        Attacking,
        Recovery,
        RageTransition,
        Takeoff,
        Landing,
    }

    public enum VelkhanaContext
    {
        CombatEntry,
        GroundCombat,
        AerialCombat,
        RageTransition,
        CriticalHealth,
    }

    /// <summary>
    /// EM124 function#101 selects three table buckets. Its real engine meaning is unresolved, so
    /// the demo keeps the honest Mode0/1/2 names while mapping the buckets to its visible ice stages.
    /// </summary>
    public enum VelkhanaCombatMode
    {
        Mode0,
        Mode1,
        Mode2,
    }

    [Flags]
    public enum VelkhanaCombatModeMask
    {
        None = 0,
        Mode0 = 1 << 0,
        Mode1 = 1 << 1,
        Mode2 = 1 << 2,
        All = Mode0 | Mode1 | Mode2,
    }

    public enum VelkhanaAirRequirement
    {
        Grounded,
        Airborne,
        Either,
    }

    public enum VelkhanaAerialOptionFamily
    {
        None,
        Global051,
        Global052,
    }

    public enum VelkhanaGroundOpenerParent
    {
        None,
        Global105,
        Global106,
        Global108,
    }

    public enum VelkhanaNode087Leaf
    {
        None,
        Global004,
        Global006,
        Global009,
    }

    public enum VelkhanaGroundContinuationNode
    {
        None,
        Global088,
        Global089,
        Global090,
    }

    public enum VelkhanaGroundContinuationTarget
    {
        None,
        Global004,
        Global006,
        Global009,
        Global079,
    }

    /// <summary>One weighted THK-style action or action sequence.</summary>
    [Serializable]
    public class MonsterAttackOption
    {
        public AttackDefinition attack;
        public RangeBand band = RangeBand.Close;

        [Tooltip("Lowest armour stage this action is available in.")]
        public ArmorStage minimumStage = ArmorStage.Neutral;

        [Tooltip("Selection weight before mode, rage, health and history modifiers.")]
        public float weight = 1f;

        [Tooltip("Additional weight used by EM124's distinct enraged selection branch.")]
        [Min(0f)] public float enragedWeightMultiplier = 1f;

        [Tooltip("Additional weight in the critical-health context.")]
        [Min(0f)] public float criticalWeightMultiplier = 1f;

        [Tooltip("Frames before this action may be chosen again.")]
        public int cooldownFrames = 180;

        [Tooltip("Legacy broad-band option: only usable when the hunter is roughly in front.")]
        public bool requiresHunterInFront;

        [Header("Decoded EM124 context")]
        [Tooltip("Use exact distance/facing/mode conditions instead of the legacy three bands.")]
        public bool useEm124Conditions;

        [Tooltip("Decoded Global node that inspired this semantic action, for HUD/debug tracing.")]
        public string thkNode;

        [Min(0f)] public float minimumDistance;
        [Min(0f)] public float maximumDistance = 28f;
        [Min(0f)] public float maximumVerticalDistance = 7.5f;

        [Tooltip("Minimum absolute angle from Velkhana's forward direction.")]
        [Range(0f, 180f)] public float minimumFacingAngle;

        [Tooltip("Maximum absolute angle from Velkhana's forward direction.")]
        [Range(0f, 180f)] public float maximumFacingAngle = 180f;

        public VelkhanaCombatModeMask modes = VelkhanaCombatModeMask.All;
        public VelkhanaAirRequirement airRequirement = VelkhanaAirRequirement.Grounded;

        [Tooltip("Combat_Main.node_006 airborne family. None marks ordinary ground options.")]
        public VelkhanaAerialOptionFamily aerialFamily;

        [Tooltip("Participates in the generic weighted ground table. Disable for hierarchical lookup-only leaves.")]
        public bool useInFlatGroundSelector = true;

        public bool calmOnly;
        public bool enragedOnly;

        [Tooltip("Per-function#101-bucket weight multipliers.")]
        [Min(0f)] public float mode0WeightMultiplier = 1f;
        [Min(0f)] public float mode1WeightMultiplier = 1f;
        [Min(0f)] public float mode2WeightMultiplier = 1f;

        [Header("THK-style sequence")]
        [Tooltip("Steps used after the first action while calm.")]
        public AttackDefinition[] calmFollowUps = Array.Empty<AttackDefinition>();

        [Tooltip("Steps used after the first action while enraged.")]
        public AttackDefinition[] enragedFollowUps = Array.Empty<AttackDefinition>();

        [Tooltip("Enter the aerial context before playing this sequence.")]
        public bool takeOffBeforeSequence;

        [Tooltip("Ground entry only: finish takeoff in airborne Observe, then run Combat_Main.node_006.")]
        public bool enterAerialChooserAfterTakeoff;

        [Tooltip("Return to the ground after this sequence.")]
        public bool landAfterSequence;

        [NonSerialized] public int CooldownRemaining;
    }

    /// <summary>
    /// A deterministic, frame-stepped interpretation of the decoded EM124 combat shape:
    /// context gate, interrupt pass, function#101 mode bucket, weighted selector, and semantic
    /// action sequences. It intentionally does not claim that unresolved engine predicates have
    /// been reverse engineered.
    /// </summary>
    public class VelkhanaBrain : MonoBehaviour, IAttacker
    {
        /// <summary>
        /// Project-only readability/reset floor. This is not a decoded EM124 timing.
        /// </summary>
        public const int ProjectMinimumGroundResetFrames = 24;

        [Header("Target")]
        public Transform hunter;

        [Tooltip("Layers searched for the hunter when an attack's hitbox is active.")]
        public LayerMask hunterLayers = ~0;

        [Header("Combat entry (metres)")]
        [Tooltip("EM124 Combat_Enter: distance_2d <= 850 game units.")]
        public float closeRange = 8.5f;

        [Tooltip("EM124 Combat_Enter: otherwise distance_3d <= 1700 game units.")]
        public float mediumRange = 17f;

        [Tooltip("EM124 Combat_Enter: vertical distance <= 750 game units in the close branch.")]
        public float combatEntryVerticalRange = 7.5f;

        [Header("Pacing (frames @ 60 Hz)")]
        public int neutralFrames = 42;
        [Range(0.1f, 1f)] public float poweredPacingMultiplier = 0.65f;
        [Range(0.1f, 1f)] public float enragedPacingMultiplier = 0.72f;
        [Range(0.1f, 1f)] public float criticalPacingMultiplier = 0.78f;
        public float idleTurnDegreesPerSecond = 90f;

        [Header("Repositioning (no NavMesh)")]
        public float repositionSpeed = 3.2f;
        public float enragedRepositionSpeed = 4.2f;
        public float repositionTurnDegreesPerSecond = 150f;
        public float repositionDistanceTolerance = 1.25f;
        [Min(1)] public int repositionDecisionIntervalFrames = 12;
        [Min(1)] public int maxRepositionFrames = 240;
        [Range(0f, 1f)] public float repositionOrbitWeight = 0.55f;
        public Vector3 arenaCenter = Vector3.zero;
        [Min(1f)] public float arenaRadius = 28f;

        [Header("Aerial context (frames @ 60 Hz)")]
        [Min(1)] public int takeoffFrames = 42;
        [Min(1)] public int landingFrames = 36;

        [Tooltip("Unresolved function#101() predicate used only by Combat_Main.node_006.")]
        public bool combatMainNode006Predicate101;

        [Header("Decoded action table")]
        public List<MonsterAttackOption> options = new List<MonsterAttackOption>();
        public bool deterministicSelection = true;
        public int selectionSeed = 124;

        [Header("Rage")]
        [Tooltip("If enabled, dealt damage fills a rage threshold like Monster Hunter's rage buildup.")]
        public bool automaticEnrage;
        [Min(1f)] public float rageDamageThreshold = 380f;
        [Min(1)] public int rageTransitionFrames = 78;
        [Min(1)] public int rageDurationFrames = 900;
        [Min(0)] public int rageCooldownFrames = 360;
        public bool enraged;

        [Header("Vitality")]
        [Min(1f)] public float maxHealth = 3000f;
        [Range(0.05f, 0.9f)] public float criticalHealthFraction = 0.25f;

        [Header("Ice armour cycle")]
        public ArmorStage stage = ArmorStage.Neutral;
        public bool automaticPhaseProgression;
        [Min(1)] public int completedSequencesPerStage = 3;
        [Min(1)] public int ultimateDurationFrames = 600;
        [Min(0)] public int armorRebuildLockoutFrames = 360;
        public float armorPerPart = 300f;
        public int armorBreaksToInterrupt = 2;
        public BodyPartHurtbox[] armoredParts = Array.Empty<BodyPartHurtbox>();

        [Header("Repetition")]
        [Range(0f, 1f)] public float repeatPenalty = 0.25f;
        [Range(0f, 1f)] public float recentHistoryPenalty = 0.65f;

        public AttackDefinition CurrentAttack { get; private set; }
        public int AttackFrame { get; private set; }
        public VelkhanaState CurrentState { get; private set; } = VelkhanaState.Observe;
        public int StateFrame { get; private set; }
        public RangeBand DesiredBand { get; private set; } = RangeBand.Medium;
        public float DesiredDistance { get; private set; } = 12.75f;
        public VelkhanaContext CurrentContext { get; private set; } = VelkhanaContext.CombatEntry;
        public VelkhanaCombatMode CombatMode { get; private set; } = VelkhanaCombatMode.Mode0;
        public bool IsAirborne { get; private set; }
        public int SequenceStep { get; private set; }
        public int SequenceLength { get; private set; } = 1;
        public string CurrentThkNode { get; private set; } = string.Empty;
        public string CurrentThkTrace { get; private set; } = string.Empty;
        public bool IsGroundOpenerSliceActive { get; private set; }
        public float CurrentHealth { get; private set; }
        public float HealthFraction => maxHealth <= 0f ? 0f : Mathf.Clamp01(CurrentHealth / maxHealth);
        public float RageBuild => rageDamageThreshold <= 0f
            ? 0f
            : Mathf.Clamp01(_rageDamage / rageDamageThreshold);

        public event Action<ArmorStage> StageChanged;
        public event Action<VelkhanaState> StateChanged;
        public event Action<bool> EnrageChanged;
        public event Action<float, float> HealthChanged;

        int _armorBreaks;
        int _completedSinceStage;
        int _ultimateFramesRemaining;
        int _armorLockoutRemaining;
        int _rageFramesRemaining;
        int _rageCooldownRemaining;
        float _rageDamage;
        float _orbitSign = 1f;
        bool _ragePending;
        MonsterAttackOption _activeOption;
        MonsterAttackOption _pendingTakeoffOption;
        AttackDefinition[] _followUps = Array.Empty<AttackDefinition>();
        MonsterAttackOption _lastUsed;
        VelkhanaNode087Leaf _groundOpenerLeaf;
        readonly Queue<MonsterAttackOption> _recentOptions = new Queue<MonsterAttackOption>(3);
        Vector3 _committedAimDirection;
        System.Random _random;
        int _randomSeed;

        readonly Collider[] _overlapBuffer = new Collider[32];
        readonly HashSet<HunterHealth> _hitThisAttack = new HashSet<HunterHealth>();
        readonly List<BodyPartHurtbox> _subscribedParts = new List<BodyPartHurtbox>();

        void Awake()
        {
            CurrentHealth = Mathf.Max(1f, maxHealth);
            _random = new System.Random(selectionSeed);
            _randomSeed = selectionSeed;
            DesiredDistance = DesiredDistanceForBand(DesiredBand, closeRange, mediumRange);

            if (enraged)
            {
                _rageFramesRemaining = Mathf.Max(1, rageDurationFrames);
                CurrentContext = VelkhanaContext.RageTransition;
            }
        }

        void Start()
        {
            // AddComponent invokes OnEnable before builders/tests have assigned serialized arrays.
            RefreshHurtboxBindings();
        }

        void OnEnable()
        {
            RefreshHurtboxBindings();
        }

        void OnDisable()
        {
            UnsubscribeHurtboxes();
        }

        public void RefreshHurtboxBindings()
        {
            UnsubscribeHurtboxes();

            var unique = new HashSet<BodyPartHurtbox>();
            foreach (BodyPartHurtbox part in GetComponentsInChildren<BodyPartHurtbox>(true))
                if (part != null) unique.Add(part);

            if (armoredParts != null)
                foreach (BodyPartHurtbox part in armoredParts)
                    if (part != null) unique.Add(part);

            foreach (BodyPartHurtbox part in unique)
            {
                part.Damaged += OnPartDamaged;
                part.IceArmorShattered += OnArmorShattered;
                _subscribedParts.Add(part);
            }
        }

        void UnsubscribeHurtboxes()
        {
            for (int i = 0; i < _subscribedParts.Count; i++)
            {
                BodyPartHurtbox part = _subscribedParts[i];
                if (part == null) continue;
                part.Damaged -= OnPartDamaged;
                part.IceArmorShattered -= OnArmorShattered;
            }

            _subscribedParts.Clear();
        }

        void FixedUpdate()
        {
            TickCooldowns();
            TickRageAndPhase();
            UpdateContext();

            switch (CurrentState)
            {
                case VelkhanaState.Attacking:
                case VelkhanaState.Recovery:
                    TickAttack();
                    break;
                case VelkhanaState.Reposition:
                    TickReposition();
                    break;
                case VelkhanaState.RageTransition:
                    TickRageTransition();
                    break;
                case VelkhanaState.Takeoff:
                    TickTakeoff();
                    break;
                case VelkhanaState.Landing:
                    TickLanding();
                    break;
                default:
                    TickObserve();
                    break;
            }

            // State ticks can synchronously enter Takeoff/Landing/Observe or change IsAirborne.
            // Refresh again so CurrentContext describes the state exposed at this frame boundary.
            UpdateContext();
        }

        void TickObserve()
        {
            if (TryEnterPendingRage()) return;

            TurnTowardHunter(idleTurnDegreesPerSecond);

            if (IsAirborne)
            {
                if (hunter == null)
                {
                    BeginLanding();
                    return;
                }

                MonsterAttackOption aerial = ChooseCombatMainNode006();
                if (aerial != null) StartOption(aerial);
                else BeginLanding();
                return;
            }

            StateFrame++;

            int pacing = ProjectGroundResetPacingFrames(
                neutralFrames,
                stage != ArmorStage.Neutral,
                enraged,
                CurrentContext == VelkhanaContext.CriticalHealth,
                poweredPacingMultiplier,
                enragedPacingMultiplier,
                criticalPacingMultiplier);

            if (StateFrame < pacing) return;

            if (TryChooseGroundDecision(
                    out MonsterAttackOption picked,
                    out string thkTrace,
                    out bool openerSlice))
            {
                StartOption(picked, thkTrace, openerSlice);
                return;
            }

            ChooseRepositionTarget();
            Vector3 toHunter = DirectionToHunter();
            _orbitSign = Vector3.Dot(transform.right, toHunter) >= 0f ? -1f : 1f;
            EnterState(VelkhanaState.Reposition);
        }

        void TickReposition()
        {
            if (TryEnterPendingRage()) return;
            if (hunter == null)
            {
                EnterState(VelkhanaState.Observe);
                return;
            }

            StateFrame++;
            TurnTowardHunter(repositionTurnDegreesPerSecond);

            if (StateFrame == 1 || StateFrame % Mathf.Max(1, repositionDecisionIntervalFrames) == 0)
            {
                if (TryChooseGroundDecision(
                        out MonsterAttackOption picked,
                        out string thkTrace,
                        out bool openerSlice))
                {
                    StartOption(picked, thkTrace, openerSlice);
                    return;
                }

                ChooseRepositionTarget();
            }

            Vector3 moveDirection = CalculateRepositionDirection(
                transform.position,
                transform.forward,
                hunter.position,
                DesiredDistance,
                repositionDistanceTolerance,
                _orbitSign,
                repositionOrbitWeight);

            float speed = enraged ? enragedRepositionSpeed : repositionSpeed;
            Vector3 next = transform.position +
                           moveDirection * (Mathf.Max(0f, speed) * Time.fixedDeltaTime);
            transform.position = ClampToArena(next);

            if (StateFrame >= Mathf.Max(1, maxRepositionFrames))
                EnterState(VelkhanaState.Observe);
        }

        void StartOption(
            MonsterAttackOption picked,
            string thkTrace = null,
            bool openerSlice = false)
        {
            _activeOption = picked;
            CurrentThkTrace = string.IsNullOrEmpty(thkTrace)
                ? DefaultThkTraceFor(picked)
                : thkTrace;
            IsGroundOpenerSliceActive = openerSlice;
            CurrentThkNode = picked != null ? picked.thkNode : string.Empty;
            _groundOpenerLeaf = openerSlice
                ? Node087LeafForNodeName(CurrentThkNode)
                : VelkhanaNode087Leaf.None;
            _lastUsed = picked;
            picked.CooldownRemaining = Mathf.Max(0, picked.cooldownFrames);
            _followUps = enraged ? picked.enragedFollowUps : picked.calmFollowUps;
            _followUps ??= Array.Empty<AttackDefinition>();
            SequenceStep = 0;
            SequenceLength = 1 + _followUps.Length;
            Remember(picked);

            if ((picked.takeOffBeforeSequence || picked.enterAerialChooserAfterTakeoff) &&
                !IsAirborne)
            {
                _pendingTakeoffOption = picked;
                EnterState(VelkhanaState.Takeoff);
                return;
            }

            StartAttackStep(picked.attack);
        }

        void StartAttackStep(AttackDefinition attack)
        {
            if (attack == null)
            {
                FinishSequence();
                return;
            }

            CurrentAttack = attack;
            AttackFrame = 0;
            _committedAimDirection = DirectionToHunter();
            _hitThisAttack.Clear();
            EnterState(VelkhanaState.Attacking);
        }

        void TickTakeoff()
        {
            TurnTowardHunter(repositionTurnDegreesPerSecond);
            if (++StateFrame < Mathf.Max(1, takeoffFrames)) return;

            IsAirborne = true;
            MonsterAttackOption option = _pendingTakeoffOption;
            _pendingTakeoffOption = null;
            if (option == null)
            {
                BeginLanding();
                return;
            }

            if (option.enterAerialChooserAfterTakeoff)
            {
                ClearSequence();
                EnterState(VelkhanaState.Observe);
                return;
            }

            StartAttackStep(option.attack);
        }

        void TickLanding()
        {
            TurnTowardHunter(idleTurnDegreesPerSecond);
            if (++StateFrame < Mathf.Max(1, landingFrames)) return;

            IsAirborne = false;
            ClearSequence();
            if (!TryEnterPendingRage()) EnterState(VelkhanaState.Observe);
        }

        void BeginLanding()
        {
            CurrentAttack = null;
            AttackFrame = 0;
            EnterState(VelkhanaState.Landing);
        }

        void TickRageTransition()
        {
            TurnTowardHunter(idleTurnDegreesPerSecond * 0.35f);
            if (++StateFrame < Mathf.Max(1, rageTransitionFrames)) return;

            _ragePending = false;
            CurrentContext = IsAirborne
                ? VelkhanaContext.AerialCombat
                : VelkhanaContext.GroundCombat;

            if (IsAirborne) BeginLanding();
            else EnterState(VelkhanaState.Observe);
        }

        bool TryEnterPendingRage()
        {
            if (!_ragePending || CurrentState == VelkhanaState.RageTransition) return false;
            EnterState(VelkhanaState.RageTransition);
            return true;
        }

        void TickAttack()
        {
            if (CurrentAttack == null)
            {
                FinishSequence();
                return;
            }

            int recoveryStart = CurrentAttack.startupFrames + CurrentAttack.activeFrames;
            if (AttackFrame >= recoveryStart && CurrentState != VelkhanaState.Recovery)
                EnterState(VelkhanaState.Recovery);

            StateFrame++;

            if (CurrentAttack.CanTrack(AttackFrame))
                _committedAimDirection = DirectionToHunter();

            float step = CurrentAttack.ForwardStep(AttackFrame);
            if (!Mathf.Approximately(step, 0f))
                transform.position = ClampToArena(
                    transform.position + _committedAimDirection * step);

            if (_committedAimDirection.sqrMagnitude > 0.001f && CurrentAttack.CanTrack(AttackFrame))
                transform.rotation = Quaternion.LookRotation(_committedAimDirection, Vector3.up);

            if (CurrentAttack.IsHitActive(AttackFrame)) CheckHunterHit(CurrentAttack);
            if (++AttackFrame < CurrentAttack.TotalFrames) return;

            if (IsGroundOpenerSliceActive && SequenceStep == 0)
            {
                if (TryStartGroundOpenerContinuation()) return;
                FinishSequence();
                return;
            }

            int nextIndex = SequenceStep;
            if (nextIndex < _followUps.Length && CanContinueSequence())
            {
                SequenceStep++;
                StartAttackStep(_followUps[nextIndex]);
                return;
            }

            FinishSequence();
        }

        bool CanContinueSequence()
        {
            if (hunter == null || _activeOption == null) return false;

            // Global.node_328 inserts an interrupt check between aerial/ground combo steps.
            // We expose the equivalent target/range/arena validity check here.
            float distance = Distance2DToHunter();
            float maximum = _activeOption.maximumDistance > 0f
                ? _activeOption.maximumDistance + 4f
                : 32f;
            if (distance > maximum) return false;

            Vector3 arenaOffset = hunter.position - arenaCenter;
            arenaOffset.y = 0f;
            return arenaOffset.sqrMagnitude <= arenaRadius * arenaRadius * 1.35f;
        }

        bool TryStartGroundOpenerContinuation()
        {
            if (!GroundOpenerTargetIsValid()) return false;

            float postMotionDistance = Distance2DToHunter();
            if (postMotionDistance > 13f) return false;

            VelkhanaGroundContinuationNode continuationNode =
                ContinuationNodeFor(_groundOpenerLeaf);
            VelkhanaGroundContinuationTarget target = SelectGroundOpenerContinuation(
                _groundOpenerLeaf, postMotionDistance, NextSelectionRoll100());

            bool throughNode079 = target == VelkhanaGroundContinuationTarget.Global079;
            VelkhanaNode087Leaf selectedLeaf;
            if (throughNode079)
            {
                Vector3 toArenaCenter = arenaCenter - transform.position;
                toArenaCenter.y = 0f;
                float distanceToArenaCenter = toArenaCenter.magnitude;

                if (distanceToArenaCenter <= 5f)
                {
                    // node_079's targetEnemy(50) retargets to this demo's sole hunter. The numeric
                    // argument is not treated as a probability; only the following random block
                    // consumes an RNG value.
                    selectedLeaf = SelectNode079NearLeaf(NextSelectionRoll100());
                }
                else
                {
                    Vector3 toHunter = hunter.position - transform.position;
                    toHunter.y = 0f;
                    selectedLeaf = SelectNode079FarLeaf(
                        DirectionIsInClockwiseSector270To90(transform.forward, toArenaCenter),
                        DirectionIsInClockwiseSector270To90(transform.forward, toHunter));
                }
            }
            else
            {
                selectedLeaf = ContinuationTargetToLeaf(target);
            }

            MonsterAttackOption selected = FindNode087Leaf(selectedLeaf);
            if (selected == null) return false;

            // Every node_088/089/090 path invokes node_076 before its action selector. The focused
            // demo is explicitly non-AT and does not model target(44), helpless predicates, or the
            // no-argument function#101(), so node_076 is retained as a conservative traceable no-op.
            string node079Trace = throughNode079 ? " > Global.node_079" : string.Empty;
            CurrentThkTrace +=
                $" > {GroundContinuationNodeName(continuationNode)} > " +
                $"Global.node_076{node079Trace} > {selected.thkNode}";
            CurrentThkNode = selected.thkNode;
            SequenceStep = 1;
            SequenceLength = 2;
            StartAttackStep(selected.attack);
            return true;
        }

        bool GroundOpenerTargetIsValid()
        {
            if (hunter == null) return false;

            // This is only the focused demo's safe arena-target check. Nodes 088/089/090 remain
            // inside node_087 and do not pass through node_328, so the generic option-range-plus-4
            // interrupt gate must not suppress their decoded <=13 m tables.
            Vector3 arenaOffset = hunter.position - arenaCenter;
            arenaOffset.y = 0f;
            return arenaOffset.sqrMagnitude <= arenaRadius * arenaRadius * 1.35f;
        }

        void FinishSequence()
        {
            bool land = _activeOption != null && _activeOption.landAfterSequence && IsAirborne;
            CurrentAttack = null;
            AttackFrame = 0;

            RegisterCompletedSequence();

            if (land)
            {
                BeginLanding();
                return;
            }

            ClearSequence();
            if (!TryEnterPendingRage()) EnterState(VelkhanaState.Observe);
        }

        void ClearSequence()
        {
            _activeOption = null;
            CurrentThkNode = string.Empty;
            CurrentThkTrace = string.Empty;
            IsGroundOpenerSliceActive = false;
            _groundOpenerLeaf = VelkhanaNode087Leaf.None;
            _followUps = Array.Empty<AttackDefinition>();
            SequenceStep = 0;
            SequenceLength = 1;
        }

        void RegisterCompletedSequence()
        {
            if (!automaticPhaseProgression || _armorLockoutRemaining > 0) return;
            if (stage == ArmorStage.Ultimate) return;

            if (++_completedSinceStage < Mathf.Max(1, completedSequencesPerStage)) return;
            _completedSinceStage = 0;
            SetStage(stage + 1);
        }

        void CheckHunterHit(AttackDefinition attack)
        {
            int count = AttackHitbox.Overlap(transform, attack, hunterLayers, _overlapBuffer);
            for (int i = 0; i < count; i++)
            {
                HunterHealth health = _overlapBuffer[i].GetComponentInParent<HunterHealth>();
                if (health == null || !_hitThisAttack.Add(health)) continue;
                health.TakeDamage(attack.damage);
            }
        }

        void TurnTowardHunter(float degreesPerSecond)
        {
            if (hunter == null) return;
            Vector3 direction = DirectionToHunter();
            if (direction.sqrMagnitude < 0.001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                Mathf.Max(0f, degreesPerSecond) * Time.fixedDeltaTime);
        }

        void TickCooldowns()
        {
            for (int i = 0; i < options.Count; i++)
                if (options[i] != null && options[i].CooldownRemaining > 0)
                    options[i].CooldownRemaining--;
        }

        void TickRageAndPhase()
        {
            if (_rageCooldownRemaining > 0) _rageCooldownRemaining--;
            if (_armorLockoutRemaining > 0) _armorLockoutRemaining--;

            if (enraged && _rageFramesRemaining > 0 && CurrentState != VelkhanaState.RageTransition)
            {
                _rageFramesRemaining--;
                if (_rageFramesRemaining <= 0) EndEnrage();
            }

            if (stage == ArmorStage.Ultimate && _ultimateFramesRemaining > 0)
            {
                _ultimateFramesRemaining--;
                if (_ultimateFramesRemaining <= 0)
                {
                    SetStage(ArmorStage.Neutral);
                    _armorLockoutRemaining = Mathf.Max(0, armorRebuildLockoutFrames);
                }
            }
        }

        void UpdateContext()
        {
            CombatMode = ModeForStage(stage);

            if (CurrentState == VelkhanaState.RageTransition)
                CurrentContext = VelkhanaContext.RageTransition;
            else if (IsAirborne || CurrentState == VelkhanaState.Takeoff ||
                     CurrentState == VelkhanaState.Landing)
                CurrentContext = VelkhanaContext.AerialCombat;
            else if (HealthFraction <= criticalHealthFraction)
                CurrentContext = VelkhanaContext.CriticalHealth;
            else if (hunter == null || !InsideCombatEntry())
                CurrentContext = VelkhanaContext.CombatEntry;
            else
                CurrentContext = VelkhanaContext.GroundCombat;
        }

        bool InsideCombatEntry()
        {
            if (hunter == null) return false;
            float distance2D = Distance2DToHunter();
            float vertical = Mathf.Abs(hunter.position.y - transform.position.y);
            if (distance2D <= closeRange && vertical <= combatEntryVerticalRange) return true;
            return Vector3.Distance(transform.position, hunter.position) <= mediumRange;
        }

        bool TryChooseGroundDecision(
            out MonsterAttackOption option,
            out string thkTrace,
            out bool openerSlice)
        {
            option = null;
            thkTrace = string.Empty;
            openerSlice = false;
            if (hunter == null || options.Count == 0 || IsAirborne) return false;

            // Decoded parent pattern: exactly one 0..99 roll decides whether this decision enters
            // the scoped Global.node_087 opener hierarchy or falls through to the flat table.
            VelkhanaGroundOpenerParent parent = SelectGroundOpenerParent(
                CombatMode, enraged, NextSelectionRoll100());
            if (parent != VelkhanaGroundOpenerParent.None)
            {
                float distance = Distance2DToHunter();
                if (distance <= 13f)
                {
                    VelkhanaNode087Leaf leaf =
                        SelectNode087Leaf(distance, NextSelectionRoll100());
                    option = FindNode087Leaf(leaf);
                    if (option != null)
                    {
                        thkTrace =
                            $"{CombatMainModeNodeName(CombatMode)} > " +
                            $"{GroundOpenerParentNodeName(parent)} > " +
                            $"Global.node_087 > {option.thkNode}";
                        openerSlice = true;
                        return true;
                    }
                }
            }

            option = ChooseFlatGroundOption();
            if (option == null) return false;
            thkTrace = DefaultThkTraceFor(option);
            return true;
        }

        MonsterAttackOption ChooseFlatGroundOption()
        {
            if (hunter == null || options.Count == 0) return null;

            float distance = Distance2DToHunter();
            float vertical = Mathf.Abs(hunter.position.y - transform.position.y);
            float facingAngle = AbsoluteFacingAngleToHunter();
            RangeBand band = BandToHunter();
            bool inFront = facingAngle <= 69.5f;

            float total = 0f;
            var weights = new float[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                MonsterAttackOption option = options[i];
                if (option == null || option.attack == null) continue;
                if (option.aerialFamily != VelkhanaAerialOptionFamily.None) continue;
                if (!option.useInFlatGroundSelector) continue;
                if (option.CooldownRemaining > 0) continue;
                if (option.minimumStage > stage) continue;
                if (option.calmOnly && enraged) continue;
                if (option.enragedOnly && !enraged) continue;

                if (option.useEm124Conditions)
                {
                    if (!DetailedConditionsMatch(
                            option, distance, vertical, facingAngle, CombatMode, IsAirborne))
                        continue;
                }
                else
                {
                    if (option.band != band) continue;
                    if (option.requiresHunterInFront && !inFront) continue;
                }

                float weight = Mathf.Max(0f, option.weight);
                weight *= ModeWeight(option, CombatMode);
                if (enraged) weight *= Mathf.Max(0f, option.enragedWeightMultiplier);
                if (CurrentContext == VelkhanaContext.CriticalHealth)
                    weight *= Mathf.Max(0f, option.criticalWeightMultiplier);
                if (option == _lastUsed) weight *= repeatPenalty;
                else if (_recentOptions.Contains(option)) weight *= recentHistoryPenalty;

                weights[i] = weight;
                total += weight;
            }

            if (total <= 0f) return null;

            float roll = NextSelectionValue() * total;
            for (int i = 0; i < options.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f && weights[i] > 0f) return options[i];
            }

            return null;
        }

        MonsterAttackOption FindNode087Leaf(VelkhanaNode087Leaf leaf)
        {
            string node;
            switch (leaf)
            {
                case VelkhanaNode087Leaf.Global004:
                    node = "Global.node_004";
                    break;
                case VelkhanaNode087Leaf.Global006:
                    node = "Global.node_006";
                    break;
                case VelkhanaNode087Leaf.Global009:
                    node = "Global.node_009";
                    break;
                default:
                    return null;
            }

            // node_087 is authoritative for its leaf once entered. Lookup deliberately ignores
            // flat-table conditions, cooldown and history; the selected AttackDefinition still
            // plays its complete startup, active and recovery timeline through StartOption.
            for (int i = 0; i < options.Count; i++)
            {
                MonsterAttackOption candidate = options[i];
                if (candidate != null && candidate.attack != null &&
                    candidate.thkNode == node)
                    return candidate;
            }

            return null;
        }

        string DefaultThkTraceFor(MonsterAttackOption option)
        {
            if (option == null) return string.Empty;
            if (option.aerialFamily != VelkhanaAerialOptionFamily.None)
                return $"Combat_Main.node_006 > {option.thkNode}";
            return $"{CombatMainModeNodeName(CombatMode)} > flat ground selector > {option.thkNode}";
        }

        static string CombatMainModeNodeName(VelkhanaCombatMode mode)
        {
            switch (mode)
            {
                case VelkhanaCombatMode.Mode1:
                    return "Combat_Main.node_003";
                case VelkhanaCombatMode.Mode2:
                    return "Combat_Main.node_004";
                default:
                    return "Combat_Main.node_002";
            }
        }

        static string GroundOpenerParentNodeName(VelkhanaGroundOpenerParent parent)
        {
            switch (parent)
            {
                case VelkhanaGroundOpenerParent.Global105:
                    return "Global.node_105";
                case VelkhanaGroundOpenerParent.Global106:
                    return "Global.node_106";
                case VelkhanaGroundOpenerParent.Global108:
                    return "Global.node_108";
                default:
                    return string.Empty;
            }
        }

        static string GroundContinuationNodeName(VelkhanaGroundContinuationNode node)
        {
            switch (node)
            {
                case VelkhanaGroundContinuationNode.Global088:
                    return "Global.node_088";
                case VelkhanaGroundContinuationNode.Global089:
                    return "Global.node_089";
                case VelkhanaGroundContinuationNode.Global090:
                    return "Global.node_090";
                default:
                    return string.Empty;
            }
        }

        static VelkhanaNode087Leaf Node087LeafForNodeName(string nodeName)
        {
            switch (nodeName)
            {
                case "Global.node_004":
                    return VelkhanaNode087Leaf.Global004;
                case "Global.node_006":
                    return VelkhanaNode087Leaf.Global006;
                case "Global.node_009":
                    return VelkhanaNode087Leaf.Global009;
                default:
                    return VelkhanaNode087Leaf.None;
            }
        }

        static VelkhanaNode087Leaf ContinuationTargetToLeaf(
            VelkhanaGroundContinuationTarget target)
        {
            switch (target)
            {
                case VelkhanaGroundContinuationTarget.Global004:
                    return VelkhanaNode087Leaf.Global004;
                case VelkhanaGroundContinuationTarget.Global006:
                    return VelkhanaNode087Leaf.Global006;
                case VelkhanaGroundContinuationTarget.Global009:
                    return VelkhanaNode087Leaf.Global009;
                default:
                    return VelkhanaNode087Leaf.None;
            }
        }

        MonsterAttackOption ChooseCombatMainNode006()
        {
            int roll = combatMainNode006Predicate101
                ? 0
                : Mathf.Clamp(Mathf.FloorToInt(NextSelectionValue() * 100f), 0, 99);
            VelkhanaAerialOptionFamily selected = SelectCombatMainNode006(
                combatMainNode006Predicate101, roll);

            for (int i = 0; i < options.Count; i++)
            {
                MonsterAttackOption option = options[i];
                if (option != null && option.attack != null &&
                    option.aerialFamily == selected)
                    return option;
            }

            return null;
        }

        /// <summary>
        /// Exact Combat_Main.node_006 dispatch. function#101() remains an unresolved predicate:
        /// true forces Global051; false uses the decoded 50/50 Global051/Global052 split.
        /// </summary>
        public static VelkhanaAerialOptionFamily SelectCombatMainNode006(
            bool unresolvedPredicate101,
            int roll0To99)
        {
            if (unresolvedPredicate101) return VelkhanaAerialOptionFamily.Global051;
            if (roll0To99 < 0 || roll0To99 > 99)
                throw new ArgumentOutOfRangeException(
                    nameof(roll0To99), "Combat_Main.node_006 expects a roll from 0 through 99.");
            return roll0To99 < 50
                ? VelkhanaAerialOptionFamily.Global051
                : VelkhanaAerialOptionFamily.Global052;
        }

        /// <summary>
        /// Preserves the decoded source-order roll intervals for Global.node_105/106/108 in the
        /// scoped opener gateway. Rolls belonging to unrelated original branches fall through to
        /// the project's flat selector; those unrelated branches are not claimed as ported.
        /// </summary>
        public static VelkhanaGroundOpenerParent SelectGroundOpenerParent(
            VelkhanaCombatMode mode,
            bool isEnraged,
            int roll0To99)
        {
            ValidateRoll100(roll0To99);

            switch (mode)
            {
                case VelkhanaCombatMode.Mode1:
                    if (isEnraged)
                    {
                        if (roll0To99 >= 35 && roll0To99 <= 44)
                            return VelkhanaGroundOpenerParent.Global105;
                        if (roll0To99 >= 45 && roll0To99 <= 49)
                            return VelkhanaGroundOpenerParent.Global106;
                        if (roll0To99 >= 65 && roll0To99 <= 74)
                            return VelkhanaGroundOpenerParent.Global108;
                    }
                    else
                    {
                        if (roll0To99 >= 50 && roll0To99 <= 59)
                            return VelkhanaGroundOpenerParent.Global105;
                        if (roll0To99 >= 60 && roll0To99 <= 69)
                            return VelkhanaGroundOpenerParent.Global106;
                    }
                    break;

                case VelkhanaCombatMode.Mode2:
                    if (isEnraged)
                    {
                        if (roll0To99 >= 40 && roll0To99 <= 49)
                            return VelkhanaGroundOpenerParent.Global106;
                        if (roll0To99 >= 70 && roll0To99 <= 79)
                            return VelkhanaGroundOpenerParent.Global108;
                    }
                    else if (roll0To99 >= 45 && roll0To99 <= 64)
                    {
                        return VelkhanaGroundOpenerParent.Global106;
                    }
                    break;

                default:
                    if (isEnraged)
                    {
                        if (roll0To99 >= 55 && roll0To99 <= 64)
                            return VelkhanaGroundOpenerParent.Global105;
                        if (roll0To99 >= 75 && roll0To99 <= 79)
                            return VelkhanaGroundOpenerParent.Global108;
                    }
                    else if (roll0To99 >= 60 && roll0To99 <= 74)
                    {
                        return VelkhanaGroundOpenerParent.Global105;
                    }
                    break;
            }

            return VelkhanaGroundOpenerParent.None;
        }

        public static VelkhanaNode087Leaf SelectNode087Leaf(
            float distanceMetres,
            int roll0To99)
        {
            ValidateRoll100(roll0To99);
            distanceMetres = Mathf.Max(0f, distanceMetres);

            if (distanceMetres <= 3f)
                return roll0To99 < 20
                    ? VelkhanaNode087Leaf.Global004
                    : VelkhanaNode087Leaf.Global009;

            if (distanceMetres <= 7f)
            {
                if (roll0To99 < 50) return VelkhanaNode087Leaf.Global004;
                if (roll0To99 < 75) return VelkhanaNode087Leaf.Global006;
                return VelkhanaNode087Leaf.Global009;
            }

            if (distanceMetres <= 13f)
                return roll0To99 < 20
                    ? VelkhanaNode087Leaf.Global004
                    : VelkhanaNode087Leaf.Global006;

            return VelkhanaNode087Leaf.None;
        }

        public static VelkhanaGroundContinuationNode ContinuationNodeFor(
            VelkhanaNode087Leaf openerLeaf)
        {
            switch (openerLeaf)
            {
                case VelkhanaNode087Leaf.Global009:
                    return VelkhanaGroundContinuationNode.Global088;
                case VelkhanaNode087Leaf.Global006:
                    return VelkhanaGroundContinuationNode.Global089;
                case VelkhanaNode087Leaf.Global004:
                    return VelkhanaGroundContinuationNode.Global090;
                default:
                    return VelkhanaGroundContinuationNode.None;
            }
        }

        /// <summary>
        /// Decoded node_088/089/090 selection after node_087's opener motion has completely
        /// finished. Distances are metres converted from the source's 300/900/1300-unit gates.
        /// Callers consume one roll only while the post-motion target distance is at most 13 m.
        /// </summary>
        public static VelkhanaGroundContinuationTarget SelectGroundOpenerContinuation(
            VelkhanaNode087Leaf openerLeaf,
            float postMotionDistanceMetres,
            int roll0To99)
        {
            postMotionDistanceMetres = Mathf.Max(0f, postMotionDistanceMetres);
            if (postMotionDistanceMetres > 13f)
                return VelkhanaGroundContinuationTarget.None;

            ValidateRoll100(roll0To99);
            switch (openerLeaf)
            {
                // node_088 repeats the same 65/35 node_004/node_006 table at <=3, <=9 and <=13 m.
                case VelkhanaNode087Leaf.Global009:
                    return roll0To99 < 65
                        ? VelkhanaGroundContinuationTarget.Global004
                        : VelkhanaGroundContinuationTarget.Global006;

                // node_089 uses 65/35 node_004/node_009 through 9 m, then node_004 at <=13 m.
                case VelkhanaNode087Leaf.Global006:
                    if (postMotionDistanceMetres <= 9f)
                        return roll0To99 < 65
                            ? VelkhanaGroundContinuationTarget.Global004
                            : VelkhanaGroundContinuationTarget.Global009;
                    return VelkhanaGroundContinuationTarget.Global004;

                // node_090 uses 50/50 node_004/node_079 through 9 m, then node_004 at <=13 m.
                case VelkhanaNode087Leaf.Global004:
                    if (postMotionDistanceMetres <= 9f)
                        return roll0To99 < 50
                            ? VelkhanaGroundContinuationTarget.Global004
                            : VelkhanaGroundContinuationTarget.Global079;
                    return VelkhanaGroundContinuationTarget.Global004;

                default:
                    return VelkhanaGroundContinuationTarget.None;
            }
        }

        /// <summary>node_079's only random block, used when Velkhana is within 5 m of arenaCenter.</summary>
        public static VelkhanaNode087Leaf SelectNode079NearLeaf(int roll0To99)
        {
            ValidateRoll100(roll0To99);
            return roll0To99 < 50
                ? VelkhanaNode087Leaf.Global006
                : VelkhanaNode087Leaf.Global009;
        }

        /// <summary>
        /// node_079's non-random branch beyond 5 m from arenaCenter. It compares the same wrapped
        /// clockwise 270..90 sector first for the arena center and then for the sole hunter.
        /// </summary>
        public static VelkhanaNode087Leaf SelectNode079FarLeaf(
            bool arenaCenterInSector,
            bool hunterInSector)
        {
            return arenaCenterInSector == hunterInSector
                ? VelkhanaNode087Leaf.Global006
                : VelkhanaNode087Leaf.Global009;
        }

        public static bool DirectionIsInClockwiseSector270To90(
            Vector3 forward,
            Vector3 direction)
        {
            forward.y = 0f;
            direction.y = 0f;
            if (forward.sqrMagnitude < 0.001f || direction.sqrMagnitude < 0.001f)
                return true;

            float clockwiseDegrees = Vector3.SignedAngle(
                forward.normalized, direction.normalized, Vector3.up);
            if (clockwiseDegrees < 0f) clockwiseDegrees += 360f;
            return clockwiseDegrees <= 90f || clockwiseDegrees >= 270f;
        }

        static void ValidateRoll100(int roll0To99)
        {
            if (roll0To99 < 0 || roll0To99 > 99)
                throw new ArgumentOutOfRangeException(
                    nameof(roll0To99), "Expected a roll from 0 through 99.");
        }

        /// <summary>
        /// Applies the demo's pacing multipliers, then enforces a project-only readability/reset
        /// floor. The floor is not a decoded or original EM124 timing.
        /// </summary>
        public static int ProjectGroundResetPacingFrames(
            int baseFrames,
            bool powered,
            bool isEnraged,
            bool criticalHealth,
            float poweredMultiplier,
            float enragedMultiplier,
            float criticalMultiplier)
        {
            int pacing = Mathf.Max(1, baseFrames);
            if (powered)
                pacing = Mathf.RoundToInt(pacing * Mathf.Max(0f, poweredMultiplier));
            if (isEnraged)
                pacing = Mathf.RoundToInt(pacing * Mathf.Max(0f, enragedMultiplier));
            if (criticalHealth)
                pacing = Mathf.RoundToInt(pacing * Mathf.Max(0f, criticalMultiplier));
            return Mathf.Max(ProjectMinimumGroundResetFrames, pacing);
        }

        int NextSelectionRoll100()
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(NextSelectionValue() * 100f), 0, 99);
        }

        float NextSelectionValue()
        {
            if (!deterministicSelection) return UnityEngine.Random.value;
            if (_random == null || _randomSeed != selectionSeed)
            {
                _random = new System.Random(selectionSeed);
                _randomSeed = selectionSeed;
            }
            return (float)_random.NextDouble();
        }

        void Remember(MonsterAttackOption option)
        {
            if (option == null) return;
            _recentOptions.Enqueue(option);
            while (_recentOptions.Count > 3) _recentOptions.Dequeue();
        }

        void ChooseRepositionTarget()
        {
            if (hunter == null || options.Count == 0)
            {
                DesiredBand = RangeBand.Medium;
                DesiredDistance = DesiredDistanceForBand(
                    DesiredBand, closeRange, mediumRange);
                return;
            }

            float currentDistance = Distance2DToHunter();
            float facing = AbsoluteFacingAngleToHunter();
            float bestScore = float.PositiveInfinity;
            MonsterAttackOption best = null;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    MonsterAttackOption option = options[i];
                    if (option == null || option.attack == null || option.minimumStage > stage)
                        continue;
                    if (!option.useInFlatGroundSelector) continue;
                    if (option.calmOnly && enraged) continue;
                    if (option.enragedOnly && !enraged) continue;
                    if (option.airRequirement == VelkhanaAirRequirement.Airborne &&
                        !option.takeOffBeforeSequence)
                        continue;
                    if (pass == 0 && option.CooldownRemaining > 0) continue;

                    float desired = DesiredDistanceForOption(
                        option, closeRange, mediumRange);
                    float distanceError = Mathf.Abs(currentDistance - desired);
                    float angleError = AngleErrorForOption(option, facing) * 0.025f;
                    float score = distanceError + angleError;
                    if (score >= bestScore) continue;

                    bestScore = score;
                    best = option;
                }

                if (best != null) break;
            }

            if (best == null)
            {
                DesiredBand = BandToHunter();
                DesiredDistance = DesiredDistanceForBand(
                    DesiredBand, closeRange, mediumRange);
                return;
            }

            DesiredBand = best.band;
            DesiredDistance = DesiredDistanceForOption(
                best, closeRange, mediumRange);
        }

        static float AngleErrorForOption(MonsterAttackOption option, float angle)
        {
            if (!option.useEm124Conditions) return option.requiresHunterInFront
                ? Mathf.Max(0f, angle - 69.5f)
                : 0f;

            if (angle < option.minimumFacingAngle)
                return option.minimumFacingAngle - angle;
            if (angle > option.maximumFacingAngle)
                return angle - option.maximumFacingAngle;
            return 0f;
        }

        RangeBand BandToHunter()
        {
            float distance = Distance2DToHunter();
            if (distance <= closeRange) return RangeBand.Close;
            return distance <= mediumRange ? RangeBand.Medium : RangeBand.Far;
        }

        float Distance2DToHunter()
        {
            if (hunter == null) return float.PositiveInfinity;
            Vector3 offset = hunter.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        float AbsoluteFacingAngleToHunter()
        {
            return AbsoluteFacingAngle(transform.forward, DirectionToHunter());
        }

        Vector3 ClampToArena(Vector3 position)
        {
            Vector3 offset = position - arenaCenter;
            float height = offset.y;
            offset.y = 0f;

            float radius = Mathf.Max(1f, arenaRadius);
            if (offset.sqrMagnitude > radius * radius)
                offset = offset.normalized * radius;

            offset.y = height;
            return arenaCenter + offset;
        }

        void EnterState(VelkhanaState next)
        {
            if (CurrentState == next) return;
            CurrentState = next;
            StateFrame = 0;
            StateChanged?.Invoke(next);
        }

        Vector3 DirectionToHunter()
        {
            if (hunter == null) return transform.forward;
            Vector3 flat = hunter.position - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;
        }

        void OnPartDamaged(BodyPartHurtbox part, float damage)
        {
            ApplyBossDamage(damage);
        }

        public void ApplyBossDamage(float damage)
        {
            if (damage <= 0f || CurrentHealth <= 0f) return;

            CurrentHealth = Mathf.Max(0f, CurrentHealth - damage);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (!automaticEnrage || enraged || _rageCooldownRemaining > 0) return;
            _rageDamage += damage;
            if (_rageDamage >= Mathf.Max(1f, rageDamageThreshold))
                BeginEnrage();
        }

        public void ResetVitality()
        {
            CurrentHealth = Mathf.Max(1f, maxHealth);
            _rageDamage = 0f;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }

        public void BeginEnrage()
        {
            if (enraged) return;

            enraged = true;
            _rageDamage = 0f;
            _rageFramesRemaining = Mathf.Max(1, rageDurationFrames);
            _ragePending = true;
            CurrentContext = VelkhanaContext.RageTransition;
            EnrageChanged?.Invoke(true);

            if (CurrentState == VelkhanaState.Observe ||
                CurrentState == VelkhanaState.Reposition)
                TryEnterPendingRage();
        }

        void EndEnrage()
        {
            if (!enraged) return;
            enraged = false;
            _rageFramesRemaining = 0;
            _rageDamage = 0f;
            _rageCooldownRemaining = Mathf.Max(0, rageCooldownFrames);
            EnrageChanged?.Invoke(false);
        }

        public void AdvanceStage()
        {
            SetStage(stage == ArmorStage.Ultimate ? ArmorStage.Neutral : stage + 1);
        }

        void SetStage(ArmorStage next)
        {
            stage = next;
            CombatMode = ModeForStage(stage);
            _armorBreaks = 0;
            _completedSinceStage = 0;
            _ultimateFramesRemaining = stage == ArmorStage.Ultimate
                ? Mathf.Max(1, ultimateDurationFrames)
                : 0;

            float armor = stage == ArmorStage.IceArmorStage1 ||
                          stage == ArmorStage.IceArmorStage2
                ? armorPerPart * (int)stage
                : 0f;

            if (armoredParts != null)
                foreach (BodyPartHurtbox part in armoredParts)
                    if (part != null) part.RestoreIceArmor(armor);

            StageChanged?.Invoke(stage);
        }

        void OnArmorShattered(BodyPartHurtbox part)
        {
            if (stage == ArmorStage.Neutral) return;
            if (++_armorBreaks < armorBreaksToInterrupt) return;

            SetStage(ArmorStage.Neutral);
            _armorLockoutRemaining = Mathf.Max(0, armorRebuildLockoutFrames);
        }

        public static VelkhanaCombatMode ModeForStage(ArmorStage armorStage)
        {
            switch (armorStage)
            {
                case ArmorStage.IceArmorStage1:
                    return VelkhanaCombatMode.Mode1;
                case ArmorStage.IceArmorStage2:
                case ArmorStage.Ultimate:
                    return VelkhanaCombatMode.Mode2;
                default:
                    return VelkhanaCombatMode.Mode0;
            }
        }

        public static float AbsoluteFacingAngle(Vector3 forward, Vector3 toTarget)
        {
            forward.y = 0f;
            toTarget.y = 0f;
            if (forward.sqrMagnitude < 0.001f || toTarget.sqrMagnitude < 0.001f)
                return 0f;
            return Mathf.Abs(Vector3.SignedAngle(
                forward.normalized, toTarget.normalized, Vector3.up));
        }

        public static bool DetailedConditionsMatch(
            MonsterAttackOption option,
            float distance2D,
            float verticalDistance,
            float absoluteFacingAngle,
            VelkhanaCombatMode mode,
            bool airborne)
        {
            if (option == null) return false;
            if (distance2D < Mathf.Max(0f, option.minimumDistance)) return false;
            if (option.maximumDistance > 0f && distance2D > option.maximumDistance) return false;
            if (option.maximumVerticalDistance > 0f &&
                verticalDistance > option.maximumVerticalDistance) return false;

            float angle = Mathf.Clamp(absoluteFacingAngle, 0f, 180f);
            if (angle < option.minimumFacingAngle || angle > option.maximumFacingAngle)
                return false;

            VelkhanaCombatModeMask currentMode = (VelkhanaCombatModeMask)(1 << (int)mode);
            if ((option.modes & currentMode) == 0) return false;

            switch (option.airRequirement)
            {
                case VelkhanaAirRequirement.Grounded:
                    return !airborne;
                case VelkhanaAirRequirement.Airborne:
                    return airborne;
                default:
                    return true;
            }
        }

        public static float ModeWeight(
            MonsterAttackOption option, VelkhanaCombatMode mode)
        {
            if (option == null) return 0f;
            switch (mode)
            {
                case VelkhanaCombatMode.Mode1:
                    return Mathf.Max(0f, option.mode1WeightMultiplier);
                case VelkhanaCombatMode.Mode2:
                    return Mathf.Max(0f, option.mode2WeightMultiplier);
                default:
                    return Mathf.Max(0f, option.mode0WeightMultiplier);
            }
        }

        public static float DesiredDistanceForOption(
            MonsterAttackOption option, float close, float medium)
        {
            if (option == null || !option.useEm124Conditions)
                return DesiredDistanceForBand(
                    option != null ? option.band : RangeBand.Medium, close, medium);

            float minimum = Mathf.Max(0f, option.minimumDistance);
            float maximum = option.maximumDistance > minimum
                ? option.maximumDistance
                : minimum + 2f;
            return Mathf.Lerp(minimum, maximum, 0.55f);
        }

        /// <summary>Centre point used by direct locomotion for a legacy attack range band.</summary>
        public static float DesiredDistanceForBand(RangeBand band, float close, float medium)
        {
            close = Mathf.Max(0.5f, close);
            medium = Mathf.Max(close + 0.5f, medium);

            switch (band)
            {
                case RangeBand.Close:
                    return close * 0.72f;
                case RangeBand.Far:
                    return medium + Mathf.Max(3f, (medium - close) * 0.35f);
                default:
                    return Mathf.Lerp(close, medium, 0.5f);
            }
        }

        /// <summary>
        /// Pure range-and-angle steering used instead of a NavMesh. Radial movement corrects the
        /// action distance; a tangent component keeps the monster moving while it turns.
        /// </summary>
        public static Vector3 CalculateRepositionDirection(
            Vector3 monsterPosition,
            Vector3 monsterForward,
            Vector3 hunterPosition,
            float desiredDistance,
            float tolerance,
            float orbitSign,
            float orbitWeight)
        {
            Vector3 toHunter = hunterPosition - monsterPosition;
            toHunter.y = 0f;
            float distance = toHunter.magnitude;
            if (distance < 0.001f) return Vector3.zero;
            toHunter /= distance;

            monsterForward.y = 0f;
            if (monsterForward.sqrMagnitude < 0.001f) monsterForward = toHunter;
            else monsterForward.Normalize();

            float error = distance - Mathf.Max(0f, desiredDistance);
            Vector3 radial = Mathf.Abs(error) <= Mathf.Max(0f, tolerance)
                ? Vector3.zero
                : toHunter * Mathf.Sign(error);

            float facing = Mathf.Clamp(Vector3.Dot(monsterForward, toHunter), -1f, 1f);
            float angleNeed = Mathf.InverseLerp(1f, -1f, facing);
            Vector3 tangent = Vector3.Cross(Vector3.up, toHunter) *
                              (orbitSign < 0f ? -1f : 1f);

            float clampedOrbit = Mathf.Clamp01(orbitWeight);
            float tangentStrength = radial == Vector3.zero
                ? clampedOrbit
                : clampedOrbit * Mathf.Lerp(0.2f, 1f, angleNeed);
            Vector3 combined = radial + tangent * tangentStrength;
            return combined.sqrMagnitude > 0.001f ? combined.normalized : Vector3.zero;
        }
    }
}
