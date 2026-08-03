using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using VelkhanaSlice.Combat;
using VelkhanaSlice.Hunter;
using VelkhanaSlice.Monster;

namespace VelkhanaSlice.DebugTools
{
    /// <summary>
    /// Playable-build combat volume overlay. F3 toggles exact oriented attack/box-collider
    /// outlines and a capsule outline for the hunter's CharacterController.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVolumeDebug : MonoBehaviour
    {
        public HunterHealth hunterHealth;
        public HunterController hunterController;
        public VelkhanaBrain brain;

        [Header("Runtime toggle")]
        public bool visibleOnStart = true;
        public Key toggleKey = Key.F3;

        [Header("Colours")]
        public Color hurtboxColor = new Color(0.1f, 0.8f, 1f, 1f);
        public Color brokenHurtboxColor = new Color(0.08f, 0.38f, 0.58f, 1f);
        public Color hunterHurtboxColor = new Color(0.2f, 1f, 0.25f, 1f);
        public Color inactiveHitboxColor = new Color(1f, 0.65f, 0.08f, 1f);
        public Color activeHitboxColor = new Color(1f, 0.12f, 0.08f, 1f);
        [Min(0.005f)] public float lineWidth = 0.035f;

        public bool Visible { get; private set; }

        readonly Dictionary<Collider, DebugShape> _hurtboxShapes =
            new Dictionary<Collider, DebugShape>();
        DebugShape _hunterShape;
        DebugShape _hunterAttackShape;
        DebugShape _monsterAttackShape;
        Material _lineMaterial;
        int _refreshCountdown;

        sealed class DebugShape
        {
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();

            public void SetVisible(bool visible)
            {
                for (int i = 0; i < Lines.Count; i++)
                    if (Lines[i] != null) Lines[i].enabled = visible;
            }
        }

        void Awake()
        {
            Visible = visibleOnStart;
            ResolveBindings();
            CreateMaterial();
            RefreshHurtboxes();
        }

        public void Bind(HunterHealth health, HunterController hunter, VelkhanaBrain monster)
        {
            hunterHealth = health;
            hunterController = hunter;
            brain = monster;
            RefreshHurtboxes();
        }

        void ResolveBindings()
        {
            CombatHud hud = GetComponent<CombatHud>();
            if (hud != null)
            {
                hunterHealth ??= hud.health;
                hunterController ??= hud.hunterController;
                brain ??= hud.brain;
            }

            hunterController ??= FindFirstObjectByType<HunterController>();
            hunterHealth ??= hunterController != null
                ? hunterController.GetComponent<HunterHealth>()
                : FindFirstObjectByType<HunterHealth>();
            brain ??= FindFirstObjectByType<VelkhanaBrain>();
        }

        void CreateMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ??
                            Shader.Find("Unlit/Color") ??
                            Shader.Find("Standard");
            if (shader == null) return;

            _lineMaterial = new Material(shader) { name = "CombatVolumeDebugMaterial" };
            if (_lineMaterial.HasProperty("_ZTest"))
                _lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                Visible = !Visible;
        }

        void LateUpdate()
        {
            if (--_refreshCountdown <= 0)
            {
                ResolveBindings();
                RefreshHurtboxes();
                _refreshCountdown = 60;
            }

            DrawHurtboxes();
            DrawHunterCollider();
            DrawAttack(
                hunterController != null ? hunterController.transform : null,
                hunterController != null ? hunterController.CurrentAttack : null,
                hunterController != null
                    ? SimulatedFrameForDisplay(
                        hunterController.AttackFrame,
                        hunterController.LastSimulatedAttackFrame)
                    : 0,
                ref _hunterAttackShape,
                "HunterAttackHitbox");
            DrawAttack(
                brain != null ? brain.transform : null,
                brain != null ? brain.CurrentAttack : null,
                brain != null
                    ? SimulatedFrameForDisplay(brain.AttackFrame, brain.LastSimulatedAttackFrame)
                    : 0,
                ref _monsterAttackShape,
                "MonsterAttackHitbox");
        }

        /// <summary>
        /// LateUpdate runs after the fixed simulation increments AttackFrame. Rendering the last
        /// simulated frame keeps startup/active/recovery colours aligned with the real query.
        /// </summary>
        public static int SimulatedFrameForDisplay(int currentFrame, int lastSimulatedFrame)
        {
            return lastSimulatedFrame >= 0 ? lastSimulatedFrame : currentFrame;
        }

        void RefreshHurtboxes()
        {
            BodyPartHurtbox[] parts = FindObjectsByType<BodyPartHurtbox>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < parts.Length; i++)
            {
                Collider collider = parts[i] != null ? parts[i].GetComponent<Collider>() : null;
                if (collider != null && !_hurtboxShapes.ContainsKey(collider))
                    _hurtboxShapes.Add(collider, CreateShape(collider.name, 1));
            }

