using System;
using Microsoft.Xna.Framework;  
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using System.Collections.Generic;

namespace ClassLibrary1
{
    public class ModEntry : Mod
    {

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsMainPlayer || !Game1.hasLoadedGame || Game1.isFestival() || Game1.eventUp || Game1.farmEvent != null)
                return;
            bool shouldPause = false;
            foreach (Farmer player in Game1.getAllFarmers())
            {
                shouldPause = this.ShouldPauseForPlayer(player);
                if (shouldPause)
                    break;
            }

            if (shouldPause)
            {
                Game1.gameTimeInterval = 0;
            } else
            {
                foreach (Farmer player in Game1.getAllFarmers()) {
                    if (player.currentLocation != null && player.currentLocation.Name == "SkullCavern")
                    {
                        Game1.gameTimeInterval = 8000;
                        return;
                    }
                }
                Game1.gameTimeInterval = 7000;
            }
            int newTime = Game1.timeOfDay;
            this.Helper.Multiplayer.SendMessage(
                newTime,
                "SyncTime",
                modIDs: new[] { this.ModManifest.UniqueID }
                );
                
            
        }

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID == this.ModManifest.UniqueID && e.Type == "SyncTime")
            {
                int syncedTime = e.ReadAs<int>();
                if (!Context.IsMainPlayer)
                    Game1.timeOfDay = syncedTime;
            }
        }
        private bool ShouldPauseForPlayer(Farmer player)
        {
            return player.CurrentTool != null && player.UsingTool ||
                   Game1.activeClickableMenu != null && player.Equals(Game1.player) ||
                   player.isInBed.Value ||
                   Game1.eventUp ||
                   Game1.currentMinigame != null ||
                   Game1.isFestival() ||
                   Game1.farmEvent != null ||
                   player.isEmoting ||
                   player.isEating ||
                   Game1.paused;
        }
    }
}