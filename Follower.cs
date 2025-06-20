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
using System.Windows.Forms;
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
        private volatile bool _mercClickRequested = false;
        private int _numRows, _numCols;
        private byte[,] _tiles;
        private Vector3 _skillTargetPosition; // <-- ADD THIS LINE

        private bool _isHandlingUltimatum = false;


        internal DateTime lastTimeAny;

        private readonly List<Skill> _skills = new List<Skill>();

        // --- Coroutine and Command System ---
        private Coroutine _botCoroutine;
        private Thread _clientThread;
        private volatile string _receivedCommand = null;
        private volatile bool _lootRequested = false;
        private volatile bool _acceptInviteRequested = false;

        private volatile bool _goToHideoutRequested = false;



        volatile bool _transitionRequested = false;

        private bool _isLevelingGem = false;


        private const int Delay = 75;

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

            // --- NEW: Define our skills here ---

            if (Settings.CryBot)
            {
                _skills.Add(new Skill
                {
                    Name = "Enduring Cry",
                    Key = Keys.Q,      // Change this to whatever key you use for Enduring Cry
                    Cooldown = Settings.WarcryCooldown.Value,   // The 4-second cooldown you mentioned
                    UseMode = SkillUseMode.OnCooldownInRange
                });

                _skills.Add(new Skill
                {
                    Name = "Ancestral Cry",
                    Key = Keys.W,      // Change this to whatever key you use for Enduring Cry
                    Cooldown = Settings.WarcryCooldown.Value,  // The 4-second cooldown you mentioned
                    UseMode = SkillUseMode.OnCooldownInRange
                });

                _skills.Add(new Skill
                {
                    Name = "Battlemage's Cry",
                    Key = Keys.R,      // Change this to whatever key you use for Enduring Cry
                    Cooldown = Settings.WarcryCooldown.Value,   // The 4-second cooldown you mentioned
                    UseMode = SkillUseMode.OnCooldownInRange
                });

                _skills.Add(new Skill
                {
                    Name = "Intimidating Cry",
                    Key = Keys.A,      // Change this to whatever key you use for Enduring Cry
                    Cooldown = Settings.WarcryCooldown.Value,   // The 4-second cooldown you mentioned
                    UseMode = SkillUseMode.OnCooldownInRange
                });

                _skills.Add(new Skill
                {
                    Name = "Seismic Cry",
                    Key = Keys.F,      // Change this to whatever key you use for Enduring Cry
                    Cooldown = Settings.WarcryCooldown.Value,   // The 4-second cooldown you mentioned
                    UseMode = SkillUseMode.OnCooldownInRange
                });

                _skills.Add(new Skill
                {
                    Name = "Warcry 6",
                    Key = Keys.D,      // Change this to whatever key you use for Enduring Cry
                    Cooldown = Settings.WarcryCooldown.Value,   // The 4-second cooldown you mentioned
                    UseMode = SkillUseMode.OnCooldownInRange
                });
            }

            if (Settings.ManaGuardian)
            {
                _skills.Add(new Skill
                {
                    Name = "Mine",
                    Key = Keys.Q,
                    Cooldown = 0.8f,
                    UseMode = SkillUseMode.OffensiveTargetedAttack,
                    // We don't need to set HPPThreshold or ESPThreshold for this mode
                });
            }

            if (Settings.Druggery)
            {
                _skills.Add(new Skill
                {
                    Name = "Mine",
                    Key = Keys.Q,
                    Cooldown = 0.4f,
                    UseMode = SkillUseMode.OffensiveTargetedAttack,
                    // We don't need to set HPPThreshold or ESPThreshold for this mode
                });
            }

            if (Settings.Aurabot)
            {
                _skills.Add(new Skill
                {
                    Name = "Mine",
                    Key = Keys.Q,
                    Cooldown = 2.5f,
                    UseMode = SkillUseMode.OffensiveTargetedAttack,
                    // We don't need to set HPPThreshold or ESPThreshold for this mode
                });

                _skills.Add(new Skill
                {
                    Name = "Smite",
                    Key = Keys.W,
                    Cooldown = 3f,
                    UseMode = SkillUseMode.OnCooldownInRange
                    // We don't need to set HPPThreshold or ESPThreshold for this mode
                });
            }


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
                Input.KeyUp(Settings.MovementKey);
            }

            if (IsUltimatumWindowOpen() && !_isHandlingUltimatum)
            {
                // We've detected the window. Start the handling process.
                var ultimatumCoroutine = new Coroutine(HandleUltimatum(), this, "HandleUltimatumAction");
                Core.ParallelRunner.Run(ultimatumCoroutine);
            }

            if (Settings.AutoLevelGems.Value && !_isLevelingGem)
            {
                var gemsToLvlUpElements = GetLevelableGems();
                if (gemsToLvlUpElements.Any())
                {
                    var elementToClick = gemsToLvlUpElements.FirstOrDefault()?.GetChildAtIndex(1);
                    if (elementToClick != null)
                    {
                        var gemLevelCoroutine = new Coroutine(LevelUpGem(elementToClick), this, "LevelUpGemAction");
                        Core.ParallelRunner.Run(gemLevelCoroutine);
                    }
                }
            }

            // Skill usage

            if (Settings.IsFollowEnabled && _botCoroutine != null && !_botCoroutine.IsDone && GameController.Player.IsAlive)
            {

                // --- NEW: The Area Check Gatekeeper ---
                var currentArea = GameController.Area.CurrentArea;
                var localFollowTarget = GetFollowingTarget();

                // 1. Check if it's a Town or Hideout. This is the fastest check.
                if (currentArea.IsTown || currentArea.IsHideout)
                {
                    return null; // Exit skill logic immediately
                }

                // 2. Check against our custom blacklist from settings.
                // var blockedZones = Settings.DisabledSkillZones.Value.Split(',')
                //                             .Select(s => s.Trim()) // Trim whitespace for safety
                //                             .ToList();

                // if (blockedZones.Contains(currentArea.Name, StringComparer.OrdinalIgnoreCase))
                // {
                //     return null; // This zone is on our blacklist, exit skill logic.
                // }

                // --- End of New Gatekeeper Logic ---

                // This prevents casting while moving, looting, or doing another animation.
                var actor = GameController.Player.GetComponent<Actor>();
                if (actor.Action.HasFlag(ActionFlags.UsingAbility))
                {
                    return null; // Don't try to cast if we're already busy.
                }

                // Go through our list of skills in order of priority.
                foreach (var skill in _skills)
                {
                    // --- The Checklist ---

                    // 1. Is the skill on cooldown?
                    if (DateTime.Now < skill.NextUseTime)
                    {
                        continue; // Skip to the next skill.
                    }

                    // 2. Is the leader visible and are we close enough? (For War Cry)
                    if (localFollowTarget == null || Vector3.Distance(GameController.Player.Pos, localFollowTarget.Pos) > 300)
                    {
                        // We're either too far away or can't see the leader.
                        continue; // Skip to the next skill.
                    }

                    if (!Gcd())
                    {
                        // If we are still in the global cooldown, skip this skill.
                        continue; // Skip to the next skill.
                    }

                    bool conditionsMet = false;
                    switch (skill.UseMode)
                    {
                        case SkillUseMode.OnCooldownInRange:
                            conditionsMet = true;
                            break;

                        case SkillUseMode.OffensiveTargetedAttack:

                            if (Vector3.Distance(GameController.Player.Pos, localFollowTarget.Pos) > 250)
                            {
                                continue;
                            }
                            // Call our new helper function to find the best target.
                                var target = GetBestOffensiveTarget(skill);

                            // If the helper found a valid target, then conditions are met.
                            if (target != null)
                            {
                                _skillTargetPosition = target.Pos; // Store its position for aiming
                                conditionsMet = true;
                            }
                            break;
                    }

                    if (conditionsMet)
                    {
                        LogMessage($"Casting skill '{skill.Name}' (Rule: {skill.UseMode})", 3, SharpDX.Color.LawnGreen);

                        // --- AIMING LOGIC for our new offensive mode ---
                        if (skill.UseMode == SkillUseMode.OffensiveTargetedAttack)
                        {
                            var targetScreenPos = Camera.WorldToScreen(_skillTargetPosition);

                            // Aim directly at the target's screen position
                            Mouse.SetCursorPos(targetScreenPos);
                        }

                        Keyboard.KeyPress(skill.Key);
                        skill.NextUseTime = DateTime.Now.AddSeconds(skill.Cooldown);
                        break;
                    }

                    // IMPORTANT: We only cast ONE skill per tick.
                    // Break the loop so we can re-evaluate priorities on the next frame.

                }
            }

            return null; // The main logic is no longer here.
        }

        private void GetAllChildrenRecursive(Element element, List<Element> allChildren)
        {
            if (element.Children == null)
                return;

            foreach (var child in element.Children)
            {
                allChildren.Add(child);
                // This is the recursion: call the function on the child element
                GetAllChildrenRecursive(child, allChildren);
            }
        }
        private IEnumerator ClickMercButton()
        {
            // This is the common path for all mercenaries. We will match anything that starts with this.
            const string MercenaryBasePath = "Metadata/Monsters/Mercenaries/Mercenary";

            // --- THIS IS THE CRITICAL CHANGE ---
            // We now use StartsWith() to find any monster that fits the pattern.
            var mercenaryMonster = GameController.EntityListWrapper.Entities
                                                .FirstOrDefault(m =>
                                                        m.Type == EntityType.Monster &&
                                                        m.IsAlive &&
                                                        m.Metadata.StartsWith(MercenaryBasePath, StringComparison.Ordinal));

            if (mercenaryMonster == null)
            {
                LogError("Could not find any mercenary monster entity in the area.", 5);
                yield break;
            }

            LogMessage($"Found mercenary: {mercenaryMonster.Metadata}", 3);

            // The rest of the logic remains the same...
            var monsterScreenPos = Camera.WorldToScreen(mercenaryMonster.Pos);
            var allUiElements = new List<Element>();
            GetAllChildrenRecursive(GameController.Game.IngameState.IngameUi, allUiElements);

            const float searchRadius = 200f;
            var optInButton = allUiElements.FirstOrDefault(e =>
                                    e.IsVisible &&
                                    e.Text != null &&
                                    e.Text.Equals("Opt-In", StringComparison.OrdinalIgnoreCase) &&
                                    Vector2.Distance(e.GetClientRect().Center, monsterScreenPos) < searchRadius);

            if (optInButton != null)
            {
                LogMessage("Found Opt-In button! Clicking...", 3, SharpDX.Color.LawnGreen);
                var buttonPos = optInButton.GetClientRect().Center;

                yield return Mouse.SetCursorPosHuman(buttonPos, false);
                yield return new WaitTime(50);
                yield return Mouse.LeftClick();
                yield return new WaitTime(200);
            }
            else
            {
                LogError("Found the monster, but could not find a matching 'Opt-In' UI element near its location.", 5);
            }
        }

        private IEnumerator BotLogic()
        {
            while (true) // The main loop will now run very fast
            {
                // --- High-Priority Checks (Run Every Pass) ---
                if (!GameController.Player.IsAlive || GameController.IsLoading)
                {
                    ResetState();
                    yield return new WaitTime(500); // Wait if we're in a state where we can't act
                    continue;
                }

                if (_acceptInviteRequested)
                {
                    _acceptInviteRequested = false;
                    yield return new WaitTime(1000);
                    yield return HandleAcceptInvite();
                    continue;
                }

                // --- ADD THIS NEW BLOCK FOR THE MERCENARY ---
                if (_mercClickRequested)
                {
                    _mercClickRequested = false; // Reset the flag immediately
                    yield return ClickMercButton();
                    continue; // Action taken, restart the loop to re-evaluate everything
                }

                if (_lootRequested)
                {
                    _lootRequested = false; // Reset the flag immediately to prevent re-triggering

                    // Pause the main follow logic and hand over control to the looting coroutine.
                    yield return HandleLooting();

                    // After looting is finished, restart the main loop from the top
                    // to re-evaluate the game state.
                    continue;
                }

                if (_transitionRequested)
                {
                    _transitionRequested = false;
                    yield return HandleTakeNearestTransition();
                    continue;
                }

                if (_goToHideoutRequested)
                {
                    _goToHideoutRequested = false;
                    yield return HandleGoToHideout();
                    continue;
                }

                var playerDistanceMoved = Vector3.Distance(GameController.Player.Pos, _lastPlayerPosition);
                if (_tasks.Any() && _tasks.First().Type == TaskNodeType.Transition && playerDistanceMoved > 150)
                {
                    // Transition success!
                    ResetState();
                    yield return new WaitTime(500); // Grace period after loading in
                    continue;
                }

                // --- Decision Making (Path Building) ---
                // This logic is mostly fine, we just let it run without extra delays.
                _followTarget = GetFollowingTarget();
                var leaderPartyElement = GetLeaderPartyElement();
                // (Your existing logic for creating _tasks when leader is far/near goes here. It doesn't need yields.)
                if (_followTarget == null && leaderPartyElement != null && leaderPartyElement.ZoneName != GameController.Area.CurrentArea.DisplayName && leaderPartyElement.ZoneName != null)
                {
                    // if (!_tasks.Any()) 
                    // {
                    //     var portalLabel = GetBestPortalToFollow(leaderPartyElement);
                    //     if (portalLabel != null)
                    //     {
                    //         _tasks.Add(new TaskNode(portalLabel, 200, TaskNodeType.Transition));
                    //     }
                    // }

                    var portal = GetBestPortalToFollow(leaderPartyElement);
                    if (portal != null && !IsInLabyrinth() && (bool)Instance?.GameController?.Area?.CurrentArea?.IsHideout)
                    {
                        // hideout -> Map || Chamber of Sins A7 -> Map
                        _tasks.Add(new TaskNode(portal, 200, TaskNodeType.Transition));
                    }
                    else if (IsInLabyrinth())
                    {
                        // Labyrinth transition
                        var transition = GetBestPortalToFollow(leaderPartyElement);
                        if (transition != null && transition.ItemOnGround.DistancePlayer < 100)
                        {
                            _tasks.Add(new TaskNode(transition, 200, TaskNodeType.Transition));
                        }
                    }
                    else
                    {
                        // tp?
                        var tpButton = GetTpButton(leaderPartyElement);
                        if (!tpButton.Equals(Vector2.Zero))
                        {
                            yield return Mouse.SetCursorPosHuman(tpButton, false);
                            yield return new WaitTime(200);
                            yield return Mouse.LeftClick();
                            yield return new WaitTime(200);
                        }

                        var tpConfirmation = GetTpConfirmation();
                        if (tpConfirmation != null)
                        {
                            yield return Mouse.SetCursorPosHuman(tpConfirmation.GetClientRect().Center);
                            yield return new WaitTime(200);
                            yield return Mouse.LeftClick();
                            yield return new WaitTime(1000);
                        }
                    }


                }
                else if (_followTarget != null)
                {
                    var leaderPos = _followTarget.Pos;
                    var distanceFromLeader = Vector3.Distance(GameController.Player.Pos, leaderPos);
                    var distanceMoved = Vector3.Distance(_lastTargetPosition, leaderPos);


                    if (distanceFromLeader >= Settings.ClearPathDistance)
                    {
                        playerDistanceMoved = Vector3.Distance(GameController.Player.Pos, _lastPlayerPosition);
                        if (distanceMoved > Settings.ClearPathDistance)
                        {
                            var transition2 = GetBestPortalToFollow(leaderPartyElement);
                            if (transition2 != null && transition2.ItemOnGround.DistancePlayer < 300)
                            {
                                _tasks.Add(new TaskNode(transition2, 200, TaskNodeType.Transition));
                            }
                        }
                        // we have no path, set us to go to leader pos
                        else if (_tasks.Count == 0 && distanceMoved < 2000 && distanceFromLeader > 200 && distanceFromLeader < 2000)

                        {
                            // Add a movement task to the leader's position
                            _tasks.Add(new TaskNode(leaderPos, Settings.PathfindingNodeDistance));
                        }

                        else if (_tasks.Count > 0)
                        {
                            var distanceToTask = Vector3.Distance(GameController.Player.Pos, _tasks.Last().WorldPosition);
                            if (distanceToTask >= Settings.PathfindingNodeDistance)
                            {
                                // If the last task is too far, we can remove it
                                _tasks.Add(new TaskNode(leaderPos, Settings.PathfindingNodeDistance));
                            }
                        }

                        //leader is far and we have no _tasks, stuck at a transition?
                        else if (_tasks.Count == 0 && distanceFromLeader > 2000)
                        {
                            var transition3 = GetBestPortalToFollow(leaderPartyElement);
                            if (transition3 != null && transition3.ItemOnGround.DistancePlayer < 500)
                            {
                                _tasks.Add(new TaskNode(transition3, 200, TaskNodeType.Transition));
                            }
                        }
                    }
                    else
                    {
                        if (_tasks.Count > 0)
                        {
                            for (var i = _tasks.Count - 1; i >= 0; i--)
                                if (_tasks[i].Type == TaskNodeType.Movement || _tasks[i].Type == TaskNodeType.Transition)
                                    _tasks.RemoveAt(i);
                            yield return null;
                        }
                        if (Settings.IsCloseFollowEnabled)
                        {
                            if (distanceFromLeader >= Settings.PathfindingNodeDistance)
                            {
                                // If we are too far from the leader, add a movement task to their position
                                _tasks.Add(new TaskNode(leaderPos, Settings.PathfindingNodeDistance));
                            }
                        }
                    }
                    // if (distanceFromLeader >= Settings.ClearPathDistance)
                    // {
                    //     if (!_tasks.Any() || Vector3.Distance(_tasks.Last().WorldPosition, leaderPos) >= Settings.PathfindingNodeDistance)
                    //     {
                    //         _tasks.Add(new TaskNode(leaderPos, Settings.PathfindingNodeDistance));
                    //     }
                    // }
                    // else 
                    // {
                    //     _tasks.RemoveAll(t => t.Type == TaskNodeType.Movement || t.Type == TaskNodeType.Transition);
                    // }
                    if (leaderPos != null)
                        _lastTargetPosition = leaderPos;
                }


                // --- Task Execution ---
                if (_tasks.Any())
                {
                    var currentTask = _tasks.First();
                    if (currentTask.AttemptCount > 6) { _tasks.RemoveAt(0); continue; }
                    currentTask.AttemptCount++;

                    float distanceToTask = Vector3.Distance(GameController.Player.Pos, currentTask.WorldPosition);
                    playerDistanceMoved = Vector3.Distance(GameController.Player.Pos, _lastPlayerPosition);

                    //We are using a same map transition and have moved significnatly since last tick. Mark the transition task as done.
                    if (currentTask.Type == TaskNodeType.Transition &&
                        playerDistanceMoved >= Settings.ClearPathDistance)
                    {
                        _tasks.RemoveAt(0);
                        _lastPlayerPosition = GameController.Player.Pos;
                        yield return null;
                        continue;
                    }

                    switch (currentTask.Type)
                    {
                        case TaskNodeType.Movement:

                            if (Settings.IsDashEnabled && CheckDashTerrain(currentTask.WorldPosition.WorldToGrid()))
                                yield return null;
                            yield return Mouse.SetCursorPosHuman(WorldToValidScreenPosition(currentTask.WorldPosition));
                            yield return new WaitTime(random.Next(25) + 30);
                            Input.KeyDown(Settings.MovementKey);
                            yield return new WaitTime(random.Next(25) + 30);
                            Input.KeyUp(Settings.MovementKey);

                            if (distanceToTask <= Settings.PathfindingNodeDistance * 1.5)
                            {
                                _tasks.RemoveAt(0);
                                continue; // Task done, immediately get the next one.
                            }
                            else
                            {
                                currentTask.AttemptCount++;
                                if (currentTask.AttemptCount > 5)
                                {
                                    var transition4 = GetBestPortalToFollow(leaderPartyElement);
                                    if (transition4 != null && transition4.ItemOnGround.DistancePlayer < 100)
                                    {
                                        _tasks.RemoveAt(0);
                                        _tasks.Add(new TaskNode(transition4, 200, TaskNodeType.Transition));
                                    }
                                }
                            }
                            yield return null;
                            yield return null;
                            continue;

                        case TaskNodeType.Transition:

                            // --- NEW VALIDATION STEP ---
                            // Before we act, is our target portal label still valid and visible?
                            if (currentTask.LabelOnGround == null || !currentTask.LabelOnGround.IsVisible)
                            {
                                LogMessage("Target portal is gone. Clearing task to find a new one.", 3, SharpDX.Color.Yellow);
                                _tasks.RemoveAt(0); // Invalidate the stale task
                                continue;           // Immediately restart the loop to find a new, valid portal
                            }

                            Input.KeyUp(Settings.MovementKey); // Stop moving
                            yield return new WaitTime(50); // Tiny pause to ensure we've stopped.
                            yield return Mouse.SetCursorPosAndLeftClickHuman(currentTask.LabelOnGround.Label.GetClientRect().Center, 100);
                            yield return new WaitTime(250); // A much shorter, non-blind wait.
                            // The logic at the top of the loop will handle success.
                            break;

                            currentTask.AttemptCount++;
                            if (currentTask.AttemptCount > 6)
                            {
                                while (_tasks?.Count > 0)
                                {
                                    _tasks.RemoveAt(0);
                                }
                                var transition2 = GetBestPortalToFollow(leaderPartyElement);
                                if (transition2 != null && transition2.ItemOnGround.DistancePlayer < 100)
                                {
                                    _tasks.Add(new TaskNode(transition2, 200, TaskNodeType.Transition));
                                }
                                yield return null;
                                continue;
                            }
                            else
                            {
                                yield return null;
                                continue;
                            }

                            // Add other task types (Loot, etc.) here
                    }
                }

                // --- Final Housekeeping ---
                _lastPlayerPosition = GameController.Player.Pos;

                // This is now the ONLY delay for a standard, active loop pass.
                // It defines the bot's "heartbeat". 50ms is very responsive.
                yield return new WaitTime(25);
            }
        }

        #region Helper and System Methods
        // --- Your existing helper methods (GetFollowingTarget, GetLeaderPartyElement, etc.) go here ---
        // --- They do not need to be changed. ---

        // Your existing Render method goes here, no changes needed.

        private IEnumerator HandleGoToHideout()
        {
            LogMessage("Go To Hideout command received. Looking for a portal...", 3, SharpDX.Color.LawnGreen);
            
            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // On every attempt, re-scan for the closest portal.
                var playerPos = GameController.Player.Pos;
                var portal = GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible
                    .Where(label => label.ItemOnGround.Metadata.ToLower().Contains("portal"))
                    .OrderBy(label => Vector3.Distance(playerPos, label.ItemOnGround.Pos)) // Use manual distance for reliability
                    .FirstOrDefault();

                if (portal == null)
                {
                    LogError($"Attempt #{attempt}: No portals found. Retrying in 1s...", 5);
                    yield return new WaitTime(1000);
                    continue; // Go to the next iteration of the for loop
                }

                // We found a portal, let's click it.
                LogMessage($"Attempt #{attempt}: Found portal. Clicking...", 3);
                yield return Mouse.SetCursorPosHuman(portal.Label.GetClientRect().Center, false);
                yield return Mouse.LeftClick();

                // Wait a significant time for the loading screen.
                yield return new WaitTime(3000);

                // --- VERIFY SUCCESS ---
                // The ultimate proof of success is that we are now in a hideout.
                if (GameController.Area.CurrentArea.IsHideout)
                {
                    LogMessage("Successfully transitioned to hideout!", 5, SharpDX.Color.LawnGreen);
                    yield break; // SUCCESS! Exit the entire coroutine.
                }
                else
                {
                    LogMessage($"Attempt #{attempt} failed. Still in map. Retrying...", 3, SharpDX.Color.Orange);
                }
            }

            LogError($"Failed to go to hideout after {maxAttempts} attempts.", 5);
        }

        private bool IsUltimatumWindowOpen()
        {
            // Use the direct path you found.
            return GameController.Game.IngameState.IngameUi.UltimatumPanel?.IsVisible == true;
        }

        private IEnumerator HandleUltimatum()
        {
            _isHandlingUltimatum = true;
            LogMessage("Ultimatum window detected. Waiting for leader's choice...", 5, SharpDX.Color.Magenta);
            Input.KeyUp(Settings.MovementKey);

            DateTime startTime = DateTime.Now;
            int leaderChoiceIndex = -1; // We will store the index (0, 1, or 2) of the leader's choice here.

            // --- WAIT/DETECT LOOP ---
            while (IsUltimatumWindowOpen() && leaderChoiceIndex == -1)
            {
                try
                {
                    // Use Path A to find the index of the locked choice.
                    var choiceDataElements = GameController.Game.IngameState.IngameUi.UltimatumPanel?.ChoicesPanel?.ChoiceElements;

                    if (choiceDataElements != null)
                    {
                        for (int i = 0; i < choiceDataElements.Count; i++)
                        {
                            var ultimatumChoiceData = choiceDataElements[i].AsObject<UltimatumChoiceElement>();
                            if (ultimatumChoiceData?.LockedVotes == 1)
                            {
                                leaderChoiceIndex = i; // We found it! The leader chose index i.
                                break;
                            }
                        }
                    }
                }
                catch (Exception e) { LogError($"Error checking Ultimatum choice: {e.Message}", 5); }

                if (leaderChoiceIndex != -1)
                {
                    LogMessage($"Detected leader's choice at index {leaderChoiceIndex}.", 3, SharpDX.Color.LawnGreen);
                    break; 
                }

                // // --- Timeout Failsafe ---
                // if ((DateTime.Now - startTime).TotalSeconds > Settings.UltimatumTimeout.Value)
                // {
                //     LogMessage("Leader choice timeout. Defaulting to first option (index 0).", 5, SharpDX.Color.Orange);
                //     leaderChoiceIndex = 0;
                //     break; 
                // }
                yield return new WaitTime(250);
            }

            if (leaderChoiceIndex == -1)
            {
                LogError("Could not determine which Ultimatum choice to make. Aborting.", 5);
                _isHandlingUltimatum = false;
                yield break;
            }

            // --- ACTION PHASE ---
            const int maxAttempts = 10; // A failsafe to prevent infinite loops
            for (int attempt = 0; attempt < maxAttempts && IsUltimatumWindowOpen(); attempt++)
            {
                LogMessage($"Ultimatum click attempt #{attempt + 1}", 3, SharpDX.Color.Aqua);
                
                // Find the clickable button based on the index we found.
                var clickableButton = GameController.Game.IngameState.IngameUi.UltimatumPanel?.ChoicesPanel?.GetChildFromIndices(0, leaderChoiceIndex);
                var confirmButton = GameController.Game.IngameState.IngameUi.UltimatumPanel?.ConfirmButton;

                if (clickableButton != null && clickableButton.IsVisible == true)
                {
                    // Click the choice
                    yield return Mouse.SetCursorPosHuman(clickableButton.GetClientRect().Center, false);
                    yield return new WaitTime(50);
                    yield return Mouse.LeftClick();
                    yield return new WaitTime(250);
                }
                else
                {
                    LogError($"Attempt #{attempt + 1}: Could not find the choice button at index {leaderChoiceIndex}. Retrying.", 5);
                }

                if (confirmButton != null && confirmButton.IsVisible == true)
                {
                    // Click confirm
                    yield return Mouse.SetCursorPosHuman(confirmButton.GetClientRect().Center, false);
                    yield return new WaitTime(50);
                    yield return Mouse.LeftClick();
                    yield return new WaitTime(250);
                }
                
                // Wait a moment for the game to process our clicks and potentially close the window.
                yield return new WaitTime(500);
            }

            // --- FINAL CLEANUP ---
            if (IsUltimatumWindowOpen())
            {
                LogError($"Ultimatum window still open after {maxAttempts} attempts. Giving up.", 5);
            }
            else
            {
                LogMessage("Ultimatum sequence complete. Window is closed.", 5, SharpDX.Color.Magenta);
            }

            _isHandlingUltimatum = false; // Reset the flag so we're ready for the next round.
        }
 
        private IEnumerator HandleAcceptInvite()
        {
            LogMessage("-> Entering HandleAcceptInvite...", 3, SharpDX.Color.Yellow);

            Element acceptButton = null;

            try
            {
                var invitesPanel = GameController.Game.IngameState.IngameUi.InvitesPanel;
                if (invitesPanel == null || invitesPanel.IsVisible == false)
                {
                    LogError("Debug: InvitesPanel is null or not visible. Exiting.", 5);
                    yield break;
                }
                LogMessage("Debug: Found visible InvitesPanel.", 3, SharpDX.Color.LawnGreen);


                var notification = invitesPanel.GetChildAtIndex(0);
                if (notification == null)
                {
                    LogError("Debug: Notification (Child 0) is null. Exiting.", 5);
                    yield break;
                }
                LogMessage("Debug: Found Notification (Child 0).", 3, SharpDX.Color.LawnGreen);


                var buttonContainer = notification.GetChildAtIndex(2);
                if (buttonContainer == null)
                {
                    LogError("Debug: Button Container (Child 0, 2) is null. Exiting.", 5);
                    yield break;
                }
                LogMessage("Debug: Found Button Container (Child 0, 2).", 3, SharpDX.Color.LawnGreen);


                acceptButton = buttonContainer.GetChildAtIndex(0);
                if (acceptButton == null)
                {
                    LogError("Debug: Accept Button (Child 0, 2, 0) is null. Exiting.", 5);
                    yield break;
                }
                LogMessage("Debug: Found Accept Button (Child 0, 2, 0).", 3, SharpDX.Color.LawnGreen);
            }
            catch (Exception e)
            {
                LogError($"An exception occurred while FINDING the button: {e.Message}", 5);
            }

            // --- Acting Phase ---
            if (acceptButton != null && acceptButton.IsVisible == true)
            {
                LogMessage("Button is valid and visible. Attempting to click.", 3, SharpDX.Color.Aqua);
                yield return Mouse.SetCursorPosHuman(acceptButton.GetClientRect().Center, false);
                yield return new WaitTime(75);
                yield return Mouse.LeftClick();
                yield return new WaitTime(500);
            }
            else
            {
                LogError("Final Check Failed: Accept button was null or not visible.", 5);
            }
        }

        private IEnumerator LevelUpGem(Element gemElementToClick)
        {
            // 1. Set our busy flag so Tick() doesn't try to start this again
            _isLevelingGem = true;

            LogMessage("Found gem to level. Initiating click sequence.", 3);

            // 2. Calculate delays (this part is fine)
            var actionDelay = 25 + GameController.IngameState.ServerData.Latency;
            var gemDelay = 25 + GameController.IngameState.ServerData.Latency;

            // 3. Translate the logic using 'yield return new WaitTime'
            Mouse.SetCursorPos(gemElementToClick.GetClientRect().Center);
            yield return new WaitTime(50);
            yield return Mouse.LeftClick();
            yield return new WaitTime(200);


            // 4. We are done, so reset the busy flag
            _isLevelingGem = false;
        }

        private List<Element> GetLevelableGems()
        {
            var gemsToLevelUp = new List<Element>();

            var possibleGemsToLvlUpElements = GameController.IngameState.IngameUi?.GemLvlUpPanel?.GemsToLvlUp;

            if (possibleGemsToLvlUpElements != null && possibleGemsToLvlUpElements.Any())
            {
                foreach (var possibleGemsToLvlUpElement in possibleGemsToLvlUpElements)
                {
                    foreach (var elem in possibleGemsToLvlUpElement.Children)
                    {
                        if (elem.Text != null && elem.Text.Contains("Click to level"))
                            gemsToLevelUp.Add(possibleGemsToLvlUpElement);
                    }
                }
            }

            return gemsToLevelUp;
        }

        private IEnumerator HandleTakeNearestTransition()
        {
            // --- STEP 1: Initial Sanity Check ---
            var leader = GetFollowingTarget();
            if (leader != null && leader.DistancePlayer < 200)
            {
                LogMessage("Already very close to leader. Ignoring transition command.", 3, SharpDX.Color.Yellow);
                yield break;
            }

            LogMessage("Take Nearest Transition command received. Beginning transition attempts...", 3, SharpDX.Color.Aqua);

            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                LogMessage($"Transition attempt {attempt}/{maxAttempts}...", 3);

                // --- STEP 2: Find the Nearest Portal (using manual distance calculation) ---
                
                // Get the player's current position once for this attempt.
                var playerPos = GameController.Player.Pos; 
                
                var nearestTransition = GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible
                    .Where(label => {
                        string metadata = label.ItemOnGround.Metadata.ToLower();
                        return metadata.Contains("areatransition") ||
                            metadata.Contains("portal") ||
                            metadata.Contains("woodsentrancetransition") ||
                            metadata.Contains("stairs") ||
                            metadata.Contains("ramp") ||
                            metadata.Contains("door") ||
                            metadata.Contains("entrance") ||
                            metadata.Contains("ladder") ||
                            metadata.Contains("arena");
                    })
                    // --- THIS IS THE FIX ---
                    // We now manually calculate the distance between the player and the transition.
                    .OrderBy(label => Vector3.Distance(playerPos, label.ItemOnGround.Pos))
                    .FirstOrDefault();

                // --- The rest of your logic remains exactly the same ---
                if (nearestTransition == null)
                {
                    LogError("Could not find any visible transitions to take.", 5);
                    yield return new WaitTime(500);
                    continue;
                }

                LogMessage($"Found portal '{nearestTransition.ItemOnGround.Metadata}' at distance {Vector3.Distance(playerPos, nearestTransition.ItemOnGround.Pos):F0}. Clicking...", 3, SharpDX.Color.LawnGreen);
                yield return Mouse.SetCursorPosHuman(nearestTransition.Label.GetClientRect().Center, false);
                yield return Mouse.LeftClick();

                yield return new WaitTime(500);

                leader = GetFollowingTarget();
                if (leader != null && leader.DistancePlayer < 150)
                {
                    LogMessage("Transition successful! Now close to leader.", 5, SharpDX.Color.LawnGreen);
                    yield break;
                }
                else
                {
                    LogMessage($"Transition attempt {attempt} failed.", 3, SharpDX.Color.Orange);
                }
            }

            LogError($"Failed to transition after {maxAttempts} attempts. Giving up.", 5);
        }

        private IEnumerator HandleLooting()
        {
            // Make sure we stop moving before we start looting.
            Input.KeyUp(Settings.MovementKey);
            yield return new WaitTime(100);

            LogMessage("Scanning for visible items to loot...", 3);

            // This is the range within which the bot will attempt to loot.
            const float LootRange = 50f;

            // Get a list of all VISIBLE item labels on the ground within loot range.
            // This relies on your custom, minimalist loot filter.
            var itemsToLoot = GameController.IngameState.IngameUi.ItemsOnGroundLabelsVisible
                                .Where(label => label.ItemOnGround.DistancePlayer < LootRange)
                                .OrderBy(label => label.ItemOnGround.DistancePlayer) // Loot closest first
                                .ToList();

            if (!itemsToLoot.Any())
            {
                LogMessage("No visible items found in range.", 3, SharpDX.Color.Yellow);
                yield break; // Exit the coroutine immediately
            }

            LogMessage($"Found {itemsToLoot.Count} items to loot. Starting pickup loop.", 3, SharpDX.Color.LawnGreen);

            // Loop through each found item and pick it up sequentially.
            foreach (var label in itemsToLoot)
            {
                // Double-check the label is still valid before we act
                if (!label.IsVisible || !label.ItemOnGround.IsTargetable)
                {
                    continue; // This item was already picked up, skip to the next one
                }

                LogMessage($"Looting {label.ItemOnGround.GetComponent<WorldItem>()?.ItemEntity.GetComponent<Base>()?.Name}", 2);

                // Move the mouse to the item's label
                yield return Mouse.SetCursorPosHuman(label.Label.GetClientRect().Center, false);
                yield return new WaitTime(50); // Small pause for realism

                yield return Mouse.LeftClick();


                // Wait a moment for the pickup animation to complete before moving to the next item
                yield return new WaitTime(300);
            }

            LogMessage("Looting complete.", 3);
        }

        public bool Gcd()
        {
            return (DateTime.Now - lastTimeAny).TotalMilliseconds > Delay;
        }

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
                // --- ADD THIS NEW BLOCK ---
                else if (commandToProcess == "CLICK_MERC")
                {
                    LogMessage("Mercenary click command received!", 3, SharpDX.Color.Aqua);
                    _mercClickRequested = true;
                }
                else if (commandToProcess == "LOOT_NEARBY")
                {
                    LogMessage("Loot command received!", 3, SharpDX.Color.Aqua);
                    _lootRequested = true;
                }

                else if (commandToProcess == "TRANSITION")
                {
                    LogMessage("Transition command received!", 3, SharpDX.Color.Aqua);
                    _transitionRequested = true;
                }

                else if (commandToProcess == "ACCEPT_INVITE")
                {
                    LogMessage("Accept Invite command received!", 3, SharpDX.Color.Aqua);
                    _acceptInviteRequested = true;
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

        private LabelOnGround GetBestPortalToFollow(PartyElementWindow leaderPartyElement)
        {
            try
            {
                var currentZoneName = Instance.GameController?.Area.CurrentArea.DisplayName;
                if (leaderPartyElement.ZoneName.Equals(currentZoneName) || (!leaderPartyElement.ZoneName.Equals(currentZoneName) && ((bool)Instance?.GameController?.Area?.CurrentArea?.IsHideout || (IsInLabyrinth())))) // TODO: or is chamber of sins a7 or is epilogue
                {
                    if ((IsInLabyrinth()))
                    {
                        var portalLabels =
                        Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                            x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible && x.ItemOnGround != null)
                            .OrderBy(x => Vector3.Distance(_lastPlayerPosition, x.ItemOnGround.Pos)).ToList();

                        return Instance?.GameController?.Area?.CurrentArea?.IsHideout != null && (bool)Instance.GameController?.Area?.CurrentArea?.IsHideout
                            ? portalLabels?[random.Next(portalLabels.Count)]
                            : portalLabels?.FirstOrDefault();
                    }
                    else
                    {
                        var portalLabels =
                            Instance.GameController?.Game?.IngameState?.IngameUi?.ItemsOnGroundLabels.Where(x =>
                            x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.Label.IsVisible && x.ItemOnGround != null &&
                            (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal") || x.ItemOnGround.Metadata.ToLower().Contains("woodsentrancetransition")))
                            .OrderBy(x => Vector3.Distance(_lastPlayerPosition, x.ItemOnGround.Pos)).ToList();

                        //debug1 = portalLabels?.FirstOrDefault().ItemOnGround.Metadata.ToLower();

                        return Instance?.GameController?.Area?.CurrentArea?.IsHideout != null && (bool)Instance.GameController?.Area?.CurrentArea?.IsHideout
                            ? portalLabels?[random.Next(portalLabels.Count)]
                            : portalLabels?.FirstOrDefault();
                    }

                }
                return null;
            }
            catch
            {
                return null;
            }
        }
        // {
        //     try
        //     {
        //         var portalLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabels
        //             .Where(x => x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.ItemOnGround != null &&
        //                         (x.ItemOnGround.Metadata.ToLower().Contains("areatransition") || x.ItemOnGround.Metadata.ToLower().Contains("portal") || x.ItemOnGround.Metadata.ToLower().Contains("woodsentrancetransition")))
        //             .ToList();

        //         if (IsInLabyrinth())
        //         {
        //             portalLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabels
        //                 .Where(x => x != null && x.IsVisible && x.Label != null && x.Label.IsValid && x.ItemOnGround != null)
        //                 .ToList();
        //         }

        //         if (targetPos.HasValue)
        //         {
        //             return portalLabels.OrderBy(x => Vector3.Distance(targetPos.Value, x.ItemOnGround.Pos)).FirstOrDefault();
        //         }
        //         return portalLabels.OrderBy(x => x.ItemOnGround.DistancePlayer).FirstOrDefault();
        //     }
        //     catch { return null; }
        // }

        private Vector2 GetTpButton(PartyElementWindow leaderPartyElement)
        {
            try
            {
                var windowOffset = Follower.Instance.GameController.Window.GetWindowRectangle().TopLeft;
                var elemCenter = (Vector2)leaderPartyElement?.TpButton?.GetClientRectCache.Center;
                var finalPos = new Vector2(elemCenter.X + windowOffset.X, elemCenter.Y + windowOffset.Y);

                return finalPos;
            }
            catch
            {
                return Vector2.Zero;
            }
        }

        private Element GetTpConfirmation()
        {
            try
            {
                var ui = Follower.Instance.GameController?.IngameState?.IngameUi?.PopUpWindow;

                if (ui.Children[0].Children[0].Children[0].Text.Equals("Are you sure you want to teleport to this player's location?"))
                    return ui.Children[0].Children[0].Children[3].Children[0];

                return null;
            }
            catch
            {
                return null;
            }
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
            //(bool)Instance?.GameController?.Game?.IngameState?.Data?.CurrentWorldArea?.IsLabyrinthArea
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
            Graphics.DrawText($"_tasks: {_tasks.Count}", new Vector2(500, 160), SharpDX.Color.White);

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

            // =======================================================
            // NEW: Live Range Visualizer
            // =======================================================
            if (Settings.ShowRangeVisualizer.Value)
            {
                var playerPos = GameController.Player.Pos;
                float radius = Settings.VisualizerRange.Value;
                int segments = 36; // The number of line segments to use to approximate a circle. 36 is smooth.

                // Calculate the points of the circle
                var points = new List<Vector2>();
                for (int i = 0; i <= segments; i++)
                {
                    float angle = (float)(i * (360.0 / segments));
                    float rad = (float)(angle * Math.PI / 180.0); // Convert angle to radians for Sin/Cos

                    // Calculate the world position of the point on the circle's edge
                    var worldPoint = new Vector3(
                        playerPos.X + radius * (float)Math.Cos(rad),
                        playerPos.Y + radius * (float)Math.Sin(rad),
                        playerPos.Z
                    );

                    // Convert the world position to a screen position
                    points.Add(Camera.WorldToScreen(worldPoint));
                }

                // Draw the lines connecting the points
                for (int i = 0; i < segments; i++)
                {
                    Graphics.DrawLine(points[i], points[i + 1], 2, SharpDX.Color.Yellow);
                }
            }

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

        // Use this exact method. It is proven to work.
        private static bool HasStat(Entity monster, GameStat stat)
        {
            try
            {
                // Using StatDictionary as per the working example.
                var value = monster?.GetComponent<Stats>()?.StatDictionary?[stat];
                return value > 0;
            }
            catch (Exception)
            {
                // The try/catch handles any errors if the entity or component becomes invalid during the check.
                return false;
            }
        }

        private Entity GetBestOffensiveTarget(Skill skill)
        {
            var windowRect = GameController.Window.GetWindowRectangleTimeCache;

            var bestTarget = GameController.EntityListWrapper.ValidEntitiesByType[EntityType.Monster]
                .Where(x => 
                {
                    if (x == null || !x.IsAlive) return false;
                    if (x.GetComponent<Life>()?.CurHP <= 0) return false;
                    // The checks remain the same, but now they call the correct HasStat method.
                    if (x?.GetComponent<Render>() == null || !x.IsHostile) return false;
                    if (x.DistancePlayer >= Settings.TargetingRange.Value) return false;
                    if (!windowRect.Contains(Camera.WorldToScreen(x.Pos))) return false;
                    if (x.GetComponent<Targetable>()?.isTargetable != true) return false;
                    
                    // This now calls the correct, robust helper function.
                    if (HasStat(x, GameStat.CannotBeDamaged)) return false;

                    return true;
                })
                .OrderByDescending(x => {
                    // Assign a score based on rarity.
                    var rarity = x.GetComponent<ObjectMagicProperties>()?.Rarity ?? MonsterRarity.White;
                    switch (rarity)
                    {
                        case MonsterRarity.Unique:  return 5;
                        case MonsterRarity.Rare:    return 4;
                        case MonsterRarity.Magic:   return 3;
                        default:                    return 1; // Normal monsters
                    }
                })
                .ThenBy(x => x.DistancePlayer) // Tie-breaker: If rarity is the same, closest is best.
                .FirstOrDefault(); // Get the single best target.

            return bestTarget;
        }


        #endregion
    }

    // Add this enum above your Skill class
    public enum SkillUseMode
    {
        OnCooldown,         // Use whenever it's off cooldown (e.g., for a temporary buff like Blood Rage)
        OnCooldownInRange,  // Use whenever it's off cooldown, BUT only if close to the leader (for War Cries)
        OnMonstersInRange,   // Use when a certain number of monsters are nearby (for attacks or curses)
        OffensiveTargetedAttack // <-- NEW
    }

    // Place this at the bottom of your Follower.cs file, inside the namespace
    public class Skill
    {
        public string Name { get; set; }
        public Keys Key { get; set; }
        public float Cooldown { get; set; } // Cooldown in seconds

        public float Range { get; set; } // Cooldown in seconds
        public DateTime NextUseTime { get; set; } = DateTime.Now;
        public SkillUseMode UseMode { get; set; }
    }
    
    

}