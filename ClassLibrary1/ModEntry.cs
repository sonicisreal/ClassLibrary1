using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Locations;

namespace PauseTimeMP
{
    /// <summary>
    /// make MP time behave like SP: pause for menus/dialogs, and slow time when anyone is in Skull Cave.
    /// host is the time authority; clients report their state via lightweight messages.
    /// </summary>
    public class ModEntry : Mod
    {
        // ~54s per in-game hour vs ~43s → ~1.2558x slower
        private const double CavernSlowFactor = 54.0 / 43.0;
        private static int _lastInterval = -1;

        // host-tracked client states
        private static readonly HashSet<long> MenuPauseRequests = new();
        private static readonly HashSet<long> CavernPresence = new();

        public override void Entry(IModHelper helper)
        {
            // patch: host can force pause when anyone is "paused"
            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.Patch(
                original: AccessTools.Method(typeof(Game1), nameof(Game1.shouldTimePass), new[] { typeof(bool) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(ShouldTimePassPostfix))
            );

            // client → host signals
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.Player.Warped += OnWarped;
            helper.Events.Multiplayer.ModMessageReceived += OnMessage;
            helper.Events.Multiplayer.PeerDisconnected += OnPeerDisconnected;

            // host: adjust accumulation when anyone is in Skull Cave
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            // reset per-day/session
            helper.Events.GameLoop.SaveLoaded += (_, __) => ResetDay();
            helper.Events.GameLoop.DayStarted += (_, __) => ResetDay();
        }

        private static void ResetDay()
        {
            _lastInterval = -1;
            MenuPauseRequests.Clear();
            CavernPresence.Clear();
        }

        // -------- clients: report menu open/close --------
        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (Context.IsMainPlayer) return;

            bool paused = e.NewMenu is not null && e.NewMenu is not TitleMenu;

            this.Helper.Multiplayer.SendMessage(
                new MenuPauseMsg(Game1.player.UniqueMultiplayerID, paused),
                "MenuPause",
                modIDs: new[] { this.ModManifest.UniqueID }
            );
        }

        // -------- clients: report skull cave enter/leave --------
        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (Context.IsMainPlayer || e.Player != Game1.player) return;

            bool nowInCave = IsSkullCave(e.NewLocation);

            this.Helper.Multiplayer.SendMessage(
                new CavernMsg(Game1.player.UniqueMultiplayerID, nowInCave),
                "CavernPresence",
                modIDs: new[] { this.ModManifest.UniqueID }
            );
        }

        // -------- host: receive client signals --------
        private void OnMessage(object? sender, ModMessageReceivedEventArgs e)
        {
            if (!Context.IsMainPlayer || e.FromModID != this.ModManifest.UniqueID) return;

            switch (e.Type)
            {
                case "MenuPause":
                    {
                        var m = e.ReadAs<MenuPauseMsg>();
                        if (m.Paused) MenuPauseRequests.Add(m.PlayerId);
                        else MenuPauseRequests.Remove(m.PlayerId);
                        break;
                    }
                case "CavernPresence":
                    {
                        var m = e.ReadAs<CavernMsg>();
                        if (m.InCave) CavernPresence.Add(m.PlayerId);
                        else CavernPresence.Remove(m.PlayerId);
                        break;
                    }
            }
        }

        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;
            MenuPauseRequests.Remove(e.Peer.PlayerID);
            CavernPresence.Remove(e.Peer.PlayerID);
        }

        // -------- host: enforce global pauses --------
        public static void ShouldTimePassPostfix(ref bool __result /*, bool ignoreMultiplayer */)
        {
            if (!Context.IsMainPlayer) return;

            // vanilla already considers the host's own menu/dialog; we add remote menus:
            if (MenuPauseRequests.Count > 0)
                __result = false;
        }

        // -------- host: slow accumulation when ANYONE is in Skull Cave --------
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsMainPlayer || !Game1.hasLoadedGame)
                return;

            int cur = Game1.gameTimeInterval;

            // if time is paused (vanilla or our global pause), just track & bail
            bool paused =
                   Game1.activeClickableMenu is not null
                || Game1.eventUp
                || Game1.currentMinigame is not null
                || Game1.isFestival()
                || Game1.farmEvent is not null
                || Game1.paused
                || MenuPauseRequests.Count > 0;

            if (paused)
            {
                _lastInterval = cur;
                return;
            }

            // anyone in Skull Cave? prefer client-reported set; also scan as fallback
            bool anyInCave = CavernPresence.Count > 0
                             || Game1.getAllFarmers().Any(f => f?.currentLocation is not null && IsSkullCave(f.currentLocation));

            if (!anyInCave)
            {
                _lastInterval = cur;
                return;
            }

            // initialize baseline
            if (_lastInterval < 0)
            {
                _lastInterval = cur;
                return;
            }

            int delta = cur - _lastInterval;

            // handle wrap/reset (after a 10-min tick the interval drops)
            if (delta <= 0)
            {
                _lastInterval = cur;
                return;
            }

            // scale progress down to emulate ~54s per in-game hour
            int adjusted = (int)(delta / CavernSlowFactor);
            if (adjusted < 1) adjusted = 1;

            Game1.gameTimeInterval = _lastInterval + adjusted;
            _lastInterval = Game1.gameTimeInterval;
        }

        // -------- helpers --------
        private static bool IsSkullCave(GameLocation loc)
        {
            if (loc is null) return false;

            // correct internal name is "SkullCave"
            if (loc.Name == "SkullCave" || (loc.NameOrUniqueName?.Contains("SkullCave") ?? false))
                return true;

            // Skull Cave is also represented as a MineShaft with mineLevel >= 121
            if (loc is MineShaft ms && ms.mineLevel >= 121)
                return true;

            return false;
        }

        // message payloads
        private readonly record struct MenuPauseMsg(long PlayerId, bool Paused);
        private readonly record struct CavernMsg(long PlayerId, bool InCave);
    }
}
