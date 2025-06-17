using SharpDX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
namespace Follower
{
    public class Follower : BaseSettingsPlugin<FollowerSettings>
    {
        public static Follower Instance { get; private set; }
        private readonly Random random = new Random();
        private Camera Camera => GameController.Game.IngameState.Camera;

        // --- State Variables ---
        private Vector3 _lastTargetPosition;
        private Vector3 _lastPlayerPosition;
        private Entity _followTarget;
        private List<TaskNode> _tasks = new List<TaskNode>();
        private bool _hasUsedWp;

        private int _numRows, _numCols;
        private byte[,] _tiles;

        // --- Coroutine and Command System ---
        private Coroutine _botCoroutine;
        private Thread _clientThread;
        private volatile string _receivedCommand = null;



        public override bool Initialise()
        {
            Instance = this;
            Name = "Follower";

            // Register hotkeys from settings
            Input.RegisterKey(Settings.MovementKey.Value);
            Input.RegisterKey(Settings.DashKey.Value);
            Input.RegisterKey(Settings.ToggleFollower.Value);
            Settings.ToggleFollower.OnValueChanged += () => { Input.RegisterKey(Settings.ToggleFollower.Value); };

            // Start the command client listener
            _clientThread = new Thread(StartClient);
            _clientThread.IsBackground = true;
            _clientThread.Start();

            return true;
        }

        public override void OnClose()
        {
            _clientThread?.Interrupt();
            _botCoroutine?.Done();
        }

        public override Job Tick()
        {
            // The Tick method is now only responsible for starting/stopping the coroutine and processing commands.

            ProcessCommands(); // Check for and process remote commands

            if (Settings.ToggleFollower.PressedOnce())
            {
                Settings.IsFollowEnabled.SetValueNoEvent(!Settings.IsFollowEnabled.Value);
            }

            if (Settings.IsFollowEnabled && (_botCoroutine == null || _botCoroutine.IsDone))
            {
                LogMessage("Starting Bot Coroutine...", 3, SharpDX.Color.LawnGreen);
                ResetState();
                _botCoroutine = new Coroutine(BotLogic(), this, "FollowerBot");
                Core.ParallelRunner.Run(_botCoroutine);
            }

            if (!Settings.IsFollowEnabled && _botCoroutine != null && !_botCoroutine.IsDone)
            {
                LogMessage("Stopping Bot Coroutine...", 3, SharpDX.Color.Red);
                _botCoroutine.Done();
            }

            return null; // The main logic is no longer here.
        }

