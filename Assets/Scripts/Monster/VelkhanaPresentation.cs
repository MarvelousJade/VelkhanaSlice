using System.Collections.Generic;
using UnityEngine;
using VelkhanaSlice.Combat;

namespace VelkhanaSlice.Monster
{
    /// <summary>
    /// Procedural graybox motion for EM124. The extracted game reference confirms that the real
    /// motion is split across em124_00..08 LMT/MBD banks; none of those copyrighted resources are
    /// imported here. This component poses original primitives from VelkhanaBrain's authoritative
    /// state and frame counters, leaving the root, hurtboxes and BodyBlocker untouched.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VelkhanaBrain))]
    public sealed class VelkhanaPresentation : MonoBehaviour
    {
        [Header("Presentation-only hierarchy")]
        public Transform visualRoot;
        public Transform torsoPivot;
        public Transform neckPivot;
        public Transform headPivot;
        public Transform wingLPivot;
        public Transform wingRPivot;
        public Transform frontLegLPivot;
        public Transform frontLegRPivot;
        public Transform rearLegLPivot;
        public Transform rearLegRPivot;
        public Transform tailRoot;
        public Transform tailMiddle;
        public Transform tailTip;
        public Transform breathBeam;
        public Light breathLight;
        public Light phaseLight;

        [Header("Attack mapping")]
        public AttackDefinition tailThrust;
        public AttackDefinition bodyCheck;
        public AttackDefinition iceBeam;
        public AttackDefinition sweepingBreath;
        public AttackDefinition iceSpires;

        [Header("Ambient motion")]
        public float breathingAmount = 0.035f;
        public float wingIdleDegrees = 6f;
        public float tailIdleDegrees = 11f;
        public float repositionStrideDegrees = 24f;

        VelkhanaBrain _brain;
        Transform[] _poseNodes;
        Vector3[] _restPositions;
        Quaternion[] _restRotations;
        Vector3[] _restScales;
        Renderer _beamRenderer;
        readonly List<MaterialState> _materials = new List<MaterialState>();

        sealed class MaterialState
        {
            public Material Material;
            public Color BaseColor;
            public string ColorProperty;
        }

        void Awake()
        {
            _brain = GetComponent<VelkhanaBrain>();
            ResolveHierarchy();
            CaptureRestPose();
            SetupEffects();
        }

        void ResolveHierarchy()
        {
            visualRoot ??= FindNamed(transform, "VisualRoot");
            torsoPivot ??= FindNamed(visualRoot, "TorsoPivot");
            neckPivot ??= FindNamed(visualRoot, "NeckPivot");
            headPivot ??= FindNamed(visualRoot, "HeadPivot");
            wingLPivot ??= FindNamed(visualRoot, "WingLPivot");
            wingRPivot ??= FindNamed(visualRoot, "WingRPivot");
            frontLegLPivot ??= FindNamed(visualRoot, "FrontLegLPivot");
            frontLegRPivot ??= FindNamed(visualRoot, "FrontLegRPivot");
            rearLegLPivot ??= FindNamed(visualRoot, "RearLegLPivot");
            rearLegRPivot ??= FindNamed(visualRoot, "RearLegRPivot");
            tailRoot ??= FindNamed(visualRoot, "TailRoot");
            tailMiddle ??= FindNamed(visualRoot, "TailMiddle");
            tailTip ??= FindNamed(visualRoot, "TailTip");
            breathBeam ??= FindNamed(visualRoot, "BreathBeam");

            Transform breathGlow = FindNamed(visualRoot, "BreathGlow");
            if (breathLight == null && breathGlow != null)
                breathLight = breathGlow.GetComponent<Light>();

            Transform armorGlow = FindNamed(visualRoot, "PhaseGlow");
            if (phaseLight == null && armorGlow != null)
                phaseLight = armorGlow.GetComponent<Light>();
        }

