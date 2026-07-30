using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VelkhanaSlice.Combat;

namespace VelkhanaSlice.Hunter
{
    /// <summary>
    /// Fixed-step hunter locomotion plus a readable ground-combat subset of cFSMPl_W00.
    /// LinkMotion-only states are collapsed into explicit transitions, while source state and
    /// ActionNo identities remain visible through Wp00Node and ActionNumberFor.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HunterController : MonoBehaviour, IAttacker
    {
        public enum State
        {
            Free,
            Charging,
            Attacking,
            Guarding,
            Rolling,
        }

        /// <summary>
        /// Combo tier is independent from charge power. Basic, Strong and True select different
        /// WP00 hold/release nodes; ChargeLevel reports the Lv0-Lv3 power inside that tier.
        /// </summary>
        public enum ChargeStage
        {
            None,
            Basic,
            Strong,
            True,
        }

        /// <summary>Source node IDs from the decoded wp00_action FSM.</summary>
        public enum Wp00Node
        {
            NoTransition = -1,
            Idle = 0,
            ChargeSlashRelease = 1,
            ChargeSlashHold = 3,
            WideSlash = 4,
            Evade = 8,
            SideBlow = 10,
            StrongChargeHold = 11,
            StrongChargeRelease = 12,
            StrongWideSlash = 13,
            RisingSlash = 14,
            Guard = 19,
            GuardEnd = 20,
            DrawMoving = 21,
            DrawStationary = 22,
            Kick = 24,
            TrueChargeFirstHit = 39,
            Tackle = 41,
            LeapingWideSlash = 42,
            EvadeSheathed = 47,
            TrueChargeHold = 53,
            TackleLevel2 = 59,
            WideSlashPostStrong = 61,
            RisingSlashPostStrong = 62,
            SideBlowPostStrong = 63,
            IdleToCharge = 74,
            // The normal second hit remains inside ActionNo 78 when the opening does not trigger
            // a FinishEx diversion. 390 is a project-only presentation phase, not a source node.
            TrueChargeNormalFinish = 390,
            TrueChargeFinishLevel1 = 111,
            TrueChargeFinishLevel2 = 112,
            TrueChargeFinishLevel3 = 113,
        }

        [Flags]
        public enum CoreInput
        {
            None = 0,
            Primary = 1 << 0,
            Secondary = 1 << 1,
            Direction = 1 << 2,
            Release = 1 << 3,
            Guard = 1 << 4,
        }

        [Header("Movement (metres per second)")]
        public float sheathedSpeed = 5.5f;
        public float drawnSpeed = 2.2f;
        [NonSerialized] public float runningSpeed = 7.5f;
        public float turnDegreesPerSecond = 720f;
        [Tooltip("Fraction of incoming damage that passes through a held Great Sword guard.")]
        [Range(0f, 1f)] public float guardDamageMultiplier = 0.2f;

        [Header("Roll (frames @ 60 Hz)")]
        public float rollSpeed = 7f;
        public int rollFrames = 26;
        public int rollInvulnStart = 4;
        public int rollInvulnEnd = 16;

        [Header("Sheathing (frames @ 60 Hz)")]
        public int drawFrames = 20;
        public int sheatheFrames = 26;

        [Header("Great Sword — decoded WP00 ground core")]
        public AttackDefinition drawSlash;
        public AttackDefinition chargedSlash;
        public AttackDefinition strongChargedSlash;
        [Tooltip("WP00 node 39 / ActionNo 78: the opening hit before the level-selected finisher.")]
        public AttackDefinition trueChargedSlash;
        public AttackDefinition trueChargedFinishNormal;
        public AttackDefinition trueChargedFinishLevel1;
        public AttackDefinition trueChargedFinishLevel2;
        public AttackDefinition trueChargedFinishLevel3;
        public AttackDefinition wideSlash;
        public AttackDefinition strongWideSlash;
        public AttackDefinition leapingWideSlash;
        public AttackDefinition wideSlashPostStrong;
        public AttackDefinition risingSlash;
        public AttackDefinition risingSlashPostStrong;
        public AttackDefinition sideBlow;
        public AttackDefinition sideBlowPostStrong;
        public AttackDefinition tackle;
        public AttackDefinition tackleLevel2;
        public AttackDefinition kick;

        [Tooltip("Frames of hold needed to reach charge power levels 1, 2 and 3.")]
        public int[] chargeThresholds = { 40, 75, 110 };
        [Tooltip("Frames past full charge before WP00 forces an overcharged level-1 release.")]
        public int overchargeFrames = 45;

        [Header("Hit detection")]
        [Tooltip("Where the sword visual sits. The gameplay damage box comes from the attack definition.")]
        public Transform bladePoint;
        public LayerMask hurtboxLayers = ~0;

        [Header("Camera")]
        [Tooltip("Camera used to turn mouse position into a ground-plane aim direction.")]
        public Camera aimCamera;

        public State CurrentState { get; private set; } = State.Free;
        public Wp00Node CurrentNode { get; private set; } = Wp00Node.Idle;
        public Wp00Node BufferedNode =>
            ResolveCoreTransition(CurrentNode, _bufferedInput);
        public bool WeaponDrawn { get; private set; }
        public int ChargeLevel { get; private set; }
        public ChargeStage CurrentChargeStage { get; private set; }
        public AttackDefinition CurrentAttack { get; private set; }
        public int AttackFrame { get; private set; }
        public int StateFrame => _stateFrame;
        public int ChargeFrames => _chargeFrames;
        public bool IsWeaponTransitioning => _sheatheTimer > 0;
        public bool WeaponTransitionDrawn => IsWeaponTransitioning ? _sheatheTarget : WeaponDrawn;
        public float WeaponTransitionProgress =>
            _sheatheTimer > 0 && _sheatheDuration > 0
                ? 1f - (float)_sheatheTimer / _sheatheDuration
                : 1f;
        public bool IsRunning =>
            CurrentState == State.Free &&
            _runHeld &&
            !WeaponDrawn &&
            !IsWeaponTransitioning &&
            _moveInput.sqrMagnitude > 0.02f;
        public bool IsGuarding => CurrentState == State.Guarding;
        public bool IsInvulnerable =>
            CurrentState == State.Rolling &&
            _stateFrame >= rollInvulnStart &&
            _stateFrame < rollInvulnEnd;
        public bool HasHyperArmor =>
            CurrentState == State.Attacking &&
            CurrentAttack != null &&
            CurrentAttack.hyperArmor;

        CharacterController _cc;
        int _stateFrame;
        int _chargeFrames;
        int _hitstopFrames;
        int _sheatheTimer;
        int _sheatheDuration;
        bool _sheatheTarget;
        float _verticalVelocity;
        bool _previousHitConnected;
        bool _currentAttackConnected;
        Vector3 _aimDirection = Vector3.forward;
        CoreInput _bufferedInput;

        readonly HashSet<BodyPartHurtbox> _hitThisSwing = new HashSet<BodyPartHurtbox>();
        readonly Collider[] _overlapBuffer = new Collider[32];

        // Render-frame input is latched and consumed by the fixed-step combat simulation.
        Vector2 _moveInput;
        Vector2 _aimInput;
        bool _primaryHeld;
        bool _secondaryHeld;
        bool _primaryPressed;
        bool _secondaryPressed;
        bool _dodgePressed;
        bool _sheathePressed;
        bool _runHeld;
        bool _guardHeld;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (aimCamera == null) aimCamera = Camera.main;

            // Keeps an older generated scene readable until the builder is run again.
            if (GetComponent<HunterPresentation>() == null &&
                transform.Find("Body") != null &&
                transform.Find("SwordVisual") != null)
            {
                gameObject.AddComponent<HunterPresentation>();
            }
        }