        private IEnumerator BotLogic()
        {
            while (true)
            {
                // Basic checks and pause
                if (!GameController.Player.IsAlive || GameController.IsLoading)
                {
                    yield return new WaitTime(250); // Longer wait if loading/dead
                    continue;
                }

                // --- Priority #1: Check if a transition just succeeded ---
                var playerDistanceMoved = Vector3.Distance(GameController.Player.Pos, _lastPlayerPosition);
                if (_tasks.Any() && _tasks.First().Type == TaskNodeType.Transition && playerDistanceMoved > 150)
                {
                    LogMessage("Transition successful, resetting state.", 3, SharpDX.Color.LawnGreen);
                    ResetState(); 
                    yield return new WaitTime(500); // Grace period after loading
                    continue;
                }

                // --- Priority #2: Decision Making (Path Building) ---
                _followTarget = GetFollowingTarget();
                var leaderPartyElement = GetLeaderPartyElement();

                // Case 1: Leader is in another zone
                if (_followTarget == null && leaderPartyElement != null && leaderPartyElement.ZoneName != GameController.Game.IngameState.Data.CurrentArea.Name)
                {
                    if (!_tasks.Any()) // Only act if we have no current task
                    {
                        var portalLabel = GetBestPortalToFollow(_lastTargetPosition);
                        if (portalLabel != null)
                        {
                            _tasks.Add(new TaskNode(portalLabel, 200, TaskNodeType.Transition));
                        }
                        else
                        {
                            // Your TP logic can go here as a fallback
                        }
                    }
                }
                // Case 2: Leader is in the same zone
                else if (_followTarget != null)
                {
                    var leaderPos = _followTarget.Pos;
                    var distanceFromLeader = Vector3.Distance(GameController.Player.Pos, leaderPos);

                    if (distanceFromLeader >= Settings.ClearPathDistance) // Far away: Build path
                    {
                        if (!_tasks.Any() || Vector3.Distance(_tasks.Last().WorldPosition, leaderPos) >= Settings.PathfindingNodeDistance)
                        {
                            _tasks.Add(new TaskNode(leaderPos, Settings.PathfindingNodeDistance));
                        }
                    }
                    else // Close: Clear movement tasks and look for other things to do
                    {
                        _tasks.RemoveAll(t => t.Type == TaskNodeType.Movement || t.Type == TaskNodeType.Transition);
                        // Future: Add logic for looting, waypoints, etc. here if _tasks is empty
                    }
                    _lastTargetPosition = leaderPos;
                }

                // --- Priority #3: Task Execution ---
                if (_tasks.Any())
                {
                    var currentTask = _tasks.First();
                    if (currentTask.AttemptCount > 6) { _tasks.RemoveAt(0); continue; }
                    currentTask.AttemptCount++;

                    float distanceToTask = Vector3.Distance(GameController.Player.Pos, currentTask.WorldPosition);

                    switch (currentTask.Type)
                    {
                        case TaskNodeType.Movement:
                            if (distanceToTask <= currentTask.Bounds)
                            {
                                _tasks.RemoveAt(0);
                                continue;
                            }
                            Input.KeyDown(Settings.MovementKey);
                            yield return Mouse.SetCursorPosHuman(WorldToValidScreenPosition(currentTask.WorldPosition));
                            break;

                        case TaskNodeType.Transition:
                            Input.KeyUp(Settings.MovementKey); // Stop moving before clicking
                            yield return new WaitTime(150);
                            yield return Mouse.SetCursorPosAndLeftClickHuman(currentTask.LabelOnGround.Label.GetClientRect().Center, 100);
                            // DO NOT wait here. Let the check at the top of the loop handle success.
                            yield return new WaitTime(500); // Just a small delay before we check again
                            break;
                    }
                }

                // Final housekeeping for the loop
                _lastPlayerPosition = GameController.Player.Pos;
                yield return new WaitTime(100); // Standard delay for each full logic cycle
            }
        }

        #region Helper and System Methods
        // --- Your existing helper methods (GetFollowingTarget, GetLeaderPartyElement, etc.) go here ---
        // --- They do not need to be changed. ---

        // Your existing Render method goes here, no changes needed.

        // --- Command System Logic ---
        private void ProcessCommands()
        {
            if (_receivedCommand != null)
            {
                string commandToProcess = _receivedCommand;
                _receivedCommand = null;

                if (commandToProcess == "SET_FOLLOW_OFF" && Settings.IsFollowEnabled)
                {
                    Settings.IsFollowEnabled.SetValueNoEvent(false);
                }
                else if (commandToProcess == "SET_FOLLOW_ON" && !Settings.IsFollowEnabled)
                {
                    Settings.IsFollowEnabled.SetValueNoEvent(true);
                }
            }
        }

        // --- (Paste all your other helper methods and Render here) ---

        public bool IsInLabyrinth()
        {
            // Use the full, direct path that you have now 100% confirmed
            var currentArea = GameController.Game.IngameState.Data.CurrentArea;

            // Safety check in case the object is momentarily null
            if (currentArea == null)
            {
                return false;
            }

            // Access the Id from that object
            string areaId = currentArea.Id;
            if (string.IsNullOrEmpty(areaId))
            {
                return false;
            }

            // Perform the check that we know is correct
            return areaId.Contains("Labyrinth", StringComparison.OrdinalIgnoreCase);
        }

