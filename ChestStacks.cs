using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ChestStacks", "MON@H", "1.4.5")]
    [Description("Higher stack sizes in storage containers.")]

    public class ChestStacks : RustPlugin //Hobobarrel_static, item_drop
    {
        #region Variables

        [PluginReference] private RustPlugin WeightSystem;
        private readonly Hash<ulong, float> _multipliersCache = new Hash<ulong, float>();
        private uint _playerPrefabID;
        private uint _backpackPrefabID;

        #endregion Variables

        #region Initialization

        private void Init()
        {
            Unsubscribe(nameof(CanMoveItem));
            Unsubscribe(nameof(OnItemDropped));
            Unsubscribe(nameof(OnMaxStackable));
        }

        private void OnServerInitialized()
        {
            _playerPrefabID = StringPool.Get("assets/prefabs/player/player.prefab");

            if (!_configData.StacksSettings.Containers.ContainsKey("Backpack"))
            {
                _configData.StacksSettings.Containers["Backpack"] = _configData.GlobalSettings.DefaultContainerMultiplier;
                SaveConfig();
            }

            _backpackPrefabID = 1;
            while (StringPool.toString.ContainsKey(_backpackPrefabID))
            {
                _backpackPrefabID += 1;
            }

            CreateMultipliersCache();
            Subscribe(nameof(CanMoveItem));
            Subscribe(nameof(OnItemDropped));
            Subscribe(nameof(OnMaxStackable));
        }

        #endregion Initialization

        #region Configuration

        private ConfigData _configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Global settings")]
            public GlobalConfiguration GlobalSettings = new GlobalConfiguration();

            [JsonProperty(PropertyName = "Stack settings")]
            public StackConfiguration StacksSettings = new StackConfiguration();

            public class GlobalConfiguration
            {
                [JsonProperty(PropertyName = "Default Multiplier for new containers")]
                public float DefaultContainerMultiplier = 1f;
            }

            public class StackConfiguration
            {
                [JsonProperty(PropertyName = "Containers list (PrefabName: multiplier)")]
                public SortedDictionary<string, float> Containers = new SortedDictionary<string, float>()
                {
                    {"assets/bundled/prefabs/static/bbq.static.prefab", 1f},
                    {"assets/bundled/prefabs/static/hobobarrel_static.prefab", 1f},
                    {"assets/bundled/prefabs/static/recycler_static.prefab", 1f},
                    {"assets/bundled/prefabs/static/repairbench_static.prefab", 1f},
                    {"assets/bundled/prefabs/static/researchtable_static.prefab", 1f},
                    {"assets/bundled/prefabs/static/small_refinery_static.prefab", 1f},
                    {"assets/bundled/prefabs/static/wall.frame.shopfront.metal.static.prefab", 1f},
                    {"assets/bundled/prefabs/static/water_catcher_small.static.prefab", 1f},
                    {"assets/bundled/prefabs/static/workbench1.static.prefab", 1f},
                    {"assets/content/props/fog machine/fogmachine.prefab", 1f},
                    {"assets/content/structures/excavator/prefabs/engine.prefab", 1f},
                    {"assets/content/structures/excavator/prefabs/excavator_output_pile.prefab", 1f},
                    {"assets/content/vehicles/boats/rhib/subents/fuel_storage.prefab", 1f},
                    {"assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab", 1f},
                    {"assets/content/vehicles/boats/rowboat/subents/fuel_storage.prefab", 1f},
                    {"assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab", 1f},
                    {"assets/content/vehicles/minicopter/subents/fuel_storage.prefab", 1f},
                    {"assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab", 1f},
                    {"assets/content/vehicles/modularcar/3module_car_spawned.entity.prefab", 1f},
                    {"assets/content/vehicles/modularcar/4module_car_spawned.entity.prefab", 1f},
                    {"assets/content/vehicles/modularcar/subents/modular_car_1mod_storage.prefab", 1f},
                    {"assets/content/vehicles/modularcar/subents/modular_car_2mod_fuel_tank.prefab", 1f},
                    {"assets/content/vehicles/modularcar/subents/modular_car_fuel_storage.prefab", 1f},
                    {"assets/content/vehicles/modularcar/subents/modular_car_i4_engine_storage.prefab", 1f},
                    {"assets/content/vehicles/modularcar/subents/modular_car_v8_engine_storage.prefab", 1f},
                    {"assets/content/vehicles/scrap heli carrier/subents/fuel_storage_scrapheli.prefab", 1f},
                    {"assets/prefabs/building/wall.frame.shopfront/wall.frame.shopfront.metal.prefab", 1f},
                    {"assets/prefabs/deployable/bbq/bbq.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/campfire/campfire.prefab", 1f},
                    {"assets/prefabs/deployable/composter/composter.prefab", 1f},
                    {"assets/prefabs/deployable/dropbox/dropbox.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/fireplace/fireplace.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/fridge/fridge.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/furnace.large/furnace.large.prefab", 1f},
                    {"assets/prefabs/deployable/furnace/furnace.prefab", 1f},
                    {"assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab", 1f},
                    {"assets/prefabs/deployable/jack o lantern/jackolantern.angry.prefab", 1f},
                    {"assets/prefabs/deployable/jack o lantern/jackolantern.happy.prefab", 1f},
                    {"assets/prefabs/deployable/lantern/lantern.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/large wood storage/box.wooden.large.prefab", 1f},
                    {"assets/prefabs/deployable/liquidbarrel/waterbarrel.prefab", 1f},
                    {"assets/prefabs/deployable/locker/locker.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/mailbox/mailbox.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/mixingtable/mixingtable.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/oil jack/crudeoutput.prefab", 1f},
                    {"assets/prefabs/deployable/oil jack/fuelstorage.prefab", 1f},
                    {"assets/prefabs/deployable/oil refinery/refinery_small_deployed.prefab", 1f},
                    {"assets/prefabs/deployable/planters/planter.large.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/planters/planter.small.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/playerioents/generators/fuel generator/small_fuel_generator.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/playerioents/poweredwaterpurifier/poweredwaterpurifier.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/playerioents/poweredwaterpurifier/poweredwaterpurifier.storage.prefab", 1f},
                    {"assets/prefabs/deployable/playerioents/waterpump/water.pump.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/quarry/fuelstorage.prefab", 1f},
                    {"assets/prefabs/deployable/quarry/hopperoutput.prefab", 1f},
                    {"assets/prefabs/deployable/repair bench/repairbench_deployed.prefab", 1f},
                    {"assets/prefabs/deployable/research table/researchtable_deployed.prefab", 1f},
                    {"assets/prefabs/deployable/single shot trap/guntrap.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/small stash/small_stash_deployed.prefab", 1f},
                    {"assets/prefabs/deployable/survivalfishtrap/survivalfishtrap.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/tier 1 workbench/workbench1.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/tier 2 workbench/workbench2.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/tier 3 workbench/workbench3.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/tuna can wall lamp/tunalight.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_attire.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_building.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_components.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_extra.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_farming.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_resources.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_tools.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_vehicleshigh.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/npcvendingmachine_weapons.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/npcvendingmachines/shopkeeper_vm_invis.prefab", 1f},
                    {"assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/water catcher/water_catcher_large.prefab", 1f},
                    {"assets/prefabs/deployable/water catcher/water_catcher_small.prefab", 1f},
                    {"assets/prefabs/deployable/water well/waterwellstatic.prefab", 1f},
                    {"assets/prefabs/deployable/waterpurifier/waterpurifier.deployed.prefab", 1f},
                    {"assets/prefabs/deployable/waterpurifier/waterstorage.prefab", 1f},
                    {"assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab", 1f},
                    {"assets/prefabs/io/electric/switches/fusebox/fusebox.prefab", 1f},
                    {"assets/prefabs/misc/casino/bigwheel/bigwheelbettingterminal.prefab", 1f},
                    {"assets/prefabs/misc/chinesenewyear/chineselantern/chineselantern.deployed.prefab", 1f},
                    {"assets/prefabs/misc/halloween/coffin/coffinstorage.prefab", 1f},
                    {"assets/prefabs/misc/halloween/cursed_cauldron/cursedcauldron.deployed.prefab", 1f},
                    {"assets/prefabs/misc/halloween/skull_fire_pit/skull_fire_pit.prefab", 1f},
                    {"assets/prefabs/misc/halloween/trophy skulls/skulltrophy.deployed.prefab", 1f},
                    {"assets/prefabs/misc/item drop/item_drop.prefab", 1f},
                    {"assets/prefabs/misc/item drop/item_drop_backpack.prefab", 1f},
                    {"assets/prefabs/misc/marketplace/marketterminal.prefab", 1f},
                    {"assets/prefabs/misc/summer_dlc/abovegroundpool/abovegroundpool.deployed.prefab", 1f},
                    {"assets/prefabs/misc/summer_dlc/paddling_pool/paddlingpool.deployed.prefab", 1f},
                    {"assets/prefabs/misc/summer_dlc/photoframe/photoframe.landscape.prefab", 1f},
                    {"assets/prefabs/misc/summer_dlc/photoframe/photoframe.large.prefab", 1f},
                    {"assets/prefabs/misc/summer_dlc/photoframe/photoframe.portrait.prefab", 1f},
                    {"assets/prefabs/misc/supply drop/supply_drop.prefab", 1f},
                    {"assets/prefabs/misc/twitch/hobobarrel/hobobarrel.deployed.prefab", 1f},
                    {"assets/prefabs/misc/xmas/snow_machine/models/snowmachine.prefab", 1f},
                    {"assets/prefabs/misc/xmas/xmastree/xmas_tree.deployed.prefab", 1f},
                    {"assets/prefabs/npc/autoturret/autoturret_deployed.prefab", 1f},
                    {"assets/prefabs/npc/flame turret/flameturret.deployed.prefab", 1f},
                    {"assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab", 1f},
                    {"Backpack", 1f}
                };
            }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _configData = Config.ReadObject<ConfigData>();
                if (_configData == null)
                    LoadDefaultConfig();
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(_configData);

        #endregion Configuration

        #region Hooks

        private object OnMaxStackable(Item item)
        {
            if (WeightSystemLoaded())
            {
                return null;
            }

            if (item.info.itemType == ItemContainer.ContentsType.Liquid)
            {
                return null;
            }

            if (item.info.stackable == 1)
            {
                return null;
            }

            if (TargetContainer != null)
            {
                BaseEntity entity = TargetContainer.entityOwner ?? TargetContainer.playerOwner;
                if (entity != null)
                {
                    TargetContainer = null;
                    float stackMultiplier = GetStackMultiplier(entity);
                    if (stackMultiplier == 1f)
                    {
                        return null;
                    }

                    return Mathf.FloorToInt(stackMultiplier * item.info.stackable);
                }
            }

            if (item?.parent?.entityOwner != null)
            {
                float stackMultiplier = 1f;
                if (item.parent.entityOwner.prefabID == _playerPrefabID && !(item.parent.HasFlag(ItemContainer.Flag.IsPlayer)))
                {
                    stackMultiplier = _multipliersCache[_backpackPrefabID];
                }
                else
                {
                    stackMultiplier = GetStackMultiplier(item.parent.entityOwner);
                }

                if (stackMultiplier == 1f)
                {
                    return null;
                }

                return Mathf.FloorToInt(stackMultiplier * item.info.stackable);
            }

            return null;
        }

        private ItemContainer TargetContainer;

        private object CanMoveItem(Item movedItem, PlayerInventory playerInventory, uint targetContainerID, int targetSlot, int amount)
        {
            if (WeightSystemLoaded())
            {
                return null;
            }

            if (movedItem == null || playerInventory == null)
            {
                return null;
            }

            ItemContainer container = playerInventory.FindContainer(targetContainerID);
            BasePlayer player = playerInventory.GetComponent<BasePlayer>();
            ItemContainer lootContainer = playerInventory.loot?.FindContainer(targetContainerID);

            TargetContainer = container;

            //Puts($"TargetSlot {targetSlot} Amount {amount} TargetContainer {targetContainerID}");

            // Right-Click Overstack into Player Inventory
            if (targetSlot == -1)
            {
                if (lootContainer == null)
                {
                    if (movedItem.amount > movedItem.info.stackable)
                    {
                        //to prevent player able to "steal" overstacked items in trades
                        ShopFront shopFront = movedItem.parent?.entityOwner?.GetComponent<ShopFront>();
                        if (shopFront != null)
                        {
                            return null;
                        }

                        int loops = 1;
                        if (player != null && player.serverInput.IsDown(BUTTON.SPRINT))
                        {
                            loops = Mathf.CeilToInt((float)movedItem.amount / movedItem.info.stackable);
                        }
                        for (int i = 0; i < loops; i++)
                        {
                            if (movedItem.amount <= movedItem.info.stackable)
                            {
                                if (container != null)
                                {
                                    movedItem.MoveToContainer(container, targetSlot);
                                }
                                else
                                {
                                    playerInventory.GiveItem(movedItem);
                                }
                                break;
                            }
                            Item itemToMove = movedItem.SplitItem(movedItem.info.stackable);
                            bool moved = false;
                            if (container != null)
                            {
                                moved = itemToMove.MoveToContainer(container, targetSlot);
                            }
                            else
                            {
                                moved = playerInventory.GiveItem(itemToMove);
                            }
                            if (moved == false)
                            {
                                movedItem.amount += itemToMove.amount;
                                itemToMove.Remove();
                                break;
                            }
                            if (movedItem != null)
                            {
                                movedItem.MarkDirty();
                            }
                        }
                        playerInventory.ServerUpdate(0f);
                        return false;
                    }
                }
                // Shift Right click into storage container
                else
                {
                    if (player != null && player.serverInput.IsDown(BUTTON.SPRINT))
                    {
                        List<Item> itemsToMove = new List<Item>();
                        foreach (Item item in playerInventory.containerMain.itemList)
                        {
                            if (item.info.itemid == movedItem.info.itemid && item != movedItem)
                            {
                                itemsToMove.Add(item);
                            }
                        }
                        foreach (Item item in playerInventory.containerBelt.itemList)
                        {
                            if (item.info.itemid == movedItem.info.itemid && item != movedItem)
                            {
                                itemsToMove.Add(item);
                            }
                        }

                        foreach (Item item in itemsToMove)
                        {
                            if (!item.MoveToContainer(lootContainer))
                            {
                                break;
                            }
                        }

                        playerInventory.ServerUpdate(0f);
                        return null;
                    }
                }
            }
            // Moving Overstacks Around In Chest
            if (amount > movedItem.info.stackable && lootContainer != null)
            {
                Item targetItem = container.GetSlot(targetSlot);
                if (targetItem == null)
                {// Split item into chest
                    if (amount < movedItem.amount)
                    {
                        ItemHelper.SplitMoveItem(movedItem, amount, container, targetSlot);
                    }
                    else
                    {// Moving items when amount > info.stacksize
                        movedItem.MoveToContainer(container, targetSlot);
                    }
                }
                else
                {
                    if (!targetItem.CanStack(movedItem) && amount == movedItem.amount)
                    {// Swapping positions of items
                        ItemHelper.SwapItems(movedItem, targetItem);
                    }
                    else
                    {
                        if (amount < movedItem.amount)
                        {
                            ItemHelper.SplitMoveItem(movedItem, amount, playerInventory);
                        }
                        else
                        {
                            movedItem.MoveToContainer(container, targetSlot);
                        }
                        // Stacking items when amount > info.stacksize
                    }
                }
                playerInventory.ServerUpdate(0f);
                return false;
            }

            // Prevent Moving Overstacks To Inventory
            if (lootContainer != null)
            {
                Item targetItem = container.GetSlot(targetSlot);
                if (targetItem != null)
                {
                    if (movedItem.parent.playerOwner == player)
                    {
                        if (!movedItem.CanStack(targetItem))
                        {
                            if (targetItem.amount > targetItem.info.stackable)
                            {
                                return false;
                            }
                        }
                    }
                }
            }

            return null;
        }

        // Covers dropping overstacks from chests onto the ground
        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item == null || entity == null) return;
            item.RemoveFromContainer();
            int stackSize = item.MaxStackable();
            if (item.amount > stackSize)
            {
                int loops = Mathf.FloorToInt((float)item.amount / stackSize);
                if (loops > 20)
                {
                    return;
                }
                for (int i = 0; i < loops; i++)
                {
                    if (item.amount <= stackSize)
                    {
                        break;
                    }
                    Item splitItem = item.SplitItem(stackSize);
                    if (splitItem != null)
                    {
                        splitItem.Drop(entity.transform.position, entity.GetComponent<Rigidbody>().velocity + Vector3Ex.Range(-1f, 1f));
                    }
                }
            }
        }
        #endregion Hooks

        #region Helpers

        private void CreateMultipliersCache()
        {
            uint id = 0;
            foreach (KeyValuePair<string, float> container in _configData.StacksSettings.Containers)
            {
                if (container.Key == "Backpack")
                {
                    _multipliersCache[_backpackPrefabID] = _configData.StacksSettings.Containers["Backpack"];
                }
                else
                {
                    id = StringPool.Get(container.Key);
                    if (id > 0)
                    {
                        _multipliersCache[id] = container.Value;
                    }
                }
            }
        }

        private bool WeightSystemLoaded()
        {
            return WeightSystem != null && WeightSystem.IsLoaded;
        }

        public class ItemHelper
        {
            public static bool SplitMoveItem(Item item, int amount, ItemContainer targetContainer, int targetSlot)
            {
                Item splitItem = item.SplitItem(amount);
                if (splitItem == null)
                {
                    return false;
                }

                if (!splitItem.MoveToContainer(targetContainer, targetSlot))
                {
                    item.amount += splitItem.amount;
                    splitItem.Remove();
                }

                return true;
            }

            public static bool SplitMoveItem(Item item, int amount, BasePlayer player)
            {
                return SplitMoveItem(item, amount, player.inventory);
            }

            public static bool SplitMoveItem(Item item, int amount, PlayerInventory inventory)
            {
                Item splitItem = item.SplitItem(amount);
                if (splitItem == null)
                {
                    return false;
                }

                if (!inventory.GiveItem(splitItem))
                {
                    item.amount += splitItem.amount;
                    splitItem.Remove();
                }

                return true;
            }

            public static void SwapItems(Item item1, Item item2)
            {
                ItemContainer container1 = item1.parent;
                ItemContainer container2 = item2.parent;
                int slot1 = item1.position;
                int slot2 = item2.position;
                item1.RemoveFromContainer();
                item2.RemoveFromContainer();
                item1.MoveToContainer(container2, slot2);
                item2.MoveToContainer(container1, slot1);
            }
        }

        public float GetStackMultiplier(BaseEntity entity)
        {
            if (entity is LootContainer || entity is BaseCorpse || entity is BasePlayer)
            {
                return 1f;
            }

            float multiplier = GetMultiplierByPrefabID(entity.prefabID);
            if (multiplier == 0)
            {
                multiplier = GetMultiplierByPrefabName(entity.PrefabName);
            }

            return multiplier;
        }

        private float GetMultiplierByPrefabID(ulong prefabID)
        {
            if (_multipliersCache.ContainsKey(prefabID))
            {
                return _multipliersCache[prefabID];
            }

            return 0;
        }

        private float GetMultiplierByPrefabName(string prefabName)
        {
            float multiplier;
            if (_configData.StacksSettings.Containers.TryGetValue(prefabName, out multiplier))
            {
                return multiplier;
            }

            multiplier = _configData.GlobalSettings.DefaultContainerMultiplier;
            _configData.StacksSettings.Containers[prefabName] = multiplier;
            SaveConfig();
            _multipliersCache.Clear();
            CreateMultipliersCache();

            return multiplier;
        }

        #endregion Helpers
    }
}