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

            // Do not signal when on TitleMenu to avoid pausing hosts during joins/loads.
            bool paused = e.NewMenu is not null && e.NewMenu is not TitleMenu;
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

            // Pause for global blockers and the host's own UI
            bool pause =
                   Game1.eventUp
                || Game1.currentMinigame is not null
                || Game1.isFestival()
                || Game1.farmEvent is not null
                || Game1.paused
                || Game1.activeClickableMenu is not null
                || Game1.dialogueUp;

            // Pause if any client reported a menu open
            if (!pause && MenuPauseRequests.Count > 0)
                pause = true;

            if (pause)
                __result = false;
        }
    }
}