        void Update()
        {
            PollInput();
        }

        void FixedUpdate()
        {
            if (_hitstopFrames > 0)
            {
                _hitstopFrames--;
                ClearEdgeInput();
                return;
            }

            UpdateAim();
            TickWeaponTransition();

            switch (CurrentState)
            {
                case State.Free: TickFree(); break;
                case State.Charging: TickCharging(); break;
                case State.Attacking: TickAttacking(); break;
                case State.Guarding: TickGuarding(); break;
                case State.Rolling: TickRolling(); break;
            }

            ClearEdgeInput();
        }

        void PollInput()
        {
            var gp = Gamepad.current;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            _moveInput = gp != null ? gp.leftStick.ReadValue() : Vector2.zero;
            if (_moveInput.sqrMagnitude < 0.02f && kb != null)
            {
                _moveInput = new Vector2(
                    (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f),
                    (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            }

            _aimInput = gp != null ? gp.rightStick.ReadValue() : Vector2.zero;

            _primaryHeld =
                (gp != null && gp.buttonWest.isPressed) ||
                (mouse != null && mouse.leftButton.isPressed);
            _secondaryHeld =
                (gp != null && gp.buttonNorth.isPressed) ||
                (mouse != null && mouse.rightButton.isPressed);
            _primaryPressed |=
                (gp != null && gp.buttonWest.wasPressedThisFrame) ||
                (mouse != null && mouse.leftButton.wasPressedThisFrame);
            _secondaryPressed |=
                (gp != null && gp.buttonNorth.wasPressedThisFrame) ||
                (mouse != null && mouse.rightButton.wasPressedThisFrame);
            _dodgePressed |=
                (gp != null && gp.buttonEast.wasPressedThisFrame) ||
                (kb != null && kb.spaceKey.wasPressedThisFrame);
            _sheathePressed |=
                (gp != null && gp.buttonSouth.wasPressedThisFrame) ||
                (kb != null && kb.fKey.wasPressedThisFrame);
            _runHeld =
                (gp != null && gp.leftStickButton.isPressed) ||
                (kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed));
            _guardHeld =
                (gp != null && gp.rightTrigger.ReadValue() > 0.5f) ||
                (kb != null && (kb.rKey.isPressed || kb.leftCtrlKey.isPressed));
        }

        void ClearEdgeInput()
        {
            _primaryPressed = false;
            _secondaryPressed = false;
            _dodgePressed = false;
            _sheathePressed = false;
        }

        void UpdateAim()
        {
            if (_aimInput.sqrMagnitude > 0.1f)
            {
                _aimDirection = new Vector3(_aimInput.x, 0f, _aimInput.y).normalized;
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || aimCamera == null) return;

            Ray ray = aimCamera.ScreenPointToRay(mouse.position.ReadValue());
            var ground = new Plane(Vector3.up, transform.position);
            if (!ground.Raycast(ray, out float distance)) return;

            Vector3 flat = ray.GetPoint(distance) - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f) _aimDirection = flat.normalized;
        }

