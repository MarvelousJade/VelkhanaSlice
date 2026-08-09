using UnityEngine;
using VelkhanaSlice.Combat;

namespace VelkhanaSlice.Hunter
{
    /// <summary>
    /// Cheap graybox animation driven from the combat state. This moves visuals only: the
    /// CharacterController, hitboxes and frame timings remain authoritative in HunterController.
    /// Replace this component with a humanoid Animator when final character art arrives.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(HunterController))]
    public sealed class HunterPresentation : MonoBehaviour
    {
        public enum CombatPoseFamily
        {
            None,
            StationaryDraw,
            MovingDraw,
            ChargeHold,
            ChargedSlash,
            WideSlash,
            RisingSlash,
            SideBlow,
            Tackle,
            Kick,
            LeapingWideSlash,
            TrueChargeOpening,
            TrueChargeFinisher,
        }

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

        [Header("Graybox visual hierarchy")]
        public Transform visualRoot;
        public Transform body;
        public Transform sword;
        public Transform handSocket;
        public Transform backSocket;

        [Header("Pose tuning")]
        public float rollTurns = 1f;
        public float chargeLeanDegrees = 16f;
        public float chargeCrouch = 0.16f;
        public float chargePulse = 0.035f;
        public float runLeanDegrees = 12f;
        public float runBob = 0.07f;

        HunterController _controller;
        Renderer _bodyRenderer;
        Material _bodyMaterial;
        Light _chargeLight;
        Vector3 _visualRestPosition;
        Quaternion _visualRestRotation;
        Vector3 _bodyRestScale;
        Vector3 _swordRestScale;
        Color _bodyRestColor = Color.white;
        int _presentationAttackFrame;

        void Awake()
        {
            _controller = GetComponent<HunterController>();
            ResolveGrayboxHierarchy();

            if (visualRoot != null)
            {
                _visualRestPosition = visualRoot.localPosition;
                _visualRestRotation = visualRoot.localRotation;
            }

            if (body != null) _bodyRestScale = body.localScale;
            if (sword != null) _swordRestScale = sword.localScale;

            SetupChargeGlow();
        }

        /// <summary>
        /// Adapts scenes generated before this presentation component existed. The scene builder
        /// now serializes the same hierarchy, but this keeps the checked-in scene playable even
        /// when a batch rebuild is unavailable.
        /// </summary>
        void ResolveGrayboxHierarchy()
        {
            if (body == null) body = FindNamed(transform, "Body");
            if (sword == null) sword = FindNamed(transform, "SwordVisual");
            if (visualRoot == null) visualRoot = FindNamed(transform, "VisualRoot");

            if (visualRoot == null && body != null && sword != null)
            {
                visualRoot = new GameObject("VisualRoot").transform;
                visualRoot.SetParent(transform, false);

                if (body.parent == transform) body.SetParent(visualRoot, false);
                Transform facingMarker = transform.Find("FacingMarker");
                if (facingMarker != null) facingMarker.SetParent(visualRoot, false);
                if (sword.parent == transform) sword.SetParent(visualRoot, false);
            }

            if (visualRoot == null) return;

            if (handSocket == null) handSocket = FindNamed(visualRoot, "HandSocket");
            if (handSocket == null)
            {
                handSocket = new GameObject("HandSocket").transform;
                handSocket.SetParent(visualRoot, false);
                handSocket.localPosition = new Vector3(0.42f, 0.08f, 0.92f);
                handSocket.localRotation = Quaternion.Euler(-8f, 0f, -5f);
            }

            if (backSocket == null) backSocket = FindNamed(visualRoot, "BackSocket");
            if (backSocket == null)
            {
                backSocket = new GameObject("BackSocket").transform;
                backSocket.SetParent(visualRoot, false);
                backSocket.localPosition = new Vector3(0f, 0.05f, -0.42f);
                backSocket.localRotation =
                    Quaternion.LookRotation(new Vector3(0.58f, 0.8f, 0.12f).normalized, Vector3.up);
            }
        }

        void SetupChargeGlow()
        {
            if (body != null) _bodyRenderer = body.GetComponent<Renderer>();
            if (_bodyRenderer != null)
            {
                // Renderer.material creates one private instance, so the hunter can glow without
                // tinting any other object that happens to use the graybox hunter material.
                _bodyMaterial = _bodyRenderer.material;
                if (_bodyMaterial.HasProperty("_Color"))
                    _bodyRestColor = _bodyMaterial.GetColor("_Color");
                if (_bodyMaterial.HasProperty("_EmissionColor"))
                {
                    _bodyMaterial.EnableKeyword("_EMISSION");
                    _bodyMaterial.SetColor("_EmissionColor", Color.black);
                }
            }

            if (visualRoot == null) return;

            Transform existing = FindNamed(visualRoot, "ChargeGlow");
            if (existing != null) _chargeLight = existing.GetComponent<Light>();
            if (_chargeLight == null)
            {
                var glow = new GameObject("ChargeGlow");
                glow.transform.SetParent(visualRoot, false);
                glow.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                _chargeLight = glow.AddComponent<Light>();
                _chargeLight.type = LightType.Point;
                _chargeLight.range = 4f;
                _chargeLight.shadows = LightShadows.None;
            }

            _chargeLight.enabled = false;
        }

