//#define DEBUG
using Facepunch;
using Network;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core;
using Oxide.Game.Rust;
using Rust;
using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine;
using System.IO;

// scrap payments to bypass cooldown
// costs to buy teleport

/*
Fixed home limit daily reset check
Fixed ErrorTPR showing a blank target message
Added TPTargetInsideEntity message to language API
Updated /radiushome to reply with any error message
Removed bounds check in CheckFoundation
*/

namespace Oxide.Plugins
{
    [Info("NTeleportation", "nivex", "1.7.0")]
    [Description("Multiple teleportation systems for admin and players")]
    class NTeleportation : RustPlugin
    {
        [PluginReference]
        private Plugin Clans, Economics, ServerRewards, Friends, CompoundTeleport, ZoneManager, NoEscape, Vanish, PopupNotifications;

        private Dictionary<string, BasePlayer> _ids = new Dictionary<string, BasePlayer>();
        private Dictionary<BasePlayer, string> _players = new Dictionary<BasePlayer, string>();

        private bool newSave;
        private string banditPrefab;
        private string outpostPrefab;
        private const string NewLine = "\n";
        private const string PermAdmin = "nteleportation.admin";
        private const string PermRestrictions = "nteleportation.norestrictions";
        private const string ConfigDefaultPermVip = "nteleportation.vip";
        private const string PermHome = "nteleportation.home";
        private const string PermWipeHomes = "nteleportation.wipehomes";
        private const string PermCraftHome = "nteleportation.crafthome";
        private const string PermDeleteHome = "nteleportation.deletehome";
        private const string PermHomeHomes = "nteleportation.homehomes";
        private const string PermImportHomes = "nteleportation.importhomes";
        private const string PermRadiusHome = "nteleportation.radiushome";
        private const string PermCraftTpR = "nteleportation.crafttpr";
        private const string PermTpR = "nteleportation.tpr";
        private const string PermTp = "nteleportation.tp";
        private const string PermTpT = "nteleportation.tpt";
        private const string PermTpB = "nteleportation.tpb";
        private const string PermTpN = "nteleportation.tpn";
        private const string PermTpL = "nteleportation.tpl";
        private const string PermTpConsole = "nteleportation.tpconsole";
        private const string PermTpHome = "nteleportation.tphome";
        private const string PermTpRemove = "nteleportation.tpremove";
        private const string PermTpSave = "nteleportation.tpsave";
        private const string PermExempt = "nteleportation.exemptfrominterruptcountdown";
        private const string PermFoundationCheck = "nteleportation.bypassfoundationcheck";
        private const string PermTpMarker = "nteleportation.tpmarker";
        private DynamicConfigFile dataConvert;
        private DynamicConfigFile dataDisabled;
        private DynamicConfigFile dataAdmin;
        private DynamicConfigFile dataHome;
        private DynamicConfigFile dataTPR;
        private DynamicConfigFile dataTPT;
        private Dictionary<ulong, AdminData> _Admin;
        private Dictionary<ulong, HomeData> _Home;
        private Dictionary<ulong, TeleportData> _TPR;
        private List<string> TPTToggle;
        private bool changedAdmin;
        private bool changedHome;
        private bool changedTPR;
        private bool changedTPT;
        private float boundary;
        private readonly Dictionary<ulong, float> TeleportCooldowns = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, TeleportTimer> TeleportTimers = new Dictionary<ulong, TeleportTimer>();
        private readonly Dictionary<ulong, Timer> PendingRequests = new Dictionary<ulong, Timer>();
        private readonly Dictionary<ulong, BasePlayer> PlayersRequests = new Dictionary<ulong, BasePlayer>();
        private readonly Dictionary<int, string> ReverseBlockedItems = new Dictionary<int, string>();
        private readonly Dictionary<ulong, Vector3> teleporting = new Dictionary<ulong, Vector3>();
        private SortedDictionary<string, Vector3> caves = new SortedDictionary<string, Vector3>();
        private SortedDictionary<string, MonInfo> monuments = new SortedDictionary<string, MonInfo>();
        private bool outpostEnabled;
        private bool banditEnabled;

        class MonInfo
        {
            public Vector3 Position;
            public float Radius;
        }

        #region Configuration

        private static Configuration config;

