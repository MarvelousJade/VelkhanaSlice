using UnityEngine;

namespace VelkhanaSlice.Combat
{
    /// <summary>
    /// Projects the attack's hitbox onto the ground: amber while it winds up, brightening as the
    /// active frames approach, then a hard flash on the frames that actually deal damage.
    /// The plan asks for ground projections because a top-down camera hides everything else.
    /// </summary>
    public class AttackTelegraph : MonoBehaviour
    {
        [Tooltip("Material to instance for the projection. Standard shader with emission works.")]
        public Material material;

        public Color windUpColor = new Color(0.42f, 0.28f, 0.06f);
        public Color readyColor = new Color(0.80f, 0.52f, 0.10f);
        public Color activeColor = new Color(1f, 0.22f, 0.15f);

        [Tooltip("Emission multiplier. The wind-up should read as a warning, not blind the player.")]
        [Range(0f, 1f)] public float emission = 0.3f;

        [Tooltip("Height above the ground plane, enough to avoid z-fighting.")]
        public float groundHeight = 0.04f;

        IAttacker _attacker;
        Transform _projection;
        Renderer _renderer;
        Material _instance;

        void Awake()
        {
            _attacker = GetComponent<IAttacker>();
            if (_attacker == null) { enabled = false; return; }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "AttackProjection";
            Destroy(go.GetComponent<Collider>());

            _projection = go.transform;
            _renderer = go.GetComponent<Renderer>();

            if (material != null)
            {
                _instance = new Material(material);
                _renderer.sharedMaterial = _instance;
            }

            Hide();
        }

        void LateUpdate()
        {
            AttackDefinition attack = _attacker.CurrentAttack;
            if (attack == null || !attack.HasHitbox) { Hide(); return; }

            int frame = _attacker.AttackFrame;
            bool active = attack.IsHitActive(frame);

            // Recovery has no hitbox left, so nothing should be drawn.
            if (!active && frame >= attack.startupFrames) { Hide(); return; }

            Vector3 centre = AttackHitbox.WorldCenter(transform, attack);
            centre.y = groundHeight;

            _projection.position = centre;
            _projection.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            _projection.localScale = new Vector3(attack.hitboxSize.x, 0.02f, attack.hitboxSize.z);

            Color colour = active
                ? activeColor
                : Color.Lerp(windUpColor, readyColor,
                    attack.startupFrames <= 0 ? 1f : (float)frame / attack.startupFrames);

            Paint(colour);
            _renderer.enabled = true;
        }

        void Paint(Color colour)
        {
            if (_instance == null) return;
            _instance.color = colour;
            _instance.SetColor("_EmissionColor", colour * emission);
        }

        void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }
    }
}
