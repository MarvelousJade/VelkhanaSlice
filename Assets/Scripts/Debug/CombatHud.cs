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
        CombatVolumeDebug _volumeDebug;

        void Awake()
        {
            _volumeDebug = GetComponent<CombatVolumeDebug>();
            if (_volumeDebug == null) _volumeDebug = gameObject.AddComponent<CombatVolumeDebug>();
            _volumeDebug.Bind(health, hunterController, brain);
        }

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
                    $"state {hunterController.CurrentState}  WP00 {hunterController.CurrentNode}  " +
                    $"charge {hunterController.CurrentChargeStage}/Lv{hunterController.ChargeLevel}  " +
                    $"drawn {hunterController.WeaponDrawn}  running {hunterController.IsRunning}  " +
                    $"pos({p.x:0.0},{p.z:0.0})  attack {attack}", _label);
            }

            GUI.Label(new Rect(840f, 50f, 420f, 24f),
                $"F3 volumes {(_volumeDebug != null && _volumeDebug.Visible ? "ON" : "OFF")}", _label);

            if (brain != null)
            {
                string attack = brain.CurrentAttack != null
                    ? $"{brain.CurrentAttack.name} f{brain.AttackFrame}"
                    : "-";
                string thkTrace = string.IsNullOrEmpty(brain.CurrentThkTrace)
                    ? "-"
                    : brain.CurrentThkTrace;
                string traceKind = brain.IsGroundOpenerSliceActive
                    ? "opener slice: "
                    : string.Empty;
                GUI.Label(new Rect(20f, 76f, 800f, 24f),
                    $"velkhana {brain.CurrentState} f{brain.StateFrame}  {brain.CurrentContext}  " +
                    $"{brain.CombatMode}/{brain.stage}  airborne {brain.IsAirborne}  " +
                    $"topple {brain.CurrentToppleCause} {brain.ToppleFramesRemaining}f", _label);
                GUI.Label(new Rect(20f, 102f, 1500f, 24f),
                    $"boss HP {brain.CurrentHealth:0}/{brain.maxHealth:0}  " +
                    $"enraged {brain.enraged} rage {brain.RageBuild:P0}  " +
                    $"target {brain.DesiredDistance:0.0}m  THK {traceKind}{thkTrace}  " +
                    $"sequence {brain.SequenceStep + 1}/{brain.SequenceLength}  attack {attack}", _label);

                float y = 128f;
                foreach (var part in brain.GetComponentsInChildren<BodyPartHurtbox>())
                {
                    float groupStagger = brain.GetAccumulatedStagger(part.part);
                    if (!part.IsBroken && part.AccumulatedDamage <= 0f && groupStagger <= 0f) continue;
                    GUI.Label(new Rect(20f, y, 720f, 22f),
                        $"{part.name} damage {part.AccumulatedDamage:0}/{part.breakThreshold:0}  " +
                        $"shared {part.part} stagger {groupStagger:0}/{part.staggerThreshold:0}" +
                        $"{(part.IsBroken ? "  BROKEN" : "")}", _label);
                    y += 22f;
                }
            }
        }
    }
}
