using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Windows.Forms;

namespace Follower;

public class FollowerSettings : ISettings
{
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public ToggleNode IsFollowEnabled { get; set; } = new ToggleNode(false);
    [Menu("Toggle Follower")] public HotkeyNode ToggleFollower { get; set; } = Keys.PageUp;
    [Menu("Min Path Distance")] public RangeNode<int> PathfindingNodeDistance { get; set; } = new RangeNode<int>(200, 10, 1000);
    [Menu("Move CMD Frequency")] public RangeNode<int> BotInputFrequency { get; set; } = new RangeNode<int>(50, 10, 250);
    [Menu("Clear Path Distance")] public RangeNode<int> ClearPathDistance { get; set; } = new RangeNode<int>(500, 100, 5000);
    [Menu("Random Click Offset")] public RangeNode<int> RandomClickOffset { get; set; } = new RangeNode<int>(10, 1, 100);
    [Menu("Follow Target Name")] public TextNode LeaderName { get; set; } = new TextNode("");
    [Menu("Movement Key")] public HotkeyNode MovementKey { get; set; } = Keys.T;
    [Menu("Allow Dash")] public ToggleNode IsDashEnabled { get; set; } = new ToggleNode(true);
    [Menu("Dash Key")] public HotkeyNode DashKey { get; set; } = Keys.W;
    [Menu("Follow Close")] public ToggleNode IsCloseFollowEnabled { get; set; } = new ToggleNode(false);

    [Menu("Auto Level Gems")] public ToggleNode AutoLevelGems { get; set; } = new ToggleNode(true);

    [Menu("CryBot")] public ToggleNode CryBot { get; set; } = new ToggleNode(false);
    [Menu("ManaGuardian")] public ToggleNode ManaGuardian { get; set; } = new ToggleNode(false);
    [Menu("Culler")] public ToggleNode Culler { get; set; } = new ToggleNode(false);
    [Menu("Aurabot")] public ToggleNode Aurabot { get; set; } = new ToggleNode(false);
    [Menu("Druggery")] public ToggleNode Druggery { get; set; } = new ToggleNode(false);

// Add these to a new "Debug" section in your settings menu if you like.
    [Menu("Debug: Show Range Visualizer", "Draws a circle around the player to show a specific range.", 1000)]
    public ToggleNode ShowRangeVisualizer { get; set; } = new ToggleNode(false);

    [Menu("Debug: Visualizer Range", "The radius of the debug circle.", 1001, 1000)]
    public RangeNode<int> VisualizerRange { get; set; } = new RangeNode<int>(70, 10, 2000);
}