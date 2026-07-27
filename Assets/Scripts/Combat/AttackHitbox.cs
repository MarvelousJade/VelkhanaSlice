using UnityEngine;

namespace VelkhanaSlice.Combat
{
    /// <summary>Anything that plays an <see cref="AttackDefinition"/> frame by frame.</summary>
    public interface IAttacker
    {
        AttackDefinition CurrentAttack { get; }
        int AttackFrame { get; }
    }

    /// <summary>
    /// Turns an attack's local hitbox into a world query. Hunter and monster both go through here,
    /// so a swing and a breath resolve against the same box maths.
    /// </summary>
    public static class AttackHitbox
    {
        public static Vector3 WorldCenter(Transform attacker, AttackDefinition attack)
        {
            return attacker.TransformPoint(attack.hitboxCenter);
        }

        public static int Overlap(Transform attacker, AttackDefinition attack, int layerMask, Collider[] buffer)
        {
            if (attack == null || !attack.HasHitbox) return 0;

            return Physics.OverlapBoxNonAlloc(
                WorldCenter(attacker, attack),
                attack.hitboxSize * 0.5f,
                buffer,
                attacker.rotation,
                layerMask,
                QueryTriggerInteraction.Collide);
        }
    }
}
