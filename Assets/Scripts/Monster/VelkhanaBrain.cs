using System;
using System.Collections.Generic;
using UnityEngine;
using VelkhanaSlice.Combat;

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
    public class VelkhanaBrain : MonoBehaviour
    {
        [Header("Target")]
        public Transform hunter;

        [Header("Range bands (metres)")]
        public float closeRange = 6f;
        public float mediumRange = 16f;

        [Header("Pacing (frames @ 60 Hz)")]
        [Tooltip("Idle frames between attacks. Shrinks in the powered stages.")]
        public int neutralFrames = 60;
        [Range(0.1f, 1f)] public float poweredPacingMultiplier = 0.65f;

        [Header("Attacks")]
        public List<MonsterAttackOption> options = new List<MonsterAttackOption>();

        [Header("Phase")]
        public ArmorStage stage = ArmorStage.Neutral;
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

        public event Action<ArmorStage> StageChanged;

        int _idleFrames;
        int _armorBreaks;
        MonsterAttackOption _lastUsed;
        Vector3 _committedAimDirection;

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

            if (CurrentAttack != null) { TickAttack(); return; }

            int pacing = stage == ArmorStage.Neutral
                ? neutralFrames
                : Mathf.RoundToInt(neutralFrames * poweredPacingMultiplier);

            if (++_idleFrames < pacing) return;

            MonsterAttackOption picked = Choose();
            if (picked == null) return;

            _idleFrames = 0;
            _lastUsed = picked;
            picked.CooldownRemaining = picked.cooldownFrames;
            CurrentAttack = picked.attack;
            AttackFrame = 0;
        }

        void TickCooldowns()
        {
            for (int i = 0; i < options.Count; i++)
                if (options[i].CooldownRemaining > 0) options[i].CooldownRemaining--;
        }

        void TickAttack()
        {
            // Tracking stops at the definition's cutoff frame. After that the attack is committed
            // and cannot be steered, which is what makes positioning beat it.
            if (CurrentAttack.CanTrack(AttackFrame)) _committedAimDirection = DirectionToHunter();

            float step = CurrentAttack.ForwardStep(AttackFrame);
            if (!Mathf.Approximately(step, 0f))
                transform.position += _committedAimDirection * step;

            if (_committedAimDirection.sqrMagnitude > 0.001f && CurrentAttack.CanTrack(AttackFrame))
                transform.rotation = Quaternion.LookRotation(_committedAimDirection, Vector3.up);

            // Velkhana never cancels out of an attack; it always plays to its recovery.
            if (++AttackFrame < CurrentAttack.TotalFrames) return;

            CurrentAttack = null;
            AttackFrame = 0;
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

        Vector3 DirectionToHunter()
        {
            if (hunter == null) return transform.forward;
            Vector3 flat = hunter.position - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude > 0.001f ? flat.normalized : transform.forward;
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
