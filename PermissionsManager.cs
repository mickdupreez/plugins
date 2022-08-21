using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic; 
using UnityEngine;
using Oxide.Core.Plugins;
using Oxide.Core.Libraries;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Database;

namespace Oxide.Plugins 
{
    //  Ordered groups alphabetically.
    //  Added local data file backup + recall.
    //  Added SQL backup + recall. 
    //  Option to automatically open groups instead of players for /perms.
    //  Added purge option for perms from unloaded plugins.
    //  Added auto save/load, for persistence after wipe.
    //  Fixd / and \ causing issues in displaynames

    // To do
    // Change button colour of permission to indicate that player/group has at least one perm from that plugin. 


    [Info("PermissionsManager", "Steenamaroo", "2.0.6", ResourceId = 3)]
    class PermissionsManager : RustPlugin
    {
        #region Declarations
        List<string> PlugList = new List<string>();
        Dictionary<int, string> numberedPerms = new Dictionary<int, string>();
        List<ulong> MenuOpen = new List<ulong>();

        bool HasPermission(string id, string perm) => permission.UserHasPermission(id, perm);
        const string permAllowed = "permissionsmanager.allowed";

        Dictionary<ulong, Info> ActiveAdmins = new Dictionary<ulong, Info>();

        public class Info
        {
            public string inheritedcheck = "";
            public int noOfPlugs;
            public int pluginPage = 1;
            public int PPage = 1;
            public int GPage = 1;
            public string subjectGroup;
            public BasePlayer subject;
        }

        string ButtonColour1 = "0.7 0.32 0.17 1";
        string ButtonColour2 = "0.2 0.2 0.2 1";

        #endregion 

        #region Hooks
        void OnPluginLoaded(Plugin plugin)
        {
            if (plugin is PermissionsManager)
                return;
            Wipe();
            OnServerInitialized();
        }

