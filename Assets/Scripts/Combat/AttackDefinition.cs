using UnityEngine;

namespace VelkhanaSlice.Combat
{
    /// <summary>
    /// Frame-exact definition of a single attack, used by both the hunter and Velkhana.
    /// All timings are simulation frames at a fixed 60 Hz, so they can be compared
    /// directly against frame-stepped reference footage.
    /// </summary>
    [CreateAssetMenu(menuName = "Velkhana/Attack Definition", fileName = "Attack_")]
    public class AttackDefinition : ScriptableObject
    {
        [Header("Timing (frames @ 60 Hz)")]
        public int startupFrames = 12;
        public int activeFrames = 4;
        public int recoveryFrames = 24;

        [Tooltip("Rotation toward the aim direction stops on this frame. The attack is committed after it.")]
        public int trackingCutoffFrame = 8;

        [Tooltip("First frame on which a follow-up may be buffered. -1 means the attack cannot be cancelled.")]
        public int cancelWindowStart = -1;

        [Header("Damage")]
        public float damage = 100f;
        public float staggerDamage = 40f;

        [Tooltip("Damage multiplier per charge level. Index 0 is uncharged.")]
        public float[] chargeMultipliers = { 1f, 1.4f, 1.8f, 2.4f };

        [Header("Defence")]
        [Tooltip("Tackle and similar moves keep playing through incoming hits.")]
        public bool hyperArmor;
        [Range(0f, 1f)] public float incomingDamageReduction;

        [Header("Motion")]
        [Tooltip("Forward displacement across the attack, sampled from 0 to 1 over its total length.")]
        public AnimationCurve forwardMotion = AnimationCurve.Constant(0f, 1f, 0f);
        [Tooltip("Metres the forward motion curve is scaled to.")]
        public float forwardMotionScale;

        [Header("Combo graph")]
        [Tooltip("Attacks reachable from this one. These links are the combo graph; there is no separate graph asset.")]
        public AttackDefinition[] followUps = new AttackDefinition[0];

        [Tooltip("True Charged Slash rule: charge scaling only applies when the previous hit connected.")]
        public bool requiresPreviousHitConnected;

        public int TotalFrames => startupFrames + activeFrames + recoveryFrames;

        public bool IsHitActive(int frame) => frame >= startupFrames && frame < startupFrames + activeFrames;

        public bool CanTrack(int frame) => frame < trackingCutoffFrame;

        public bool CanCancel(int frame) => cancelWindowStart >= 0 && frame >= cancelWindowStart;

        /// <summary>Metres to move on the given frame of this attack.</summary>
        public float ForwardStep(int frame)
        {
            if (Mathf.Approximately(forwardMotionScale, 0f) || TotalFrames <= 0) return 0f;
            float a = forwardMotion.Evaluate(Mathf.Clamp01((float)frame / TotalFrames));
            float b = forwardMotion.Evaluate(Mathf.Clamp01((float)(frame + 1) / TotalFrames));
            return (b - a) * forwardMotionScale;
        }

        public float DamageAt(int chargeLevel, bool previousHitConnected)
        {
            if (requiresPreviousHitConnected && !previousHitConnected) chargeLevel = 0;
            if (chargeMultipliers == null || chargeMultipliers.Length == 0) return damage;
            return damage * chargeMultipliers[Mathf.Clamp(chargeLevel, 0, chargeMultipliers.Length - 1)];
        }

        public bool CanFollowInto(AttackDefinition next)
        {
            if (next == null || followUps == null) return false;
            for (int i = 0; i < followUps.Length; i++)
                if (followUps[i] == next) return true;
            return false;
        }
    }
}
