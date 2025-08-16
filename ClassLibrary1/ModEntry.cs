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
            helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        }
        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            foreach (Farmer player in Game1.getAllFarmers())
            {
                bool shouldPause = this.ShouldPauseForPlayer(player);
                if (shouldPause != this.isTimePaused)
                {
                    this.isTimePaused = shouldPause;
                    Game1.timeOfDay = e.OldTime;
                } 
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