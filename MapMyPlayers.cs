using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Convert = System.Convert;
using CompanionServer.Handlers;


namespace Oxide.Plugins
{
    [Info("Map My Players", "1AK1", "1.1.1")]
    [Description("Display all players positions with texted markers on ingame map (name/steamID/activ/sleepers) with autorefresh")]

    public class MapMyPlayers : RustPlugin
    {
        string Prefix = "[MMP] :";                       // CHAT PLUGIN PREFIX
        string PrefixColor = "#008000";                 // CHAT PLUGIN PREFIX COLOR
        ulong SteamIDIcon = 76561198190843170;          // SteamID FOR PLUGIN ICON

        bool debug = false;
        float refreshrate = 20;
        float MarkerRadius = 0.2f;
        bool ShowSteamID = false;

        const string MMPAdmin = "mapmyplayers.admin"; 

        bool ConfigChanged;
		private Timer mmptimer;

        public List<MapMarkerGenericRadius> PublicRadMarker = new List<MapMarkerGenericRadius>(); 
        public List<VendingMachineMapMarker> PublicVendMarker = new List<VendingMachineMapMarker>(); 
        public Dictionary<ulong, string> activplayers = new Dictionary<ulong, string>();
        public Dictionary<ulong, string> sleepplayers = new Dictionary<ulong, string>();
        public Dictionary<ulong, Vector3> playerspos = new Dictionary<ulong, Vector3>();

		private void Init()
        {
            LoadVariables();
            permission.RegisterPermission(MMPAdmin, this);
        }

#region CONFIG

    protected override void LoadDefaultConfig()
        {
            LoadVariables();
        }

        private void LoadVariables()
        {
            Prefix = Convert.ToString(GetConfig("Chat Settings", "Prefix", "[MMP] :"));                       // CHAT PLUGIN PREFIX
            PrefixColor = Convert.ToString(GetConfig("Chat Settings", "PrefixColor", "#008000"));                // CHAT PLUGIN PREFIX COLOR
            SteamIDIcon = Convert.ToUInt64(GetConfig("Chat Settings", "SteamIDIcon", "76561198190843170"));  
            refreshrate = Convert.ToSingle(GetConfig("Refresh Rate", "Value in seconds", "20"));
            MarkerRadius = Convert.ToSingle(GetConfig("Markers", "radius size (0.2 by default)", "0.2"));
            ShowSteamID = Convert.ToBoolean(GetConfig("Markers Label", "Show player steam ID", "false"));

            if (!ConfigChanged) return;
            SaveConfig();
            ConfigChanged = false;
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                ConfigChanged = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                ConfigChanged = true;
            }
            return value;
        }

#endregion

        void Unload()
        {
            MarkerDisplayingDelete(null, null, null);
			if (mmptimer != null) mmptimer.Destroy();
        }

        object CanNetworkTo(MapMarkerGenericRadius marker, BasePlayer player)
        {
            if (!PublicRadMarker.Contains(marker))
            {
                return null;
            }

            if (player.IPlayer.HasPermission(MMPAdmin) && PublicRadMarker.Contains(marker))
            {
                return null;
            }

            return false;
        }

        object CanNetworkTo(VendingMachineMapMarker marker, BasePlayer player)
        {
            if (!PublicVendMarker.Contains(marker))
            {
                return null;
            }

            if (player.IPlayer.HasPermission(MMPAdmin) && PublicVendMarker.Contains(marker))
            {
                return null;
            }

            return false;
        }

        #region Lang messages

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"NoAdminPermMsg", "Not authorized to control this plugin."},
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"NoAdminPermMsg", "Vous n'êtes pas autorisé à contrôler ce plugin."},
            }, this, "fr");
        }


