using Newtonsoft.Json;
using System.Collections.Generic;
using Oxide.Core;
using UnityEngine;
using System.Linq;
using System;

namespace Oxide.Plugins
{
    [Info("Instant Craft", "Vlad-0003 / Orange / rostov114", "2.2.1")]
    [Description("Allows players to instantly craft items with features")]
    public class InstantCraft : RustPlugin
    {
        #region Vars
        private const string permUse = "instantcraft.use";
        #endregion

        #region Oxide Hooks
        private void Init()
        {
            permission.RegisterPermission(permUse, this);
        }

        private object OnItemCraft(ItemCraftTask task)
        {
            if (task.cancelled)
            {
                return null;
            }

            if (!permission.UserHasPermission(task.owner.UserIDString, permUse))
            {
                return null;
            }

            if (_config.IsBlocked(task))
            {
                CancelTask(task, "Blocked");
                return false;
            }

            List<int> stacks = GetStacks(task.blueprint.targetItem, task.amount * task.blueprint.amountToCreate);
            int slots = FreeSlots(task.owner);
            if (!HasPlace(slots, stacks))
            {
                CancelTask(task, "Slots", stacks.Count, slots);
                return false;
            }

            if (_config.IsNormal(task))
            {
                Message(task.owner, "Normal");
                return null;
            }

            if (!GiveItem(task, stacks))
            {
                return null;
            }

            return true;
        }
        #endregion

        #region Helpers
        private void CancelTask(ItemCraftTask task, string reason, params object[] args)
        {
            task.cancelled = true;
            Message(task.owner, reason, args);
            GiveRefund(task);
            Interface.CallHook("OnItemCraftCancelled", task);
        }

        private void GiveRefund(ItemCraftTask task)
        {
            if (task.takenItems != null && task.takenItems.Count > 0)
            {
                foreach (var item in task.takenItems)
                {
                    task.owner.inventory.GiveItem(item, null);
                }
            }
        }

        private bool GiveItem(ItemCraftTask task, List<int> stacks)
        {
            ulong skin = ItemDefinition.FindSkin(task.blueprint.targetItem.itemid, task.skinID);
            int iteration = 0;

            if (_config.split)
            {
                foreach (var stack in stacks)
                {
                    if (!Give(task, stack, skin) && iteration <= 0)
                    {
                        return false;
                    }

                    iteration++;
                }
            }
            else
            {
                int final = 0;
                foreach (var stack in stacks)
                {
                    final += stack;
                }

                if (!Give(task, final, skin))
                {
                    return false;
                }
            }

            task.cancelled = true;
            return true;
        }

        private bool Give(ItemCraftTask task, int amount, ulong skin)
        {
            Item item = null;
            try
            {
                item = ItemManager.CreateByItemID(task.blueprint.targetItem.itemid, amount, skin);
            }
            catch (Exception e)
            {
                PrintError($"Exception creating item! targetItem: {task.blueprint.targetItem}-{amount}-{skin}; Exception: {e}");
            }

            if (item == null)
            {
                return false;
            }

            if (item.hasCondition && task.conditionScale != 1f)
            {
                item.maxCondition *= task.conditionScale;
                item.condition = item.maxCondition;
            }

            item.OnVirginSpawn();

            if (task.instanceData != null)
            {
                item.instanceData = task.instanceData;
            }

            Interface.CallHook("OnItemCraftFinished", task, item);

            if (task.owner.inventory.GiveItem(item, null))
            {
                task.owner.Command("note.inv", new object[]{item.info.itemid, item.amount});
                return true;
            }

            ItemContainer itemContainer = task.owner.inventory.crafting.containers.First<ItemContainer>();
            task.owner.Command("note.inv", new object[]{item.info.itemid, item.amount});
            task.owner.Command("note.inv", new object[]{item.info.itemid, -item.amount});
            item.Drop(itemContainer.dropPosition, itemContainer.dropVelocity, default(Quaternion));

            return true;
        }

        private int FreeSlots(BasePlayer player)
        {
            var slots = player.inventory.containerMain.capacity + player.inventory.containerBelt.capacity;
            var taken = player.inventory.containerMain.itemList.Count + player.inventory.containerBelt.itemList.Count;
            return slots - taken;
        }

        private List<int> GetStacks(ItemDefinition item, int amount) 
        {
            var list = new List<int>();
            var maxStack = item.stackable;

            if (maxStack == 0)
            {
                maxStack = 1;
            }

            while (amount > maxStack)
            {
                amount -= maxStack;
                list.Add(maxStack);
            }
            
            list.Add(amount);
            
            return list; 
        }

        private bool HasPlace(int slots, List<int> stacks)
        {
            if (!_config.checkPlace)
            {
                return true;
            }

            if (_config.split && slots - stacks.Count < 0)
            {
                return false;
            }

            return slots > 0;
        }
        #endregion

        #region Localization 1.1.1
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                {"Blocked", "Crafting of that item is blocked!"},
                {"Slots", "You don't have enough place to craft! Need {0}, have {1}!"},
                {"Normal", "Item will be crafted with normal speed."}
            }, this, "en");
        }

        private void Message(BasePlayer player, string messageKey, params object[] args)
        {
            if (player == null)
            {
                return;
            }

            var message = GetMessage(messageKey, player.UserIDString, args);
            player.ChatMessage(message);
        }

        private string GetMessage(string messageKey, string playerID, params object[] args)
        {
            return string.Format(lang.GetMessage(messageKey, this, playerID), args);
        }
        #endregion
        
        #region Configuration 1.1.0
        private Configuration _config;
        private class Configuration
        {
            [JsonProperty(PropertyName = "Check for free place")]
            public bool checkPlace = true;
            
            [JsonProperty(PropertyName = "Split crafted stacks")]
            public bool split = true;
            
            [JsonProperty(PropertyName = "Normal Speed")]
            public string[] normal =
            {
                "hammer",
                "put item shortname here"
            };

            [JsonProperty(PropertyName = "Blacklist")]
            public string[] blocked =
            {
                "rock",
                "put item shortname here"
            };

            public bool IsNormal(ItemCraftTask task) => normal?.Contains(task.blueprint.targetItem.shortname) ?? false;
            public bool IsBlocked(ItemCraftTask task) => blocked?.Contains(task.blueprint.targetItem.shortname) ?? false;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<Configuration>();
                SaveConfig();
            }
            catch
            {
                PrintError("Error reading config, please check!");

                Unsubscribe(nameof(OnItemCraft));
            }
        }

        protected override void LoadDefaultConfig()
        {
            _config = new Configuration();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(_config);
        #endregion
    }
}