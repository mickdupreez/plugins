using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SkinBox", "k1lly0u", "2.1.11"), Description("Allows you to reskin item's by placing it in the SkinBox and selecting a new skin")]
    class SkinBox : RustPlugin
    {
        #region Fields
        [PluginReference]
        private Plugin ServerRewards, Economics;


        private Hash<ulong, string> _skinPermissions = new Hash<ulong, string>();

        private readonly Hash<string, HashSet<ulong>> _skinList = new Hash<string, HashSet<ulong>>();

        private readonly Hash<ulong, LootHandler> _activeSkinBoxes = new Hash<ulong, LootHandler>();

        private readonly Hash<string, string> _shortnameToDisplayname = new Hash<string, string>();

        private readonly Hash<ulong, string> _skinSearchLookup = new Hash<ulong, string>();

        private readonly Hash<ulong, string> _skinNameLookup = new Hash<ulong, string>();

        private readonly Hash<ulong, double> _cooldownTimes = new Hash<ulong, double>();


        private bool _apiKeyMissing = false;

        private bool _skinsLoaded = false;

        private Timer _approvedTimeout;


        private CostType _costType;

        private SortBy _sorting;

        private static SkinBox Instance { get; set; }

        private static Func<string, ulong, string> GetMessage;

        private const string COFFIN_PREFAB = "assets/prefabs/misc/halloween/coffin/coffinstorage.prefab";

        private const string LOOT_PANEL = "generic_resizable";

        private const int SCRAP_ITEM_ID = -932201673;

        private enum CostType { Scrap, ServerRewards, Economics }

        public enum SortBy { Config = 0, ConfigReversed = 1, Alphabetical = 2 }
        #endregion

        #region Oxide Hooks
        private void Loaded()
        {
            Instance = this;

            Configuration.Permission.RegisterPermissions(permission, this);
            Configuration.Permission.ReverseCustomSkinPermissions(ref _skinPermissions);

            Configuration.Command.RegisterCommands(cmd, this);

            _costType = ParseType<CostType>(Configuration.Cost.Currency);
            _sorting = ParseType<SortBy>(Configuration.Skins.Sorting);

            GetMessage = (string key, ulong userId) => lang.GetMessage(key, this, userId.ToString());
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoAPIKey"] = "The server owner has not entered a Steam API key in the config. Unable to continue!",
                ["SkinsLoading"] = "SkinBox is still gathering skins. Please try again soon",
                ["NoPermission"] = "You don't have permission to use the SkinBox",
                ["ToNearPlayer"] = "The SkinBox is currently not usable at this place",
                ["CooldownTime"] = "You need to wait {0} seconds to use the SkinBox again",

                ["NotEnoughBalanceOpen"] = "You need at least {0} {1} to open the SkinBox",
                ["NotEnoughBalanceUse"] = "You would need at least {0} {1} to skin {2}",
                ["NotEnoughBalanceTake"] = "{0} was not skinned. You do not have enough {1}",
                ["CostToUse"] = "Skinning a item is not free!\n{0} {3} to skin a deployable\n{1} {3} to skin a weapon\n{2} {3} to skin attire",

                ["TargetSkinNotInList"] = "That skin is not defined in the skin list for that item",
                ["TargetSkinIsCurrentSkin"] = "The target item is already skinned with that skin ID",
                ["ApplyingSkin"] = "Applying skin {0}",
                ["NoItemInHands"] = "You must have a item in your hands to use the quick skin method",

                ["NoSkinsForItem"] = "There are no skins setup for that item",
                ["BrokenItem"] = "You can not skin broken items",
                ["HasItem"] = "The skin box already contains an item",
                ["RedirectsDisabled"] = "Redirected skins are disabled on this server",
                ["InsufficientItemPermission"] = "You do not have permission to skin this type of item",

                ["Cost.Scrap"] = "Scrap",
                ["Cost.ServerRewards"] = "RP",
                ["Cost.Economics"] = "Eco",

                ["ReskinError.InvalidResourcePath"] = "Failed to find resource path for deployed item",
                ["ReskinError.TargetNull"] = "The target deployable has been destroyed",
                ["ReskinError.MountBlocked"] = "You can not skin this while a player is mounted",
                ["ReskinError.IOConnected"] = "You can not skin this while it is connected",
                ["ReskinError.NoAuth"] = "You need building auth to reskin deployed items",
                ["ReskinError.NoTarget"] = "Unable to find a valid deployed item",
                ["NoDefinitionForEntity"] = "Failed to find the definition for the target item"
            }, this);
        }

        private void OnServerInitialized()
        {
            UnsubscribeHooks();

            if (string.IsNullOrEmpty(Configuration.Skins.APIKey))
            {
                _apiKeyMissing = true;
                SendAPIMissingWarning();                
                return;
            }

            if ((Steamworks.SteamInventory.Definitions?.Length ?? 0) == 0 && Configuration.Skins.UseApproved)
            {
                PrintWarning("Waiting for Steamworks to update item definitions....");
                Steamworks.SteamInventory.OnDefinitionsUpdated += StartSkinRequest;

                _approvedTimeout = timer.Once(Configuration.Skins.ApprovedTimeout, AbortApprovedSkinRequest);
            }
            else StartSkinRequest();
        }

        private object CanAcceptItem(ItemContainer container, Item item)
        {
            if (container.entityOwner == null)
                return null;

            LootHandler lootHandler = container.entityOwner.GetComponent<LootHandler>();
            if (lootHandler == null)
                return null;

            if (item.isBroken) 
            {
                lootHandler.PopupMessage(GetMessage("BrokenItem", lootHandler.Looter.userID));
                return ItemContainer.CanAcceptResult.CannotAccept;
            }

            if (lootHandler.HasItem)
            {
                lootHandler.PopupMessage(GetMessage("HasItem", lootHandler.Looter.userID));
                return ItemContainer.CanAcceptResult.CannotAccept;
            }
            
            if (!HasItemPermissions(lootHandler.Looter, item))
            {
                lootHandler.PopupMessage(GetMessage("InsufficientItemPermission", lootHandler.Looter.userID));
                return ItemContainer.CanAcceptResult.CannotAccept;
            }

            string shortname = GetRedirectedShortname(item.info.shortname);
            if (!Configuration.Skins.UseRedirected && !shortname.Equals(item.info.shortname, StringComparison.OrdinalIgnoreCase))
            {
                lootHandler.PopupMessage(GetMessage("RedirectsDisabled", lootHandler.Looter.userID)); 
                return ItemContainer.CanAcceptResult.CannotAccept;
            }

            HashSet<ulong> skins;
            if (!_skinList.TryGetValue(shortname, out skins) || skins.Count == 0)
            {
                lootHandler.PopupMessage(GetMessage("NoSkinsForItem", lootHandler.Looter.userID));
                return ItemContainer.CanAcceptResult.CannotAccept;
            }
                        
            int reskinCost = GetReskinCost(item);
            if (reskinCost > 0 && !CanAffordToUse(lootHandler.Looter, reskinCost))
            {                
                lootHandler.PopupMessage(string.Format(GetMessage("NotEnoughBalanceUse", lootHandler.Looter.userID), 
                                                                   reskinCost,
                                                                   GetCostType(lootHandler.Looter.userID),
                                                                   item.info.displayName.english));

                return ItemContainer.CanAcceptResult.CannotAccept;

            }

            string result = Interface.Call<string>("SB_CanAcceptItem", lootHandler.Looter, item);
            if (!string.IsNullOrEmpty(result))
            {
                lootHandler.PopupMessage(result);
                return ItemContainer.CanAcceptResult.CannotAccept;
            }

            return null;
        }

        private void OnItemSplit(Item item, int amount)
        {
            LootHandler lootHandler = item.parent?.entityOwner?.GetComponent<LootHandler>();
            if (lootHandler != null)
                lootHandler.CheckItemHasSplit(item);
        }        

        private object CanMoveItem(Item item, PlayerInventory inventory, uint targetContainerID, int targetSlot, int amount)
        {
            if (item.parent?.entityOwner != null)
            {
                LootHandler lootHandler = item.parent != null && item.parent.entityOwner != null ? item.parent.entityOwner.GetComponent<LootHandler>() : null; 
                if (lootHandler != null && lootHandler.InputAmount > 1)
                {
                    if (targetContainerID == 0 || targetSlot == -1)
                        return false;

                    ItemContainer targetContainer = inventory.FindContainer(targetContainerID);
                    if (targetContainer != null && targetContainer.GetSlot(targetSlot) != null)                    
                        return false;
                }
            }
            return null;
        }

        private object OnItemAction(Item item, string action, BasePlayer player)
        {
            LootHandler lootHandler;
            if (!_activeSkinBoxes.TryGetValue(player.userID, out lootHandler))
                return null;

            const string DROP_ACTION = "drop";
            if (!action.Equals(DROP_ACTION, StringComparison.OrdinalIgnoreCase))            
                return null;

            if (lootHandler.Entity.inventory?.itemList?.Contains(item) ?? false)
                return true;

            return null;
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (container?.entityOwner == null || container.entityOwner.IsDestroyed)
                return;

            LootHandler lootHandler = container.entityOwner.GetComponent<LootHandler>();
            if (lootHandler != null)
                lootHandler.OnItemAdded(item);
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (container?.entityOwner == null || container.entityOwner.IsDestroyed)
                return;
            
            LootHandler lootHandler = container.entityOwner.GetComponent<LootHandler>();
            if (lootHandler != null)            
                lootHandler.OnItemRemoved(item);            
        }

        private void OnLootEntityEnd(BasePlayer player, StorageContainer storageContainer)
        {
            LootHandler lootHandler;
            if (_activeSkinBoxes.TryGetValue(player.userID, out lootHandler))            
                UnityEngine.Object.Destroy(lootHandler);            
        }

        private void Unload()
        {
            LootHandler[] lootHandlers = UnityEngine.Object.FindObjectsOfType<LootHandler>();
            for (int i = 0; i < lootHandlers.Length; i++)
            {
                LootHandler lootHandler = lootHandlers[i];
                if (lootHandler.Looter != null)                
                    lootHandler.Looter.EndLooting();                
                UnityEngine.Object.Destroy(lootHandler);
            }

            Configuration = null;
            Instance = null;
        }
        #endregion

        #region Functions
        private void SendAPIMissingWarning()
        {
            Debug.LogWarning("You must enter a Steam API key in the config!\nYou can get a API key here -> https://steamcommunity.com/dev/apikey \nOnce you have your API key copy it to the 'Skin Options/Steam API Key' field in your SkinBox.json config file");
        }

        private void ChatMessage(BasePlayer player, string key, params object[] args)
        {
            if (args == null)
                player.ChatMessage(lang.GetMessage(key, this, player.UserIDString));
            else player.ChatMessage(string.Format(lang.GetMessage(key, this, player.UserIDString), args));
        }

        private void CreateSkinBox(BasePlayer player, DeployableHandler.ReskinTarget reskinTarget)
        {
            StorageContainer container = GameManager.server.CreateEntity(COFFIN_PREFAB, player.transform.position + (Vector3.down * 250f)) as StorageContainer;
            container.limitNetworking = true;
            container.enableSaving = false;

            UnityEngine.Object.Destroy(container.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.Destroy(container.GetComponent<GroundWatch>());

            container.Spawn();

            LootHandler lootHandler;
            if (_activeSkinBoxes.TryGetValue(player.userID, out lootHandler))
            {
                player.EndLooting();
                UnityEngine.Object.Destroy(lootHandler);
            }

            if (reskinTarget != null)
            {
                lootHandler = container.gameObject.AddComponent<DeployableHandler>();
                lootHandler.Looter = player;
                (lootHandler as DeployableHandler).Target = reskinTarget;
            }
            else
            {
                lootHandler = container.gameObject.AddComponent<LootHandler>();
                lootHandler.Looter = player;
            }

            player.inventory.loot.Clear();
            player.inventory.loot.PositionChecks = false;
            player.inventory.loot.entitySource = container;
            player.inventory.loot.itemSource = null;
            player.inventory.loot.MarkDirty();
            player.inventory.loot.AddContainer(container.inventory);
            player.inventory.loot.SendImmediate();

            player.ClientRPCPlayer(null, player, "RPC_OpenLootPanel", LOOT_PANEL);
            container.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                        
            if (Configuration.Cost.Enabled && !permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCost))
            {
                lootHandler.PopupMessage(string.Format(GetMessage("CostToUse", lootHandler.Looter.userID), Configuration.Cost.Deployable, 
                                                                                                           Configuration.Cost.Weapon, 
                                                                                                           Configuration.Cost.Attire,
                                                                                                           GetCostType(player.userID)));
            }

            _activeSkinBoxes[player.userID] = lootHandler;

            ToggleHooks();
        }
        #endregion

        #region Hook Subscriptions
        private void ToggleHooks()
        {
            if (_activeSkinBoxes.Count > 0)
                SubscribeHooks();
            else UnsubscribeHooks();
        }

        private void SubscribeHooks()
        {
            Subscribe(nameof(OnLootEntityEnd));
            Subscribe(nameof(OnItemRemovedFromContainer));
            Subscribe(nameof(OnItemAddedToContainer));
            Subscribe(nameof(CanMoveItem));
            Subscribe(nameof(OnItemSplit));
            Subscribe(nameof(CanAcceptItem));
        }

        private void UnsubscribeHooks()
        {
            Unsubscribe(nameof(OnLootEntityEnd));
            Unsubscribe(nameof(OnItemRemovedFromContainer));
            Unsubscribe(nameof(OnItemAddedToContainer));
            Unsubscribe(nameof(CanMoveItem));
            Unsubscribe(nameof(OnItemSplit));
            Unsubscribe(nameof(CanAcceptItem));
        }
        #endregion

        #region Helpers
        private void GetSkinsFor(BasePlayer player, string shortname, ref List<ulong> list)
        {
            list.Clear();

            List<ulong> skinOverrides = Interface.Call<List<ulong>>("SB_GetSkinOverrides", player, shortname);
            if (skinOverrides != null && skinOverrides.Count > 0)
            {
                list.AddRange(skinOverrides);
                return;
            }

            HashSet<ulong> skins;
            if (_skinList.TryGetValue(shortname, out skins))
            {                
                foreach(ulong skinId in skins)
                {
                    string perm;
                    if (_skinPermissions.TryGetValue(skinId, out perm) && !permission.UserHasPermission(player.UserIDString, perm))
                        continue;

                    if (Configuration.Blacklist.Contains(skinId) && player.net.connection.authLevel < Configuration.Other.BlacklistAuth)
                        continue;

                    list.Add(skinId);
                }
            }
        }

        private bool HasItemPermissions(BasePlayer player, Item item)
        {
            switch (item.info.category)
            {
                case ItemCategory.Weapon:
                case ItemCategory.Tool:
                    return permission.UserHasPermission(player.UserIDString, Configuration.Permission.Weapon);
                case ItemCategory.Construction:                   
                case ItemCategory.Items:
                    return permission.UserHasPermission(player.UserIDString, Configuration.Permission.Deployable);
                case ItemCategory.Attire:
                    return permission.UserHasPermission(player.UserIDString, Configuration.Permission.Attire);
                default:
                    return true;
            }            
        }
        
        private T ParseType<T>(string type)
        {
            try
            {
                return (T)Enum.Parse(typeof(T), type, true);
            }
            catch
            {
                return default(T);
            }
        }

        private double CurrentTime => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;
        #endregion

        #region Usage Costs 
        private string GetCostType(ulong playerId) => GetMessage($"Cost.{_costType}", playerId);

        private int GetReskinCost(Item item)
        {
            if (!Configuration.Cost.Enabled)
                return 0;

            switch (item.info.category)
            {
                case ItemCategory.Weapon:
                case ItemCategory.Tool:
                    return Configuration.Cost.Weapon;
                case ItemCategory.Construction:
                case ItemCategory.Items:
                    return Configuration.Cost.Deployable;
                case ItemCategory.Attire:
                    return Configuration.Cost.Attire;
                default:
                    return 0;
            }
        }

        private bool CanAffordToUse(BasePlayer player, int amount)
        {
            if (!Configuration.Cost.Enabled || amount == 0 || permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCost))
                return true;

            switch (_costType)
            {
                case CostType.Scrap:
                    return player.inventory.GetAmount(SCRAP_ITEM_ID) >= amount;
                case CostType.ServerRewards:
                    return (int)ServerRewards?.Call("CheckPoints", player.userID) >= amount;
                case CostType.Economics:
                    return (double)Economics?.Call("Balance", player.UserIDString) >= amount;                
            }

            return false;
        }

        private bool ChargePlayer(BasePlayer player, ItemCategory itemCategory)
        {
            if (!Configuration.Cost.Enabled || permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCost))
                return true;

            int amount = itemCategory == ItemCategory.Weapon || itemCategory == ItemCategory.Tool ? Configuration.Cost.Weapon :
                         itemCategory == ItemCategory.Items || itemCategory == ItemCategory.Construction ? Configuration.Cost.Deployable :
                         itemCategory == ItemCategory.Attire ? Configuration.Cost.Attire : 0;

            return ChargePlayer(player, amount);
        }

        private bool ChargePlayer(BasePlayer player, int amount)
        {
            if (amount == 0 || !Configuration.Cost.Enabled || permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCost))
                return true;

            switch (_costType)
            {
                case CostType.Scrap:
                    if (amount <= player.inventory.GetAmount(SCRAP_ITEM_ID))
                    {
                        player.inventory.Take(null, SCRAP_ITEM_ID, amount);
                        return true;
                    }
                    return false;
                case CostType.ServerRewards:
                    return (bool)ServerRewards?.Call("TakePoints", player.userID, amount);
                case CostType.Economics:
                    return (bool)Economics?.Call("Withdraw", player.UserIDString, (double)amount);
            }
            return false;
        }
        #endregion

        #region Cooldown
        private void ApplyCooldown(BasePlayer player)
        {
            if (!Configuration.Cooldown.Enabled || permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCooldown))
                return;

            _cooldownTimes[player.userID] = CurrentTime + Configuration.Cooldown.Time;
        }

        private bool IsOnCooldown(BasePlayer player)
        {
            if (!Configuration.Cooldown.Enabled || permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCooldown))
                return false;

            double time;
            if (_cooldownTimes.TryGetValue(player.userID, out time) && time > CurrentTime)
                return true;

            return false;
        }

        private bool IsOnCooldown(BasePlayer player, out double remaining)
        {
            remaining = 0;

            if (!Configuration.Cooldown.Enabled || permission.UserHasPermission(player.UserIDString, Configuration.Permission.NoCooldown))
                return false;

            double time;
            if (_cooldownTimes.TryGetValue(player.userID, out time) && time > CurrentTime)
            {
                remaining = time - CurrentTime;
                return true;
            }
            
            return false;
        }
        #endregion

        #region Approved Skins
        private void AbortApprovedSkinRequest()
        {
            Debug.LogWarning("[SkinBox] - Aborting approved skin processing. Server has not yet downloaded approved manifest. Only workshop skins will be available");
            Steamworks.SteamInventory.OnDefinitionsUpdated -= StartSkinRequest;

            if (!Configuration.Skins.UseWorkshop)
            {
                PrintError("You have workshop skins disabled. This leaves no skins available to use in SkinBox!");
                return;
            }

            VerifyWorkshopSkins();
        }

        private void StartSkinRequest()
        {
            _approvedTimeout?.Destroy();

            Steamworks.SteamInventory.OnDefinitionsUpdated -= StartSkinRequest;

            UpdateWorkshopNameConversionList();

            FindItemRedirects();

            FixLR300InvalidShortname();

            if (!Configuration.Skins.UseApproved && !Configuration.Skins.UseWorkshop)
            {
                PrintError("You have approved skins and workshop skins disabled. This leaves no skins available to use in SkinBox!");
                return;
            }

            if (!Configuration.Skins.UseApproved && Configuration.Skins.UseWorkshop)
            {
                VerifyWorkshopSkins();
                return;
            }

            PrintWarning("Retrieving approved skin lists...");

            CollectApprovedSkins();
        }

        private void CollectApprovedSkins()
        {
            int count = 0;

            bool addApprovedPermission = Configuration.Permission.Approved != Configuration.Permission.Use;

            bool updateConfig = false;

            List<ulong> list = Facepunch.Pool.GetList<ulong>();

            List<int> itemSkinDirectory = Facepunch.Pool.GetList<int>();
            itemSkinDirectory.AddRange(ItemSkinDirectory.Instance.skins.Select(x => x.id));

            foreach (ItemDefinition itemDefinition in ItemManager.itemList)
            {
                list.Clear();                 

                foreach (Steamworks.InventoryDef item in Steamworks.SteamInventory.Definitions)
                {
                    string shortname = item.GetProperty("itemshortname");
                    if (string.IsNullOrEmpty(shortname) || item.Id < 100)
                        continue;
                                        
                    if (_workshopNameToShortname.ContainsKey(shortname))
                        shortname = _workshopNameToShortname[shortname];

                    if (!shortname.Equals(itemDefinition.shortname, StringComparison.OrdinalIgnoreCase))  
                        continue;                    

                    ulong skinId;

                    if (itemSkinDirectory.Contains(item.Id))
                        skinId = (ulong)item.Id;
                    else if (!ulong.TryParse(item.GetProperty("workshopid"), out skinId))
                        continue;

                    if (list.Contains(skinId) || Configuration.Skins.ApprovedLimit > 0 && list.Count >= Configuration.Skins.ApprovedLimit)                    
                        continue;
                    
                    list.Add(skinId);

                    _skinNameLookup[skinId] = item.Name;
                    _skinSearchLookup[skinId] = $"{skinId} {item.Name}";
                }

                if (list.Count > 1)
                {
                    count += list.Count;

                    HashSet<ulong> skins;
                    if (!_skinList.TryGetValue(itemDefinition.shortname, out skins))
                        skins = _skinList[itemDefinition.shortname] = new HashSet<ulong>();

                    int removeCount = 0;

                    list.ForEach((ulong skin) =>
                    {
                        if (Configuration.Skins.RemoveApproved && Configuration.SkinList.ContainsKey(itemDefinition.shortname) && 
                                                                  Configuration.SkinList[itemDefinition.shortname].Contains(skin))
                        {
                            Configuration.SkinList[itemDefinition.shortname].Remove(skin);
                            removeCount++;
                            updateConfig = true;
                        }

                        skins.Add(skin);

                        if (addApprovedPermission)
                            _skinPermissions[skin] = Configuration.Permission.Approved;
                    });

                    if (removeCount > 0)
                        Debug.Log($"[SkinBox] Removed {removeCount} approved skin ID's for {itemDefinition.shortname} from the config skin list");
                }
            }

            if (updateConfig)
                SaveConfig();

            Facepunch.Pool.FreeList(ref list);
            Facepunch.Pool.FreeList(ref itemSkinDirectory);

            Debug.Log($"[SkinBox] - Loaded {count} approved skins");

            if (Configuration.Skins.UseWorkshop && Configuration.SkinList.Sum(x => x.Value.Count) > 0)
                VerifyWorkshopSkins();
            else
            {
                SortSkinLists();

                _skinsLoaded = true;
                Interface.Oxide.CallHook("OnSkinBoxSkinsLoaded", _skinList);
                Debug.Log($"[SkinBox] - SkinBox has loaded all required skins and is ready to use! ({_skinList.Values.Sum(x => x.Count)} skins acrosss {_skinList.Count} items)");
            }
        }

        private void SortSkinLists()
        {
            List<ulong> list = Facepunch.Pool.GetList<ulong>();

            foreach (KeyValuePair<string, HashSet<ulong>> kvp in _skinList)
            {
                list.AddRange(kvp.Value);

                SortSkinList(kvp.Key, ref list);

                kvp.Value.Clear();

                list.ForEach((ulong skinId) => kvp.Value.Add(skinId));

                list.Clear();
            }

            Facepunch.Pool.FreeList(ref list);
        }

        private void SortSkinList(string shortname, ref List<ulong> list)
        {
            if (_sorting == SortBy.Alphabetical)
            {
                list.Sort((ulong a, ulong b) =>
                {
                    string nameA = string.Empty;
                    string nameB = string.Empty;

                    _skinNameLookup.TryGetValue(a, out nameA);
                    _skinNameLookup.TryGetValue(b, out nameB);

                    return nameA.CompareTo(nameB);
                });

                return;
            }
            else
            {
                HashSet<ulong> configList;
                Configuration.SkinList.TryGetValue(shortname, out configList);
                if (configList != null)
                {
                    List<ulong> l = configList.ToList();

                    list.Sort((ulong a, ulong b) =>
                    {
                        int indexA = l.IndexOf(a);
                        int indexB = l.IndexOf(b);

                        return _sorting == SortBy.Config ? indexA.CompareTo(indexB) : indexA.CompareTo(indexB) * -1;
                    });
                }
            }            
        }
        #endregion

        #region Workshop Skins
        private List<ulong> _skinsToVerify = new List<ulong>();
        private Queue<ulong> _collectionsToVerify = new Queue<ulong>();

        private const string PUBLISHED_FILE_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";
        private const string COLLECTION_DETAILS = "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/";
        private const string ITEMS_BODY = "?key={0}&itemcount={1}";
        private const string ITEM_ENTRY = "&publishedfileids[{0}]={1}";
        private const string COLLECTION_BODY = "?key={0}&collectioncount=1&publishedfileids[0]={1}";

        private void VerifyWorkshopSkins()
        {
            foreach (HashSet<ulong> list in Configuration.SkinList.Values)
                _skinsToVerify.AddRange(list);

            SendWorkshopQuery();
        }

        private void SendWorkshopQuery(int page = 0, int success = 0, ConsoleSystem.Arg arg = null, string perm = null)
        {
            int totalPages = Mathf.CeilToInt((float)_skinsToVerify.Count / 100f);
            int index = page * 100;
            int limit = Mathf.Min((page + 1) * 100, _skinsToVerify.Count);
            string details = string.Format(ITEMS_BODY, Configuration.Skins.APIKey, (limit - index));

            for (int i = index; i < limit; i++)
            {
                details += string.Format(ITEM_ENTRY, i - index, _skinsToVerify[i]);
            }

            try
            {
                webrequest.Enqueue(PUBLISHED_FILE_DETAILS, details, (code, response) => ServerMgr.Instance.StartCoroutine(ValidateRequiredSkins(code, response, page + 1, totalPages, success, arg, perm)), this, RequestMethod.POST);
            }
            catch { }
        }

        public enum CollectionAction { AddSkin, RemoveSkin, ExcludeSkin, RemoveExludeSkin }

        private void SendWorkshopCollectionQuery(ulong collectionId, CollectionAction action, int success, ConsoleSystem.Arg arg = null, string perm = null)
        {
            string details = string.Format(COLLECTION_BODY, Configuration.Skins.APIKey, collectionId);

            try
            {
                webrequest.Enqueue(COLLECTION_DETAILS, details, (code, response) => ServerMgr.Instance.StartCoroutine(ProcessCollectionRequest(collectionId, code, response, action, success, arg, perm)), this, RequestMethod.POST);
            }
            catch { }
        }
       
        private IEnumerator ValidateRequiredSkins(int code, string response, int page, int totalPages, int success, ConsoleSystem.Arg arg, string perm)
        {
            bool hasChanged = false;
            int newSkins = 0;
            if (response != null && code == 200)
            {
                QueryResponse queryRespone = JsonConvert.DeserializeObject<QueryResponse>(response);
                if (queryRespone != null && queryRespone.response != null && queryRespone.response.publishedfiledetails?.Length > 0)
                {
                    SendResponse($"Processing workshop response. Page: {page} / {totalPages}", arg);                    

                    foreach (PublishedFileDetails publishedFileDetails in queryRespone.response.publishedfiledetails)
                    {
                        if (publishedFileDetails.tags != null)
                        {
                            foreach (PublishedFileDetails.Tag tag in publishedFileDetails.tags)
                            {                                
                                if (string.IsNullOrEmpty(tag.tag))
                                    continue;

                                ulong workshopid = Convert.ToUInt64(publishedFileDetails.publishedfileid);

                                string adjTag = tag.tag.ToLower().Replace("skin", "").Replace(" ", "").Replace("-", "").Replace(".item", "");
                                if (_workshopNameToShortname.ContainsKey(adjTag))
                                {
                                    string shortname = _workshopNameToShortname[adjTag];

                                    if (shortname == "ammo.snowballgun")
                                        continue;

                                    HashSet<ulong> skins;
                                    if (!_skinList.TryGetValue(shortname, out skins))
                                        skins = _skinList[shortname] = new HashSet<ulong>();

                                    if (!skins.Contains(workshopid))
                                    {                                        
                                        skins.Add(workshopid);
                                        _skinNameLookup[workshopid] = publishedFileDetails.title;
                                        _skinSearchLookup[workshopid] = $"{workshopid} {publishedFileDetails.title}";
                                    }

                                    HashSet<ulong> configSkins;
                                    if (!Configuration.SkinList.TryGetValue(shortname, out configSkins))
                                        configSkins = Configuration.SkinList[shortname] = new HashSet<ulong>();

                                    if (!configSkins.Contains(workshopid))
                                    {
                                        hasChanged = true;
                                        configSkins.Add(workshopid);
                                        newSkins += 1;
                                    }

                                    if (!string.IsNullOrEmpty(perm) && !Configuration.Permission.Custom[perm].Contains(workshopid))
                                    {
                                        hasChanged = true;
                                        Configuration.Permission.Custom[perm].Add(workshopid);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            yield return CoroutineEx.waitForEndOfFrame;
            yield return CoroutineEx.waitForEndOfFrame;

            if (hasChanged)
                SaveConfig();

            if (page < totalPages)
                SendWorkshopQuery(page, success + newSkins);
            else
            {
                if (_collectionsToVerify.Count != 0)
                {
                    SendWorkshopCollectionQuery(_collectionsToVerify.Dequeue(), CollectionAction.AddSkin, success + newSkins, arg, perm);
                    yield break;
                }

                if (!_skinsLoaded)
                {
                    SortSkinLists();

                    _skinsLoaded = true;
                    Interface.Oxide.CallHook("OnSkinBoxSkinsLoaded", _skinList);
                    Debug.Log($"[SkinBox] - SkinBox has loaded all required skins and is ready to use! ({_skinList.Values.Sum(x => x.Count)} skins acrosss {_skinList.Count} items)");
                }
                else SendResponse($"{success + newSkins} new skins have been added!", arg);
            }
        }

        private IEnumerator ProcessCollectionRequest(ulong collectionId, int code, string response, CollectionAction action, int success, ConsoleSystem.Arg arg, string perm)
        {
            if (response != null && code == 200)
            {
                SendResponse($"Processing response for collection {collectionId}", arg);

                CollectionQueryResponse collectionQuery = JsonConvert.DeserializeObject<CollectionQueryResponse>(response);
                if (collectionQuery == null || !(collectionQuery is CollectionQueryResponse))
                {
                    SendResponse("Failed to receive a valid workshop collection response", arg);
                    yield break;
                }

                if (collectionQuery.response.resultcount == 0 || collectionQuery.response.collectiondetails == null ||
                    collectionQuery.response.collectiondetails.Length == 0 || collectionQuery.response.collectiondetails[0].result != 1)
                {
                    SendResponse("Failed to receive a valid workshop collection response", arg);
                    yield break;
                }

                _skinsToVerify.Clear();

                foreach (CollectionChild child in collectionQuery.response.collectiondetails[0].children)
                {
                    try
                    {
                        switch (child.filetype)
                        {
                            case 1:
                                _skinsToVerify.Add(Convert.ToUInt64(child.publishedfileid));
                                break;
                            case 2:
                                _collectionsToVerify.Enqueue(Convert.ToUInt64(child.publishedfileid));
                                SendResponse($"Collection {collectionId} contains linked collection {child.publishedfileid}", arg);
                                break;
                            default:
                                break;
                        }                      
                    }
                    catch { }
                }

                if (_skinsToVerify.Count == 0)
                {
                    if (_collectionsToVerify.Count != 0)
                    {
                        SendWorkshopCollectionQuery(_collectionsToVerify.Dequeue(), action, success, arg, perm);
                        yield break;
                    }

                    SendResponse("No valid skin ID's in the specified collection", arg);
                    yield break;
                }

                switch (action)
                {
                    case CollectionAction.AddSkin:
                        SendWorkshopQuery(0, success, arg, perm);
                        break;

                    case CollectionAction.RemoveSkin:
                        RemoveSkins(arg, perm);
                        break;

                    case CollectionAction.ExcludeSkin:
                        AddSkinExclusions(arg);
                        break;

                    case CollectionAction.RemoveExludeSkin:
                        RemoveSkinExclusions(arg);
                        break;
                }              
            }
            else SendResponse($"[SkinBox] Collection response failed. Error code {code}", arg);
        }

        private void RemoveSkins(ConsoleSystem.Arg arg, string perm = null)
        {
            int removedCount = 0;
            for (int y = _skinList.Count - 1; y >= 0; y--)
            {
                KeyValuePair<string, HashSet<ulong>> skin = _skinList.ElementAt(y);

                for (int i = 0; i < _skinsToVerify.Count; i++)
                {
                    ulong skinId = _skinsToVerify[i];
                    if (skin.Value.Contains(skinId))
                    {
                        skin.Value.Remove(skinId);
                        Configuration.SkinList[skin.Key].Remove(skinId);
                        removedCount++;

                        if (!string.IsNullOrEmpty(perm))
                            Configuration.Permission.Custom[perm].Remove(skinId);
                    }
                }

            }

            if (removedCount > 0)
                SaveConfig();

            SendReply(arg, $"[SkinBox] - Removed {removedCount} skins");

            if (_collectionsToVerify.Count != 0)
                SendWorkshopCollectionQuery(_collectionsToVerify.Dequeue(), CollectionAction.RemoveSkin, 0, arg, perm);                
        }

        private void AddSkinExclusions(ConsoleSystem.Arg arg)
        {
            int count = 0;
            foreach (ulong skinId in _skinsToVerify)
            {
                if (!Configuration.Blacklist.Contains(skinId))
                {
                    Configuration.Blacklist.Add(skinId);
                    count++;
                }
            }

            if (count > 0)
            {
                SendResponse($"Added {count} new skin ID's to the excluded list", arg);
                SaveConfig();
            }
        }

        private void RemoveSkinExclusions(ConsoleSystem.Arg arg)
        {
            int count = 0;
            foreach (ulong skinId in _skinsToVerify)
            {
                if (Configuration.Blacklist.Contains(skinId))
                {
                    Configuration.Blacklist.Remove(skinId);
                    count++;
                }
            }

            if (count > 0)
            {
                SendResponse($"Removed {count} skin ID's from the excluded list", arg);
                SaveConfig();
            }
        }

        private void SendResponse(string message, ConsoleSystem.Arg arg)
        {
            if (arg != null)
                SendReply(arg, message);
            else Debug.Log($"[SkinBox] - {message}");
        }
        #endregion

        #region Workshop Name Conversions
        private Dictionary<string, string> _workshopNameToShortname = new Dictionary<string, string>
        {
            {"longtshirt", "tshirt.long" },
            {"cap", "hat.cap" },
            {"beenie", "hat.beenie" },
            {"boonie", "hat.boonie" },
            {"balaclava", "mask.balaclava" },
            {"pipeshotgun", "shotgun.waterpipe" },
            {"woodstorage", "box.wooden" },
            {"ak47", "rifle.ak" },
            {"bearrug", "rug.bear" },
            {"boltrifle", "rifle.bolt" },
            {"bandana", "mask.bandana" },
            {"hideshirt", "attire.hide.vest" },
            {"snowjacket", "jacket.snow" },
            {"buckethat", "bucket.helmet" },
            {"semiautopistol", "pistol.semiauto" },            
            {"roadsignvest", "roadsign.jacket" },
            {"roadsignpants", "roadsign.kilt" },
            {"burlappants", "burlap.trousers" },
            {"collaredshirt", "shirt.collared" },
            {"mp5", "smg.mp5" },
            {"sword", "salvaged.sword" },
            {"workboots", "shoes.boots" },
            {"vagabondjacket", "jacket" },
            {"hideshoes", "attire.hide.boots" },
            {"deerskullmask", "deer.skull.mask" },
            {"minerhat", "hat.miner" },
            {"lr300", "rifle.lr300" },
            {"lr300.item", "rifle.lr300" },
            {"burlapgloves", "burlap.gloves" },
            {"burlap.gloves", "burlap.gloves"},
            {"leather.gloves", "burlap.gloves"},
            {"python", "pistol.python" },
            {"m39", "rifle.m39" },
            {"l96", "rifle.l96" },
            {"woodendoubledoor", "door.double.hinged.wood" }
        };

        private void UpdateWorkshopNameConversionList()
        {     
            foreach (ItemDefinition item in ItemManager.itemList)
            {
                _shortnameToDisplayname[item.shortname] = item.displayName.english;

                string workshopName = item.displayName.english.ToLower().Replace("skin", "").Replace(" ", "").Replace("-", "");
                
                if (!_workshopNameToShortname.ContainsKey(workshopName))
                    _workshopNameToShortname[workshopName] = item.shortname;

                if (!_workshopNameToShortname.ContainsKey(item.shortname))
                    _workshopNameToShortname[item.shortname] = item.shortname;

                if (!_workshopNameToShortname.ContainsKey(item.shortname.Replace(".", "")))
                    _workshopNameToShortname[item.shortname.Replace(".", "")] = item.shortname;
            }

            foreach (Skinnable skin in Skinnable.All.ToList())
            {
                if (string.IsNullOrEmpty(skin.Name) || string.IsNullOrEmpty(skin.ItemName) || _workshopNameToShortname.ContainsKey(skin.Name.ToLower()))
                    continue;

                _workshopNameToShortname[skin.Name.ToLower()] = skin.ItemName.ToLower();
            }
        }

        private void FixLR300InvalidShortname()
        {
            const string LR300_ITEM = "lr300.item";
            const string LR300 = "rifle.lr300";

            HashSet<ulong> list;
            if (Configuration.SkinList.TryGetValue(LR300_ITEM, out list))
            {
                Configuration.SkinList.Remove(LR300_ITEM);
                Configuration.SkinList[LR300] = list;

                SaveConfig();
            }
        }
        #endregion

        #region Item Skin Redirects
        private Hash<string, string> _itemSkinRedirects = new Hash<string, string>();

        private void FindItemRedirects()
        {            
            bool addApprovedPermission = Configuration.Permission.Approved != Configuration.Permission.Use;

            foreach (ItemSkinDirectory.Skin skin in ItemSkinDirectory.Instance.skins)
            {
                ItemSkin itemSkin = skin.invItem as ItemSkin;
                if (itemSkin == null || itemSkin.Redirect == null)                
                    continue;

                _itemSkinRedirects[itemSkin.Redirect.shortname] = itemSkin.itemDefinition.shortname;

                if (Configuration.Skins.UseRedirected)
                {
                    HashSet<ulong> skins;
                    if (!_skinList.TryGetValue(itemSkin.itemDefinition.shortname, out skins))
                        skins = _skinList[itemSkin.itemDefinition.shortname] = new HashSet<ulong>();

                    skins.Add((ulong)skin.id);

                    _skinNameLookup[(ulong)skin.id] = itemSkin.displayName.english;
                    _skinSearchLookup[(ulong)skin.id] = $"{(ulong)skin.id} {itemSkin.displayName.english}";

                    if (addApprovedPermission)
                        _skinPermissions[(ulong)skin.id] = Configuration.Permission.Approved;
                }
            }
        }

        private string GetRedirectedShortname(string shortname)
        {
            string redirectedName;

            if (_itemSkinRedirects.TryGetValue(shortname, out redirectedName))
                return redirectedName;

            return shortname;
        }
        #endregion

        #region SkinBox Component
        private class DeployableHandler : LootHandler
        {
            internal ReskinTarget Target
            {
                set
                {
                    reskinTarget = value;
                    Populate();
                }
            }

            protected override ItemDefinition Definition => reskinTarget.itemDefinition.isRedirectOf ?? reskinTarget.itemDefinition;

            protected override ulong InputSkin => reskinTarget.entity.skinID;


            private ReskinTarget reskinTarget;

            protected override void Awake()
            {
                _filteredSkins = Facepunch.Pool.GetList<ulong>();

                Entity = GetComponent<StorageContainer>();

                if (!Configuration.Other.AllowStacks)
                {
                    Entity.maxStackSize = 1;
                    Entity.inventory.maxStackSize = 1;
                }

                Entity.SetFlag(BaseEntity.Flags.Open, true, false);
            }

            internal void Populate()
            {
                if (HasItem)
                    return;

                HasItem = true;

                _availableSkins = reskinTarget.skins;
                _availableSkins.Remove(0UL);

                if (InputSkin != 0UL)
                    _availableSkins.Remove(InputSkin);

                _filteredSkins.AddRange(_availableSkins);

                _itemsPerPage = InputSkin == 0UL ? 41 : 40;

                _currentPage = 0;
                _maximumPages = Mathf.Min(Configuration.Skins.MaximumPages, Mathf.CeilToInt((float)_filteredSkins.Count / (float)_itemsPerPage));

                if (_currentPage > 0 || _maximumPages > 1)
                    CreateOverlay();

                CreateSearchBar();

                ClearContainer();

                StartCoroutine(FillContainer());
            }

            internal override void OnItemRemoved(Item item)
            {
                if (!HasItem)
                    return;

                CuiHelper.DestroyUi(Looter, UI_PANEL);
                CuiHelper.DestroyUi(Looter, UI_SEARCH);

                bool skinChanged = item.info != reskinTarget.itemDefinition || (item.skin != InputSkin);
                bool wasSuccess = false;

                if (!skinChanged)
                    goto IGNORE_RESKIN;

                if (skinChanged && !Instance.ChargePlayer(Looter, Definition.category))
                {
                    item.skin = InputSkin;

                    if (item.GetHeldEntity() != null)
                        item.GetHeldEntity().skinID = InputSkin;

                    PopupMessage(string.Format(GetMessage("NotEnoughBalanceTake", Looter.userID), item.info.displayName.english, Instance.GetCostType(Looter.userID)));
                    goto IGNORE_RESKIN;
                }

                string result2 = Interface.Call<string>("SB_CanReskinDeployableWith", Looter, reskinTarget.entity, reskinTarget.itemDefinition, item.skin);
                if (!string.IsNullOrEmpty(result2))
                {
                    PopupMessage(result2);
                    goto IGNORE_RESKIN;
                }

                wasSuccess = ReskinEntity(Looter, reskinTarget.entity, reskinTarget.itemDefinition, item);

                IGNORE_RESKIN:
                Looter.Invoke(()=> DestroyItem(item), 0.2f);

                ClearContainer();

                Entity.inventory.MarkDirty();
                
                HasItem = false;

                reskinTarget = null;

                if (wasSuccess && Configuration.Cooldown.ActivateOnTake)
                    Instance.ApplyCooldown(Looter);

                Looter.EndLooting();
            }

            public static bool ReskinEntity(BasePlayer looter, BaseEntity targetEntity, ItemDefinition defaultDefinition, Item targetItem)
            {
                string reason;
                if (!CanEntityBeRespawned(targetEntity, out reason))
                {
                    Instance.ChatMessage(looter, reason);
                    return false;
                }

                if (defaultDefinition != targetItem.info)
                    return ChangeSkinForRedirectedItem(looter, targetEntity, targetItem);
                return ChangeSkinForExistingItem(targetEntity, targetItem);
            }

            private static bool ChangeSkinForRedirectedItem(BasePlayer looter, BaseEntity targetEntity, Item targetItem)
            {
                string resourcePath;
                if (!GetEntityPrefabPath(targetItem.info, out resourcePath))
                {
                    Instance.ChatMessage(looter, "ReskinError.InvalidResourcePath");
                    return false;
                }

                Vector3 position = targetEntity.transform.position;
                Quaternion rotation = targetEntity.transform.rotation;
                BaseEntity parentEntity = targetEntity.GetParentEntity();

                float health = targetEntity.Health();
                float lastAttackedTime = targetEntity is BaseCombatEntity ? (targetEntity as BaseCombatEntity).lastAttackedTime : 0;

                ulong owner = targetEntity.OwnerID;
                
                float sleepingBagUnlockTime = 0;
                if (targetEntity is SleepingBag)
                    sleepingBagUnlockTime = (targetEntity as SleepingBag).unlockTime;

                Rigidbody rb = targetEntity.GetComponent<Rigidbody>();
                bool isDoor = targetEntity is Door;

                Dictionary<ContainerSet, List<Item>> containerSets = new Dictionary<ContainerSet, List<Item>>();
                SaveEntityStorage(targetEntity, containerSets, 0);

                List<ChildPreserveInfo> list = Facepunch.Pool.GetList<ChildPreserveInfo>();
                if (!isDoor)
                {
                    for (int i = 0; i < targetEntity.children.Count; i++)
                        SaveEntityStorage(targetEntity.children[i], containerSets, i + 1);
                }
                else
                {
                    foreach (BaseEntity child in targetEntity.children)
                    {
                        ChildPreserveInfo childPreserveInfo = new ChildPreserveInfo()
                        {
                            TargetEntity = child,
                            TargetBone = child.parentBone,
                            LocalPosition = child.transform.localPosition,
                            LocalRotation = child.transform.localRotation,
                            Slot = GetEntitySlot(targetEntity, child)
                        };
                        list.Add(childPreserveInfo);
                    }

                    foreach (ChildPreserveInfo childPreserveInfo in list)
                        childPreserveInfo.TargetEntity.SetParent(null, true, false);
                }

                targetEntity.Kill(BaseNetworkable.DestroyMode.None);

                BaseEntity newEntity = GameManager.server.CreateEntity(resourcePath, position, rotation, true);

                if (rb != null)
                {
                    Rigidbody rigidbody;
                    if (newEntity.TryGetComponent<Rigidbody>(out rigidbody) && !rigidbody.isKinematic && rigidbody.useGravity)
                    {
                        rigidbody.useGravity = false;
                        newEntity.Invoke(() => RestoreRigidbody(rigidbody), 0.1f);
                    }
                }

                newEntity.SetParent(parentEntity, false, false);
                newEntity.skinID = targetItem.skin;
                newEntity.OwnerID = owner;

                newEntity.Spawn();

                if (newEntity is BaseCombatEntity)
                {
                    (newEntity as BaseCombatEntity).SetHealth(health);
                    (newEntity as BaseCombatEntity).lastAttackedTime = lastAttackedTime;
                }

                if (newEntity is SleepingBag)
                {
                    sleepingBagUnlockTime = (newEntity as SleepingBag).unlockTime;
                    (newEntity as SleepingBag).deployerUserID = owner;
                }

                if (newEntity is DecayEntity)
                    (newEntity as DecayEntity).AttachToBuilding(null);

                if (containerSets.Count > 0)
                {
                    RestoreEntityStorage(newEntity, 0, containerSets);
                    if (!isDoor)
                    {
                        for (int j = 0; j < newEntity.children.Count; j++)
                            RestoreEntityStorage(newEntity.children[j], j + 1, containerSets);
                    }

                    foreach (KeyValuePair<ContainerSet, List<Item>> containerSet in containerSets)
                    {
                        foreach (Item value in containerSet.Value)
                        {
                            value.Remove(0f);
                        }
                    }
                }

                if (isDoor)
                {
                    foreach (ChildPreserveInfo child in list)
                    {
                        child.TargetEntity.SetParent(newEntity, child.TargetBone, true, false);
                        child.TargetEntity.transform.localPosition = child.LocalPosition;
                        child.TargetEntity.transform.localRotation = child.LocalRotation;
                        child.TargetEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);

                        if (child.Slot >= 0)
                        {
                            newEntity.SetSlot((BaseEntity.Slot)child.Slot, child.TargetEntity);
                        }
                    }
                }

                Facepunch.Pool.FreeList<ChildPreserveInfo>(ref list);

                return true;
            }
                        
            private static bool ChangeSkinForExistingItem(BaseEntity targetEntity, Item targetItem)
            {
                targetEntity.skinID = targetItem.skin;
                targetEntity.SendNetworkUpdateImmediate();

                targetEntity.ClientRPC<int, uint>(null, "Client_ReskinResult", 1, targetEntity.net.ID);
                return true;
            }

            private static int GetEntitySlot(BaseEntity rootEntity, BaseEntity childEntity)
            {
                int count = Enum.GetNames(typeof(BaseEntity.Slot)).Length;

                for (int i = 0; i < count; i++)
                {
                    if (rootEntity.GetSlot((BaseEntity.Slot)i) == childEntity)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private void DestroyItem(Item item)
            {
                if (item == null)
                    return;

                item.RemoveFromContainer();
                item.Remove(0f);
            }

            #region Helpers
            private static RaycastHit raycastHit;

            internal static BaseEntity FindReskinTarget(BasePlayer player)
            {
                const int LAYERS = 1 << 0 | 1 << 8 | 1 << 15 | 1 << 16 | 1 << 21;

                BaseEntity baseEntity = null;

                if (Physics.Raycast(player.eyes.HeadRay(), out raycastHit, 5f, LAYERS, QueryTriggerInteraction.Ignore))                
                    baseEntity = raycastHit.GetEntity();
              
                return baseEntity;
            }

            internal static bool CanEntityBeRespawned(BaseEntity targetEntity, out string reason)
            {
                if (targetEntity == null || targetEntity.IsDestroyed)
                {
                    reason = "ReskinError.TargetNull";
                    return false;
                }

                BaseMountable baseMountable = targetEntity as BaseMountable;
                if (baseMountable != null && baseMountable.IsMounted())
                {
                    reason = "ReskinError.MountBlocked";
                    return false;
                }

                if (targetEntity.isServer)
                {
                    BaseVehicle baseVehicle = targetEntity as BaseVehicle;
                    if (baseVehicle != null && (baseVehicle.HasDriver() || baseVehicle.AnyMounted()))
                    {
                        reason = "ReskinError.MountBlocked";
                        return false;
                    }
                }

                IOEntity ioEntity = targetEntity as IOEntity;
                if (ioEntity != null && (HasIOConnection(ioEntity.inputs) || HasIOConnection(ioEntity.outputs)))
                {
                    reason = "ReskinError.IOConnected";
                    return false;
                }

                reason = null;
                return true;
            }

            private static bool GetEntityPrefabPath(ItemDefinition itemDefinition, out string resourcePath)
            {
                ItemModDeployable itemModDeployable;
                ItemModEntity itemModEntity;
                ItemModEntityReference itemModEntityReference;
                resourcePath = string.Empty;

                if (itemDefinition.TryGetComponent<ItemModDeployable>(out itemModDeployable))
                {
                    resourcePath = itemModDeployable.entityPrefab.resourcePath;
                    return true;
                }

                if (itemDefinition.TryGetComponent<ItemModEntity>(out itemModEntity))
                {
                    resourcePath = itemModEntity.entityPrefab.resourcePath;
                    return true;
                }   
                
                if (itemDefinition.TryGetComponent<ItemModEntityReference>(out itemModEntityReference))
                {
                    resourcePath = itemModEntityReference.entityPrefab.resourcePath;
                    return true;
                }

                return false;
            }

            internal static bool HasIOConnection(IOEntity.IOSlot[] slots)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].connectedTo.Get(true) != null)
                        return true;
                }
                return false;
            }

            internal static bool GetItemDefinitionForEntity(BaseEntity entity, out ItemDefinition itemDefinition, bool useRedirect = true)
            {
                itemDefinition = null;

                BaseCombatEntity baseCombatEntity = entity as BaseCombatEntity;
                if (baseCombatEntity != null)
                {
                    if (baseCombatEntity.pickup.enabled && baseCombatEntity.pickup.itemTarget != null)
                        itemDefinition = baseCombatEntity.pickup.itemTarget;

                    else if (baseCombatEntity.repair.enabled && baseCombatEntity.repair.itemTarget != null)
                        itemDefinition = baseCombatEntity.repair.itemTarget;
                }

                if (useRedirect && itemDefinition != null && itemDefinition.isRedirectOf != null)
                    itemDefinition = itemDefinition.isRedirectOf;

                return itemDefinition != null;
            }

            private static void SaveEntityStorage(BaseEntity baseEntity, Dictionary<ContainerSet, List<Item>> dictionary, int index)
            {
                IItemContainerEntity itemContainerEntity = baseEntity as IItemContainerEntity;

                if (itemContainerEntity != null)
                {
                    ContainerSet containerSet = new ContainerSet() { ContainerIndex = index, PrefabId = index == 0 ? 0 : baseEntity.prefabID };
                  
                    dictionary.Add(containerSet, new List<Item>());

                    while(itemContainerEntity.inventory.itemList.Count > 0)
                    {
                        Item item = itemContainerEntity.inventory.itemList[0];

                        dictionary[containerSet].Add(item);
                        item.RemoveFromContainer();
                    }                    
                }
            }

            private static void RestoreEntityStorage(BaseEntity baseEntity, int index, Dictionary<ContainerSet, List<Item>> copy)
            {
                IItemContainerEntity itemContainerEntity = baseEntity as IItemContainerEntity;
                if (itemContainerEntity != null)
                {
                    ContainerSet containerSet = new ContainerSet() { ContainerIndex = index, PrefabId = index == 0 ? 0 : baseEntity.prefabID };

                    if (copy.ContainsKey(containerSet))
                    {
                        foreach (Item item in copy[containerSet])                        
                            item.MoveToContainer(itemContainerEntity.inventory, -1, true, false);
                        
                        copy.Remove(containerSet);
                    }
                }
            }

            private static void RestoreRigidbody(Rigidbody rb)
            {
                if (rb != null)                
                    rb.useGravity = true;                
            }
            #endregion

            internal class ReskinTarget
            {
                public BaseEntity entity;
                public ItemDefinition itemDefinition;
                public List<ulong> skins;

                public ReskinTarget(BaseEntity entity, ItemDefinition itemDefinition, List<ulong> skins)
                {
                    this.entity = entity;
                    this.itemDefinition = itemDefinition;
                    this.skins = skins;
                }
            }

            private struct ChildPreserveInfo
            {
                public BaseEntity TargetEntity;

                public uint TargetBone;

                public Vector3 LocalPosition;

                public Quaternion LocalRotation;

                public int Slot;
            }

            private struct ContainerSet
            {
                public int ContainerIndex;

                public uint PrefabId;
            }
        }

        private class LootHandler : MonoBehaviour
        {
            public StorageContainer Entity { get; protected set; }

            internal BasePlayer Looter { get; set; }
            

            public bool HasItem { get; protected set; }

            internal int InputAmount => inputItem?.amount ?? 0;

            protected virtual ItemDefinition Definition => inputItem.itemDefinition;

            protected virtual ulong InputSkin => inputItem.skin;


            private InputItem inputItem;


            protected int _currentPage = 0;

            protected int _maximumPages = 0;

            protected int _itemsPerPage;

            protected List<ulong> _availableSkins;

            protected List<ulong> _filteredSkins;

            protected bool _isFillingContainer;


            protected virtual void Awake()
            {                
                _availableSkins = Facepunch.Pool.GetList<ulong>();
                _filteredSkins = Facepunch.Pool.GetList<ulong>();

                Entity = GetComponent<StorageContainer>();

                if (!Configuration.Other.AllowStacks)
                {
                    Entity.maxStackSize = 1;
                    Entity.inventory.maxStackSize = 1;
                }

                Entity.SetFlag(BaseEntity.Flags.Open, true, false);
            }

            private void OnDestroy()
            {
                CuiHelper.DestroyUi(Looter, UI_PANEL);
                CuiHelper.DestroyUi(Looter, UI_SEARCH);

                Instance?._activeSkinBoxes?.Remove(Looter.userID);

                if (HasItem && inputItem != null)
                    Looter.GiveItem(inputItem.Create(), BaseEntity.GiveItemReason.PickedUp);

                Facepunch.Pool.FreeList(ref _availableSkins);
                Facepunch.Pool.FreeList(ref _filteredSkins);

                if (Entity != null && !Entity.IsDestroyed)
                {
                    if (Entity.inventory.itemList.Count > 0)
                        ClearContainer();

                    Entity.Kill(BaseNetworkable.DestroyMode.None);
                }

                Instance?.ToggleHooks();
            }

            internal void OnItemAdded(Item item)
            {
                if (HasItem)
                    return;

                HasItem = true;

                string shortname = Instance.GetRedirectedShortname(item.info.shortname);

                Instance.GetSkinsFor(Looter, shortname, ref _availableSkins);

                _availableSkins.Remove(0UL);

                if (item.skin != 0UL)
                    _availableSkins.Remove(item.skin);

                _filteredSkins.Clear();
                _filteredSkins.AddRange(_availableSkins);

                inputItem = new InputItem(shortname, item);

                _itemsPerPage = InputSkin == 0UL ? 41 : 40;

                _currentPage = 0;
                _maximumPages = Mathf.Min(Configuration.Skins.MaximumPages, Mathf.CeilToInt((float)_filteredSkins.Count / (float)_itemsPerPage));

                if (_currentPage > 0 || _maximumPages > 1)
                    CreateOverlay();

                CreateSearchBar();

                RemoveItem(item);
                ClearContainer();

                StartCoroutine(FillContainer());
            }

            internal virtual void OnItemRemoved(Item item)
            {
                if (!HasItem)
                    return;

                CuiHelper.DestroyUi(Looter, UI_PANEL);
                CuiHelper.DestroyUi(Looter, UI_SEARCH);

                bool skinChanged = item.skin != 0UL && item.skin != InputSkin;
                bool aborted = false;
                inputItem.CloneTo(item);

                if (skinChanged && !Instance.ChargePlayer(Looter, Definition.category))
                {
                    item.skin = InputSkin;

                    if (item.GetHeldEntity() != null)
                        item.GetHeldEntity().skinID = InputSkin;

                    aborted = true;

                    PopupMessage(string.Format(GetMessage("NotEnoughBalanceTake", Looter.userID), item.info.displayName.english, Instance.GetCostType(Looter.userID)));
                }

                string result = Interface.Call<string>("SB_CanReskinItem", Looter, item, item.skin);
                if (!string.IsNullOrEmpty(result))
                {
                    item.skin = InputSkin;

                    if (item.GetHeldEntity() != null)
                        item.GetHeldEntity().skinID = InputSkin;

                    aborted = true;

                    PopupMessage(result);                    
                }

                if (!aborted && (item.skin != 0UL || Configuration.Skins.ShowSkinIDs))
                    Instance._skinNameLookup.TryGetValue(item.skin, out item.name);
                else item.name = inputItem.name;

                item.MarkDirty();

                ClearContainer();

                Entity.inventory.MarkDirty();

                inputItem.Dispose();
                inputItem = null;
                HasItem = false;

                if (Configuration.Cooldown.ActivateOnTake)
                    Instance.ApplyCooldown(Looter);

                if (Instance.IsOnCooldown(Looter))
                    Looter.EndLooting();
            }

            internal void ChangePage(int change)
            {
                if (_isFillingContainer)
                    return;

                _currentPage = Mathf.Clamp(_currentPage + change, 0, _maximumPages);

                StartCoroutine(RefillContainer());
            }

            internal void SetSearchParameters(string s)
            {
                _filteredSkins.Clear();
                if (string.IsNullOrEmpty(s))
                    _filteredSkins.AddRange(_availableSkins);
                else
                {
                    for (int i = 0; i < _availableSkins.Count; i++)
                    {
                        string skinLabel;
                        Instance._skinSearchLookup.TryGetValue(_availableSkins[i], out skinLabel);

                        if (!string.IsNullOrEmpty(skinLabel) && skinLabel.Contains(s, System.Globalization.CompareOptions.OrdinalIgnoreCase))
                            _filteredSkins.Add(_availableSkins[i]);
                    }                    
                }

                _currentPage = 0;
                _maximumPages = Mathf.Min(Configuration.Skins.MaximumPages, Mathf.CeilToInt((float)_filteredSkins.Count / (float)_itemsPerPage));

                if (_currentPage > 0 || _maximumPages > 1)
                    CreateOverlay();
                else CuiHelper.DestroyUi(Looter, UI_PANEL);

                if (string.IsNullOrEmpty(s))
                    CreateSearchBar();

                StartCoroutine(RefillContainer());
            }

            private IEnumerator RefillContainer()
            {
                ClearContainer();

                yield return StartCoroutine(FillContainer());

                if (HasItem)
                    CreateOverlay();
            }

            protected IEnumerator FillContainer()
            {
                _isFillingContainer = true;

                CreateItem(0UL);

                if (InputSkin != 0UL)
                    CreateItem(InputSkin);

                for (int i = _currentPage * _itemsPerPage; i < Mathf.Min(_filteredSkins.Count, (_currentPage + 1) * _itemsPerPage); i++)
                {
                    if (!HasItem)
                        break;

                    CreateItem(_filteredSkins[i]);

                    if (i % 2 == 0)
                        yield return null;
                }

                _isFillingContainer = false;
            }

            protected void ClearContainer()
            {               
                for (int i = Entity.inventory.itemList.Count - 1; i >= 0; i--)
                    RemoveItem(Entity.inventory.itemList[i]);                
            }

            private Item CreateItem(ulong skinId)
            {
                Item item = ItemManager.Create(Definition, 1, skinId);
                item.contents?.SetFlag(ItemContainer.Flag.IsLocked, true);
                item.contents?.SetFlag(ItemContainer.Flag.NoItemInput, true);

                if (skinId != 0UL)
                {
                    Instance._skinNameLookup.TryGetValue(skinId, out item.name);

                    if (Configuration.Skins.ShowSkinIDs && Looter.IsAdmin)                    
                        item.name = $"{item.name} ({skinId})";
                }

                if (!InsertItem(item))
                    item.Remove(0f);
                else item.MarkDirty();

                return item;
            }

            private bool InsertItem(Item item)
            {
                if (Entity.inventory.itemList.Contains(item))
                    return false;

                if (Entity.inventory.IsFull())
                    return false;

                Entity.inventory.itemList.Add(item);
                item.parent = Entity.inventory;

                if (!Entity.inventory.FindPosition(item))
                    return false;

                Entity.inventory.MarkDirty();
                Entity.inventory.onItemAddedRemoved?.Invoke(item, true);

                return true;
            }

            protected void RemoveItem(Item item)
            {
                if (!Entity.inventory.itemList.Contains(item))
                    return;

                Entity.inventory.onPreItemRemove?.Invoke(item);

                Entity.inventory.itemList.Remove(item);
                item.parent = null;

                Entity.inventory.MarkDirty();

                Entity.inventory.onItemAddedRemoved?.Invoke(item, false);

                item.Remove(0f);
            }

            internal void CheckItemHasSplit(Item item) => StartCoroutine(CheckItemHasSplit(item, item.amount)); // Item split dupe solution?

            private IEnumerator CheckItemHasSplit(Item item, int originalAmount)
            {
                yield return null;

                if (item != null && item.amount != originalAmount)
                {
                    int splitAmount = originalAmount - item.amount;
                    Looter.inventory.Take(null, item.info.itemid, splitAmount);
                    item.amount += splitAmount;
                }
            }

            private class InputItem
            {
                public ItemDefinition itemDefinition;
                public string name;
                public int amount;
                public ulong skin;

                public float condition;
                public float maxCondition;

                public int magazineContents;
                public int magazineCapacity;
                public ItemDefinition ammoType;

                public List<InputItem> contents;

                internal InputItem(string shortname, Item item)
                {
                    if (!item.info.shortname.Equals(shortname))
                        itemDefinition = ItemManager.FindItemDefinition(shortname);
                    else itemDefinition = item.info;

                    name = item.name;
                    amount = item.amount;
                    skin = item.skin;

                    if (item.hasCondition)
                    {
                        condition = item.condition;
                        maxCondition = item.maxCondition;
                    }

                    BaseProjectile baseProjectile = item.GetHeldEntity() as BaseProjectile;
                    if (baseProjectile != null)
                    {
                        magazineContents = baseProjectile.primaryMagazine.contents;
                        magazineCapacity = baseProjectile.primaryMagazine.capacity;
                        ammoType = baseProjectile.primaryMagazine.ammoType;
                    }

                    if (item.contents?.itemList?.Count > 0)
                    {
                        contents = Facepunch.Pool.GetList<InputItem>();

                        for (int i = 0; i < item.contents.itemList.Count; i++)
                        {
                            Item content = item.contents.itemList[i];
                            if (content == null)
                                continue;

                            contents.Add(new InputItem(content.info.shortname, content));
                        }
                    }
                }

                internal void Dispose()
                {
                    if (contents != null)
                        Facepunch.Pool.FreeList(ref contents);
                }

                internal Item Create()
                {
                    Item item = ItemManager.Create(itemDefinition, amount, skin);

                    item.name = name;

                    if (item.hasCondition)
                    {
                        item.condition = condition;
                        item.maxCondition = maxCondition;
                    }

                    BaseProjectile baseProjectile = item.GetHeldEntity() as BaseProjectile;
                    if (baseProjectile != null)
                    {
                        baseProjectile.primaryMagazine.contents = magazineContents;
                        baseProjectile.primaryMagazine.capacity = magazineCapacity;
                        baseProjectile.primaryMagazine.ammoType = ammoType;
                    }

                    if (contents?.Count > 0)
                    {
                        for (int i = 0; i < contents.Count; i++)
                        {
                            InputItem content = contents[i];

                            Item attachment = ItemManager.Create(content.itemDefinition, Mathf.Max(content.amount, 1), content.skin);
                            if (attachment != null)
                            {
                                if (attachment.hasCondition)
                                {
                                    attachment.condition = content.condition;
                                    attachment.maxCondition = content.maxCondition;
                                }

                                attachment.MoveToContainer(item.contents, -1, false);
                                attachment.MarkDirty();
                            }
                        }

                        item.contents.MarkDirty();
                    }

                    item.MarkDirty();
                    
                    return item;
                }

                internal void CloneTo(Item item)
                {
                    item.contents?.SetFlag(ItemContainer.Flag.IsLocked, false);
                    item.contents?.SetFlag(ItemContainer.Flag.NoItemInput, false);

                    item.amount = amount;

                    if (item.hasCondition)
                    {
                        item.condition = condition;
                        item.maxCondition = maxCondition;
                    }

                    BaseProjectile baseProjectile = item.GetHeldEntity() as BaseProjectile;
                    if (baseProjectile != null && baseProjectile.primaryMagazine != null)
                    {
                        baseProjectile.primaryMagazine.contents = magazineContents;
                        baseProjectile.primaryMagazine.capacity = magazineCapacity;
                        baseProjectile.primaryMagazine.ammoType = ammoType;
                    }

                    if (contents?.Count > 0)
                    {
                        for (int i = 0; i < contents.Count; i++)
                        {
                            InputItem content = contents[i];

                            Item attachment = ItemManager.Create(content.itemDefinition, content.amount, content.skin);
                            if (attachment.hasCondition)
                            {
                                attachment.condition = content.condition;
                                attachment.maxCondition = content.maxCondition;
                            }

                            attachment.MoveToContainer(item.contents, -1, false);
                            attachment.MarkDirty();
                        }

                        item.contents.MarkDirty();
                    }

                    item.MarkDirty();                    
                }
            }

            #region UI
            protected const string UI_PANEL = "SkinBox_UI";
            protected const string UI_SEARCH = "SkinBox_Search_UI";
            private const string UI_POPUP = "SkinBox_Popup";

            private const string PAGE_COLOR = "0.65 0.65 0.65 0.06";
            private const string PAGE_TEXT_COLOR = "0.7 0.7 0.7 1.0";
            private const string BUTTON_COLOR = "0.75 0.75 0.75 0.2";
            private const string BUTTON_TEXT_COLOR = "0.77 0.68 0.68 1";

            private readonly UI4 Popup = new UI4(0.65f, 0.8f, 0.99f, 0.99f);
            private readonly UI4 Container = new UI4(0.9505f, 0.15f, 0.99f, 0.6f);
            
            private readonly UI4 BackButton = new UI4(0f, 0.7f, 1f, 1f);
            private readonly UI4 Text = new UI4(0f, 0.3f, 1f, 0.7f);
            private readonly UI4 NextButton = new UI4(0f, 0f, 1f, 0.3f);
            private readonly UI4 MagnifyImage = new UI4(0f, 0f, 0.119f, 1f);
            private readonly UI4 SearchText = new UI4(0.125f, 0f, 0.881f, 1f);
            private readonly UI4 SearchClose = new UI4(0.881f, 0f, 1f, 1f);

            protected void CreateOverlay()
            {
                CuiElementContainer container = UI.Container(UI_PANEL, Container);

                UI.Button(container, UI_PANEL, BUTTON_COLOR, "◀", BUTTON_TEXT_COLOR, 50, BackButton, _currentPage > 0 ? "skinbox.pageprev" : "");

                UI.Panel(container, UI_PANEL, PAGE_COLOR, Text);
                UI.Label(container, UI_PANEL, $"{_currentPage + 1}\nof\n{_maximumPages}", PAGE_TEXT_COLOR, 20, Text);

                UI.Button(container, UI_PANEL, BUTTON_COLOR, "▶", BUTTON_TEXT_COLOR, 50, NextButton, (_currentPage + 1) < _maximumPages ? "skinbox.pagenext" : "");

                CuiHelper.DestroyUi(Looter, UI_PANEL);
                CuiHelper.AddUi(Looter, container);
            }
                        
            protected void CreateSearchBar()
            {
                CuiElementContainer container = UI.Container(UI_SEARCH, Configuration.UI.SearchBar);

                UI.Panel(container, UI_SEARCH, BUTTON_COLOR, UI4.Full);
                const string MAGNIFY_ICON = "https://chaoscode.io/oxide/Images/magnifyingglass_sb.png";

                UI.Image(container, UI_SEARCH, MAGNIFY_ICON, MagnifyImage);

                UI.InputField(container, UI_SEARCH, BUTTON_TEXT_COLOR, string.Empty, 15, SearchText, "skinbox.updatesearch");
                
                UI.Button(container, UI_SEARCH, BUTTON_COLOR, "<b>×</b>", BUTTON_TEXT_COLOR, 20, SearchClose, "skinbox.updatesearch");

                CuiHelper.DestroyUi(Looter, UI_SEARCH);
                CuiHelper.AddUi(Looter, container);
            }

            internal void PopupMessage(string message)
            {
                CuiElementContainer container = UI.Container(UI_POPUP, Popup);
             
                UI.Label(container, UI_POPUP, message, BUTTON_TEXT_COLOR, 15, UI4.Full, TextAnchor.UpperRight);

                CuiHelper.DestroyUi(Looter, UI_POPUP);
                CuiHelper.AddUi(Looter, container);

                Looter.Invoke(() => CuiHelper.DestroyUi(Looter, UI_POPUP), 5f);
            }
            #endregion
        }
        #endregion

        #region UI
        public static class UI
        {
            public static CuiElementContainer Container(string panel, UI4 dimensions, string parent = "Hud.Menu")
            {
                CuiElementContainer container = new CuiElementContainer()
                {
                    {
                        new CuiPanel { Image = { Color = "0 0 0 0" }, RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax(),  } },
                        new CuiElement().Parent = parent,
                        panel
                    }
                };
                return container;
            }

            public static void Panel(CuiElementContainer container, string panel, string color, UI4 dimensions)
            {
                container.Add(new CuiPanel
                {
                    Image = { Color = color },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() },
                },
                panel);
            }

            public static void Label(CuiElementContainer container, string panel, string text, string color, int size, UI4 dimensions, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiLabel
                {
                    Text = { FontSize = size, Color = color, Align = align, Text = text },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                },
                panel);

            }

            public static void Button(CuiElementContainer container, string panel, string color, string text, string textColor, int size, UI4 dimensions, string command, TextAnchor align = TextAnchor.MiddleCenter)
            {
                container.Add(new CuiButton
                {
                    Button = { Color = color, Command = command },
                    RectTransform = { AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() },
                    Text = { Text = text, Color = textColor, FontSize = size, Align = align }
                },
                panel);
            }   

            public static void Image(CuiElementContainer container, string panel, string url, UI4 dimensions)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiRawImageComponent { Url = url },
                        new CuiRectTransformComponent {AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }
            
            public static void InputField(CuiElementContainer container, string panel, string textColor, string text, int size, UI4 dimensions, string command)
            {
                container.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = panel,
                    Components =
                    {
                        new CuiInputFieldComponent
                        {
                            Align = TextAnchor.MiddleLeft,
                            CharsLimit = 50,
                            Color = textColor,
                            Command = command + text,
                            FontSize = size,
                            IsPassword = false,
                            Text = text,
                            LineType = UnityEngine.UI.InputField.LineType.SingleLine                            
                        },
                        new CuiRectTransformComponent {AnchorMin = dimensions.GetMin(), AnchorMax = dimensions.GetMax() }
                    }
                });
            }
        }
        public class UI4
        {
            [JsonProperty("X Minimum")]
            public float xMin;

            [JsonProperty("Y Minimum")]
            public float yMin;

            [JsonProperty("X Maximum")]
            public float xMax;

            [JsonProperty("Y Maximum")]
            public float yMax;

            [JsonIgnore]
            public static UI4 Full { get; private set; } = new UI4(0f, 0f, 1f, 1f);

            public UI4(float xMin, float yMin, float xMax, float yMax)
            {
                this.xMin = xMin;
                this.yMin = yMin;
                this.xMax = xMax;
                this.yMax = yMax;
            }

            public string GetMin() => $"{xMin} {yMin}";

            public string GetMax() => $"{xMax} {yMax}";
        }
        #endregion

        #region UI Command
        [ConsoleCommand("skinbox.pagenext")]
        private void cmdPageNext(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null)
                return;

            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null)
                return;

            LootHandler lootHandler;
            if (_activeSkinBoxes.TryGetValue(player.userID, out lootHandler))
                lootHandler.ChangePage(1);            
        }

        [ConsoleCommand("skinbox.pageprev")]
        private void cmdPagePrev(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null)
                return;

            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null)
                return;

            LootHandler lootHandler;
            if (_activeSkinBoxes.TryGetValue(player.userID, out lootHandler))
                lootHandler.ChangePage(-1);
        }

        [ConsoleCommand("skinbox.updatesearch")]
        private void cmdUpdateSearch(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Connection == null)
                return;

            BasePlayer player = arg.Connection?.player as BasePlayer;
            if (player == null)
                return;

            LootHandler lootHandler;
            if (_activeSkinBoxes.TryGetValue(player.userID, out lootHandler))
                lootHandler.SetSearchParameters(arg.GetString(0));
        }        
        #endregion

        #region Chat Commands   
        private bool CanOpenSkinBox(BasePlayer player)
        {
            if (_apiKeyMissing)
            {
                SendAPIMissingWarning();
                ChatMessage(player, "NoAPIKey");
                return false;
            }

            if (!_skinsLoaded)
            {
                ChatMessage(player, "SkinsLoading");
                return false;
            }

            if (player.inventory.loot.IsLooting())
                return false;

            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, Configuration.Permission.Use))
            {
                ChatMessage(player, "NoPermission");
                return false;
            }

            double cooldownRemaining;
            if (IsOnCooldown(player, out cooldownRemaining))
            {
                ChatMessage(player, "CooldownTime", Mathf.RoundToInt((float)cooldownRemaining));
                return false;
            }

            string result = Interface.Call<string>("SB_CanUseSkinBox", player);
            if (!string.IsNullOrEmpty(result))
            {
                ChatMessage(player, result);
                return false;
            }

            return true;
        }
        private void cmdSkinBox(BasePlayer player, string command, string[] args)
        {
            if (!CanOpenSkinBox(player))
                return;
            
            if (!ChargePlayer(player, Configuration.Cost.Open))
            {
                ChatMessage(player, "NotEnoughBalanceOpen", Configuration.Cost.Open, GetCostType(player.userID));
                return;
            }

            if (args.Length > 0)
            {
                ulong targetSkin = 0UL;
                if (ulong.TryParse(args[0], out targetSkin))
                {
                    Item activeItem = player.GetActiveItem();
                    if (activeItem == null)
                    {
                        ChatMessage(player, "NoItemInHands");
                        return;
                    }

                    if (targetSkin == activeItem.skin)
                    {
                        ChatMessage(player, "TargetSkinIsCurrentSkin");
                        return;
                    }

                    List<ulong> skins = Facepunch.Pool.GetList<ulong>();

                    GetSkinsFor(player, activeItem.info.shortname, ref skins);

                    bool contains = skins.Contains(targetSkin);

                    Facepunch.Pool.FreeList(ref skins);

                    if (!contains && targetSkin != 0UL)
                    {
                        ChatMessage(player, "TargetSkinNotInList");
                        return;
                    }

                    bool skinChanged = targetSkin != 0UL && targetSkin != activeItem.skin;

                    if (skinChanged && !ChargePlayer(player, activeItem.info.category))
                    {                        
                        ChatMessage(player, "NotEnoughBalanceTake", activeItem.info.displayName.english, GetCostType(player.userID));
                        return;
                    }

                    string result = Interface.Call<string>("SB_CanReskinItem", player, activeItem, targetSkin);
                    if (!string.IsNullOrEmpty(result))
                    {
                        ChatMessage(player, result);
                        return;
                    }

                    ChatMessage(player, "ApplyingSkin", targetSkin);

                    if (activeItem.skin != 0UL || Configuration.Skins.ShowSkinIDs)
                        Instance._skinNameLookup.TryGetValue(activeItem.skin, out activeItem.name);

                    activeItem.skin = targetSkin;
                    activeItem.MarkDirty();

                    BaseEntity heldEntity = activeItem.GetHeldEntity();
                    if (heldEntity != null)
                    {
                        heldEntity.skinID = targetSkin;
                        heldEntity.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                    }

                    int slot = activeItem.position;
                    activeItem.SetParent(null);
                    activeItem.MarkDirty();

                    timer.Once(0.15f, () =>
                    {
                        if (activeItem == null)
                            return;

                        activeItem.SetParent(player.inventory.containerBelt);
                        activeItem.position = slot;
                        activeItem.MarkDirty();
                    });

                    if (Configuration.Cooldown.ActivateOnTake)
                        Instance.ApplyCooldown(player);
                    
                    return;
                }
            }

            timer.In(0.2f, () => CreateSkinBox(player, null));
        }

        private void cmdDeployedSkinBox(BasePlayer player, string command, string[] args)
        {
            if (!CanOpenSkinBox(player))
                return;

            if (!player.CanBuild())
            {
                ChatMessage(player, "ReskinError.NoAuth");
                return;
            }

            BaseEntity entity = DeployableHandler.FindReskinTarget(player);
            if (entity == null || entity.IsDestroyed)
            {
                ChatMessage(player, "ReskinError.NoTarget");
                return;
            }

            string reason;
            if (!DeployableHandler.CanEntityBeRespawned(entity, out reason))
            {
                ChatMessage(player, reason);
                return;
            }

            ItemDefinition itemDefinition;
            if (!DeployableHandler.GetItemDefinitionForEntity(entity, out itemDefinition, false))
            {
                ChatMessage(player, "NoDefinitionForEntity");
                return;
            }

            string shortname = GetRedirectedShortname(itemDefinition.shortname);
            if (!Configuration.Skins.UseRedirected && !shortname.Equals(itemDefinition.shortname, StringComparison.OrdinalIgnoreCase))
            {
                ChatMessage(player, "RedirectsDisabled");
                return;
            }

            List<ulong> skins = Facepunch.Pool.GetList<ulong>();
            GetSkinsFor(player, shortname, ref skins);

            if (skins.Count == 0)
            {
                ChatMessage(player, "NoSkinsForItem");
                return;
            }

            if (!ChargePlayer(player, Configuration.Cost.Open))
            {
                ChatMessage(player, "NotEnoughBalanceOpen", Configuration.Cost.Open, GetCostType(player.userID));
                return;
            }

            string result = Interface.Call<string>("SB_CanReskinDeployable", player, entity, itemDefinition);
            if (!string.IsNullOrEmpty(result))
            {
                ChatMessage(player, result);
                return;
            }

            if (args.Length > 0)
            {
                ulong targetSkin = 0UL;
                if (ulong.TryParse(args[0], out targetSkin))
                {
                    if (targetSkin == entity.skinID)
                    {
                        ChatMessage(player, "TargetSkinIsCurrentSkin");
                        return;
                    }

                    if (!skins.Contains(targetSkin) && targetSkin != 0UL)
                    {
                        ChatMessage(player, "TargetSkinNotInList");
                        return;
                    }

                    Item item = ItemManager.CreateByName(shortname, 1, targetSkin);

                    bool skinChanged = item.info != itemDefinition || (item.skin != entity.skinID);
                    bool wasSuccess = false;

                    if (!skinChanged)
                        goto IGNORE_RESKIN;

                    if (skinChanged && !ChargePlayer(player, itemDefinition.category))
                    {                        
                        ChatMessage(player, "NotEnoughBalanceTake", item.info.displayName.english, GetCostType(player.userID));
                        goto IGNORE_RESKIN;
                    }

                    string result2 = Interface.Call<string>("SB_CanReskinDeployableWith", player, entity, itemDefinition, item.skin);
                    if (!string.IsNullOrEmpty(result2))
                    {
                        ChatMessage(player, result2);
                        goto IGNORE_RESKIN;
                    }

                    wasSuccess = DeployableHandler.ReskinEntity(player, entity, itemDefinition, item);

                    ChatMessage(player, "ApplyingSkin", targetSkin);

                    IGNORE_RESKIN:
                    item.Remove(0f);

                    if (wasSuccess && Configuration.Cooldown.ActivateOnTake)
                        Instance.ApplyCooldown(player);

                    return;
                }
            }

            timer.In(0.2f, () => CreateSkinBox(player, new DeployableHandler.ReskinTarget(entity, itemDefinition, skins)));
        }
        #endregion
      
        #region Console Commands
        [ConsoleCommand("skinbox.cmds")]
        private void cmdListCmds(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
                return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\n> SkinBox command overview <");

            TextTable textTable = new TextTable();
            textTable.AddColumn("Command");
            textTable.AddColumn("Description");
            textTable.AddRow(new string[] { "skinbox.addskin", "Add one or more skin-id's to the workshop skin list" });
            textTable.AddRow(new string[] { "skinbox.removeskin", "Remove one or more skin-id's from the workshop skin list" });
            textTable.AddRow(new string[] { "skinbox.addvipskin", "Add one or more skin-id's to the workshop skin list for the specified permission" });
            textTable.AddRow(new string[] { "skinbox.removevipskin", "Remove one or more skin-id's from the workshop skin list for the specified permission" });
            textTable.AddRow(new string[] { "skinbox.addexcluded", "Add one or more skin-id's to the exclusion list (for players)" });
            textTable.AddRow(new string[] { "skinbox.removeexcluded", "Remove one or more skin-id's from the exclusion list" });
            textTable.AddRow(new string[] { "skinbox.addcollectionexclusion", "Add a skin collection to the exclusion list (for players)" });
            textTable.AddRow(new string[] { "skinbox.removecollectionexclusion", "Remove a skin collection from the exclusion list (for players)" });
            textTable.AddRow(new string[] { "skinbox.addcollection", "Adds a whole skin-collection to the workshop skin list"});
            textTable.AddRow(new string[] { "skinbox.removecollection", "Removes a whole collection from the workshop skin list" });
            textTable.AddRow(new string[] { "skinbox.addvipcollection", "Adds a whole skin-collection to the workshop skin list for the specified permission" });
            textTable.AddRow(new string[] { "skinbox.removevipcollection", "Removes a whole collection from the workshop skin list for the specified permission" });

            sb.AppendLine(textTable.ToString());
            SendReply(arg, sb.ToString());
        }

        #region Add/Remove Skins
        [ConsoleCommand("skinbox.addskin")]
        private void consoleAddSkin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in one or more workshop skin ID's");
                return;
            }

            _skinsToVerify.Clear();

            for (int i = 0; i < arg.Args.Length; i++)
            {
                ulong fileId = 0uL;
                if (!ulong.TryParse(arg.Args[i], out fileId))
                {
                    SendReply(arg, $"Ignored '{arg.Args[i]}' as it's not a number");
                    continue;
                }
                else
                {
                    if (arg.Args[i].Length < 9 || arg.Args[i].Length > 10)
                    {
                        SendReply(arg, $"Ignored '{arg.Args[i]}' as it is not the correct length (9 - 10 digits)");
                        continue;
                    }

                    _skinsToVerify.Add(fileId);
                }
            }

            if (_skinsToVerify.Count > 0)
                SendWorkshopQuery(0, 0, arg);
            else SendReply(arg, "No valid skin ID's were entered");
        }

        [ConsoleCommand("skinbox.removeskin")]
        private void consoleRemoveSkin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in one or more workshop skin ID's");
                return;
            }

            _skinsToVerify.Clear();

            for (int i = 0; i < arg.Args.Length; i++)
            {
                ulong fileId = 0uL;
                if (!ulong.TryParse(arg.Args[i], out fileId))
                {
                    SendReply(arg, $"Ignored '{arg.Args[i]}' as it's not a number");
                    continue;
                }
                else
                {
                    if (arg.Args[i].Length < 9 || arg.Args[i].Length > 10)
                    {
                        SendReply(arg, $"Ignored '{arg.Args[i]}' as it is not the correct length (9 - 10 digits)");
                        continue;
                    }

                    _skinsToVerify.Add(fileId);
                }
            }

            if (_skinsToVerify.Count > 0)
                RemoveSkins(arg);
            else SendReply(arg, "No valid skin ID's were entered");
        }

        [ConsoleCommand("skinbox.addvipskin")]
        private void consoleAddVIPSkin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                SendReply(arg, "You need to type in a permission and one or more workshop skin ID's");
                return;
            }

            string perm = arg.Args[0];
            if (!Configuration.Permission.Custom.ContainsKey(perm))
            {
                SendReply(arg, $"The permission {perm} does not exist in the custom permission section of the config");
                return;
            }

            _skinsToVerify.Clear();
            
            for (int i = 1; i < arg.Args.Length; i++)
            {
                ulong fileId = 0uL;
                if (!ulong.TryParse(arg.Args[i], out fileId))
                {
                    SendReply(arg, $"Ignored '{arg.Args[i]}' as it's not a number");
                    continue;
                }
                else
                {
                    if (arg.Args[i].Length < 9 || arg.Args[i].Length > 10)
                    {
                        SendReply(arg, $"Ignored '{arg.Args[i]}' as it is not the correct length (9 - 10 digits)");
                        continue;
                    }

                    _skinsToVerify.Add(fileId);
                }
            }

            if (_skinsToVerify.Count > 0)
                SendWorkshopQuery(0, 0, arg, perm);
            else SendReply(arg, "No valid skin ID's were entered");
        }

        [ConsoleCommand("skinbox.removevipskin")]
        private void consoleRemoveVIPSkin(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                SendReply(arg, "You need to type in a permission and one or more workshop skin ID's");
                return;
            }

            string perm = arg.Args[0];
            if (!Configuration.Permission.Custom.ContainsKey(perm))
            {
                SendReply(arg, $"The permission {perm} does not exist in the custom permission section of the config");
                return;
            }

            _skinsToVerify.Clear();

            for (int i = 1; i < arg.Args.Length; i++)
            {
                ulong fileId = 0uL;
                if (!ulong.TryParse(arg.Args[i], out fileId))
                {
                    SendReply(arg, $"Ignored '{arg.Args[i]}' as it's not a number");
                    continue;
                }
                else
                {
                    if (arg.Args[i].Length < 9 || arg.Args[i].Length > 10)
                    {
                        SendReply(arg, $"Ignored '{arg.Args[i]}' as it is not the correct length (9 - 10 digits)");
                        continue;
                    }

                    _skinsToVerify.Add(fileId);
                }
            }

            if (_skinsToVerify.Count > 0)
                RemoveSkins(arg, perm);
            else SendReply(arg, "No valid skin ID's were entered");
        }

        [ConsoleCommand("skinbox.validatevipskins")]
        private void consoleValidateVIPSkins(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            _skinsToVerify.Clear();

            foreach (List<ulong> list in Configuration.Permission.Custom.Values)
            {
                foreach (ulong skinId in list)
                {
                    if (skinId == 0UL)
                        continue;

                    ItemSkinDirectory.Skin skin = ItemSkinDirectory.Instance.skins.FirstOrDefault<ItemSkinDirectory.Skin>((ItemSkinDirectory.Skin x) => x.id == (int)skinId);
                    if (skin.invItem != null)
                        continue;

                    if (!Configuration.SkinList.Values.Any(x => x.Contains(skinId)))
                        _skinsToVerify.Add(skinId);
                }
            }

            if (_skinsToVerify.Count > 0)
            {
                SendReply(arg, $"Found {_skinsToVerify.Count} permission based skin IDs that are not in the skin list. Sending workshop request");
                SendWorkshopQuery(0, 0, arg);
            }
            else SendReply(arg, "No permission based skin ID's are missing from the skin list");
        }
        #endregion

        #region Add/Remove Collections
        [ConsoleCommand("skinbox.addcollection")]
        private void consoleAddCollection(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in a skin collection ID");
                return;
            }

            ulong collectionId = 0uL;
            if (!ulong.TryParse(arg.Args[0], out collectionId))
            {
                SendReply(arg, $"{arg.Args[0]} is an invalid collection ID");
                return;
            }

            SendWorkshopCollectionQuery(collectionId, CollectionAction.AddSkin, 0, arg);
        }

        [ConsoleCommand("skinbox.removecollection")]
        private void consoleRemoveCollection(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in a skin collection ID");
                return;
            }

            ulong collectionId = 0uL;
            if (!ulong.TryParse(arg.Args[0], out collectionId))
            {
                SendReply(arg, $"{arg.Args[0]} is an invalid collection ID");
                return;
            }

            SendWorkshopCollectionQuery(collectionId, CollectionAction.RemoveSkin, 0, arg);
        }

        [ConsoleCommand("skinbox.addvipcollection")]
        private void consoleAddVIPCollection(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                SendReply(arg, "You need to type in a permission and one or more workshop skin ID's");
                return;
            }

            string perm = arg.Args[0];
            if (!Configuration.Permission.Custom.ContainsKey(perm))
            {
                SendReply(arg, $"The permission {perm} does not exist in the custom permission section of the config");
                return;
            }

            ulong collectionId = 0uL;
            if (!ulong.TryParse(arg.Args[1], out collectionId))
            {
                SendReply(arg, $"{arg.Args[1]} is an invalid collection ID");
                return;
            }

            SendWorkshopCollectionQuery(collectionId, CollectionAction.AddSkin, 0, arg, perm);
        }

        [ConsoleCommand("skinbox.removevipcollection")]
        private void consoleRemoveVIPCollection(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 2)
            {
                SendReply(arg, "You need to type in a permission and one or more workshop skin ID's");
                return;
            }

            string perm = arg.Args[0];
            if (!Configuration.Permission.Custom.ContainsKey(perm))
            {
                SendReply(arg, $"The permission {perm} does not exist in the custom permission section of the config");
                return;
            }

            ulong collectionId = 0uL;
            if (!ulong.TryParse(arg.Args[1], out collectionId))
            {
                SendReply(arg, $"{arg.Args[1]} is an invalid collection ID");
                return;
            }

            SendWorkshopCollectionQuery(collectionId, CollectionAction.RemoveSkin, 0, arg, perm);
        }
        #endregion

        #region Blacklisted Skins
        [ConsoleCommand("skinbox.addexcluded")]
        private void consoleAddExcluded(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in one or more skin ID's");
                return;
            }

            int count = 0;

            for (int i = 0; i < arg.Args.Length; i++)
            {
                ulong skinId = 0uL;
                if (!ulong.TryParse(arg.Args[i], out skinId))
                {
                    SendReply(arg, $"Ignored '{arg.Args[i]}' as it's not a number");
                    continue;
                }
                else
                {
                    Configuration.Blacklist.Add(skinId);
                    count++;
                }
            }

            if (count > 0)
            {
                SaveConfig();
                SendReply(arg, $"Blacklisted {count} skin ID's");
            }
            else SendReply(arg, "No skin ID's were added to the blacklist");
        }

        [ConsoleCommand("skinbox.removeexcluded")]
        private void consoleRemoveExcluded(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in one or more skin ID's");
                return;
            }

            int count = 0;

            for (int i = 0; i < arg.Args.Length; i++)
            {
                ulong skinId = 0uL;
                if (!ulong.TryParse(arg.Args[i], out skinId))
                {
                    SendReply(arg, $"Ignored '{arg.Args[i]}' as it's not a number");
                    continue;
                }
                else
                {
                    if (Configuration.Blacklist.Contains(skinId))
                    {
                        Configuration.Blacklist.Remove(skinId);
                        SendReply(arg, $"The skin ID {skinId} is not on the blacklist");
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                SaveConfig();
                SendReply(arg, $"Removed {count} skin ID's from the blacklist");
            }
            else SendReply(arg, "No skin ID's were removed from the blacklist");
        }

        [ConsoleCommand("skinbox.addcollectionexclusion")]
        private void consoleAddCollectionExclusion(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in a collection ID");
                return;
            }

            ulong collectionId = 0uL;
            if (!ulong.TryParse(arg.Args[0], out collectionId))
            {
                SendReply(arg, $"{arg.Args[0]} is an invalid collection ID");
                return;
            }

            SendWorkshopCollectionQuery(collectionId, CollectionAction.ExcludeSkin, 0, arg);
        }

        [ConsoleCommand("skinbox.removecollectionexclusion")]
        private void consoleRemoveCollectionExclusion(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !permission.UserHasPermission(arg.Connection.userid.ToString(), Configuration.Permission.Admin))
            {
                SendReply(arg, "You do not have permission to use this command");
                return;
            }

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                SendReply(arg, "You need to type in a collection ID");
                return;
            }

            ulong collectionId = 0uL;
            if (!ulong.TryParse(arg.Args[0], out collectionId))
            {
                SendReply(arg, $"{arg.Args[0]} is an invalid collection ID");
                return;
            }

            SendWorkshopCollectionQuery(collectionId, CollectionAction.RemoveExludeSkin, 0, arg);
        }
        #endregion

        [ConsoleCommand("skinbox.open")]
        private void consoleSkinboxOpen(ConsoleSystem.Arg arg)
        {
            if (arg == null)
                return;

            if (!_skinsLoaded)
            {
                SendReply(arg, "SkinBox is still loading skins. Please wait");
                return;
            }

            if (arg.Connection == null)
            {
                if (arg.Args == null || arg.Args.Length == 0)
                {
                    SendReply(arg, "This command requires a Steam ID of the target user");
                    return;
                }

                ulong targetUserID = 0uL;
                if (!ulong.TryParse(arg.Args[0], out targetUserID) || !Oxide.Core.ExtensionMethods.IsSteamId(targetUserID))
                {
                    SendReply(arg, "Invalid Steam ID entered");
                    return;
                }

                BasePlayer targetPlayer = BasePlayer.FindByID(targetUserID);
                if (targetPlayer == null || !targetPlayer.IsConnected)
                {
                    SendReply(arg, $"Unable to find a player with the specified Steam ID");
                    return;
                }

                if (targetPlayer.IsDead())
                {
                    SendReply(arg, $"The specified player is currently dead");
                    return;
                }

                if (!targetPlayer.inventory.loot.IsLooting())
                    CreateSkinBox(targetPlayer, null);
            }
            else if (arg.Connection != null && arg.Connection.player != null)
            {
                BasePlayer player = arg.Player();

                cmdSkinBox(player, string.Empty, Array.Empty<string>());
            }
        }
        #endregion

        #region API
        private bool IsSkinBoxPlayer(ulong playerId) => _activeSkinBoxes.ContainsKey(playerId);
        #endregion

        #region Config        
        private static ConfigData Configuration;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Skin Options")]
            public SkinOptions Skins { get; set; }

            [JsonProperty(PropertyName = "Cooldown Options")]
            public CooldownOptions Cooldown { get; set; }

            [JsonProperty(PropertyName = "Command Options")]
            public CommandOptions Command { get; set; }

            [JsonProperty(PropertyName = "Permission Options")]
            public PermissionOptions Permission { get; set; }

            [JsonProperty(PropertyName = "Usage Cost Options")]
            public CostOptions Cost { get; set; }

            [JsonProperty(PropertyName = "Other Options")]
            public OtherOptions Other { get; set; }

            [JsonProperty(PropertyName = "UI Options")]
            public UIOptions UI { get; set; }

            [JsonProperty(PropertyName = "Imported Workshop Skins")]
            public Hash<string, HashSet<ulong>> SkinList { get; set; }

            [JsonProperty(PropertyName = "Blacklisted Skin ID's")]
            public HashSet<ulong> Blacklist { get; set; }

            public class SkinOptions
            {
                [JsonProperty(PropertyName = "Maximum number of approved skins allowed for each item (-1 is unlimited)")]
                public int ApprovedLimit { get; set; }

                [JsonProperty(PropertyName = "Maximum number of pages viewable")]
                public int MaximumPages { get; set; }

                [JsonProperty(PropertyName = "Include approved skins")]
                public bool UseApproved { get; set; }

                [JsonProperty(PropertyName = "Approved skin timeout (seconds)")]
                public int ApprovedTimeout { get; set; }

                [JsonProperty(PropertyName = "Include manually imported workshop skins")]
                public bool UseWorkshop { get; set; }

                [JsonProperty(PropertyName = "Remove approved skin ID's from config workshop skin list")]
                public bool RemoveApproved { get; set; }

                [JsonProperty(PropertyName = "Include redirected skins")]
                public bool UseRedirected { get; set; }

                [JsonProperty(PropertyName = "Show skin ID's in the name for admins")]
                public bool ShowSkinIDs { get; set; }

                [JsonProperty(PropertyName = "Skin list order (Config, ConfigReversed, Alphabetical)")]
                public string Sorting { get; set; }

                [JsonProperty(PropertyName = "Steam API key for workshop skins (https://steamcommunity.com/dev/apikey)")]
                public string APIKey { get; set; }
            }

            public class CooldownOptions
            {
                [JsonProperty(PropertyName = "Enable cooldowns")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "Cooldown time start's when a item is removed from the box")]
                public bool ActivateOnTake { get; set; }

                [JsonProperty(PropertyName = "Length of cooldown time (seconds)")]
                public int Time { get; set; }
            }

            public class PermissionOptions
            {
                [JsonProperty(PropertyName = "Permission required to use SkinBox")]
                public string Use { get; set; }

                [JsonProperty(PropertyName = "Permission required to reskin deployed items")]
                public string UseDeployed { get; set; }

                [JsonProperty(PropertyName = "Permission required to use admin functions")]
                public string Admin { get; set; }

                [JsonProperty(PropertyName = "Permission that bypasses usage costs")]
                public string NoCost { get; set; }

                [JsonProperty(PropertyName = "Permission that bypasses usage cooldown")]
                public string NoCooldown { get; set; }

                [JsonProperty(PropertyName = "Permission required to skin weapons")]
                public string Weapon { get; set; }

                [JsonProperty(PropertyName = "Permission required to skin deployables")]
                public string Deployable { get; set; }

                [JsonProperty(PropertyName = "Permission required to skin attire")]
                public string Attire { get; set; }

                [JsonProperty(PropertyName = "Permission required to view approved skins")]
                public string Approved { get; set; }

                [JsonProperty(PropertyName = "Custom permissions per skin")]
                public Hash<string, List<ulong>> Custom { get; set; }

                public void RegisterPermissions(Permission permission, Plugin plugin)
                {
                    permission.RegisterPermission(Use, plugin);

                    if (!permission.PermissionExists(UseDeployed, plugin))
                        permission.RegisterPermission(UseDeployed, plugin);

                    if (!permission.PermissionExists(Admin, plugin))
                        permission.RegisterPermission(Admin, plugin);

                    if (!permission.PermissionExists(NoCost, plugin))
                        permission.RegisterPermission(NoCost, plugin);

                    if (!permission.PermissionExists(NoCooldown, plugin))
                        permission.RegisterPermission(NoCooldown, plugin);

                    if (!permission.PermissionExists(Weapon, plugin))
                        permission.RegisterPermission(Weapon, plugin);

                    if (!permission.PermissionExists(Deployable, plugin))
                        permission.RegisterPermission(Deployable, plugin);

                    if (!permission.PermissionExists(Attire, plugin))
                        permission.RegisterPermission(Attire, plugin);

                    if (!permission.PermissionExists(Approved, plugin))
                        permission.RegisterPermission(Approved, plugin);

                    foreach (string perm in Custom.Keys)
                    {
                        if (!permission.PermissionExists(perm, plugin))
                            permission.RegisterPermission(perm, plugin);
                    }
                }

                public void ReverseCustomSkinPermissions(ref Hash<ulong, string> list)
                {
                    foreach (KeyValuePair<string, List<ulong>> kvp in Custom)
                    {
                        for (int i = 0; i < kvp.Value.Count; i++)
                        {
                            list[kvp.Value[i]] = kvp.Key;
                        }
                    }
                }
            }

            public class CommandOptions
            {
                [JsonProperty(PropertyName = "Commands to open the SkinBox")]
                public string[] Commands { get; set; }

                [JsonProperty(PropertyName = "Commands to open the deployed item SkinBox")]
                public string[] DeployedCommands { get; set; }

                internal void RegisterCommands(Game.Rust.Libraries.Command cmd, Plugin plugin)
                {
                    for (int i = 0; i < Commands.Length; i++)                    
                        cmd.AddChatCommand(Commands[i], plugin, "cmdSkinBox");

                    for (int i = 0; i < DeployedCommands.Length; i++)
                        cmd.AddChatCommand(DeployedCommands[i], plugin, "cmdDeployedSkinBox");
                }
            }

            public class CostOptions
            {
                [JsonProperty(PropertyName = "Enable usage costs")]
                public bool Enabled { get; set; }

                [JsonProperty(PropertyName = "Currency used for usage costs (Scrap, Economics, ServerRewards)")]
                public string Currency { get; set; }

                [JsonProperty(PropertyName = "Cost to open the SkinBox")]
                public int Open { get; set; }

                [JsonProperty(PropertyName = "Cost to skin deployables")]
                public int Deployable { get; set; }

                [JsonProperty(PropertyName = "Cost to skin attire")]
                public int Attire { get; set; }

                [JsonProperty(PropertyName = "Cost to skin weapons")]
                public int Weapon { get; set; }
            }  
            
            public class OtherOptions
            {
                [JsonProperty(PropertyName = "Allow stacked items")]
                public bool AllowStacks { get; set; }

                [JsonProperty(PropertyName = "Auth-level required to view blacklisted skins")]
                public int BlacklistAuth { get; set; }
            }

            public class UIOptions
            {
                [JsonProperty(PropertyName = "Search bar dimensions")]
                public UI4 SearchBar { get; set; }
            }
                        
            public Oxide.Core.VersionNumber Version { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Configuration = Config.ReadObject<ConfigData>();

            if (Configuration.Version < Version)
                UpdateConfigValues();

            Config.WriteObject(Configuration, true);
        }

        protected override void LoadDefaultConfig() => Configuration = GetBaseConfig();

        private ConfigData GetBaseConfig()
        {
            return new ConfigData
            {
                Skins = new ConfigData.SkinOptions
                {
                    APIKey = string.Empty,
                    ApprovedLimit = -1,
                    MaximumPages = 3,
                    UseApproved = true,
                    ApprovedTimeout = 180,
                    RemoveApproved = false,
                    UseRedirected = true,
                    UseWorkshop = true,
                    ShowSkinIDs = true,
                    Sorting = "Config"
                },
                Command = new ConfigData.CommandOptions
                {
                    Commands = new string[] { "skinbox", "sb" },
                    DeployedCommands = new string[] { "skindeployed", "sd" }
                },
                Permission = new ConfigData.PermissionOptions
                {
                    Admin = "skinbox.admin",
                    NoCost = "skinbox.ignorecost",
                    NoCooldown = "skinbox.ignorecooldown",
                    Use = "skinbox.use",
                    UseDeployed = "skinbox.use",
                    Approved = "skinbox.use",
                    Attire = "skinbox.use",
                    Deployable = "skinbox.use",
                    Weapon = "skinbox.use",                    
                    Custom = new Hash<string, List<ulong>>
                    {
                        ["skinbox.example1"] = new List<ulong>() { 9990, 9991, 9992 },
                        ["skinbox.example2"] = new List<ulong>() { 9993, 9994, 9995 },
                        ["skinbox.example3"] = new List<ulong>() { 9996, 9997, 9998 }
                    }
                },
                Cooldown = new ConfigData.CooldownOptions
                {
                    Enabled = false,
                    ActivateOnTake = true,
                    Time = 60
                },
                Cost = new ConfigData.CostOptions
                {
                    Enabled = false,
                    Currency = "Scrap",
                    Open = 5,
                    Weapon = 30,
                    Attire = 20,
                    Deployable = 10
                },
                Other = new ConfigData.OtherOptions
                {
                    AllowStacks = false,
                    BlacklistAuth = 2,
                },
                UI = new ConfigData.UIOptions
                {
                    SearchBar = new UI4(0.654f, 0.022f, 0.83f, 0.056f)
                },
                SkinList = new Hash<string, HashSet<ulong>>(),
                Blacklist = new HashSet<ulong>(),
                Version = Version
            };
        }

        protected override void SaveConfig() => Config.WriteObject(Configuration, true);

        private void UpdateConfigValues()
        {
            PrintWarning("Config update detected! Updating config values...");

            ConfigData baseConfig = GetBaseConfig();

            if (Configuration.Version < new VersionNumber(2, 0, 0))
                Configuration = baseConfig;

            if (Configuration.Version < new VersionNumber(2, 0, 10))
            {
                Configuration.Skins.Sorting = "Config";
                Configuration.Skins.ShowSkinIDs = true;
            }

            if (Configuration.Version < new VersionNumber(2, 1, 0))
            {
                Configuration.Permission.UseDeployed = baseConfig.Permission.UseDeployed;
                Configuration.Command.DeployedCommands = baseConfig.Command.DeployedCommands;
            }

            if (Configuration.Version < new VersionNumber(2, 1, 3))
                Configuration.Skins.ApprovedTimeout = 90;

            if (Configuration.Version < new VersionNumber(2, 1, 8))
                Configuration.UI = baseConfig.UI;

            if (Configuration.Version < new VersionNumber(2, 1, 9) && Configuration.Skins.ApprovedTimeout == 90)
                Configuration.Skins.ApprovedTimeout = 180;

            Configuration.Version = Version;
            PrintWarning("Config update completed!");
        }

        #endregion

        #region JSON Deserialization        
        public class QueryResponse
        {
            public Response response;
        }

        public class Response
        {
            public int total;
            public PublishedFileDetails[] publishedfiledetails;
        }

        public class PublishedFileDetails
        {
            public int result;
            public string publishedfileid;
            public string creator;
            public int creator_appid;
            public int consumer_appid;
            public int consumer_shortcutid;
            public string filename;
            public string file_size;
            public string preview_file_size;
            public string file_url;
            public string preview_url;
            public string url;
            public string hcontent_file;
            public string hcontent_preview;
            public string title;
            public string file_description;
            public int time_created;
            public int time_updated;
            public int visibility;
            public int flags;
            public bool workshop_file;
            public bool workshop_accepted;
            public bool show_subscribe_all;
            public int num_comments_public;
            public bool banned;
            public string ban_reason;
            public string banner;
            public bool can_be_deleted;
            public string app_name;
            public int file_type;
            public bool can_subscribe;
            public int subscriptions;
            public int favorited;
            public int followers;
            public int lifetime_subscriptions;
            public int lifetime_favorited;
            public int lifetime_followers;
            public string lifetime_playtime;
            public string lifetime_playtime_sessions;
            public int views;
            public int num_children;
            public int num_reports;
            public Preview[] previews;
            public Tag[] tags;
            public int language;
            public bool maybe_inappropriate_sex;
            public bool maybe_inappropriate_violence;

            public class Tag
            {
                public string tag;
                public bool adminonly;
            }

        }

        public class PublishedFileQueryResponse
        {
            public FileResponse response { get; set; }
        }

        public class FileResponse
        {
            public int result { get; set; }
            public int resultcount { get; set; }
            public PublishedFileQueryDetail[] publishedfiledetails { get; set; }
        }

        public class PublishedFileQueryDetail
        {
            public string publishedfileid { get; set; }
            public int result { get; set; }
            public string creator { get; set; }
            public int creator_app_id { get; set; }
            public int consumer_app_id { get; set; }
            public string filename { get; set; }
            public int file_size { get; set; }
            public string preview_url { get; set; }
            public string hcontent_preview { get; set; }
            public string title { get; set; }
            public string description { get; set; }
            public int time_created { get; set; }
            public int time_updated { get; set; }
            public int visibility { get; set; }
            public int banned { get; set; }
            public string ban_reason { get; set; }
            public int subscriptions { get; set; }
            public int favorited { get; set; }
            public int lifetime_subscriptions { get; set; }
            public int lifetime_favorited { get; set; }
            public int views { get; set; }
            public Tag[] tags { get; set; }

            public class Tag
            {
                public string tag { get; set; }
            }
        }

        public class Preview
        {
            public string previewid;
            public int sortorder;
            public string url;
            public int size;
            public string filename;
            public int preview_type;
            public string youtubevideoid;
            public string external_reference;
        }


        public class CollectionQueryResponse
        {
            public CollectionResponse response { get; set; }
        }

        public class CollectionResponse
        {
            public int result { get; set; }
            public int resultcount { get; set; }
            public CollectionDetails[] collectiondetails { get; set; }
        }

        public class CollectionDetails
        {
            public string publishedfileid { get; set; }
            public int result { get; set; }
            public CollectionChild[] children { get; set; }
        }

        public class CollectionChild
        {
            public string publishedfileid { get; set; }
            public int sortorder { get; set; }
            public int filetype { get; set; }
        }

        #endregion
    }
}