        void TickWeaponTransition()
        {
            if (_sheatheTimer > 0)
            {
                if (--_sheatheTimer == 0) WeaponDrawn = _sheatheTarget;
                return;
            }

            if (CurrentState != State.Free) return;

            // Run-to-sheathe is a project locomotion rule. It is deliberately above WP00.
            if (_runHeld && WeaponDrawn)
            {
                BeginWeaponTransition(false);
                return;
            }

            if (_sheathePressed) BeginWeaponTransition(!WeaponDrawn);
        }

        void TickFree()
        {
            Move(IsRunning ? runningSpeed : WeaponDrawn ? drawnSpeed : sheathedSpeed);
            FaceMoveOrAim();

            if (_dodgePressed)
            {
                EnterRoll();
                return;
            }

            if (_guardHeld)
            {
                StartFreshCombo();
                EnterGuard();
                return;
            }

            bool effectivelySheathed =
                !WeaponDrawn ||
                (IsWeaponTransitioning && !_sheatheTarget);

            if (_primaryPressed || _primaryHeld)
            {
                StartFreshCombo();

                if (effectivelySheathed)
                {
                    CancelWeaponTransition();
                    WeaponDrawn = true;
                    Wp00Node drawNode =
                        _moveInput.sqrMagnitude > 0.02f
                            ? Wp00Node.DrawMoving
                            : Wp00Node.DrawStationary;
                    BeginAttack(drawSlash, drawNode, false);
                    return;
                }

                CancelWeaponTransition();
                if (_secondaryPressed || _secondaryHeld)
                {
                    BeginAttack(risingSlash, Wp00Node.RisingSlash, false);
                    return;
                }

                BeginCharge(ChargeStage.Basic, Wp00Node.IdleToCharge);
                return;
            }

            if (_secondaryPressed && WeaponDrawn)
            {
                StartFreshCombo();
                BeginAttack(wideSlash, Wp00Node.WideSlash, false);
            }
        }