        void CaptureRestPose()
        {
            _poseNodes = new[]
            {
                visualRoot, torsoPivot, neckPivot, headPivot, wingLPivot, wingRPivot,
                frontLegLPivot, frontLegRPivot, rearLegLPivot, rearLegRPivot,
                tailRoot, tailMiddle, tailTip, breathBeam,
            };

            _restPositions = new Vector3[_poseNodes.Length];
            _restRotations = new Quaternion[_poseNodes.Length];
            _restScales = new Vector3[_poseNodes.Length];

            for (int i = 0; i < _poseNodes.Length; i++)
            {
                Transform node = _poseNodes[i];
                if (node == null) continue;
                _restPositions[i] = node.localPosition;
                _restRotations[i] = node.localRotation;
                _restScales[i] = node.localScale;
            }
        }

        void SetupEffects()
        {
            if (breathBeam != null)
            {
                _beamRenderer = breathBeam.GetComponent<Renderer>();
                if (_beamRenderer != null) _beamRenderer.enabled = false;
            }

            if (breathLight != null) breathLight.enabled = false;

            if (visualRoot == null) return;
            foreach (Renderer visualRenderer in visualRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (visualRenderer == _beamRenderer) continue;

                // A private material instance lets the armour phase glow without modifying the
                // shared graybox assets or the hunter's material.
                Material material = visualRenderer.material;
                string property = material.HasProperty("_BaseColor")
                    ? "_BaseColor"
                    : material.HasProperty("_Color") ? "_Color" : null;
                _materials.Add(new MaterialState
                {
                    Material = material,
                    ColorProperty = property,
                    BaseColor = property == null ? Color.white : material.GetColor(property),
                });

                if (material.HasProperty("_EmissionColor"))
                {
                    material.EnableKeyword("_EMISSION");
                    material.SetColor("_EmissionColor", Color.black);
                }
            }
        }

        void LateUpdate()
        {
            if (_brain == null || visualRoot == null) return;

            ResetPose();
            SetBreathEffect(false, 0f);
            ApplyAmbientPose();

            if (_brain.CurrentAttack != null)
                ApplyAttackPose(_brain.CurrentAttack, _brain.AttackFrame);
            else if (_brain.CurrentState == VelkhanaState.Reposition)
                ApplyRepositionPose();

            ApplyPhaseGlow();
        }

        void ResetPose()
        {
            for (int i = 0; i < _poseNodes.Length; i++)
            {
                Transform node = _poseNodes[i];
                if (node == null) continue;
                node.localPosition = _restPositions[i];
                node.localRotation = _restRotations[i];
                node.localScale = _restScales[i];
            }
        }

        void ApplyAmbientPose()
        {
            float time = Time.time;
            float breath = Mathf.Sin(time * 1.8f);
            float wing = Mathf.Sin(time * 1.15f) * wingIdleDegrees;
            float tail = Mathf.Sin(time * 0.9f) * tailIdleDegrees;

            if (torsoPivot != null)
                torsoPivot.localScale = Vector3.Scale(
                    torsoPivot.localScale,
                    new Vector3(1f - breath * breathingAmount * 0.25f,
                        1f + breath * breathingAmount,
                        1f + breath * breathingAmount * 0.2f));

            Rotate(wingLPivot, new Vector3(0f, 0f, wing));
            Rotate(wingRPivot, new Vector3(0f, 0f, -wing));
            Rotate(tailRoot, new Vector3(0f, tail, 0f));
            Rotate(tailMiddle, new Vector3(0f, -tail * 0.65f, 0f));
            Rotate(tailTip, new Vector3(0f, tail * 0.45f, 0f));
        }

