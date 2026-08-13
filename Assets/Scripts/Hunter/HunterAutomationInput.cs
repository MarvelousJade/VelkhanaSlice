using System;
using UnityEngine;

namespace VelkhanaSlice.Hunter
{
    /// <summary>
    /// Device-independent hunter controls used by deterministic tests and the runtime automation
    /// bridge. Button fields are held states; HunterController derives one-frame edges when a
    /// field changes from false to true.
    /// </summary>
    [Serializable]
    public struct HunterAutomationInput
    {
        public float moveX;
        public float moveY;
        public float aimX;
        public float aimY;
        public bool primary;
        public bool secondary;
        public bool dodge;
        public bool sheathe;
        public bool run;
        public bool guard;

        public Vector2 Move => Vector2.ClampMagnitude(new Vector2(moveX, moveY), 1f);
        public Vector2 Aim => Vector2.ClampMagnitude(new Vector2(aimX, aimY), 1f);

        public static HunterAutomationInput Released => default;
    }
}
