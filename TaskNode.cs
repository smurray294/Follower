using ExileCore.PoEMemory.Elements;
using SharpDX;

namespace Follower
{
    public class TaskNode
    {

        public Vector3 WorldPosition { get; set; }
        public LabelOnGround LabelOnGround { get; set; } // Important for transitions
        public float Bounds { get; set; }
        public TaskNodeType Type { get; set; }
        public int AttemptCount { get; set; }

        // Constructor for world position tasks (movement, looting, waypoints)
        public TaskNode(Vector3 position, float bounds, TaskNodeType type = TaskNodeType.Movement)
        {
            WorldPosition = position;
            Bounds = bounds;
            Type = type;
            AttemptCount = 0;
            LabelOnGround = null;
        }

        // Constructor for label-based tasks (transitions)
        public TaskNode(LabelOnGround label, float bounds, TaskNodeType type)
        {
            LabelOnGround = label;
            WorldPosition = label.ItemOnGround.Pos; // Store world position as a fallback
            Bounds = bounds;
            Type = type;
            AttemptCount = 0;
        }
    }

    public enum TaskNodeType
    {
        Movement,
        Transition,
        Loot,
        ClaimWaypoint
    }
}