using System;
using System.Collections;
using System.Runtime.InteropServices;
using SharpDX;
using ExileCore.Shared; // Required for WaitTime

namespace Follower
{
    public static class Mouse
    {
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        public const int MOUSEEVENTF_LEFTDOWN = 0x02;
        public const int MOUSEEVENTF_LEFTUP = 0x04;

        public static void SetCursorPos(Vector2 pos)
        {
            SetCursorPos((int)pos.X, (int)pos.Y);
        }

        public static IEnumerator LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            yield return new WaitTime(20 + new Random().Next(30));
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        /// <summary>
        /// Moves the mouse smoothly from its current position to the target.
        /// </summary>
        public static IEnumerator SetCursorPosHuman(Vector2 targetPos)
        {
            var initialPos = Follower.Instance.GetMousePosition();
            var distance = Vector2.Distance(initialPos, targetPos);
            
            if (distance < 3)
            {
                SetCursorPos(targetPos);
                yield break; // Exit the coroutine
            }

            var steps = (int)Math.Min(30, distance / 15);
            if (steps < 5) steps = 5;

            for (var i = 1; i <= steps; i++)
            {
                var nextPos = Vector2.Lerp(initialPos, targetPos, (float)i / steps);
                SetCursorPos(nextPos);
                yield return new WaitTime(2); // Wait 2ms between each small move
            }
        }

        /// <summary>
        /// Moves the mouse to a position and then left-clicks.
        /// </summary>
        public static IEnumerator SetCursorPosAndLeftClickHuman(Vector2 coords, int extraDelay)
        {
            yield return SetCursorPosHuman(coords);
            yield return new WaitTime(Follower.Instance.Settings.BotInputFrequency + extraDelay);
            yield return LeftClick();
            yield return new WaitTime(100);
        }
    }
}