        void LateUpdate()
        {
            if (_controller == null || visualRoot == null) return;

            _presentationAttackFrame = SelectPresentationFrame(
                _controller.AttackFrame,
                _controller.LastSimulatedAttackFrame);
            PoseBody();
            PoseSword();
            PoseChargeGlow();
        }

        void PoseBody()
        {
            Vector3 position = _visualRestPosition;
            Quaternion rotation = _visualRestRotation;
            Vector3 scale = _bodyRestScale;
            CombatPoseFamily family = ClassifyPose(_controller.CurrentNode);

            if (_controller.CurrentState == HunterController.State.Rolling)
            {
                float duration = Mathf.Max(1f, _controller.rollFrames);
                float t = Mathf.Clamp01(_controller.StateFrame / duration);
                float tuck = Mathf.Sin(t * Mathf.PI);
                float bank = Mathf.Sin(t * Mathf.PI * 2f) * 6f;

                position.y -= 0.22f * tuck;
                position.z -= 0.06f * tuck;
                rotation *= Quaternion.Euler(360f * rollTurns * t, 0f, bank);
                scale = Vector3.Scale(scale, new Vector3(1f + 0.12f * tuck, 1f - 0.2f * tuck, 1f));
            }
            else if (_controller.CurrentState == HunterController.State.Launched)
            {
                if (_controller.IsKnockedDown)
                {
                    position.y -= 0.58f;
                    position.x += 0.12f;
                    rotation *= Quaternion.Euler(0f, 0f, 78f);
                    scale = Vector3.Scale(scale, new Vector3(1f, 0.82f, 1f));
                }
                else
                {
                    float tumble = _controller.StateFrame * 18f;
                    rotation *= Quaternion.Euler(tumble, 0f, tumble * 0.35f);
                }
            }
            else if (_controller.CurrentState == HunterController.State.Charging)
            {
                ApplyChargeBodyPose(ref position, ref rotation, ref scale, family);
            }
            else if (_controller.CurrentState == HunterController.State.Guarding)
            {
                position.y -= 0.13f;
                position.z -= 0.06f;
                rotation *= Quaternion.Euler(12f, 0f, -10f);
                scale = Vector3.Scale(scale, new Vector3(1.06f, 0.92f, 1f));
            }
            else if (_controller.CurrentState == HunterController.State.Attacking &&
                     _controller.CurrentAttack != null)
            {
                ApplyAttackBodyPose(ref position, ref rotation, ref scale, family);
            }
            else if (_controller.IsRunning)
            {
                float stride = Mathf.Sin(Time.time * 14f);
                position.y += Mathf.Abs(stride) * runBob;
                position.x += stride * 0.015f;
                rotation *= Quaternion.Euler(runLeanDegrees, 0f, stride * 5f);
                scale = Vector3.Scale(scale, new Vector3(1f - 0.035f * stride, 1f + 0.05f * stride, 1f));
            }

            visualRoot.localPosition = position;
            visualRoot.localRotation = rotation;
            if (body != null) body.localScale = scale;
        }

        void ApplyChargeBodyPose(
            ref Vector3 position,
            ref Quaternion rotation,
            ref Vector3 scale,
            CombatPoseFamily family)
        {
            float chargeTime = _controller.ChargeFrames * Time.fixedDeltaTime;
            float settle = SmoothStep(Mathf.Clamp01(_controller.ChargeFrames / 12f));
            float pulse = Mathf.Sin(chargeTime * 9f) * chargePulse * settle;
            float stageWeight = ChargeStageWeight();
            float movingDraw = family == CombatPoseFamily.MovingDraw ? 1f : 0f;

            position.y -= chargeCrouch * settle * stageWeight + pulse;
            position.z -= 0.04f * settle + 0.05f * movingDraw * settle;
            position.x -= 0.025f * movingDraw * settle;
            rotation *= Quaternion.Euler(
                chargeLeanDegrees * settle * stageWeight + 4f * movingDraw * settle,
                0f,
                Mathf.Lerp(3f, -9f, movingDraw) * settle - 4f * (stageWeight - 1f) * settle);
            scale = Vector3.Scale(
                scale,
                new Vector3(1f + 0.04f * settle, 1f - 0.08f * settle, 1f));
        }