        void TickCharging()
        {
            Move(drawnSpeed * 0.35f);
            FaceMoveOrAim();
            _stateFrame++;
            _chargeFrames++;

            if (CurrentNode == Wp00Node.IdleToCharge)
                CurrentNode = Wp00Node.ChargeSlashHold;

            ChargeLevel = ChargeLevelFor(_chargeFrames);

            // WP00 charge nodes have a Circle/tackle edge, but no direct evade edge.
            if (_secondaryPressed)
            {
                bool basic = CurrentChargeStage == ChargeStage.Basic;
                BeginAttack(
                    basic ? tackle : tackleLevel2,
                    basic ? Wp00Node.Tackle : Wp00Node.TackleLevel2,
                    false);
                return;
            }

            int lastThreshold =
                chargeThresholds.Length > 0
                    ? chargeThresholds[chargeThresholds.Length - 1]
                    : 0;
            bool forcedOvercharge =
                chargeThresholds.Length > 0 &&
                overchargeFrames >= 0 &&
                _chargeFrames >= lastThreshold + overchargeFrames;

            if (!_primaryHeld || forcedOvercharge)
                ReleaseCharge();
        }

        void ReleaseCharge()
        {
            ChargeLevel = ChargeLevelFor(_chargeFrames);

            switch (CurrentChargeStage)
            {
                case ChargeStage.Strong:
                    BeginAttack(strongChargedSlash, Wp00Node.StrongChargeRelease, true);
                    break;
                case ChargeStage.True:
                    BeginAttack(trueChargedSlash, Wp00Node.TrueChargeFirstHit, true);
                    break;
                default:
                    BeginAttack(chargedSlash, Wp00Node.ChargeSlashRelease, true);
                    break;
            }
        }

        void TickGuarding()
        {
            Move(drawnSpeed * 0.2f);
            FaceMoveOrAim();
            _stateFrame++;

            // Node 19 uses Triangle (the primary button) for kick.
            if (_primaryPressed)
            {
                BeginAttack(kick, Wp00Node.Kick, false);
                return;
            }

            if (!_guardHeld)
            {
                CurrentNode = Wp00Node.GuardEnd;
                EndCombo();
            }
        }

        void TickAttacking()
        {
            var attack = CurrentAttack;
            if (attack == null)
            {
                EndCombo();
                return;
            }

            if (attack.CanTrack(AttackFrame)) FaceDirection(_aimDirection);

            float step = attack.ForwardStep(AttackFrame);
            if (!Mathf.Approximately(step, 0f))
                _cc.Move(transform.forward * step);
            ApplyGravity();

            if (attack.IsHitActive(AttackFrame)) CheckHits(attack);

            // Buffer button edges even before the legal cancel frame. The transition still waits
            // for action completion, but a short early press is not lost between fixed steps.
            CaptureComboInput();

            if (attack.CanCancel(AttackFrame))
            {
                if (_dodgePressed)
                {
                    EnterRoll();
                    return;
                }
            }

            AttackFrame++;
            if (AttackFrame >= attack.TotalFrames)
                CompleteAttack();
        }

        void CaptureComboInput()
        {
            bool primary = _primaryPressed;
            bool secondary = _secondaryPressed;
            if (!primary && !secondary) return;

            // If both buttons overlap in this render frame, preserve the combined route.
            if ((primary && _secondaryHeld) || (secondary && _primaryHeld))
            {
                primary = true;
                secondary = true;
            }

            _bufferedInput = CoreInput.None;
            if (primary) _bufferedInput |= CoreInput.Primary;
            if (secondary) _bufferedInput |= CoreInput.Secondary;
            if (_moveInput.sqrMagnitude > 0.02f) _bufferedInput |= CoreInput.Direction;
        }