#endregion

        void MarkerDisplayingDelete(BasePlayer player, string command, string[] args)
        { 
            foreach (var Rad in PublicRadMarker)
            {
                if (Rad != null)
                {
                    Rad.Kill();
                    Rad.SendUpdate();   
                }
            }
            if (debug) Puts($"-> DEL ALL RAD MARKER");
            foreach (var Vend in PublicVendMarker)
            {
                if (Vend != null) Vend.Kill();
            }
            if (debug) Puts($"-> DEL ALL VEND MARKER");
            PublicRadMarker.Clear();
            PublicVendMarker.Clear();         
        }

        private void ListPlayers()
        {
#region EACH ACTIV
            activplayers.Clear();
            playerspos.Clear();
            sleepplayers.Clear();
            foreach(BasePlayer player in BasePlayer.activePlayerList.ToList())
            {
                Vector3 pos;
                pos = player.transform.position;
                if (pos == null)
                {           
                    if (debug){Puts($"-> MY MARKER Position update ERROR for {player.UserIDString}");}
                    return;
                }
                string playername = player.displayName.ToString();
                if (playername.Length > 16){playername = playername.Substring(0,16);}
                activplayers.Remove(player.userID);
                activplayers.Add(player.userID, playername);
                playerspos.Remove(player.userID);
                playerspos.Add(player.userID, pos);
            }
#endregion

 #region EACH SLEEPER

            foreach(BasePlayer player in BasePlayer.sleepingPlayerList.ToList())
            {
                Vector3 pos;
                pos = player.transform.position;
                if (pos == null)
                {           
                    if (debug){Puts($"-> MY MARKER Position update ERROR SLEEPER for {player.UserIDString}");}
                    return;
                }
                string playername = player.displayName.ToString();
                if (playername.Length > 16){playername = playername.Substring(0,16);}
                sleepplayers.Remove(player.userID);
                sleepplayers.Add(player.userID, playername);
                playerspos.Remove(player.userID);
                playerspos.Add(player.userID, pos);
            }
#endregion

        }

        [ChatCommand("mmp_stop")] 
        private void MapMyCommandStop(BasePlayer player, string command, string[] args)
        {
            bool isadmin = permission.UserHasPermission(player.UserIDString, MMPAdmin);
            if (isadmin == false)
            {
                Player.Message(player, $"{lang.GetMessage("NoAdminPermMsg", this, player.UserIDString)}",$"<color={PrefixColor}> {Prefix} </color>", SteamIDIcon);
                return;
            }
            //chat stop
            MarkerDisplayingDelete(null, null, null);
			if (mmptimer != null){mmptimer.Destroy();}

        }

        [ChatCommand("mmp_show")]
        private void MapMyCommand(BasePlayer player, string command, string[] args)
        {
            bool isadmin = permission.UserHasPermission(player.UserIDString, MMPAdmin);
            if (isadmin == false)
            {
                Player.Message(player, $"{lang.GetMessage("NoAdminPermMsg", this, player.UserIDString)}",$"<color={PrefixColor}> {Prefix} </color>", SteamIDIcon);
                return;
            }
            if (debug) Puts($"-> REFRESH RATE IS {refreshrate}");
            //chat show
            GenerateMarkers();
            mmptimer = timer.Repeat(refreshrate, 0, () =>
            {
                GenerateMarkers();
            });   
        }

#region marker generator

        void GenerateMarkers()
        { 
            ListPlayers();
            MarkerDisplayingDelete(null, null, null);
            MapMarkerGenericRadius MapMarkerCustom; 
            VendingMachineMapMarker MapMarkerVendingCustom;
            Vector3 pos;
            string activname;
            string sleepername;
            foreach (var playeringame in activplayers)
            {
                playerspos.TryGetValue(playeringame.Key, out pos);
                activplayers.TryGetValue(playeringame.Key, out activname);
                if (debug){Puts($"-> LOADED MARKER ACTIV LOCATION");}           
                MapMarkerVendingCustom = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", pos) as VendingMachineMapMarker;
                if (MapMarkerVendingCustom == null) return;
                if (ShowSteamID) MapMarkerVendingCustom.markerShopName = $"ACTIVE PLAYER\n{activname}\nSTEAM : {playeringame.Key}";
                else MapMarkerVendingCustom.markerShopName = $"ACTIVE PLAYER\n{activname}";
                PublicVendMarker.Add(MapMarkerVendingCustom);
                if (debug) Puts($"-> VENDING MARKER STORED DICT");
                MapMarkerCustom = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", pos) as MapMarkerGenericRadius;
                MapMarkerCustom.alpha = 1.0f;
                MapMarkerCustom.color1 = Color.green;
                MapMarkerCustom.color2 = Color.black;
                MapMarkerCustom.radius = MarkerRadius;
                PublicRadMarker.Add(MapMarkerCustom);
                if (debug) {Puts($"-> SPAWN MARKER FOR ACTIV PLAYER {activname}");}      
            }

            foreach (var playersleepin in sleepplayers)
            {
                playerspos.TryGetValue(playersleepin.Key, out pos);
                sleepplayers.TryGetValue(playersleepin.Key, out sleepername);
                if (debug){Puts($"-> LOADED MARKER ACTIV LOCATION");}
                MapMarkerVendingCustom = GameManager.server.CreateEntity("assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab", pos) as VendingMachineMapMarker;
                if (MapMarkerVendingCustom == null) return;
                if (ShowSteamID) MapMarkerVendingCustom.markerShopName = $"SLEEPER PLAYER\n{sleepername}\nSTEAM : {playersleepin.Key}";
                else MapMarkerVendingCustom.markerShopName = $"SLEEPER PLAYER\n{sleepername}";
                PublicVendMarker.Add(MapMarkerVendingCustom);
                if (debug) Puts($"-> VENDING MARKER STORED DICT");
                MapMarkerCustom = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", pos) as MapMarkerGenericRadius;
                MapMarkerCustom.alpha = 1.0f;
                MapMarkerCustom.color1 = Color.red;
                MapMarkerCustom.color2 = Color.black;
                MapMarkerCustom.radius = MarkerRadius;
                PublicRadMarker.Add(MapMarkerCustom);
                if (debug) Puts($"-> SPAWN MARKER FOR SLEEPING PLAYER {sleepername}"); 
            }
            foreach (var Vend in PublicVendMarker)
            {
                Vend.Spawn();
                MapMarker.serverMapMarkers.Remove(Vend);
                if (debug){Puts($"-> SPAWN ALL VEND MARKER");}             
            }
            foreach (var Rad in PublicRadMarker)
            {
                Rad.Spawn();
                MapMarker.serverMapMarkers.Remove(Rad);
                Rad.SendUpdate();   
                if (debug){Puts($"-> SPAWN ALL RAD MARKER");}         
            }               
        }
#endregion
    }
}