        void ApplyAttackBodyPose(
            ref Vector3 position,
            ref Quaternion rotation,
            ref Vector3 scale,
            CombatPoseFamily family)
        {
            AttackPosePhase phase = EvaluateAttackPhase(
                _controller.CurrentAttack,
                _presentationAttackFrame);
            float chargePower = 1f + 0.08f * Mathf.Clamp(_controller.ChargeLevel, 0, 3);

            switch (family)
            {
                case CombatPoseFamily.StationaryDraw:
                    float drawWeight = Mathf.Max(phase.Anticipation, phase.FollowThrough);
                    float drawSettle = RecoverySettle(phase.RecoveryEase);
                    position.y -= 0.05f * drawWeight;
                    position.z -= 0.04f * phase.Anticipation;
                    rotation *= Quaternion.Euler(
                        -5f * phase.Anticipation + 10f * phase.FollowThrough,
                        0f,
                        8f * phase.Anticipation - 5f * drawSettle);
                    scale = Vector3.Scale(
                        scale,
                        new Vector3(1f + 0.015f * phase.FollowThrough, 1f, 1f));
                    break;

                case CombatPoseFamily.MovingDraw:
                case CombatPoseFamily.ChargedSlash:
                    float slashPower = chargePower +
                                       (_controller.CurrentNode == HunterController.Wp00Node.StrongVerticalSlash ? 0.18f : 0f);
                    float movingDraw = family == CombatPoseFamily.MovingDraw ? 1f : 0f;
                    position.y -= 0.13f * phase.Anticipation * slashPower + 0.04f * phase.FollowThrough;
                    position.z += 0.08f * phase.Impact + 0.1f * phase.FollowThrough;
                    position.x -= 0.02f * movingDraw * phase.Anticipation;
                    rotation *= Quaternion.Euler(
                        -24f * phase.Anticipation * slashPower + 42f * phase.Impact * slashPower -
                        14f * RecoverySettle(phase.RecoveryEase),
                        0f,
                        10f * phase.Anticipation - 18f * phase.Impact + 8f * phase.FollowThrough +
                        4f * movingDraw * phase.Anticipation);
                    scale = Vector3.Scale(
                        scale,
                        new Vector3(1f + 0.03f * phase.Impact, 1f - 0.06f * phase.Anticipation + 0.03f * phase.FollowThrough, 1f));
                    break;

                case CombatPoseFamily.TrueChargeOpening:
                    position.y -= 0.17f * phase.Anticipation + 0.02f * phase.FollowThrough;
                    position.z -= 0.05f * phase.Anticipation + 0.1f * phase.Impact;
                    rotation *= Quaternion.Euler(
                        -30f * phase.Anticipation + 30f * phase.Impact -
                        8f * RecoverySettle(phase.RecoveryEase),
                        0f,
                        16f * phase.Anticipation - 20f * phase.Impact + 8f * phase.FollowThrough);
                    scale = Vector3.Scale(
                        scale,
                        new Vector3(1f + 0.025f * phase.Impact, 1f - 0.08f * phase.Anticipation + 0.02f * phase.FollowThrough, 1f));
                    break;

                case CombatPoseFamily.TrueChargeFinisher:
                    float finisherPower = TrueChargeFinisherPower();
                    position.y -= 0.22f * phase.Anticipation * finisherPower + 0.06f * phase.FollowThrough;
                    position.z += 0.12f * phase.Impact + 0.14f * phase.FollowThrough;
                    position.x -= 0.03f * phase.Anticipation + 0.02f * phase.Impact;
                    rotation *= Quaternion.Euler(
                        -38f * phase.Anticipation * finisherPower + 64f * phase.Impact * finisherPower -
                        22f * RecoverySettle(phase.RecoveryEase),
                        0f,
                        18f * phase.Anticipation - 30f * phase.Impact + 16f * phase.FollowThrough);
                    scale = Vector3.Scale(
                        scale,
                        new Vector3(1f + 0.05f * phase.Impact, 1f - 0.1f * phase.Anticipation + 0.04f * phase.FollowThrough, 1f));
                    break;

                case CombatPoseFamily.WideSlash:
                    float widePower = _controller.CurrentNode == HunterController.Wp00Node.StrongWideSlash ? 1.18f : 1f;
                    position.y -= 0.08f * phase.Anticipation;
                    position.x += 0.06f * phase.Impact;
                    rotation *= Quaternion.Euler(
                        -10f * phase.Anticipation + 12f * phase.Impact,
                        0f,
                        14f * phase.Anticipation - 24f * phase.Impact * widePower + 10f * phase.FollowThrough);
                    break;

                case CombatPoseFamily.RisingSlash:
                    position.y -= 0.12f * phase.Anticipation - 0.1f * phase.Impact;
                    position.z -= 0.05f * phase.Anticipation;
                    rotation *= Quaternion.Euler(
                        26f * phase.Impact - 14f * phase.Anticipation,
                        0f,
                        -10f * phase.Anticipation + 12f * phase.Impact);
                    break;

                case CombatPoseFamily.SideBlow:
                    position.y -= 0.07f * phase.Anticipation;
                    rotation *= Quaternion.Euler(
                        -8f * phase.Anticipation + 8f * phase.Impact,
                        0f,
                        18f * phase.Anticipation - 22f * phase.Impact + 6f * phase.FollowThrough);
                    break;

                case CombatPoseFamily.Tackle:
                    float brace = Mathf.Max(
                        phase.Anticipation,
                        Mathf.Max(phase.Impact, phase.FollowThrough));
                    position.y -= 0.16f * phase.Anticipation;
                    position.z += 0.09f * phase.Impact + 0.08f * phase.FollowThrough;
                    rotation *= Quaternion.Euler(
                        24f * phase.Impact - 18f * phase.Anticipation,
                        0f,
                        -12f * phase.Anticipation + 8f * phase.Impact);
                    scale = Vector3.Scale(
                        scale,
                        new Vector3(1f + 0.05f * brace, 1f - 0.06f * brace, 1f));
                    break;

                case CombatPoseFamily.Kick:
                    position.y -= 0.06f * phase.Anticipation;
                    position.z += 0.04f * phase.Impact;
                    rotation *= Quaternion.Euler(
                        12f * phase.Impact - 6f * phase.Anticipation,
                        0f,
                        -8f * phase.Anticipation + 6f * phase.Impact);
                    break;

                case CombatPoseFamily.LeapingWideSlash:
                    position.y += 0.12f * phase.Impact - 0.08f * phase.Anticipation;
                    position.z += 0.08f * phase.Impact;
                    rotation *= Quaternion.Euler(
                        -16f * phase.Anticipation + 12f * phase.Impact,
                        0f,
                        18f * phase.Anticipation - 24f * phase.Impact + 10f * phase.FollowThrough);
                    break;
            }
        }

