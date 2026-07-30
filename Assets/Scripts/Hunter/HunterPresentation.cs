using UnityEngine;

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

            PoseBody();
            PoseSword();
            PoseChargeGlow();
        }

        void PoseBody()
        {
            Vector3 position = _visualRestPosition;
            Quaternion rotation = _visualRestRotation;
            Vector3 scale = _bodyRestScale;

            if (_controller.CurrentState == HunterController.State.Rolling)
            {
                float duration = Mathf.Max(1, _controller.rollFrames);
                float t = Mathf.Clamp01(_controller.StateFrame / duration);
                float tuck = Mathf.Sin(t * Mathf.PI);

                position.y -= 0.22f * tuck;
                rotation *= Quaternion.Euler(360f * rollTurns * t, 0f, 0f);
                scale = Vector3.Scale(scale, new Vector3(1f + 0.12f * tuck, 1f - 0.2f * tuck, 1f));
            }
            else if (_controller.CurrentState == HunterController.State.Charging)
            {
                float chargeTime = _controller.ChargeFrames * Time.fixedDeltaTime;
                float settle = Mathf.Clamp01(_controller.ChargeFrames / 12f);
                float pulse = Mathf.Sin(chargeTime * 9f) * chargePulse * settle;

                position.y -= chargeCrouch * settle + pulse;
                rotation *= Quaternion.Euler(chargeLeanDegrees * settle, 0f, 0f);
                scale = Vector3.Scale(scale, new Vector3(1f + 0.05f * settle, 1f - 0.08f * settle, 1f));
            }
            else if (_controller.IsRunning)
            {
                float stride = Mathf.Sin(Time.time * 14f);
                position.y += Mathf.Abs(stride) * runBob;
                rotation *= Quaternion.Euler(runLeanDegrees, 0f, stride * 4f);
                scale = Vector3.Scale(scale, new Vector3(1f - 0.04f * stride, 1f + 0.05f * stride, 1f));
            }
            else if (_controller.CurrentState == HunterController.State.Attacking &&
                     _controller.CurrentAttack != null)
            {
                float total = Mathf.Max(1, _controller.CurrentAttack.TotalFrames - 1);
                float t = Mathf.Clamp01(_controller.AttackFrame / total);
                float effort = Mathf.Sin(t * Mathf.PI);

                position.y -= 0.08f * effort;
                rotation *= Quaternion.Euler(8f * effort, 0f, -5f * effort);
            }

            visualRoot.localPosition = position;
            visualRoot.localRotation = rotation;
            if (body != null) body.localScale = scale;
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

            if (_controller.CurrentState == HunterController.State.Charging)
            {
                float settle = SmoothStep(Mathf.Clamp01(_controller.ChargeFrames / 12f));
                Vector3 chargePosition = ChargeSwordPosition();
                Quaternion chargeRotation = ChargeSwordRotation();
                localPosition = Vector3.Lerp(localPosition, chargePosition, settle);
                localRotation = Quaternion.Slerp(localRotation, chargeRotation, settle);
            }
            else if (_controller.CurrentState == HunterController.State.Attacking &&
                     _controller.CurrentAttack != null)
            {
                float total = Mathf.Max(1, _controller.CurrentAttack.TotalFrames - 1);
                float t = Mathf.Clamp01(_controller.AttackFrame / total);
                float recover = SmoothStep(Mathf.Clamp01((t - 0.62f) / 0.38f));

                if (_controller.CurrentAttack == _controller.drawSlash)
                {
                    float drawDuration = Mathf.Max(1f, _controller.CurrentAttack.startupFrames);
                    float draw = SmoothStep(Mathf.Clamp01(_controller.AttackFrame / drawDuration));
                    Quaternion drawStrike = Quaternion.Euler(42f, -18f, 55f);

                    localPosition = Vector3.Lerp(backSocket.localPosition, handSocket.localPosition, draw);
                    localRotation = Quaternion.Slerp(backSocket.localRotation, drawStrike, draw);
                    localRotation = Quaternion.Slerp(localRotation, handSocket.localRotation, recover);
                }
                else
                {
                    float swing = SmoothStep(Mathf.Clamp01(t * 1.8f));
                    Quaternion strike = Quaternion.Slerp(
                        ChargeSwordRotation(),
                        Quaternion.Euler(82f, 0f, 12f),
                        swing);

                    localPosition = Vector3.Lerp(
                        ChargeSwordPosition(),
                        handSocket.localPosition,
                        recover);
                    localRotation = Quaternion.Slerp(strike, handSocket.localRotation, recover);
                }
            }

            sword.localPosition = localPosition;
            sword.localRotation = localRotation;
            sword.localScale = _swordRestScale;
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
            return handSocket.localPosition + new Vector3(-0.12f, 0.28f, -0.9f);
        }

        static Quaternion ChargeSwordRotation()
        {
            Quaternion pulledBack = Quaternion.LookRotation(
                new Vector3(-0.18f, 0.62f, -0.76f).normalized,
                Vector3.up);
            return pulledBack * Quaternion.Euler(0f, 0f, -12f);
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
