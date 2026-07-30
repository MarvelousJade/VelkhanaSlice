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
    /// High-level states exposed to presentation, HUD and tests. The original EM124 data divides
    /// decisions and actions across a THKLST project; this compact state set is our readable Unity
    /// boundary for that same decide/reposition/commit/recover loop.
    /// </summary>
    public enum VelkhanaState
    {
        Observe,
        Reposition,
        Attacking,
        Recovery,
    }

    /// <summary>One attack Velkhana can pick, with the context that makes it legal.</summary>
    [Serializable]
    public class MonsterAttackOption
    {
        public AttackDefinition attack;
        public RangeBand band = RangeBand.Close;

        [Tooltip("Lowest armour stage this attack is available in.")]
        public ArmorStage minimumStage = ArmorStage.Neutral;

        [Tooltip("Selection weight before context modifiers.")]
        public float weight = 1f;

        [Tooltip("Additional weight used by EM124's distinct enraged selection branch.")]
        [Min(0f)] public float enragedWeightMultiplier = 1f;

        [Tooltip("Frames before this attack may be chosen again.")]
        public int cooldownFrames = 180;

        [Tooltip("Only usable when the hunter is roughly in front of Velkhana.")]
        public bool requiresHunterInFront;

        [NonSerialized] public int CooldownRemaining;
    }

    /// <summary>
    /// Velkhana's decision layer. Picks attacks from context and weight, never from player input.
    /// Runs on the same fixed 60 Hz step as the hunter.
    /// </summary>
    public class VelkhanaBrain : MonoBehaviour, IAttacker
    {
        [Header("Target")]
        public Transform hunter;

        [Tooltip("Layers searched for the hunter when an attack's hitbox is active.")]
        public LayerMask hunterLayers = ~0;

        [Header("Range bands (metres)")]
        [Tooltip("EM124 Combat_Enter reference threshold: distance_2d <= 850 game units.")]
        public float closeRange = 8.5f;
        [Tooltip("EM124 Combat_Enter reference threshold: distance_3d <= 1700 game units.")]
        public float mediumRange = 17f;

        [Header("Pacing (frames @ 60 Hz)")]
        [Tooltip("Idle frames between attacks. Shrinks in the powered stages.")]
        public int neutralFrames = 60;
        [Range(0.1f, 1f)] public float poweredPacingMultiplier = 0.65f;

        [Tooltip("How fast Velkhana turns to face the hunter between attacks.")]
        public float idleTurnDegreesPerSecond = 90f;

        [Header("Repositioning (no NavMesh)")]
        [Tooltip("Ground speed while moving into a range band that has a legal attack.")]
        public float repositionSpeed = 3.2f;
        [Tooltip("Turn speed while repositioning.")]
        public float repositionTurnDegreesPerSecond = 150f;
        [Tooltip("Distance error small enough to orbit rather than move directly in or out.")]
        public float repositionDistanceTolerance = 1.25f;
        [Tooltip("Frames between attack checks while repositioning.")]
        [Min(1)] public int repositionDecisionIntervalFrames = 12;
        [Tooltip("Return to Observe after this many reposition frames, even if no attack became legal.")]
        [Min(1)] public int maxRepositionFrames = 240;
        [Range(0f, 1f)] public float repositionOrbitWeight = 0.55f;
        [Tooltip("Keeps direct transform locomotion inside the graybox arena.")]
        public Vector3 arenaCenter = Vector3.zero;
        [Min(1f)] public float arenaRadius = 28f;

        [Header("Attacks")]
        public List<MonsterAttackOption> options = new List<MonsterAttackOption>();

        [Header("Phase")]
        public ArmorStage stage = ArmorStage.Neutral;
        [Tooltip("EM124 Combat_Main has separate enraged and non-enraged weighted branches.")]
        public bool enraged;
        [Tooltip("Ice armour applied to each armoured part when a powered stage begins.")]
        public float armorPerPart = 300f;
        [Tooltip("Armoured parts that must shatter to drop out of a powered stage early.")]
        public int armorBreaksToInterrupt = 2;
        public BodyPartHurtbox[] armoredParts = new BodyPartHurtbox[0];

        [Header("Repetition")]
        [Tooltip("Weight multiplier applied to the attack used last, so Velkhana does not spam it.")]
        [Range(0f, 1f)] public float repeatPenalty = 0.25f;

        public AttackDefinition CurrentAttack { get; private set; }
        public int AttackFrame { get; private set; }
        public VelkhanaState CurrentState { get; private set; } = VelkhanaState.Observe;
        public int StateFrame { get; private set; }
        public RangeBand DesiredBand { get; private set; } = RangeBand.Medium;

        public event Action<ArmorStage> StageChanged;
        public event Action<VelkhanaState> StateChanged;

        int _armorBreaks;
        float _orbitSign = 1f;
        MonsterAttackOption _lastUsed;
        Vector3 _committedAimDirection;
        // Sized for the widest sweep box. A full buffer silently drops targets, so leave headroom.
        readonly Collider[] _overlapBuffer = new Collider[32];
        readonly HashSet<HunterHealth> _hitThisAttack = new HashSet<HunterHealth>();

        void OnEnable()
        {
            foreach (var part in armoredParts)
                if (part != null) part.IceArmorShattered += OnArmorShattered;
        }

        void OnDisable()
        {
            foreach (var part in armoredParts)
                if (part != null) part.IceArmorShattered -= OnArmorShattered;
        }

        void FixedUpdate()
        {
            TickCooldowns();

            switch (CurrentState)
            {
                case VelkhanaState.Attacking:
                case VelkhanaState.Recovery:
                    TickAttack();
                    break;
                case VelkhanaState.Reposition:
                    TickReposition();
                    break;
                default:
                    TickObserve();
                    break;
            }
        }

        void TickObserve()
        {
            TurnTowardHunter(idleTurnDegreesPerSecond);
            StateFrame++;

            int pacing = stage == ArmorStage.Neutral
                ? neutralFrames
                : Mathf.RoundToInt(neutralFrames * poweredPacingMultiplier);

            if (StateFrame < Mathf.Max(1, pacing)) return;

            MonsterAttackOption picked = Choose();
            if (picked != null)
            {
                StartAttack(picked);
                return;
            }

            DesiredBand = ChooseRepositionBand();
            Vector3 toHunter = DirectionToHunter();
            _orbitSign = Vector3.Dot(transform.right, toHunter) >= 0f ? -1f : 1f;
            EnterState(VelkhanaState.Reposition);
        }

        void TickReposition()
        {
            if (hunter == null)
            {
                EnterState(VelkhanaState.Observe);
                return;
            }

            StateFrame++;
            TurnTowardHunter(repositionTurnDegreesPerSecond);

            if (StateFrame == 1 || StateFrame % Mathf.Max(1, repositionDecisionIntervalFrames) == 0)
            {
                MonsterAttackOption picked = Choose();
                if (picked != null)
                {
                    StartAttack(picked);
                    return;
                }

                DesiredBand = ChooseRepositionBand();
            }

            float desiredDistance = DesiredDistanceForBand(DesiredBand, closeRange, mediumRange);
            Vector3 moveDirection = CalculateRepositionDirection(
                transform.position,
                transform.forward,
                hunter.position,
                desiredDistance,
                repositionDistanceTolerance,
                _orbitSign,
                repositionOrbitWeight);

            Vector3 next = transform.position +
                           moveDirection * (Mathf.Max(0f, repositionSpeed) * Time.fixedDeltaTime);
            next = ClampToArena(next);
            transform.position = next;

            if (StateFrame >= Mathf.Max(1, maxRepositionFrames))
                EnterState(VelkhanaState.Observe);
        }

        void StartAttack(MonsterAttackOption picked)
        {
            _lastUsed = picked;
            picked.CooldownRemaining = picked.cooldownFrames;
            CurrentAttack = picked.attack;
            AttackFrame = 0;
            _committedAimDirection = DirectionToHunter();
            _hitThisAttack.Clear();
            EnterState(VelkhanaState.Attacking);
        }

        /// <summary>
        /// Applies the active hitbox to the hunter, once per attack. The hunter side decides what
        /// the hit is worth, since roll invulnerability and hyper armour live there.
        /// </summary>
        void CheckHunterHit(AttackDefinition attack)
        {
            int count = AttackHitbox.Overlap(transform, attack, hunterLayers, _overlapBuffer);

            for (int i = 0; i < count; i++)
            {
                var health = _overlapBuffer[i].GetComponentInParent<HunterHealth>();
                if (health == null || !_hitThisAttack.Add(health)) continue;
                health.TakeDamage(attack.damage);
            }
        }

        /// <summary>
        /// Faces the hunter between attacks. Without this a monster whose options all require the
        /// hunter in front can end up facing away and never choose anything again, which is exactly
        /// what happens when the player circles behind it.
        /// </summary>
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
                if (options[i].CooldownRemaining > 0) options[i].CooldownRemaining--;
        }

        void TickAttack()
        {
            if (CurrentAttack == null)
            {
                AttackFrame = 0;
                EnterState(VelkhanaState.Observe);
                return;
            }

            int recoveryStart = CurrentAttack.startupFrames + CurrentAttack.activeFrames;
            if (AttackFrame >= recoveryStart && CurrentState != VelkhanaState.Recovery)
                EnterState(VelkhanaState.Recovery);

            StateFrame++;

            // Tracking stops at the definition's cutoff frame. After that the attack is committed
            // and cannot be steered, which is what makes positioning beat it.
            if (CurrentAttack.CanTrack(AttackFrame)) _committedAimDirection = DirectionToHunter();

            float step = CurrentAttack.ForwardStep(AttackFrame);
            if (!Mathf.Approximately(step, 0f))
                transform.position += _committedAimDirection * step;

            if (_committedAimDirection.sqrMagnitude > 0.001f && CurrentAttack.CanTrack(AttackFrame))
                transform.rotation = Quaternion.LookRotation(_committedAimDirection, Vector3.up);

            if (CurrentAttack.IsHitActive(AttackFrame)) CheckHunterHit(CurrentAttack);

            // Velkhana never cancels out of an attack; it always plays to its recovery.
            if (++AttackFrame < CurrentAttack.TotalFrames) return;

            CurrentAttack = null;
            AttackFrame = 0;
            EnterState(VelkhanaState.Observe);
        }

        MonsterAttackOption Choose()
        {
            if (hunter == null || options.Count == 0) return null;

            RangeBand band = BandToHunter();
            bool inFront = Vector3.Dot(transform.forward, DirectionToHunter()) > 0.35f;

            float total = 0f;
            var weights = new float[options.Count];

            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option.attack == null) continue;
                if (option.CooldownRemaining > 0) continue;
                if (option.band != band) continue;
                if (option.minimumStage > stage) continue;
                if (option.requiresHunterInFront && !inFront) continue;

                float w = Mathf.Max(0f, option.weight);
                if (enraged) w *= Mathf.Max(0f, option.enragedWeightMultiplier);
                if (option == _lastUsed) w *= repeatPenalty;
                weights[i] = w;
                total += w;
            }

            if (total <= 0f) return null;

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < options.Count; i++)
            {
                roll -= weights[i];
                if (roll <= 0f && weights[i] > 0f) return options[i];
            }
            return null;
        }

        RangeBand BandToHunter()
        {
            float distance = Vector3.Distance(transform.position, hunter.position);
            if (distance <= closeRange) return RangeBand.Close;
            return distance <= mediumRange ? RangeBand.Medium : RangeBand.Far;
        }

        RangeBand ChooseRepositionBand()
        {
            if (hunter == null || options.Count == 0) return RangeBand.Medium;

            float currentDistance = Vector3.Distance(transform.position, hunter.position);
            RangeBand best = BandToHunter();
            float bestError = float.PositiveInfinity;

            // Prefer an off-cooldown attack, but still move toward a useful band while all legal
            // attacks cool down. That prevents the monster from freezing between THK-like choices.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < options.Count; i++)
                {
                    MonsterAttackOption option = options[i];
                    if (option.attack == null || option.minimumStage > stage) continue;
                    if (pass == 0 && option.CooldownRemaining > 0) continue;

                    float desired = DesiredDistanceForBand(option.band, closeRange, mediumRange);
                    float error = Mathf.Abs(currentDistance - desired);
                    if (error >= bestError) continue;

                    bestError = error;
                    best = option.band;
                }

                if (!float.IsPositiveInfinity(bestError)) break;
            }

            return best;
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

        /// <summary>Centre point used by direct locomotion for each attack range band.</summary>
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
        /// attack distance; a tangent component keeps motion readable while the body turns.
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

        public void AdvanceStage()
        {
            SetStage(stage == ArmorStage.Ultimate ? ArmorStage.Neutral : stage + 1);
        }

        void SetStage(ArmorStage next)
        {
            stage = next;
            _armorBreaks = 0;

            float armor = stage == ArmorStage.IceArmorStage1 || stage == ArmorStage.IceArmorStage2
                ? armorPerPart * (int)stage
                : 0f;

            foreach (var part in armoredParts)
                if (part != null) part.RestoreIceArmor(armor);

            StageChanged?.Invoke(stage);
        }

        void OnArmorShattered(BodyPartHurtbox part)
        {
            if (stage == ArmorStage.Neutral) return;
            if (++_armorBreaks < armorBreaksToInterrupt) return;

            // Enough armour broken: the powered stage ends early instead of reaching the ultimate.
            SetStage(ArmorStage.Neutral);
        }
    }
}