        private LabelOnGround GetBestPortalToFollow(Vector3? targetPos = null)
        {
            try
            {
                var portalLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabels
                    .Where(x => x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.ItemOnGround != null &&
                                (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal") || x.ItemOnGround.Metadata.ToLower().Contains("woodsentrancetransition")))
                    .ToList();

                if (IsInLabyrinth())
                {
                    portalLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabels
                        .Where(x => x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.ItemOnGround != null)
                        .ToList();
                }

                if (targetPos.HasValue)
                {
                    return portalLabels.OrderBy(x => Vector3.Distance(targetPos.Value, x.ItemOnGround.Pos)).FirstOrDefault();
                }
                return portalLabels.OrderBy(x => x.ItemOnGround.DistancePlayer).FirstOrDefault();
            }
            catch { return null; }
        }

        private Vector2 GetTpButton(PartyElementWindow leaderPartyElement)
        {
            try
            {
                if (leaderPartyElement == null || leaderPartyElement.TpButton == null || !leaderPartyElement.TpButton.IsVisible)
                    return Vector2.Zero;

                var windowOffset = GameController.Window.GetWindowRectangle().TopLeft;
                var elemCenter = leaderPartyElement.TpButton.GetClientRectCache.Center;
                return new Vector2(elemCenter.X + windowOffset.X, elemCenter.Y + windowOffset.Y);
            }
            catch { return Vector2.Zero; }
        }

        private Element GetTpConfirmation()
        {
            try
            {
                var popUp = GameController.IngameState.IngameUi.PopUpWindow;
                if (popUp != null && popUp.IsVisible && popUp.Children[0].Children[0].Children[0].Text
                    .Contains("teleport to this player's location"))
                    return popUp.Children[0].Children[0].Children[3].Children[0]; //This is the "OK" button
                return null;
            }
            catch { return null; }
        }

        private bool CheckDashTerrain(Vector2 targetPosition)
        {
            if (_tiles == null) return false;

            var playerGridPos = GameController.Player.GridPos;
            var dir = Vector2.Normalize(targetPosition - playerGridPos);

            var distanceBeforeWall = 0;
            var distanceInWall = 0;
            var shouldDash = false;
            var points = new HashSet<System.Drawing.Point>();

            for (var i = 0; i < 500; i++) // Check up to 500 units away
            {
                var currentGridPos = playerGridPos + i * dir;
                if (Vector2.Distance(currentGridPos, targetPosition) < 2) break;

                var point = new System.Drawing.Point((int)currentGridPos.X, (int)currentGridPos.Y);
                if (points.Contains(point)) continue;
                points.Add(point);

                // Bounds check for the tiles array
                if (point.X < 0 || point.X >= _numCols || point.Y < 0 || point.Y >= _numRows)
                {
                    shouldDash = false;
                    break;
                }

                var tile = _tiles[point.X, point.Y];

                if (tile == 255) // Impassable wall
                {
                    shouldDash = false;
                    break;
                }
                else if (tile == 2) // Dashable obstacle
                {
                    if (shouldDash) distanceInWall++;
                    shouldDash = true;
                }
                else if (!shouldDash) // Walkable ground
                {
                    distanceBeforeWall++;
                    if (distanceBeforeWall > 10) break; // Don't dash if there's a long walkable path first
                }
            }

            if (distanceBeforeWall > 10 || distanceInWall < 5)
                shouldDash = false;

            if (shouldDash)
            {
                Mouse.SetCursorPos(WorldToValidScreenPosition(targetPosition.GridToWorld(GameController.Player.Pos.Z)));
                Thread.Sleep(50 + random.Next(Settings.BotInputFrequency));
                Input.KeyPress(Settings.DashKey.Value);
                return true;
            }

            return false;
        }

        private Entity GetFollowingTarget()
        {
            // Make a copy of the entity list to prevent errors if the list is modified while we are reading it.
            var players = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Player].ToList();
            var leaderNameLower = Settings.LeaderName.Value.ToLower();

            foreach (var player in players)
            {
                // Add multiple safety checks
                if (player == null || !player.IsValid) continue;

                var playerComponent = player.GetComponent<Player>();
                if (playerComponent == null) continue;

                var playerName = playerComponent.PlayerName;
                if (string.IsNullOrEmpty(playerName)) continue;

                if (playerName.ToLower() == leaderNameLower)
                {
                    // We found a match! Return it.
                    return player;
                }
            }

            // If we get through the whole list without a match, return null.
            return null;
        }

        private Entity GetLootableQuestItem()
        {
            try
            {
                return GameController.EntityListWrapper.Entities
                    .Where(e => e.Type == EntityType.WorldItem && e.IsTargetable && e.HasComponent<WorldItem>())
                    .FirstOrDefault(e =>
                    {
                        var itemEntity = e.GetComponent<WorldItem>().ItemEntity;
                        var baseItem = GameController.Files.BaseItemTypes.Translate(itemEntity.Path);
                        return baseItem != null && baseItem.ClassName == "QuestItem";
                    });
            }
            catch { return null; }
        }

        public override void Render()
        {
            if (!Settings.IsFollowEnabled) return;

            if (_tasks != null && _tasks.Count > 1)
            {
                for (var i = 1; i < _tasks.Count; i++)
                {
                    var start = WorldToValidScreenPosition(_tasks[i - 1].WorldPosition);
                    var end = WorldToValidScreenPosition(_tasks[i].WorldPosition);
                    Graphics.DrawLine(start, end, 2, SharpDX.Color.Pink);
                }
            }

            // In Follower.cs -> Render() method

            // In Follower.cs -> Render() method

            // --- START OF FINAL LABYRINTH DEBUG CODE ---

            // Default message in case something goes wrong
            string labStatusMessage = "Area data not found.";
            SharpDX.Color labStatusColor = SharpDX.Color.Red;

            // THIS IS THE FIX: Use the full, direct path you found in DevTree
            var currentArea = GameController.Game.IngameState.Data.CurrentArea;
            //(bool)CoPilot.Instance?.GameController?.Game?.IngameState?.Data?.CurrentWorldArea?.IsLabyrinthArea
            if (currentArea != null)
            {
                // Now, we can access the 'Id' property from this correct object
                string areaId = currentArea.Id;

                if (string.IsNullOrEmpty(areaId))
                {
                    labStatusMessage = "Area.Id property is null or empty!";
                    labStatusColor = SharpDX.Color.Red;
                }
                else
                {
                    // Check if the Id contains "Labyrinth"
                    bool isLab = areaId.Contains("Labyrinth", StringComparison.OrdinalIgnoreCase);

                    // Prepare the display message based on the result
                    labStatusMessage = $"IsLab (from correct path, ID: '{areaId}'): {isLab}";
                    labStatusColor = isLab ? SharpDX.Color.LawnGreen : SharpDX.Color.Orange;
                }
            }

            // Draw the final status to the screen
            Graphics.DrawText(labStatusMessage, new Vector2(100, 450), labStatusColor);

            // --- END OF FINAL LABYRINTH DEBUG CODE ---
            var leaderPartyElement = GetLeaderPartyElement(); // Get it once for rendering

            // Main status text
            var leaderStatus = "Not Found";
            if (_followTarget != null) leaderStatus = "Following in Area";
            else if (leaderPartyElement != null) leaderStatus = $"Found in Zone: {leaderPartyElement.ZoneName}";

            Graphics.DrawText($"Follower: {Settings.IsFollowEnabled.Value}", new Vector2(500, 120), SharpDX.Color.White);
            Graphics.DrawText($"Leader '{Settings.LeaderName.Value}': {leaderStatus}", new Vector2(500, 140), SharpDX.Color.LawnGreen);
            Graphics.DrawText($"Tasks: {_tasks.Count}", new Vector2(500, 160), SharpDX.Color.White);

            // --- START OF NEW DEBUG SECTION ---

            Graphics.DrawText("--- Condition Check for Zone Change ---", new Vector2(500, 200), SharpDX.Color.Yellow);

            // Check 1: Is the leader entity null?
            bool isFollowTargetNull = _followTarget == null;
            Graphics.DrawText($"1. _followTarget == null: {isFollowTargetNull}", new Vector2(500, 220), isFollowTargetNull ? SharpDX.Color.LawnGreen : SharpDX.Color.Red);

            // Check 2: Did we find the leader in the party UI?
            bool isLeaderPartyElementFound = leaderPartyElement != null;
            Graphics.DrawText($"2. leaderPartyElement != null: {isLeaderPartyElementFound}", new Vector2(500, 240), isLeaderPartyElementFound ? SharpDX.Color.LawnGreen : SharpDX.Color.Red);

            // Check 3: Are the zones different?
            bool areZonesDifferent = false;
            if (isLeaderPartyElementFound)
            {
                areZonesDifferent = leaderPartyElement.ZoneName != GameController.Area.CurrentArea.DisplayName;
                Graphics.DrawText($"3. Zones Different? (L: {leaderPartyElement.ZoneName} | F: {GameController.Area.CurrentArea.DisplayName}) -> {areZonesDifferent}", new Vector2(500, 260), areZonesDifferent ? SharpDX.Color.LawnGreen : SharpDX.Color.Red);
            }
            else
            {
                Graphics.DrawText("3. Zones Different?: (Can't check, party element not found)", new Vector2(500, 260), SharpDX.Color.Gray);
            }

            // Final Result of the IF statement
            bool finalCondition = isFollowTargetNull && isLeaderPartyElementFound && areZonesDifferent;
            Graphics.DrawText($"==> Overall Condition is: {finalCondition}", new Vector2(500, 280), finalCondition ? SharpDX.Color.LawnGreen : SharpDX.Color.Red);

            // --- END OF NEW DEBUG SECTION ---
        }

        private Vector2 WorldToValidScreenPosition(Vector3 worldPos)
        {
            var windowRect = GameController.Window.GetWindowRectangle();
            var screenPos = Camera.WorldToScreen(worldPos);
            var result = screenPos + windowRect.Location;

            var edgeBounds = 50;
            // Clamp the position to be within the window bounds, with a margin
            result.X = Math.Max(windowRect.Left + edgeBounds, Math.Min(result.X, windowRect.Right - edgeBounds));
            result.Y = Math.Max(windowRect.Top + edgeBounds, Math.Min(result.Y, windowRect.Bottom - edgeBounds));

            return result;
        }

        // MERGED: The original Follower had EntityAdded/Removed to cache transitions.
        // The new logic finds them on-the-fly, which is more robust, so these are no longer needed.
        public override void EntityAdded(Entity entity) { }
        public override void EntityRemoved(Entity entity) { }

        // MERGED: No longer need to generate a PNG of the map on every area change.
        public void GeneratePNG() { }


        private void StartClient()
        {
            while (true)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        LogMessage("Attempting to connect to command server...", 5);

                        // --- START OF FIX ---
                        // Replace "YOUR_HOST_PC_IP_ADDRESS" with the actual IP you found, e.g., "192.168.1.105"
                        client.Connect("192.168.188.87", 6969);
                        // --- END OF FIX ---
                        LogMessage("Connected to command server!", 5, SharpDX.Color.LawnGreen);

                        using (var stream = client.GetStream())
                        {
                            byte[] buffer = new byte[1024];
                            int bytesRead;
                            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                string command = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                                // Put the command in our "mailbox" for the main thread to process
                                _receivedCommand = command;
                                LogMessage($"Received command: '{command}'", 5, SharpDX.Color.Aqua);
                            }
                        }
                    }
                }
                catch { /* Connection failed or lost */ }