            var stale = new List<Collider>();
            foreach (KeyValuePair<Collider, DebugShape> pair in _hurtboxShapes)
                if (pair.Key == null) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) _hurtboxShapes.Remove(stale[i]);
        }

        void DrawHurtboxes()
        {
            foreach (KeyValuePair<Collider, DebugShape> pair in _hurtboxShapes)
            {
                Collider collider = pair.Key;
                DebugShape shape = pair.Value;
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                {
                    shape.SetVisible(false);
                    continue;
                }

                BodyPartHurtbox part = collider.GetComponent<BodyPartHurtbox>();
                Color color = part != null && part.IsBroken ? brokenHurtboxColor : hurtboxColor;
                DrawCollider(collider, shape, color);
            }
        }

        void DrawHunterCollider()
        {
            CharacterController controller = hunterController != null
                ? hunterController.GetComponent<CharacterController>()
                : null;
            if (controller == null)
            {
                _hunterShape?.SetVisible(false);
                return;
            }

            _hunterShape ??= CreateShape("HunterHurtbox", 4);
            DrawCapsule(controller, _hunterShape, hunterHurtboxColor);
        }

        void DrawCollider(Collider collider, DebugShape shape, Color color)
        {
            if (collider is BoxCollider box)
            {
                DrawBox(
                    box.transform,
                    box.center,
                    box.size,
                    shape.Lines[0],
                    color);
                shape.SetVisible(Visible);
                return;
            }

            Bounds bounds = collider.bounds;
            DrawWorldBox(bounds.center, bounds.size, Quaternion.identity, shape.Lines[0], color);
            shape.SetVisible(Visible);
        }

        void DrawAttack(
            Transform attacker,
            AttackDefinition attack,
            int frame,
            ref DebugShape shape,
            string objectName)
        {
            if (!Visible || attacker == null || attack == null || !attack.HasHitbox ||
                frame >= attack.startupFrames + attack.activeFrames)
            {
                shape?.SetVisible(false);
                return;
            }

            shape ??= CreateShape(objectName, 1);
            Color color = attack.IsHitActive(frame) ? activeHitboxColor : inactiveHitboxColor;
            DrawWorldBox(
                AttackHitbox.WorldCenter(attacker, attack),
                attack.hitboxSize,
                attacker.rotation,
                shape.Lines[0],
                color);
            shape.SetVisible(true);
        }

        void DrawBox(
            Transform boxTransform,
            Vector3 localCenter,
            Vector3 localSize,
            LineRenderer line,
            Color color)
        {
            Vector3 half = localSize * 0.5f;
            var corners = new Vector3[8];
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local = localCenter + new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                corners[i] = boxTransform.TransformPoint(local);
            }
            SetBoxLine(line, corners, color);
        }

        void DrawWorldBox(
            Vector3 center,
            Vector3 size,
            Quaternion rotation,
            LineRenderer line,
            Color color)
        {
            Vector3 half = size * 0.5f;
            var corners = new Vector3[8];
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 local = new Vector3(
                    (i & 1) == 0 ? -half.x : half.x,
                    (i & 2) == 0 ? -half.y : half.y,
                    (i & 4) == 0 ? -half.z : half.z);
                corners[i] = center + rotation * local;
            }
            SetBoxLine(line, corners, color);
        }

        static void SetBoxLine(LineRenderer line, Vector3[] corners, Color color)
        {
            // One continuous edge walk covers all twelve edges without diagonal connectors.
            int[] walk = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
            line.positionCount = walk.Length;
            for (int i = 0; i < walk.Length; i++) line.SetPosition(i, corners[walk[i]]);
            line.startColor = line.endColor = color;
        }

        void DrawCapsule(CharacterController capsule, DebugShape shape, Color color)
        {
            float radius = capsule.radius;
            float cylinderHalf = Mathf.Max(0f, capsule.height * 0.5f - radius);
            Vector3 center = capsule.center;
            const int segments = 24;

            DrawRing(capsule.transform, center + Vector3.up * cylinderHalf,
                radius, segments, shape.Lines[0], color);
            DrawRing(capsule.transform, center - Vector3.up * cylinderHalf,
                radius, segments, shape.Lines[1], color);
            DrawCapsuleMeridian(capsule.transform, center, radius, cylinderHalf,
                Vector3.right, segments, shape.Lines[2], color);
            DrawCapsuleMeridian(capsule.transform, center, radius, cylinderHalf,
                Vector3.forward, segments, shape.Lines[3], color);
            shape.SetVisible(Visible);
        }

        static void DrawRing(
            Transform transform,
            Vector3 localCenter,
            float radius,
            int segments,
            LineRenderer line,
            Color color)
        {
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 local = localCenter +
                                new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                line.SetPosition(i, transform.TransformPoint(local));
            }
            line.startColor = line.endColor = color;
        }

        static void DrawCapsuleMeridian(
            Transform transform,
            Vector3 center,
            float radius,
            float cylinderHalf,
            Vector3 radialAxis,
            int segments,
            LineRenderer line,
            Color color)
        {
            int halfSegments = segments / 2;
            line.positionCount = segments + 3;
            int index = 0;

            for (int i = 0; i <= halfSegments; i++)
            {
                float angle = i * Mathf.PI / halfSegments;
                Vector3 local = center + Vector3.up * cylinderHalf +
                                radialAxis * (Mathf.Cos(angle) * radius) +
                                Vector3.up * (Mathf.Sin(angle) * radius);
                line.SetPosition(index++, transform.TransformPoint(local));
            }

            for (int i = 0; i <= halfSegments; i++)
            {
                float angle = Mathf.PI + i * Mathf.PI / halfSegments;
                Vector3 local = center - Vector3.up * cylinderHalf +
                                radialAxis * (Mathf.Cos(angle) * radius) +
                                Vector3.up * (Mathf.Sin(angle) * radius);
                line.SetPosition(index++, transform.TransformPoint(local));
            }

            line.SetPosition(index, line.GetPosition(0));
            line.startColor = line.endColor = color;
        }

        DebugShape CreateShape(string shapeName, int lineCount)
        {
            var shape = new DebugShape();
            for (int i = 0; i < lineCount; i++)
            {
                var lineObject = new GameObject($"{shapeName}_{i}");
                lineObject.transform.SetParent(transform, false);
                var line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = false;
                line.widthMultiplier = lineWidth;
                line.numCapVertices = 1;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                if (_lineMaterial != null) line.sharedMaterial = _lineMaterial;
                shape.Lines.Add(line);
            }
            return shape;
        }

        void OnDestroy()
        {
            if (_lineMaterial != null) Destroy(_lineMaterial);
        }
    }
}
