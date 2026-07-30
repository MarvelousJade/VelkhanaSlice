using UnityEngine;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;

namespace VelkhanaSlice.DebugTools
{
    /// <summary>
    /// Bare IMGUI readout of the combat state. Not the shipping HUD, just enough to see that damage,
    /// charge levels and part breaks are actually happening.
    /// </summary>
    public class CombatHud : MonoBehaviour
    {
        public HunterHealth health;
        public HunterController hunterController;
        public VelkhanaBrain brain;

        GUIStyle _label;

        void OnGUI()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };

            if (health != null)
            {
                float fraction = health.maxHealth <= 0f ? 0f : health.Current / health.maxHealth;
                GUI.color = Color.black;
                GUI.Box(new Rect(18f, 18f, 304f, 26f), GUIContent.none);
                GUI.color = Color.Lerp(Color.red, new Color(0.35f, 0.85f, 0.4f), fraction);
                GUI.Box(new Rect(20f, 20f, 300f * fraction, 22f), GUIContent.none);
                GUI.color = Color.white;
                GUI.Label(new Rect(24f, 20f, 320f, 24f), $"HP {health.Current:0} / {health.maxHealth:0}", _label);
            }

            if (hunterController != null)
            {
                string attack = hunterController.CurrentAttack != null
                    ? $"{hunterController.CurrentAttack.name} f{hunterController.AttackFrame}"
                    : "-";
                Vector3 p = hunterController.transform.position;
                GUI.Label(new Rect(20f, 50f, 800f, 24f),
                    $"state {hunterController.CurrentState}  charge {hunterController.ChargeLevel}  " +
                    $"drawn {hunterController.WeaponDrawn}  running {hunterController.IsRunning}  " +
                    $"pos({p.x:0.0},{p.z:0.0})  attack {attack}", _label);
            }

            if (brain != null)
            {
                string attack = brain.CurrentAttack != null
                    ? $"{brain.CurrentAttack.name} f{brain.AttackFrame}"
                    : "-";
                GUI.Label(new Rect(20f, 76f, 800f, 24f),
                    $"velkhana {brain.CurrentState} f{brain.StateFrame}  {brain.stage}  " +
                    $"enraged {brain.enraged}  target {brain.DesiredBand}  attack {attack}", _label);

                float y = 102f;
                foreach (var part in brain.GetComponentsInChildren<BodyPartHurtbox>())
                {
                    if (!part.IsBroken && part.AccumulatedDamage <= 0f) continue;
                    GUI.Label(new Rect(20f, y, 600f, 22f),
                        $"{part.part} {part.AccumulatedDamage:0}{(part.IsBroken ? "  BROKEN" : "")}", _label);
                    y += 22f;
                }
            }
        }
    }
}