        void ApplyRepositionPose()
        {
            float stride = Mathf.Sin(_brain.StateFrame * Time.fixedDeltaTime * 9f);
            float bob = Mathf.Abs(stride) * 0.09f;

            Move(visualRoot, new Vector3(0f, bob, 0f));
            Rotate(torsoPivot, new Vector3(4f, 0f, stride * 3f));
            Rotate(frontLegLPivot, new Vector3(stride * repositionStrideDegrees, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-stride * repositionStrideDegrees, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(-stride * repositionStrideDegrees, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(stride * repositionStrideDegrees, 0f, 0f));
            Rotate(wingLPivot, new Vector3(0f, 18f, -12f));
            Rotate(wingRPivot, new Vector3(0f, -18f, 12f));
        }

        void ApplyAttackPose(AttackDefinition attack, int frame)
        {
            float startup = StartupProgress(attack, frame);
            float active = ActiveProgress(attack, frame);
            float recovery = RecoveryProgress(attack, frame);

            if (attack == tailThrust)
                PoseTailThrust(startup, active, recovery);
            else if (attack == bodyCheck)
                PoseBodyCheck(startup, active, recovery);
            else if (attack == iceBeam)
                PoseIceBeam(startup, active, recovery, false);
            else if (attack == sweepingBreath)
                PoseIceBeam(startup, active, recovery, true);
            else if (attack == iceSpires)
                PoseIceSpires(startup, active, recovery);
        }

        void PoseTailThrust(float startup, float active, float recovery)
        {
            float windup = Smooth(startup) * (1f - active) * (1f - recovery);
            float strike = active > 0f ? 1f : 0f;
            float extension = Mathf.Clamp01(strike * (1f - Smooth(recovery)));

            Rotate(torsoPivot, new Vector3(-7f * windup, 0f, 10f * windup));
            Rotate(neckPivot, new Vector3(8f * windup, -12f * windup, 0f));
            Rotate(tailRoot, new Vector3(-28f * windup, 178f * extension, 62f * windup));
            Rotate(tailMiddle, new Vector3(16f * windup, -112f * windup, -30f * windup));
            Rotate(tailTip, new Vector3(-12f * windup, 82f * windup, 20f * windup));
        }

        void PoseBodyCheck(float startup, float active, float recovery)
        {
            float commitment = Smooth(startup) * (1f - Smooth(recovery));
            float impact = Mathf.Sin(Mathf.Clamp01(active) * Mathf.PI);

            Move(visualRoot, new Vector3(0.4f * impact, -0.18f * commitment, 0f));
            Rotate(torsoPivot, new Vector3(12f * commitment, 0f, -32f * commitment));
            Rotate(neckPivot, new Vector3(-10f * commitment, 18f * commitment, 18f * commitment));
            Rotate(wingLPivot, new Vector3(0f, 52f * commitment, -28f * commitment));
            Rotate(wingRPivot, new Vector3(0f, -52f * commitment, 28f * commitment));
            Rotate(frontLegLPivot, new Vector3(-22f * commitment, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(18f * commitment, 0f, 0f));
        }

        void PoseIceBeam(float startup, float active, float recovery, bool sweeping)
        {
            float charge = Smooth(startup) * (1f - Smooth(recovery));
            float fire = active > 0f && recovery <= 0f ? 1f : 0f;

            Rotate(wingLPivot, new Vector3(-8f * charge, 0f, 38f * charge));
            Rotate(wingRPivot, new Vector3(-8f * charge, 0f, -38f * charge));
            Rotate(neckPivot, new Vector3(-26f * charge + 30f * fire, 0f, 0f));
            Move(headPivot, new Vector3(0f, 0f, -0.45f * charge + 0.7f * fire));

            if (sweeping)
            {
                float sweepAngle = Mathf.Lerp(-62f, 62f, Smooth(active));
                Rotate(neckPivot, new Vector3(0f, sweepAngle * fire, 0f));
            }

            SetBreathEffect(fire > 0f, sweeping ? 0.8f : 1f);
        }

        void PoseIceSpires(float startup, float active, float recovery)
        {
            float raise = Smooth(startup) * (1f - Smooth(recovery));
            float stomp = Mathf.Sin(Mathf.Clamp01(active) * Mathf.PI);

            Move(visualRoot, new Vector3(0f, 0.42f * raise - 0.35f * stomp, 0f));
            Rotate(torsoPivot, new Vector3(-12f * raise + 20f * stomp, 0f, 0f));
            Rotate(wingLPivot, new Vector3(-24f * raise, 0f, 62f * raise));
            Rotate(wingRPivot, new Vector3(-24f * raise, 0f, -62f * raise));
            Rotate(frontLegLPivot, new Vector3(-38f * raise + 52f * stomp, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-38f * raise + 52f * stomp, 0f, 0f));
            Rotate(tailRoot, new Vector3(18f * raise, 0f, 0f));
        }

        void SetBreathEffect(bool active, float width)
        {
            if (_beamRenderer != null) _beamRenderer.enabled = active;
            if (breathLight != null)
            {
                breathLight.enabled = active;
                if (active)
                {
                    breathLight.color = new Color(0.36f, 0.9f, 1f);
                    breathLight.intensity = 3.5f;
                }
            }

            if (!active || breathBeam == null) return;
            Vector3 scale = breathBeam.localScale;
            scale.x *= width;
            scale.y *= width;
            breathBeam.localScale = scale;
        }

        void ApplyPhaseGlow()
        {
            Color glow = StageGlowColor(_brain.stage);
            float strength = StageGlowStrength(_brain.stage);

            for (int i = 0; i < _materials.Count; i++)
            {
                MaterialState state = _materials[i];
                if (state.Material == null) continue;
                if (state.ColorProperty != null)
                    state.Material.SetColor(
                        state.ColorProperty,
                        Color.Lerp(state.BaseColor, glow, strength * 0.2f));
                if (state.Material.HasProperty("_EmissionColor"))
                    state.Material.SetColor("_EmissionColor", glow * strength);
            }

            if (phaseLight == null) return;
            phaseLight.enabled = strength > 0f;
            phaseLight.color = glow;
            phaseLight.intensity = 1.2f + strength * 1.8f;
        }

        public static Color StageGlowColor(ArmorStage stage)
        {
            switch (stage)
            {
                case ArmorStage.IceArmorStage1:
                    return new Color(0.18f, 0.72f, 1f);
                case ArmorStage.IceArmorStage2:
                    return new Color(0.58f, 0.94f, 1f);
                case ArmorStage.Ultimate:
                    return new Color(0.82f, 0.72f, 1f);
                default:
                    return new Color(0.1f, 0.35f, 0.5f);
            }
        }

        public static float StageGlowStrength(ArmorStage stage)
        {
            return stage == ArmorStage.Neutral ? 0f : 0.55f + (int)stage * 0.35f;
        }

        static float StartupProgress(AttackDefinition attack, int frame)
        {
            if (attack.startupFrames <= 0) return 1f;
            return Mathf.Clamp01((float)frame / attack.startupFrames);
        }

        static float ActiveProgress(AttackDefinition attack, int frame)
        {
            if (frame < attack.startupFrames) return 0f;
            if (attack.activeFrames <= 0) return 1f;
            return Mathf.Clamp01((float)(frame - attack.startupFrames + 1) / attack.activeFrames);
        }

        static float RecoveryProgress(AttackDefinition attack, int frame)
        {
            int start = attack.startupFrames + attack.activeFrames;
            if (frame < start) return 0f;
            if (attack.recoveryFrames <= 0) return 1f;
            return Mathf.Clamp01((float)(frame - start + 1) / attack.recoveryFrames);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i].Material != null) Destroy(_materials[i].Material);
        }

        static void Move(Transform node, Vector3 localOffset)
        {
            if (node != null) node.localPosition += localOffset;
        }

        static void Rotate(Transform node, Vector3 localEuler)
        {
            if (node != null) node.localRotation *= Quaternion.Euler(localEuler);
        }

        static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        static Transform FindNamed(Transform root, string objectName)
        {
            if (root == null) return null;
            if (root.name == objectName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindNamed(root.GetChild(i), objectName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
