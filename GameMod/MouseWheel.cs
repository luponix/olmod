using UnityEngine;

namespace GameMod
{
    static class MouseWheel
    {
        static int evaluatedFrame = -1;
        static bool wasTurning;
        static int step;

        public static bool IsTurning
        {
            get { return Input.GetAxis("Mouse ScrollWheel") != 0f; }
        }

        public static int Step()
        {
            int frame = Time.frameCount;
            if (frame == evaluatedFrame)
                return step;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            bool isTurning = IsTurning;
            bool turnedLastFrame = wasTurning && evaluatedFrame == frame - 1;
            step = isTurning && !turnedLastFrame ? (wheel < 0f ? 1 : -1) : 0;
            wasTurning = isTurning;
            evaluatedFrame = frame;
            return step;
        }
    }
}
