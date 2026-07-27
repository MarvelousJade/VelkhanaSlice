using UnityEngine;

namespace VelkhanaSlice.Combat
{
    public struct HitResult
    {
        public bool Connected;
        public float Damage;
        public BodyPart Part;
        public bool BrokePart;
        public bool ShatteredArmor;
        public int HitstopFrames;
    }

    /// <summary>
    /// Single place where an active hitbox turns into damage. Everything that deals damage
    /// routes through here so charge scaling and hitstop stay consistent.
    /// </summary>
    public static class DamageResolver
    {
        public const int BaseHitstopFrames = 3;
        public const int HitstopFramesPerChargeLevel = 2;

        public static HitResult Resolve(
            AttackDefinition attack,
            int chargeLevel,
            bool previousHitConnected,
            BodyPartHurtbox target)
        {
            if (attack == null || target == null) return default;

            bool hadArmor = target.HasIceArmor;
            bool wasBroken = target.IsBroken;

            float raw = attack.DamageAt(chargeLevel, previousHitConnected);
            float dealt = target.Apply(raw, attack.staggerDamage);

            return new HitResult
            {
                Connected = true,
                Damage = dealt,
                Part = target.part,
                BrokePart = !wasBroken && target.IsBroken,
                ShatteredArmor = hadArmor && !target.HasIceArmor,
                HitstopFrames = BaseHitstopFrames + chargeLevel * HitstopFramesPerChargeLevel,
            };
        }

        /// <summary>Damage taken by the hunter, reduced while a hyper-armour move is playing.</summary>
        public static float ResolveIncoming(float damage, AttackDefinition activeHunterAttack)
        {
            if (activeHunterAttack == null) return damage;
            return damage * (1f - Mathf.Clamp01(activeHunterAttack.incomingDamageReduction));
        }
    }
}
