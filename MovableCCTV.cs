using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using VLB;

namespace Oxide.Plugins
{
    [Info("Movable CCTV", "Bazz3l", "1.1.0")]
    [Description("Allow players to control placed cctv cameras using WASD")]
    class MovableCCTV : CovalencePlugin
    {
        #region Fields

        private const string PERM_USE = "movablecctv.use";
        private static MovableCCTV _plugin;
        private PluginConfig _config;

        #endregion

        #region Config

        protected override void LoadDefaultConfig() => _config = new PluginConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _config = Config.ReadObject<PluginConfig>();

                if (_config == null)
                {
                    throw new JsonException();
                }
                
                if (!_config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintWarning("Config was updated");
                    
                    SaveConfig();
                }
            }
            catch
            {
                PrintWarning("Invalid config, default config has been loaded.");
                
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private class PluginConfig
        {
            public float RotateSpeed = 0.2f;
            public string TextColor = "1 1 1 0.5";
            public int TextSize = 14;
            public string AnchorMin = "0.293 0.903";
            public string AnchorMax = "0.684 0.951";

            public string ToJson() => JsonConvert.SerializeObject(this);
            
            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }
        
        #endregion

        #region Local
        
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                { "Description", "Control the camera using WASD" }
            }, this);
        }
        
        #endregion

        #region Oxide

        private void Init()
        {
            _plugin = this;

            permission.RegisterPermission(PERM_USE, this);
            
            LoadConfig();
        }

        private void OnServerInitialized()
        {
            CheckCCTV();
        }

        private void Unload()
        {
            CameraMover.RemoveAll();
            
            UI.RemoveAll();

            _plugin = null;
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            CCTV_RC cctvRc = go.ToBaseEntity() as CCTV_RC;

            if (cctvRc == null || cctvRc.IsStatic())
            {
                return;
            }
            
            cctvRc.hasPTZ = true;
        }

        private void OnBookmarkControlStarted(ComputerStation computerStation, BasePlayer player, string bookmarkName, IRemoteControllable entity)
        {
            UI.RemoveUI(player);
            
            CCTV_RC cctvRc = entity as CCTV_RC;
            if (cctvRc == null
                || cctvRc.IsStatic()
                || !HasPermission(player)
                || BecomeMovableWasBlocked(cctvRc, player))
            {
                return;
            }
            
            player.GetOrAddComponent<CameraMover>();
            
            UI.CreateUI(player, Lang("Description", player.UserIDString));
        }
        
        private void OnBookmarkControlEnded(ComputerStation station, BasePlayer player, CCTV_RC cctvRc)
        {
            player.GetComponent<CameraMover>()?.DestroyImmediate();
            
            UI.RemoveUI(player);
        }
        
        #endregion

        #region Core

        private void CheckCCTV()
        {
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                CCTV_RC cctv = entity as CCTV_RC;
                
                if (cctv == null || cctv.IsStatic()) continue;
                
                cctv.hasPTZ = true;
            }
        }
        
        private class CameraMover : MonoBehaviour
        {
            public static void RemoveAll()
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    player.GetComponent<CameraMover>()?.Destroy();
                }
            }
            
            private ComputerStation _computerStation;
            private BasePlayer _player;

            private void Awake()
            {
                _player = GetComponent<BasePlayer>();

                if (_player == null)
                {
                    Destroy(this);
                    return;
                }

                _computerStation = _player.GetMounted() as ComputerStation;
            }

            private void FixedUpdate()
            {
                CCTV_RC cctvRc = GetControlledCctv(_computerStation);

                if (cctvRc == null || cctvRc.IsStatic())
                {
                    return;
                }

                float y = _player.serverInput.IsDown(BUTTON.FORWARD) ? 1f : (_player.serverInput.IsDown(BUTTON.BACKWARD) ? -1f : 0f);
                float x = _player.serverInput.IsDown(BUTTON.LEFT) ? -1f : (_player.serverInput.IsDown(BUTTON.RIGHT) ? 1f : 0f);

                InputState inputState = new InputState();
                inputState.current.mouseDelta.y = y * _plugin._config.RotateSpeed;
                inputState.current.mouseDelta.x = x * _plugin._config.RotateSpeed;

                cctvRc.UserInput(inputState, _player);
            }

            public void Destroy() => Destroy(this);
            
            public void DestroyImmediate() => DestroyImmediate(this);

            private CCTV_RC GetControlledCctv(ComputerStation computerStation)
            {
                if (computerStation == null || computerStation.IsDestroyed)
                {
                    return null;
                }
                
                return computerStation.currentlyControllingEnt.Get(serverside: true) as CCTV_RC;
            }
        }
        
        #endregion

        #region UI

        private static class UI
        {
            private const string PANEL_NAME = "MovableCCTV";

            public static void CreateUI(BasePlayer player, string description = "")
            {
                CuiElementContainer container = new CuiElementContainer();
                
                string panel = container.Add(new CuiPanel {
                    Image =
                    {
                        Color = "0 0 0 0"
                    },
                    RectTransform = {
                        AnchorMin = _plugin._config.AnchorMin,
                        AnchorMax = _plugin._config.AnchorMax
                    }
                }, "Overlay", PANEL_NAME);
                
                container.Add(new CuiLabel
                {
                    Text = {
                        FontSize = _plugin._config.TextSize,
                        Color = _plugin._config.TextColor,
                        Text  = description,
                        Align = TextAnchor.MiddleCenter
                    },
                    RectTransform = {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }, panel);
                
                CuiHelper.AddUi(player, container);
            }
            
            public static void RemoveUI(BasePlayer player) => CuiHelper.DestroyUi(player, PANEL_NAME);

            public static void RemoveAll()
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    RemoveUI(player);
                }
            }
        }
        
        #endregion

        #region Helpers

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        
        private bool HasPermission(BasePlayer player) => permission.UserHasPermission(player.UserIDString, PERM_USE);
        
        private bool BecomeMovableWasBlocked(CCTV_RC cctvRc, BasePlayer player)
        {
            object hookResult = Interface.CallHook("OnCCTVMovableBecome", cctvRc, player);
            return hookResult is bool && (bool)hookResult == false;
        }
        
        #endregion
    }
}