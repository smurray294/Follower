using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.MemoryObjects;

namespace Follower
{
    [SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen")]
    public static class PartyElements
    {
        public static List<string> ListOfPlayersInParty(int child)
        {
            var playersInParty = new List<string>();

            try
            {
                var baseWindow = Follower.Instance.GameController.IngameState.IngameUi.Children[child];
                if (baseWindow != null)
                {
                    var partyList = baseWindow.Children[0]?.Children[0]?.Children;
                    playersInParty.AddRange(from player in partyList where player != null && player.ChildCount >= 3 select player.Children[0].Text);
                }

            }
            catch (Exception)
            {
                // ignored
            }

            return playersInParty;
        }

        // In PartyElements.cs

// In PartyElement.cs

// In PartyElement.cs

        public static List<PartyElementWindow> GetPlayerInfoElementList()
        {
            var playersInParty = new List<PartyElementWindow>();

            try
            {
                var uiRoot = Follower.Instance.GameController?.IngameState?.IngameUi;
                if (uiRoot == null) return playersInParty;

                // --- START OF CORRECTED PATH LOGIC ---
                // Using the path you found with DevTree: 18 -> 0 -> 0
                
                var partyListContainer = uiRoot.GetChildAtIndex(18)?
                                            .GetChildAtIndex(0)?
                                            .GetChildAtIndex(0);

                // The list of party members is the 'Children' of this container
                var partElementList = partyListContainer?.Children;
                
                // --- END OF CORRECTED PATH LOGIC ---

                if (partElementList == null)
                {
                    // If the path is wrong or the UI isn't there, exit gracefully.
                    return playersInParty;
                }

                foreach (var partyElement in partElementList)
                {
                    if (partyElement?.Children == null || partyElement.ChildCount < 1) continue;

                    var newElement = new PartyElementWindow();
                    var children = partyElement.Children;

                    // This logic should now work correctly because we have the right elements.
                    newElement.PlayerName = children[0]?.Text;

                    if (children.Count == 4)
                    {
                        // Player is in another zone
                        newElement.ZoneName = children[2]?.Text;
                        newElement.TpButton = children[3];
                    }
                    else if (children.Count >= 3)
                    {
                        // Player is in the same zone
                        newElement.ZoneName = Follower.Instance.GameController?.Area.CurrentArea.DisplayName;
                        newElement.TpButton = children[2];
                    }
                    else
                    {
                        // Player is in the same zone, but a different UI state (e.g., this is you)
                        newElement.ZoneName = Follower.Instance.GameController?.Area.CurrentArea.DisplayName;
                        newElement.TpButton = null;
                    }

                    newElement.Element = partyElement;
                    playersInParty.Add(newElement);
                }
            }
            catch (Exception e)
            {
                Follower.Instance.LogError($"Error in GetPlayerInfoElementList: {e.Message}", 5);
            }

            return playersInParty;
        }
        // public static List<PartyElementWindow> GetPlayerInfoElementList()
        // {
        //     var playersInParty = new List<PartyElementWindow>();

        //     try
        //     {
        //         // The index 17 for the party UI is very specific and might change. 
        //         // A safer way is to find it by its type/name if possible, but for now we'll keep it.
        //         var baseWindow = Follower.Instance.GameController?.IngameState?.IngameUi?.GetChildAtIndex(17);
        //         var partElementList = baseWindow?.Children?[0]?.Children?[0]?.Children;

        //         if (partElementList == null) return playersInParty; // Exit early if no party elements are found

        //         foreach (var partyElement in partElementList)
        //         {
        //             if (partyElement?.Children == null) continue; // Skip if this element has no children

        //             var newElement = new PartyElementWindow();
        //             var children = partyElement.Children;

        //             // --- START OF THE FIX ---

        //             // Safely get Player Name (usually at index 0)
        //             if (children.Count > 0)
        //             {
        //                 newElement.PlayerName = children[0]?.Text;
        //             }

        //             // This is the logic from the old code, now made safe
        //             if (children.Count == 4)
        //             {
        //                 // Player is in another zone. Zone name is at index 2, TP button is at index 3.
        //                 newElement.ZoneName = children[2]?.Text;
        //                 newElement.TpButton = children[3];
        //             }
        //             else if (children.Count == 3)
        //             {
        //                 // Player is in the same zone. TP button is at index 2.
        //                 newElement.ZoneName = Follower.Instance.GameController?.Area.CurrentArea.DisplayName;
        //                 newElement.TpButton = children[2];
        //             }
        //             else
        //             {
        //                 // Player is in the same zone, but the UI is in a state with fewer children (e.g., yourself)
        //                 // We'll set the zone name but there is no TP button.
        //                 newElement.ZoneName = Follower.Instance.GameController?.Area.CurrentArea.DisplayName;
        //                 newElement.TpButton = null; // Explicitly set to null as it doesn't exist
        //             }

        //             newElement.Element = partyElement;
        //             playersInParty.Add(newElement);

        //             // --- END OF THE FIX ---
        //         }
        //     }
        //     catch (Exception e)
        //     {
        //         // Use LogError for better formatting in the debug console
        //         Follower.Instance.LogError($"Error in GetPlayerInfoElementList: {e.Message}", 5);
        //     }

        //     return playersInParty;
        // }

        // public static List<PartyElementWindow> GetPlayerInfoElementList()


        // {
        //     var playersInParty = new List<PartyElementWindow>();

        //     try
        //     {
        //         var baseWindow = Follower.Instance.GameController?.IngameState?.IngameUi?.GetChildAtIndex(17);
        //         var partElementList = baseWindow?.Children?[0]?.Children?[0]?.Children;
        //         if (partElementList != null)
        //         {
        //             foreach (var partyElement in partElementList)
        //             {
        //                 var playerName = partyElement?.Children?[0]?.Text;
        //                 if (partyElement?.Children != null)
        //                 {
        //                     var newElement = new PartyElementWindow
        //                     {
        //                         PlayerName = playerName,
        //                         //get party element
        //                         Element = partyElement,
        //                         //party element swirly tp thingo, if in another area it becomes child 4 as child 3 becomes the area string
        //                         TpButton = partyElement.Children[partyElement.ChildCount == 4 ? 3 : 2],
        //                         ZoneName = (partyElement.ChildCount == 4) ? partyElement.Children[2].Text : Follower.Instance.GameController?.Area.CurrentArea.DisplayName
        //                     };

        //                     playersInParty.Add(newElement);
        //                 }
        //             }
        //         }
        //     }
        //     catch (Exception e)
        //     {
        //         Follower.Instance.LogError("Character: " + e, 5);
        //     }

        //     return playersInParty;
        // }
    }

    [SuppressMessage("Interoperability", "CA1416:Plattformkompatibilität überprüfen")]
    public class PartyElementWindow
    {
        public string PlayerName { get; set; } = string.Empty;
        public PlayerData Data { get; set; } = new PlayerData();
        public Element Element { get; set; } = new Element();
        public string ZoneName { get; set; } = string.Empty;
        public Element TpButton { get; set; } = new Element();

        public override string ToString()
        {
            return $"PlayerName: {PlayerName}, Data.PlayerEntity.Distance: {Data.PlayerEntity.Distance(Entity.Player).ToString() ?? "Null"}";
        }
    }

    public class PlayerData
    {
        public Entity PlayerEntity { get; set; } = null;
    }
}