        void PoseSword()
        {
            if (sword == null || handSocket == null || backSocket == null) return;

            Vector3 localPosition;
            Quaternion localRotation;

            if (_controller.IsWeaponTransitioning)
            {
                float t = SmoothStep(_controller.WeaponTransitionProgress);
                Transform from = _controller.WeaponTransitionDrawn ? backSocket : handSocket;
                Transform to = _controller.WeaponTransitionDrawn ? handSocket : backSocket;
                localPosition = Vector3.Lerp(from.localPosition, to.localPosition, t);
                localRotation = Quaternion.Slerp(from.localRotation, to.localRotation, t);
            }
            else
            {
                Transform socket = _controller.WeaponDrawn ? handSocket : backSocket;
                localPosition = socket.localPosition;
                localRotation = socket.localRotation;
            }

            switch (_controller.CurrentState)
            {
                case HunterController.State.Charging:
                    ApplyChargeSwordPose(ref localPosition, ref localRotation, ClassifyPose(_controller.CurrentNode));
                    break;
                case HunterController.State.Guarding:
                    localPosition = handSocket.localPosition + new Vector3(-0.15f, 0.18f, 0.42f);
                    localRotation = Quaternion.Euler(-18f, 4f, 78f);
                    break;
                case HunterController.State.Attacking:
                    ApplyAttackSwordPose(ref localPosition, ref localRotation, ClassifyPose(_controller.CurrentNode));
                    break;
                case HunterController.State.Rolling:
                    ApplyRollingSwordPose(ref localPosition, ref localRotation);
                    break;
                case HunterController.State.Launched:
                    ApplyLaunchedSwordPose(ref localPosition, ref localRotation);
                    break;
                default:
                    ApplyIdleSwordPose(ref localPosition, ref localRotation);
                    break;
            }

            sword.localPosition = localPosition;
            sword.localRotation = localRotation;
            sword.localScale = _swordRestScale;
        }