        void CompleteAttack()
        {
            Wp00Node completedNode = CurrentNode;
            CoreInput buffered = _bufferedInput;
            bool openingConnected = _currentAttackConnected;

            CurrentAttack = null;
            _bufferedInput = CoreInput.None;
            _hitThisSwing.Clear();

            if (completedNode == Wp00Node.TrueChargeFirstHit)
            {
                Wp00Node finishNode = ResolveTrueChargeFinish(ChargeLevel, openingConnected);
                BeginAttack(AttackForNode(finishNode), finishNode, true);
                return;
            }

            // Stationary draw can be held into the basic charge route. Moving draw remains its
            // own draw attack, matching the distinct WP00 nodes 21 and 22.
            if (completedNode == Wp00Node.DrawStationary && _primaryHeld)
            {
                BeginCharge(ChargeStage.Basic, Wp00Node.ChargeSlashHold);
                return;
            }

            Wp00Node next = ResolveCoreTransition(completedNode, buffered);
            if (next != Wp00Node.NoTransition && ExecuteNode(next))
                return;

            EndCombo();
        }

        void BeginCharge(ChargeStage stage, Wp00Node node)
        {
            CancelWeaponTransition();
            WeaponDrawn = true;
            CurrentChargeStage = stage;
            CurrentNode = node;
            CurrentState = State.Charging;
            CurrentAttack = null;
            _stateFrame = 0;
            _chargeFrames = 0;
            ChargeLevel = 0;
            _bufferedInput = CoreInput.None;
            _currentAttackConnected = false;
        }

        void BeginAttack(
            AttackDefinition attack,
            Wp00Node node,
            bool preserveChargePower)
        {
            if (attack == null)
            {
                EndCombo();
                return;
            }

            CancelWeaponTransition();
            WeaponDrawn = true;
            CurrentAttack = attack;
            CurrentNode = node;
            CurrentState = State.Attacking;
            AttackFrame = 0;
            _stateFrame = 0;
            _bufferedInput = CoreInput.None;
            _hitThisSwing.Clear();
            _currentAttackConnected = false;

            if (!preserveChargePower) ChargeLevel = 0;
        }

        void EnterGuard()
        {
            CancelWeaponTransition();
            WeaponDrawn = true;
            CurrentNode = Wp00Node.Guard;
            CurrentChargeStage = ChargeStage.None;
            CurrentState = State.Guarding;
            CurrentAttack = null;
            ChargeLevel = 0;
            _stateFrame = 0;
        }

        bool ExecuteNode(Wp00Node node)
        {
            switch (node)
            {
                case Wp00Node.ChargeSlashHold:
                case Wp00Node.IdleToCharge:
                    BeginCharge(ChargeStage.Basic, node);
                    return true;
                case Wp00Node.StrongChargeHold:
                    BeginCharge(ChargeStage.Strong, node);
                    return true;
                case Wp00Node.TrueChargeHold:
                    BeginCharge(ChargeStage.True, node);
                    return true;
                case Wp00Node.Guard:
                    EnterGuard();
                    return true;
                case Wp00Node.Idle:
                case Wp00Node.GuardEnd:
                    EndCombo();
                    return true;
                default:
                    AttackDefinition attack = AttackForNode(node);
                    if (attack == null) return false;
                    bool preservesPower =
                        node == Wp00Node.ChargeSlashRelease ||
                        node == Wp00Node.StrongChargeRelease ||
                        node == Wp00Node.TrueChargeFirstHit ||
                        node == Wp00Node.TrueChargeNormalFinish ||
                        node == Wp00Node.TrueChargeFinishLevel1 ||
                        node == Wp00Node.TrueChargeFinishLevel2 ||
                        node == Wp00Node.TrueChargeFinishLevel3;
                    BeginAttack(attack, node, preservesPower);
                    return true;
            }
        }

