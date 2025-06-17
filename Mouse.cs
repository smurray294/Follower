using System;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ExileCore.Shared;
using SharpDX;

namespace Follower
{
    public static class Mouse
    {

        public const int MouseeventfMove = 0x0001;
        public const int MouseeventfLeftdown = 0x02;
        public const int MouseeventfLeftup = 0x04;
        public const int MouseeventfMiddown = 0x0020;
        public const int MouseeventfMidup = 0x0040;
        public const int MouseeventfRightdown = 0x0008;
        public const int MouseeventfRightup = 0x0010;
        public const int MouseEventWheel = 0x800;

        public static float speedMouse = 1;
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        public const int MOUSEEVENTF_LEFTDOWN = 0x02;
        public const int MOUSEEVENTF_LEFTUP = 0x04;

        public static void SetCursorPos(Vector2 pos)
        {
            SetCursorPos((int)pos.X, (int)pos.Y);
        }

        public static void LeftMouseDown()
        {
            mouse_event(MouseeventfLeftdown, 0, 0, 0, 0);
        }

        public static void LeftMouseUp()
        {
            mouse_event(MouseeventfLeftup, 0, 0, 0, 0);
        }

        public static IEnumerator LeftClick()
        {
            LeftMouseDown();
            yield return new WaitTime(40);
            LeftMouseUp();
            yield return new WaitTime(100);
        }

        /// <summary>
        /// Moves the mouse smoothly from its current position to the target.
        /// </summary>
        public static IEnumerator SetCursorPosHuman(Vector2 targetPos, bool limited=true)
        {

            // Keep Curser Away from Screen Edges to prevent UI Interaction.
            var windowRect = Follower.Instance.GameController.Window.GetWindowRectangle();
            var edgeBoundsX = windowRect.Size.Width / 4;
            var edgeBoundsY = windowRect.Size.Height / 4;

            if (limited)
            {
                if (targetPos.X <= windowRect.Left + edgeBoundsX ) targetPos.X = windowRect.Left + edgeBoundsX;
                if (targetPos.Y <= windowRect.Top + edgeBoundsY) targetPos.Y = windowRect.Left + edgeBoundsY;
                if (targetPos.X >= windowRect.Right - edgeBoundsX) targetPos.X = windowRect.Right -edgeBoundsX;
                if (targetPos.Y >= windowRect.Bottom - edgeBoundsY) targetPos.Y = windowRect.Bottom - edgeBoundsY;
            }

            var step = (float)Math.Sqrt(Vector2.Distance(Follower.Instance.GetMousePosition(), targetPos)) * speedMouse / 20;

            if (step > 6)
                for (var i = 0; i < step; i++)
                {
                    var vector2 = Vector2.SmoothStep(Follower.Instance.GetMousePosition(), targetPos, i / step);
                    SetCursorPos((int)vector2.X, (int)vector2.Y);
                    yield return new WaitTime(5);
                }
            else
                SetCursorPos(targetPos);
        }

        /// <summary>
        /// Moves the mouse to a position and then left-clicks.
        /// </summary>
        public static IEnumerator SetCursorPosAndLeftClickHuman(Vector2 coords, int extraDelay)
        {
            yield return SetCursorPosHuman(coords);
            yield return new WaitTime(Follower.Instance.Settings.BotInputFrequency + extraDelay);
            LeftMouseDown();
            yield return new WaitTime(Follower.Instance.Settings.BotInputFrequency + extraDelay);
            LeftMouseUp();
            yield return new WaitTime(100);
        }
    }
}