using C3.ModKit;
using HarmonyLib;
using Unfoundry;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

namespace BulkDemolishTerrain
{
    [UnfoundryMod(Plugin.GUID)]
    public class Plugin : UnfoundryPlugin
    {
        public const string
            MODNAME = "BulkDemolishTerrain",
            AUTHOR = "erkle64",
            GUID = AUTHOR + "." + MODNAME,
            VERSION = "1.3.2";

        public static LogSource log;

        public static TypedConfigEntry<KeyCode> configChangeModeKey;

        private static TerrainMode _currentTerrainMode = TerrainMode.Collect;

        private enum TerrainMode
        {
            Collect,
            Destroy,
            Ignore
        }

        public Plugin()
        {
            log = new LogSource(MODNAME);

            new Config(GUID)
                .Group("Input",
                    "Key Codes: Backspace, Tab, Clear, Return, Pause, Escape, Space, Exclaim,",
                    "DoubleQuote, Hash, Dollar, Percent, Ampersand, Quote, LeftParen, RightParen,",
                    "Asterisk, Plus, Comma, Minus, Period, Slash,",
                    "Alpha0, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,",
                    "Colon, Semicolon, Less, Equals, Greater, Question, At,",
                    "LeftBracket, Backslash, RightBracket, Caret, Underscore, BackQuote,",
                    "A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,",
                    "LeftCurlyBracket, Pipe, RightCurlyBracket, Tilde, Delete,",
                    "Keypad0, Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,",
                    "KeypadPeriod, KeypadDivide, KeypadMultiply, KeypadMinus, KeypadPlus, KeypadEnter, KeypadEquals,",
                    "UpArrow, DownArrow, RightArrow, LeftArrow, Insert, Home, End, PageUp, PageDown,",
                    "F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, F13, F14, F15,",
                    "Numlock, CapsLock, ScrollLock,",
                    "RightShift, LeftShift, RightControl, LeftControl, RightAlt, LeftAlt, RightApple, RightApple,",
                    "LeftCommand, LeftCommand, LeftWindows, RightWindows, AltGr,",
                    "Help, Print, SysReq, Break, Menu,",
                    "Mouse0, Mouse1, Mouse2, Mouse3, Mouse4, Mouse5, Mouse6")
                    .Entry(out configChangeModeKey, "changeModeKey", KeyCode.Backslash, "Keyboard shortcut for change terrain mode.")
                .EndGroup()
                .Load()
                .Save();
        }

        public override void Load(Mod mod)
        {
            log.Log($"Loading {MODNAME}");
        }