                LogMessage("Connection to command server lost. Retrying in 5 seconds...", 5, SharpDX.Color.Red);
                Thread.Sleep(5000);
            }
        }

        internal Vector2 GetMousePosition()
        {
            return new Vector2(GameController.IngameState.MousePosX, GameController.IngameState.MousePosY);
        }
        // need to find where corpses/enemys are defined in the new codebase
        // private int CountCorpsesAroundMouse(float maxDistance)
        // {
        //     return corpses.Count(x => Vector2.Distance(GameController.IngameState.Camera.WorldToScreen(x.Pos), GetMousePosition()) <= maxDistance);
        // }

        // private int CountEnemysAroundMouse(float maxDistance)
        // {
        //     return enemys.Count(x => Vector2.Distance(GameController.IngameState.Camera.WorldToScreen(x.Pos), GetMousePosition()) <= maxDistance);
        // }

        /// <summary>
        /// Clears all pathfinding values. Used on area transitions primarily.
        /// MERGED: Combined logic from both files.
        /// </summary>
        private void ResetState()
        {
            _tasks = new List<TaskNode>();
            _followTarget = null;
            _lastTargetPosition = Vector3.Zero;
            _lastPlayerPosition = Vector3.Zero;
            _hasUsedWp = false;
        }

        public override void AreaChange(AreaInstance area)
        {
            ResetState();

            // MERGED: This terrain generation logic is present in both files and is crucial for the dash check.
            // I've kept the implementation from Follower as it's clean and functional.
            var terrain = GameController.IngameState.Data.Terrain;
            var terrainBytes = GameController.Memory.ReadBytes(terrain.LayerMelee.First, terrain.LayerMelee.Size);
            _numCols = (int)(terrain.NumCols - 1) * 23;
            _numRows = (int)(terrain.NumRows - 1) * 23;
            if ((_numCols & 1) > 0)
                _numCols++;

            _tiles = new byte[_numCols, _numRows];
            int dataIndex = 0;
            for (int y = 0; y < _numRows; y++)
            {
                for (int x = 0; x < _numCols; x += 2)
                {
                    var b = terrainBytes[dataIndex + (x >> 1)];
                    _tiles[x, y] = (byte)((b & 0xf) > 0 ? 1 : 255);
                    _tiles[x + 1, y] = (byte)((b >> 4) > 0 ? 1 : 255);
                }
                dataIndex += terrain.BytesPerRow;
            }

            terrainBytes = GameController.Memory.ReadBytes(terrain.LayerRanged.First, terrain.LayerRanged.Size);
            dataIndex = 0;
            for (int y = 0; y < _numRows; y++)
            {
                for (int x = 0; x < _numCols; x += 2)
                {
                    var b = terrainBytes[dataIndex + (x >> 1)];
                    var current = _tiles[x, y];
                    if (current == 255)
                        _tiles[x, y] = (byte)((b & 0xf) > 3 ? 2 : 255);
                    current = _tiles[x + 1, y];
                    if (current == 255)
                        _tiles[x + 1, y] = (byte)((b >> 4) > 3 ? 2 : 255);
                }
                dataIndex += terrain.BytesPerRow;
            }
        }

        private PartyElementWindow GetLeaderPartyElement()
        {
            try
            {
                foreach (var partyElementWindow in PartyElements.GetPlayerInfoElementList())
                {
                    if (string.Equals(partyElementWindow?.PlayerName?.ToLower(), Follower.Instance.Settings.LeaderName.Value.ToLower(), StringComparison.CurrentCultureIgnoreCase))
                    {
                        return partyElementWindow;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        // MERGED: This helper function to mouse over items is useful for looting.
        private void MouseoverItem(Entity item)
        {
            var uiLoot = GameController.IngameState.IngameUi.ItemsOnGroundLabels.FirstOrDefault(I => I.IsVisible && I.ItemOnGround.Id == item.Id);
            if (uiLoot != null)
            {
                var clickPos = uiLoot.Label.GetClientRect().Center;
                Mouse.SetCursorPos(new Vector2(
                    clickPos.X + random.Next(-15, 15),
                    clickPos.Y + random.Next(-10, 10)));
                Thread.Sleep(30 + random.Next(Settings.BotInputFrequency));
            }
        }

        #endregion
    }
}