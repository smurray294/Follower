// File: Mouse.cs (Corrected Synchronous Version)

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SharpDX;

namespace Follower
{
    public static class Mouse
    {
        private static readonly Random Rng = new Random();

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        public const int MOUSEEVENTF_LEFTDOWN = 0x02;
        public const int MOUSEEVENTF_LEFTUP = 0x04;
        public const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const int MOUSEEVENTF_RIGHTUP = 0x10;

        public static void SetCursorPos(Vector2 pos)
        {
            SetCursorPos((int)pos.X, (int)pos.Y);
        }

        // --- START OF NEW/CORRECTED SYNCHRONOUS METHODS ---

        public static void LeftClick()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(20 + Rng.Next(30));
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        public static void RightClick()
        {
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            Thread.Sleep(20 + Rng.Next(30));
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
        }
        
        public static void LeftMouseDown()
        {
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        }

        public static void LeftMouseUp()
        {
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        /// <summary>
        /// Moves the mouse smoothly from its current position to the target.
        /// </summary>
        public static void SetCursorPosHuman(Vector2 targetPos)
        {
            var initialPos = Follower.Instance.GetMousePosition();
            var distance = Vector2.Distance(initialPos, targetPos);
            
            // If the distance is very small, just jump to the position.
            if (distance < 5)
            {
                SetCursorPos(targetPos);
                return;
            }

            // Calculate number of steps for the smooth movement
            var steps = (int)Math.Min(25, distance / 10);
            if (steps < 5) steps = 5;

            for (var i = 1; i <= steps; i++)
            {
                // Lerp (Linear Interpolation) finds a point between 'initial' and 'target'
                var nextPos = Vector2.Lerp(initialPos, targetPos, (float)i / steps);
                SetCursorPos(nextPos);
                Thread.Sleep(5); // A small delay between each step
            }
        }

        /// <summary>
        /// Moves the mouse to a position and then left-clicks.
        /// </summary>
        public static void SetCursorPosAndLeftClickHuman(Vector2 coords, int extraDelay)
        {
            SetCursorPosHuman(coords);
            Thread.Sleep(Follower.Instance.Settings.BotInputFrequency + extraDelay);
            LeftMouseDown();
            Thread.Sleep(Follower.Instance.Settings.BotInputFrequency + extraDelay);
            LeftMouseUp();
            Thread.Sleep(100);
        }

        // --- END OF NEW/CORRECTED SYNCHRONOUS METHODS ---
    }
}