        public class InterruptSettings
        {
            [JsonProperty(PropertyName = "Interrupt Teleport At Specific Monuments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Monuments { get; set; } = new List<string>();

            [JsonProperty(PropertyName = "Above Water")]
            public bool AboveWater { get; set; } = true;

            [JsonProperty(PropertyName = "Balloon")]
            public bool Balloon { get; set; } = true;

            [JsonProperty(PropertyName = "Boats")]
            public bool Boats { get; set; }

            [JsonProperty(PropertyName = "Cargo Ship")]
            public bool Cargo { get; set; } = true;

            [JsonProperty(PropertyName = "Cold")]
            public bool Cold { get; set; } = false;

            [JsonProperty(PropertyName = "Excavator")]
            public bool Excavator { get; set; } = false;

            [JsonProperty(PropertyName = "Hot")]
            public bool Hot { get; set; } = false;

            [JsonProperty(PropertyName = "Hostile")]
            public bool Hostile { get; set; } = false;

            [JsonProperty(PropertyName = "Hurt")]
            public bool Hurt { get; set; } = true;

            [JsonProperty(PropertyName = "Junkpiles")]
            public bool Junkpiles { get; set; }

            [JsonProperty(PropertyName = "Lift")]
            public bool Lift { get; set; } = true;

            [JsonProperty(PropertyName = "Monument")]
            public bool Monument { get; set; } = false;

            [JsonProperty(PropertyName = "Ignore Monument Marker Prefab")]
            public bool BypassMonumentMarker { get; set; } = false;

            [JsonProperty(PropertyName = "Mounted")]
            public bool Mounted { get; set; } = true;

            [JsonProperty(PropertyName = "Oil Rig")]
            public bool Oilrig { get; set; } = false;

            [JsonProperty(PropertyName = "Safe Zone")]
            public bool Safe { get; set; } = true;

            [JsonProperty(PropertyName = "Swimming")]
            public bool Swimming { get; set; } = false;
        }

        public class PluginSettings
        {
            [JsonProperty(PropertyName = "Interrupt TP")]
            public InterruptSettings Interrupt { get; set; } = new InterruptSettings();

            [JsonProperty(PropertyName = "Block Teleport (NoEscape)")]
            public bool BlockNoEscape { get; set; } = false;

            [JsonProperty(PropertyName = "Block Teleport (ZoneManager)")]
            public bool BlockZoneFlag { get; set; } = false;

            [JsonProperty(PropertyName = "Chat Name")]
            public string ChatName { get; set; } = "<color=red>Teleportation</color> \n\n";

            [JsonProperty(PropertyName = "Chat Steam64ID")]
            public ulong ChatID { get; set; } = 76561199056025689;

            [JsonProperty(PropertyName = "Check Boundaries On Teleport X Y Z")]
            public bool CheckBoundaries { get; set; } = true;

            [JsonProperty(PropertyName = "Data File Directory (Blank = Default)")]
            public string DataFileFolder { get; set; } = string.Empty;

            [JsonProperty(PropertyName = "Draw Sphere On Set Home")]
            public bool DrawHomeSphere { get; set; } = true;

            [JsonProperty(PropertyName = "Homes Enabled")]
            public bool HomesEnabled { get; set; } = true;

            [JsonProperty(PropertyName = "TPR Enabled")]
            public bool TPREnabled { get; set; } = true;

            [JsonProperty(PropertyName = "Strict Foundation Check")]
            public bool StrictFoundationCheck { get; set; } = false;

            [JsonProperty(PropertyName = "Cave Distance Small")]
            public float CaveDistanceSmall { get; set; } = 50f;

            [JsonProperty(PropertyName = "Cave Distance Medium")]
            public float CaveDistanceMedium { get; set; } = 70f;

            [JsonProperty(PropertyName = "Cave Distance Large")]
            public float CaveDistanceLarge { get; set; } = 110f;

            [JsonProperty(PropertyName = "Default Monument Size")]
            public float DefaultMonumentSize { get; set; } = 50f;

            [JsonProperty(PropertyName = "Minimum Temp")]
            public float MinimumTemp { get; set; } = 0f;

            [JsonProperty(PropertyName = "Maximum Temp")]
            public float MaximumTemp { get; set; } = 40f;

            [JsonProperty(PropertyName = "Blocked Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> BlockedItems { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty(PropertyName = "Bypass CMD")]
            public string BypassCMD { get; set; } = "pay";

            [JsonProperty(PropertyName = "Use Monument Topology Check")]
            public bool MonumentTopologyCheck { get; set; }

            [JsonProperty(PropertyName = "Use Cave Topology Check")]
            public bool CaveTopologyCheck { get; set; } = false;

            [JsonProperty(PropertyName = "Use Economics")]
            public bool UseEconomics { get; set; } = false;

            [JsonProperty(PropertyName = "Use Server Rewards")]
            public bool UseServerRewards { get; set; } = false;

            [JsonProperty(PropertyName = "Wipe On Upgrade Or Change")]
            public bool WipeOnUpgradeOrChange { get; set; } = true;

            [JsonProperty(PropertyName = "Auto Generate Outpost Location")]
            public bool AutoGenOutpost { get; set; } = true;

            [JsonProperty(PropertyName = "Auto Generate Bandit Location")]
            public bool AutoGenBandit { get; set; } = true;

            [JsonProperty(PropertyName = "Show Time As Seconds Instead")]
            public bool UseSeconds { get; set; } = false;

            [JsonProperty(PropertyName = "Chat Command Color")]
            public string ChatCommandColor = "#FFFF00";

            [JsonProperty(PropertyName = "Chat Command Argument Color")]
            public string ChatCommandArgumentColor = "#FFA500";

            [JsonProperty("Enable Popup Support")]
            public bool UsePopup = false;

            [JsonProperty("Block All Teleporting From Inside Authorized Base")]
            public bool BlockAuthorizedTeleporting = false;

            [JsonProperty("TPB Available After X Seconds")]
            public float TPBTime = 0f;

            [JsonProperty("Global Teleport Cooldown")]
            public float Global = 0f;

            [JsonProperty("Global VIP Teleport Cooldown")]
            public float GlobalVIP = 0f;

            [JsonProperty("Play Sounds After Teleport")]
            public bool PlaySounds = false;

            [JsonProperty("Sounds To Play After Teleport", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> PrefabSounds = new List<string>
            {
                "assets/prefabs/misc/xmas/presents/effects/unwrap.prefab",
                "assets/bundled/prefabs/fx/player/howl.prefab",
                "assets/content/vehicles/minicopter/debris_effect.prefab",
                "assets/prefabs/npc/patrol helicopter/damage_effect_debris.prefab",
                "assets/prefabs/npc/patrol helicopter/effects/rocket_fire.prefab"
            };
        }
        
        public class AdminSettings
        {
            [JsonProperty(PropertyName = "Announce Teleport To Target")]
            public bool AnnounceTeleportToTarget { get; set; } = false;

            [JsonProperty(PropertyName = "Usable By Admins")]
            public bool UseableByAdmins { get; set; } = true;

            [JsonProperty(PropertyName = "Usable By Moderators")]
            public bool UseableByModerators { get; set; } = true;

            [JsonProperty(PropertyName = "Location Radius")]
            public int LocationRadius { get; set; } = 25;

            [JsonProperty(PropertyName = "Teleport Near Default Distance")]
            public int TeleportNearDefaultDistance { get; set; } = 30;
        }

        public class HomesSettings
        {
            [JsonProperty(PropertyName = "Homes Limit")]
            public int HomesLimit { get; set; } = 2;

            [JsonProperty(PropertyName = "VIP Homes Limits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPHomesLimits { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "Allow Sethome At Specific Monuments", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AllowedMonuments { get; set; } = new List<string> { "HQM Quarry", "Stone Quarry", "Sulfur Quarry", "Ice Lake" };

            [JsonProperty(PropertyName = "Allow Sethome At All Monuments")]
            public bool AllowAtAllMonuments { get; set; } = false;

            [JsonProperty(PropertyName = "Allow TPB")]
            public bool AllowTPB { get; set; } = true;

            [JsonProperty(PropertyName = "Cooldown")]
            public int Cooldown { get; set; } = 600;

            [JsonProperty(PropertyName = "Countdown")]
            public int Countdown { get; set; } = 15;

            [JsonProperty(PropertyName = "Daily Limit")]
            public int DailyLimit { get; set; } = 5;

            [JsonProperty(PropertyName = "VIP Daily Limits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPDailyLimits { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "VIP Cooldowns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPCooldowns { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "VIP Countdowns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPCountdowns { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "Location Radius")]
            public int LocationRadius { get; set; } = 25;

            [JsonProperty(PropertyName = "Force On Top Of Foundation")]
            public bool ForceOnTopOfFoundation { get; set; } = true;

            [JsonProperty(PropertyName = "Check Foundation For Owner")]
            public bool CheckFoundationForOwner { get; set; } = true;

            [JsonProperty(PropertyName = "Use Friends")]
            public bool UseFriends { get; set; } = true;

            [JsonProperty(PropertyName = "Use Clans")]
            public bool UseClans { get; set; } = true;

            [JsonProperty(PropertyName = "Use Teams")]
            public bool UseTeams { get; set; } = true;

            [JsonProperty(PropertyName = "Usable Out Of Building Blocked")]
            public bool UsableOutOfBuildingBlocked { get; set; } = false;

            [JsonProperty(PropertyName = "Usable Into Building Blocked")]
            public bool UsableIntoBuildingBlocked { get; set; } = false;

            [JsonProperty(PropertyName = "Usable From Safe Zone Only")]
            public bool UsableFromSafeZoneOnly { get; set; } = false;

            [JsonProperty(PropertyName = "Allow Cupboard Owner When Building Blocked")]
            public bool CupOwnerAllowOnBuildingBlocked { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Iceberg")]
            public bool AllowIceberg { get; set; } = false;

            [JsonProperty(PropertyName = "Allow Cave")]
            public bool AllowCave { get; set; }

            [JsonProperty(PropertyName = "Allow Crafting")]
            public bool AllowCraft { get; set; } = false;

            [JsonProperty(PropertyName = "Allow Above Foundation")]
            public bool AllowAboveFoundation { get; set; } = true;

            [JsonProperty(PropertyName = "Check If Home Is Valid On Listhomes")]
            public bool CheckValidOnList { get; set; } = false;

            [JsonProperty(PropertyName = "Pay")]
            public int Pay { get; set; } = 0;

            [JsonProperty(PropertyName = "Bypass")]
            public int Bypass { get; set; } = 0;
        }

        public class TPTSettings
        {
            [JsonProperty(PropertyName = "Use Friends")]
            public bool UseFriends { get; set; }

            [JsonProperty(PropertyName = "Use Clans")]
            public bool UseClans { get; set; }

            [JsonProperty(PropertyName = "Use Teams")]
            public bool UseTeams { get; set; }

            [JsonProperty(PropertyName = "Allow Cave")]
            public bool AllowCave { get; set; }
        }

        public class TPRSettings
        {
            [JsonProperty(PropertyName = "Require Player To Be Friend, Clan Mate, Or Team Mate")]
            public bool UseClans_Friends_Teams { get; set; }

            [JsonProperty(PropertyName = "Allow Cave")]
            public bool AllowCave { get; set; }

            [JsonProperty(PropertyName = "Allow TPB")]
            public bool AllowTPB { get; set; } = true;

            [JsonProperty(PropertyName = "Cooldown")]
            public int Cooldown { get; set; } = 600;

            [JsonProperty(PropertyName = "Countdown")]
            public int Countdown { get; set; } = 15;

            [JsonProperty(PropertyName = "Daily Limit")]
            public int DailyLimit { get; set; } = 5;

            [JsonProperty(PropertyName = "VIP Daily Limits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPDailyLimits { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "VIP Cooldowns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPCooldowns { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "VIP Countdowns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPCountdowns { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "Request Duration")]
            public int RequestDuration { get; set; } = 30;

            [JsonProperty(PropertyName = "Block TPA On Ceiling")]
            public bool BlockTPAOnCeiling { get; set; } = true;

            [JsonProperty(PropertyName = "Usable Out Of Building Blocked")]
            public bool UsableOutOfBuildingBlocked { get; set; } = false;

            [JsonProperty(PropertyName = "Usable Into Building Blocked")]
            public bool UsableIntoBuildingBlocked { get; set; } = false;

            [JsonProperty(PropertyName = "Allow Cupboard Owner When Building Blocked")]
            public bool CupOwnerAllowOnBuildingBlocked { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Crafting")]
            public bool AllowCraft { get; set; } = false;

            [JsonProperty(PropertyName = "Pay")]
            public int Pay { get; set; } = 0;

            [JsonProperty(PropertyName = "Bypass")]
            public int Bypass { get; set; } = 0;
        }

        public class TownSettings
        {
            [JsonProperty(PropertyName = "Command Enabled")]
            public bool Enabled { get; set; } = true;

            [JsonProperty(PropertyName = "Allow TPB")]
            public bool AllowTPB { get; set; } = true;

            [JsonProperty(PropertyName = "Allow Cave")]
            public bool AllowCave { get; set; }

            [JsonProperty(PropertyName = "Cooldown")]
            public int Cooldown { get; set; } = 600;

            [JsonProperty(PropertyName = "Countdown")]
            public int Countdown { get; set; } = 15;

            [JsonProperty(PropertyName = "Daily Limit")]
            public int DailyLimit { get; set; } = 5;

            [JsonProperty(PropertyName = "VIP Daily Limits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPDailyLimits { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "VIP Cooldowns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPCooldowns { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "VIP Countdowns", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> VIPCountdowns { get; set; } = new Dictionary<string, int> { { ConfigDefaultPermVip, 5 } };

            [JsonProperty(PropertyName = "Location")]
            public Vector3 Location { get; set; } = Vector3.zero;

            [JsonProperty(PropertyName = "Locations", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<Vector3> Locations { get; set; } = new List<Vector3>();

            [JsonProperty(PropertyName = "Teleport To Random Location")]
            public bool Random { get; set; } = true;

            [JsonProperty(PropertyName = "Usable Out Of Building Blocked")]
            public bool UsableOutOfBuildingBlocked { get; set; } = false;

            [JsonProperty(PropertyName = "Allow Crafting")]
            public bool AllowCraft { get; set; } = false;

            [JsonProperty(PropertyName = "Pay")]
            public int Pay { get; set; } = 0;

            [JsonProperty(PropertyName = "Bypass")]
            public int Bypass { get; set; } = 0;

            public bool CanCraft(BasePlayer player, string command)
            {
                return AllowCraft || player.IPlayer.HasPermission($"nteleportation.craft{command}");
            }

            [JsonIgnore]
            public StoredData Teleports = new StoredData();

            [JsonIgnore]
            public string Command { get; set; }
        }

        private class Configuration
        {
            [JsonProperty(PropertyName = "Settings")]
            public PluginSettings Settings = new PluginSettings();

            [JsonProperty(PropertyName = "Admin")]
            public AdminSettings Admin = new AdminSettings();

            [JsonProperty(PropertyName = "Home")]
            public HomesSettings Home = new HomesSettings();

            [JsonProperty(PropertyName = "TPT")]
            public TPTSettings TPT = new TPTSettings();

            [JsonProperty(PropertyName = "TPR")]
            public TPRSettings TPR = new TPRSettings();

            [JsonProperty(PropertyName = "Dynamic Commands", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, TownSettings> DynamicCommands { get; set; } = DefaultCommands;
        }

        private static Dictionary<string, TownSettings> DefaultCommands = new Dictionary<string, TownSettings>
        {
            ["Town"] = new TownSettings() { Random = false },
            ["Island"] = new TownSettings() { AllowTPB = false },
            ["Outpost"] = new TownSettings(),
            ["Bandit"] = new TownSettings(),
        };

        public void InitializeDynamicCommands()
        {
            banditPrefab = StringPool.Get(2074025910);
            outpostPrefab = StringPool.Get(1879405026);

            foreach (var entry in config.DynamicCommands)
            {
                if (!entry.Value.Enabled)
                {
                    continue;
                }
                else if (entry.Key.Equals("bandit", StringComparison.OrdinalIgnoreCase))
                {
                    if (CompoundTeleport == null || Convert.ToBoolean(CompoundTeleport?.Call("umodversion")))
                    {
                        banditEnabled = true;
                    }
                    else continue;
                }
                else if (entry.Key.Equals("outpost", StringComparison.OrdinalIgnoreCase))
                {
                    if (CompoundTeleport == null || Convert.ToBoolean(CompoundTeleport?.Call("umodversion")))
                    {
                        outpostEnabled = true;
                    }
                    else continue;
                }

                entry.Value.Command = entry.Key;
                RegisterCommand(entry.Key, nameof(CommandCustom));
            }

            RegisterCommand("ntp", nameof(CommandDynamic), PermAdmin);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Config.Settings.Converters = new JsonConverter[] { new UnityVector3Converter() };
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
                SaveConfig();
            }
            catch (JsonException ex)
            {
                Debug.LogException(ex);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        protected override void LoadDefaultConfig()
        {
            config = new Configuration();
            Puts("Loaded default configuration.");
        }

        #endregion

        class DisabledData
        {
            [JsonProperty("List of disabled commands")]
            public List<string> DisabledCommands = new List<string>();

            public DisabledData() { }
        }

        DisabledData DisabledCommandData = new DisabledData();

        class AdminData
        {
            [JsonProperty("pl")]
            public Vector3 PreviousLocation { get; set; }

            [JsonProperty("l")]
            public Dictionary<string, Vector3> Locations { get; set; } = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);
        }

        class HomeData
        {
            [JsonProperty("l")]
            public Dictionary<string, Vector3> Locations { get; set; } = new Dictionary<string, Vector3>(StringComparer.OrdinalIgnoreCase);

            [JsonProperty("t")]
            public TeleportData Teleports { get; set; } = new TeleportData();
        }

        public class TeleportData
        {
            [JsonProperty("a")]
            public int Amount { get; set; }

            [JsonProperty("d")]
            public string Date { get; set; }

            [JsonProperty("t")]
            public int Timestamp { get; set; }
        }

        class TeleportTimer
        {
            public Timer Timer { get; set; }
            public BasePlayer OriginPlayer { get; set; }
            public BasePlayer TargetPlayer { get; set; }
        }

        private enum checkmode
        {
            home, tpr, tpa, town
        };

        protected override void LoadDefaultMessages()
        {
            var en = new Dictionary<string, string>
            {
                {"ErrorTPR", "Teleporting to {0} is blocked ({1})"},
                {"AdminTP", "You teleported to {0}!"},
                {"AdminTPTarget", "{0} teleported to you!"},
                {"AdminTPPlayers", "You teleported {0} to {1}!"},
                {"AdminTPPlayer", "{0} teleported you to {1}!"},
                {"AdminTPPlayerTarget", "{0} teleported {1} to you!"},
                {"AdminTPCoordinates", "You teleported to {0}!"},
                {"AdminTPTargetCoordinates", "You teleported {0} to {1}!"},
                {"AdminTPOutOfBounds", "You tried to teleport to a set of coordinates outside the map boundaries!"},
                {"AdminTPBoundaries", "X and Z values need to be between -{0} and {0} while the Y value needs to be between -100 and 2000!"},
                {"AdminTPLocation", "You teleported to {0}!"},
                {"AdminTPLocationSave", "You have saved the current location!"},
                {"AdminTPLocationRemove", "You have removed the location {0}!"},
                {"AdminLocationList", "The following locations are available:"},
                {"AdminLocationListEmpty", "You haven't saved any locations!"},
                {"AdminTPBack", "You've teleported back to your previous location!"},
                {"AdminTPBackSave", "Your previous location has been saved, use /tpb to teleport back!"},
                {"AdminTPTargetCoordinatesTarget", "{0} teleported you to {1}!"},
                {"AdminTPConsoleTP", "You were teleported to {0}"},
                {"AdminTPConsoleTPPlayer", "You were teleported to {0}"},
                {"AdminTPConsoleTPPlayerTarget", "{0} was teleported to you!"},
                {"HomeTP", "You teleported to your home '{0}'!"},
                {"HomeAdminTP", "You teleported to {0}'s home '{1}'!"},
                {"HomeSave", "You have saved the current location as your home!"},
                {"HomeNoFoundation", "You can only use a home location on a foundation!"},
                {"HomeFoundationNotOwned", "You can't use home on someone else's house."},
                {"HomeFoundationUnderneathFoundation", "You can't use home on a foundation that is underneath another foundation."},
                {"HomeFoundationNotFriendsOwned", "You or a friend need to own the house to use home!"},
                {"HomeRemovedInvalid", "Your home '{0}' was removed because not on a foundation or not owned!"},
                {"HighWallCollision", "High Wall Collision!"},
                {"HomeRemovedInsideBlock", "Your home '{0}' was removed because inside a foundation!"},
                {"HomeRemove", "You have removed your home {0}!"},
                {"HomeDelete", "You have removed {0}'s home '{1}'!"},
                {"HomeList", "The following homes are available:"},
                {"HomeListEmpty", "You haven't saved any homes!"},
                {"HomeMaxLocations", "Unable to set your home here, you have reached the maximum of {0} homes!"},
                {"HomeQuota", "You have set {0} of the maximum {1} homes!"},
                {"HomeTPStarted", "Teleporting to your home {0} in {1} seconds!"},
                {"PayToTown", "Standard payment of {0} applies to all {1} teleports!"},
                {"PayToTPR", "Standard payment of {0} applies to all tprs!"},
                {"HomeTPCooldown", "Your teleport is currently on cooldown. You'll have to wait {0} for your next teleport."},
                {"HomeTPCooldownBypass", "Your teleport was currently on cooldown. You chose to bypass that by paying {0} from your balance."},
                {"HomeTPCooldownBypassF", "Your teleport is currently on cooldown. You do not have sufficient funds - {0} - to bypass."},
                {"HomeTPCooldownBypassP", "You may choose to pay {0} to bypass this cooldown." },
                {"HomeTPCooldownBypassP2", "Type /home NAME {0}." },
                {"HomeTPLimitReached", "You have reached the daily limit of {0} teleports today!"},
                {"HomeTPAmount", "You have {0} home teleports left today!"},
                {"HomesListWiped", "You have wiped all the saved home locations!"},
                {"HomeTPBuildingBlocked", "You can't set your home if you are not allowed to build in this zone!"},
                {"HomeTPSwimming", "You can't set your home while swimming!"},
                {"HomeTPCrafting", "You can't set your home while crafting!"},
                {"Request", "You've requested a teleport to {0}!"},
                {"RequestTarget", "{0} requested to be teleported to you! Use '/tpa' to accept!"},
                {"RequestTargetOff", "Your request has been cancelled as the target is offline now." },
                {"TPR_NoClan_NoFriend_NoTeam", "This command is only available to friends or teammates or clanmates!"},
                {"PendingRequest", "You already have a request pending, cancel that request or wait until it gets accepted or times out!"},
                {"PendingRequestTarget", "The player you wish to teleport to already has a pending request, try again later!"},
                {"NoPendingRequest", "You have no pending teleport request!"},
                {"AcceptOnRoof", "You can't accept a teleport while you're on a ceiling, get to ground level!"},
                {"Accept", "{0} has accepted your teleport request! Teleporting in {1} seconds!"},
                {"AcceptTarget", "You've accepted the teleport request of {0}!"},
                {"AcceptToggleOff", "You've disabled automatic /tpa!"},
                {"AcceptToggleOn", "You've enabled automatic /tpa!"},
                {"NotAllowed", "You are not allowed to use this command!"},
                {"Success", "You teleported to {0}!"},
                {"SuccessTarget", "{0} teleported to you!"},
                {"Cancelled", "Your teleport request to {0} was cancelled!"},
                {"CancelledTarget", "{0} teleport request was cancelled!"},
                {"TPCancelled", "Your teleport was cancelled!"},
                {"TPCancelledTarget", "{0} cancelled teleport!"},
                {"TPYouCancelledTarget", "You cancelled {0} teleport!"},
                {"TimedOut", "{0} did not answer your request in time!"},
                {"TimedOutTarget", "You did not answer {0}'s teleport request in time!"},
                {"TargetDisconnected", "{0} has disconnected, your teleport was cancelled!"},
                {"TPRCooldown", "Your teleport requests are currently on cooldown. You'll have to wait {0} to send your next teleport request."},
                {"TPRCooldownBypass", "Your teleport request was on cooldown. You chose to bypass that by paying {0} from your balance."},
                {"TPRCooldownBypassF", "Your teleport is currently on cooldown. You do not have sufficient funds - {0} - to bypass."},
                {"TPRCooldownBypassP", "You may choose to pay {0} to bypass this cooldown." },
                {"TPMoney", "{0} deducted from your account!"},
                {"TPNoMoney", "You do not have {0} in any account!"},
                {"TPRCooldownBypassP2", "Type /tpr {0}." },
                {"TPRCooldownBypassP2a", "Type /tpr NAME {0}." },
                {"TPRLimitReached", "You have reached the daily limit of {0} teleport requests today!"},
                {"TPRAmount", "You have {0} teleport requests left today!"},
                {"TPRTarget", "Your target is currently not available!"},
                {"TPDead", "You can't teleport while being dead!"},
                {"TPWounded", "You can't teleport while wounded!"},
                {"TPTooCold", "You're too cold to teleport!"},
                {"TPTooHot", "You're too hot to teleport!"},
                {"TPBoat", "You can't teleport while on a boat!"},
                {"TPHostile", "Can't teleport to outpost or bandit when hostile!"},
                {"TPJunkpile", "You can't teleport from a junkpile!"},
                {"HostileTimer", "Teleport available in {0} minutes."},
                {"TPMounted", "You can't teleport while seated!"},
                //{"TPInsideTerrainFrom", "You can't teleport while inside terrain!"},
                //{"TPInsideTerrainTo", "You can't teleport into a location that is inside terrain!"},
                {"TPBuildingBlocked", "You can't teleport while in a building blocked zone!"},
                {"TPAboveWater", "You can't teleport while above water!"},
                {"TPTargetBuildingBlocked", "You can't teleport in a building blocked zone!"},
                {"TPTargetInsideBlock", "You can't teleport into a foundation!"},
                {"TPTargetInsideEntity", "You can't teleport into another entity!"},
                {"TPTargetInsideRock", "You can't teleport into a rock!"},
                {"TPSwimming", "You can't teleport while swimming!"},
                {"TPCargoShip", "You can't teleport from the cargo ship!"},
                {"TPOilRig", "You can't teleport from the oil rig!"},
                {"TPExcavator", "You can't teleport from the excavator!"},
                {"TPHotAirBalloon", "You can't teleport to or from a hot air balloon!"},
                {"TPLift", "You can't teleport while in an elevator or bucket lift!"},
                {"TPBucketLift", "You can't teleport while in a bucket lift!"},
                {"TPRegLift", "You can't teleport while in an elevator!"},
                {"TPSafeZone", "You can't teleport from a safezone!"},
                {"TPFlagZone", "You can't teleport from this zone!"},
                {"TPNoEscapeBlocked", "You can't teleport while blocked!"},
                {"TPCrafting", "You can't teleport while crafting!"},
                {"TPBlockedItem", "You can't teleport while carrying: {0}!"},
                {"TPHomeSafeZoneOnly", "You can only teleport home from within a safe zone!" },
                {"TooCloseToMon", "You can't teleport so close to the {0}!"},
                {"TooCloseToCave", "You can't teleport so close to a cave!"},
                {"HomeTooCloseToCave", "You can't set home so close to a cave!"},
                {"HomeTooCloseToMon", "You can't set home so close to a monument!"},
                {"CannotTeleportFromHome", "You must leave your base to be able to teleport!"},
                {"WaitGlobalCooldown", "You must wait {0} on your global teleport cooldown!" },
                {"DM_TownTP", "You teleported to {0}!"},
                {"DM_TownTPNoLocation", "<color=yellow>{0}</color> location is currently not set!"},
                {"DM_TownTPDisabled", "<color=yellow>{0}</color> is currently disabled in config file!"},
                {"DM_TownTPLocation", "You have set the <color=yellow>{0}</color> location to {1}!"},
                {"DM_TownTPCreated", "You have created the command: <color=yellow>{0}</color>"},
                {"DM_TownTPRemoved", "You have removed the command: <color=yellow>{0}</color>"},
                {"DM_TownTPDoesNotExist", "Command does not exist: <color=yellow>{0}</color>"},
                {"DM_TownTPExists", "Command <color=yellow>{0}</color> already exists!"},
                {"DM_TownTPLocationsCleared", "You have cleared all locations for {0}!"},
                {"DM_TownTPStarted", "Teleporting to {0} in {1} seconds!"},
                {"DM_TownTPCooldown", "Your teleport is currently on cooldown. You'll have to wait {0} for your next teleport."},
                {"DM_TownTPCooldownBypass", "Your teleport request was on cooldown. You chose to bypass that by paying {0} from your balance."},
                {"DM_TownTPCooldownBypassF", "Your teleport is currently on cooldown. You do not have sufficient funds ({0}) to bypass."},
                {"DM_TownTPCooldownBypassP", "You may choose to pay {0} to bypass this cooldown." },
                {"DM_TownTPCooldownBypassP2", "Type <color=yellow>/{0} {1}</color>" },
                {"DM_TownTPLimitReached", "You have reached the daily limit of {0} teleports today! You'll have to wait {1} for your next teleport."},
                {"DM_TownTPAmount", "You have {0} <color=yellow>{1}</color> teleports left today!"},

                { "Days", "Days" },
                { "Hours", "Hours" },
                { "Minutes", "Minutes" },
                { "Seconds", "Seconds" },

                {"Interrupted", "Your teleport was interrupted!"},
                {"InterruptedTarget", "{0}'s teleport was interrupted!"},
                {"Unlimited", "Unlimited"},
                {
                    "TPInfoGeneral", string.Join(NewLine, new[]
                    {
                        "Please specify the module you want to view the info of.",
                        "The available modules are: ",
                    })
                },
                {
                    "TPHelpGeneral", string.Join(NewLine, new[]
                    {
                        "/tpinfo - Shows limits and cooldowns.",
                        "Please specify the module you want to view the help of.",
                        "The available modules are: ",
                    })
                },
                {
                    "TPHelpadmintp", string.Join(NewLine, new[]
                    {
                        "As an admin you have access to the following commands:",
                        "/tp \"targetplayer\" - Teleports yourself to the target player.",
                        "/tp \"player\" \"targetplayer\" - Teleports the player to the target player.",
                        "/tp x y z - Teleports you to the set of coordinates.",
                        "/tpl - Shows a list of saved locations.",
                        "/tpl \"location name\" - Teleports you to a saved location.",
                        "/tpsave \"location name\" - Saves your current position as the location name.",
                        "/tpremove \"location name\" - Removes the location from your saved list.",
                        "/tpb - Teleports you back to the place where you were before teleporting.",
                        "/home radius \"radius\" - Find all homes in radius.",
                        "/home delete \"player name|id\" \"home name\" - Remove a home from a player.",
                        "/home tp \"player name|id\" \"name\" - Teleports you to the home location with the name 'name' from the player.",
                        "/home homes \"player name|id\" - Shows you a list of all homes from the player."
                    })
                },
                {
                    "TPHelphome", string.Join(NewLine, new[]
                    {
                        "With the following commands you can set your home location to teleport back to:",
                        "/home add \"name\" - Saves your current position as the location name.",
                        "/home list - Shows you a list of all the locations you have saved.",
                        "/home remove \"name\" - Removes the location of your saved homes.",
                        "/home \"name\" - Teleports you to the home location."
                    })
                },
                {
                    "TPHelptpr", string.Join(NewLine, new[]
                    {
                        "With these commands you can request to be teleported to a player or accept someone else's request:",
                        "/tpr \"player name\" - Sends a teleport request to the player.",
                        "/tpa - Accepts an incoming teleport request.",
                        "/tpat - Toggle automatic /tpa on incoming teleport requests.",
                        "/tpc - Cancel teleport or request."
                    })
                },
                {
                    "TPSettingsGeneral", string.Join(NewLine, new[]
                    {
                        "Please specify the module you want to view the settings of. ",
                        "The available modules are:",
                    })
                },
                {
                    "TPSettingshome", string.Join(NewLine, new[]
                    {
                        "Home System has the current settings enabled:",
                        "Time between teleports: {0}",
                        "Daily amount of teleports: {1}",
                        "Amount of saved Home locations: {2}"
                    })
                },
                {
                    "TPSettingsbandit", string.Join(NewLine, new[]
                    {
                        "Bandit System has the current settings enabled:",
                        "Time between teleports: {0}",
                        "Daily amount of teleports: {1}"
                    })
                },
                {
                    "TPSettingsoutpost", string.Join(NewLine, new[]
                    {
                        "Outpost System has the current settings enabled:",
                        "Time between teleports: {0}",
                        "Daily amount of teleports: {1}"
                    })
                },
                {
                    "TPSettingstpr", string.Join(NewLine, new[]
                    {
                        "TPR System has the current settings enabled:",
                        "Time between teleports: {0}",
                        "Daily amount of teleports: {1}"
                    })
                },
                {
                    "TPSettingstown", string.Join(NewLine, new[]
                    {
                        "Town System has the current settings enabled:",
                        "Time between teleports: {0}",
                        "Daily amount of teleports: {1}"
                    })
                },
                {
                    "TPSettingsdynamic", string.Join(NewLine, new[]
                    {
                        "{0} System has the current settings enabled:",
                        "Time between teleports: {1}",
                        "Daily amount of teleports: {2}"
                    })
                },
                {"PlayerNotFound", "The specified player couldn't be found please try again!"},
                {"MultiplePlayers", "Found multiple players: {0}"},
                {"CantTeleportToSelf", "You can't teleport to yourself!"},
                {"CantTeleportPlayerToSelf", "You can't teleport a player to himself!"},
                {"TeleportPendingTPC", "You can't initiate another teleport while you have a teleport pending! Use /tpc to cancel this."},
                {"TeleportPendingTarget", "You can't request a teleport to someone who's about to teleport!"},
                {"LocationExists", "A location with this name already exists at {0}!"},
                {"LocationExistsNearby", "A location with the name {0} already exists near this position!"},
                {"LocationNotFound", "Couldn't find a location with that name!"},
                {"NoPreviousLocationSaved", "No previous location saved!"},
                {"HomeExists", "You have already saved a home location by this name!"},
                {"HomeExistsNearby", "A home location with the name {0} already exists near this position!"},
                {"HomeNotFound", "Couldn't find your home with that name!"},
                {"InvalidCoordinates", "The coordinates you've entered are invalid!"},
                {"InvalidHelpModule", "Invalid module supplied!"},
                {"InvalidCharacter", "You have used an invalid character, please limit yourself to the letters a to z and numbers."},
                {
                    "SyntaxCommandTP", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tp command as follows:",
                        "/tp \"targetplayer\" - Teleports yourself to the target player.",
                        "/tp \"player\" \"targetplayer\" - Teleports the player to the target player.",
                        "/tp x y z - Teleports you to the set of coordinates.",
                        "/tp \"player\" x y z - Teleports the player to the set of coordinates."
                    })
                },
                {
                    "SyntaxCommandTPL", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpl command as follows:",
                        "/tpl - Shows a list of saved locations.",
                        "/tpl \"location name\" - Teleports you to a saved location."
                    })
                },
                {
                    "SyntaxCommandTPSave", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpsave command as follows:",
                        "/tpsave \"location name\" - Saves your current position as 'location name'."
                    })
                },
                {
                    "SyntaxCommandTPRemove", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpremove command as follows:",
                        "/tpremove \"location name\" - Removes the location with the name 'location name'."
                    })
                },
                {
                    "SyntaxCommandTPN", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpn command as follows:",
                        "/tpn \"targetplayer\" - Teleports yourself the default distance behind the target player.",
                        "/tpn \"targetplayer\" \"distance\" - Teleports you the specified distance behind the target player."
                    })
                },
                {
                    "SyntaxCommandSetHome", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home add command as follows:",
                        "/home add \"name\" - Saves the current location as your home with the name 'name'."
                    })
                },
                {
                    "SyntaxCommandRemoveHome", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home remove command as follows:",
                        "/home remove \"name\" - Removes the home location with the name 'name'."
                    })
                },
                {
                    "SyntaxCommandHome", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home command as follows:",
                        "/home \"name\" - Teleports yourself to your home with the name 'name'.",
                        "/home \"name\" pay - Teleports yourself to your home with the name 'name', avoiding cooldown by paying for it.",
                        "/home add \"name\" - Saves the current location as your home with the name 'name'.",
                        "/home list - Shows you a list of all your saved home locations.",
                        "/home remove \"name\" - Removes the home location with the name 'name'."
                    })
                },
                {
                    "SyntaxCommandHomeAdmin", string.Join(NewLine, new[]
                    {
                        "/home radius \"radius\" - Shows you a list of all homes in radius(10).",
                        "/home delete \"player name|id\" \"name\" - Removes the home location with the name 'name' from the player.",
                        "/home tp \"player name|id\" \"name\" - Teleports you to the home location with the name 'name' from the player.",
                        "/home homes \"player name|id\" - Shows you a list of all homes from the player."
                    })
                },
                {
                    "SyntaxCommandTown", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /town command as follows:",
                        "/town - Teleports yourself to town.",
                        "/town pay - Teleports yourself to town, paying the penalty."
                    })
                },
                {
                    "SyntaxCommandTownAdmin", string.Join(NewLine, new[]
                    {
                        "/town set - Saves the current location as town.",
                    })
                },
                {
                    "SyntaxCommandOutpost", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /outpost command as follows:",
                        "/outpost - Teleports yourself to the Outpost.",
                        "/outpost pay - Teleports yourself to the Outpost, paying the penalty."
                    })
                },
                {
                    "SyntaxCommandOutpostAdmin", string.Join(NewLine, new[]
                    {
                        "/outpost set - Saves the current location as Outpost.",
                    })
                },
                {
                    "SyntaxCommandBandit", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /bandit command as follows:",
                        "/bandit - Teleports yourself to the Bandit Town.",
                        "/bandit pay - Teleports yourself to the Bandit Town, paying the penalty."
                    })
                },
                {
                    "SyntaxCommandBanditAdmin", string.Join(NewLine, new[]
                    {
                        "/bandit set - Saves the current location as Bandit Town.",
                    })
                },
                {
                    "SyntaxCommandHomeDelete", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home delete command as follows:",
                        "/home delete \"player name|id\" \"name\" - Removes the home location with the name 'name' from the player."
                    })
                },
                {
                    "SyntaxCommandHomeAdminTP", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home tp command as follows:",
                        "/home tp \"player name|id\" \"name\" - Teleports you to the home location with the name 'name' from the player."
                    })
                },
                {
                    "SyntaxCommandHomeHomes", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home homes command as follows:",
                        "/home homes \"player name|id\" - Shows you a list of all homes from the player."
                    })
                },
                {
                    "SyntaxCommandListHomes", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /home list command as follows:",
                        "/home list - Shows you a list of all your saved home locations."
                    })
                },
                {
                    "SyntaxCommandTPR", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpr command as follows:",
                        "/tpr \"player name\" - Sends out a teleport request to 'player name'."
                    })
                },
                {
                    "SyntaxCommandTPA", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpa command as follows:",
                        "/tpa - Accepts an incoming teleport request."
                    })
                },
                {
                    "SyntaxCommandTPC", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the /tpc command as follows:",
                        "/tpc - Cancels an teleport request."
                    })
                },
                {
                    "SyntaxConsoleCommandToPos", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the teleport.topos console command as follows:",
                        " > teleport.topos \"player\" x y z"
                    })
                },
                {
                    "SyntaxConsoleCommandToPlayer", string.Join(NewLine, new[]
                    {
                        "A Syntax Error Occurred!",
                        "You can only use the teleport.toplayer console command as follows:",
                        " > teleport.toplayer \"player\" \"target player\""
                    })
                },
                {"LogTeleport", "{0} teleported to {1}."},
                {"LogTeleportPlayer", "{0} teleported {1} to {2}."},
                {"LogTeleportBack", "{0} teleported back to previous location."}
            };

            foreach (var key in config.DynamicCommands.Keys)
            {
                en[key] = key;
            }

            lang.RegisterMessages(en, this, "en");

            var ru = new Dictionary<string, string>
            {
                {"ErrorTPR", "Телепорт к {0} блокирован ({1})"},
                {"AdminTP", "Вы телепортированы к {0}!"},
                {"AdminTPTarget", "{0} телепортировал вас!"},
                {"AdminTPPlayers", "Вы телепортировали {0} к {1}!"},
                {"AdminTPPlayer", "{0} телепортировал вас к {1}!"},
                {"AdminTPPlayerTarget", "{0} телепортировал {1} к вам!"},
                {"AdminTPCoordinates", "Вы телепортированы к {0}!"},
                {"AdminTPTargetCoordinates", "Вы телепортировали {0} к {1}!"},
                {"AdminTPOutOfBounds", "Вы пытались телепортироваться к координатам вне границ карты!"},
                {"AdminTPBoundaries", "Значения X и Z должны быть между -{0} и {0}, а значение Y между -100 и 2000!"},
                {"AdminTPLocation", "Вы телепортированы к {0}!"},
                {"AdminTPLocationSave", "Вы сохранили текущее местоположение!"},
                {"AdminTPLocationRemove", "Вы удалили местоположение {0}!"},
                {"AdminLocationList", "Доступны следующие местоположения:"},
                {"AdminLocationListEmpty", "Вы не сохранили никаких местоположений!"},
                {"AdminTPBack", "Вы телепортированы назад, в ваше предыдущее местоположение!"},
                {"AdminTPBackSave", "Ваше предыдущее местоположение сохранено, используйте <color=yellow>/tpb</color>, чтобы телепортироваться назад!"},
                {"AdminTPTargetCoordinatesTarget", "{0} телепортировал вас к {1}!"},
                {"AdminTPConsoleTP", "Вы были телепортированы к {0}"},
                {"AdminTPConsoleTPPlayer", "Вы были телепортированы к {0}"},
                {"AdminTPConsoleTPPlayerTarget", "{0} был телепортирован к вам!"},
                {"HomeTP", "Вы телепортированы в ваш дом '{0}'!"},
                {"HomeAdminTP", "Вы телепортированы к дому '{1}' принадлежащему {0}!"},
                {"HomeSave", "Вы сохранили текущее местоположение как ваш дом!"},
                {"HomeNoFoundation", "Использовать местоположение в качестве дома разрешено только на фундаменте!"},
                {"HomeFoundationNotOwned", "Вы не можете использовать команду home в чужом доме."},
                {"HomeFoundationUnderneathFoundation", "Вы не можете использовать команду home на фундаменте, который находится под другим фундаментом."},
                {"HomeFoundationNotFriendsOwned", "Вы, или ваш друг, должны быть владельцем дома, чтобы использовать команду home!"},
                {"HomeRemovedInvalid", "Ваш дом '{0}' был удалён потому, что не на фундаменте, или у фундамента новый владелец!"},
                {"HighWallCollision", "Столкновение Высоких Стен!"},
                {"HomeRemovedInsideBlock", "Ваш дом '{0}' был удалён потому, что внутри фундамента!"},
                {"HomeRemove", "Вы удалили свой дом {0}!"},
                {"HomeDelete", "Вы удалили дом '{1}' принадлежащий {0}!"},
                {"HomeList", "Доступны следующие дома:"},
                {"HomeListEmpty", "Вы не сохранили ни одного дома!"},
                {"HomeMaxLocations", "Невозможно установить здесь ваш дом, вы достигли лимита в {0} домов!"},
                {"HomeQuota", "Вы установили {0} из {1} максимально возможных домов!"},
                {"HomeTPStarted", "Телепортация в ваш дом {0} через {1} секунд!"},
                {"PayToTown", "Стандартный платеж {0} распространяется на все телепорты в город!"},
                {"PayToTPR", "Стандартный платеж {0} распространяется на все tpr'ы!"},
                {"HomeTPCooldown", "Ваш телепорт перезаряжается. Вам необходимо подождать {0} до следующей телепортации."},
                {"HomeTPCooldownBypass", "Ваш телепорт был на перезарядке. Вы выбрали избежать ожидания, оплатив {0} с вашего баланса."},
                {"HomeTPCooldownBypassF", "Ваш телепорт перезаряжается. У вас недостаточно средств - {0} - чтобы избежать ожидания."},
                {"HomeTPCooldownBypassP", "Вы можете выбрать оплатить {0} чтобы избежать ожидания перезарядки." },
                {"HomeTPCooldownBypassP2", "Напишите <color=yellow>/home \"название дома\" {0}</color>." },
                {"HomeTPLimitReached", "Вы исчерпали ежедневный лимит {0} телепортаций сегодня!"},
                {"HomeTPAmount", "У вас осталось {0} телепортаций домой сегодня!"},
                {"HomesListWiped", "Вы очистили все местоположения, сохранённые как дом!"},
                {"HomeTPBuildingBlocked", "Вы не можете сохранить местоположение в качестве дома, если у вас нет прав на строительство в этой зоне!"},
                {"HomeTPSwimming", "Вы не можете устанавливать местоположение а качестве дома пока плывёте!"},
                {"HomeTPCrafting", "Вы не можете устанавливать местоположение а качестве дома в процессе крафта!"},
                {"Request", "Вы запросили телепортацию к {0}!"},
                {"RequestTarget", "{0} запросил телепортацию к вам! Используйте <color=yellow>/tpa</color>, чтобы принять!"},
                {"RequestTargetOff", "Ваш запрос был отменен, так как цель сейчас не в сети." },
                {"TPR_NoClan_NoFriend_NoTeam", "Эта команда доступна только друзьям, участникам команды или клана!"},
                {"PendingRequest", "У вас уже есть активный запрос, отмените его, ожидайте подтверждения, либо отмены по таймауту!"},
                {"PendingRequestTarget", "У игрока, к которому вы хотите телепортироваться уже есть активный запрос, попробуйте позже!"},
                {"NoPendingRequest", "У вас нет активных запросов на телепортацию!"},
                {"AcceptOnRoof", "Вы не можете принять запрос на телепортацию стоя на потолке, спуститесь на уровень фундамента!"},
                {"Accept", "{0} принял ваш запрос! Телепортация через {1} секунд!"},
                {"AcceptTarget", "Вы приняли запрос на телепортацию {0}!"},
                {"AcceptToggleOff", "Вы отключили автоматическое /tpa!"},
                {"AcceptToggleOn", "Вы включили автоматическое /tpa!"},
                {"NotAllowed", "Вам не разрешено использовать эту команду!"},
                {"Success", "Вы телепортированы к {0}!"},
                {"SuccessTarget", "{0} телепортирован к вам!"},
                {"Cancelled", "Ваш запрос на телепортацию к {0} был отменён!"},
                {"CancelledTarget", "Запрос на телепортацию {0} был отменён!"},
                {"TPCancelled", "Ваша телепортация отменена!"},
                {"TPCancelledTarget", "{0} отменил телепортацию!"},
                {"TPYouCancelledTarget", "Вы отменили телепортацию {0}!"},
                {"TimedOut", "{0} не ответил на ваш запрос во время!"},
                {"TimedOutTarget", "Вы не ответили вовремя на запрос телепортации от {0}!"},
                {"TargetDisconnected", "{0} отключился, ваша телепортация отменена!"},
                {"TPRCooldown", "Ваши запросы на телепортацию в данный момент на перезарядке. Вам необходимо подождать {0} прежде чем отправить следующий запрос."},
                {"TPRCooldownBypass", "Ваши запросы на телепортацию были на перезарядке. Вы выбрали избежать ожидания, оплатив {0} с вашего баланса."},
                {"TPRCooldownBypassF", "Ваши запросы на телепортацию в данный момент на перезарядке. У вас недостаточно средств - {0} - чтобы избежать ожидания."},
                {"TPRCooldownBypassP", "Вы можете выбрать оплатить {0} чтобы избежать ожидания перезарядки." },
                {"TPMoney", "{0} списано с вашего аккаунта!"},
                {"TPNoMoney", "У вас нет {0} ни на одном аккаунте!"},
                {"TPRCooldownBypassP2", "Напишите <color=yellow>/tpr {0}</color>." },
                {"TPRCooldownBypassP2a", "Напишите <color=yellow>/tpr \"имя игрока\" {0}</color>." },
                {"TPRLimitReached", "Вы исчерпали ежедневный лимит {0} запросов на телепортацию сегодня!"},
                {"TPRAmount", "У вас осталось {0} запросов на телепортацию на сегодня!"},
                {"TPRTarget", "Ваша цель в данный момент не доступна!"},
                {"TPDead", "Вы не можете телепортироваться, пока мертвы!"},
                {"TPWounded", "Вы не можете телепортироваться, будучи раненым!"},
                {"TPTooCold", "Вам слишком холодно для телепортации!"},
                {"TPTooHot", "Вам слишком жарко для телепортации!"},
                {"TPBoat", "Вы не можете телепортироваться находясь на лодке!"},
                {"TPHostile", "Вы не можете телепортироваться в Город NPC или Лагерь бандитов пока враждебны!"},
                {"TPJunkpile", "Вы не можете телепортироваться с кучи мусора"},
                {"HostileTimer", "Телепорт станет доступен через {0} минут."},
                {"TPMounted", "Вы не можете телепортироваться, когда сидите!"},
                {"TPBuildingBlocked", "Вы не можете телепортироваться, находясь в зоне блокировки строительства!"},
                //{"TPInsideTerrainFrom", "Вы не можете телепортироваться, находясь внутри местности!"},
                //{"TPInsideTerrainTo", "Вы не можете телепортироваться в место, которое находится внутри местности!"},
                {"TPAboveWater", "Вы не можете телепортироваться находясь над водой!"},
                {"TPTargetBuildingBlocked", "Вы не можете телепортироваться в зону, где блокировано строительство!"},
                {"TPTargetInsideBlock", "Вы не можете телепортироваться в фундамент!"},
                {"TPTargetInsideRock", "Вы не можете телепортироваться в скалу!"},
                {"TPSwimming", "Вы не можете телепортироваться, пока плывёте!"},
                {"TPCargoShip", "Вы не можете телепортироваться с грузового корабля!"},
                {"TPOilRig", "Вы не можете телепортироваться с нефтяной вышки!"},
                {"TPExcavator", "Вы не можете телепортироваться с экскаватора!"},
                {"TPHotAirBalloon", "Вы не можете телепортироваться с, или на воздушный шар!"},
                {"TPLift", "Вы не можете телепортироваться находясь в лифте или подъемнике!"},
                {"TPBucketLift", "Вы не можете телепортироваться находясь в ковшевом подъемнике!"},
                {"TPRegLift", "Вы не можете телепортироваться находясь в лифте!"},
                {"TPSafeZone", "Вы не можете телепортироваться из безопасной зоны!"},
                {"TPFlagZone", "Вы не можете телепортироваться из этой зоны!"},
                {"TPNoEscapeBlocked", "Вы не можете телепортироваться пока активна блокировка!"},
                {"TPCrafting", "Вы не можете телепортироваться в процессе крафта!"},
                {"TPBlockedItem", "Вы не можете телепортироваться пока несёте: {0}!"},
                {"TooCloseToMon", "Вы не можете телепортироваться так близко к {0}!"},
                {"TPHomeSafeZoneOnly", "Вы можете телепортироваться домой только из безопасной зоны!" },
                {"TooCloseToCave", "Вы не можете телепортироваться так близко к пещере!"},
                {"HomeTooCloseToCave", "Вы не можете сохранить местоположение в качестве дома так близко к пещере!"},
                {"HomeTooCloseToMon", "Вы не можете сохранить местоположение в качестве дома так близко к монументу!"},
                {"CannotTeleportFromHome", "Вы должны выйти из вашей базы, прежде чем телепортироваться!"},
                {"WaitGlobalCooldown", "Вы должны подождать {0}, пока ваш глобальный телепорт перезаряжается!" },

                {"DM_TownTP", "Вы телепортированы в {0}!"},
                {"DM_TownTPNoLocation", "Местоположение <color=yellow>{0}</color> в данный момент не установлено!"},
                {"DM_TownTPDisabled", "<color=yellow>{0}</color> в данный момент отключен в файле настройек!"},
                {"DM_TownTPLocation", "Вы установили местоположение <color=yellow>{0}</color> в {1}!"},
                {"DM_TownTPCreated", "Вы создали команду: <color=yellow>{0}</color>"},
                {"DM_TownTPRemoved", "Вы удалили команду: <color=yellow>{0}</color>"},
                {"DM_TownTPDoesNotExist", "Команда не существует: <color=yellow>{0}</color>"},
                {"DM_TownTPExists", "Команда <color=yellow>{0}</color> уже сущуствует!"},
                {"DM_TownTPLocationsCleared", "You have cleared all locations for {0}!"},
                {"DM_TownTPStarted", "Телепортация в {0} через {1} секунд!"},
                {"DM_TownTPCooldown", "Ваш телепорт перезаряжается. Вам необходимо подождать {0} до следующей телепортации."},
                {"DM_TownTPCooldownBypass", "Ваш телепорт был на перезарядке. Вы выбрали избежать ожидания, оплатив {0} с вашего баланса."},
                {"DM_TownTPCooldownBypassF", "Ваш телепорт перезаряжается. У вас недостаточно средств ({0}) чтобы избежать ожидания."},
                {"DM_TownTPCooldownBypassP", "Вы можете выбрать оплатить {0} чтобы избежать ожидания перезарядки." },
                {"DM_TownTPCooldownBypassP2", "Введите <color=yellow>/{0} {1}</color>" },
                {"DM_TownTPLimitReached", "Вы исчерпали ежедневный лимит {0} телепортаций сегодня! Вам необходимо подождать {1} до следующей телепортации."},
                {"DM_TownTPAmount", "У вас осталось {0} телепортаций <color=yellow>{1}</color> сегодня!"},

                {"Days", "дней" },
                {"Hours", "часов" },
                {"Minutes", "минут" },
                {"Seconds", "секунд" },

                {"Interrupted", "Ваша телепортация была прервана!"},
                {"InterruptedTarget", "Телепортация {0} была прервана!"},
                {"Unlimited", "Не ограничено"},
                {
                    "TPInfoGeneral", string.Join(NewLine, new[]
                    {
                        "Пожалуйста, укажите модуль, о котором вы хотите просмотреть информацию.",
                        "Доступные модули: ",
                    })
                },
                {
                    "TPHelpGeneral", string.Join(NewLine, new[]
                    {
                        "<color=yellow>/tpinfo</color> - Отображает лимиты и перезарядки.",
                        "Пожалуйста, укажите модуль, по которому вы хотите получить помощь.",
                        "Доступные модули: ",
                    })
                },
                {
                    "TPHelpadmintp", string.Join(NewLine, new[]
                    {
                        "Как админ, вы имеете доступ к следующим командам:",
                        "<color=yellow>/tp \"имя игрока\"</color> - Телепортирует вас к указанному игроку.",
                        "<color=yellow>/tp \"имя игрока\" \"имя игрока 2\"</color> - Телепортирует игрока с именем 'имя игрока' к игроку 'имя игрока 2'.",
                        "<color=yellow>/tp x y z</color> - Телепортирует вас к указанным координатам.",
                        "<color=yellow>/tpl</color> - Отображает список сохранённых местоположений.",
                        "<color=yellow>/tpl \"название местоположения\"</color> - Телепортирует вас в сохранённое местоположение.",
                        "<color=yellow>/tpsave \"название местоположения\"</color> - Сохраняет ваше текущее местоположение с указанным названием.",
                        "<color=yellow>/tpremove \"название местоположения\"</color> - Удаляет местоположение из списка сохранённых.",
                        "<color=yellow>/tpb</color> - Телепортирует вас назад на место, где вы были перед телепортацией.",
                        "<color=yellow>/home radius \"радиус\"</color> - Найти все дома в радиусе.",
                        "<color=yellow>/home delete \"имя игрока или ID\" \"название дома\"</color> - Удаляет дом с указанным именем принадлежащий указанному игроку.",
                        "<color=yellow>/home tp \"имя игрока или ID\" \"название дома\"</color> - Телепортирует вас в дом игрока с указанным названием принадлежащий указанному игроку.",
                        "<color=yellow>/home homes \"имя игрока или ID\"</color> - Отображает вам список всех домов, принадлежащих указанному игроку."
                    })
                },
                {
                    "TPHelphome", string.Join(NewLine, new[]
                    {
                        "Используя следующие команды, вы можете установить местоположение вашего дома, чтобы затем в него телепортироваться:",
                        "<color=yellow>/home add \"название дома\"</color> - Сохраняет ваше текущее местоположение как ваш дом с указанным названием.",
                        "<color=yellow>/home list</color> - Отображает список всех местоположений, сохранённых вами как дом.",
                        "<color=yellow>/home remove \"название дома\"</color> - Удаляет расположение сохранённого дома с указанным названием.",
                        "<color=yellow>/home \"название дома\"</color> - Телепортирует вас в местоположение дома с указанным названием."
                    })
                },
                {
                    "TPHelptpr", string.Join(NewLine, new[]
                    {
                        "Используя эти команды, вы можете отправить запрос на телепортацию к игроку, или принять чей-то запрос:",
                        "<color=yellow>/tpr \"имя игрока\"</color> - Отправляет запрос на телепортацию игроку с указанным именем.",
                        "<color=yellow>/tpa</color> - Принять входящий запрос на телепортацию.",
                        "<color=yellow>/tpat</color> - Вкл./Выкл. автоматическое принятие входящих запросов на телепортацию к вам /tpa.",
                        "<color=yellow>/tpc</color> - Отменить запрос на телепортацию."
                    })
                },
                {
                    "TPSettingsGeneral", string.Join(NewLine, new[]
                    {
                        "Пожалуйста, укажите модуль, настройки которого вы хотите просмотреть. ",
                        "Доступные модули:",
                    })
                },
                {
                    "TPSettingshome", string.Join(NewLine, new[]
                    {
                        "Система домов в данный момент имеет следующие включённые параметры:",
                        "Время между телепортами: {0}",
                        "Ежедневный лимит телепортаций: {1}",
                        "Количество сохранённых домов: {2}"
                    })
                },
                {
                    "TPSettingsbandit", string.Join(NewLine, new[]
                    {
                        "Система Лагерь бандитов в данный момент имеет следующие включённые параметры:",
                        "Время между телепортами: {0}",
                        "Ежедневный лимит телепортаций: {1}"
                    })
                },
                {
                    "TPSettingsoutpost", string.Join(NewLine, new[]
                    {
                        "Система Город NPC в данный момент имеет следующие включённые параметры:",
                        "Время между телепортами: {0}",
                        "Ежедневный лимит телепортаций: {1}"
                    })
                },
                {
                    "TPSettingstpr", string.Join(NewLine, new[]
                    {
                        "Система TPR в данный момент имеет следующие включённые параметры:",
                        "Время между телепортами: {0}",
                        "Ежедневный лимит телепортаций: {1}"
                    })
                },
                {
                    "TPSettingstown", string.Join(NewLine, new[]
                    {
                        "В Системе Городов включены следующие параметры:",
                        "Время между телепортами: {0}",
                        "Ежедневный лимит телепортаций: {1}"
                    })
                },
                {
                    "TPSettingsdynamic", string.Join(NewLine, new[]
                    {
                        "В Системе {0} включены следующие параметры:",
                        "Время между телепортами: {1}",
                        "Ежедневный лимит телепортаций: {2}"
                    })
                },
                {"PlayerNotFound", "Указанный игрок не обнаружен, пожалуйста попробуйте ещё раз!"},
                {"MultiplePlayers", "Найдено несколько игроков: {0}"},
                {"CantTeleportToSelf", "Вы не можете телепортироваться к самому себе!"},
                {"CantTeleportPlayerToSelf", "Вы не можете телепортровать игрока к самому себе!"},
                {"TeleportPendingTPC", "Вы не можете инициировать телепортацию, пока у вас есть активный запрос! Используйте <color=yellow>/tpc</color> чтобы отменить его."},
                {"TeleportPendingTarget", "Вы не можете отправить запрос к тому, кто в процессе телепортации!"},
                {"LocationExists", "Местоположение с таким названием уже существует в {0}!"},
                {"LocationExistsNearby", "Местоположение с названием {0} уже существует рядом с текущей позицией!"},
                {"LocationNotFound", "Не найдено местоположение с таким названием!"},
                {"NoPreviousLocationSaved", "Предыдущее местоположение не сохранено!"},
                {"HomeExists", "Вы уже сохранили дом с таким названием!"},
                {"HomeExistsNearby", "Дом с названием {0} уже существует рядом с текущей позицией!"},
                {"HomeNotFound", "Дом с таким названием не найден!"},
                {"InvalidCoordinates", "Вы указали неверные координаты!"},
                {"InvalidHelpModule", "Указан неверный модуль!"},
                {"InvalidCharacter", "Вы использовали недопустимый символ, ограничьтесь буквами от a до z и цифрами."},
                {
                    "SyntaxCommandTP", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tp</color> возможно только следующим образом:",
                        "<color=yellow>/tp \"имя игрока\"</color> - Телепортирует вас к указанному игроку.",
                        "<color=yellow>/tp \"имя игрока\" \"имя игрока 2\"</color> - Телепортирует игрока с именем 'имя игрока' к игроку 'имя игрока 2'.",
                        "<color=yellow>/tp x y z</color> - Телепортирует вас к указанным координатам.",
                        "<color=yellow>/tp \"имя игрока\" x y z</color> - Телепортирует игрока с именем 'имя игрока' к указанным координатам."
                    })
                },
                {
                    "SyntaxCommandTPL", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpl</color> возможно только следующим образом:",
                        "<color=yellow>/tpl</color> - Отображает список сохранённых местоположений.",
                        "<color=yellow>/tpl \"название местоположения\"</color> - Телепортирует вас в место с указанным названием."
                    })
                },
                {
                    "SyntaxCommandTPSave", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpsave</color> возможно только следующим образом:",
                        "<color=yellow>/tpsave \"название местоположения\"</color> - Сохраняет ваше текущее местоположение с указанным названием."
                    })
                },
                {
                    "SyntaxCommandTPRemove", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpremove</color> возможно только следующим образом:",
                        "<color=yellow>/tpremove \"название местоположения\"</color> - Удаляет местоположение с указанным названием."
                    })
                },
                {
                    "SyntaxCommandTPN", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpn</color> возможно только следующим образом:",
                        "<color=yellow>/tpn \"имя игрока\"</color> - Телепортирует вас на расстояние по умолчанию позади игрока с указанным именем.",
                        "<color=yellow>/tpn \"имя игрока\" \"расстояние\"</color> - Телепортирует вас на указанное расстояние позади игрока с указанным именем."
                    })
                },
                {
                    "SyntaxCommandSetHome", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home add</color> возможно только следующим образом:",
                        "<color=yellow>/home add \"название\"</color> - Сохраняет ваше текущее местоположение как ваш дом с указанным названием."
                    })
                },
                {
                    "SyntaxCommandRemoveHome", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home remove</color> возможно только следующим образом:",
                        "<color=yellow>/home remove \"название\"</color> - Удаляет местоположение дома с указанным названием."
                    })
                },
                {
                    "SyntaxCommandHome", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home</color> возможно только следующим образом:",
                        "<color=yellow>/home \"название\"</color> - Телепортирует вас в ваш дом с указанным названием.",
                        "<color=yellow>/home \"название\" pay</color> - Телепортирует вас в ваш дом с указанным названием, избегая перезарядки, заплатив за это.",
                        "<color=yellow>/home add \"название\"</color> - Сохраняет ваше текущее местоположение как ваш дом с указанным названием.",
                        "<color=yellow>/home list</color> - Отображает список всех местоположений, сохранённых вами как дом.",
                        "<color=yellow>/home remove \"название\"</color> - Удаляет местоположение дома с указанным названием."
                    })
                },
                {
                    "SyntaxCommandHomeAdmin", string.Join(NewLine, new[]
                    {
                        "<color=yellow>/home radius \"радиус\"</color> - Отображает список всех домов в радиусе(10).",
                        "<color=yellow>/home delete \"имя игрока или ID\" \"название\"</color> - Удаляет дом с указанным названием, принадлежащий указанному игроку.",
                        "<color=yellow>/home tp \"имя игрока или ID\" \"название\"</color> - Телепортирует вас в дом с указанным названием, принадлежащий указанному игроку.",
                        "<color=yellow>/home homes \"имя игрока или ID\"</color> - Отображает вам список всех домов, принадлежащих указанному игроку."
                    })
                },
                {
                    "SyntaxCommandTown", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/town</color> возможно только следующим образом:",
                        "<color=yellow>/town</color> - Телепортирует вас в Город.",
                        "<color=yellow>/town pay</color> - Телепортирует вас в Город с оплатой штрафа."
                    })
                },
                {
                    "SyntaxCommandTownAdmin", string.Join(NewLine, new[]
                    {
                        "<color=yellow>/town set</color> - Сохраняет текущее местоположение как Город.",
                    })
                },
                {
                    "SyntaxCommandOutpost", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/outpost</color> возможно только следующим образом:",
                        "<color=yellow>/outpost</color> - Телепортирует вас в Город NPC.",
                        "<color=yellow>/outpost pay</color> - Телепортирует вас в Город NPC с оплатой штрафа."
                    })
                },
                {
                    "SyntaxCommandOutpostAdmin", string.Join(NewLine, new[]
                    {
                        "<color=yellow>/outpost set</color> - Сохраняет текущее местоположение как Город NPC.",
                    })
                },
                {
                    "SyntaxCommandBandit", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/bandit</color> возможно только следующим образом:",
                        "<color=yellow>/bandit</color> - Телепортирует вас в Лагерь бандитов.",
                        "<color=yellow>/bandit pay</color> - Телепортирует вас в Лагерь бандитов с оплатой штрафа."
                    })
                },
                {
                    "SyntaxCommandBanditAdmin", string.Join(NewLine, new[]
                    {
                        "<color=yellow>/bandit set</color> - Сохраняет текущее местоположение как Лагерь бандитов.",
                    })
                },
                {
                    "SyntaxCommandHomeDelete", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home delete</color> возможно только следующим образом:",
                        "<color=yellow>/home delete \"имя игрока или ID\" \"название\"</color> - Удаляет дом с указанным названием, принадлежащий указанному игроку."
                    })
                },
                {
                    "SyntaxCommandHomeAdminTP", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home tp</color> возможно только следующим образом:",
                        "<color=yellow>/home tp \"имя игрока или ID\" \"название\"</color> - Телепортирует вас в дом игрока с указанным названием, принадлежащий указанному игроку."
                    })
                },
                {
                    "SyntaxCommandHomeHomes", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home homes</color> возможно только следующим образом:",
                        "<color=yellow>/home homes \"имя игрока или ID\"</color> - Отображает вам список всех домов, принадлежащих указанному игроку."
                    })
                },
                {
                    "SyntaxCommandListHomes", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/home list</color> возможно только следующим образом:",
                        "<color=yellow>/home list</color> - Отображает список всех местоположений, сохранённых вами как дом."
                    })
                },
                {
                    "SyntaxCommandTPR", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpr</color> возможно только следующим образом:",
                        "<color=yellow>/tpr \"имя игрока или ID\"</color> - Отправляет указанному игроку запрос на телепортацию."
                    })
                },
                {
                    "SyntaxCommandTPA", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpa</color> возможно только следующим образом:",
                        "<color=yellow>/tpa</color> - Принять входящий запрос на телепортацию."
                    })
                },
                {
                    "SyntaxCommandTPC", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование команды <color=yellow>/tpc</color> возможно только следующим образом:",
                        "<color=yellow>/tpc</color> - Отменить запрос на телепортацию."
                    })
                },
                {
                    "SyntaxConsoleCommandToPos", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование консольной команды <color=orange>teleport.topos</color> возможно только следующим образом:",
                        " > <color=orange>teleport.topos \"имя игрока\" x y z</color>"
                    })
                },
                {
                    "SyntaxConsoleCommandToPlayer", string.Join(NewLine, new[]
                    {
                        "Произошла синтаксическая ошибка!",
                        "Использование консольной команды <color=orange>teleport.toplayer</color> возможно только следующим образом:",
                        " > <color=orange>teleport.toplayer \"имя игрока или ID\" \"имя игрока 2|id 2\"</color>"
                    })
                },
                {"LogTeleport", "{0} телепортирован к {1}."},
                {"LogTeleportPlayer", "{0} телепортировал {1} к {2}."},
                {"LogTeleportBack", "{0} телепортирован назад, в предыдущее местоположение."}
            };

            foreach (var key in config.DynamicCommands.Keys)
            {
                ru[key] = key;
            }

            lang.RegisterMessages(ru, this, "ru");
        }

        private void Init()
        {
            Unsubscribe(nameof(OnPlayerViolation));
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(OnPlayerDisconnected));
        }

        private string OnPlayerConnected(BasePlayer player)
        {
            var uid = UnityEngine.Random.Range(1000, 9999).ToString();
            var names = BasePlayer.activePlayerList.Select(x => x.displayName);

            while (_ids.ContainsKey(uid) || names.Any(name => name.Contains(uid)))
            {
                uid = UnityEngine.Random.Range(1000, 9999).ToString();
            }

            _ids[uid] = player;
            _players[player] = uid;

            return uid;
        }

        private Dictionary<string, StoredData> _DynamicData = new Dictionary<string, StoredData>();

        public class StoredData
        {
            public Dictionary<ulong, TeleportData> TPData = new Dictionary<ulong, TeleportData>();
            public bool Changed = true;
        }

        private void LoadDataAndPerms()
        {
            dataAdmin = GetFile("Admin");
            try { _Admin = dataAdmin.ReadObject<Dictionary<ulong, AdminData>>(); } catch { }
            if (_Admin == null) { _Admin = new Dictionary<ulong, AdminData>(); changedAdmin = true; }

            dataHome = GetFile("Home");
            try { _Home = dataHome.ReadObject<Dictionary<ulong, HomeData>>(); } catch { }
            if (_Home == null) { _Home = new Dictionary<ulong, HomeData>(); changedHome = true; }

            dataTPT = GetFile("TPT");
            try { TPTToggle = dataTPT.ReadObject<List<string>>(); } catch { }
            if (TPTToggle == null) { TPTToggle = new List<string>(); changedTPT = true; }

            foreach (var entry in config.DynamicCommands)
            {
                if (!entry.Value.Enabled) continue;

                var dcf = GetFile(entry.Key);
                Dictionary<ulong, TeleportData> data = null;

                try
                {
                    data = dcf.ReadObject<Dictionary<ulong, TeleportData>>();
                }
                catch
                {

                }

                if (data == null)
                {
                    data = new Dictionary<ulong, TeleportData>();
                }

                GetSettings(entry.Key).Teleports = _DynamicData[entry.Key] = new StoredData
                {
                    TPData = data,
                    Changed = true
                };
            }

            dataTPR = GetFile("TPR");
            try { _TPR = dataTPR.ReadObject<Dictionary<ulong, TeleportData>>(); } catch { }
            if (_TPR == null) { _TPR = new Dictionary<ulong, TeleportData>(); changedTPR = true; }

            dataDisabled = GetFile("DisabledCommands");
            try { DisabledCommandData = dataDisabled.ReadObject<DisabledData>(); } catch { }
            if (DisabledCommandData == null) { DisabledCommandData = new DisabledData(); }

            permission.RegisterPermission("nteleportation.norestrictions", this);
            permission.RegisterPermission("nteleportation.globalcooldownvip", this);
            permission.RegisterPermission(PermFoundationCheck, this);
            permission.RegisterPermission(PermDeleteHome, this);
            permission.RegisterPermission(PermHome, this);
            permission.RegisterPermission(PermHomeHomes, this);
            permission.RegisterPermission(PermImportHomes, this);
            permission.RegisterPermission(PermRadiusHome, this);
            permission.RegisterPermission(PermTp, this);
            permission.RegisterPermission(PermTpB, this);
            permission.RegisterPermission(PermTpR, this);
            permission.RegisterPermission(PermTpConsole, this);
            permission.RegisterPermission(PermTpHome, this);
            permission.RegisterPermission(PermTpT, this);
            permission.RegisterPermission(PermTpN, this);
            permission.RegisterPermission(PermTpL, this);
            permission.RegisterPermission(PermTpRemove, this);
            permission.RegisterPermission(PermTpSave, this);
            permission.RegisterPermission(PermWipeHomes, this);
            permission.RegisterPermission(PermCraftHome, this);
            permission.RegisterPermission(PermCraftTpR, this);
            permission.RegisterPermission(PermExempt, this);
            permission.RegisterPermission(PermTpMarker, this);

            CheckPerms(config.Home.VIPCooldowns);
            CheckPerms(config.Home.VIPCountdowns);
            CheckPerms(config.Home.VIPDailyLimits);
            CheckPerms(config.Home.VIPHomesLimits);
            CheckPerms(config.TPR.VIPCooldowns);
            CheckPerms(config.TPR.VIPCountdowns);
            CheckPerms(config.TPR.VIPDailyLimits);

            foreach (var entry in config.DynamicCommands)
            {
                RegisterCommand(entry.Key, entry.Value, false);
            }
        }

        private bool CanBypassRestrictions(string userid) => permission.UserHasPermission(userid, "nteleportation.norestrictions");

        private void RegisterCommand(string command, string callback, string perm = null)
        {
            if (!string.IsNullOrEmpty(command) && !command.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                AddCovalenceCommand(command, callback, perm);
            }
        }

        private void UnregisterCommand(string command)
        {
            covalence.UnregisterCommand(command, this);
        }

        private void RegisterCommand(string key, TownSettings settings, bool justCreated)
        {
            CheckPerms(settings.VIPCooldowns);
            CheckPerms(settings.VIPCountdowns);
            CheckPerms(settings.VIPDailyLimits);

            string tpPerm = $"{Name}.tp{key}".ToLower();
            string craftPerm = $"{Name}.craft{key}".ToLower();

            if (!permission.PermissionExists(tpPerm, this))
            {
                permission.RegisterPermission(tpPerm, this);
            }

            if (!permission.PermissionExists(craftPerm))
            {
                permission.RegisterPermission(craftPerm, this);
            }

            if (justCreated)
            {
                settings.Teleports = _DynamicData[key] = new StoredData();
            }
        }

        private DynamicConfigFile GetFile(string name)
        {
            var fileName = string.IsNullOrEmpty(config.Settings.DataFileFolder) ? $"{Name}{name}" : $"{config.Settings.DataFileFolder}{Path.DirectorySeparatorChar}{name}";
            var file = Interface.Oxide.DataFileSystem.GetFile(fileName);
            file.Settings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
            file.Settings.Converters = new JsonConverter[] { new UnityVector3Converter(), new CustomComparerDictionaryCreationConverter<string>(StringComparer.OrdinalIgnoreCase) };
            return file;
        }

        private void SetGlobalCooldown(BasePlayer player)
        {
            if (config.Settings.GlobalVIP > 0f && permission.UserHasPermission(player.UserIDString, "nteleportation.globalcooldownvip"))
            {
                ulong userid = player.userID;
                TeleportCooldowns[userid] = Time.time + config.Settings.GlobalVIP;
                timer.Once(config.Settings.GlobalVIP, () => TeleportCooldowns.Remove(userid));
            }
            else if (config.Settings.Global > 0f)
            {
                ulong userid = player.userID;
                TeleportCooldowns[userid] = Time.time + config.Settings.Global;
                timer.Once(config.Settings.Global, () => TeleportCooldowns.Remove(userid));
            }
        }

        private float GetGlobalCooldown(BasePlayer player)
        {
            float cooldown;
            if (!TeleportCooldowns.TryGetValue(player.userID, out cooldown))
            {
                return 0f;
            }

            return cooldown - Time.time;
        }

        private void CheckNewSave()
        {
            if (BuildingManager.server.buildingDictionary.Count == 0)
            {
                newSave = true;
            }

            if (!newSave)
            {
                return;
            }

            if (config.Settings.WipeOnUpgradeOrChange)
            {
                bool changed = false;

                if (_Home.Count > 0)
                {
                    changed = true;
                    _Home.Clear();
                    changedHome = true;
                }

                if (_TPR.Count > 0)
                {
                    changed = true;
                    _TPR.Clear();
                    changedTPR = true;
                }

                foreach (var entry in config.DynamicCommands.ToList())
                {
                    if (entry.Value.Location != Vector3.zero || entry.Value.Locations.Count > 0)
                    {
                        changed = true;
                        entry.Value.Location = Vector3.zero;
                        entry.Value.Locations.Clear();
                    }
                }

                if (!changed) return;
                Puts("Rust was upgraded or map changed - clearing homes and all locations!");                
                SaveConfig();
            }
            else
            {
                Puts("Rust was upgraded or map changed - homes, town, islands, outpost and bandit may be invalid!");
            }
        }

        void OnServerInitialized()
        {
            if (config.Settings.Interrupt.Hurt || config.Settings.Interrupt.Cold || config.Settings.Interrupt.Hot)
            {
                Subscribe(nameof(OnEntityTakeDamage));
            }

            Subscribe(nameof(OnPlayerSleepEnded));
            Subscribe(nameof(OnPlayerDisconnected));

            boundary = TerrainMeta.Size.x / 2;

            foreach (var item in config.Settings.BlockedItems)
            {
                var definition = ItemManager.FindItemDefinition(item.Key);
                if (definition == null)
                {
                    Puts("Blocked item not found: {0}", item.Key);
                    continue;
                }
                ReverseBlockedItems[definition.itemid] = item.Value;
            }

            InitializeDynamicCommands();
            LoadDataAndPerms();
            CheckNewSave();

            if (config.Settings.TPREnabled) AddCovalenceCommand("tpr", nameof(CommandTeleportRequest));
            if (config.Settings.HomesEnabled)
            {
                AddCovalenceCommand("home", nameof(CommandHome));
                AddCovalenceCommand("sethome", nameof(CommandSetHome));
                AddCovalenceCommand("listhomes", nameof(CommandListHomes));
                AddCovalenceCommand("removehome", nameof(CommandRemoveHome));
                AddCovalenceCommand("radiushome", nameof(CommandHomeRadius));
                AddCovalenceCommand("deletehome", nameof(CommandHomeDelete));
                AddCovalenceCommand("tphome", nameof(CommandHomeAdminTP));
                AddCovalenceCommand("homehomes", nameof(CommandHomeHomes));
            }

            AddCovalenceCommand("tnt", nameof(CommandToggle));
            AddCovalenceCommand("tp", nameof(CommandTeleport));
            AddCovalenceCommand("tpn", nameof(CommandTeleportNear));
            AddCovalenceCommand("tpl", nameof(CommandTeleportLocation));
            AddCovalenceCommand("tpsave", nameof(CommandSaveTeleportLocation));
            AddCovalenceCommand("tpremove", nameof(CommandRemoveTeleportLocation));
            AddCovalenceCommand("tpb", nameof(CommandTeleportBack));
            AddCovalenceCommand("tpa", nameof(CommandTeleportAccept));
            AddCovalenceCommand("tpat", nameof(CommandTeleportAcceptToggle));
            AddCovalenceCommand("wipehomes", nameof(CommandWipeHomes));
            AddCovalenceCommand("tphelp", nameof(CommandTeleportHelp));
            AddCovalenceCommand("tpinfo", nameof(CommandTeleportInfo));
            AddCovalenceCommand("tpc", nameof(CommandTeleportCancel));
            AddCovalenceCommand("teleport.toplayer", nameof(CommandTeleportII));
            AddCovalenceCommand("teleport.topos", nameof(CommandTeleportII));
            AddCovalenceCommand("teleport.importhomes", nameof(CommandImportHomes));
            AddCovalenceCommand("spm", nameof(CommandSphereMonuments));
            FindMonuments();
            foreach (var player in BasePlayer.activePlayerList) OnPlayerConnected(player);
        }

        void OnNewSave(string strFilename)
        {
            newSave = true;
        }

        void OnServerSave()
        {
            SaveTeleportsAdmin();
            SaveTeleportsHome();
            SaveTeleportsTPR();
            SaveTeleportsTPT();
            SaveTeleportsTown();
        }

        void OnServerShutdown() => OnServerSave();

        void Unload() => OnServerSave();

        void OnEntityTakeDamage(BasePlayer player, HitInfo hitInfo)
        {
            if (player == null || player.IsNpc || !player.userID.IsSteamId() || hitInfo == null) return;
            if (hitInfo.damageTypes.Has(DamageType.Fall) && teleporting.ContainsKey(player.userID))
            {
                hitInfo.damageTypes = new DamageTypeList();
                teleporting.Remove(player.userID);
                if (teleporting.Count == 0) Unsubscribe(nameof(OnPlayerViolation));
                return;
            }
            if (permission.UserHasPermission(player.userID.ToString(), PermExempt)) return;
            TeleportTimer teleportTimer;
            if (!TeleportTimers.TryGetValue(player.userID, out teleportTimer)) return;
            DamageType major = hitInfo.damageTypes.GetMajorityDamageType();

            NextTick(() =>
            {
                if (!player || !hitInfo.hasDamage) return;
                // 1.0.84 new checks for cold/heat based on major damage for the player
                if (major == DamageType.Cold)
                {
                    if (config.Settings.Interrupt.Cold && player.metabolism.temperature.value <= config.Settings.MinimumTemp)
                    {
                        PrintMsgL(teleportTimer.OriginPlayer, "TPTooCold");
                        if (teleportTimer.TargetPlayer != null)
                        {
                            PrintMsgL(teleportTimer.TargetPlayer, "InterruptedTarget", teleportTimer.OriginPlayer?.displayName);
                        }
                        teleportTimer.Timer.Destroy();
                        TeleportTimers.Remove(player.userID);
                    }
                }
                else if (major == DamageType.Heat)
                {
                    if (config.Settings.Interrupt.Hot && player.metabolism.temperature.value >= config.Settings.MaximumTemp)
                    {
                        PrintMsgL(teleportTimer.OriginPlayer, "TPTooHot");
                        if (teleportTimer.TargetPlayer != null)
                        {
                            PrintMsgL(teleportTimer.TargetPlayer, "InterruptedTarget", teleportTimer.OriginPlayer?.displayName);
                        }
                        teleportTimer.Timer.Destroy();
                        TeleportTimers.Remove(player.userID);
                    }
                }
                else if (config.Settings.Interrupt.Hurt)
                {
                    PrintMsgL(teleportTimer.OriginPlayer, "Interrupted");
                    if (teleportTimer.TargetPlayer != null)
                    {
                        PrintMsgL(teleportTimer.TargetPlayer, "InterruptedTarget", teleportTimer.OriginPlayer?.displayName);
                    }
                    teleportTimer.Timer.Destroy();
                    TeleportTimers.Remove(player.userID);
                }
            });
        }

        object OnPlayerViolation(BasePlayer player, AntiHackType type, float amount)
        {
            if (type == AntiHackType.InsideTerrain && teleporting.ContainsKey(player.userID))
            {
                return false;
            }

            return null;
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (!player || !teleporting.ContainsKey(player.userID)) return;
            ulong userID = player.userID;
            timer.Once(3f, () =>
            {
                teleporting.Remove(userID);

                if (teleporting.Count == 0) Unsubscribe(nameof(OnPlayerViolation));
            });
            SendEffect(player);
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            if (!player) return;
            Timer reqTimer;
            if (PendingRequests.TryGetValue(player.userID, out reqTimer))
            {
                var originPlayer = PlayersRequests[player.userID];
                if (originPlayer)
                {
                    PlayersRequests.Remove(originPlayer.userID);
                    PrintMsgL(originPlayer, "RequestTargetOff");
                }
                reqTimer.Destroy();
                PendingRequests.Remove(player.userID);
                PlayersRequests.Remove(player.userID);
            }
            TeleportTimer teleportTimer;
            if (TeleportTimers.TryGetValue(player.userID, out teleportTimer))
            {
                teleportTimer.Timer.Destroy();
                TeleportTimers.Remove(player.userID);
            }
            teleporting.Remove(player.userID);
        }

        private void SaveTeleportsAdmin()
        {
            if (_Admin == null || !changedAdmin) return;
            dataAdmin.WriteObject(_Admin);
            changedAdmin = false;
        }

        private void SaveTeleportsHome()
        {
            if (_Home == null || !changedHome) return;
            dataHome.WriteObject(_Home);
            changedHome = false;
        }

        private void SaveTeleportsTPR()
        {
            if (_TPR == null || !changedTPR) return;
            dataTPR.WriteObject(_TPR);
            changedTPR = false;
        }

        private void SaveTeleportsTPT()
        {
            if (TPTToggle == null || !changedTPT) return;
            dataTPT.WriteObject(TPTToggle);
            changedTPT = false;
        }

        private void SaveTeleportsTown()
        {
            foreach (var entry in _DynamicData.ToList())
            {
                if (entry.Value.Changed)
                {
                    var fileName = string.IsNullOrEmpty(config.Settings.DataFileFolder) ? $"{Name}{entry.Key}" : $"{config.Settings.DataFileFolder}{Path.DirectorySeparatorChar}{entry.Key}";
                    Interface.Oxide.DataFileSystem.WriteObject(fileName, entry.Value.TPData);
                    entry.Value.Changed = false;
                }
            }
        }

        private void SaveLocation(BasePlayer player, Vector3 position)
        {
            if (player == null || _Admin == null || !IsAllowed(player, PermTpB)) return;
            AdminData adminData;
            if (!_Admin.TryGetValue(player.userID, out adminData) || adminData == null)
                _Admin[player.userID] = adminData = new AdminData();
            adminData.PreviousLocation = position;
            changedAdmin = true;
            PrintMsgL(player, "AdminTPBackSave");
        }

        private void RemoveLocation(BasePlayer player)
        {
            AdminData adminData;
            if (!_Admin.TryGetValue(player.userID, out adminData))
                return;
            adminData.PreviousLocation = Vector3.zero;
            changedAdmin = true;
        }

        char[] chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz".ToCharArray();
        private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

        string RandomString(int minAmount = 4, int maxAmount = 10)
        {
            _sb.Length = 0;

            for (int i = 0; i < UnityEngine.Random.Range(minAmount, maxAmount + 1); i++)
                _sb.Append(chars.GetRandom());

            return _sb.ToString();
        }

        void FindMonuments()
        {
            var bandit = GetSettings("bandit");
            var outpost = GetSettings("outpost");
            var realWidth = 0f;
            string name = null;
            foreach (var monument in UnityEngine.Object.FindObjectsOfType<MonumentInfo>())
            {
                var monPos = monument.transform.position;
                name = monument.displayPhrase.english.TrimEnd();

                if (string.IsNullOrEmpty(name))
                {
                    if (monument.name.Contains("cave"))
                    {
                        name = (monument.name.Contains("cave_small") ? "Small Cave" : monument.name.Contains("cave_medium") ? "Medium Cave" : "Large Cave") + ":" + RandomString();
                    }
                    else name = monument.name;
                }
                realWidth = monument.name == "OilrigAI" ? 100f : monument.name == "OilrigAI2" ? 200f : 0f;
#if DEBUG
                Puts($"Found {name}, extents {monument.Bounds.extents}");
#endif
                if (realWidth > 0f)
                {
#if DEBUG
                    Puts($"  corrected to {realWidth}");
#endif
                }
                if (monument.name.Contains("cave"))
                {
#if DEBUG
                    Puts("  Adding to cave list");
#endif
                    if (caves.ContainsKey(name)) name += RandomString();
                    caves.Add(name, monPos);
                }
                else if (monument.name == outpostPrefab)
                {
                    if (outpost == null)
                    {
                        outpostEnabled = false;
                        continue;
                    }

                    float radius = monument.Bounds.size.Max();

                    if (outpost.Location != Vector3.zero && outpost.Locations.Exists(a => OutOfRange(monument, a)))
                    {
#if DEBUG
                        Puts("Invalid Outpost location detected");
#endif
                        outpost.Location = Vector3.zero;
                        outpost.Locations = new List<Vector3>();
                    }
                    if (config.Settings.AutoGenOutpost && outpost.Location == Vector3.zero)
                    {
#if DEBUG
                        Puts("  Adding Outpost target");
#endif
                        bool changedOutpost = false;
                        var ents = Pool.GetList<BaseEntity>();
                        Vis.Entities(monPos, radius, ents);
                        foreach (BaseEntity entity in ents)
                        {
                            if (OutOfRange(monument, entity.transform.position))
                            {
                                continue;
                            }
                            if (entity.prefabID == 3858860623)
                            {
                                outpost.Location = entity.transform.position + entity.transform.forward + new Vector3(0f, 1f, 0f);
                                if (!outpost.Locations.Contains(outpost.Location)) outpost.Locations.Add(outpost.Location);
                                changedOutpost = true;
                            }
                            else if (entity is Workbench)
                            {
                                outpost.Location = entity.transform.position + entity.transform.forward + new Vector3(0f, 1f, 0f);
                                if (!outpost.Locations.Contains(outpost.Location)) outpost.Locations.Add(outpost.Location);
                                changedOutpost = true;
                            }
                            else if (entity is BaseChair)
                            {
                                outpost.Location = entity.transform.position + entity.transform.right + new Vector3(0f, 1f, 0f);
                                if (!outpost.Locations.Contains(outpost.Location)) outpost.Locations.Add(outpost.Location);
                                changedOutpost = true;
                            }
                        }
                        if (changedOutpost) SaveConfig();
                        Pool.FreeList(ref ents);
                    }

                    if (outpost.Location == Vector3.zero)
                    {
                        outpostEnabled = false;
                    }
                }
                else if (monument.name == banditPrefab)
                {
                    if (bandit == null)
                    {
                        banditEnabled = false;
                        continue;
                    }

                    float radius = monument.Bounds.size.Max();

                    if (bandit.Location != Vector3.zero && bandit.Locations.Exists(a => OutOfRange(monument, a)))
                    {
#if DEBUG
                        Puts("Invalid Bandit location detected");
#endif
                        bandit.Location = Vector3.zero;
                        bandit.Locations = new List<Vector3>();
                    }
                    if (config.Settings.AutoGenBandit && bandit.Location == Vector3.zero)
                    {
#if DEBUG
                        Puts("  Adding BanditTown target");
#endif
                        bool changedBandit = false;
                        var ents = Pool.GetList<BaseEntity>();
                        Vis.Entities(monPos, radius, ents);
                        foreach (BaseEntity entity in ents)
                        {
                            if (OutOfRange(monument, entity.transform.position))
                            {
                                continue;
                            }
                            if (entity.prefabID == 3858860623)
                            {
                                bandit.Location = entity.transform.position + entity.transform.forward + new Vector3(0f, 1f, 0f);
                                if (!bandit.Locations.Contains(bandit.Location)) bandit.Locations.Add(bandit.Location);
                                changedBandit = true;
                            }
                            else if (entity is Workbench)
                            {
                                bandit.Location = entity.transform.position + entity.transform.forward + new Vector3(0f, 1f, 0f);
                                if (!bandit.Locations.Contains(bandit.Location)) bandit.Locations.Add(bandit.Location);
                                changedBandit = true;
                            }
                            else if (entity is BaseChair)
                            {
                                bandit.Location = entity.transform.position + entity.transform.forward + new Vector3(0f, 1f, 0f);
                                if (!bandit.Locations.Contains(bandit.Location)) bandit.Locations.Add(bandit.Location);
                                changedBandit = true;
                            }
                        }
                        Pool.FreeList(ref ents);
                        if (changedBandit) SaveConfig();
                    }

                    if (bandit.Location == Vector3.zero)
                    {
                        banditEnabled = false;
                    }
                }
                else
                {
                    if (monuments.ContainsKey(name)) name += ":" + RandomString(5, 5);
                    if (monument.name.Contains("power_sub")) name = monument.name.Substring(monument.name.LastIndexOf("/") + 1).Replace(".prefab", "") + ":" + RandomString(5, 5);
                    float radius = GetMonumentFloat(name);
                    monuments[name] = new MonInfo() { Position = monPos, Radius = radius };
#if DEBUG
                    Puts($"Adding Monument: {name}, pos: {monPos}, size: {radius}");
#endif
                }
            }

            if (bandit != null && bandit.Location != Vector3.zero && !bandit.Locations.Contains(bandit.Location))
            {
                bandit.Locations.Add(bandit.Location);
            }

            if (outpost != null && outpost.Location != Vector3.zero && !outpost.Locations.Contains(outpost.Location))
            {
                outpost.Locations.Add(outpost.Location);
            }
        }

        private bool OutOfRange(MonumentInfo m, Vector3 a) => m.transform.position.y - a.y > 3f || !m.IsInBounds(a);

        private void CommandToggle(IPlayer user, string command, string[] args)
        {
            if (!user.IsAdmin) return;

            if (args.Length == 0)
            {
                user.Reply("tnt commandname");
                return;
            }

            string arg = args[0].ToLower();

            if (arg == command.ToLower()) return;

            if (!DisabledCommandData.DisabledCommands.Contains(arg))
                DisabledCommandData.DisabledCommands.Add(arg);
            else DisabledCommandData.DisabledCommands.Remove(arg);

            dataDisabled.WriteObject(DisabledCommandData);
            user.Reply("{0} {1}", null, DisabledCommandData.DisabledCommands.Contains(arg) ? "Disabled:" : "Enabled:", arg);
        }

        private void CommandTeleport(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTp) || !player.IsConnected || (player.IsSleeping() && !player.IsAdmin)) return;
            BasePlayer target;
            float x, y, z;
            switch (args.Length)
            {
                case 1:
                    target = FindPlayersSingle(args[0], player);
                    if (target == null) return;
                    if (target == player)
                    {
#if DEBUG
                        Puts("Debug mode - allowing self teleport.");
#else
                    PrintMsgL(player, "CantTeleportToSelf");
                    return;
#endif
                    }
                    Teleport(player, target);
                    PrintMsgL(player, "AdminTP", target.displayName);
                    Puts(_("LogTeleport", null, player.displayName, target.displayName));
                    if (config.Admin.AnnounceTeleportToTarget)
                        PrintMsgL(target, "AdminTPTarget", player.displayName);
                    break;
                case 2:
                    var origin = FindPlayersSingle(args[0], player);
                    if (origin == null) return;
                    target = FindPlayersSingle(args[1], player);
                    if (target == null) return;
                    if (target == origin)
                    {
                        PrintMsgL(player, "CantTeleportPlayerToSelf");
                        return;
                    }
                    Teleport(origin, target);
                    PrintMsgL(player, "AdminTPPlayers", origin.displayName, target.displayName);
                    PrintMsgL(origin, "AdminTPPlayer", player.displayName, target.displayName);
                    if (config.Admin.AnnounceTeleportToTarget)
                        PrintMsgL(target, "AdminTPPlayerTarget", player.displayName, origin.displayName);
                    Puts(_("LogTeleportPlayer", null, player.displayName, origin.displayName, target.displayName));
                    break;
                case 3:
                    if (!float.TryParse(args[0].Replace(",", string.Empty), out x) || !float.TryParse(args[1].Replace(",", string.Empty), out y) || !float.TryParse(args[2], out z))
                    {
                        PrintMsgL(player, "InvalidCoordinates");
                        return;
                    }
                    if (config.Settings.CheckBoundaries && !CheckBoundaries(x, y, z)) // added this option because I HATE boundaries
                    {
                        PrintMsgL(player, "AdminTPOutOfBounds");
                        PrintMsgL(player, "AdminTPBoundaries", boundary);
                        return;
                    }
                    Teleport(player, x, y, z);
                    PrintMsgL(player, "AdminTPCoordinates", player.transform.position);
                    Puts(_("LogTeleport", null, player.displayName, player.transform.position));
                    break;
                case 4:
                    target = FindPlayersSingle(args[0], player);
                    if (target == null) return;
                    if (!float.TryParse(args[1].Replace(",", string.Empty), out x) || !float.TryParse(args[2].Replace(",", string.Empty), out y) || !float.TryParse(args[3], out z))
                    {
                        PrintMsgL(player, "InvalidCoordinates");
                        return;
                    }
                    if (!CheckBoundaries(x, y, z))
                    {
                        PrintMsgL(player, "AdminTPOutOfBounds");
                        PrintMsgL(player, "AdminTPBoundaries", boundary);
                        return;
                    }
                    Teleport(target, x, y, z);
                    if (player == target)
                    {
                        PrintMsgL(player, "AdminTPCoordinates", player.transform.position);
                        Puts(_("LogTeleport", null, player.displayName, player.transform.position));
                    }
                    else
                    {
                        PrintMsgL(player, "AdminTPTargetCoordinates", target.displayName, player.transform.position);
                        if (config.Admin.AnnounceTeleportToTarget)
                            PrintMsgL(target, "AdminTPTargetCoordinatesTarget", player.displayName, player.transform.position);
                        Puts(_("LogTeleportPlayer", null, player.displayName, target.displayName, player.transform.position));
                    }
                    break;
                default:
                    PrintMsgL(player, "SyntaxCommandTP");
                    break;
            }
        }

        private void CommandTeleportNear(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpN) || !player.IsConnected || player.IsSleeping()) return;
            switch (args.Length)
            {
                case 1:
                case 2:
                    var target = FindPlayersSingle(args[0], player);
                    if (target == null) return;
                    if (target == player)
                    {
#if DEBUG
                        Puts("Debug mode - allowing self teleport.");
#else
                        PrintMsgL(player, "CantTeleportToSelf");
                        return;
#endif
                    }
                    int distance = 0;
                    if (args.Length != 2 || !int.TryParse(args[1], out distance))
                        distance = config.Admin.TeleportNearDefaultDistance;
                    float x = UnityEngine.Random.Range(-distance, distance);
                    var z = (float)Math.Sqrt(Math.Pow(distance, 2) - Math.Pow(x, 2));
                    var destination = target.transform.position;
                    destination.x -= x;
                    destination.z -= z;
                    Teleport(player, GetGroundBuilding(destination), true);
                    PrintMsgL(player, "AdminTP", target.displayName);
                    Puts(_("LogTeleport", null, player.displayName, target.displayName));
                    if (config.Admin.AnnounceTeleportToTarget)
                        PrintMsgL(target, "AdminTPTarget", player.displayName);
                    break;
                default:
                    PrintMsgL(player, "SyntaxCommandTPN");
                    break;
            }
        }

        private void CommandTeleportLocation(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpL) || !player.IsConnected || player.IsSleeping()) return;
            AdminData adminData;
            if (!_Admin.TryGetValue(player.userID, out adminData) || adminData.Locations.Count <= 0)
            {
                PrintMsgL(player, "AdminLocationListEmpty");
                return;
            }
            switch (args.Length)
            {
                case 0:
                    PrintMsgL(player, "AdminLocationList");
                    foreach (var location in adminData.Locations)
                        PrintMsgL(player, $"{location.Key} {location.Value}");
                    break;
                case 1:
                    Vector3 loc;
                    if (!adminData.Locations.TryGetValue(args[0], out loc))
                    {
                        PrintMsgL(player, "LocationNotFound");
                        return;
                    }
                    Teleport(player, loc, true);
                    PrintMsgL(player, "AdminTPLocation", args[0]);
                    break;
                default:
                    PrintMsgL(player, "SyntaxCommandTPL");
                    break;
            }
        }

        private void CommandSaveTeleportLocation(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpSave) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 1)
            {
                PrintMsgL(player, "SyntaxCommandTPSave");
                return;
            }
            AdminData adminData;
            if (!_Admin.TryGetValue(player.userID, out adminData))
                _Admin[player.userID] = adminData = new AdminData();
            Vector3 location;
            if (adminData.Locations.TryGetValue(args[0], out location))
            {
                PrintMsgL(player, "LocationExists", location);
                return;
            }
            var positionCoordinates = player.transform.position;
            if (!CanBypassRestrictions(player.UserIDString))
            {
                foreach (var loc in adminData.Locations)
                {
                    if ((positionCoordinates - loc.Value).magnitude < config.Admin.LocationRadius)
                    {
                        PrintMsgL(player, "LocationExistsNearby", loc.Key);
                        return;
                    }
                }
            }
            adminData.Locations[args[0]] = positionCoordinates;
            PrintMsgL(player, "AdminTPLocationSave");
            changedAdmin = true;
        }

        private void CommandRemoveTeleportLocation(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpRemove) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 1)
            {
                PrintMsgL(player, "SyntaxCommandTPRemove");
                return;
            }
            AdminData adminData;
            if (!_Admin.TryGetValue(player.userID, out adminData) || adminData.Locations.Count <= 0)
            {
                PrintMsgL(player, "AdminLocationListEmpty");
                return;
            }
            if (adminData.Locations.Remove(args[0]))
            {
                PrintMsgL(player, "AdminTPLocationRemove", args[0]);
                changedAdmin = true;
                return;
            }
            PrintMsgL(player, "LocationNotFound");
        }

        private void CommandTeleportBack(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpB) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 0)
            {
                PrintMsgL(player, "SyntaxCommandTPB");
                return;
            }
            AdminData adminData;
            if (!_Admin.TryGetValue(player.userID, out adminData) || adminData.PreviousLocation == Vector3.zero)
            {
                PrintMsgL(player, "NoPreviousLocationSaved");
                return;
            }

            Teleport(player, adminData.PreviousLocation, false);
            adminData.PreviousLocation = Vector3.zero;
            changedAdmin = true;
            PrintMsgL(player, "AdminTPBack");
            Puts(_("LogTeleportBack", null, player.displayName));
        }

        private void CommandSetHome(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowed(player, PermHome) || !player.IsConnected || player.IsSleeping()) return;
            if (!config.Settings.HomesEnabled) { user.Reply("Homes are not enabled in the config."); return; }
            if (args.Length != 1)
            {
                PrintMsgL(player, "SyntaxCommandSetHome");
                return;
            }
            string err = null;
            HomeData homeData;
            if (!_Home.TryGetValue(player.userID, out homeData))
                _Home[player.userID] = homeData = new HomeData();
            var limit = GetHigher(player, config.Home.VIPHomesLimits, config.Home.HomesLimit, true);
            if (!args[0].All(char.IsLetterOrDigit))
            {
                PrintMsgL(player, "InvalidCharacter");
                return;
            }
            Vector3 location;
            if (homeData.Locations.TryGetValue(args[0], out location))
            {
                PrintMsgL(player, "HomeExists", location);
                return;
            }
            var positionCoordinates = player.transform.position;
            if (!CanBypassRestrictions(player.UserIDString))
            {
                err = CheckPlayer(player, false, CanCraftHome(player), true, "sethome");
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                if (!player.CanBuild())
                {
                    PrintMsgL(player, "HomeTPBuildingBlocked");
                    return;
                }                
                if (limit > 0 && homeData.Locations.Count >= limit)
                {
                    PrintMsgL(player, "HomeMaxLocations", limit);
                    return;
                }
                foreach (var loc in homeData.Locations)
                {
                    if ((positionCoordinates - loc.Value).magnitude < config.Home.LocationRadius)
                    {
                        PrintMsgL(player, "HomeExistsNearby", loc.Key);
                        return;
                    }
                }
                err = CanPlayerTeleport(player, positionCoordinates);
                if (err != null)
                {
                    SendReply(player, err);
                    return;
                }
                err = CheckFoundation(player.userID, positionCoordinates);
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
            }
            if (player.IsAdmin && config.Settings.DrawHomeSphere) player.SendConsoleCommand("ddraw.sphere", 30f, Color.blue, GetGround(positionCoordinates), 2.5f);
            homeData.Locations[args[0]] = positionCoordinates;
            changedHome = true;
            PrintMsgL(player, "HomeSave");
            PrintMsgL(player, "HomeQuota", homeData.Locations.Count, limit);
        }

        private void CommandRemoveHome(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            if (!config.Settings.HomesEnabled) { user.Reply("Homes are not enabled in the config."); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowed(player, PermHome) || !player.IsConnected || player.IsSleeping()) return;
            if (player.IsAdmin && args.Length == 2 && args[0] == "all")
            {
                float radius;
                if (float.TryParse(args[1], out radius))
                {
                    int amount = 0;
                    foreach (var home in _Home.ToList())
                    {
                        foreach (var location in home.Value.Locations.ToList())
                        {
                            if (Vector3Ex.Distance2D(location.Value, player.transform.position) < radius)
                            {
                                string username = covalence.Players.FindPlayerById(home.Key.ToString())?.Name ?? "N/A";
                                Puts("{0} ({1}) removed home from {2} ({3}) at {4}", player.displayName, player.userID, username, home.Key, location.Value);
                                player.SendConsoleCommand("ddraw.text", 30f, Color.red, location.Value, "X");
                                home.Value.Locations.Remove(location.Key);
                                amount++;
                            }
                        }
                    }

                    user.Reply($"Removed {amount} homes within {radius} meters");
                }
                else user.Reply("/removehome all <radius>");

                return;
            }
            if (args.Length != 1)
            {
                PrintMsgL(player, "SyntaxCommandRemoveHome");
                return;
            }
            HomeData homeData;
            if (!_Home.TryGetValue(player.userID, out homeData) || homeData.Locations.Count <= 0)
            {
                PrintMsgL(player, "HomeListEmpty");
                return;
            }
            if (homeData.Locations.Remove(args[0]))
            {
                changedHome = true;
                PrintMsgL(player, "HomeRemove", args[0]);
            }
            else
                PrintMsgL(player, "HomeNotFound");
        }

        private void CommandHome(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            if (!config.Settings.HomesEnabled) { user.Reply("Homes are not enabled in the config."); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowed(player, PermHome) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length == 0)
            {
                PrintMsgL(player, "SyntaxCommandHome");
                if (IsAllowed(player)) PrintMsgL(player, "SyntaxCommandHomeAdmin");
                return;
            }
            switch (args[0].ToLower())
            {
                case "add":
                    CommandSetHome(user, command, args.Skip(1).ToArray());
                    break;
                case "list":
                    CommandListHomes(user, command, args.Skip(1).ToArray());
                    break;
                case "remove":
                    CommandRemoveHome(user, command, args.Skip(1).ToArray());
                    break;
                case "radius":
                    CommandHomeRadius(user, command, args.Skip(1).ToArray());
                    break;
                case "delete":
                    CommandHomeDelete(user, command, args.Skip(1).ToArray());
                    break;
                case "tp":
                    CommandHomeAdminTP(user, command, args.Skip(1).ToArray());
                    break;
                case "homes":
                    CommandHomeHomes(user, command, args.Skip(1).ToArray());
                    break;
                case "wipe":
                    CommandWipeHomes(user, command, args.Skip(1).ToArray());
                    break;
                default:
                    cmdChatHomeTP(player, command, args);
                    break;
            }
        }

        private void CommandHomeRadius(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermRadiusHome) || !player.IsConnected || player.IsSleeping()) return;
            float radius;
            if (args.Length != 1 || !float.TryParse(args[0], out radius)) radius = 10;
            var found = false;
            foreach (var homeData in _Home)
            {
                var toRemove = new List<string>();
                var target = RustCore.FindPlayerById(homeData.Key)?.displayName ?? homeData.Key.ToString();
                foreach (var location in homeData.Value.Locations)
                {
                    if ((player.transform.position - location.Value).magnitude <= radius)
                    {
                        string err = CheckFoundation(homeData.Key, location.Value);
                        if (err != null)
                        {
                            PrintMsgL(player, "HomeRemovedInvalid", $"{location.Key} {location.Value}");
                            PrintMsgL(player, err);
                            toRemove.Add(location.Key);
                            found = true;
                            continue;
                        }
                        var entity = GetFoundationOwned(location.Value, homeData.Key);
                        if (entity == null)
                        {
                            player.SendConsoleCommand("ddraw.text", 30f, Color.blue, location.Value, $"<size=20>{target} - {location.Key} {location.Value}</size>");
                        }
                        else
                        {
                            player.SendConsoleCommand("ddraw.text", 30f, Color.blue, entity.CenterPoint() + new Vector3(0, .5f), $"<size=20>{target} - {location.Key} {location.Value}</size>");
                            DrawBox(player, entity.CenterPoint(), entity.transform.rotation, entity.bounds.size);
                        }
                        PrintMsg(player, $"{target} - {location.Key} {location.Value}");
                        found = true;
                    }
                }
                foreach (var loc in toRemove)
                {
                    homeData.Value.Locations.Remove(loc);
                    changedHome = true;
                }
            }
            if (!found)
                PrintMsgL(player, "HomeNoFound");
        }

        private void CommandHomeDelete(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermDeleteHome) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 2)
            {
                PrintMsgL(player, "SyntaxCommandHomeDelete");
                return;
            }
            var userId = FindPlayersSingleId(args[0], player);
            if (userId <= 0) return;
            HomeData targetHome;
            if (!_Home.TryGetValue(userId, out targetHome) || !targetHome.Locations.Remove(args[1]))
            {
                PrintMsgL(player, "HomeNotFound");
                return;
            }
            changedHome = true;
            PrintMsgL(player, "HomeDelete", args[0], args[1]);
        }

        private void CommandHomeAdminTP(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpHome) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 2)
            {
                PrintMsgL(player, "SyntaxCommandHomeAdminTP");
                return;
            }
            var userId = FindPlayersSingleId(args[0], player);
            if (userId <= 0) return;
            HomeData targetHome;
            Vector3 location;
            if (!_Home.TryGetValue(userId, out targetHome) || !targetHome.Locations.TryGetValue(args[1], out location))
            {
                PrintMsgL(player, "HomeNotFound");
                return;
            }

            Teleport(player, location, true);
            PrintMsgL(player, "HomeAdminTP", args[0], args[1]);
        }

        // Check that plugins are available and enabled for CheckEconomy()
        private bool UseEconomy()
        {
            return (config.Settings.UseEconomics && Economics != null) || (config.Settings.UseServerRewards && ServerRewards != null);
        }

        // Check balance on multiple plugins and optionally withdraw money from the player
        private bool CheckEconomy(BasePlayer player, double bypass, bool withdraw = false, bool deposit = false)
        {
            if (player == null)
            {
                return false;
            }
            if (CanBypassRestrictions(player.UserIDString)) return true;
            bool foundmoney = false;

            // Check Economics first.  If not in use or balance low, check ServerRewards below
            if (config.Settings.UseEconomics && Economics != null)
            {
                var balance = (double)Economics?.CallHook("Balance", player.UserIDString);
                if (balance >= bypass)
                {
                    foundmoney = true;
                    if (withdraw)
                    {
                        return Convert.ToBoolean(Economics?.CallHook("Withdraw", player.userID, bypass));
                    }
                    else if (deposit)
                    {
                        Economics?.CallHook("Deposit", player.userID, bypass);
                    }
                }
            }

            // No money via Economics, or plugin not in use.  Try ServerRewards.
            if (!foundmoney && config.Settings.UseServerRewards && ServerRewards != null)
            {
                object bal = ServerRewards?.Call("CheckPoints", player.userID);
                var balance = Convert.ToDouble(bal);
                if (balance >= bypass)
                {
                    foundmoney = true;
                    if (withdraw)
                    {
                        return Convert.ToBoolean(ServerRewards?.Call("TakePoints", player.userID, (int)bypass));
                    }
                    else if (deposit)
                    {
                        ServerRewards?.Call("AddPoints", player.userID, (int)bypass);
                    }
                }
            }

            // Just checking balance without withdrawal - did we find anything?
            return foundmoney;
        }

        private void cmdChatHomeTP(BasePlayer player, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { player.ChatMessage("Disabled command."); return; }
            if (!IsAllowed(player, PermHome) || !player.IsConnected || player.IsSleeping()) return;
            bool paidmoney = false;
            if (!config.Settings.HomesEnabled) { player.ChatMessage("Homes are not enabled in the config."); return; }
            if (args.Length < 1)
            {
                PrintMsgL(player, "SyntaxCommandHome");
                return;
            }
            HomeData homeData;
            if (!_Home.TryGetValue(player.userID, out homeData) || homeData.Locations.Count <= 0)
            {
                PrintMsgL(player, "HomeListEmpty");
                return;
            }
            Vector3 location;
            if (!homeData.Locations.TryGetValue(args[0], out location))
            {
                PrintMsgL(player, "HomeNotFound");
                return;
            }
            int limit = 0;
            string err = null;
            var timestamp = Facepunch.Math.Epoch.Current;
            if (!CanBypassRestrictions(player.UserIDString))
            {
                float globalCooldownTime = GetGlobalCooldown(player);
                if (globalCooldownTime > 0f)
                {
                    PrintMsgL(player, "WaitGlobalCooldown", FormatTime(player, (int)globalCooldownTime));
                    return;
                }
                if (config.Settings.BlockAuthorizedTeleporting && player.IsBuildingAuthed())
                {
                    PrintMsgL(player, "CannotTeleportFromHome");
                    return;
                }
                err = CheckPlayer(player, config.Home.UsableOutOfBuildingBlocked, CanCraftHome(player), true, "home");
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                if (config.Settings.BlockNoEscape && Convert.ToBoolean(NoEscape?.Call("IsBlockedZone", location)))
                {
                    PrintMsgL(player, "TPNoEscapeBlocked");
                    return;
                }
                err = CheckFoundation(player.userID, location) ?? CheckTargetLocation(player, location, config.Home.UsableIntoBuildingBlocked, config.Home.CupOwnerAllowOnBuildingBlocked);
                if (err != null)
                {
                    PrintMsgL(player, "HomeRemovedInvalid", args[0]);
                    PrintMsgL(player, err);
                    homeData.Locations.Remove(args[0]);
                    changedHome = true;
                    return;
                }
                var cooldown = GetLower(player, config.Home.VIPCooldowns, config.Home.Cooldown);
                if (cooldown > 0 && timestamp - homeData.Teleports.Timestamp < cooldown)
                {
                    var cmdSent = args.Length >= 2 ? args[1].ToLower() : string.Empty;

                    if (!string.IsNullOrEmpty(config.Settings.BypassCMD) && !paidmoney)
                    {
                        if (cmdSent == config.Settings.BypassCMD.ToLower() && config.Home.Bypass > -1)
                        {
                            bool foundmoney = CheckEconomy(player, config.Home.Bypass);

                            if (foundmoney)
                            {
                                CheckEconomy(player, config.Home.Bypass, true);
                                paidmoney = true;

                                if (config.Home.Bypass > 0)
                                {
                                    PrintMsgL(player, "HomeTPCooldownBypass", config.Home.Bypass);
                                }

                                if (config.Home.Pay > 0)
                                {
                                    PrintMsgL(player, "PayToHome", config.Home.Pay);
                                }
                            }
                            else
                            {
                                PrintMsgL(player, "HomeTPCooldownBypassF", config.Home.Bypass);
                                return;
                            }
                        }
                        else if (UseEconomy())
                        {
                            if (config.Home.Bypass > 0)
                            {
                                var remain = cooldown - (timestamp - homeData.Teleports.Timestamp);
                                PrintMsgL(player, "HomeTPCooldown", FormatTime(player, remain));
                                PrintMsgL(player, "HomeTPCooldownBypassP", config.Home.Bypass);
                                PrintMsgL(player, "HomeTPCooldownBypassP2", config.Settings.BypassCMD);
                                return;
                            }
                        }
                        else
                        {
                            var remain = cooldown - (timestamp - homeData.Teleports.Timestamp);

                            PrintMsgL(player, "HomeTPCooldown", FormatTime(player, remain));
                            return;
                        }
                    }
                    else
                    {
                        var remain = cooldown - (timestamp - homeData.Teleports.Timestamp);
                        PrintMsgL(player, "HomeTPCooldown", FormatTime(player, remain));
                        return;
                    }
                }
                var currentDate = DateTime.Now.ToString("d");
                if (homeData.Teleports.Date != currentDate)
                {
                    homeData.Teleports.Amount = 0;
                    homeData.Teleports.Date = currentDate;
                }
                limit = GetHigher(player, config.Home.VIPDailyLimits, config.Home.DailyLimit, true);
                if (limit > 0 && homeData.Teleports.Amount >= limit)
                {
                    PrintMsgL(player, "HomeTPLimitReached", limit);
                    return;
                }
                err = CanPlayerTeleport(player, location);
                if (err != null)
                {
                    SendReply(player, err);
                    return;
                }
                err = CheckItems(player);
                if (err != null)
                {
                    PrintMsgL(player, "TPBlockedItem", err);
                    return;
                }
                if (config.Home.UsableFromSafeZoneOnly && !player.InSafeZone())
                {
                    PrintMsgL(player, "TPHomeSafeZoneOnly");
                    return;
                }
            }
            if (TeleportTimers.ContainsKey(player.userID))
            {
                PrintMsgL(player, "TeleportPendingTPC");
                return;
            }
            var countdown = GetLower(player, config.Home.VIPCountdowns, config.Home.Countdown);
            TeleportTimers[player.userID] = new TeleportTimer
            {
                OriginPlayer = player,
                Timer = timer.Once(countdown, () =>
                {
#if DEBUG
                    Puts("Calling CheckPlayer from cmdChatHomeTP");
#endif
                    if (!CanBypassRestrictions(player.UserIDString))
                    {
                        err = CheckPlayer(player, config.Home.UsableOutOfBuildingBlocked, CanCraftHome(player), true, "home");
                        if (err != null)
                        {
                            PrintMsgL(player, "Interrupted");
                            PrintMsgL(player, err);
                            if (paidmoney)
                            {
                                paidmoney = false;
                                CheckEconomy(player, config.Home.Bypass, false, true);
                            }
                            TeleportTimers.Remove(player.userID);
                            return;
                        }
                        err = CanPlayerTeleport(player, location);
                        if (err != null)
                        {
                            PrintMsgL(player, "Interrupted");
                            PrintMsgL(player, err);
                            if (paidmoney)
                            {
                                paidmoney = false;
                                CheckEconomy(player, config.Home.Bypass, false, true);
                            }
                            TeleportTimers.Remove(player.userID);
                            return;
                        }
                        err = CheckItems(player);
                        if (err != null)
                        {
                            PrintMsgL(player, "Interrupted");
                            PrintMsgL(player, "TPBlockedItem", err);
                            if (paidmoney)
                            {
                                paidmoney = false;
                                CheckEconomy(player, config.Home.Bypass, false, true);
                            }
                            TeleportTimers.Remove(player.userID);
                            return;
                        }
                        err = CheckFoundation(player.userID, location) ?? CheckTargetLocation(player, location, config.Home.UsableIntoBuildingBlocked, config.Home.CupOwnerAllowOnBuildingBlocked);
                        if (err != null)
                        {
                            PrintMsgL(player, "HomeRemovedInvalid", args[0]);
                            PrintMsgL(player, err);
                            homeData.Locations.Remove(args[0]);
                            changedHome = true;
                            if (paidmoney)
                            {
                                paidmoney = false;
                                CheckEconomy(player, config.Home.Bypass, false, true);
                            }
                            return;
                        }
                        if (UseEconomy())
                        {
                            if (config.Home.Pay < 0)
                            {
                                TeleportTimers.Remove(player.userID);
                                PrintMsgL(player, "DM_TownTPDisabled", "/home");
                                return;
                            }
                            else if (config.Home.Pay > 0)
                            {
                                if (!CheckEconomy(player, config.Home.Pay))
                                {
                                    TeleportTimers.Remove(player.userID);
                                    PrintMsgL(player, "TPNoMoney", config.Home.Pay);
                                    return;
                                }

                                if (!paidmoney)
                                {
                                    PrintMsgL(player, "TPMoney", (double)config.Home.Pay);
                                }

                                paidmoney = CheckEconomy(player, config.Home.Pay, true);
                            }
                        }
                    }
                    Teleport(player, location, config.Home.AllowTPB);
                    homeData.Teleports.Amount++;
                    homeData.Teleports.Timestamp = timestamp;
                    changedHome = true;
                    PrintMsgL(player, "HomeTP", args[0]);
                    if (limit > 0) PrintMsgL(player, "HomeTPAmount", limit - homeData.Teleports.Amount);
                    TeleportTimers.Remove(player.userID);
                })
            };

            if (countdown > 0)
            {
                PrintMsgL(player, "HomeTPStarted", args[0], countdown);
            }
        }

        private void CommandListHomes(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !player.IsConnected || player.IsSleeping()) return;
            if (!config.Settings.HomesEnabled) { user.Reply("Homes are not enabled in the config."); return; }
            if (args.Length != 0)
            {
                PrintMsgL(player, "SyntaxCommandListHomes");
                return;
            }
            HomeData homeData;
            if (!_Home.TryGetValue(player.userID, out homeData) || homeData.Locations.Count <= 0)
            {
                PrintMsgL(player, "HomeListEmpty");
                return;
            }
            PrintMsgL(player, "HomeList");
            ValidateHomes(player, homeData, true);
            foreach (var location in homeData.Locations)
                PrintMsgL(player, $"{location.Key} {location.Value}");
        }

        private void ValidateHomes(BasePlayer player, HomeData homeData, bool flag)
        {
            if (config.Home.CheckValidOnList)
            {
                var toRemove = new List<string>();
                foreach (var location in homeData.Locations)
                {
                    var err = CheckFoundation(player.userID, location.Value);
                    if (err != null)
                    {
                        if (flag) PrintMsgL(player, err);
                        toRemove.Add(location.Key);
                        continue;
                    }
                    if (flag) PrintMsgL(player, $"{location.Key} {location.Value}");
                }
                foreach (var loc in toRemove)
                {
                    if (flag) PrintMsgL(player, "HomeRemovedInvalid", loc);
                    homeData.Locations.Remove(loc);
                    changedHome = true;
                }
            }
        }

        private void CommandHomeHomes(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermHomeHomes) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 1)
            {
                PrintMsgL(player, "SyntaxCommandHomeHomes");
                return;
            }
            var userId = FindPlayersSingleId(args[0], player);
            if (userId <= 0) return;
            HomeData homeData;
            if (!_Home.TryGetValue(userId, out homeData) || homeData.Locations.Count <= 0)
            {
                PrintMsgL(player, "HomeListEmpty");
                return;
            }
            PrintMsgL(player, "HomeList");
            var toRemove = new List<string>();
            foreach (var location in homeData.Locations)
            {
                var err = CheckFoundation(userId, location.Value);
                if (err != null)
                {
                    PrintMsgL(player, err);
                    toRemove.Add(location.Key);
                    continue;
                }
                PrintMsgL(player, $"{location.Key} {location.Value}");
            }
            foreach (var loc in toRemove)
            {
                PrintMsgL(player, "HomeRemovedInvalid", loc);
                homeData.Locations.Remove(loc);
                changedHome = true;
            }
        }

        private void CommandTeleportAcceptToggle(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !player.IsConnected || player.IsSleeping()) return;

            if (TPTToggle.Contains(player.UserIDString))
            {
                TPTToggle.Remove(player.UserIDString);
                PrintMsgL(player, "AcceptToggleOn");
            }
            else
            {
                TPTToggle.Add(player.UserIDString);
                PrintMsgL(player, "AcceptToggleOff");
            }

            changedTPT = true;
        }

        public bool IsOnSameTeam(ulong playerId, ulong targetId)
        {
            RelationshipManager.PlayerTeam team;
            return RelationshipManager.ServerInstance.playerToTeam.TryGetValue(playerId, out team) && team.members.Contains(targetId);
        }

        private bool AreFriends(string playerId, string targetId)
        {
            return Friends != null && Convert.ToBoolean(Friends?.Call("AreFriends", playerId, targetId));
        }

        private bool IsInSameClan(string playerId, string targetId)
        {
            return Clans != null && Convert.ToBoolean(Clans?.Call("IsMemberOrAlly", playerId, targetId));
        }

        private void OnTeleportRequested(BasePlayer target, BasePlayer player)
        {
            if (!permission.UserHasPermission(target.UserIDString, PermTpT) || !permission.UserHasPermission(player.UserIDString, PermTpT) || TPTToggle.Contains(target.UserIDString))
            {
                return;
            }

            if (config.TPT.UseClans && IsInSameClan(player.UserIDString, target.UserIDString))
            {
                target.SendConsoleCommand("chat.say /tpa");
            }
            else if (config.TPT.UseFriends && AreFriends(player.UserIDString, target.UserIDString))
            {
                target.SendConsoleCommand("chat.say /tpa");
            }
            else if (config.TPT.UseTeams && IsOnSameTeam(player.userID, target.userID))
            {
                target.SendConsoleCommand("chat.say /tpa");
            }
        }

        private string GetMultiplePlayers(List<BasePlayer> players)
        {
            var list = new List<string>();

            foreach (var player in players)
            {
                string id;
                if (!_players.TryGetValue(player, out id))
                {
                    id = OnPlayerConnected(player);
                }

                list.Add(string.Format("<color={0}>{1}</color> - {2}", config.Settings.ChatCommandArgumentColor, id, player.displayName));
            }

            return string.Join(", ", list.ToArray());
        }

        private void CommandTeleportRequest(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpR) || !player.IsConnected || player.IsSleeping()) return;
            if (!config.Settings.TPREnabled) { user.Reply("TPR is not enabled in the config."); return; }
            string err = null;
            if (args.Length == 0)
            {
                PrintMsgL(player, "SyntaxCommandTPR");
                return;
            }
            var targets = FindPlayers(args[0]);
            if (targets.Count <= 0)
            {
                PrintMsgL(player, "PlayerNotFound");
                return;
            }
            BasePlayer target = null;
            if (args.Length >= 2)
            {
                if (targets.Count > 1)
                {
                    PrintMsgL(player, "MultiplePlayers", GetMultiplePlayers(targets));
                    return;
                }
                else target = targets[0];
            }
            else
            {
                if (targets.Count > 1)
                {
                    PrintMsgL(player, "MultiplePlayers", GetMultiplePlayers(targets));
                    return;
                }

                target = targets[0];
            }

            if (target == player)
            {
#if DEBUG
                Puts("Debug mode - allowing self teleport.");
#else
                PrintMsgL(player, "CantTeleportToSelf");
                return;
#endif
            }
