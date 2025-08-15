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
        private bool isTimePaused = false;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMultiplayer)
                return;
            if (!Context.IsMultiplayer)
            {
                this.Monitor.Log($"{e}", LogLevel.Debug);
                return;
            }

            bool shouldPause = false;
            foreach (Farmer player in Game1.getOnlineFarmers())
            {
                if (ShouldPauseForPlayer(player))
                {
                    shouldPause = true;
                    break;
                }
            }

            if (Context.IsMainPlayer && shouldPause != isTimePaused)
            {
                isTimePaused = shouldPause;
                UpdateTimePause(shouldPause);
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


        

        private void UpdateTimePause(bool shouldPause)
        {
            if (shouldPause)
            {
                Game1.gameTimeInterval = 0; 
            }
            else
            {
                Game1.gameTimeInterval = 7000;
            }
        }
    }

    public class PauseMessage
    {
        public bool ShouldPause { get; set; }
    }
}