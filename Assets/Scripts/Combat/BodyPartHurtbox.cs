using System;
using UnityEngine;

namespace VelkhanaSlice.Combat
{
    public enum BodyPart
    {
        Head,
        Torso,
        FrontLeg,
        RearLeg,
        Wing,
        Tail,
    }

    /// <summary>
    /// One independently damageable section of a monster. Sits on a child collider.
    /// Ice armour absorbs hits until it shatters, which is how the powered phase is ended early.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BodyPartHurtbox : MonoBehaviour
    {
        public BodyPart part = BodyPart.Torso;

        [Tooltip("Head should give the Great Sword its best punish.")]
        public float damageMultiplier = 1f;

        [Tooltip("Accumulated damage that breaks this part. Wings and horns break early, the torso topples.")]
        public float breakThreshold = 400f;

        [Tooltip("Ice armour covering this part. Zero means unarmoured.")]
        public float iceArmorHealth;

        public float AccumulatedDamage { get; private set; }
        public float AccumulatedStagger { get; private set; }
        public bool IsBroken { get; private set; }
        public bool HasIceArmor => iceArmorHealth > 0f;

        public event Action<BodyPartHurtbox> Broken;
        public event Action<BodyPartHurtbox> IceArmorShattered;

        /// <summary>Applies damage that has already been scaled by attack and charge level.</summary>
        public float Apply(float damage, float stagger)
        {
            float dealt = damage * damageMultiplier;

            if (HasIceArmor)
            {
                iceArmorHealth = Mathf.Max(0f, iceArmorHealth - dealt);
                if (iceArmorHealth <= 0f) IceArmorShattered?.Invoke(this);
                return dealt;
            }

            AccumulatedDamage += dealt;
            AccumulatedStagger += stagger;

            if (!IsBroken && AccumulatedDamage >= breakThreshold)
            {
                IsBroken = true;
                Broken?.Invoke(this);
            }
            return dealt;
        }

        public void ConsumeStagger() => AccumulatedStagger = 0f;

        public void RestoreIceArmor(float amount)
        {
            iceArmorHealth = amount;
        }
    }
}
