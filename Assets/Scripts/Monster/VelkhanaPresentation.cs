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
        public readonly struct AttackPosePhase
        {
            public readonly float OverallProgress;
            public readonly float StartupProgress;
            public readonly float ActiveProgress;
            public readonly float RecoveryProgress;
            public readonly float Anticipation;
            public readonly float Impact;
            public readonly float FollowThrough;
            public readonly float RecoveryEase;

            public AttackPosePhase(
                float overallProgress,
                float startupProgress,
                float activeProgress,
                float recoveryProgress,
                float anticipation,
                float impact,
                float followThrough,
                float recoveryEase)
            {
                OverallProgress = overallProgress;
                StartupProgress = startupProgress;
                ActiveProgress = activeProgress;
                RecoveryProgress = recoveryProgress;
                Anticipation = anticipation;
                Impact = impact;
                FollowThrough = followThrough;
                RecoveryEase = recoveryEase;
            }
        }

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
        public AttackDefinition adjustBite;
        public AttackDefinition rush;
        public AttackDefinition rush2;
        public AttackDefinition backStepPierce;
        public AttackDefinition tailThrust;
        public AttackDefinition tailSwing;
        public AttackDefinition straightBreath;
        public AttackDefinition sweep90Breath;
        public AttackDefinition sweep180Breath;
        public AttackDefinition iceWave;
        public AttackDefinition areaBreath;
        public AttackDefinition freezeBreath;
        public AttackDefinition iceSpires;
        public AttackDefinition verticalBreathFly;
        public AttackDefinition verticalBreathFlyToGround;
        public AttackDefinition iceWaveStartFly;
        public AttackDefinition flyTailStingToGround;

        [Header("Ambient motion")]
        public float breathingAmount = 0.035f;
        public float wingIdleDegrees = 6f;
        public float tailIdleDegrees = 11f;
        public float repositionStrideDegrees = 24f;
        public float airborneHeight = 3.2f;

        VelkhanaBrain _brain;
        Transform[] _poseNodes;
        Vector3[] _restPositions;
        Quaternion[] _restRotations;
        Vector3[] _restScales;
        Renderer _beamRenderer;
        Vector3 _breathBeamRestScale;
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

            if (breathBeam != null) _breathBeamRestScale = breathBeam.localScale;
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
            SetBreathEffect(false, 1f, 1f, 0f);
            ApplyAmbientPose();
            ApplyContextPose();

            if (_brain.CurrentAttack != null)
                ApplyAttackPose(
                    _brain.CurrentAttack,
                    SelectPresentationFrame(
                        _brain.AttackFrame,
                        _brain.LastSimulatedAttackFrame));
            else if (_brain.CurrentState == VelkhanaState.Reposition)
                ApplyRepositionPose();
            else if (_brain.CurrentState == VelkhanaState.RageTransition)
                ApplyRagePose();
            else if (_brain.CurrentState == VelkhanaState.Toppled)
                ApplyTopplePose();

            float attentionWeight = _brain.CurrentAttack != null
                ? 0.18f
                : _brain.IsAirborne ? 0.35f : 0.85f;
            ApplyAttentionPose(attentionWeight);
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
            float neck = Mathf.Sin(time * 1.05f + 0.6f);

            Move(visualRoot, new Vector3(0f, Mathf.Abs(breath) * 0.035f, 0f));

            if (torsoPivot != null)
                torsoPivot.localScale = Vector3.Scale(
                    torsoPivot.localScale,
                    new Vector3(1f - breath * breathingAmount * 0.25f,
                        1f + breath * breathingAmount,
                        1f + breath * breathingAmount * 0.2f));

            Rotate(torsoPivot, new Vector3(breath * 1.5f, neck * 1.5f, 0f));
            Rotate(neckPivot, new Vector3(-breath * 3.5f, neck * 2.5f, 0f));
            Rotate(headPivot, new Vector3(breath * 4f, -neck * 2f, 0f));
            Rotate(wingLPivot, new Vector3(0f, 0f, wing));
            Rotate(wingRPivot, new Vector3(0f, 0f, -wing));
            Rotate(frontLegLPivot, new Vector3(-breath * 2f, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-breath * 2f, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(breath * 1.5f, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(breath * 1.5f, 0f, 0f));
            Rotate(tailRoot, new Vector3(0f, tail, 0f));
            Rotate(tailMiddle, new Vector3(0f, -tail * 0.7f, 0f));
            Rotate(tailTip, new Vector3(0f, tail * 0.5f, 0f));
        }

        void ApplyRepositionPose()
        {
            float phase = _brain.StateFrame * Time.fixedDeltaTime * 8.5f;
            float stride = Mathf.Sin(phase);
            float counter = Mathf.Sin(phase + Mathf.PI);
            float bob = Mathf.Abs(stride) * 0.11f;

            Move(visualRoot, new Vector3(0f, bob, 0f));
            Rotate(torsoPivot, new Vector3(5f, 0f, stride * 4f));
            Rotate(neckPivot, new Vector3(-4f, -stride * 4f, 0f));
            Rotate(headPivot, new Vector3(3f, stride * 2f, 0f));
            Rotate(frontLegLPivot, new Vector3(stride * repositionStrideDegrees, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(counter * repositionStrideDegrees, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(counter * repositionStrideDegrees * 0.85f, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(stride * repositionStrideDegrees * 0.85f, 0f, 0f));
            Rotate(wingLPivot, new Vector3(-6f, 14f, -14f - stride * 4f));
            Rotate(wingRPivot, new Vector3(-6f, -14f, 14f + stride * 4f));
            Rotate(tailRoot, new Vector3(-4f, -stride * 16f, 0f));
            Rotate(tailMiddle, new Vector3(0f, stride * 12f, 0f));
            Rotate(tailTip, new Vector3(0f, -stride * 8f, 0f));
        }

        void ApplyContextPose()
        {
            if (_brain.CurrentState == VelkhanaState.Takeoff)
            {
                float t = Smooth(Mathf.Clamp01((float)_brain.StateFrame / Mathf.Max(1, _brain.takeoffFrames)));
                float crouch = Mathf.Sin(Mathf.Clamp01(t * 1.65f) * Mathf.PI);
                float lift = airborneHeight * t;

                Move(visualRoot, new Vector3(0f, lift - crouch * 0.24f, 0f));
                Rotate(torsoPivot, new Vector3(12f * crouch - 10f * t, 0f, 0f));
                Rotate(neckPivot, new Vector3(-8f * crouch, 0f, 0f));
                Rotate(headPivot, new Vector3(10f * crouch, 0f, 0f));
                Rotate(wingLPivot, new Vector3(-18f * crouch - 12f * t, 8f * crouch, Mathf.Lerp(22f, 76f, t)));
                Rotate(wingRPivot, new Vector3(-18f * crouch - 12f * t, -8f * crouch, -Mathf.Lerp(22f, 76f, t)));
                Rotate(frontLegLPivot, new Vector3(-18f * crouch - 18f * t, 0f, 0f));
                Rotate(frontLegRPivot, new Vector3(-18f * crouch - 18f * t, 0f, 0f));
                Rotate(rearLegLPivot, new Vector3(18f * crouch + 22f * t, 0f, 0f));
                Rotate(rearLegRPivot, new Vector3(18f * crouch + 22f * t, 0f, 0f));
                Rotate(tailRoot, new Vector3(-14f * crouch, 0f, 0f));
                return;
            }

            if (_brain.CurrentState == VelkhanaState.Landing)
            {
                float t = Smooth(Mathf.Clamp01((float)_brain.StateFrame / Mathf.Max(1, _brain.landingFrames)));
                float flare = 1f - t;
                float impact = Mathf.Sin(Mathf.Clamp01(t * 1.5f) * Mathf.PI);
                float lift = airborneHeight * flare;

                Move(visualRoot, new Vector3(0f, lift - impact * 0.28f, 0f));
                Rotate(torsoPivot, new Vector3(-10f * flare + 16f * impact, 0f, 0f));
                Rotate(neckPivot, new Vector3(8f * flare - 10f * impact, 0f, 0f));
                Rotate(headPivot, new Vector3(-8f * flare + 10f * impact, 0f, 0f));
                Rotate(wingLPivot, new Vector3(-12f + 10f * impact, 0f, 74f * flare + 18f * impact));
                Rotate(wingRPivot, new Vector3(-12f + 10f * impact, 0f, -74f * flare - 18f * impact));
                Rotate(frontLegLPivot, new Vector3(-30f * flare + 34f * impact, 0f, 0f));
                Rotate(frontLegRPivot, new Vector3(-30f * flare + 34f * impact, 0f, 0f));
                Rotate(rearLegLPivot, new Vector3(22f * flare - 26f * impact, 0f, 0f));
                Rotate(rearLegRPivot, new Vector3(22f * flare - 26f * impact, 0f, 0f));
                Rotate(tailRoot, new Vector3(-12f * flare + 16f * impact, 0f, 0f));
                return;
            }

            if (!_brain.IsAirborne) return;

            float glide = Mathf.Sin(Time.time * 2.2f);
            float flap = Mathf.Sin(Time.time * 8.5f);
            Move(visualRoot, new Vector3(0f, airborneHeight + glide * 0.14f, 0f));
            Rotate(torsoPivot, new Vector3(-10f + flap * 2f, glide * 3f, 0f));
            Rotate(neckPivot, new Vector3(6f, -glide * 4f, 0f));
            Rotate(headPivot, new Vector3(-4f, glide * 2f, 0f));
            Rotate(wingLPivot, new Vector3(-16f - flap * 10f, 0f, 70f + flap * 24f));
            Rotate(wingRPivot, new Vector3(-16f - flap * 10f, 0f, -70f - flap * 24f));
            Rotate(frontLegLPivot, new Vector3(-32f, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-32f, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(24f, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(24f, 0f, 0f));
            Rotate(tailRoot, new Vector3(-10f, glide * 18f, 0f));
            Rotate(tailMiddle, new Vector3(0f, -glide * 12f, 0f));
        }

        void ApplyRagePose()
        {
            float duration = Mathf.Max(1, _brain.rageTransitionFrames);
            float t = Mathf.Clamp01(_brain.StateFrame / duration);
            float roar = Mathf.Sin(t * Mathf.PI);
            float shake = Mathf.Sin(_brain.StateFrame * 0.9f) * roar;

            Move(visualRoot, new Vector3(shake * 0.08f, roar * 0.28f, 0f));
            Rotate(torsoPivot, new Vector3(-18f * roar, 0f, shake * 3f));
            Rotate(neckPivot, new Vector3(-38f * roar, shake * 4f, 0f));
            Rotate(headPivot, new Vector3(22f * roar, 0f, 0f));
            Rotate(wingLPivot, new Vector3(-16f * roar, 0f, 82f * roar));
            Rotate(wingRPivot, new Vector3(-16f * roar, 0f, -82f * roar));
            Rotate(frontLegLPivot, new Vector3(-18f * roar, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-18f * roar, 0f, 0f));
            Rotate(tailRoot, new Vector3(18f * roar, shake * 8f, 0f));
            Rotate(tailMiddle, new Vector3(0f, -24f * roar, 0f));
        }

        void ApplyTopplePose()
        {
            float fallFrames = Mathf.Min(22f, Mathf.Max(1f, _brain.ActiveToppleFrames * 0.15f));
            float fall = Smooth(Mathf.Clamp01(_brain.StateFrame / fallFrames));
            float settle = Mathf.Sin(_brain.StateFrame * 0.55f) *
                           Mathf.Clamp01(1f - _brain.StateFrame / 40f);

            Move(visualRoot, new Vector3(0.55f * fall, -0.72f * fall, 0f));
            Rotate(visualRoot, new Vector3(0f, 0f, 76f * fall + settle * 2f));
            Rotate(torsoPivot, new Vector3(12f * fall, 0f, 0f));
            Rotate(neckPivot, new Vector3(28f * fall, 0f, -10f * fall));
            Rotate(headPivot, new Vector3(-24f * fall, 0f, 0f));
            Rotate(wingLPivot, new Vector3(18f * fall, 26f * fall, -68f * fall));
            Rotate(wingRPivot, new Vector3(18f * fall, -26f * fall, 68f * fall));
            Rotate(frontLegLPivot, new Vector3(-58f * fall, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-42f * fall, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(48f * fall, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(36f * fall, 0f, 0f));
            Rotate(tailRoot, new Vector3(-18f * fall, 24f * fall, 0f));
            Rotate(tailMiddle, new Vector3(0f, -34f * fall, 0f));
            Rotate(tailTip, new Vector3(0f, 22f * fall, 0f));
        }

        void ApplyAttentionPose(float weight)
        {
            if (weight <= 0f || _brain.hunter == null || neckPivot == null) return;

            Transform lookRoot = headPivot != null ? headPivot : neckPivot;
            Vector3 toHunter = _brain.hunter.position - lookRoot.position;
            Vector3 flat = new Vector3(toHunter.x, 0f, toHunter.z);
            float yaw = 0f;
            if (flat.sqrMagnitude > 0.001f)
                yaw = Mathf.Clamp(Vector3.SignedAngle(transform.forward, flat.normalized, Vector3.up), -32f, 32f);

            float pitch = 0f;
            float flatLength = flat.magnitude;
            if (toHunter.sqrMagnitude > 0.001f)
                pitch = Mathf.Clamp(Mathf.Atan2(toHunter.y, Mathf.Max(0.001f, flatLength)) * Mathf.Rad2Deg, -18f, 18f);

            Rotate(neckPivot, new Vector3(-pitch * 0.35f * weight, yaw * 0.65f * weight, 0f));
            Rotate(headPivot, new Vector3(-pitch * 0.55f * weight, yaw * 0.45f * weight, 0f));
        }

        void ApplyAttackPose(AttackDefinition attack, int frame)
        {
            AttackPosePhase phase = EvaluateAttackPhase(attack, frame);

            if (attack == adjustBite)
                PoseBite(phase);
            else if (attack == rush || attack == rush2)
                PoseRush(phase, false);
            else if (attack == backStepPierce)
                PoseRush(phase, true);
            else if (attack == tailThrust || attack == flyTailStingToGround)
                PoseTailThrust(phase, attack == flyTailStingToGround);
            else if (attack == tailSwing)
                PoseTailSwing(phase);
            else if (attack == straightBreath || attack == freezeBreath)
                PoseIceBeam(phase, false);
            else if (attack == sweep90Breath || attack == sweep180Breath)
                PoseIceBeam(phase, true);
            else if (attack == iceWave || attack == areaBreath || attack == iceSpires ||
                     attack == iceWaveStartFly)
                PoseIceSpires(phase);
            else if (attack == verticalBreathFly || attack == verticalBreathFlyToGround)
                PoseVerticalBreath(phase, attack == verticalBreathFlyToGround);
        }

        void PoseBite(AttackPosePhase phase)
        {
            Move(visualRoot, new Vector3(0f, -0.08f * phase.Anticipation, 0.12f * phase.Impact));
            Rotate(torsoPivot, new Vector3(8f * phase.Anticipation - 6f * phase.FollowThrough, 0f, 0f));
            Rotate(neckPivot, new Vector3(-36f * phase.Anticipation + 60f * phase.Impact - 18f * phase.FollowThrough, 10f * phase.Anticipation, 0f));
            Rotate(headPivot, new Vector3(22f * phase.Anticipation - 46f * phase.Impact + 20f * phase.FollowThrough, 0f, 0f));
            Move(headPivot, new Vector3(0f, -0.12f * phase.Anticipation, 0.62f * phase.Impact - 0.14f * phase.FollowThrough));
            Rotate(wingLPivot, new Vector3(2f * phase.Anticipation, 12f * phase.Anticipation, -18f * phase.Anticipation));
            Rotate(wingRPivot, new Vector3(2f * phase.Anticipation, -12f * phase.Anticipation, 18f * phase.Anticipation));
            Rotate(frontLegLPivot, new Vector3(-18f * phase.Anticipation + 14f * phase.Impact, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-18f * phase.Anticipation + 14f * phase.Impact, 0f, 0f));
            Rotate(tailRoot, new Vector3(-6f * phase.Impact, -18f * phase.Impact, 0f));
            Rotate(tailMiddle, new Vector3(0f, 12f * phase.Impact, 0f));
        }

        void PoseRush(AttackPosePhase phase, bool backwards)
        {
            float direction = backwards ? -1f : 1f;
            float stride = Mathf.Sin(phase.OverallProgress * Mathf.PI * 4f);
            float drive = phase.Impact + phase.FollowThrough * 0.65f;

            Move(visualRoot, new Vector3(0f, -0.14f * phase.Anticipation + 0.05f * drive, 0.3f * drive * direction));
            Rotate(torsoPivot, new Vector3(
                16f * phase.Anticipation * direction -
                10f * RecoverySettle(phase.RecoveryEase) * direction,
                0f,
                stride * 2.5f));
            Rotate(neckPivot, new Vector3(-22f * phase.Anticipation * direction + 8f * drive, 0f, 0f));
            Rotate(headPivot, new Vector3(10f * drive, 0f, 0f));
            Rotate(wingLPivot, new Vector3(-4f * phase.Anticipation, 38f * phase.Anticipation, -20f * phase.Anticipation - stride * 5f));
            Rotate(wingRPivot, new Vector3(-4f * phase.Anticipation, -38f * phase.Anticipation, 20f * phase.Anticipation + stride * 5f));
            Rotate(frontLegLPivot, new Vector3(-18f * phase.Anticipation + stride * 38f, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-18f * phase.Anticipation - stride * 38f, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(14f * phase.Anticipation - stride * 28f, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(14f * phase.Anticipation + stride * 28f, 0f, 0f));
            Rotate(tailRoot, new Vector3(-4f * drive, -stride * 10f, 0f));
            Rotate(tailMiddle, new Vector3(0f, stride * 8f, 0f));
        }

        void PoseTailThrust(AttackPosePhase phase, bool descending)
        {
            float strike = phase.Impact + 0.35f * phase.FollowThrough;
            float descent = descending ? RecoverySettle(phase.RecoveryEase) : 0f;

            Move(visualRoot, new Vector3(0f, 0.04f * phase.Anticipation - 0.08f * strike, 0f));
            Rotate(torsoPivot, new Vector3(-8f * phase.Anticipation + 10f * strike, 0f, 12f * phase.Anticipation));
            Rotate(neckPivot, new Vector3(8f * phase.Anticipation, -14f * phase.Anticipation, 0f));
            Rotate(wingLPivot, new Vector3(-4f * descent, 18f * phase.Anticipation, -12f * phase.Anticipation));
            Rotate(wingRPivot, new Vector3(-4f * descent, -18f * phase.Anticipation, 12f * phase.Anticipation));
            Rotate(tailRoot, new Vector3(-30f * phase.Anticipation - 16f * descent, 138f * strike, 62f * phase.Anticipation));
            Rotate(tailMiddle, new Vector3(18f * phase.Anticipation + 20f * descent, -122f * phase.Anticipation + 18f * strike, -32f * phase.Anticipation));
            Rotate(tailTip, new Vector3(-12f * phase.Anticipation - 16f * strike, 104f * strike, 18f * phase.Anticipation));
            Rotate(frontLegLPivot, new Vector3(12f * phase.Anticipation, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(12f * phase.Anticipation, 0f, 0f));
        }

        void PoseTailSwing(AttackPosePhase phase)
        {
            float sweep = phase.ActiveProgress > 0f
                ? Mathf.Lerp(-1f, 1f, Smooth(phase.ActiveProgress))
                : -1f;
            float whip = Mathf.Max(phase.Impact, phase.FollowThrough);

            Rotate(torsoPivot, new Vector3(0f, -26f * phase.Anticipation + 42f * sweep * whip, 10f * phase.Anticipation));
            Rotate(neckPivot, new Vector3(0f, 20f * phase.Anticipation - 30f * sweep * whip, 0f));
            Rotate(wingLPivot, new Vector3(-4f * phase.Anticipation, 10f * phase.Anticipation, -18f * phase.Anticipation));
            Rotate(wingRPivot, new Vector3(-4f * phase.Anticipation, -10f * phase.Anticipation, 18f * phase.Anticipation));
            Rotate(frontLegLPivot, new Vector3(-18f * phase.Anticipation, 0f, -12f * sweep * whip));
            Rotate(frontLegRPivot, new Vector3(-18f * phase.Anticipation, 0f, 12f * sweep * whip));
            Rotate(rearLegLPivot, new Vector3(12f * phase.Anticipation, 0f, 10f * sweep * whip));
            Rotate(rearLegRPivot, new Vector3(12f * phase.Anticipation, 0f, -10f * sweep * whip));
            Rotate(tailRoot, new Vector3(0f, 92f * phase.Anticipation + 148f * sweep * whip, 34f * phase.Anticipation));
            Rotate(tailMiddle, new Vector3(0f, -118f * phase.Anticipation + 116f * sweep * whip, -18f * phase.Anticipation));
            Rotate(tailTip, new Vector3(0f, 82f * phase.Anticipation + 88f * sweep * whip, 0f));
        }

        void PoseIceBeam(AttackPosePhase phase, bool sweeping)
        {
            float beam = Mathf.Max(phase.Impact, phase.FollowThrough * (1f - phase.RecoveryEase * 0.7f));
            float sweepAngle = sweeping ? Mathf.Lerp(-60f, 60f, Smooth(phase.ActiveProgress)) * beam : 0f;
            float headRecoil = beam * Mathf.Sin(phase.ActiveProgress * Mathf.PI * (sweeping ? 1f : 2f));

            Move(visualRoot, new Vector3(0f, 0.08f * phase.Anticipation, 0f));
            Rotate(torsoPivot, new Vector3(-10f * phase.Anticipation + 6f * beam, sweeping ? sweepAngle * 0.08f : 0f, 0f));
            Rotate(neckPivot, new Vector3(-26f * phase.Anticipation + 18f * beam, sweepAngle, 0f));
            Rotate(headPivot, new Vector3(12f * phase.Anticipation - 14f * beam + 8f * headRecoil, sweepAngle * 0.45f, 0f));
            Move(headPivot, new Vector3(0f, -0.06f * phase.Anticipation, -0.3f * phase.Anticipation + 0.74f * beam));
            Rotate(wingLPivot, new Vector3(
                -10f * phase.Anticipation,
                0f,
                30f * beam + 18f * phase.Anticipation));
            Rotate(wingRPivot, new Vector3(
                -10f * phase.Anticipation,
                0f,
                -30f * beam - 18f * phase.Anticipation));
            Rotate(frontLegLPivot, new Vector3(-18f * phase.Anticipation + 18f * beam, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-18f * phase.Anticipation + 18f * beam, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(8f * phase.Anticipation, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(8f * phase.Anticipation, 0f, 0f));
            Rotate(tailRoot, new Vector3(-8f * phase.Anticipation, -sweepAngle * 0.4f, 0f));
            Rotate(tailMiddle, new Vector3(0f, sweepAngle * 0.3f, 0f));
            Rotate(tailTip, new Vector3(0f, -sweepAngle * 0.2f, 0f));
            SetBreathEffect(beam > 0.01f, sweeping ? 0.95f : 1.05f, sweeping ? 1.15f : 1.35f, beam);
        }

        void PoseIceSpires(AttackPosePhase phase)
        {
            float wingBrace = Mathf.Max(phase.Anticipation, phase.FollowThrough);
            Move(visualRoot, new Vector3(0f, 0.34f * phase.Anticipation - 0.28f * phase.Impact, 0f));
            Rotate(torsoPivot, new Vector3(-14f * phase.Anticipation + 22f * phase.Impact - 8f * phase.FollowThrough, 0f, 0f));
            Rotate(neckPivot, new Vector3(-12f * phase.Anticipation + 18f * phase.Impact, 0f, 0f));
            Rotate(headPivot, new Vector3(10f * phase.Anticipation - 12f * phase.Impact, 0f, 0f));
            Rotate(wingLPivot, new Vector3(
                -22f * phase.Anticipation,
                0f,
                34f * wingBrace + 26f * phase.Anticipation));
            Rotate(wingRPivot, new Vector3(
                -22f * phase.Anticipation,
                0f,
                -34f * wingBrace - 26f * phase.Anticipation));
            Rotate(frontLegLPivot, new Vector3(-36f * phase.Anticipation + 54f * phase.Impact, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-36f * phase.Anticipation + 54f * phase.Impact, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(18f * phase.Anticipation - 12f * phase.Impact, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(18f * phase.Anticipation - 12f * phase.Impact, 0f, 0f));
            Rotate(tailRoot, new Vector3(18f * phase.Anticipation - 12f * phase.Impact, 0f, 0f));
            Rotate(tailMiddle, new Vector3(0f, -18f * phase.Anticipation, 0f));
        }

        void PoseVerticalBreath(AttackPosePhase phase, bool toGround)
        {
            float beam = Mathf.Max(phase.Impact, phase.FollowThrough * (1f - phase.RecoveryEase * 0.65f));
            float descent = toGround ? RecoverySettle(phase.RecoveryEase) : 0f;
            float flightBrace = Mathf.Max(phase.Anticipation, beam);

            Move(visualRoot, new Vector3(0f, 0.12f * phase.Anticipation - 0.18f * descent, 0f));
            Rotate(torsoPivot, new Vector3(-12f * phase.Anticipation + 8f * beam + 18f * descent, 0f, 0f));
            Rotate(neckPivot, new Vector3(56f * phase.Anticipation - 16f * beam, 0f, 0f));
            Rotate(headPivot, new Vector3(30f * phase.Anticipation - 24f * beam, 0f, 0f));
            Rotate(wingLPivot, new Vector3(
                -16f * flightBrace - 8f * phase.Anticipation + 10f * beam,
                0f,
                58f * flightBrace + 18f * phase.Anticipation - 14f * descent));
            Rotate(wingRPivot, new Vector3(
                -16f * flightBrace - 8f * phase.Anticipation + 10f * beam,
                0f,
                -58f * flightBrace - 18f * phase.Anticipation + 14f * descent));
            Move(headPivot, new Vector3(0f, -0.38f * phase.Anticipation, 0f));
            Rotate(frontLegLPivot, new Vector3(-26f * phase.Anticipation + 30f * descent, 0f, 0f));
            Rotate(frontLegRPivot, new Vector3(-26f * phase.Anticipation + 30f * descent, 0f, 0f));
            Rotate(rearLegLPivot, new Vector3(18f * phase.Anticipation - 22f * descent, 0f, 0f));
            Rotate(rearLegRPivot, new Vector3(18f * phase.Anticipation - 22f * descent, 0f, 0f));
            Rotate(tailRoot, new Vector3(-10f * phase.Anticipation + 16f * descent, 0f, 0f));
            Rotate(tailMiddle, new Vector3(0f, -12f * phase.Anticipation, 0f));
            SetBreathEffect(beam > 0.01f, 1.18f, 1.55f, beam * 1.1f);
        }

        void SetBreathEffect(bool active, float width, float length, float intensity)
        {
            if (_beamRenderer != null) _beamRenderer.enabled = active;
            if (breathLight != null)
            {
                breathLight.enabled = active;
                if (active)
                {
                    breathLight.color = new Color(0.36f, 0.9f, 1f);
                    breathLight.intensity = 2.2f + 2.4f * intensity;
                    breathLight.range = 3.6f + 1.8f * width;
                }
            }

            if (!active || breathBeam == null) return;
            Vector3 scale = _breathBeamRestScale;
            scale.x *= width;
            scale.y *= width;
            scale.z *= length;
            breathBeam.localScale = scale;
        }

        void ApplyPhaseGlow()
        {
            Color glow = StageGlowColor(_brain.stage);
            float strength = StageGlowStrength(_brain.stage);
            if (_brain.enraged)
            {
                float ragePulse = 0.85f + Mathf.Sin(Time.time * 7f) * 0.15f;
                glow = Color.Lerp(glow, new Color(1f, 0.12f, 0.04f), 0.48f);
                strength = Mathf.Max(strength, 0.9f * ragePulse);
            }

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

        public static AttackPosePhase EvaluateAttackPhase(AttackDefinition attack, int frame)
        {
            if (attack == null) return default;
            return EvaluateAttackPhase(
                attack.startupFrames,
                attack.activeFrames,
                attack.recoveryFrames,
                frame);
        }

        public static AttackPosePhase EvaluateAttackPhase(
            int startupFrames,
            int activeFrames,
            int recoveryFrames,
            int frame)
        {
            startupFrames = Mathf.Max(0, startupFrames);
            activeFrames = Mathf.Max(0, activeFrames);
            recoveryFrames = Mathf.Max(0, recoveryFrames);

            int totalFrames = Mathf.Max(1, startupFrames + activeFrames + recoveryFrames);
            float overall = Mathf.Clamp01((frame + 1f) / totalFrames);
            float startup = SegmentProgress(frame, startupFrames);
            float active = activeFrames > 0
                ? SegmentProgress(frame - startupFrames, activeFrames)
                : 0f;
            float recovery = SegmentProgress(
                frame - startupFrames - activeFrames,
                recoveryFrames);
            float recoveryEase = Smooth(recovery);

            float anticipation = startupFrames > 0
                ? Smooth(startup) *
                  (1f - 0.68f * Smooth(active)) *
                  (1f - recoveryEase)
                : 0f;

            int activeFrame = frame - startupFrames;
            float impact = 0f;
            if (activeFrame >= 0 && activeFrame < activeFrames)
            {
                impact = activeFrames == 1
                    ? 1f
                    : Mathf.Max(
                        0f,
                        Mathf.Sin(
                            Mathf.Clamp01((activeFrame + 1f) / activeFrames) * Mathf.PI));
            }

            float followSource = activeFrames > 0
                ? Smooth(active)
                : Smooth(startup);
            float followThrough = followSource * (1f - recoveryEase);

            return new AttackPosePhase(
                overall,
                startup,
                active,
                recovery,
                anticipation,
                impact,
                followThrough,
                recoveryEase);
        }

        void OnDestroy()
        {
            for (int i = 0; i < _materials.Count; i++)
                if (_materials[i].Material != null) Destroy(_materials[i].Material);
        }

        public static int SelectPresentationFrame(int currentFrame, int lastSimulatedFrame)
        {
            return lastSimulatedFrame >= 0 ? lastSimulatedFrame : currentFrame;
        }

        static float SegmentProgress(int localFrame, int frames)
        {
            if (frames <= 0) return localFrame >= 0 ? 1f : 0f;
            return Mathf.Clamp01((localFrame + 1f) / frames);
        }

        static float RecoverySettle(float recoveryEase)
        {
            recoveryEase = Mathf.Clamp01(recoveryEase);
            return 4f * recoveryEase * (1f - recoveryEase);
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
