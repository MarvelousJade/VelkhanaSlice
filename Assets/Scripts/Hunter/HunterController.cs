using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using VelkhanaSlice.Combat;

namespace VelkhanaSlice.Hunter
{
    /// <summary>
    /// Hunter movement and Great Sword combat. All gameplay runs in FixedUpdate at 60 Hz and
    /// displacement is driven by code, not by animation, so timings can be compared to reference
    /// footage frame by frame. Animation is presentation only.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HunterController : MonoBehaviour, IAttacker
    {
        public enum State { Free, Charging, Attacking, Rolling }

        [Header("Movement (metres per second)")]
        public float sheathedSpeed = 5.5f;
        public float drawnSpeed = 2.2f;
        public float turnDegreesPerSecond = 720f;

        [Header("Roll (frames @ 60 Hz)")]
        public float rollSpeed = 7f;
        public int rollFrames = 26;
        public int rollInvulnStart = 4;
        public int rollInvulnEnd = 16;

        [Header("Sheathing (frames @ 60 Hz)")]
        public int drawFrames = 20;
        public int sheatheFrames = 26;

        [Header("Great Sword")]
        public AttackDefinition drawSlash;
        public AttackDefinition chargedSlash;
        public AttackDefinition wideSlash;
        public AttackDefinition tackle;

        [Tooltip("Frames of hold needed to reach charge levels 1, 2 and 3. Holding past the last one overcharges.")]
        public int[] chargeThresholds = { 40, 75, 110 };
        [Tooltip("Frames past full charge before the swing drops back to charge level 1.")]
        public int overchargeFrames = 45;

        [Header("Hit detection")]
        [Tooltip("Where the sword visual sits. The damage box itself comes from the attack definition.")]
        public Transform bladePoint;
        public LayerMask hurtboxLayers = ~0;

        [Header("Camera")]
        [Tooltip("Camera used to turn mouse position into a ground-plane aim direction.")]
        public Camera aimCamera;

        public State CurrentState { get; private set; } = State.Free;
        public bool WeaponDrawn { get; private set; }
        public int ChargeLevel { get; private set; }
        public AttackDefinition CurrentAttack { get; private set; }
        public int AttackFrame { get; private set; }
        public bool IsInvulnerable => CurrentState == State.Rolling && _stateFrame >= rollInvulnStart && _stateFrame < rollInvulnEnd;
        public bool HasHyperArmor => CurrentState == State.Attacking && CurrentAttack != null && CurrentAttack.hyperArmor;

        CharacterController _cc;
        int _stateFrame;
        int _chargeFrames;
        int _hitstopFrames;
        int _sheatheTimer;
        float _verticalVelocity;
        bool _previousHitConnected;
        Vector3 _aimDirection = Vector3.forward;
        AttackDefinition _bufferedAttack;
        readonly HashSet<BodyPartHurtbox> _hitThisSwing = new HashSet<BodyPartHurtbox>();
        // A wide swing can cover every body part at once, so leave headroom above the nine hurtboxes.
        readonly Collider[] _overlapBuffer = new Collider[32];

        // Input is polled every render frame and consumed by the fixed-step simulation, because
        // a button press can happen between two fixed steps and must not be dropped.
        Vector2 _moveInput;
        Vector2 _aimInput;
        bool _primaryHeld;
        bool _secondaryPressed;
        bool _dodgePressed;
        bool _sheathePressed;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (aimCamera == null) aimCamera = Camera.main;
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
            TickSheathe();

            switch (CurrentState)
            {
                case State.Free: TickFree(); break;
                case State.Charging: TickCharging(); break;
                case State.Attacking: TickAttacking(); break;
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

            _primaryHeld = (gp != null && gp.buttonWest.isPressed) || (mouse != null && mouse.leftButton.isPressed);

            _secondaryPressed |= (gp != null && gp.buttonNorth.wasPressedThisFrame)
                                 || (mouse != null && mouse.rightButton.wasPressedThisFrame);
            _dodgePressed |= (gp != null && gp.buttonEast.wasPressedThisFrame)
                             || (kb != null && kb.spaceKey.wasPressedThisFrame);
            _sheathePressed |= (gp != null && gp.buttonSouth.wasPressedThisFrame)
                               || (kb != null && kb.fKey.wasPressedThisFrame);
        }

        void ClearEdgeInput()
        {
            _secondaryPressed = false;
            _dodgePressed = false;
            _sheathePressed = false;
        }

        void UpdateAim()
        {
            // ponytail: gamepad stick wins, otherwise the mouse is projected onto the hunter's
            // ground plane. Good enough until lock-on exists.
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

        void TickSheathe()
        {
            if (_sheatheTimer > 0)
            {
                _sheatheTimer--;
                if (_sheatheTimer == 0) WeaponDrawn = !WeaponDrawn;
                return;
            }

            if (_sheathePressed && CurrentState == State.Free)
                _sheatheTimer = WeaponDrawn ? sheatheFrames : drawFrames;
        }

        void TickFree()
        {
            Move(WeaponDrawn ? drawnSpeed : sheathedSpeed);
            FaceMoveOrAim();

            if (_dodgePressed) { EnterRoll(); return; }

            if (_primaryHeld)
            {
                // Sheathed, the first swing is a draw slash and it starts already committed.
                if (!WeaponDrawn) { WeaponDrawn = true; BeginAttack(drawSlash); return; }
                CurrentState = State.Charging;
                _chargeFrames = 0;
                return;
            }

            if (_secondaryPressed && WeaponDrawn) BeginAttack(wideSlash);
        }

        void TickCharging()
        {
            Move(drawnSpeed * 0.5f);
            FaceMoveOrAim();
            _chargeFrames++;

            // Tackle out of a charge is the Iceborne route that skips combo stages.
            if (_secondaryPressed) { BeginAttack(tackle); return; }

            if (_dodgePressed) { EnterRoll(); return; }

            if (!_primaryHeld)
            {
                ChargeLevel = ChargeLevelFor(_chargeFrames);
                BeginAttack(chargedSlash);
            }
        }

        /// <summary>Charge level for a hold length. Public so the thresholds can be tested directly.</summary>
        public int ChargeLevelFor(int heldFrames)
        {
            int level = 0;
            for (int i = 0; i < chargeThresholds.Length; i++)
                if (heldFrames >= chargeThresholds[i]) level = i + 1;

            // Overcharging past the last threshold drops the swing back down.
            int last = chargeThresholds.Length > 0 ? chargeThresholds[chargeThresholds.Length - 1] : 0;
            if (level == chargeThresholds.Length && heldFrames >= last + overchargeFrames) level = 1;
            return level;
        }

        void BeginAttack(AttackDefinition attack)
        {
            if (attack == null) { CurrentState = State.Free; return; }

            CurrentAttack = attack;
            CurrentState = State.Attacking;
            AttackFrame = 0;
            _bufferedAttack = null;
            _hitThisSwing.Clear();
        }

        void TickAttacking()
        {
            var attack = CurrentAttack;

            // Rotation is allowed only before the tracking cutoff, which is what makes a heavy
            // swing commit and makes dodging around it meaningful.
            if (attack.CanTrack(AttackFrame)) FaceDirection(_aimDirection);

            float step = attack.ForwardStep(AttackFrame);
            if (!Mathf.Approximately(step, 0f)) _cc.Move(transform.forward * step);
            ApplyGravity();

            if (attack.IsHitActive(AttackFrame)) CheckHits(attack);

            if (attack.CanCancel(AttackFrame))
            {
                if (_dodgePressed) { EnterRoll(); return; }
                if (_secondaryPressed && attack.CanFollowInto(tackle)) _bufferedAttack = tackle;
                else if (_primaryHeld && attack.followUps.Length > 0) _bufferedAttack = attack.followUps[0];
            }

            AttackFrame++;
            if (AttackFrame < attack.TotalFrames) return;

            if (_bufferedAttack != null) { BeginAttack(_bufferedAttack); return; }

            CurrentAttack = null;
            ChargeLevel = 0;
            CurrentState = State.Free;
        }

        void CheckHits(AttackDefinition attack)
        {
            int count = AttackHitbox.Overlap(transform, attack, hurtboxLayers, _overlapBuffer);

            for (int i = 0; i < count; i++)
            {
                var hurtbox = _overlapBuffer[i].GetComponentInParent<BodyPartHurtbox>();
                if (hurtbox == null || !_hitThisSwing.Add(hurtbox)) continue;

                HitResult result = DamageResolver.Resolve(attack, ChargeLevel, _previousHitConnected, hurtbox);
                _previousHitConnected = result.Connected;
                _hitstopFrames = Mathf.Max(_hitstopFrames, result.HitstopFrames);
            }

            // A swing that reached the end of its active frames without connecting breaks the
            // True Charged Slash chain.
            if (AttackFrame == attack.startupFrames + attack.activeFrames - 1 && _hitThisSwing.Count == 0)
                _previousHitConnected = false;
        }

        /// <summary>
        /// Cuts the current attack short after taking a hit. Hyper-armour moves such as the tackle
        /// plough through instead, which is the reason to use them.
        /// </summary>
        public void Interrupt()
        {
            if (HasHyperArmor || CurrentState != State.Attacking) return;

            CurrentAttack = null;
            ChargeLevel = 0;
            _bufferedAttack = null;
            _hitThisSwing.Clear();
            CurrentState = State.Free;
        }

        void EnterRoll()
        {
            CurrentState = State.Rolling;
            _stateFrame = 0;
            CurrentAttack = null;
            ChargeLevel = 0;

            Vector3 dir = new Vector3(_moveInput.x, 0f, _moveInput.y);
            if (dir.sqrMagnitude > 0.02f) FaceDirection(dir.normalized);
        }

        void TickRolling()
        {
            _cc.Move(transform.forward * (rollSpeed * Time.fixedDeltaTime));
            ApplyGravity();

            if (++_stateFrame >= rollFrames) CurrentState = State.Free;
        }

        void Move(float speed)
        {
            Vector3 dir = new Vector3(_moveInput.x, 0f, _moveInput.y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            _cc.Move(dir * (speed * Time.fixedDeltaTime));
            ApplyGravity();
        }

        void ApplyGravity()
        {
            // Gravity has to accumulate. Moving by a constant each frame gives a fixed fall speed
            // rather than a fall, which reads wrong the moment the hunter leaves the ground.
            if (_cc.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += Physics.gravity.y * Time.fixedDeltaTime;

            _cc.Move(Vector3.up * (_verticalVelocity * Time.fixedDeltaTime));
        }

        void FaceMoveOrAim()
        {
            Vector3 dir = new Vector3(_moveInput.x, 0f, _moveInput.y);
            FaceDirection(dir.sqrMagnitude > 0.02f ? dir.normalized : _aimDirection);
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
