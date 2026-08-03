using System;
using UnityEngine;
using VelkhanaSlice.Combat;

namespace VelkhanaSlice.Hunter
{
    /// <summary>
    /// Hunter side of the damage exchange. Roll invulnerability, Great Sword guard and tackle
    /// hyper-armour reduction are applied here.
    /// </summary>
    [RequireComponent(typeof(HunterController))]
    public class HunterHealth : MonoBehaviour
    {
        public float maxHealth = 150f;

        public float Current { get; private set; }
        public bool IsDead => Current <= 0f;

        /// <summary>Damage actually taken, after invulnerability and reduction.</summary>
        public event Action<float> Damaged;
        public event Action Died;

        HunterController _controller;

        void Awake()
        {
            _controller = GetComponent<HunterController>();
            Current = maxHealth;
        }

        /// <returns>True when the hit landed. False when it was rolled through or the hunter is down.</returns>
        public bool TakeDamage(float amount)
        {
            return TakeDamage(amount, Vector3.zero, 0);
        }

        /// <returns>True when the hit landed. False when it was rolled through or the hunter is down.</returns>
        public bool TakeDamage(float amount, Vector3 launchVelocity, int knockdownFrames)
        {
            if (IsDead) return false;

            // Rolling through an attack is the whole point of the roll.
            if (_controller.IsInvulnerable) return false;

            // Grounded knockdown recovery is invulnerable. Airborne follow-up hits may still deal
            // damage, but they cannot restart the launch arc or its recovery timer below.
            if (_controller.IsKnockedDown) return false;

            float taken = DamageResolver.ResolveIncoming(amount, _controller.CurrentAttack);
            if (_controller.IsGuarding)
                taken *= _controller.guardDamageMultiplier;
            Current = Mathf.Max(0f, Current - taken);
            Damaged?.Invoke(taken);

            // Guard and tackle-style hyper armour absorb the displacement reaction. Ordinary
            // attacks still interrupt exactly as before when this hit has no launch metadata.
            bool resistsLaunch = _controller.IsGuarding || _controller.HasHyperArmor ||
                                 _controller.IsLaunched;
            if (!resistsLaunch && launchVelocity.sqrMagnitude > 0.001f)
                _controller.Launch(launchVelocity, knockdownFrames);
            else
                _controller.Interrupt();

            if (IsDead) Died?.Invoke();
            return true;
        }

        public void Revive()
        {
            Current = maxHealth;
        }
    }
}