#if DEBUG
            Puts("Calling CheckPlayer from cmdChatTeleportRequest");
#endif
            TeleportData tprData;
            if (!_TPR.TryGetValue(player.userID, out tprData))
                _TPR[player.userID] = tprData = new TeleportData();
            if (!CanBypassRestrictions(player.UserIDString))
            {
                float globalCooldownTime = GetGlobalCooldown(player);
                if (globalCooldownTime > 0f)
                {
                    PrintMsgL(player, "WaitGlobalCooldown", FormatTime(player, (int)globalCooldownTime));
                    return;
                }
                if (config.Settings.BlockAuthorizedTeleporting && player.IsBuildingAuthed())
                {
                    PrintMsgL(player, "CannotTeleportFromHome");
                    return;
                }
                err = CheckPlayer(player, config.TPR.UsableOutOfBuildingBlocked, CanCraftTPR(player), true, "tpr");
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                var err2 = CheckPlayer(target, config.TPR.UsableIntoBuildingBlocked, CanCraftTPR(target), true, "tpr");
                if (err2 != null)
                {
                    string error = string.Format(lang.GetMessage("ErrorTPR", this, player.UserIDString), target.displayName, err2);
                    PrintMsg(player, error);
                    return;
                }
                err = CheckTargetLocation(target, target.transform.position, config.TPR.UsableIntoBuildingBlocked, config.TPR.CupOwnerAllowOnBuildingBlocked);
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                var timestamp = Facepunch.Math.Epoch.Current;
                var currentDate = DateTime.Now.ToString("d");

                if (tprData.Date != currentDate)
                {
                    tprData.Amount = 0;
                    tprData.Date = currentDate;
                }

                var cooldown = GetLower(player, config.TPR.VIPCooldowns, config.TPR.Cooldown);
                if (cooldown > 0 && timestamp - tprData.Timestamp < cooldown)
                {
                    var cmdSent = args.Length >= 2 ? args[1].ToLower() : string.Empty;

                    if (!string.IsNullOrEmpty(config.Settings.BypassCMD))
                    {
                        if (cmdSent == config.Settings.BypassCMD.ToLower() && config.TPR.Bypass > -1)
                        {
                            if (CheckEconomy(player, config.TPR.Bypass))
                            {
                                CheckEconomy(player, config.TPR.Bypass, true);

                                if (config.TPR.Bypass > 0)
                                {
                                    PrintMsgL(player, "TPRCooldownBypass", config.TPR.Bypass);
                                }

                                if (config.TPR.Pay > 0)
                                {
                                    PrintMsgL(player, "PayToTPR", config.TPR.Pay);
                                }
                            }
                            else
                            {
                                PrintMsgL(player, "TPRCooldownBypassF", config.TPR.Bypass);
                                return;
                            }
                        }
                        else if (UseEconomy())
                        {
                            var remain = cooldown - (timestamp - tprData.Timestamp);
                            PrintMsgL(player, "TPRCooldown", FormatTime(player, remain));
                            if (config.TPR.Bypass > -1)
                            {
                                if (config.TPR.Bypass > 0)
                                {
                                    PrintMsgL(player, "TPRCooldownBypassP", config.TPR.Bypass);

                                    if (config.TPR.Pay > 0)
                                    {
                                        PrintMsgL(player, "PayToTPR", config.TPR.Pay);
                                    }

                                    PrintMsgL(player, "TPRCooldownBypassP2a", config.Settings.BypassCMD);
                                    return;
                                }
                            }
                            else return;
                        }
                        else
                        {
                            var remain = cooldown - (timestamp - tprData.Timestamp);
                            PrintMsgL(player, "TPRCooldown", FormatTime(player, remain));
                            return;
                        }
                    }
                    else
                    {
                        var remain = cooldown - (timestamp - tprData.Timestamp);
                        PrintMsgL(player, "TPRCooldown", FormatTime(player, remain));
                        return;
                    }
                }
                var limit = GetHigher(player, config.TPR.VIPDailyLimits, config.TPR.DailyLimit, true);
                if (limit > 0 && tprData.Amount >= limit)
                {
                    PrintMsgL(player, "TPRLimitReached", limit);
                    return;
                }
                err = CanPlayerTeleport(player, target.transform.position);
                if (err != null)
                {
                    SendReply(player, err);
                    return;
                }
                err = CanPlayerTeleport(target, player.transform.position);
                if (err != null)
                {
                    PrintMsgL(player, string.IsNullOrEmpty(err) ? "TPRTarget" : err);
                    return;
                }
                err = CheckItems(player);
                if (err != null)
                {
                    PrintMsgL(player, "TPBlockedItem", err);
                    return;
                }
            }
            if (TeleportTimers.ContainsKey(player.userID))
            {
                PrintMsgL(player, "TeleportPendingTPC");
                return;
            }
            if (TeleportTimers.ContainsKey(target.userID))
            {
                PrintMsgL(player, "TeleportPendingTarget");
                return;
            }
            if (PlayersRequests.ContainsKey(player.userID))
            {
                PrintMsgL(player, "PendingRequest");
                return;
            }
            if (PlayersRequests.ContainsKey(target.userID))
            {
                PrintMsgL(player, "PendingRequestTarget");
                return;
            }
            if (!config.TPR.UseClans_Friends_Teams || IsInSameClan(player.UserIDString, target.UserIDString) || AreFriends(player.UserIDString, target.UserIDString) || IsOnSameTeam(player.userID, target.userID) || CanBypassRestrictions(player.UserIDString))
            {
                PlayersRequests[player.userID] = target;
                PlayersRequests[target.userID] = player;
                PendingRequests[target.userID] = timer.Once(config.TPR.RequestDuration, () => { RequestTimedOut(player, target); });
                PrintMsgL(player, "Request", target.displayName);
                PrintMsgL(target, "RequestTarget", player.displayName);
                Interface.CallHook("OnTeleportRequested", target, player);
            }
            else
            {
                PrintMsgL(player, "TPR_NoClan_NoFriend_NoTeam");
            }
        }

        private void CommandTeleportAccept(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            if (!config.Settings.TPREnabled) { user.Reply("TPR is not enabled in the config."); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermTpR) || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 0)
            {
                PrintMsgL(player, "SyntaxCommandTPA");
                return;
            }
            Timer reqTimer;
            if (!PendingRequests.TryGetValue(player.userID, out reqTimer))
            {
                PrintMsgL(player, "NoPendingRequest");
                return;
            }
