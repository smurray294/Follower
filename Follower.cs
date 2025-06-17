private IEnumerator BotLogic()
{
    while (true) // The main loop will now run very fast
    {
        // --- High-Priority Checks (Run Every Pass) ---
        if (!GameController.Player.IsAlive || GameController.IsLoading)
        {
            ResetState();
            yield return new WaitTime(250); // Wait if we're in a state where we can't act
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

        // (Your existing logic for creating tasks when leader is far/near goes here. It doesn't need yields.)
        if (_followTarget == null && leaderPartyElement != null && leaderPartyElement.ZoneName != GameController.Game.IngameState.Data.CurrentArea.Name)
        {
            if (!_tasks.Any()) 
            {
                var portalLabel = GetBestPortalToFollow(_lastTargetPosition);
                if (portalLabel != null)
                {
                    _tasks.Add(new TaskNode(portalLabel, 200, TaskNodeType.Transition));
                }
            }
        }
        else if (_followTarget != null)
        {
            var leaderPos = _followTarget.Pos;
            var distanceFromLeader = Vector3.Distance(GameController.Player.Pos, leaderPos);

            if (distanceFromLeader >= Settings.ClearPathDistance)
            {
                if (!_tasks.Any() || Vector3.Distance(_tasks.Last().WorldPosition, leaderPos) >= Settings.PathfindingNodeDistance)
                {
                    _tasks.Add(new TaskNode(leaderPos, Settings.PathfindingNodeDistance));
                }
            }
            else 
            {
                _tasks.RemoveAll(t => t.Type == TaskNodeType.Movement || t.Type == TaskNodeType.Transition);
            }
            _lastTargetPosition = leaderPos;
        }


        // --- Task Execution ---
        if (_tasks.Any())
        {
            var currentTask = _tasks.First();
            if (currentTask.AttemptCount > 6) { _tasks.RemoveAt(0); continue; }
            currentTask.AttemptCount++;

            float distanceToTask = Vector3.Distance(GameController.Player.Pos, currentTask.WorldPosition);

            switch (currentTask.Type)
            {
                case TaskNodeType.Movement:
                    if (distanceToTask <= currentTask.Bounds * 1.5)
                    {
                        _tasks.RemoveAt(0);
                        continue; // Task done, immediately get the next one.
                    }
                    Input.KeyDown(Settings.MovementKey);
                    yield return Mouse.SetCursorPosHuman(WorldToValidScreenPosition(currentTask.WorldPosition));
                    // No extra delay needed. The key is held down.
                    break;

                case TaskNodeType.Transition:
                    Input.KeyUp(Settings.MovementKey); // Stop moving
                    yield return new WaitTime(50); // Tiny pause to ensure we've stopped.
                    yield return Mouse.SetCursorPosAndLeftClickHuman(currentTask.LabelOnGround.Label.GetClientRect().Center, 100);
                    yield return new WaitTime(250); // A much shorter, non-blind wait.
                    // The logic at the top of the loop will handle success.
                    break;
                
                // Add other task types (Loot, etc.) here
            }
        }

        // --- Final Housekeeping ---
        _lastPlayerPosition = GameController.Player.Pos;

        // This is now the ONLY delay for a standard, active loop pass.
        // It defines the bot's "heartbeat". 50ms is very responsive.
        yield return new WaitTime(50); 
    }
}