        void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin is PermissionsManager)
                return;
            Wipe();
            OnServerInitialized();
        }

        void Init()
        {
            lang.RegisterMessages(messages, this);
            permission.RegisterPermission(permAllowed, this);
        }

        void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList.Where(player => MenuOpen.Contains(player.userID)))
                DestroyMenu(player, true);
            if (!LoadConfigVariables())
            {
                Puts("Config file issue detected. Please delete file, or check syntax and fix.");
                Interface.Oxide.UnloadPlugin(Name);
                return;
            }
            if (Sql_conn == null)
                Sql_conn = Sql.OpenDb(config.MySQL.sql_host, config.MySQL.sql_port, config.MySQL.sql_db, config.MySQL.sql_user, config.MySQL.sql_pass + ";Connection Timeout = 10; CharSet=utf8mb4", this);

            if (!config.Keep_Perms_After_Wipe)
            {
                Unsubscribe(nameof(OnServerSave));
                Unsubscribe(nameof(OnNewSave));
            }
        }

        void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList.Where(player => MenuOpen.Contains(player.userID)))
                DestroyMenu(player, true);
            SaveData();
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            if (MenuOpen.Contains(player.userID))
                DestroyMenu(player, true);
        }

        void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (MenuOpen.Contains(player.userID))
                DestroyMenu(player, true);
        }
        #endregion

        #region Methods
        void DestroyMenu(BasePlayer player, bool all)
        {
            CuiHelper.DestroyUi(player, "PMMainUI");
            CuiHelper.DestroyUi(player, "PMPermsUI");
            CuiHelper.DestroyUi(player, "PMConfirmUI");
            CuiHelper.DestroyUi(player, "PMDataUI");
            CuiHelper.DestroyUi(player, "PMDataConfirm");
            CuiHelper.DestroyUi(player, "PMMsgUI");

            if (all)
            {
                CuiHelper.DestroyUi(player, "PMBgUI");
                MenuOpen.Remove(player.userID);
            }
        }

        void Wipe()
        {
            PlugList.Clear();
            numberedPerms.Clear();
        }

        void GetPlugs(BasePlayer player)
        {
            var path = ActiveAdmins[player.userID];
            PlugList.Clear();
            path.noOfPlugs = 0;
            foreach (var entry in plugins.GetAll())
            {
                if (entry.IsCorePlugin)
                    continue;

                var str = entry.ToString();
                var charsToRemove = new string[] { "Oxide.Plugins." };

                foreach (var c in charsToRemove)
                    str = str.Replace(c, string.Empty).ToLower();

                foreach (var perm in permission.GetPermissions().ToList().Where(perm => perm.Contains($"{str}") && !(config.BlockList.Split(',').ToList().Contains($"{str}"))))
                    if (!PlugList.Contains(str))
                        PlugList.Add(str);
            }
            PlugList.Sort();
        }

        bool IsAuth(BasePlayer player) => player?.net?.connection != null && player.net.connection.authLevel == 2;

        void SetButtons(bool on)
        {
            ButtonColour1 = (on) ? config.OffColour : config.OnColour;
            ButtonColour2 = (on) ? config.OnColour : config.OffColour;
        }

        object[] PermsCheck(BasePlayer player, string group, string info)
        {
            bool has = false;
            List<string> inherited = new List<string>();
            var path = ActiveAdmins[player.userID];
            if (group == "true")
            {
                if (permission.GroupHasPermission(path.subjectGroup, info))
                    has = true;
            }
            else
            {
                UserData userData = permission.GetUserData(path.subject.UserIDString);
                if (userData.Perms.Contains(info))
                    has = true;
                foreach (var group1 in permission.GetUserGroups(path.subject.UserIDString))
                    if (permission.GroupHasPermission(group1, info))
                        inherited.Add(group1);
            }
            return new object[] { has, inherited };
        }
        #endregion

        #region UI
        void PMBgUI(BasePlayer player)
        {
            MenuOpen.Add(player.userID);
            string guiString = String.Format("0.1 0.1 0.1 {0}", config.guitransparency);
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = guiString }, RectTransform = { AnchorMin = "0.3 0.1", AnchorMax = "0.7 0.9" }, CursorEnabled = true, FadeOut = 0.1f }, "Overlay", "PMBgUI");
            elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0.95", AnchorMax = $"0.999 1" }, CursorEnabled = true }, mainName);
            elements.Add(new CuiPanel { Image = { Color = $"0 0 0 1" }, RectTransform = { AnchorMin = $"0 0", AnchorMax = $"0.999 0.05" }, CursorEnabled = true }, mainName);
            elements.Add(new CuiButton { Button = { Command = "ClosePM", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.955 0.96", AnchorMax = "0.99 0.995" }, Text = { Text = "X", FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            CuiHelper.AddUi(player, elements);
        }

        void PMMainUI(BasePlayer player, bool group, int page)
        {
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "PMMainUI");
            string subject = group ? lang.GetMessage("GUIPlayers", this) : lang.GetMessage("GUIGroups", this);
            string current = !group ? lang.GetMessage("GUIPlayers", this) : lang.GetMessage("GUIGroups", this);

            elements.Add(new CuiLabel { Text = { Text = "Permissions Manager V2", FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMTogglePlayerGroup {group} 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.6 0.04" }, Text = { Text = lang.GetMessage("All", this) + " " + subject, FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);

            int pos1 = 20 - (page * 20), quantity = 0;
            float top = 0.87f;
            float bottom = 0.85f;

            elements.Add(new CuiLabel { Text = { Text = lang.GetMessage("All", this) + " " + current, FontSize = 14, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 0.97" } }, mainName);

            if (group)
            {
                foreach (var grp in permission.GetGroups().OrderBy(x => x))
                {
                    pos1++;
                    quantity++;
                    if (pos1 > 0 && pos1 < 21)
                    {
                        elements.Add(new CuiButton { Button = { Command = $"PMSelected group {grp}", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 {bottom}", AnchorMax = $"0.7 {top}" }, Text = { Text = $"{grp}", FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
                        top = top - 0.025f;
                        bottom = bottom - 0.025f;
                    }
                }
            }
            else
            {
                foreach (var plyr in BasePlayer.allPlayerList)
                {
                    pos1++;
                    quantity++;
                    if (pos1 > 0 && pos1 < 21)
                    {
                        elements.Add(new CuiButton { Button = { Command = $"PMSelected player {plyr.userID}", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 {bottom}", AnchorMax = $"0.7 {top}" }, Text = { Text = StripSlashes(plyr.displayName), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
                        top = top - 0.025f;
                        bottom = bottom - 0.025f;
                    }
                }
            }

            if (quantity > (page * 20))
                elements.Add(new CuiButton { Button = { Command = $"PMTogglePlayerGroup {!group} {page + 1}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.9 0.04" }, Text = { Text = lang.GetMessage("->", this), FontSize = 11, Align = TextAnchor.MiddleCenter }, }, mainName);

            if (page > 1)
                elements.Add(new CuiButton { Button = { Command = $"PMTogglePlayerGroup {!group} {page - 1}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.2 0.04" }, Text = { Text = lang.GetMessage("<-", this), FontSize = 11, Align = TextAnchor.MiddleCenter }, }, mainName);

            CuiHelper.AddUi(player, elements);
        }

        void PMDataUI(BasePlayer player)
        {
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "PMDataUI");
            elements.Add(new CuiLabel { Text = { Text = "Permissions Manager - Data Menu", FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, mainName);

            elements.Add(new CuiButton { Button = { Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0.1 0.85", AnchorMax = $"0.898 0.88" }, Text = { Text = String.Empty } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = "Local data backup.", FontSize = 14, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.85", AnchorMax = $"0.898 0.88" }, }, mainName);

            elements.Add(new CuiLabel { Text = { Text = "Save", FontSize = 13, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.81", AnchorMax = "0.3 0.835" } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 0", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.81", AnchorMax = $"0.6 0.835" }, Text = { Text = $"Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.7 0.81", AnchorMax = $"0.9 0.835" }, Text = { Text = $"Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);

            elements.Add(new CuiLabel { Text = { Text = "Load", FontSize = 13, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.78", AnchorMax = "0.3 0.805" } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 2", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.78", AnchorMax = $"0.6 0.805" }, Text = { Text = $"Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 3", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.7 0.78", AnchorMax = $"0.9 0.805" }, Text = { Text = $"Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 4", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.74", AnchorMax = $"0.9 0.765" }, Text = { Text = $"Wipe local data backup", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);

            if (config.MySQL.useMySQL)
            { 
                elements.Add(new CuiButton { Button = { Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0.1 0.65", AnchorMax = $"0.898 0.68" }, Text = { Text = String.Empty } }, mainName);
                elements.Add(new CuiLabel { Text = { Text = "SQL table backup.", FontSize = 14, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.65", AnchorMax = $"0.898 0.68" }, }, mainName);

                elements.Add(new CuiLabel { Text = { Text = "Save", FontSize = 13, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.61", AnchorMax = "0.3 0.635" } }, mainName);
                elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 5", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.61", AnchorMax = $"0.6 0.635" }, Text = { Text = $"Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 6", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.7 0.61", AnchorMax = $"0.9 0.635" }, Text = { Text = $"Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);

                elements.Add(new CuiLabel { Text = { Text = "Load", FontSize = 13, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.58", AnchorMax = "0.3 0.605" } }, mainName);
                elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 7", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.58", AnchorMax = $"0.6 0.605" }, Text = { Text = $"Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 8", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.7 0.58", AnchorMax = $"0.9 0.605" }, Text = { Text = $"Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 9", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 0.54", AnchorMax = $"0.9 0.565" }, Text = { Text = $"Wipe SQL backup", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            }

            elements.Add(new CuiButton { Button = { Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = $"0.898 0.43" }, Text = { Text = String.Empty } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = "Purge Unloaded Permissions for...", FontSize = 14, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.4", AnchorMax = $"0.898 0.43" }, }, mainName);

            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 13", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.36", AnchorMax = $"0.7 0.39" }, Text = { Text = $"Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 14", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.325", AnchorMax = $"0.7 0.355" }, Text = { Text = $"Players", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);

            elements.Add(new CuiButton { Button = { Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0.1 0.17", AnchorMax = $"0.898 0.20" }, Text = { Text = String.Empty } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = "Server Permissions.", FontSize = 13, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.1 0.17", AnchorMax = $"0.898 0.20" }, }, mainName);

            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 10", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.13", AnchorMax = $"0.7 0.16" }, Text = { Text = $"Empty All Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 11", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.095", AnchorMax = $"0.7 0.125" }, Text = { Text = $"Delete All Groups", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PMDataConfirm 12", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.3 0.06", AnchorMax = $"0.7 0.09" }, Text = { Text = $"Wipe All Player Permissions", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);


            CuiHelper.AddUi(player, elements);
        }

        void MsgUI(BasePlayer player, string message)
        {
            CuiHelper.DestroyUi(player, "PMMsgUI");
            timer.Once(1.5f, () =>
            {
                if (player != null)
                    CuiHelper.DestroyUi(player, "PMMsgUI");
            });
            var elements = new CuiElementContainer();
            var first = elements.Add(new CuiPanel { Image = { FadeIn = 0.3f, Color = $"0.1 0.1 0.1 0.95" }, RectTransform = { AnchorMin = "0.3 0.4", AnchorMax = "0.7 0.6" }, CursorEnabled = false, FadeOut = 0.3f }, "Overlay", "PMMsgUI");
            elements.Add(new CuiLabel { FadeOut = 0.5f, Text = { FadeIn = 0.5f, Text = message, Color = "1 1 1 1", FontSize = 28, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" } }, first);
            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("PMDataConfirm")]
        private void PMDataConfirm(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = "0.2 0.2 0.2 0.1", Material = "assets/content/ui/uibackgroundblur.mat" }, RectTransform = { AnchorMin = "0.3 0.09", AnchorMax = "0.7 0.91" }, CursorEnabled = true, FadeOut = 0.1f }, "Overlay", "PMDataConfirm");

            elements.Add(new CuiButton { Button = { Color = "0 0 0 1" }, RectTransform = { AnchorMin = "0.28 0.46", AnchorMax = $"0.72 0.54" }, Text = { Text = String.Empty } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = "", FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.28 0.46", AnchorMax = $"0.72 0.54" }, }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"ManageData true {arg.Args[0]}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.3 0.48", AnchorMax = "0.45 0.52" }, Text = { Text = lang.GetMessage("confirm", this), FontSize = 14, Align = TextAnchor.MiddleCenter }, }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"ManageData false {arg.Args[0]}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.55 0.48", AnchorMax = "0.7 0.52" }, Text = { Text = lang.GetMessage("cancel", this), FontSize = 14, Align = TextAnchor.MiddleCenter }, }, mainName);

            CuiHelper.AddUi(player, elements);
        }

        [ConsoleCommand("ManageData")]
        private void ManageData(ConsoleSystem.Arg arg)
        {
            string confirmation = arg.Args[0];
            if (confirmation == "false")
                CuiHelper.DestroyUi(arg.Player(), "PMDataConfirm");
            else
            {
                int cmd = Convert.ToInt16(arg.Args[1]);
                if (cmd == 1)
                    MsgUI(arg.Player(), "Local Players Saved"); // Do this message for every option.

                switch (cmd)
                {
                    case 0: DoLocal(true, true); break;
                    case 1: DoLocal(true, false); break;
                    case 2: DoLocal(false, true); break;
                    case 3: DoLocal(false, false); break;
                    case 4: WipeLocal(); break;
                    case 5: DoSQL(true, true); break;
                    case 6: DoSQL(true, false); break;
                    case 7: DoSQL(false, true); break;
                    case 8: DoSQL(false, false); break;
                    case 9: WipeSQL(); break;
                    case 10: EmptyAllGroups(); break;
                    case 11: DeleteAllGroups(); break;
                    case 12: WipePlayerPerms(); break;
                    case 13: PurgeUnloadedForGroups(); break;
                    case 14: PurgeUnloadedForPlayers(); break;
                }
                CuiHelper.DestroyUi(arg.Player(), "PMDataConfirm");
            }
        }

        void PlugsUI(BasePlayer player, string msg, string group, int page)
        {
            var path = ActiveAdmins[player.userID];
            var backpage = group == "false" ? path.PPage : path.GPage;
            string toggle = (group == "true") ? "false" : "true";
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "PMPermsUI");
            int plugsTotal = 0, pos1 = 60 - (page * 60), next = page + 1, previous = page - 1;

            for (int i = 0; i < PlugList.Count; i++)
            {
                pos1++;
                plugsTotal++;

                if (pos1 > 0 && pos1 < 21)
                    elements.Add(new CuiButton { Button = { Command = $"PermsList {i} null null {group} null 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.1 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.3 {0.91 - (pos1 * 3f) / 100f}" }, Text = { Text = $"{PlugList[i]}", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                if (pos1 > 20 && pos1 < 41)
                    elements.Add(new CuiButton { Button = { Command = $"PermsList {i} null null {group} null 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.4 {(0.89 - ((pos1 - 20) * 3f) / 100f)}", AnchorMax = $"0.6 {0.91 - ((pos1 - 20) * 3f) / 100f}" }, Text = { Text = $"{PlugList[i]}", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                if (pos1 > 40 && pos1 < 61)
                    elements.Add(new CuiButton { Button = { Command = $"PermsList {i} null null {group} null 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.7 {(0.89 - ((pos1 - 40) * 3f) / 100f)}", AnchorMax = $"0.9 {0.91 - ((pos1 - 40) * 3f) / 100f}" }, Text = { Text = $"{PlugList[i]}", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            }
            elements.Add(new CuiButton { Button = { Command = $"PMTogglePlayerGroup {toggle} {backpage}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.55 0.02", AnchorMax = "0.75 0.04" }, Text = { Text = lang.GetMessage("GUIBack", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = msg, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, mainName);

            if (group == "false")
                elements.Add(new CuiButton { Button = { Command = "Groups 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.45 0.04" }, Text = { Text = lang.GetMessage("GUIGroups", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            else
                elements.Add(new CuiButton { Button = { Command = "PlayersIn 1", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.45 0.04" }, Text = { Text = lang.GetMessage("GUIPlayers", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);

            if (plugsTotal > (page * 60))
                elements.Add(new CuiButton { Button = { Command = $"Navigate {group} {next}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.9 0.04" }, Text = { Text = lang.GetMessage("->", this), FontSize = 11, Align = TextAnchor.MiddleCenter }, }, mainName);

            if (page > 1)
                elements.Add(new CuiButton { Button = { Command = $"Navigate {group} {previous}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.2 0.04" }, Text = { Text = lang.GetMessage("<-", this), FontSize = 11, Align = TextAnchor.MiddleCenter }, }, mainName);

            CuiHelper.AddUi(player, elements);
        }

        void PMPermsUI(BasePlayer player, string msg, int PlugNumber, string group, int page)
        {
            var path = ActiveAdmins[player.userID];
            var elements = new CuiElementContainer();

            var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "PMPermsUI");

            int permsTotal = 0, pos1 = 20 - (page * 20), next = (page + 1), previous = (page - 1);
            elements.Add(new CuiButton { Button = { Command = $"PermsList {PlugNumber} grant null {group} all {page}", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.5 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.6 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUIAll", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"PermsList {PlugNumber} revoke null {group} all {page}", Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.65 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.75 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUINone", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);

            var elements1 = new CuiElementContainer();
            bool list = false;
            foreach (var perm in numberedPerms)
            {
                SetButtons(true);
                pos1++;
                permsTotal++;
                var permNo = perm.Key;
                string showName = numberedPerms[permNo];
                string output = showName.Substring(showName.IndexOf('.') + 1);
                string granted = lang.GetMessage("GUIGranted", this);

                if (pos1 > 0 && pos1 < 21)
                {
                    granted = lang.GetMessage("GUIGranted", this);
                    if ((bool)PermsCheck(player, group, numberedPerms[permNo])[0])
                        SetButtons(false);
                    List<string> inheritcheck = (List<string>)(PermsCheck(player, group, numberedPerms[permNo])[1]);
                    if (inheritcheck.Count > 0)
                    {
                        if (path.inheritedcheck == numberedPerms[permNo])
                        {
                            var mainName1 = elements1.Add(new CuiPanel { Image = { Color = "0.1 0.1 0.1 0.99" }, RectTransform = { AnchorMin = "0.3 0.1", AnchorMax = "0.7 0.86" }, CursorEnabled = true, FadeOut = 0.1f }, "Overlay", "PMConfirmUI");
                            elements1.Add(new CuiButton { Button = { Command = $"ShowInherited {PlugNumber} null {numberedPerms[permNo]} {group} null {page} -", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.01", AnchorMax = "0.6 0.05" }, Text = { Text = lang.GetMessage("GUIBack", this), FontSize = 14, Align = TextAnchor.MiddleCenter }, }, mainName1);

                            float h1 = 0, h2 = 0;
                            elements1.Add(new CuiButton { Button = { Command = "", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.3 0.8", AnchorMax = "0.7 0.825" }, Text = { Text = $"{numberedPerms[permNo]}", FontSize = 12, Align = TextAnchor.MiddleCenter }, }, mainName1);
                            elements1.Add(new CuiLabel { Text = { Text = $"{lang.GetMessage("GUIInheritedFrom", this)}", FontSize = 11, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.77", AnchorMax = "1 0.8" } }, mainName1);

                            for (int i = 0; i < inheritcheck.Count; i++)
                            {
                                h1 = i * 0.022f;
                                h2 = i * 0.022f;
                                elements1.Add(new CuiLabel { Text = { Text = $"{inheritcheck[i]}", FontSize = 11, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = $"0 {0.7 - h1}", AnchorMax = $"1 {0.75 - h2}" } }, mainName1);
                            }

                            list = true;
                        }
                        elements.Add(new CuiButton { Button = { Command = $"ShowInherited {PlugNumber} null {numberedPerms[permNo]} {group} null {page} {numberedPerms[permNo]}", Color = config.InheritedColour }, RectTransform = { AnchorMin = $"0.8 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.9 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUIInherited", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                    }
                    elements.Add(new CuiButton { Button = { Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.1 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.45 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = $"{output}", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);

                    elements.Add(new CuiButton { Button = { Command = $"PermsList {PlugNumber} grant {numberedPerms[permNo]} {group} null {page}", Color = ButtonColour1 }, RectTransform = { AnchorMin = $"0.5 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.6 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUIGranted", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                    elements.Add(new CuiButton { Button = { Command = $"PermsList {PlugNumber} revoke {numberedPerms[permNo]} {group} null {page}", Color = ButtonColour2 }, RectTransform = { AnchorMin = $"0.65 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.75 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUIRevoked", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                }
            }

            elements.Add(new CuiButton { Button = { Command = $"Navigate {group} {path.pluginPage}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.6 0.04" }, Text = { Text = lang.GetMessage("GUIBack", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = msg, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, mainName);

            if (permsTotal > (page * 20))
                elements.Add(new CuiButton { Button = { Command = $"PermsList {PlugNumber} null null {group} null {next}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.9 0.04" }, Text = { Text = "->", FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            if (page > 1)
                elements.Add(new CuiButton { Button = { Command = $"PermsList {PlugNumber} null null {group} null {previous}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.2 0.04" }, Text = { Text = "<-", FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            CuiHelper.AddUi(player, elements);

            if (list)
                CuiHelper.AddUi(player, elements1);
        }

        void ViewPlayersUI(BasePlayer player, string msg, int page)
        {
            var path = ActiveAdmins[player.userID];
            var outmsg = string.Format(lang.GetMessage("GUIPlayersIn", this), msg);
            string guiString = String.Format("0.1 0.1 0.1 {0}", config.guitransparency);
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "PMPermsUI");

            int playerCounter = 0, pos1 = 20 - (page * 20), next = page + 1, previous = page - 1;

            foreach (var useringroup in permission.GetUsersInGroup(path.subjectGroup))
            {
                pos1++;
                playerCounter++;
                if (pos1 > 0 && pos1 < 21)
                    elements.Add(new CuiButton { Button = { Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.2 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.8 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = $"{useringroup}", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
            }
            elements.Add(new CuiLabel { Text = { Text = outmsg, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, mainName);

            if (playerCounter > (page * 20))
                elements.Add(new CuiButton { Button = { Command = $"PlayersIn {next}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.9 0.04" }, Text = { Text = lang.GetMessage("->", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            if (page > 1)
                elements.Add(new CuiButton { Button = { Command = $"PlayersIn {previous}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.02", AnchorMax = "0.2 0.04" }, Text = { Text = lang.GetMessage("<-", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);

            elements.Add(new CuiButton { Button = { Command = $"PMEmptyGroup", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.25 0.02", AnchorMax = "0.45 0.04" }, Text = { Text = lang.GetMessage("removePlayers", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiButton { Button = { Command = $"Navigate true {path.PPage}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.55 0.02", AnchorMax = "0.75 0.04" }, Text = { Text = lang.GetMessage("GUIBack", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);

            CuiHelper.AddUi(player, elements);
        }

        void ViewGroupsUI(BasePlayer player, string msg, int page)
        {
            var path = ActiveAdmins[player.userID];
            var outmsg = string.Format(lang.GetMessage("GUIGroupsFor", this), msg);
            string guiString = String.Format("0.1 0.1 0.1 {0}", config.guitransparency);
            var elements = new CuiElementContainer();
            var mainName = elements.Add(new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = "0.32 0.1", AnchorMax = "0.68 0.9" }, CursorEnabled = true }, "Overlay", "PMPermsUI");

            int groupTotal = 0, pos1 = 20 - (page * 20), next = page + 1, previous = page - 1;

            foreach (var group in permission.GetGroups().OrderBy(x => x))
            {
                SetButtons(true);
                pos1++;
                groupTotal++;
                if (pos1 > 0 && pos1 < 21)
                {
                    foreach (var user in permission.GetUsersInGroup(group))
                    {
                        if (user.Contains(path.subject.UserIDString))
                        {
                            SetButtons(false);
                            break;
                        }
                    }

                    //MAKE THIS OPEN UI FOR THAT GROUP  
                    elements.Add(new CuiButton { Button = { Color = config.ButtonColour }, RectTransform = { AnchorMin = $"0.2 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.5 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = $"{group}", FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                    elements.Add(new CuiButton { Button = { Command = $"GroupAddRemove add {RemoveSpaces(group)} {page}", Color = ButtonColour1 }, RectTransform = { AnchorMin = $"0.55 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.65 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUIGranted", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                    elements.Add(new CuiButton { Button = { Command = $"GroupAddRemove remove {RemoveSpaces(group)} {page}", Color = ButtonColour2 }, RectTransform = { AnchorMin = $"0.7 {(0.89 - (pos1 * 3f) / 100f)}", AnchorMax = $"0.8 {(0.91 - (pos1 * 3f) / 100f)}" }, Text = { Text = lang.GetMessage("GUIRevoked", this), FontSize = 10, Align = TextAnchor.MiddleCenter } }, mainName);
                }
            }
            elements.Add(new CuiButton { Button = { Command = $"Navigate false {path.pluginPage}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.4 0.02", AnchorMax = "0.6 0.04" }, Text = { Text = lang.GetMessage("GUIBack", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            elements.Add(new CuiLabel { Text = { Text = outmsg, FontSize = 16, Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0 0.95", AnchorMax = "1 1" } }, mainName);

            if (groupTotal > (page * 20))
                elements.Add(new CuiButton { Button = { Command = $"Groups {next}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.7 0.02", AnchorMax = "0.8 0.04" }, Text = { Text = lang.GetMessage("->", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);
            if (page > 1)
                elements.Add(new CuiButton { Button = { Command = $"Groups {previous}", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.2 0.02", AnchorMax = "0.3 0.04" }, Text = { Text = lang.GetMessage("<-", this), FontSize = 11, Align = TextAnchor.MiddleCenter } }, mainName);

            CuiHelper.AddUi(player, elements);
        }
        #endregion

        #region console commands
        [ConsoleCommand("PMToMain")]
        private void PMToMain(ConsoleSystem.Arg arg) => PMMainUI(arg.Player(), false, 1);

        [ConsoleCommand("PMTogglePlayerGroup")]
        private void PMTogglePlayerGroup(ConsoleSystem.Arg arg, bool group, int page)
        {
            var player = arg?.Connection?.player as BasePlayer;
            if (player == null || arg.Args == null || arg.Args.Length != 2) return;
            group = !(Convert.ToBoolean(arg.Args[0]));
            page = Convert.ToInt16(arg.Args[1]);
            if (group)
                ActiveAdmins[player.userID].GPage = page;
            else
                ActiveAdmins[player.userID].PPage = page;
            DestroyMenu(player, false);
            PMMainUI(player, group, page);
        }

        [ConsoleCommand("ShowInherited")]
        private void ShowInherited(ConsoleSystem.Arg arg)
        {
            var path = ActiveAdmins[arg.Player().userID];
            if (arg.Player() == null || arg.Args == null || arg.Args.Length < 6) return;
            int pageNo = Convert.ToInt32(arg.Args[5]);
            path.inheritedcheck = arg.Args[6];
            var plugNumber = Convert.ToInt32(arg.Args[0]);
            string plugName = PlugList[Convert.ToInt32(arg.Args[0])];
            DestroyMenu(arg.Player(), false);
            PMPermsUI(arg.Player(), $"{StripSlashes(path.subject.displayName)} - {plugName}", plugNumber, "false", pageNo);
        }

        [ConsoleCommand("PMEmptyGroup")]
        private void PMEmptyGroup(ConsoleSystem.Arg arg)
        {
            var player = arg?.Connection?.player as BasePlayer;

            var elements1 = new CuiElementContainer();
            var mainName1 = elements1.Add(new CuiPanel { Image = { Color = "0.1 0.1 0.1 0.8" }, RectTransform = { AnchorMin = "0.4 0.42", AnchorMax = "0.6 0.48" }, CursorEnabled = true, FadeOut = 0.1f }, "Overlay", "PMConfirmUI");
            elements1.Add(new CuiButton { Button = { Command = $"Empty true", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.1 0.2", AnchorMax = "0.4 0.8" }, Text = { Text = lang.GetMessage("confirm", this), FontSize = 14, Align = TextAnchor.MiddleCenter }, }, mainName1);
            elements1.Add(new CuiButton { Button = { Command = $"Empty false", Color = config.ButtonColour }, RectTransform = { AnchorMin = "0.6 0.2", AnchorMax = "0.9 0.8" }, Text = { Text = lang.GetMessage("cancel", this), FontSize = 14, Align = TextAnchor.MiddleCenter }, }, mainName1);

            CuiHelper.AddUi(player, elements1);
        }

        [ConsoleCommand("Empty")]
        private void Empty(ConsoleSystem.Arg arg)
        {
            var path = ActiveAdmins[arg.Player().userID];
            string confirmation = arg.Args[0];
            if (confirmation == "true")
            {
                int count = 0;
                foreach (var user in permission.GetUsersInGroup(path.subjectGroup))
                {
                    count++;
                    string str = user.Substring(0, 17);
                    permission.RemoveUserGroup(str, path.subjectGroup);
                    DestroyMenu(arg.Player(), false);
                    var argsOut = new string[] { "group", path.subjectGroup };
                    CmdPerms(arg.Player(), null, argsOut);
                }
                if (count == 0)
                    CuiHelper.DestroyUi(arg.Player(), "PMConfirmUI");
            }
            else
                CuiHelper.DestroyUi(arg.Player(), "PMConfirmUI");
        }

        [ConsoleCommand("EmptyGroup")]//user console command
        private void EmptyGroup(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !HasPermission(arg.Player().UserIDString, permAllowed) & !IsAuth(arg.Player()))
            {
                SendReply(arg.Player(), config.TitleColour + lang.GetMessage("title", this) + "</color>" + config.MessageColour + lang.GetMessage("NotAdmin", this) + "</color>");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
                return;
            string groupname = arg.Args[0];
            var list = permission.GetUsersInGroup(groupname);
            if (list == null || list.Length == 0)
            {
                Puts($"Group {groupname} was not found.");
                return;
            }
            foreach (var user in permission.GetUsersInGroup(groupname))
            {
                string str = user.Substring(0, 17);
                permission.RemoveUserGroup(str, groupname);
            }
            Puts($"All users were removed from {groupname}");
        }

        string RemoveSpaces(string input) => input.Replace(" ", "-");
        string RemoveDashes(string input) => input.Replace("-", " ");

        [ConsoleCommand("GroupAddRemove")]
        private void GroupAddRemove(ConsoleSystem.Arg arg)
        {
            var path = ActiveAdmins[arg.Player().userID];
            if (arg.Player() == null || arg.Args == null || arg.Args.Length < 3) return;
            string Pname = path.subject.userID.ToString();
            string userGroup = RemoveDashes(arg.Args[1]);
            int page = Convert.ToInt32(arg.Args[2]);
            if (arg.Args[0] == "add")
                permission.AddUserGroup(Pname, userGroup);
            if (arg.Args[0] == "remove")
                permission.RemoveUserGroup(Pname, userGroup);
            DestroyMenu(arg.Player(), false);
            ViewGroupsUI(arg.Player(), StripSlashes(path.subject.displayName), page);
        }

        [ConsoleCommand("Groups")]
        private void GroupsPM(ConsoleSystem.Arg arg)
        {
            var path = ActiveAdmins[arg.Player().userID];
            if (arg.Player() == null || arg.Args == null || arg.Args.Length < 1) return;
            ActiveAdmins[arg.Player().userID].GPage = Convert.ToInt32(arg.Args[0]);
            DestroyMenu(arg.Player(), false);
            ViewGroupsUI(arg.Player(), StripSlashes(path.subject.displayName), path.GPage);
        }

        [ConsoleCommand("PlayersIn")]
        private void PlayersPM(ConsoleSystem.Arg arg)
        {
            var path = ActiveAdmins[arg.Player().userID];
            if (arg.Player() == null || arg.Args == null || arg.Args.Length < 1) return;
            ActiveAdmins[arg.Player().userID].PPage = Convert.ToInt32(arg.Args[0]);
            DestroyMenu(arg.Player(), false);
            ViewPlayersUI(arg.Player(), path.subjectGroup, path.PPage);
        }

        [ConsoleCommand("ClosePM")]
        private void ClosePM(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null) return;
            ActiveAdmins.Remove(arg.Player().userID);
            DestroyMenu(arg.Player(), true);
        }

        [ConsoleCommand("Navigate")]
        private void Navigate(ConsoleSystem.Arg arg)
        {
            var path = ActiveAdmins[arg.Player().userID];
            if (arg.Player() == null || arg.Args == null || arg.Args.Length < 2) return;
            ActiveAdmins[arg.Player().userID].pluginPage = Convert.ToInt32(arg.Args[1]);
            DestroyMenu(arg.Player(), false);
            string[] argsOut;
            if (arg.Args[0] == "true")
            {
                argsOut = new string[] { "group", path.subjectGroup, path.pluginPage.ToString() };
                CmdPerms(arg.Player(), null, argsOut);
            }
            else
            {
                argsOut = new string[] { "player", path.subject.userID.ToString(), path.pluginPage.ToString() };
                CmdPerms(arg.Player(), null, argsOut);
            }
            return;
        }

        [ConsoleCommand("PMSelected")]
        private void PMSelected(ConsoleSystem.Arg arg)
        {
            DestroyMenu(arg.Player(), false);
            string[] argsOut;
            argsOut = new string[] { arg.Args[0], arg.Args[1] };
            if (arg.Args[0] == "player")
                ActiveAdmins[arg.Player().userID].subject = FindPlayer(Convert.ToUInt64(arg.Args[1]));
            else
                ActiveAdmins[arg.Player().userID].subjectGroup = arg.Args[1];
            CmdPerms(arg.Player(), null, argsOut);
            return;
        }

        [ConsoleCommand("PermsList")]
        private void PermsList(ConsoleSystem.Arg arg, int plugNumber)
        {
            var path = ActiveAdmins[arg.Player().userID];
            if (arg.Player() == null || arg.Args == null || arg.Args.Length < 6) return;
            int pageNo = Convert.ToInt32(arg.Args[5]);
            string Pname;
            string group = arg.Args[3];

            if (arg.Args[4] == "all")
            {
                if (arg.Args[2] != null)
                {
                    Pname = path.subject?.userID.ToString();
                    string action = arg.Args[1];
                    foreach (var perm in numberedPerms)
                    {
                        if (config.AllPerPage == true && perm.Key > (pageNo * 20) - 20 && perm.Key < ((pageNo * 20) + 1))
                        {
                            if (action == "grant" && group == "false")
                                permission.GrantUserPermission(Pname, perm.Value, null);
                            if (action == "revoke" && group == "false")
                                permission.RevokeUserPermission(Pname, perm.Value);
                            if (action == "grant" && group == "true")
                                permission.GrantGroupPermission(path.subjectGroup, perm.Value, null);
                            if (action == "revoke" && group == "true")
                                permission.RevokeGroupPermission(path.subjectGroup, perm.Value);
                        }
                        if (config.AllPerPage == false)
                        {
                            if (action == "grant" && group == "false")
                                permission.GrantUserPermission(Pname, perm.Value, null);
                            if (action == "revoke" && group == "false")
                                permission.RevokeUserPermission(Pname, perm.Value);
                            if (action == "grant" && group == "true")
                                permission.GrantGroupPermission(path.subjectGroup, perm.Value, null);
                            if (action == "revoke" && group == "true")
                                permission.RevokeGroupPermission(path.subjectGroup, perm.Value);
                        }
                    }
                }
            }
            else
            {
                Pname = path.subject?.userID.ToString();
                string action = arg.Args[1];
                string PermInHand = arg.Args[2];
                if (arg.Args[2] != null)
                {
                    if (action == "grant" && group == "false")
                        permission.GrantUserPermission(Pname, PermInHand, null);
                    if (action == "revoke" && group == "false")
                        permission.RevokeUserPermission(Pname, PermInHand);
                    if (action == "grant" && group == "true")
                        permission.GrantGroupPermission(path.subjectGroup, PermInHand, null);
                    if (action == "revoke" && group == "true")
                        permission.RevokeGroupPermission(path.subjectGroup, PermInHand);
                }
            }
            plugNumber = Convert.ToInt32(arg.Args[0]);
            string plugName = PlugList[plugNumber];

            numberedPerms.Clear();
            int numOfPerms = 0;
            foreach (var perm in permission.GetPermissions().OrderBy(x => x))
            {
                if (perm.Contains($"{plugName}."))
                {
                    numOfPerms++;
                    numberedPerms.Add(numOfPerms, perm);
                }
            }
            DestroyMenu(arg.Player(), false);
            if (group == "false")
                PMPermsUI(arg.Player(), $"{StripSlashes(path.subject.displayName)} - {plugName}", plugNumber, group, pageNo);
            else
                PMPermsUI(arg.Player(), $"{path.subjectGroup} - {plugName}", plugNumber, group, pageNo);
            return;
        }
        #endregion

        #region chat commands
        [ChatCommand("perms")]
        void CmdPerms(BasePlayer player, string command, string[] args)
        {
            var id = player.userID;
            if (!HasPermission(player.UserIDString, permAllowed) & !IsAuth(player))
            {
                SendReply(player, config.TitleColour + lang.GetMessage("title", this) + "</color>" + config.MessageColour + lang.GetMessage("NotAdmin", this) + "</color>");
                return;
            }

            if (args?.Length == 1 && args[0] == "data")
            {
                PMBgUI(player);
                PMDataUI(player);
                return;
            }

            if (!ActiveAdmins.ContainsKey(player.userID))
                ActiveAdmins.Add(player.userID, new Info());

            var path = ActiveAdmins[player.userID];
            GetPlugs(player);

            int page = 1;
            if (args.Length == 3)
                page = Convert.ToInt32(args[2]);

            if (args.Length < 2)
            {
                bool group = (args != null && args.Length == 1 && args[0] == "group") ? true : false;
                if (MenuOpen.Contains(player.userID))
                    DestroyMenu(player, true);
                PMBgUI(player);
                PMMainUI(player, group, 1);
                return;
            }

            if (args[0] == "player")
            {
                UInt64 n = 0;
                bool isNumeric = UInt64.TryParse(args[1], out n);
                path.subject = isNumeric ? FindPlayer(n) : FindPlayerByName(args[1]);
                if (path.subject == null)
                {
                    SendReply(player, config.TitleColour + lang.GetMessage("title", this) + "</color>" + config.MessageColour + lang.GetMessage("NoPlayer", this) + "</color>", args[1]);
                    return;
                }
                string msg = string.Format(lang.GetMessage("GUIName", this), StripSlashes(path.subject.displayName));

                if (MenuOpen.Contains(player.userID))
                    DestroyMenu(player, true);
                PMBgUI(player);
                PlugsUI(player, msg, "false", page);
            }
            else if (args[0] == "group")
            {
                List<string> Groups = new List<string>();
                foreach (var group in permission.GetGroups())
                    Groups.Add(group);
                if (Groups.Contains($"{args[1]}"))
                {
                    string msg = string.Format(lang.GetMessage("GUIName", this), args[1].Replace(@"\", ""));

                    ActiveAdmins[player.userID].subjectGroup = args[1];
                    if (MenuOpen.Contains(player.userID))
                        DestroyMenu(player, true);
                    PMBgUI(player);
                    PlugsUI(player, msg, "true", page);
                    return;
                }
                SendReply(player, config.TitleColour + lang.GetMessage("title", this) + "</color>" + config.MessageColour + lang.GetMessage("NoGroup", this) + "</color>", args[1]);
            }
            else
                SendReply(player, config.TitleColour + lang.GetMessage("title", this) + "</color>" + config.MessageColour + lang.GetMessage("Syntax", this) + "</color>");
        }

        string StripSlashes(string name) => name.Replace(@"\", "").Replace(@"/", "");

        BasePlayer FindPlayer(ulong ID)
        {
            BasePlayer result = null;
            foreach (BasePlayer current in BasePlayer.allPlayerList)
            {
                if (current.userID == ID)
                    result = current;
            }
            return result;
        }

        BasePlayer FindPlayerByName(string name)
        {
            BasePlayer result = null;
            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player.displayName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    || player.UserIDString.Contains(name, CompareOptions.OrdinalIgnoreCase)
                    || player.displayName.Contains(name, CompareOptions.OrdinalIgnoreCase))
                    result = player;
            }
            return result;
        }

        #endregion

        #region Data
        StoredData storedData;
        class StoredData
        {
            public Dictionary<string, Data> PermsData = new Dictionary<string, Data>();
        }

        class Data
        {
            public List<string> Perms;
            public List<string> Players;
            public int player;
        }

        void Loaded() => storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>("PermissionsManager");

        void SaveData() => Interface.Oxide.DataFileSystem.WriteObject("PermissionsManager", storedData);
        #endregion
        #region config
        public ConfigData config;
        public class ConfigData
        {
            [JsonProperty(PropertyName = "Options - GUI Transparency 0-1")]
            public double guitransparency = 0.9;
            [JsonProperty(PropertyName = "Chat - Title colour")]
            public string TitleColour = "<color=orange>";
            [JsonProperty(PropertyName = "Chat - Message colour")]
            public string MessageColour = "<color=white>";
            [JsonProperty(PropertyName = "Options - Plugin BlockList")]
            public string BlockList = "";
            [JsonProperty(PropertyName = "GUI - Label colour")]
            public string ButtonColour = "0.7 0.32 0.17 1";
            [JsonProperty(PropertyName = "GUI - On colour")]
            public string OnColour = "0.7 0.32 0.17 1";
            [JsonProperty(PropertyName = "GUI - Off colour")]
            public string OffColour = "0.2 0.2 0.2 1";
            [JsonProperty(PropertyName = "GUI - All = per page")]
            public bool AllPerPage = false;
            [JsonProperty(PropertyName = "GUI - Inherited colour")]
            public string InheritedColour = "0.9 0.6 0.17 1";
            public bool Keep_Perms_After_Wipe = false;
            public MySQL MySQL = new MySQL();
        }

        public class MySQL
        {
            public bool useMySQL;
            public int sql_port = 3306;
            public string sql_host = String.Empty, sql_db = String.Empty, sql_user = String.Empty, sql_pass = String.Empty;
            public string tablename = "PermissionsManager";
        }

        private bool LoadConfigVariables()
        {
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                    return false;
            }
            catch
            {
                return false;
            }

            SaveConfig(config);
            return true;
        }

        protected override void LoadDefaultConfig()
        {
            Puts("Creating new config file.");
            var config = new ConfigData();
            SaveConfig(config);
        }

        void SaveConfig(ConfigData config) => Config.WriteObject(config, true);
        #endregion

        #region messages
        readonly Dictionary<string, string> messages = new Dictionary<string, string>()
        {
            {"title", "Permissions Manager: " },
            {"NoGroup", "Group {0} was not found." },
            {"NoPlayer", "Player {0} was not found." },
            {"GUIAll", "Grant All" },
            {"GUINone", "Revoke All" },
            {"GUIBack", "Back" },
            {"GUIGroups", "Groups" },
            {"GUIPlayers", "Players" },
            {"GUIInherited", "Inherited" },
            {"GUIInheritedFrom", "Inherited from" },
            {"GUIGranted", "Granted" },
            {"GUIRevoked", "Revoked" },
            {"GUIName", "Permissions for {0}" },
            {"GUIGroupsFor", "Groups for {0}"},
            {"GUIPlayersIn", "Players in {0}"},
            {"removePlayers", "Remove All Players"},
            {"confirm", "Confirm"},
            {"cancel", "Cancel"},
            {"NotAdmin", "You need Auth Level 2, or permission, to use this command."},
            {"Back", "Back"},
            {"All", "All"},
            {"Syntax", "Use /perms, /perms player *name*, or /perms group *name*"},
            {"Safe", "SQL operation is complete."},
            {"NotSafe", "Please do not reload, or unload, PermissionsManager until save-completion message." },
        };
        #endregion

        #region Backup
        Core.MySql.Libraries.MySql Sql = Interface.Oxide.GetLibrary<Core.MySql.Libraries.MySql>();
        Connection Sql_conn;

        private void WipeLocal()
        {
            storedData = new StoredData();
            SaveData();
            Puts("Permissions Manager local backup was wiped.");
        }

        private void WipeSQL()
        {
            try { Sql_conn?.Con?.Open(); }
            catch (Exception e) { PrintWarning(e.Message); return; }

            Sql.Insert(Core.Database.Sql.Builder.Append($"DROP TABLE IF EXISTS {config.MySQL.tablename}"), Sql_conn);
            Puts("Permissions Manager MySQL backup was wropped.");
        }

        void EmptyAllGroups()
        {
            foreach (var group in permission.GetGroups().ToList())
            {
                permission.RemoveGroup(group);
                permission.CreateGroup(group, group, 0);
            }
        }
        void DeleteAllGroups()
        { 
            foreach (var group in permission.GetGroups().ToList())
                permission.RemoveGroup(group);

            permission.CreateGroup("default", "default", 0);
            permission.CreateGroup("admin", "admin", 0);
        }
         
        void WipePlayerPerms()
        {
            var permissions = permission.GetPermissions();

            foreach (var perm in permissions)
            {
                var allusers = permission.GetPermissionUsers(perm);

                foreach (var user in allusers)
                    if (user.Length > 17)
                        permission.RevokeUserPermission(user.Substring(0,17), perm);
            }
            permission.SaveData();
        }

        void PurgeUnloadedForGroups()
        {
            var allperms = permission.GetPermissions();

            foreach (var group in permission.GetGroups())
                foreach (var entry in permission.GetGroupPermissions(group))
                    if (!allperms.Contains(entry))
                        permission.RevokeGroupPermission(group, entry);

            permission.SaveData();
        }

        void PurgeUnloadedForPlayers()
        {
            var allperms = permission.GetPermissions();
            List<string> perms = new List<string>();

            var userData = ProtoStorage.Load<Dictionary<string, UserData>>(new string[] { "oxide.users" });
            userData = userData.Where(x => x.Value.Perms.Count > 0).ToDictionary(x => x.Key, x => x.Value);

            foreach (var user in userData.ToDictionary(val => val.Key, val => val.Value))
            {
                perms = new List<string>();
                foreach (var entry in user.Value.Perms)
                {
                    if (!allperms.Contains(entry))
                        permission.RevokeUserPermission(user.Key, entry); 
                }
            }
            permission.SaveData();
        }

        public void HandleAction(int num)
        {
            permission.SaveData();
            PrintWarning(lang.GetMessage("Safe", this));
        }

        void OnServerSave() => DoLocal(true, true);
        void OnNewSave() => DoLocal(false, true);

        void DoLocal(bool save, bool groups)
        {
            permission.SaveData();
            if (save)
            {
                List<string> perms = new List<string>();
                if (groups)
                {
                    foreach (var group in permission.GetGroups())
                    {
                        perms = new List<string>();
                        List<string> players = new List<string>();
                        foreach (var entry in permission.GetGroupPermissions(group))
                            perms.Add(entry);

                        foreach (var entry in permission.GetUsersInGroup(group))
                            players.Add(entry);

                        storedData.PermsData[group] = new Data()
                        {
                            Perms = perms,
                            Players = players,
                            player = 0
                        };
                    }
                }
                else
                {
                    var userData = ProtoStorage.Load<Dictionary<string, UserData>>(new string[] { "oxide.users" });
                    userData = userData.Where(x => x.Value.Perms.Count > 0).ToDictionary(x => x.Key, x => x.Value);

                    foreach (var user in userData)
                    {
                        perms = new List<string>();
                        foreach (var entry in user.Value.Perms)
                            perms.Add(entry);

                        storedData.PermsData[user.Key] = new Data()
                        {
                            Perms = perms,
                            Players = null,
                            player = 1
                        };
                    }
                }
            }
            else
            {
                foreach (var record in storedData.PermsData) 
                {
                    if (record.Value.player == 0)
                    {
                        if (!permission.GroupExists(record.Key))
                            permission.CreateGroup(record.Key, record.Key, 0);

                        foreach (var player in record.Value.Players)
                            permission.AddUserGroup(player, record.Key);
                        foreach (var perm in record.Value.Perms)
                            permission.GrantGroupPermission(record.Key, perm, null);
                    }
                    else
                    {
                        foreach (var perm in record.Value.Perms)
                            permission.GrantUserPermission(record.Key, perm, null);
                    }
                }
            }
            permission.SaveData();
            SaveData();
        }

        void DoSQL(bool save, bool groups)
        {
            permission.SaveData();

            var userData = ProtoStorage.Load<Dictionary<string, UserData>>(new string[] { "oxide.users" });

            userData = userData.Where(x => x.Value.Perms.Count > 0 && x.Value.Groups.Count > 0).ToDictionary(x => x.Key, x => x.Value);

            int rows = 0;
            try { Sql_conn?.Con?.Open(); }
            catch (Exception e) { PrintWarning(e.Message); return; }

            Sql.Insert(Core.Database.Sql.Builder.Append("SET NAMES utf8mb4"), Sql_conn); 
            Sql.Insert(Core.Database.Sql.Builder.Append($"CREATE TABLE IF NOT EXISTS {config.MySQL.tablename} ( ID VARCHAR(17) NOT NULL, Perms TEXT, Players Text, Player TINYINT, PRIMARY KEY (ID)) DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;"), Sql_conn);

            Sql sqlString = Core.Database.Sql.Builder.Append($"SELECT * FROM {config.MySQL.tablename}");
            Sql.Query(sqlString, Sql_conn, list =>
            {
                if (list == null && list.Count() == 0)
                    rows = list[0].Count();
                else if (!save)
                {
                    foreach (var dict in list)
                    {
                        List<string> perms = dict["Perms"].ToString().Split(',').ToList();
                        List<string> players = dict["Players"].ToString().Split(',').ToList();
                        string ID = (string)dict["ID"];

                        if (groups && Convert.ToInt16(dict["Player"]) == 0)
                        {
                            if (!permission.GroupExists(ID))
                                permission.CreateGroup(ID, ID, 0);

                            foreach (var player in players)
                                permission.AddUserGroup(player, ID);

                            if (dict["Perms"].ToString() != string.Empty)
                                foreach (var perm in perms)
                                    permission.GrantGroupPermission(ID, perm, null);
                        }
                        else if (!groups && Convert.ToInt16(dict["Player"]) == 1)
                            foreach (var perm in perms)
                                permission.GrantUserPermission(ID, perm, null);
                    }
                }
            });

            if (save)
            {
                var allperms = permission.GetPermissions();

                int counter = 0;
                Sql main = new Sql();

                main.Append($"REPLACE INTO {config.MySQL.tablename} (ID, Perms, Players, Player) VALUES ");
                var permissions = permission.GetPermissions();

                if (groups)
                {
                    foreach (var group in permission.GetGroups())
                    {
                        string perms = "";
                        string players = "";
                        foreach (var entry in permission.GetGroupPermissions(group))
                            perms += entry + ",";

                        if (perms.Length > 0) 
                            perms = perms.Remove(perms.Length - 1);

                        foreach (var entry in permission.GetUsersInGroup(group))
                        {
                            if (entry.Length > 17)
                                players += entry.Substring(0, 17) + ",";
                        }

                        if (players.Length > 0)
                            players = players.Remove(players.Length - 1);


                        if (rows == 0)
                        {
                            if (counter > 0)
                                main.Append(",");
                            main.Append($"( @0, @1, @2, @3)", group, perms, players, 0);
                            counter++;
                        }
                    }
                }
                else
                {
                    foreach (var user in userData)
                    {
                        string perms = "";
                        foreach (var entry in user.Value.Perms)
                            perms += entry + ",";

                        if (perms.Length > 0)
                            perms = perms.Remove(perms.Length - 1);

                        if (rows == 0)
                        {
                            if (counter > 0)
                                main.Append(",");
                            main.Append($"( @0, @1, @2, @3)", user.Key, perms, "", 1);
                            counter++;
                        }
                    }
                }

                if (counter > 0)
                {
                    PrintWarning(lang.GetMessage("NotSafe", this));
                    Sql.Insert(main, Sql_conn, HandleAction);
                }
            }
        }
        #endregion
    }
}