        void ApplyIdleSwordPose(ref Vector3 localPosition, ref Quaternion localRotation)
        {
            if (_controller.IsWeaponTransitioning) return;

            if (_controller.WeaponDrawn)
            {
                float sway = Mathf.Sin(Time.time * (_controller.IsRunning ? 11.5f : 4.2f));
                localPosition += new Vector3(
                    0.02f * sway,
                    Mathf.Abs(sway) * (_controller.IsRunning ? 0.06f : 0.02f),
                    0.02f * Mathf.Abs(sway));
                localRotation *= Quaternion.Euler(
                    -2f + Mathf.Abs(sway) * 3f,
                    sway * 3.5f,
                    -1.5f * sway);
                return;
            }

            if (_controller.IsRunning)
            {
                float stride = Mathf.Sin(Time.time * 10f);
                localPosition += new Vector3(0f, Mathf.Abs(stride) * 0.04f, -0.03f * Mathf.Abs(stride));
                localRotation *= Quaternion.Euler(6f * Mathf.Abs(stride), stride * 4f, -2f * stride);
            }
        }

        void ApplyRollingSwordPose(ref Vector3 localPosition, ref Quaternion localRotation)
        {
            Transform socket = _controller.WeaponDrawn ? handSocket : backSocket;
            float duration = Mathf.Max(1f, _controller.rollFrames);
            float t = Mathf.Clamp01(_controller.StateFrame / duration);
            float trail = Mathf.Sin(t * Mathf.PI);

            localPosition = socket.localPosition + new Vector3(0f, -0.08f * trail, -0.14f * trail);
            localRotation = socket.localRotation * Quaternion.Euler(40f * trail, 0f, (_controller.WeaponDrawn ? -22f : 16f) * trail);
        }

        void ApplyLaunchedSwordPose(ref Vector3 localPosition, ref Quaternion localRotation)
        {
            Transform socket = _controller.WeaponDrawn ? handSocket : backSocket;
            localPosition = socket.localPosition + new Vector3(0f, -0.08f, -0.06f);
            localRotation = socket.localRotation * Quaternion.Euler(18f, 0f, _controller.IsKnockedDown ? 42f : -18f);
        }

        void ApplyChargeSwordPose(
            ref Vector3 localPosition,
            ref Quaternion localRotation,
            CombatPoseFamily family)
        {
            float settle = SmoothStep(Mathf.Clamp01(_controller.ChargeFrames / 12f));
            Vector3 chargePosition = ChargeSwordPosition();
            Quaternion chargeRotation = ChargeSwordRotation();

            if (family == CombatPoseFamily.MovingDraw)
            {
                // N021 charges while drawing: visibly travel from the back socket into the
                // ready pose instead of snapping into the hand first.
                Vector3 movingPose = chargePosition + new Vector3(-0.08f, 0.04f, -0.1f);
                Quaternion movingRotation = chargeRotation * Quaternion.Euler(-6f, -18f, 10f);
                localPosition = Vector3.Lerp(backSocket.localPosition, movingPose, settle);
                localRotation = Quaternion.Slerp(backSocket.localRotation, movingRotation, settle);
                return;
            }

            localPosition = Vector3.Lerp(localPosition, chargePosition, settle);
            localRotation = Quaternion.Slerp(localRotation, chargeRotation, settle);
        }

        void ApplyAttackSwordPose(
            ref Vector3 localPosition,
            ref Quaternion localRotation,
            CombatPoseFamily family)
        {
            AttackDefinition attack = _controller.CurrentAttack;
            if (attack == null) return;

            AttackPosePhase phase = EvaluateAttackPhase(attack, _presentationAttackFrame);
            Vector3 restPosition = handSocket.localPosition;
            Quaternion restRotation = handSocket.localRotation;

            switch (family)
            {
                case CombatPoseFamily.StationaryDraw:
                    Vector3 unsheathePosition = handSocket.localPosition + new Vector3(-0.08f, 0.22f, -0.08f);
                    Quaternion unsheatheRotation = Quaternion.Euler(-42f, -12f, 18f);
                    localPosition = Vector3.Lerp(backSocket.localPosition, unsheathePosition, SmoothStep(phase.StartupProgress));
                    localRotation = Quaternion.Slerp(backSocket.localRotation, unsheatheRotation, SmoothStep(phase.StartupProgress));
                    localPosition = Vector3.Lerp(localPosition, restPosition, phase.RecoveryEase);
                    localRotation = Quaternion.Slerp(localRotation, restRotation, phase.RecoveryEase);
                    break;

                case CombatPoseFamily.MovingDraw:
                case CombatPoseFamily.ChargedSlash:
                    float slashPower = 1f + 0.1f * Mathf.Clamp(_controller.ChargeLevel, 0, 3) +
                                       (_controller.CurrentNode == HunterController.Wp00Node.StrongVerticalSlash ? 0.18f : 0f);
                    Vector3 anticipationPosition = family == CombatPoseFamily.MovingDraw
                        ? Vector3.Lerp(backSocket.localPosition, ChargeSwordPosition() + new Vector3(-0.08f, 0.06f, -0.1f), phase.Anticipation)
                        : ChargeSwordPosition() + new Vector3(-0.08f * slashPower, 0.06f, -0.1f);
                    Quaternion anticipationRotation = family == CombatPoseFamily.MovingDraw
                        ? Quaternion.Slerp(backSocket.localRotation, ChargeSwordRotation() * Quaternion.Euler(-8f, -16f, -8f), phase.Anticipation)
                        : ChargeSwordRotation() * Quaternion.Euler(-8f, -16f, -8f);
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        anticipationPosition,
                        anticipationRotation,
                        handSocket.localPosition + new Vector3(0.15f, -0.1f, 0.16f),
                        Quaternion.Euler(98f * slashPower, 8f, -18f),
                        handSocket.localPosition + new Vector3(0.08f, -0.28f, 0.48f),
                        Quaternion.Euler(42f, 16f, 14f),
                        phase,
                        1f);
                    break;

                case CombatPoseFamily.TrueChargeOpening:
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        ChargeSwordPosition() + new Vector3(-0.2f, 0.1f, -0.22f),
                        ChargeSwordRotation() * Quaternion.Euler(-18f, -28f, -12f),
                        handSocket.localPosition + new Vector3(0.12f, 0.02f, 0.3f),
                        Quaternion.Euler(72f, 10f, -24f),
                        handSocket.localPosition + new Vector3(-0.06f, -0.16f, -0.1f),
                        Quaternion.Euler(18f, -10f, 44f),
                        phase,
                        0.65f);
                    break;