        AttackDefinition AttackForNode(Wp00Node node)
        {
            switch (node)
            {
                case Wp00Node.DrawMoving:
                case Wp00Node.DrawStationary:
                    return drawSlash;
                case Wp00Node.ChargeSlashRelease:
                    return chargedSlash;
                case Wp00Node.StrongChargeRelease:
                    return strongChargedSlash;
                case Wp00Node.TrueChargeFirstHit:
                    return trueChargedSlash;
                case Wp00Node.TrueChargeNormalFinish:
                    return trueChargedFinishNormal;
                case Wp00Node.TrueChargeFinishLevel1:
                    return trueChargedFinishLevel1;
                case Wp00Node.TrueChargeFinishLevel2:
                    return trueChargedFinishLevel2;
                case Wp00Node.TrueChargeFinishLevel3:
                    return trueChargedFinishLevel3;
                case Wp00Node.WideSlash:
                    return wideSlash;
                case Wp00Node.StrongWideSlash:
                    return strongWideSlash;
                case Wp00Node.LeapingWideSlash:
                    return leapingWideSlash;
                case Wp00Node.WideSlashPostStrong:
                    return wideSlashPostStrong;
                case Wp00Node.RisingSlash:
                    return risingSlash;
                case Wp00Node.RisingSlashPostStrong:
                    return risingSlashPostStrong;
                case Wp00Node.SideBlow:
                    return sideBlow;
                case Wp00Node.SideBlowPostStrong:
                    return sideBlowPostStrong;
                case Wp00Node.Tackle:
                    return tackle;
                case Wp00Node.TackleLevel2:
                    return tackleLevel2;
                case Wp00Node.Kick:
                    return kick;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Pure transition table for the retained decoded ground graph. Tests use this directly so
        /// fidelity does not depend on placeholder animation timings.
        /// </summary>
        public static Wp00Node ResolveCoreTransition(Wp00Node node, CoreInput input)
        {
            bool primary = (input & CoreInput.Primary) != 0;
            bool secondary = (input & CoreInput.Secondary) != 0;
            bool direction = (input & CoreInput.Direction) != 0;
            bool release = (input & CoreInput.Release) != 0;
            bool guard = (input & CoreInput.Guard) != 0;
            bool both = primary && secondary;

            switch (node)
            {
                case Wp00Node.Idle:
                    if (both) return Wp00Node.RisingSlash;
                    if (secondary) return Wp00Node.WideSlash;
                    if (primary) return Wp00Node.IdleToCharge;
                    if (guard) return Wp00Node.Guard;
                    break;
                case Wp00Node.IdleToCharge:
                    if (release) return Wp00Node.ChargeSlashRelease;
                    if (primary) return Wp00Node.ChargeSlashHold;
                    break;
                case Wp00Node.ChargeSlashHold:
                    if (secondary) return Wp00Node.Tackle;
                    if (release) return Wp00Node.ChargeSlashRelease;
                    break;
                case Wp00Node.ChargeSlashRelease:
                    if (both) return Wp00Node.RisingSlash;
                    if (primary && direction) return Wp00Node.StrongChargeHold;
                    if (secondary) return Wp00Node.WideSlash;
                    if (primary) return Wp00Node.SideBlow;
                    break;
                case Wp00Node.WideSlash:
                    if (both) return Wp00Node.RisingSlash;
                    if (secondary) return Wp00Node.Tackle;
                    if (primary) return Wp00Node.ChargeSlashHold;
                    break;
                case Wp00Node.SideBlow:
                    if (both) return Wp00Node.RisingSlash;
                    if (secondary) return Wp00Node.WideSlash;
                    if (primary) return Wp00Node.ChargeSlashHold;
                    break;
                case Wp00Node.StrongChargeHold:
                    if (secondary) return Wp00Node.TackleLevel2;
                    if (release) return Wp00Node.StrongChargeRelease;
                    break;
                case Wp00Node.StrongChargeRelease:
                    if (both) return Wp00Node.RisingSlashPostStrong;
                    if (primary && direction) return Wp00Node.TrueChargeHold;
                    if (secondary) return Wp00Node.StrongWideSlash;
                    if (primary) return Wp00Node.SideBlowPostStrong;
                    break;
                case Wp00Node.StrongWideSlash:
                    if (secondary) return Wp00Node.WideSlashPostStrong;
                    if (primary) return Wp00Node.StrongChargeHold;
                    break;
                case Wp00Node.SideBlowPostStrong:
                    if (both) return Wp00Node.RisingSlashPostStrong;
                    if (secondary) return Wp00Node.WideSlashPostStrong;
                    if (primary) return Wp00Node.StrongChargeHold;
                    break;
                case Wp00Node.WideSlashPostStrong:
                    if (both) return Wp00Node.RisingSlashPostStrong;
                    if (secondary) return Wp00Node.TackleLevel2;
                    if (primary) return Wp00Node.StrongChargeHold;
                    break;
                case Wp00Node.RisingSlash:
                    if (primary) return Wp00Node.ChargeSlashHold;
                    if (secondary) return Wp00Node.WideSlash;
                    break;
                case Wp00Node.RisingSlashPostStrong:
                    if (primary) return Wp00Node.StrongChargeHold;
                    if (secondary) return Wp00Node.WideSlashPostStrong;
                    break;
                case Wp00Node.TrueChargeHold:
                    if (secondary) return Wp00Node.TackleLevel2;
                    if (release) return Wp00Node.TrueChargeFirstHit;
                    break;
                case Wp00Node.Tackle:
                    if (primary) return Wp00Node.StrongChargeHold;
                    if (secondary) return Wp00Node.LeapingWideSlash;
                    break;
                case Wp00Node.TackleLevel2:
                    if (primary) return Wp00Node.TrueChargeHold;
                    if (secondary) return Wp00Node.LeapingWideSlash;
                    break;
                case Wp00Node.LeapingWideSlash:
                    if (primary) return Wp00Node.SideBlow;
                    break;
                case Wp00Node.Guard:
                    if (primary) return Wp00Node.Kick;
                    if (release) return Wp00Node.GuardEnd;
                    break;
                case Wp00Node.Kick:
                    if (primary) return Wp00Node.Tackle;
                    break;
            }

            return Wp00Node.NoTransition;
        }

        public static Wp00Node ResolveTrueChargeFinish(int chargePower, bool openingHitConnected)
        {
            if (!openingHitConnected) return Wp00Node.TrueChargeNormalFinish;

            switch (Mathf.Clamp(chargePower, 1, 3))
            {
                case 2: return Wp00Node.TrueChargeFinishLevel2;
                case 3: return Wp00Node.TrueChargeFinishLevel3;
                default: return Wp00Node.TrueChargeFinishLevel1;
            }
        }

        /// <summary>Decoded WP00 ActionNo for traceable core states; -1 means LinkMotion/common.</summary>
        public static int ActionNumberFor(Wp00Node node)
        {
            switch (node)
            {
                case Wp00Node.Idle: return 1000;
                case Wp00Node.ChargeSlashRelease: return 65;
                case Wp00Node.ChargeSlashHold: return 1013;
                case Wp00Node.WideSlash: return 1016;
                case Wp00Node.Evade: return 1005;
                case Wp00Node.SideBlow: return 71;
                case Wp00Node.StrongChargeHold: return 1014;
                case Wp00Node.StrongChargeRelease: return 74;
                case Wp00Node.StrongWideSlash: return 75;
                case Wp00Node.RisingSlash: return 69;
                case Wp00Node.Guard: return 1012;
                case Wp00Node.GuardEnd: return 89;
                case Wp00Node.DrawMoving: return 7;
                case Wp00Node.DrawStationary: return 5;
                case Wp00Node.Kick: return 73;
                case Wp00Node.TrueChargeFirstHit: return 78;
                case Wp00Node.TrueChargeNormalFinish: return 78;
                case Wp00Node.Tackle: return 79;
                case Wp00Node.LeapingWideSlash: return 81;
                case Wp00Node.EvadeSheathed: return 1004;
                case Wp00Node.TrueChargeHold: return 102;
                case Wp00Node.TackleLevel2: return 80;
                case Wp00Node.WideSlashPostStrong: return 67;
                case Wp00Node.RisingSlashPostStrong: return 70;
                case Wp00Node.SideBlowPostStrong: return 72;
                case Wp00Node.IdleToCharge: return 84;
                case Wp00Node.TrueChargeFinishLevel1: return 135;
                case Wp00Node.TrueChargeFinishLevel2: return 136;
                case Wp00Node.TrueChargeFinishLevel3: return 137;
                default: return -1;
            }
        }

        /// <summary>Charge power for a hold length, including the decoded overcharge drop.</summary>
        public int ChargeLevelFor(int heldFrames)
        {
            int level = 0;
            for (int i = 0; i < chargeThresholds.Length; i++)
                if (heldFrames >= chargeThresholds[i]) level = i + 1;

            int last =
                chargeThresholds.Length > 0
                    ? chargeThresholds[chargeThresholds.Length - 1]
                    : 0;
            if (level == chargeThresholds.Length &&
                heldFrames >= last + overchargeFrames)
            {
                level = 1;
            }

            return level;
        }

        void CheckHits(AttackDefinition attack)
        {
            int count = AttackHitbox.Overlap(transform, attack, hurtboxLayers, _overlapBuffer);

            for (int i = 0; i < count; i++)
            {
                var hurtbox = _overlapBuffer[i].GetComponentInParent<BodyPartHurtbox>();
                if (hurtbox == null || !_hitThisSwing.Add(hurtbox)) continue;

                HitResult result =
                    DamageResolver.Resolve(
                        attack,
                        ChargeLevel,
                        _previousHitConnected,
                        hurtbox);
                _previousHitConnected = result.Connected;
                _currentAttackConnected |= result.Connected;
                _hitstopFrames = Mathf.Max(_hitstopFrames, result.HitstopFrames);
            }

            if (AttackFrame == attack.startupFrames + attack.activeFrames - 1 &&
                _hitThisSwing.Count == 0)
            {
                _previousHitConnected = false;
            }
        }

        public void Interrupt()
        {
            if (HasHyperArmor || CurrentState != State.Attacking) return;
            EndCombo();
        }

        void StartFreshCombo()
        {
            _previousHitConnected = false;
            _currentAttackConnected = false;
            _bufferedInput = CoreInput.None;
            CurrentChargeStage = ChargeStage.None;
            ChargeLevel = 0;
        }

        void EndCombo()
        {
            CurrentAttack = null;
            CurrentState = State.Free;
            CurrentNode = Wp00Node.Idle;
            CurrentChargeStage = ChargeStage.None;
            ChargeLevel = 0;
            _chargeFrames = 0;
            _stateFrame = 0;
            _bufferedInput = CoreInput.None;
            _hitThisSwing.Clear();
            _currentAttackConnected = false;
        }

        void BeginWeaponTransition(bool drawn)
        {
            _sheatheTarget = drawn;
            _sheatheDuration = drawn ? drawFrames : sheatheFrames;
            _sheatheTimer = _sheatheDuration;
        }

        void CancelWeaponTransition()
        {
            _sheatheTimer = 0;
            _sheatheDuration = 0;
        }

        void EnterRoll()
        {
            bool wasSheathed =
                !WeaponDrawn ||
                (IsWeaponTransitioning && !_sheatheTarget);
            CancelWeaponTransition();
            WeaponDrawn = !wasSheathed;
            CurrentState = State.Rolling;
            CurrentNode = wasSheathed ? Wp00Node.EvadeSheathed : Wp00Node.Evade;
            CurrentAttack = null;
            CurrentChargeStage = ChargeStage.None;
            ChargeLevel = 0;
            _stateFrame = 0;
            _bufferedInput = CoreInput.None;

            Vector3 direction = new Vector3(_moveInput.x, 0f, _moveInput.y);
            if (direction.sqrMagnitude > 0.02f)
                FaceDirection(direction.normalized);
        }

        void TickRolling()
        {
            _cc.Move(transform.forward * (rollSpeed * Time.fixedDeltaTime));
            ApplyGravity();

            if (++_stateFrame < rollFrames) return;

            if (_guardHeld)
            {
                EnterGuard();
                return;
            }

            EndCombo();
        }

        void Move(float speed)
        {
            Vector3 direction = new Vector3(_moveInput.x, 0f, _moveInput.y);
            if (direction.sqrMagnitude > 1f) direction.Normalize();
            _cc.Move(direction * (speed * Time.fixedDeltaTime));
            ApplyGravity();
        }

        void ApplyGravity()
        {
            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;

            _cc.Move(Vector3.up * (_verticalVelocity * Time.fixedDeltaTime));
        }

        void FaceMoveOrAim()
        {
            Vector3 direction = new Vector3(_moveInput.x, 0f, _moveInput.y);
            FaceDirection(
                direction.sqrMagnitude > 0.02f
                    ? direction.normalized
                    : _aimDirection);
        }

        void FaceDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude < 0.001f) return;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up),
                turnDegreesPerSecond * Time.fixedDeltaTime);
        }
    }
}