#if DEBUG
            Puts("Calling CheckPlayer from cmdChatTeleportAccept");
#endif
            string err = null;
            var originPlayer = PlayersRequests[player.userID];
            if (!CanBypassRestrictions(player.UserIDString))
            {
                err = CheckPlayer(player, config.TPR.UsableIntoBuildingBlocked, CanCraftTPR(player), false, "tpa");
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                err = CheckPlayer(originPlayer, config.TPR.UsableOutOfBuildingBlocked, CanCraftTPR(originPlayer), true, "tpa");
                if (err != null)
                {
                    PrintMsgL(originPlayer, err);
                    return;
                }
                err = CheckTargetLocation(originPlayer, player.transform.position, config.TPR.UsableIntoBuildingBlocked, config.TPR.CupOwnerAllowOnBuildingBlocked);
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                err = CanPlayerTeleport(player, originPlayer.transform.position);
                if (err != null)
                {
                    SendReply(player, err);
                    return;
                }
                if (config.TPR.BlockTPAOnCeiling)
                {
                    if (GetFloor(player.eyes.position).Count > 0)
                    {
                        PrintMsgL(player, "AcceptOnRoof");
                        return;
                    }
                }
                float globalCooldownTime = GetGlobalCooldown(player);
                if (globalCooldownTime > 0f)
                {
                    PrintMsgL(player, "WaitGlobalCooldown", FormatTime(player, (int)globalCooldownTime));
                    return;
                }
                if (config.Settings.BlockAuthorizedTeleporting && player.IsBuildingAuthed())
                {
                    PrintMsgL(player, "CannotTeleportFromHome");
                    return;
                }
            }
            var countdown = GetLower(originPlayer, config.TPR.VIPCountdowns, config.TPR.Countdown);
            PrintMsgL(originPlayer, "Accept", player.displayName, countdown);
            PrintMsgL(player, "AcceptTarget", originPlayer.displayName);
            var timestamp = Facepunch.Math.Epoch.Current;
            TeleportTimers[originPlayer.userID] = new TeleportTimer
            {
                OriginPlayer = originPlayer,
                TargetPlayer = player,
                Timer = timer.Once(countdown, () =>
                {
#if DEBUG
                    Puts("Calling CheckPlayer from cmdChatTeleportAccept timer loop");
#endif
                    if (!CanBypassRestrictions(player.UserIDString))
                    {
                        if (config.Settings.BlockAuthorizedTeleporting && player.IsBuildingAuthed())
                        {
                            PrintMsgL(player, "CannotTeleportFromHome");
                            return;
                        }
                        err = CheckPlayer(originPlayer, config.TPR.UsableOutOfBuildingBlocked, CanCraftTPR(originPlayer), true, "tpa") ?? CheckPlayer(player, false, CanCraftTPR(player), true, "tpa");
                        if (err != null)
                        {
                            PrintMsgL(player, "InterruptedTarget", originPlayer.displayName);
                            PrintMsgL(originPlayer, "Interrupted");
                            PrintMsgL(originPlayer, err);
                            TeleportTimers.Remove(originPlayer.userID);
                            return;
                        }
                        err = CheckTargetLocation(originPlayer, player.transform.position, config.TPR.UsableIntoBuildingBlocked, config.TPR.CupOwnerAllowOnBuildingBlocked);
                        if (err != null)
                        {
                            PrintMsgL(player, err);
                            PrintMsgL(originPlayer, "Interrupted");
                            PrintMsgL(originPlayer, err);
                            TeleportTimers.Remove(originPlayer.userID);
                            return;
                        }
                        err = CanPlayerTeleport(originPlayer, player.transform.position) ?? CanPlayerTeleport(player, originPlayer.transform.position);
                        if (err != null)
                        {
                            SendReply(player, err);
                            PrintMsgL(originPlayer, "Interrupted");
                            SendReply(originPlayer, err);
                            TeleportTimers.Remove(originPlayer.userID);
                            return;
                        }
                        err = CheckItems(originPlayer);
                        if (err != null)
                        {
                            PrintMsgL(player, "InterruptedTarget", originPlayer.displayName);
                            PrintMsgL(originPlayer, "Interrupted");
                            PrintMsgL(originPlayer, "TPBlockedItem", err);
                            TeleportTimers.Remove(originPlayer.userID);
                            return;
                        }
                        if (UseEconomy())
                        {
                            if (config.TPR.Pay > -1)
                            {
                                if (!CheckEconomy(originPlayer, config.TPR.Pay))
                                {
                                    if (config.TPR.Pay > 0)
                                    {
                                        PrintMsgL(originPlayer, "TPNoMoney", config.TPR.Pay);
                                    }

                                    PrintMsgL(player, "InterruptedTarget", originPlayer.displayName);
                                    TeleportTimers.Remove(originPlayer.userID);
                                    return;
                                }
                                else
                                {
                                    CheckEconomy(originPlayer, config.TPR.Pay, true);

                                    if (config.TPR.Pay > 0)
                                    {
                                        PrintMsgL(originPlayer, "TPMoney", (double)config.TPR.Pay);
                                    }
                                }
                            }
                        }
                    }
                    Teleport(originPlayer, player.transform.position, config.TPR.AllowTPB);
                    var tprData = _TPR[originPlayer.userID];
                    tprData.Amount++;
                    tprData.Timestamp = timestamp;
                    changedTPR = true;
                    PrintMsgL(player, "SuccessTarget", originPlayer.displayName);
                    PrintMsgL(originPlayer, "Success", player.displayName);
                    var limit = GetHigher(originPlayer, config.TPR.VIPDailyLimits, config.TPR.DailyLimit, true);
                    if (limit > 0) PrintMsgL(originPlayer, "TPRAmount", limit - tprData.Amount);
                    TeleportTimers.Remove(originPlayer.userID);
                })
            };
            reqTimer.Destroy();
            PendingRequests.Remove(player.userID);
            PlayersRequests.Remove(player.userID);
            PlayersRequests.Remove(originPlayer.userID);
        }

        private void CommandWipeHomes(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !IsAllowedMsg(player, PermWipeHomes) || !player.IsConnected || player.IsSleeping()) return;
            _Home.Clear();
            changedHome = true;
            PrintMsgL(player, "HomesListWiped");
        }

        private void CommandTeleportHelp(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !player.IsConnected || player.IsSleeping()) return;
            if (!config.Settings.HomesEnabled && !config.Settings.TPREnabled && !IsAllowedMsg(player)) return;
            if (args.Length == 1)
            {
                var key = $"TPHelp{args[0].ToLower()}";
                var msg = _(key, player);
                if (key.Equals(msg))
                    PrintMsgL(player, "InvalidHelpModule");
                else
                    PrintMsg(player, msg);
            }
            else
            {
                var msg = _("TPHelpGeneral", player);
                if (IsAllowed(player))
                    msg += NewLine + "/tphelp AdminTP";
                if (config.Settings.HomesEnabled)
                    msg += NewLine + "/tphelp Home";
                if (config.Settings.TPREnabled)
                    msg += NewLine + "/tphelp TPR";
                PrintMsg(player, msg);
            }
        }

        private List<string> _tpid = new List<string> { "home", "bandit", "outpost", "tpr", "town" };

        private void CommandTeleportInfo(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length == 1)
            {
                var module = args[0].ToLower();
                var settings = GetSettings(module);
                var msg = _(_tpid.Contains(module) || settings == null ? $"TPSettings{module}" : "TPSettingsdynamic", player);
                var timestamp = Facepunch.Math.Epoch.Current;
                var currentDate = DateTime.Now.ToString("d");
                int limit;
                int cooldown;

                switch (module)
                {
                    case "home":
                        limit = GetHigher(player, config.Home.VIPDailyLimits, config.Home.DailyLimit, true);
                        cooldown = GetLower(player, config.Home.VIPCooldowns, config.Home.Cooldown);
                        int homeLimits = GetHigher(player, config.Home.VIPHomesLimits, config.Home.HomesLimit, true);
                        PrintMsg(player, string.Format(msg, FormatTime(player, cooldown), limit > 0 ? limit.ToString() : _("Unlimited", player), homeLimits));
                        HomeData homeData;
                        if (!_Home.TryGetValue(player.userID, out homeData))
                            _Home[player.userID] = homeData = new HomeData();
                        if (homeData.Teleports.Date != currentDate)
                        {
                            homeData.Teleports.Amount = 0;
                            homeData.Teleports.Date = currentDate;
                        }
                        if (limit > 0) PrintMsgL(player, "HomeTPAmount", limit - homeData.Teleports.Amount);
                        if (cooldown > 0 && timestamp - homeData.Teleports.Timestamp < cooldown)
                        {
                            var remain = cooldown - (timestamp - homeData.Teleports.Timestamp);
                            PrintMsgL(player, "HomeTPCooldown", FormatTime(player, remain));
                        }
                        break;
                    case "tpr":
                        limit = GetHigher(player, config.TPR.VIPDailyLimits, config.TPR.DailyLimit, true);
                        cooldown = GetLower(player, config.TPR.VIPCooldowns, config.TPR.Cooldown);
                        PrintMsg(player, string.Format(msg, FormatTime(player, cooldown), limit > 0 ? limit.ToString() : _("Unlimited", player)));
                        TeleportData tprData;
                        if (!_TPR.TryGetValue(player.userID, out tprData))
                            _TPR[player.userID] = tprData = new TeleportData();
                        if (tprData.Date != currentDate)
                        {
                            tprData.Amount = 0;
                            tprData.Date = currentDate;
                        }
                        if (limit > 0) PrintMsgL(player, "TPRAmount", limit - tprData.Amount);
                        if (cooldown > 0 && timestamp - tprData.Timestamp < cooldown)
                        {
                            var remain = cooldown - (timestamp - tprData.Timestamp);
                            PrintMsgL(player, "TPRCooldown", FormatTime(player, remain));
                        }
                        break;
                    default: // town island outpost bandit etc
                        if (settings == null)
                        {
                            PrintMsgL(player, "InvalidHelpModule");
                            break;
                        }

                        limit = GetHigher(player, settings.VIPDailyLimits, settings.DailyLimit, true);
                        cooldown = GetLower(player, settings.VIPCooldowns, settings.Cooldown);
                        if (_tpid.Contains(module)) PrintMsg(player, string.Format(msg, FormatTime(player, cooldown), limit > 0 ? limit.ToString() : _("Unlimited", player)));
                        else PrintMsg(player, string.Format(msg, module.SentenceCase(), FormatTime(player, cooldown), limit > 0 ? limit.ToString() : _("Unlimited", player)));
                        TeleportData tpData;
                        if (!settings.Teleports.TPData.TryGetValue(player.userID, out tpData))
                            settings.Teleports.TPData[player.userID] = tpData = new TeleportData();
                        if (tpData.Date != currentDate)
                        {
                            tpData.Amount = 0;
                            tpData.Date = currentDate;
                        }
                        var language = lang.GetMessage(settings.Command, this, user.Id);
                        if (limit > 0) PrintMsgL(player, "DM_TownTPAmount", limit - tpData.Amount, language);
                        if (!string.IsNullOrEmpty(config.Settings.BypassCMD) && cooldown > 0 && timestamp - tpData.Timestamp < cooldown)
                        {
                            var remain = cooldown - (timestamp - tpData.Timestamp);

                            PrintMsgL(player, "DM_TownTPCooldown", FormatTime(player, remain));

                            if (settings.Bypass > 0)
                            {
                                PrintMsgL(player, "DM_TownTPCooldownBypassP", settings.Bypass);
                                PrintMsgL(player, "DM_TownTPCooldownBypassP2", language, config.Settings.BypassCMD);
                            }
                        }
                        break;
                }
            }
            else
            {
                var msg = _("TPInfoGeneral", player);
                if (config.Settings.HomesEnabled)
                    msg += NewLine + "/tpinfo Home";
                if (config.Settings.TPREnabled)
                    msg += NewLine + "/tpinfo TPR";
                foreach (var entry in config.DynamicCommands)
                {
                    if (entry.Value.Enabled)
                    {
                        if (command == "bandit" && !banditEnabled) continue;
                        if (command == "outpost" && !outpostEnabled) continue;
                        if (!IsAllowed(player, $"{Name}.tp{entry.Key}")) continue;
                        msg += NewLine + $"/tpinfo {entry.Key}";
                    }
                }
                PrintMsgL(player, msg);
            }
        }

        private void CommandTeleportCancel(IPlayer user, string command, string[] args)
        {
            var player = user.Object as BasePlayer;
            if (!player || !player.IsConnected || player.IsSleeping()) return;
            if (args.Length != 0)
            {
                PrintMsgL(player, "SyntaxCommandTPC");
                return;
            }
            TeleportTimer teleportTimer;
            if (TeleportTimers.TryGetValue(player.userID, out teleportTimer))
            {
                teleportTimer.Timer?.Destroy();
                PrintMsgL(player, "TPCancelled");
                PrintMsgL(teleportTimer.TargetPlayer, "TPCancelledTarget", player.displayName);
                TeleportTimers.Remove(player.userID);
                return;
            }
            foreach (var keyValuePair in TeleportTimers)
            {
                if (keyValuePair.Value.TargetPlayer != player) continue;
                keyValuePair.Value.Timer?.Destroy();
                PrintMsgL(keyValuePair.Value.OriginPlayer, "TPCancelledTarget", player.displayName);
                PrintMsgL(player, "TPYouCancelledTarget", keyValuePair.Value.OriginPlayer.displayName);
                TeleportTimers.Remove(keyValuePair.Key);
                return;
            }
            BasePlayer target;
            if (!PlayersRequests.TryGetValue(player.userID, out target))
            {
                PrintMsgL(player, "NoPendingRequest");
                return;
            }
            Timer reqTimer;
            if (PendingRequests.TryGetValue(player.userID, out reqTimer))
            {
                reqTimer.Destroy();
                PendingRequests.Remove(player.userID);
            }
            else if (PendingRequests.TryGetValue(target.userID, out reqTimer))
            {
                reqTimer.Destroy();
                PendingRequests.Remove(target.userID);
                var temp = player;
                player = target;
                target = temp;
            }
            PlayersRequests.Remove(target.userID);
            PlayersRequests.Remove(player.userID);
            PrintMsgL(player, "Cancelled", target.displayName);
            PrintMsgL(target, "CancelledTarget", player.displayName);
        }

        private void CommandDynamic(IPlayer user, string command, string[] args)
        {
            if (!user.HasPermission(PermAdmin) || args.Length != 2 || args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                CommandTeleportInfo(user, command, args);
                return;
            }

            var value = args[1].ToLower();

            if (args[0].Equals("add", StringComparison.OrdinalIgnoreCase))
            {
                TownSettings settings;
                if (GetSettings(value) == null)
                {
                    config.DynamicCommands.Add(value, settings = new TownSettings());
                    RegisterCommand(value, settings, true);
                    RegisterCommand(value, nameof(CommandCustom));
                    PrintMsgL(user, "DM_TownTPCreated", value);
                    SaveConfig();
                }
                else PrintMsgL(user, "DM_TownTPExists", value);
            }
            else if (args[0].Equals("remove", StringComparison.OrdinalIgnoreCase))
            {
                var key = config.DynamicCommands.Keys.FirstOrDefault(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(key))
                {
                    PrintMsgL(user, "DM_TownTPRemoved", key);
                    config.DynamicCommands.Remove(key);
                    UnregisterCommand(value);
                    SaveConfig();
                }
                else PrintMsgL(user, "DM_TownTPDoesNotExist", value);
            }
            else CommandTeleportInfo(user, command, args);
        }

        private void CommandCustom(IPlayer user, string command, string[] args)
        {
            CommandTown(user, command, args);
        }

        private TownSettings GetSettings(string command, ulong userid = 0uL)
        {
            if (command.Equals("home", StringComparison.OrdinalIgnoreCase) && _Home.ContainsKey(userid))
            {
                return new TownSettings
                {
                    VIPCooldowns = config.Home.VIPCooldowns,
                    Cooldown = config.Home.Cooldown,
                    Countdown = config.Home.Countdown,
                    Teleports = new StoredData
                    {
                        TPData = new Dictionary<ulong, TeleportData>
                        {
                            [userid] = _Home[userid].Teleports
                        }
                    }
                };
            }

            if (command.Equals("tpr", StringComparison.OrdinalIgnoreCase) && _TPR.ContainsKey(userid))
            {
                return new TownSettings
                {
                    VIPCooldowns = config.TPR.VIPCooldowns,
                    Cooldown = config.TPR.Cooldown,
                    Countdown = config.TPR.Countdown,
                    Teleports = new StoredData
                    {
                        TPData = new Dictionary<ulong, TeleportData>
                        {
                            [userid] = _TPR[userid]
                        }
                    }
                };
            }

            foreach (var x in config.DynamicCommands)
            {
                if (x.Key.Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    return x.Value;
                }
            }

            return null;
        }

        private void CommandTown(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command)) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !player.IsConnected || player.IsSleeping()) return;