                case CombatPoseFamily.TrueChargeFinisher:
                    float finisherPower = TrueChargeFinisherPower();
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        ChargeSwordPosition() + new Vector3(-0.16f * finisherPower, 0.14f, -0.32f),
                        ChargeSwordRotation() * Quaternion.Euler(-18f, -20f, -12f),
                        handSocket.localPosition + new Vector3(0.16f, -0.12f, 0.26f),
                        Quaternion.Euler(128f * finisherPower, 8f, -22f),
                        handSocket.localPosition + new Vector3(0.1f, -0.34f, 0.58f),
                        Quaternion.Euler(28f, 24f, 18f),
                        phase,
                        1f);
                    break;

                case CombatPoseFamily.WideSlash:
                    float widePower = _controller.CurrentNode == HunterController.Wp00Node.StrongWideSlash ? 1.18f : 1f;
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        handSocket.localPosition + new Vector3(-0.12f, 0.12f, -0.28f),
                        Quaternion.Euler(-18f, -96f, 92f),
                        handSocket.localPosition + new Vector3(0.1f, 0.02f, 0.34f),
                        Quaternion.Euler(18f, 102f * widePower, 68f),
                        handSocket.localPosition + new Vector3(0.04f, -0.02f, 0.18f),
                        Quaternion.Euler(8f, 48f, 62f),
                        phase,
                        1f);
                    break;

                case CombatPoseFamily.RisingSlash:
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        handSocket.localPosition + new Vector3(0f, -0.44f, -0.34f),
                        Quaternion.Euler(114f, 0f, -28f),
                        handSocket.localPosition + new Vector3(0f, 0.54f, 0.16f),
                        Quaternion.Euler(-58f, 0f, 18f),
                        handSocket.localPosition + new Vector3(0f, 0.22f, 0.28f),
                        Quaternion.Euler(-24f, 0f, 10f),
                        phase,
                        1f);
                    break;

                case CombatPoseFamily.SideBlow:
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        handSocket.localPosition + new Vector3(-0.14f, 0.1f, -0.18f),
                        Quaternion.Euler(-8f, -54f, 88f),
                        handSocket.localPosition + new Vector3(0.08f, 0.02f, 0.18f),
                        Quaternion.Euler(10f, 48f, 70f),
                        handSocket.localPosition + new Vector3(0.02f, -0.04f, 0.08f),
                        Quaternion.Euler(4f, 20f, 72f),
                        phase,
                        1f);
                    break;

                case CombatPoseFamily.Tackle:
                case CombatPoseFamily.Kick:
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        handSocket.localPosition + new Vector3(-0.18f, 0.18f, -0.42f),
                        Quaternion.Euler(-30f, -18f, 90f),
                        handSocket.localPosition + new Vector3(0.02f, -0.06f, 0.14f),
                        Quaternion.Euler(8f, 0f, 76f),
                        handSocket.localPosition + new Vector3(-0.04f, 0.04f, 0f),
                        Quaternion.Euler(-6f, 0f, 78f),
                        phase,
                        1f);
                    break;

                case CombatPoseFamily.LeapingWideSlash:
                    ApplyLayeredSwordPose(
                        ref localPosition,
                        ref localRotation,
                        restPosition,
                        restRotation,
                        handSocket.localPosition + new Vector3(-0.1f, 0.18f, -0.24f),
                        Quaternion.Euler(-12f, -86f, 88f),
                        handSocket.localPosition + new Vector3(0.08f, 0.18f, 0.42f),
                        Quaternion.Euler(2f, 112f, 60f),
                        handSocket.localPosition + new Vector3(0.04f, 0.06f, 0.18f),
                        Quaternion.Euler(-6f, 48f, 58f),
                        phase,
                        1f);
                    break;
            }
        }

        void PoseChargeGlow()
        {
            bool charging = _controller.CurrentState == HunterController.State.Charging;
            if (!charging)
            {
                if (_bodyMaterial != null)
                {
                    if (_bodyMaterial.HasProperty("_Color"))
                        _bodyMaterial.SetColor("_Color", _bodyRestColor);
                    if (_bodyMaterial.HasProperty("_EmissionColor"))
                        _bodyMaterial.SetColor("_EmissionColor", Color.black);
                }

                if (_chargeLight != null) _chargeLight.enabled = false;
                return;
            }

            int finalThreshold = 1;
            if (_controller.chargeThresholds != null && _controller.chargeThresholds.Length > 0)
                finalThreshold = Mathf.Max(1, _controller.chargeThresholds[_controller.chargeThresholds.Length - 1]);

            float progress = Mathf.Clamp01((float)_controller.ChargeFrames / finalThreshold);
            float pulse = 1f + Mathf.Sin(_controller.ChargeFrames * 0.32f) * 0.12f;
            Color glowColor = ChargeGlowColor(_controller.ChargeFrames, _controller.chargeThresholds);

            if (_bodyMaterial != null)
            {
                if (_bodyMaterial.HasProperty("_Color"))
                    _bodyMaterial.SetColor("_Color", Color.Lerp(_bodyRestColor, glowColor, 0.55f));
                if (_bodyMaterial.HasProperty("_EmissionColor"))
                    _bodyMaterial.SetColor("_EmissionColor", glowColor * ((0.75f + 2.1f * progress) * pulse));
            }

            if (_chargeLight != null)
            {
                _chargeLight.enabled = true;
                _chargeLight.color = glowColor;
                _chargeLight.intensity = (0.65f + 2.35f * progress) * pulse;
            }
        }

        Vector3 ChargeSwordPosition()
        {
            float stageWeight = ChargeStageWeight();
            return handSocket.localPosition +
                   new Vector3(
                       -0.12f * stageWeight,
                       0.22f + 0.08f * stageWeight,
                       -0.72f - 0.2f * stageWeight);
        }

        Quaternion ChargeSwordRotation()
        {
            float stageWeight = ChargeStageWeight();
            Quaternion pulledBack = Quaternion.LookRotation(
                new Vector3(
                    -0.18f - 0.05f * (stageWeight - 1f),
                    0.62f - 0.08f * (stageWeight - 1f),
                    -0.76f).normalized,
                Vector3.up);
            return pulledBack * Quaternion.Euler(0f, 0f, -12f * stageWeight);
        }

        float ChargeStageWeight()
        {
            switch (_controller.CurrentChargeStage)
            {
                case HunterController.ChargeStage.Strong: return 1.22f;
                case HunterController.ChargeStage.True: return 1.48f;
                default: return 1f;
            }
        }

        float TrueChargeFinisherPower()
        {
            switch (_controller.CurrentNode)
            {
                case HunterController.Wp00Node.TrueChargeFinishLevel1:
                    return 1.15f;
                case HunterController.Wp00Node.TrueChargeFinishLevel2:
                    return 1.28f;
                case HunterController.Wp00Node.TrueChargeFinishLevel3:
                    return 1.42f;
                default:
                    return 1.05f;
            }
        }

        static void ApplyLayeredSwordPose(
            ref Vector3 localPosition,
            ref Quaternion localRotation,
            Vector3 restPosition,
            Quaternion restRotation,
            Vector3 anticipationPosition,
            Quaternion anticipationRotation,
            Vector3 impactPosition,
            Quaternion impactRotation,
            Vector3 followPosition,
            Quaternion followRotation,
            AttackPosePhase phase,
            float returnWeight)
        {
            localPosition = BlendPosition(
                restPosition,
                anticipationPosition,
                impactPosition,
                followPosition,
                phase,
                returnWeight);
            localRotation = BlendRotation(
                restRotation,
                anticipationRotation,
                impactRotation,
                followRotation,
                phase,
                returnWeight);
        }

        static Vector3 BlendPosition(
            Vector3 rest,
            Vector3 anticipation,
            Vector3 impact,
            Vector3 follow,
            AttackPosePhase phase,
            float returnWeight)
        {
            Vector3 pose = rest;
            pose = Vector3.Lerp(pose, anticipation, phase.Anticipation);
            pose = Vector3.Lerp(pose, impact, phase.Impact);
            pose = Vector3.Lerp(pose, follow, phase.FollowThrough);
            return Vector3.Lerp(pose, rest, phase.RecoveryEase * Mathf.Clamp01(returnWeight));
        }

        static Quaternion BlendRotation(
            Quaternion rest,
            Quaternion anticipation,
            Quaternion impact,
            Quaternion follow,
            AttackPosePhase phase,
            float returnWeight)
        {
            Quaternion pose = rest;
            pose = Quaternion.Slerp(pose, anticipation, phase.Anticipation);
            pose = Quaternion.Slerp(pose, impact, phase.Impact);
            pose = Quaternion.Slerp(pose, follow, phase.FollowThrough);
            return Quaternion.Slerp(pose, rest, phase.RecoveryEase * Mathf.Clamp01(returnWeight));
        }

        public static CombatPoseFamily ClassifyPose(HunterController.Wp00Node node)
        {
            switch (node)
            {
                case HunterController.Wp00Node.DrawStationary:
                    return CombatPoseFamily.StationaryDraw;
                case HunterController.Wp00Node.MovingDrawToVerticalSlash:
                    return CombatPoseFamily.MovingDraw;
                case HunterController.Wp00Node.IdleToCharge:
                case HunterController.Wp00Node.ChargeSlashHold:
                case HunterController.Wp00Node.StrongChargeHold:
                case HunterController.Wp00Node.TrueChargeHold:
                    return CombatPoseFamily.ChargeHold;
                case HunterController.Wp00Node.VerticalSlash:
                case HunterController.Wp00Node.StrongVerticalSlash:
                    return CombatPoseFamily.ChargedSlash;
                case HunterController.Wp00Node.WideSlash:
                case HunterController.Wp00Node.StrongWideSlash:
                case HunterController.Wp00Node.WideSlashPostStrong:
                    return CombatPoseFamily.WideSlash;
                case HunterController.Wp00Node.RisingSlash:
                case HunterController.Wp00Node.RisingSlashPostStrong:
                    return CombatPoseFamily.RisingSlash;
                case HunterController.Wp00Node.SideBlow:
                case HunterController.Wp00Node.SideBlowPostStrong:
                    return CombatPoseFamily.SideBlow;
                case HunterController.Wp00Node.Tackle:
                case HunterController.Wp00Node.TackleLevel2:
                    return CombatPoseFamily.Tackle;
                case HunterController.Wp00Node.Kick:
                    return CombatPoseFamily.Kick;
                case HunterController.Wp00Node.LeapingWideSlash:
                    return CombatPoseFamily.LeapingWideSlash;
                case HunterController.Wp00Node.TrueChargeFirstHit:
                    return CombatPoseFamily.TrueChargeOpening;
                case HunterController.Wp00Node.TrueChargeNormalFinish:
                case HunterController.Wp00Node.TrueChargeFinishLevel1:
                case HunterController.Wp00Node.TrueChargeFinishLevel2:
                case HunterController.Wp00Node.TrueChargeFinishLevel3:
                    return CombatPoseFamily.TrueChargeFinisher;
                default:
                    return CombatPoseFamily.None;
            }
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
            float recoveryEase = SmoothStep(recovery);

            float anticipation = startupFrames > 0
                ? SmoothStep(startup) *
                  (1f - 0.7f * SmoothStep(active)) *
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
                ? SmoothStep(active)
                : SmoothStep(startup);
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

        /// <summary>White at level one, yellow at level two, red at full charge.</summary>
        public static Color ChargeGlowColor(int heldFrames, int[] thresholds)
        {
            Color yellow = new Color(1f, 0.72f, 0.04f);
            Color red = new Color(1f, 0.04f, 0.015f);
            if (thresholds == null || thresholds.Length == 0) return Color.white;

            int first = thresholds[0];
            int second = thresholds.Length > 1 ? thresholds[1] : first;
            int final = thresholds[thresholds.Length - 1];

            if (heldFrames <= first) return Color.white;
            if (heldFrames <= second)
                return Color.Lerp(Color.white, yellow, Mathf.InverseLerp(first, second, heldFrames));

            return Color.Lerp(yellow, red, Mathf.InverseLerp(second, final, heldFrames));
        }

        void OnDestroy()
        {
            if (_bodyMaterial != null) Destroy(_bodyMaterial);
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

        static float SmoothStep(float value)
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
