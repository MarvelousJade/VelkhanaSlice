using UnityEngine;

namespace VelkhanaSlice
{
    /// <summary>
    /// Keeps the angled top-down camera on the hunter, biased toward whatever it is fighting so
    /// both stay framed. Runs in LateUpdate so it reads positions after the fixed step has moved them.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public Transform target;

        [Tooltip("Kept in frame alongside the target, so the monster does not sit off screen.")]
        public Transform secondaryTarget;

        [Tooltip("How far toward the secondary target the camera leans, 0 is fully on the hunter.")]
        [Range(0f, 0.5f)] public float secondaryBias = 0.3f;

        [Tooltip("Offset from the focus point. The Y and Z set the pitch the plan asks for.")]
        public Vector3 offset = new Vector3(0f, 17f, -11f);

        [Tooltip("Metres per second of catch-up. Zero snaps.")]
        public float followSharpness = 8f;

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 focus = target.position;
            if (secondaryTarget != null)
                focus = Vector3.Lerp(focus, secondaryTarget.position, secondaryBias);

            Vector3 wanted = focus + offset;
            transform.position = followSharpness <= 0f
                ? wanted
                : Vector3.Lerp(transform.position, wanted, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
        }
    }
}