#if DEBUG
            Puts($"cmdChatTown: command={command}");
#endif
            if (!IsAllowedMsg(player, $"{Name}.tp{command}".ToLower())) return;

            if (!CanBypassRestrictions(player.UserIDString))
            {
                float globalCooldownTime = GetGlobalCooldown(player);
                if (globalCooldownTime > 0f)
                {
                    PrintMsgL(player, "WaitGlobalCooldown", FormatTime(player, (int)globalCooldownTime));
                    return;
                }

                if (config.Settings.BlockAuthorizedTeleporting && player.IsBuildingAuthed())
                {
                    PrintMsgL(player, "CannotTeleportFromHome");
                    return;
                }
            }

            var settings = GetSettings(command);

            if (settings == null)
            {
                return;
            }

            var language = lang.GetMessage(settings.Command, this, user.Id);

            // For admin using set, add, clear or show command locations
            if (args.Length >= 1 && IsAllowed(player, PermAdmin))
            {
                var param = args[0].ToLower();

                if (param.Equals("clear"))
                {
                    settings.Location = Vector3.zero;
                    settings.Locations.Clear();
                    SaveConfig();
                    PrintMsgL(player, "DM_TownTPLocationsCleared", language);
                    return;
                }
                else if (param.Equals("set"))
                {
                    if (settings.Locations.Count > 0)
                    {
                        settings.Locations.RemoveAt(0);
                    }
                    var position = player.transform.position;
                    settings.Locations.Insert(0, settings.Location = position);
                    SaveConfig();
                    PrintMsgL(player, "DM_TownTPLocation", language, position);
                    return;
                }
                else if (param.Equals("add"))
                {
                    var position = player.transform.position;
                    int num = settings.Locations.RemoveAll(x => Vector3.Distance(position, x) < 25f);
                    settings.Locations.Add(position);
                    SaveConfig();
                    PrintMsgL(player, "DM_TownTPLocation", language, position);
                    return;
                }
                else if (args[0].Equals("show", StringComparison.OrdinalIgnoreCase))
                {
                    settings.Locations.ForEach(x => player.SendConsoleCommand("ddraw.text", 30f, Color.green, x, command));
                    return;
                }
            }

            bool paidmoney = false;

            // Is command usage enabled?
            if (!settings.Enabled)
            {
                PrintMsgL(player, "DM_TownTPDisabled", language.SentenceCase());
                return;
            }

            if (settings.Location != Vector3.zero && !settings.Locations.Contains(settings.Location))
            {
                settings.Locations.Add(settings.Location);
            }

            // Is location set?
            if (settings.Locations.Count == 0)
            {
                PrintMsgL(player, "DM_TownTPNoLocation", language.SentenceCase());
                return;
            }

            // Are they trying to bypass cooldown or did they just type something else?
            if (args.Length == 1 && !string.IsNullOrEmpty(config.Settings.BypassCMD) && args[0].ToLower() != config.Settings.BypassCMD.ToLower() && !args[0].All(char.IsDigit))
            {
                string com = command ?? "town";
                string msg = "SyntaxCommand" + char.ToUpper(com[0]) + com.Substring(1);
                PrintMsgL(player, msg);
                if (IsAllowed(player)) PrintMsgL(player, msg + "Admin");
                return;
            }

            TeleportData teleportData;
            if (!settings.Teleports.TPData.TryGetValue(player.userID, out teleportData))
            {
                settings.Teleports.TPData[player.userID] = teleportData = new TeleportData();
            }
            int limit = 0;
            var timestamp = Facepunch.Math.Epoch.Current;
            var currentDate = DateTime.Now.ToString("d");

            // Setup vars for checks below
            string err = null;
            if (!CanBypassRestrictions(player.UserIDString))
            {
                err = CheckPlayer(player, settings.UsableOutOfBuildingBlocked, settings.CanCraft(player, command), true, command);
                if (err != null)
                {
                    PrintMsgL(player, err);
                    return;
                }
                var cooldown = GetLower(player, settings.VIPCooldowns, settings.Cooldown);

                if (teleportData.Date != currentDate)
                {
                    teleportData.Amount = 0;
                    teleportData.Date = currentDate;
                }
                limit = GetHigher(player, settings.VIPDailyLimits, settings.DailyLimit, true);
#if DEBUG
                Puts("Calling CheckPlayer from cmdChatTown");
#endif

                // Check and process cooldown, bypass, and payment for all modes
                if (cooldown > 0 && timestamp - teleportData.Timestamp < cooldown)
                {
                    var cmdSent = args.Length >= 1 ? args[0].ToLower() : string.Empty;

                    if (!string.IsNullOrEmpty(config.Settings.BypassCMD))
                    {
                        if (cmdSent == config.Settings.BypassCMD.ToLower() && settings.Bypass > -1)
                        {
                            bool foundmoney = CheckEconomy(player, settings.Bypass);

                            if (foundmoney)
                            {
                                CheckEconomy(player, settings.Bypass, true);
                                paidmoney = true;

                                if (settings.Bypass > 0)
                                {
                                    PrintMsgL(player, "DM_TownTPCooldownBypass", settings.Bypass);
                                }

                                if (settings.Pay > 0)
                                {
                                    PrintMsgL(player, "PayToTown", settings.Pay, language);
                                }
                            }
                            else
                            {
                                PrintMsgL(player, "DM_TownTPCooldownBypassF", settings.Bypass);
                                return;
                            }
                        }
                        else if (UseEconomy())
                        {
                            var remain = cooldown - (timestamp - teleportData.Timestamp);
                            PrintMsgL(player, "DM_TownTPCooldown", FormatTime(player, remain));
                            if (settings.Bypass > -1)
                            {
                                PrintMsgL(player, "DM_TownTPCooldownBypassP", settings.Bypass);
                                PrintMsgL(player, "DM_TownTPCooldownBypassP2", language, config.Settings.BypassCMD);
                            }
                            return;
                        }
                        else
                        {
                            var remain = cooldown - (timestamp - teleportData.Timestamp);
                            PrintMsgL(player, "DM_TownTPCooldown", FormatTime(player, remain));
                            return;
                        }
                    }
                    else
                    {
                        var remain = cooldown - (timestamp - teleportData.Timestamp);
                        PrintMsgL(player, "DM_TownTPCooldown", FormatTime(player, remain));
                        return;
                    }
                }

                if (limit > 0 && teleportData.Amount >= limit)
                {
                    var left = FormatTime(player, (int)SecondsUntilTomorrow());
                    PrintMsgL(player, "DM_TownTPLimitReached", limit, left);
                    return;
                }
            }
            if (TeleportTimers.ContainsKey(player.userID))
            {
                PrintMsgL(player, "TeleportPendingTPC");
                return;
            }

            Vector3 location;
            int index;
            if (args.Length == 1 && int.TryParse(args[0], out index))
            {
                index = Mathf.Clamp(index, 0, settings.Locations.Count - 1);
                location = settings.Locations[index];
            }
            else if (settings.Random)
            {
                location = settings.Locations.GetRandom();
            }
            else location = settings.Locations.First();

            if (!CanBypassRestrictions(player.UserIDString))
            {
                err = CanPlayerTeleport(player, location);
                if (err != null)
                {
                    SendReply(player, err);
                    return;
                }
                err = CheckItems(player);
                if (err != null)
                {
                    PrintMsgL(player, "TPBlockedItem", err);
                    return;
                }
            }
            int countdown = GetLower(player, settings.VIPCountdowns, settings.Countdown);
            TeleportTimers[player.userID] = new TeleportTimer
            {
                OriginPlayer = player,
                Timer = timer.Once(countdown, () =>
                {
#if DEBUG
                    Puts($"Calling CheckPlayer from cmdChatTown {command} timer loop");
#endif
                    if (!CanBypassRestrictions(player.UserIDString))
                    {
                        if (config.Settings.BlockAuthorizedTeleporting && player.IsBuildingAuthed())
                        {
                            PrintMsgL(player, "CannotTeleportFromHome");
                            return;
                        }
                        err = CheckPlayer(player, settings.UsableOutOfBuildingBlocked, settings.CanCraft(player, command.ToLower()), true, command.ToLower(), settings.AllowCave);
                        if (err != null)
                        {
                            Interrupt(player, paidmoney, settings.Bypass);
                            PrintMsgL(player, err);
                            return;
                        }
                        err = CanPlayerTeleport(player, location);
                        if (err != null)
                        {
                            Interrupt(player, paidmoney, settings.Bypass);
                            PrintMsgL(player, err);
                            return;
                        }
                        err = CheckItems(player);
                        if (err != null)
                        {
                            Interrupt(player, paidmoney, settings.Bypass);
                            PrintMsgL(player, "TPBlockedItem", err);
                            return;
                        }
                        if (settings.Locations.Count == 0)
                        {
                            Interrupt(player, paidmoney, settings.Bypass);
                            return;
                        }
                        if (UseEconomy())
                        {
                            if (settings.Pay < 0)
                            {
                                return;
                            }
                            if (settings.Pay > 0 && !CheckEconomy(player, settings.Pay))
                            {
                                Interrupt(player, false, 0);
                                PrintMsgL(player, "TPNoMoney", settings.Pay);
                                return;
                            }
                            if (settings.Pay > -1 && !paidmoney)
                            {
                                CheckEconomy(player, settings.Pay, true);

                                if (settings.Pay > 0)
                                {
                                    PrintMsgL(player, "TPMoney", (double)settings.Pay);
                                }
                            }
                        }
                    }
                    Teleport(player, location, settings.AllowTPB);
                    teleportData.Amount++;
                    teleportData.Timestamp = timestamp;
                    settings.Teleports.Changed = true;
                    PrintMsgL(player, "DM_TownTP", language);
                    if (limit > 0) PrintMsgL(player, "DM_TownTPAmount", limit - teleportData.Amount, language);
                    TeleportTimers.Remove(player.userID);
                })
            };

            if (countdown > 0)
            {
                PrintMsgL(player, "DM_TownTPStarted", language, countdown);
            }
        }

        private double SecondsUntilTomorrow()
        {
            var tomorrow = DateTime.Now.AddDays(1).Date;
            return (tomorrow - DateTime.Now).TotalSeconds;
        }

        private void Interrupt(BasePlayer player, bool paidmoney, double bypass)
        {
            PrintMsgL(player, "Interrupted");
            if (paidmoney)
            {
                CheckEconomy(player, bypass, false, true);
            }
            TeleportTimers.Remove(player.userID);
        }

        private void CommandTeleportII(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (player != null && (!IsAllowedMsg(player, PermTpConsole) || !player.IsConnected || player.IsSleeping())) return;

            List<BasePlayer> players;
            switch (command)
            {
                case "teleport.topos":
                    if (args.Length < 4)
                    {
                        user.Reply(_("SyntaxConsoleCommandToPos", player));
                        return;
                    }
                    players = FindPlayers(args[0], true);
                    if (players.Count <= 0)
                    {
                        user.Reply(_("PlayerNotFound", player));
                        return;
                    }
                    if (players.Count > 1)
                    {
                        user.Reply(_("MultiplePlayers", player, GetMultiplePlayers(players)));
                        return;
                    }
                    var targetPlayer = players.First();
                    players.Clear();
                    float x;
                    if (!float.TryParse(args[1], out x)) x = -10000f;
                    float y;
                    if (!float.TryParse(args[2], out y)) y = -10000f;
                    float z;
                    if (!float.TryParse(args[3], out z)) z = -10000f;
                    if (!CheckBoundaries(x, y, z))
                    {
                        user.Reply(_("AdminTPOutOfBounds", player) + System.Environment.NewLine + _("AdminTPBoundaries", player, boundary));
                        return;
                    }
                    Teleport(targetPlayer, x, y, z);
                    if (config.Admin.AnnounceTeleportToTarget)
                        PrintMsgL(targetPlayer, "AdminTPConsoleTP", targetPlayer.transform.position);
                    user.Reply(_("AdminTPTargetCoordinates", player, targetPlayer.displayName, targetPlayer.transform.position));
                    Puts(_("LogTeleportPlayer", null, player?.displayName, targetPlayer.displayName, targetPlayer.transform.position));
                    break;
                case "teleport.toplayer":
                    if (args.Length < 2)
                    {
                        user.Reply(_("SyntaxConsoleCommandToPlayer", player));
                        return;
                    }
                    players = FindPlayers(args[0], true);
                    if (players.Count <= 0)
                    {
                        user.Reply(_("PlayerNotFound", player));
                        return;
                    }
                    if (players.Count > 1)
                    {
                        user.Reply(_("MultiplePlayers", player, GetMultiplePlayers(players)));
                        return;
                    }
                    var originPlayer = players.First();
                    players = FindPlayers(args[1], true);
                    if (players.Count <= 0)
                    {
                        user.Reply(_("PlayerNotFound", player));
                        return;
                    }
                    if (players.Count > 1)
                    {
                        user.Reply(_("MultiplePlayers", player, GetMultiplePlayers(players)));
                        players.Clear();
                        return;
                    }
                    targetPlayer = players.First();
                    if (targetPlayer == originPlayer)
                    {
                        players.Clear();
                        user.Reply(_("CantTeleportPlayerToSelf", player));
                        return;
                    }
                    players.Clear();
                    Teleport(originPlayer, targetPlayer);
                    user.Reply(_("AdminTPPlayers", player, originPlayer.displayName, targetPlayer.displayName));
                    PrintMsgL(originPlayer, "AdminTPConsoleTPPlayer", targetPlayer.displayName);
                    if (config.Admin.AnnounceTeleportToTarget)
                        PrintMsgL(targetPlayer, "AdminTPConsoleTPPlayerTarget", originPlayer.displayName);
                    Puts(_("LogTeleportPlayer", null, player?.displayName, originPlayer.displayName, targetPlayer.displayName));
                    break;
            }
        }

        private float GetMonumentFloat(string monumentName)
        {
            string name = monumentName.Contains(":") ? monumentName.Substring(0, monumentName.LastIndexOf(":")) : monumentName.TrimEnd();

            switch (name)
            {
                case "Abandoned Cabins":
                    return 54f;
                case "Abandoned Supermarket":
                    return 50f;
                case "Airfield":
                    return 200f;
                case "Barn":
                case "Large Barn":
                    return 75f;
                case "Fishing Village":
                case "Large Fishing Village":
                    return 50f;
                case "Bandit Camp":
                    return 125f;
                case "Junkyard":
                    return 100f;
                case "Giant Excavator Pit":
                    return 225f;
                case "Harbor":
                    return 150f;
                case "HQM Quarry":
                    return 37.5f;
                case "Ice Lake":
                    return 50f;
                case "Large Oil Rig":
                    return 200f;
                case "Launch Site":
                    return 300f;
                case "Lighthouse":
                    return 48f;
                case "Military Tunnel":
                    return 100f;
                case "Mining Outpost":
                    return 45f;
                case "Oil Rig":
                    return 100f;
                case "Outpost":
                    return 250f;
                case "Oxum's Gas Station":
                    return 65f;
                case "Power Plant":
                    return 140f;
                case "power_sub_small_1":
                case "power_sub_small_2":
                case "power_sub_big_1":
                case "power_sub_big_2":
                    return 30f;
                case "Ranch":
                    return 75f;
                case "Satellite Dish":
                    return 90f;
                case "Sewer Branch":
                    return 100f;
                case "Stone Quarry":
                    return 27.5f;
                case "Sulfur Quarry":
                    return 27.5f;
                case "The Dome":
                    return 70f;
                case "Train Yard":
                    return 150f;
                case "Underwater Lab":
                    return 100f;
                case "Water Treatment Plant":
                    return 185f;
                case "Water Well":
                    return 24f;
                case "Wild Swamp":
                    return 24f;
            }

            return config.Settings.DefaultMonumentSize;
        }

        private void CommandSphereMonuments(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;
            if (!player || !player.IsAdmin || !player.IsConnected || player.IsSleeping()) return;

            //foreach (var monument in  TerrainMeta.Path.Monuments) player.SendConsoleCommand("ddraw.sphere", 30f, Color.blue, monument.transform.position, monument.Bounds.size.Max());

            foreach (var monument in monuments)
            {
                string name = monument.Key.Contains(":") ? monument.Key.Substring(0, monument.Key.LastIndexOf(":")) : monument.Key.TrimEnd();

                player.SendConsoleCommand("ddraw.sphere", 30f, Color.red, monument.Value.Position, GetMonumentFloat(name));
                player.SendConsoleCommand("ddraw.text", 30f, Color.blue, monument.Value.Position, name);
            }

            foreach (var cave in caves)
            {
                string name = cave.Key.Contains(":") ? cave.Key.Substring(0, cave.Key.LastIndexOf(":")) : cave.Key.TrimEnd();
                float realdistance = cave.Key.Contains("Small") ? config.Settings.CaveDistanceSmall : cave.Key.Contains("Medium") ? config.Settings.CaveDistanceMedium : config.Settings.CaveDistanceLarge;
                realdistance += 50f;

                player.SendConsoleCommand("ddraw.sphere", 30f, Color.black, cave.Value, realdistance);
                player.SendConsoleCommand("ddraw.text", 30f, Color.cyan, cave.Value, name);
            }
        }

        private void CommandImportHomes(IPlayer user, string command, string[] args)
        {
            if (DisabledCommandData.DisabledCommands.Contains(command.ToLower())) { user.Reply("Disabled command: " + command); return; }
            var player = user.Object as BasePlayer;

            if (player != null && (!IsAllowedMsg(player, PermImportHomes) || !player.IsConnected || player.IsSleeping()))
            {
                user.Reply(_("NotAllowed", player));
                return;
            }
            var fileName = string.IsNullOrEmpty(config.Settings.DataFileFolder) ? "m-Teleportation" : $"{config.Settings.DataFileFolder}{Path.DirectorySeparatorChar}m-Teleportation";
            var datafile = Interface.Oxide.DataFileSystem.GetFile(fileName);
            if (!datafile.Exists())
            {
                user.Reply("No m-Teleportation.json exists.");
                return;
            }
            datafile.Load();
            var allHomeData = datafile["HomeData"] as Dictionary<string, object>;
            if (allHomeData == null)
            {
                user.Reply(_("HomeListEmpty", player));
                return;
            }
            var count = 0;
            foreach (var kvp in allHomeData)
            {
                var homeDataOld = kvp.Value as Dictionary<string, object>;
                if (homeDataOld == null) continue;
                if (!homeDataOld.ContainsKey("HomeLocations")) continue;
                var homeList = homeDataOld["HomeLocations"] as Dictionary<string, object>;
                if (homeList == null) continue;
                var userId = Convert.ToUInt64(kvp.Key);
                HomeData homeData;
                if (!_Home.TryGetValue(userId, out homeData))
                    _Home[userId] = homeData = new HomeData();
                foreach (var kvp2 in homeList)
                {
                    var positionData = kvp2.Value as Dictionary<string, object>;
                    if (positionData == null) continue;
                    if (!positionData.ContainsKey("x") || !positionData.ContainsKey("y") || !positionData.ContainsKey("z")) continue;
                    var position = new Vector3(Convert.ToSingle(positionData["x"]), Convert.ToSingle(positionData["y"]), Convert.ToSingle(positionData["z"]));
                    homeData.Locations[kvp2.Key] = position;
                    changedHome = true;
                    count++;
                }
            }
            user.Reply(string.Format("Imported {0} homes.", count));
        }

        private void RequestTimedOut(BasePlayer player, BasePlayer target)
        {
            PlayersRequests.Remove(player.userID);
            PlayersRequests.Remove(target.userID);
            PendingRequests.Remove(target.userID);
            PrintMsgL(player, "TimedOut", target.displayName);
            PrintMsgL(target, "TimedOutTarget", player.displayName);
        }

        #region Util

        private string FormatTime(BasePlayer player, int seconds) // Credits MoNaH
        {
            if (config.Settings.UseSeconds) return $"{seconds} {_("Seconds", player)}";

            TimeSpan _ts = TimeSpan.FromSeconds(seconds);

            _sb.Length = 0;

            if (_ts.TotalDays >= 1)
            {
                _sb.Append($"<color={config.Settings.ChatCommandArgumentColor}>{_ts.Days}</color> {_("Days", player)} ");
            }

            if (_ts.TotalHours >= 1)
            {
                _sb.Append($"<color={config.Settings.ChatCommandArgumentColor}>{_ts.Hours}</color> {_("Hours", player)} ");
            }

            if (_ts.TotalMinutes >= 1)
            {
                _sb.Append($"<color={config.Settings.ChatCommandArgumentColor}>{_ts.Minutes}</color> {_("Minutes", player)} ");
            }

            _sb.Append($"<color={config.Settings.ChatCommandArgumentColor}>{_ts.Seconds}</color> {_("Seconds", player)} ");

            return _sb.ToString();
        }

        private double ConvertToRadians(double angle)
        {
            return System.Math.PI / 180 * angle;
        }
        #endregion

        #region Teleport

        public void Teleport(BasePlayer player, BasePlayer target) => Teleport(player, target.transform.position, true);

        public void Teleport(BasePlayer player, float x, float y, float z) => Teleport(player, new Vector3(x, y, z), true);

        [HookMethod("Teleport")]
        public void Teleport(BasePlayer player, Vector3 newPosition, bool allowTPB)
        {
            if (!player.IsValid() || Vector3.Distance(newPosition, Vector3.zero) < 5f) return;
            if (allowTPB)
            {
                if (config.Settings.TPBTime > 0)
                {
                    Vector3 position = player.transform.position;
                    timer.In(config.Settings.TPBTime, () => SaveLocation(player, position));
                }
                else SaveLocation(player, player.transform.position);
            }

            newPosition.y += 0.1f;

            teleporting[player.userID] = newPosition;

            Subscribe(nameof(OnPlayerViolation));

            var oldPosition = player.transform.position;

            try
            {
                player.UpdateActiveItem(0u); // Prevent weapons when going to safe zone
                player.EnsureDismounted(); // 1.1.2 @Def
                player.Server_CancelGesture();

                if (player.HasParent())
                {
                    player.SetParent(null, true, true);
                }

                if (player.IsConnected) // 1.1.2 @Def
                {
                    player.EndLooting();
                    StartSleeping(player);
                }

                player.RemoveFromTriggers(); // 1.1.2 @Def recommendation to use natural method for issue with triggers
                player.Teleport(newPosition); // 1.1.6

                if (player.IsConnected && !Net.sv.visibility.IsInside(player.net.group, newPosition))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                    player.ClientRPCPlayer(null, player, "StartLoading");
                    player.SendEntityUpdate();

                    if (!IsInvisible(player)) // fix for becoming networked briefly with vanish while teleporting
                    {
                        player.UpdateNetworkGroup(); // 1.1.1 building fix @ctv
                        player.SendNetworkUpdateImmediate(false);
                    }
                }
            }
            finally
            {
                if (!IsInvisible(player))
                    player.ForceUpdateTriggers(); // 1.1.4 exploit fix for looting sleepers in safe zones
            }

            SetGlobalCooldown(player);

            Interface.CallHook("OnPlayerTeleported", player, oldPosition, newPosition);
        }

        private void OnMapMarkerAdded(BasePlayer player, ProtoBuf.MapNote note)
        {
            if (player.IsAlive() && permission.UserHasPermission(player.UserIDString, PermTpMarker))
            {
                float y = TerrainMeta.HeightMap.GetHeight(note.worldPosition);
                if (player.IsFlying) y = Mathf.Max(y, player.transform.position.y);
                Teleport(player, note.worldPosition + new Vector3(0f, y, 0f), true);
            }
        }

        bool IsInvisible(BasePlayer player)
        {
            return Vanish != null && Convert.ToBoolean(Vanish?.Call("IsInvisible", player));
        }

        public void StartSleeping(BasePlayer player) // custom as to not cancel crafting, or remove player from vanish
        {
            if (!player.IsSleeping())
            {
                Interface.CallHook("OnPlayerSleep", player);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
                player.sleepStartTime = Time.time;
                BasePlayer.sleepingPlayerList.Add(player);
                player.CancelInvoke("InventoryUpdate");
                player.CancelInvoke("TeamUpdate");
            }
        }

        #endregion

        #region Checks
        private string CanPlayerTeleport(BasePlayer player, Vector3 to)
        {
            if (CanBypassRestrictions(player.UserIDString)) return null;
            return Interface.Oxide.CallHook("CanTeleport", player, to) as string;
        }

        private bool CanCraftHome(BasePlayer player)
        {
            return config.Home.AllowCraft || permission.UserHasPermission(player.UserIDString, PermCraftHome) || CanBypassRestrictions(player.UserIDString);
        }

        private bool CanCraftTPR(BasePlayer player)
        {
            return config.TPR.AllowCraft || permission.UserHasPermission(player.UserIDString, PermCraftTpR) || CanBypassRestrictions(player.UserIDString);
        }

        public bool AboveWater(BasePlayer player)
        {
            var pos = player.transform.position;
#if DEBUG
            Puts($"Player position: {pos}.  Checking for water...");
#endif
            if ((TerrainMeta.HeightMap.GetHeight(pos) - TerrainMeta.WaterMap.GetHeight(pos)) < 0)
            {
#if DEBUG
                Puts("Player is above water!");
#endif
                return true;
            }
            else
            {
#if DEBUG
                Puts("Player not above water.");
#endif
                return false;
            }
        }

        private string NearMonument(Vector3 target)
        {
            foreach (var entry in monuments)
            {
                if (entry.Key.ToLower().Contains("power_")) continue;
                if (entry.Key.ToLower().Contains("swamp")) continue;
                if (entry.Key.Contains("monument_marker.prefab") && config.Settings.Interrupt.BypassMonumentMarker) continue;

                var pos = entry.Value.Position;
                pos.y = target.y;
                float dist = (target - pos).magnitude;
#if DEBUG
                Puts($"Checking {entry.Key} dist: {dist}, realdistance: {entry.Value.Radius}");
#endif
                if (dist < entry.Value.Radius)
                {
                    if (config.Home.AllowedMonuments.Any(m => entry.Key.Equals(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        return null;
                    }

                    if (config.Settings.Interrupt.Monuments.Count > 0)
                    {
                        if (config.Settings.Interrupt.Monuments.Exists(value => entry.Key.Contains(value, CompareOptions.OrdinalIgnoreCase)))
                        {
#if DEBUG
                            Puts($"Player in range of {entry.Key}");
#endif
                            return entry.Key;
                        }

                        return null;
                    }
#if DEBUG
                    Puts($"Player in range of {entry.Key}");
#endif
                    return entry.Key;
                }
            }

            if (IsMonument(target))
            {
                return "monument";
            }

            return null;
        }

        private bool ContainsTopology(TerrainTopology.Enum mask, Vector3 position)
        {
            return (TerrainMeta.TopologyMap.GetTopology(position) & (int)mask) != 0;
        }

        public bool IsMonument(Vector3 position)
        {
            if (!config.Settings.MonumentTopologyCheck) return false;
            return !ContainsTopology(TerrainTopology.Enum.Building, position) && ContainsTopology(TerrainTopology.Enum.Monument, position);
        }

        public bool IsNearCave(BasePlayer player)
        {
            if (!config.Settings.CaveTopologyCheck) return false;
            return !player.IsOutside() && ContainsTopology(TerrainTopology.Enum.Building | TerrainTopology.Enum.Monument, player.transform.position);
        }

        private bool NearCave(BasePlayer player)
        {
            if (player.IsOutside())
            {
                return false;
            }

            foreach (var entry in caves)
            {
                float realdistance = entry.Key.Contains("Small") ? config.Settings.CaveDistanceSmall : entry.Key.Contains("Medium") ? config.Settings.CaveDistanceMedium : config.Settings.CaveDistanceLarge;

                if (realdistance <= 0)
                {
                    continue;
                }

                if (Vector3.Distance(player.transform.position, entry.Value) < realdistance + 50f)
                {
#if DEBUG
                    Puts("NearCave: {0} nearby.", entry.Key.Contains(":") ? entry.Key.Substring(0, entry.Key.LastIndexOf(":")) : entry.Key);
#endif
                    return true;
                }
                else
                {
#if DEBUG
                    Puts("NearCave: Not near this cave, or above it.");
#endif
                }
            }

            if (IsNearCave(player))
            {
                return true;
            }

            return false;
        }

        private string CheckPlayer(BasePlayer player, bool build = false, bool craft = false, bool origin = true, string mode = "home", bool allowcave = true)
        {
            if (CanBypassRestrictions(player.UserIDString)) return null;
            if (config.Settings.Interrupt.Oilrig || config.Settings.Interrupt.Excavator || config.Settings.Interrupt.Monument || mode == "sethome")
            {
                string monname = NearMonument(player.transform.position);

                if (!string.IsNullOrEmpty(monname))
                {
                    if (mode == "sethome")
                    {
                        if (config.Home.AllowAtAllMonuments || config.Home.AllowedMonuments.Exists(value => monname.Contains(value, CompareOptions.OrdinalIgnoreCase)))
                        {
                            return null;
                        }

                        return "HomeTooCloseToMon";
                    }
                    else
                    {
                        if (config.Settings.Interrupt.Oilrig && monname.Contains("Oil Rig"))
                        {
                            return "TPOilRig";
                        }

                        if (config.Settings.Interrupt.Excavator && monname.Contains("Excavator"))
                        {
                            return "TPExcavator";
                        }

                        if (config.Settings.Interrupt.Monument)
                        {
                            if (config.Home.AllowedMonuments.Exists(value => monname.Contains(value, CompareOptions.OrdinalIgnoreCase)))
                            {
                                return null;
                            }

                            if (monname.Contains(":")) monname = monname.Substring(0, monname.IndexOf(":"));
                            return _("TooCloseToMon", player, monname);
                        }
                    }
                }
            }

#if DEBUG
            Puts($"CheckPlayer(): called mode is {mode}");
#endif
            switch (mode)
            {
                case "tpt":
                    allowcave = config.TPT.AllowCave;
                    break;
                case "home":
                    allowcave = config.Home.AllowCave;
                    break;
                case "tpa":
                case "tpr":
                    allowcave = config.TPR.AllowCave;
                    break;
                default:
#if DEBUG
                    Puts("Skipping cave check...");
#endif
                    break;
            }
            if (!allowcave)
            {
#if DEBUG
                Puts("Checking cave distance...");
#endif
                if (NearCave(player))
                {
                    return "TooCloseToCave";
                }
            }

            if (config.Settings.Interrupt.Hostile && (mode == "bandit" || mode == "outpost" || mode == "town") && player.IsHostile())
            {
                return "TPHostile";
            }

            if (config.Settings.Interrupt.Junkpiles && IsOnJunkPile(player))
            {
                return "TPJunkpile";
            }

            if (config.Settings.Interrupt.Mounted && player.GetMounted() is BaseMountable)
            {
                return "TPMounted";
            }

            if (config.Settings.Interrupt.Boats && player.isMounted && player.GetMounted() is BaseBoat)
            {
                return "TPBoat";
            }

            if (config.Settings.Interrupt.Hurt && origin && player.IsWounded())
            {
                return "TPWounded";
            }

            if (config.Settings.Interrupt.Cold && player.metabolism.temperature.value <= config.Settings.MinimumTemp)
            {
                return "TPTooCold";
            }

            if (config.Settings.Interrupt.Hot && player.metabolism.temperature.value >= config.Settings.MaximumTemp)
            {
                return "TPTooHot";
            }

            if (config.Settings.Interrupt.AboveWater && AboveWater(player))
            {
                return "TPAboveWater";
            }

            if (config.Settings.Interrupt.Swimming && player.IsSwimming())
            {
                return "TPSwimming";
            }

            if (config.Settings.Interrupt.Cargo && player.GetComponentInParent<CargoShip>())
            {
                return "TPCargoShip";
            }

            if (config.Settings.Interrupt.Balloon && player.GetComponentInParent<HotAirBalloon>())
            {
                return "TPHotAirBalloon";
            }

            if (config.Settings.Interrupt.Lift && player.GetComponentInParent<Lift>())
            {
                return "TPBucketLift";
            }

            if (config.Settings.Interrupt.Lift && GetLift(player.transform.position))
            {
                return "TPRegLift";
            }

            if (config.Settings.Interrupt.Safe && player.InSafeZone())
            {
                return "TPSafeZone";
            }

            if (!craft && player.inventory.crafting.queue.Count > 0)
            {
                return "TPCrafting";
            }

            if (player.IsDead())
            {
                return "TPDead";
            }

            if (!build && !player.CanBuild())
            {
                return "TPBuildingBlocked";
            }

            if (config.Settings.BlockZoneFlag && ZoneManager != null)
            {
                var success = ZoneManager?.Call("PlayerHasFlag", player, "notp");

                if (success is bool && (bool)success)
                {
                    return "TPFlagZone";
                }
            }

            if (config.Settings.BlockNoEscape && NoEscape != null)
            {
                if (Convert.ToBoolean(NoEscape?.Call("IsBlocked", player)))
                {
                    return "TPNoEscapeBlocked";
                }
            }

            //if (AntiHack.TestInsideTerrain(player.transform.position + new Vector3(0f, 0.1f, 0f))) return "TPInsideTerrainFrom";

            return null;
        }

        private string CheckTargetLocation(BasePlayer player, Vector3 targetLocation, bool usableIntoBuildingBlocked, bool cupOwnerAllowOnBuildingBlocked)
        {
            if (CanBypassRestrictions(player.UserIDString)) return null;
            // ubb == UsableIntoBuildingBlocked
            // obb == CupOwnerAllowOnBuildingBlocked
            var entities = Pool.GetList<BuildingBlock>();
            Vis.Entities(targetLocation, 3f, entities, Layers.Mask.Construction, QueryTriggerInteraction.Ignore);
            bool denied = false;

            foreach (var block in entities)
            {
                if (CheckCupboardBlock(block, player, cupOwnerAllowOnBuildingBlocked))
                {
                    denied = false;
#if DEBUG
                    Puts("Cupboard either owned or there is no cupboard");
#endif
                }
                else if (usableIntoBuildingBlocked && player.userID != block.OwnerID)
                {
                    denied = false;
#if DEBUG
                    Puts("Player does not own block, but UsableIntoBuildingBlocked=true");
#endif
                }
                else if (player.userID == block.OwnerID)
                {
#if DEBUG
                    Puts("Player owns block");
#endif

                    if (!player.IsBuildingBlocked(targetLocation, new Quaternion(), block.bounds))
                    {
#if DEBUG
                        Puts("Player not BuildingBlocked. Likely unprotected building.");
#endif
                        denied = false;
                        break;
                    }
                    else if (usableIntoBuildingBlocked)
                    {
#if DEBUG
                        Puts("Player not blocked because UsableIntoBuildingBlocked=true");
#endif
                        denied = false;
                        break;
                    }
                    else
                    {
#if DEBUG
                        Puts("Player owns block but blocked by UsableIntoBuildingBlocked=false");
#endif
                        denied = true;
                        break;
                    }
                }
                else
                {
#if DEBUG
                    Puts("Player blocked");
#endif
                    denied = true;
                    break;
                }
            }
            Pool.FreeList(ref entities);

            return denied ? "TPTargetBuildingBlocked" : null;
        }

        // Check that a building block is owned by/attached to a cupboard, allow tp if not blocked unless allowed by config
        private bool CheckCupboardBlock(BuildingBlock block, BasePlayer player, bool cupOwnerAllowOnBuildingBlocked)
        {
            // obb == CupOwnerAllowOnBuildingBlocked
            var building = block.GetBuilding();
            if (building != null)
            {
#if DEBUG
                Puts("Found building, checking privileges...");
                Puts($"Building ID: {building.ID}");
#endif
                // cupboard overlap.  Check privs.
                if (building.buildingPrivileges == null)
                {
#if DEBUG
                    Puts("No cupboard found, allowing teleport");
#endif
                    return player.CanBuild();
                }

                foreach (var priv in building.buildingPrivileges)
                {
                    if (priv.IsAuthed(player))
                    {
                        // player is authorized to the cupboard
#if DEBUG
                        Puts("Player owns cupboard with auth");
#endif
                        return true;
                    }
                }

                if (player.userID == block.OwnerID)
                {
                    if (cupOwnerAllowOnBuildingBlocked)
                    {
#if DEBUG
                        // player set the cupboard and is allowed in by config
                        Puts("Player owns cupboard with no auth, but allowed by CupOwnerAllowOnBuildingBlocked=true");
#endif
                        return true;
                    }
#if DEBUG
                    // player set the cupboard but is blocked by config
                    Puts("Player owns cupboard with no auth, but blocked by CupOwnerAllowOnBuildingBlocked=false");
#endif
                    return false;
                }

#if DEBUG
                // player not authed
                Puts("Player does not own cupboard and is not authorized");
#endif
                return false;
            }
#if DEBUG
            Puts("No cupboard or building found - we cannot tell the status of this block");
#endif
            return true;
        }

        private string CheckInsideEntity(Vector3 targetLocation, ulong userid)
        {
            Vector3 a = targetLocation + new Vector3(0, 0.55f);
            var entities = FindEntitiesOfType<BaseEntity>(a, 0.5f, Layers.Mask.Construction | Layers.Mask.Deployed);
            if (entities.Any(e => e is BuildingBlock || e is SimpleBuildingBlock || e is IceFence || e is ElectricBattery || e is Door))
            {
                return "TPTargetInsideEntity";
            }
            if (Exploits.TestRock(targetLocation))
            {
                LogToFile("exploiters", $"{userid} sethome inside a rock at {targetLocation}", this, true);
                PrintMsgL(userid, "TPTargetInsideRock");
                return "TPTargetInsideRock";
            }
            if (Exploits.TestFoundation(targetLocation))
            {
                LogToFile("exploiters", $"{userid} sethome inside a foundation at {targetLocation}", this, true);
                PrintMsgL(userid, "TPTargetInsideBlock");
                return "TPTargetInsideBlock";
            }
            return null;
        }

        private static List<T> FindEntitiesOfType<T>(Vector3 a, float n, int m = -1) where T : BaseNetworkable
        {
            int hits = Physics.OverlapSphereNonAlloc(a, n, Vis.colBuffer, m, QueryTriggerInteraction.Collide);
            List<T> entities = new List<T>();
            for (int i = 0; i < hits; i++)
            {
                var entity = Vis.colBuffer[i]?.ToBaseEntity();
                if (entity is T) entities.Add(entity as T);
                Vis.colBuffer[i] = null;
            }
            return entities;
        }

        private string CheckItems(BasePlayer player)
        {
            foreach (var blockedItem in ReverseBlockedItems)
            {
                if (player.inventory.FindItemID(blockedItem.Key) != null)
                {
                    return blockedItem.Value;
                }
            }
            return null;
        }

        private string CheckFoundation(ulong userid, Vector3 position)
        {
            if (CanBypassRestrictions(userid.ToString())) return null;
            string insideErr = CheckInsideEntity(position, userid);
            if (insideErr != null) 
            {
                return insideErr;                    
            }
            if (permission.UserHasPermission(userid.ToString(), PermFoundationCheck))
            {
                return null;
            }
            if (!config.Home.ForceOnTopOfFoundation) return null; // Foundation/floor not required
            if (UnderneathFoundation(position))
            {
                return "HomeFoundationUnderneathFoundation";
            }

            var entities = new List<BuildingBlock>();
            if (config.Home.AllowAboveFoundation) // Can set on a foundation or floor
            {
#if DEBUG
                Puts($"CheckFoundation() looking for foundation or floor at {position}");
#endif
                entities = GetFoundationOrFloor(position);
            }
            else // Can only use foundation, not floor/ceiling
            {
#if DEBUG
                Puts($"CheckFoundation() looking for foundation at {position}");
#endif
                entities = GetFoundation(position);
            }

            entities.RemoveAll(x => !x.IsValid() || x.IsDestroyed);
            if (entities.Count == 0) return "HomeNoFoundation";

            if (!config.Home.CheckFoundationForOwner) return null;
            for (var i = 0; i < entities.Count; i++)
            {
                if (IsFriend(userid, entities[i].OwnerID)) return null;
            }

            return "HomeFoundationNotFriendsOwned";
        }

        private BuildingBlock GetFoundationOwned(Vector3 position, ulong userID)
        {
#if DEBUG
            Puts("GetFoundationOwned() called...");
#endif
            var entities = GetFoundation(position);
            if (entities.Count == 0) return null;
            if (!config.Home.CheckFoundationForOwner) return entities[0];

            for (var i = 0; i < entities.Count; i++)
            {
                if (IsFriend(userID, entities[i].OwnerID)) return entities[i];
            }
            return null;
        }

        // Borrowed/modified from PreventLooting and Rewards
        // playerid = active player, ownerid = owner of building block, who may be offline
        bool IsFriend(ulong playerid, ulong ownerid)
        {
            if (playerid == ownerid) return true;
            if (config.Home.UseFriends && Friends != null && Friends.IsLoaded)
            {
#if DEBUG
                Puts("Checking Friends...");
#endif
                var fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if (fr != null && fr is bool && (bool)fr)
                {
#if DEBUG
                    Puts("  IsFriend: true based on Friends plugin");
#endif
                    return true;
                }
            }
            if (config.Home.UseClans && Clans != null && Clans.IsLoaded)
            {
#if DEBUG
                Puts("Checking Clans...");
#endif
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan = (string)Clans?.CallHook("GetClanOf", ownerid);
                if (playerclan != null && ownerclan != null && playerclan == ownerclan)
                {
#if DEBUG
                    Puts("  IsFriend: true based on Clans plugin");
#endif
                    return true;
                }
            }
            if (config.Home.UseTeams)
            {
#if DEBUG
                Puts("Checking Rust teams...");
#endif
                RelationshipManager.PlayerTeam playerTeam;
                if (RelationshipManager.ServerInstance.playerToTeam.TryGetValue(playerid, out playerTeam))
                {
                    if (playerTeam.members.Contains(ownerid))
                    {
#if DEBUG
                        Puts("  IsFriend: true based on Rust teams");
#endif
                        return true;
                    }
                }
            }
            return false;
        }

        // Check that we are near the middle of a block.  Also check for high wall overlap
        private bool ValidBlock(BaseEntity entity, Vector3 position)
        {
            if (!config.Settings.StrictFoundationCheck)
            {
                return true;
            }
#if DEBUG
            Puts($"ValidBlock() called for {entity.ShortPrefabName}");
#endif
            Vector3 center = entity.CenterPoint();

            List<BaseEntity> ents = new List<BaseEntity>();
            Vis.Entities<BaseEntity>(center, 1.5f, ents);
            foreach (BaseEntity wall in ents)
            {
                if (wall.name.Contains("external.high"))
                {
#if DEBUG
                    Puts($"    Found: {wall.name} @ center {center}, pos {position}");
#endif
                    return false;
                }
            }
#if DEBUG
            Puts($"  Checking block: {entity.name} @ center {center}, pos: {position}");
#endif
            if (entity.PrefabName.Contains("triangle.prefab"))
            {
                if (Math.Abs(center.x - position.x) < 0.5f && Math.Abs(center.z - position.z) < 0.5f)
                {
#if DEBUG
                    Puts($"    Found: {entity.ShortPrefabName} @ center: {center}, pos: {position}");
#endif
                    return true;
                }
            }
            else if (entity.PrefabName.Contains("foundation.prefab") || entity.PrefabName.Contains("floor.prefab"))
            {
                if (Math.Abs(center.x - position.x) < 0.7f && Math.Abs(center.z - position.z) < 0.7f)
                {
#if DEBUG
                    Puts($"    Found: {entity.ShortPrefabName} @ center: {center}, pos: {position}");
#endif
                    return true;
                }
            }

            return false;
        }

        private List<BuildingBlock> GetFoundation(Vector3 position)
        {
            RaycastHit hit;
            var entities = new List<BuildingBlock>();

            if (Physics.Raycast(position + new Vector3(0f, 0.2f, 0f), Vector3.down, out hit, 3f, Layers.Mask.Construction))
            {
                var entity = hit.GetEntity();

                if (entity.IsValid())
                {
                    if (entity.PrefabName.Contains("foundation")) // || position.y < entity.WorldSpaceBounds().ToBounds().max.y)
                    {
                        if (ValidBlock(entity, position))
                        {
#if DEBUG
                            Puts($"  GetFoundation() found {entity.PrefabName} at {entity.transform.position}");
#endif
                            entities.Add(entity as BuildingBlock);
                        }
                    }
                }
            }
#if DEBUG            
            if (entities.Count == 0)
            {
                Puts("  GetFoundation() none found.");
            }
#endif
            return entities;
        }

        private List<BuildingBlock> GetFloor(Vector3 position)
        {
            RaycastHit hitinfo;
            var entities = new List<BuildingBlock>();

            if (Physics.Raycast(position, Vector3.down, out hitinfo, 3f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore) && hitinfo.GetEntity().IsValid())
            {
                var entity = hitinfo.GetEntity();

                if (entity.IsValid() && entity.PrefabName.Contains("floor"))
                {
#if DEBUG
                    Puts($"  GetFloor() found {entity.PrefabName} at {entity.transform.position}");
#endif
                    entities.Add(entity as BuildingBlock);
                }
            }
            else
            {
#if DEBUG
                Puts("  GetFloor() none found.");
#endif
            }

            return entities;
        }

        private List<BuildingBlock> GetFoundationOrFloor(Vector3 position)
        {
            RaycastHit hitinfo;
            var entities = new List<BuildingBlock>();

            if (Physics.Raycast(position + new Vector3(0f, 0.2f, 0f), Vector3.down, out hitinfo, 3f, Layers.Mask.Construction) && hitinfo.GetEntity().IsValid())
            {
                var entity = hitinfo.GetEntity();
                if (entity.PrefabName.Contains("floor") || entity.PrefabName.Contains("foundation"))// || position.y < entity.WorldSpaceBounds().ToBounds().max.y))
                {
#if DEBUG
                    Puts($"  GetFoundationOrFloor() found {entity.PrefabName} at {entity.transform.position}");
#endif
                    if (ValidBlock(entity, position))
                    {
                        entities.Add(entity as BuildingBlock);
                    }
                }
            }
            else
            {
#if DEBUG
                Puts("  GetFoundationOrFloor() none found.");
#endif
            }

            return entities;
        }

        private bool CheckBoundaries(float x, float y, float z)
        {
            return x <= boundary && x >= -boundary && y <= 2000 && y >= -100 && z <= boundary && z >= -boundary;
        }

        private Vector3 GetGround(Vector3 sourcePos)
        {
            if (!config.Home.AllowAboveFoundation) return sourcePos;
            var newPos = sourcePos;
            newPos.y = TerrainMeta.HeightMap.GetHeight(newPos);
            sourcePos.y += .5f;
            RaycastHit hitinfo;
            var done = false;

#if DEBUG
            Puts("GetGround(): Looking for iceberg or cave");
#endif
            //if (Physics.SphereCast(sourcePos, .1f, Vector3.down, out hitinfo, 250, groundLayer))
            if (Physics.Raycast(sourcePos, Vector3.down, out hitinfo, 250f, Layers.Mask.Terrain | Layers.Mask.World))
            {
                if ((config.Home.AllowIceberg && hitinfo.collider.name.Contains("iceberg")) || (config.Home.AllowCave && hitinfo.collider.name.Contains("cave_")))
                {
#if DEBUG
                    Puts("GetGround():   found iceberg or cave");
#endif
                    sourcePos.y = hitinfo.point.y;
                    done = true;
                }
                else
                {
                    var mesh = hitinfo.collider.GetComponentInChildren<MeshCollider>();
                    if (mesh != null && mesh.sharedMesh.name.Contains("rock_"))
                    {
                        sourcePos.y = hitinfo.point.y;
                        done = true;
                    }
                }
            }
#if DEBUG
            Puts("GetGround(): Looking for cave or rock");
#endif
            //if (!_config.Home.AllowCave && Physics.SphereCast(sourcePos, .1f, up, out hitinfo, 250, Layers.Mask.Terrain | Layers.Mask.World) && hitinfo.collider.name.Contains("rock_"))
            if (!config.Home.AllowCave && Physics.Raycast(sourcePos, Vector3.up, out hitinfo, 250f, Layers.Mask.Terrain | Layers.Mask.World) && hitinfo.collider.name.Contains("rock_"))
            {
#if DEBUG
                Puts("GetGround():   found cave or rock");
#endif
                sourcePos.y = newPos.y - 10;
                done = true;
            }
            return done ? sourcePos : newPos;
        }

        private bool GetLift(Vector3 position)
        {
            List<ProceduralLift> nearObjectsOfType = new List<ProceduralLift>();
            Vis.Entities<ProceduralLift>(position, 0.5f, nearObjectsOfType);
            if (nearObjectsOfType.Count > 0)
            {
                return true;
            }
            return false;
        }

        private bool IsOnJunkPile(BasePlayer player)
        {
            if (player.GetParentEntity() is JunkPile)
            {
                return true;
            }

            RaycastHit hit;
            if (Physics.Raycast(player.transform.position + new Vector3(0f, 0.5f, 0f), Vector3.down, out hit, 3f, Layers.Mask.World, QueryTriggerInteraction.Ignore))
            {
                return hit.GetEntity() is JunkPile;
            }

            return false;
        }

        private Vector3 GetGroundBuilding(Vector3 sourcePos)
        {
            sourcePos.y = TerrainMeta.HeightMap.GetHeight(sourcePos);
            RaycastHit hitinfo;
            if (Physics.Raycast(sourcePos, Vector3.down, out hitinfo, Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Construction | Layers.Mask.Deployed))
            {
                sourcePos.y = Mathf.Max(hitinfo.point.y, sourcePos.y);
                return sourcePos;
            }
            if (Physics.Raycast(sourcePos, Vector3.up, out hitinfo, Layers.Mask.Terrain | Layers.Mask.World | Layers.Mask.Construction | Layers.Mask.Deployed))
                sourcePos.y = Mathf.Max(hitinfo.point.y, sourcePos.y);
            return sourcePos;
        }

        private bool UnderneathFoundation(Vector3 position)
        {
            RaycastHit hit;
            if (Physics.Raycast(position + new Vector3(0f, 3f, 0f), Vector3.down, out hit, 5f, Layers.Mask.Construction, QueryTriggerInteraction.Ignore))
            {
                var block = hit.GetEntity() as BuildingBlock;

                if (block.IsValid() && (block.prefabID == 72949757 || block.prefabID == 3234260181))
                {
                    return hit.point.y > position.y;
                }
            }
            return false;
        }

        private bool IsAllowed(BasePlayer player, string perm = null)
        {
            var playerAuthLevel = player.net?.connection?.authLevel;

            int requiredAuthLevel = 3;
            if (config.Admin.UseableByModerators)
            {
                requiredAuthLevel = 1;
            }
            else if (config.Admin.UseableByAdmins)
            {
                requiredAuthLevel = 2;
            }
            if (playerAuthLevel >= requiredAuthLevel) return true;

            return !string.IsNullOrEmpty(perm) && permission.UserHasPermission(player.UserIDString, perm);
        }

        private bool IsAllowedMsg(BasePlayer player, string perm = null)
        {
            if (IsAllowed(player, perm)) return true;
            PrintMsgL(player, "NotAllowed");
            return false;
        }

        private Effect reusableSoundEffectInstance = new Effect();

        private void SendEffect(BasePlayer player, string sound = null)
        {
            if (config.Settings.PlaySounds && config.Settings.PrefabSounds.Count != 0)
            {
                if (string.IsNullOrEmpty(sound)) sound = config.Settings.PrefabSounds.GetRandom();

                reusableSoundEffectInstance.Init(Effect.Type.Generic, player, 0, Vector3.zero, Vector3.forward);
                reusableSoundEffectInstance.pooledString = sound;

                EffectNetwork.Send(reusableSoundEffectInstance, player.Connection);
            }
        }

        private int GetHigher(BasePlayer player, Dictionary<string, int> limits, int limit, bool unlimited)
        {
            if (unlimited && limit == 0) return limit;

            foreach (var l in limits)
            {
                if (permission.UserHasPermission(player.UserIDString, l.Key))
                {
                    if (unlimited && l.Value == 0) return l.Value;

                    limit = Math.Max(l.Value, limit);
                }
            }
            return limit;
        }

        private int GetLower(BasePlayer player, Dictionary<string, int> times, int time)
        {
            foreach (var l in times)
            {
                if (permission.UserHasPermission(player.UserIDString, l.Key))
                {
                    time = Math.Min(l.Value, time);
                }
            }
            return time;
        }

        private void CheckPerms(Dictionary<string, int> limits)
        {
            foreach (var limit in limits)
            {
                if (!permission.PermissionExists(limit.Key))
                {
                    permission.RegisterPermission(limit.Key, this);
                }
            }
        }
        #endregion

        #region Message
        private string _(string msgId, BasePlayer player, params object[] args)
        {
            var msg = lang.GetMessage(msgId, this, player?.UserIDString);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        private void PrintMsgL(IPlayer user, string msgId, params object[] args)
        {
            if (user.IsServer)
            {
                user.Reply(string.Format(lang.GetMessage(msgId, this, user.Id), args));
            }
            else PrintMsgL(user.Object as BasePlayer, msgId, args);
        }

        private void PrintMsgL(BasePlayer player, string msgId, params object[] args)
        {
            if (player == null) return;
            PrintMsg(player, _(msgId, player, args));
        }

        private void PrintMsgL(ulong userid, string msgId, params object[] args)
        {
            var player = BasePlayer.FindAwakeOrSleeping(userid.ToString());
            if (player == null) return;
            PrintMsgL(player, msgId, args);
        }

        private void PrintMsg(BasePlayer player, string message)
        {
            if (player == null || string.IsNullOrEmpty(message)) return;
            if (config.Settings.UsePopup)
            {
                PopupNotifications?.Call("CreatePopupNotification", config.Settings.ChatName + message, player);
            }
            else Player.Message(player, $"{config.Settings.ChatName}{message}", config.Settings.ChatID);
        }

        #endregion

        #region DrawBox
        private static void DrawBox(BasePlayer player, Vector3 center, Quaternion rotation, Vector3 size)
        {
            size /= 2;
            var point1 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y + size.y, center.z + size.z), center, rotation);
            var point2 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y - size.y, center.z + size.z), center, rotation);
            var point3 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y + size.y, center.z - size.z), center, rotation);
            var point4 = RotatePointAroundPivot(new Vector3(center.x + size.x, center.y - size.y, center.z - size.z), center, rotation);
            var point5 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y + size.y, center.z + size.z), center, rotation);
            var point6 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y - size.y, center.z + size.z), center, rotation);
            var point7 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y + size.y, center.z - size.z), center, rotation);
            var point8 = RotatePointAroundPivot(new Vector3(center.x - size.x, center.y - size.y, center.z - size.z), center, rotation);

            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point1, point2);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point1, point3);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point1, point5);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point4, point2);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point4, point3);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point4, point8);

            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point5, point6);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point5, point7);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point6, point2);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point8, point6);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point8, point7);
            player.SendConsoleCommand("ddraw.line", 30f, Color.blue, point7, point3);
        }

        private static Vector3 RotatePointAroundPivot(Vector3 point, Vector3 pivot, Quaternion rotation)
        {
            return rotation * (point - pivot) + pivot;
        }
        #endregion

        #region FindPlayer
        private ulong FindPlayersSingleId(string nameOrIdOrIp, BasePlayer player)
        {
            var targets = FindPlayers(nameOrIdOrIp, true);
            if (targets.Count > 1)
            {
                PrintMsgL(player, "MultiplePlayers", GetMultiplePlayers(targets));
                return 0;
            }
            ulong userId;
            if (targets.Count <= 0)
            {
                if (ulong.TryParse(nameOrIdOrIp, out userId)) return userId;
                PrintMsgL(player, "PlayerNotFound");
                return 0;
            }
            else
                userId = targets.First().userID;

            return userId;
        }

        private BasePlayer FindPlayersSingle(string value, BasePlayer player)
        {
            if (string.IsNullOrEmpty(value)) return null;
            BasePlayer target;
            if (_ids.TryGetValue(value, out target) && target.IsValid())
            {
                return target;
            }
            var targets = FindPlayers(value, true);
            if (targets.Count <= 0)
            {
                PrintMsgL(player, "PlayerNotFound");
                return null;
            }
            if (targets.Count > 1)
            {
                PrintMsgL(player, "MultiplePlayers", GetMultiplePlayers(targets));
                return null;
            }

            return targets.First();
        }

        private List<BasePlayer> FindPlayers(string arg, bool all = false)
        {
            var players = new List<BasePlayer>();

            if (string.IsNullOrEmpty(arg))
            {
                return players;
            }

            BasePlayer target;
            if (_ids.TryGetValue(arg, out target) && target.IsValid())
            {
                if (all || target.IsConnected)
                {
                    players.Add(target);
                    return players;
                }
            }

            foreach (var user in all ? BasePlayer.allPlayerList : BasePlayer.activePlayerList)
            {
                if (user == null || string.IsNullOrEmpty(user.displayName) || players.Contains(user))
                {
                    continue;
                }

                if (user.UserIDString == arg || user.displayName.Contains(arg, CompareOptions.OrdinalIgnoreCase))
                {
                    players.Add(user);
                }
            }

            return players;
        }
        #endregion
                
        private class UnityVector3Converter : JsonConverter
        {
            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var vector = (Vector3)value;
                writer.WriteValue($"{vector.x} {vector.y} {vector.z}");
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.String)
                {
                    var values = reader.Value.ToString().Trim().Split(' ');
                    return new Vector3(Convert.ToSingle(values[0]), Convert.ToSingle(values[1]), Convert.ToSingle(values[2]));
                }
                var o = JObject.Load(reader);
                return new Vector3(Convert.ToSingle(o["x"]), Convert.ToSingle(o["y"]), Convert.ToSingle(o["z"]));
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(Vector3);
            }
        }

        private class CustomComparerDictionaryCreationConverter<T> : CustomCreationConverter<IDictionary>
        {
            private readonly IEqualityComparer<T> comparer;

            public CustomComparerDictionaryCreationConverter(IEqualityComparer<T> comparer)
            {
                if (comparer == null)
                    throw new ArgumentNullException(nameof(comparer));
                this.comparer = comparer;
            }

            public override bool CanConvert(Type objectType)
            {
                return HasCompatibleInterface(objectType) && HasCompatibleConstructor(objectType);
            }

            private static bool HasCompatibleInterface(Type objectType)
            {
                return objectType.GetInterfaces().Where(i => HasGenericTypeDefinition(i, typeof(IDictionary<,>))).Any(i => typeof(T).IsAssignableFrom(i.GetGenericArguments().First()));
            }

            private static bool HasGenericTypeDefinition(Type objectType, Type typeDefinition)
            {
                return objectType.GetTypeInfo().IsGenericType && objectType.GetGenericTypeDefinition() == typeDefinition;
            }

            private static bool HasCompatibleConstructor(Type objectType)
            {
                return objectType.GetConstructor(new[] { typeof(IEqualityComparer<T>) }) != null;
            }

            public override IDictionary Create(Type objectType)
            {
                return Activator.CreateInstance(objectType, comparer) as IDictionary;
            }
        }

        public class Exploits
        {
            public static bool TestFoundation(Vector3 a)
            {
                Physics.queriesHitBackfaces = true;

                bool flag = IsFoundationUpwards(a);

                Physics.queriesHitBackfaces = false;

                return flag;
            }

            private static bool IsFoundationUpwards(Vector3 point)
            {
                RaycastHit hit;
                if (!Physics.Raycast(point, Vector3.up, out hit, 3f, Layers.Construction)) return false;
                return hit.collider.name.Contains("foundation");
            }

            public static bool TestRock(Vector3 a)
            {
                Physics.queriesHitBackfaces = true;

                bool flag = IsRockFaceUpwards(a);

                Physics.queriesHitBackfaces = false;

                return flag || IsRockFaceDownwards(a);
            }

            private static bool IsRockFaceDownwards(Vector3 a)
            {
                Vector3 b = a + new Vector3(0f, 20f, 0f);
                Vector3 d = a - b;
                var hits = Physics.RaycastAll(b, d, d.magnitude, Layers.World);
                return Array.Exists(hits, hit => IsRock(hit.collider.name));
            }

            private static bool IsRockFaceUpwards(Vector3 point)
            {
                RaycastHit hit;
                if (!Physics.Raycast(point, Vector3.up, out hit, 20f, Layers.World)) return false;
                return IsRock(hit.collider.gameObject.name);
            }

            private static bool IsRock(string name) => name.Contains("rock", CompareOptions.OrdinalIgnoreCase) || name.Contains("formation", CompareOptions.OrdinalIgnoreCase) || name.Contains("cliff", CompareOptions.OrdinalIgnoreCase);
        }

        [HookMethod("SendHelpText")]
        private void SendHelpText(BasePlayer player)
        {
            PrintMsgL(player, "<size=14>NTeleportation</size> by <color=#ce422b>Nogrod</color>\n<color=#ffd479>/sethome NAME</color> - Set home on current foundation\n<color=#ffd479>/home NAME</color> - Go to one of your homes\n<color=#ffd479>/home list</color> - List your homes\n<color=#ffd479>/town</color> - Go to town, if set\n/tpb - Go back to previous location\n/tpr PLAYER - Request teleport to PLAYER\n/tpa - Accept teleport request");
        }

        private bool API_HavePendingRequest(BasePlayer player)
        {
            return PendingRequests.ContainsKey(player.userID) || PlayersRequests.ContainsKey(player.userID) || TeleportTimers.ContainsKey(player.userID);
        }

        private bool API_HaveAvailableHomes(BasePlayer player)
        {
            HomeData homeData;
            if (!_Home.TryGetValue(player.userID, out homeData))
            {
                _Home[player.userID] = homeData = new HomeData();
            }

            ValidateHomes(player, homeData, false);

            var limit = GetHigher(player, config.Home.VIPHomesLimits, config.Home.HomesLimit, true);

            if (limit == 0) return true;

            return homeData.Locations.Count < limit;
        }

        private Dictionary<string, Vector3> API_GetHomes(BasePlayer player)
        {
            HomeData homeData;
            if (!_Home.TryGetValue(player.userID, out homeData))
            {
                _Home[player.userID] = homeData = new HomeData();
            }

            ValidateHomes(player, homeData, false);

            return homeData.Locations;
        }

        private List<Vector3> API_GetLocations(string command)
        {
            var settings = GetSettings(command);

            if (settings == null)
            {
                return new List<Vector3>();
            }

            return settings.Locations;
        }

        private Dictionary<string, List<Vector3>> API_GetAllLocations()
        {
            var dict = new Dictionary<string, List<Vector3>>();

            foreach (var dc in config.DynamicCommands)
            {
                dict[dc.Key] = dc.Value.Locations;
            }

            return dict;
        }
        
        private int GetLimitRemaining(BasePlayer player, string type)
        {
            if (player == null || string.IsNullOrEmpty(type)) return -1;
            var settings = GetSettings(type, player.userID);
            if (settings == null) return -1;
            var currentDate = DateTime.Now.ToString("d");
            var limit = GetHigher(player, settings.VIPDailyLimits, settings.DailyLimit, true);
            TeleportData data;
            if (!settings.Teleports.TPData.TryGetValue(player.userID, out data))
            {
                settings.Teleports.TPData[player.userID] = data = new TeleportData();
            }
            if (data.Date != currentDate)
            {
                data.Amount = 0;
                data.Date = currentDate;
            }            
            if (limit > 0)
            {
                return limit - data.Amount;
            }
            return 0;
        }
        
        private int GetCooldownRemaining(BasePlayer player, string type)
        {
            if (player == null || string.IsNullOrEmpty(type)) return -1;
            var settings = GetSettings(type, player.userID);
            if (settings == null) return -1;
            var currentDate = DateTime.Now.ToString("d");
            var timestamp = Facepunch.Math.Epoch.Current;
            var cooldown = GetLower(player, settings.VIPCooldowns, settings.Cooldown);
            TeleportData data;
            if (!settings.Teleports.TPData.TryGetValue(player.userID, out data))
            {
                settings.Teleports.TPData[player.userID] = data = new TeleportData();
            }
            if (data.Date != currentDate)
            {
                data.Amount = 0;
                data.Date = currentDate;
            }
            if (cooldown > 0 && timestamp - data.Timestamp < cooldown)
            {
                return cooldown - (timestamp - data.Timestamp);
            }
            return 0;
        }

        private int GetCountdownRemaining(BasePlayer player, string type)
        {
            if (player == null || string.IsNullOrEmpty(type))
            {
                return -1;
            }

            TownSettings settings = GetSettings(type, player.userID);
            if (settings == null)
            {
                return -1;
            }

            return GetLower(player, settings.VIPCountdowns, settings.Countdown);
        }
    }
}