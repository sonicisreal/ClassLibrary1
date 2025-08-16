using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;  
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Events;
using StardewValley.Menus;
using StardewValley.Network;

namespace ClassLibrary1
{
    public class ModEntry : Mod
    {
        public override void Entry(IModHelper helper)
        {
            var harmony = new Harmony(this.ModManifest.UniqueID);
            var original = AccessTools.Method(typeof(Game1), nameof(Game1.shouldTimePass));
            harmony.Patch(
                original: original,
                postfix: new HarmonyMethod(typeof(Game1Patches), nameof(Game1Patches.ShouldTimePassPostfix))
            );

            // Messaging to communicate client-only pause states (e.g., menus) to the host.
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            // Only clients need to tell the host about their menu state.
            if (Context.IsMainPlayer)
                return;

            bool paused = e.NewMenu != null;
            this.Helper.Multiplayer.SendMessage(
                new PauseRequest(Game1.player.UniqueMultiplayerID, paused),
                "MenuPause",
                modIDs: new[] { this.ModManifest.UniqueID }
            );
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || e.FromModID != this.ModManifest.UniqueID)
                return;

            if (e.Type == "MenuPause")
            {
                var msg = e.ReadAs<PauseRequest>();
                if (msg.Paused)
                    Game1Patches.MenuPauseRequests.Add(msg.PlayerId);
                else
                    Game1Patches.MenuPauseRequests.Remove(msg.PlayerId);
            }
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            Game1Patches.MenuPauseRequests.Remove(e.Peer.PlayerID);
        }
    }

    // Tiny payload for client→host pause requests.
    public readonly record struct PauseRequest(long PlayerId, bool Paused);

    public static class Game1Patches
    {
        // Tracks which clients requested a pause (e.g., they have a menu open).
        internal static readonly HashSet<long> MenuPauseRequests = new();

        // Postfix so we preserve vanilla conditions and add our "pause for any player" rule.
        public static void ShouldTimePassPostfix(ref bool __result)
        {
            // Only the host decides whether time advances.
            if (!Context.IsMainPlayer)
                return;

            bool pause = false;

            // Global/host-local blockers
            pause |= Game1.eventUp
                  || Game1.currentMinigame != null
                  || Game1.isFestival()
                  || Game1.farmEvent != null
                  || Game1.paused
                  || Game1.activeClickableMenu != null; // host's own menu

            // Any synced per-player states that should pause time
            if (!pause)
            {
                foreach (Farmer f in Game1.getAllFarmers())
                {
                    if (f == null) continue;

                    if (f.isInBed.Value
                        || f.UsingTool
                        || f.isEating
                        || f.isEmoting)
                    {
                        pause = true;
                        break;
                    }
                }
            }

            // Any client reported a pause-only state (like having a menu open)
            if (!pause && MenuPauseRequests.Count > 0)
                pause = true;

            // If any condition says "pause", the host says time should not pass.
            if (pause)
                __result = false;
        }
    }
}