        [HarmonyPatch]
        public class Patch
        {
            private static List<bool> shouldRemove = null;
            private static List<BuildableObjectGO> bogoQueryResult = new List<BuildableObjectGO>(0);

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), nameof(Character.BulkDemolishBuildingEvent.processEvent))]
            [HarmonyPostfix]
            public static void processBulkDemolishBuildingEvent(Character.BulkDemolishBuildingEvent __instance)
            {
                if (_currentTerrainMode == TerrainMode.Ignore) return;

                ulong characterHash = __instance.characterHash;

                if (shouldRemove == null)
                {
                    var terrainTypes = ItemTemplateManager.getAllTerrainTemplates();

                    shouldRemove = new List<bool>();
                    shouldRemove.Add(false); // Air
                    shouldRemove.Add(false); // ???

                    foreach (var terrainType in terrainTypes)
                    {
                        shouldRemove.Add(terrainType.Value.destructible);
                        //log.LogFormat(string.Format("Terrain {0} {1} {2} {3} {4}", terrainType.Value.name, terrainType.Value.identifier, terrainType.Value.id, terrainType.Value._isOre(), terrainType.Value._isDestructible()));
                    }
                }

                var useDestroyMode = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (_currentTerrainMode == TerrainMode.Destroy) useDestroyMode = !useDestroyMode;

                var pos = __instance.demolitionAreaAABB_pos;
                var size = __instance.demolitionAreaAABB_size;
                for (int z = 0; z < size.z; ++z)
                {
                    for (int y = 0; y < size.y; ++y)
                    {
                        for (int x = 0; x < size.x; ++x)
                        {
                            var coords = pos + new Vector3Int(x, y, z);
                            Chunk chunk;
                            uint blockIdx;
                            var terrainData = ChunkManager.getTerrainDataForWorldCoord(coords, out chunk, out blockIdx);
                            if (terrainData < shouldRemove.Count && shouldRemove[terrainData])
                            {
                                if (useDestroyMode)
                                {
                                    ActionManager.AddQueuedEvent(() => { ActionManager.AddQueuedEvent(() => GameRoot.addLockstepEvent(new Character.RemoveTerrainEvent(characterHash, coords, ulong.MaxValue))); });
                                }
                                else
                                {
                                    ActionManager.AddQueuedEvent(() => GameRoot.addLockstepEvent(new Character.RemoveTerrainEvent(characterHash, coords, 0)));
                                }
                            }
                        }
                    }
                }

                AABB3D aabb = ObjectPoolManager.aabb3ds.getObject();
                aabb.reinitialize(pos.x, pos.y, pos.z, size.x, size.y, size.z);
                StreamingSystem.getBuildableObjectGOQuadtreeArray().queryAABB3D(aabb, bogoQueryResult, true);
                if (bogoQueryResult.Count > 0)
                {
                    foreach (var bogo in bogoQueryResult)
                    {
                        if (bogo.template.type == BuildableObjectTemplate.BuildableObjectType.WorldDecorMineAble)
                        {
                            if (useDestroyMode)
                            {
                                ActionManager.AddQueuedEvent(() => GameRoot.addLockstepEvent(new Character.DemolishBuildingEvent(characterHash, bogo.relatedEntityId, -2, 0)));
                            }
                            else
                            {
                                ActionManager.AddQueuedEvent(() => GameRoot.addLockstepEvent(new Character.RemoveWorldDecorEvent(characterHash, bogo.relatedEntityId, 0)));
                            }
                        }
                    }
                }
                ObjectPoolManager.aabb3ds.returnObject(aabb); aabb = null;
            }

            [HarmonyPatch(typeof(Character.DemolishBuildingEvent), nameof(Character.DemolishBuildingEvent.processEvent))]
            [HarmonyPrefix]
            private static bool DemolishBuildingEvent_processEvent(Character.DemolishBuildingEvent __instance)
            {
                if (__instance.clientPlaceholderId == -2)
                {
                    __instance.clientPlaceholderId = 0;
                    BuildingManager.buildingManager_demolishBuildingEntityForDynamite(__instance.entityId);
                    return false;
                }

                return true;
            }

            private static readonly FieldInfo _HandheldTabletHH_currentlySetMode = typeof(HandheldTabletHH).GetField("currentlySetMode", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly FieldInfo _GameRoot_bulkDemolitionState = typeof(GameRoot).GetField("bulkDemolitionState", BindingFlags.NonPublic | BindingFlags.Instance);
            [HarmonyPatch(typeof(HandheldTabletHH), nameof(HandheldTabletHH._updateBehavoir))]
            [HarmonyPostfix]
            private static void HandheldTabletHH_updateBehavoir(HandheldTabletHH __instance)
            {
                var gameRoot = GameRoot.getSingleton();
                if (gameRoot == null) return;

                var clientCharacter = GameRoot.getClientCharacter();
                if (clientCharacter == null) return;

                if (clientCharacter.clientData.isBulkDemolitionModeActive())
                {
                    if (Input.GetKeyDown(configChangeModeKey.Get()))
                    {
                        switch (_currentTerrainMode)
                        {
                            case TerrainMode.Collect: _currentTerrainMode = TerrainMode.Destroy; break;
                            case TerrainMode.Destroy: _currentTerrainMode = TerrainMode.Ignore; break;
                            case TerrainMode.Ignore: _currentTerrainMode = TerrainMode.Collect; break;
                        }
                    }

                    var infoText = GameRoot.getSingleton().uiText_infoText.tmp.text;
                    infoText += $"\nTerrain Mode: {_currentTerrainMode}.";
                    if ((int)_GameRoot_bulkDemolitionState.GetValue(GameRoot.getSingleton()) == 2)
                    {
                        switch (_currentTerrainMode)
                        {
                            case TerrainMode.Collect:
                                infoText += " Hold [ALT] to destroy.";
                                break;

                            case TerrainMode.Destroy:
                                infoText += " Hold [ALT] to collect.";
                                break;
                        }
                    }
                    GameRoot.setInfoText(infoText);
                }
            }
        }
    }
}
