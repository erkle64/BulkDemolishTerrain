using C3.ModKit;
using HarmonyLib;
using Unfoundry;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;
using System;

namespace BulkDemolishTerrain
{
    [UnfoundryMod(GUID)]
    public class Plugin : UnfoundryPlugin
    {
        public const string
            MODNAME = "BulkDemolishTerrain",
            AUTHOR = "erkle64",
            GUID = AUTHOR + "." + MODNAME,
            VERSION = "1.3.4";

        public static LogSource log;

        public static TypedConfigEntry<KeyCode> configChangeModeKey;

        private static TerrainMode _currentTerrainMode = TerrainMode.Collect;

        private static readonly Queue<Vector3Int> _queuedTerrainRemovals = new Queue<Vector3Int>();
        private static float _lastTerrainRemovalUpdate = 0.0f;

        private enum TerrainMode
        {
            Collect,
            Destroy,
            Ignore,
            CollectTerrainOnly,
            DestroyTerrainOnly
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
            private static readonly List<BuildableObjectGO> bogoQueryResult = new List<BuildableObjectGO>(0);

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), MethodType.Constructor, new Type[] { typeof(ulong), typeof(Vector3Int), typeof(Vector3Int) })]
            [HarmonyPostfix]
            public static void BulkDemolishBuildingEventConstructor(Character.BulkDemolishBuildingEvent __instance, Vector3Int demolitionAreaAABB_size)
            {
                if (_currentTerrainMode == TerrainMode.CollectTerrainOnly || _currentTerrainMode == TerrainMode.DestroyTerrainOnly)
                {
                    if (_currentTerrainMode == TerrainMode.CollectTerrainOnly || _currentTerrainMode == TerrainMode.DestroyTerrainOnly)
                    {
                        __instance.demolitionAreaAABB_size = -demolitionAreaAABB_size;
                    }
                }
            }

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), nameof(Character.BulkDemolishBuildingEvent.processEvent))]
            [HarmonyPrefix]
            public static bool processBulkDemolishBuildingEventPrefix(Character.BulkDemolishBuildingEvent __instance)
            {
                if (__instance.demolitionAreaAABB_size.x < 0)
                {
                    __instance.demolitionAreaAABB_size = new Vector3Int(Mathf.Abs(__instance.demolitionAreaAABB_size.x), Mathf.Abs(__instance.demolitionAreaAABB_size.y), Mathf.Abs(__instance.demolitionAreaAABB_size.z));
                    processBulkDemolishBuildingEvent(__instance);
                    return false;
                }

                return true;
            }

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), nameof(Character.BulkDemolishBuildingEvent.processEvent))]
            [HarmonyPostfix]
            public static void processBulkDemolishBuildingEvent(Character.BulkDemolishBuildingEvent __instance)
            {
                if (GlobalStateManager.isDedicatedServer) return;

                var clientCharacter = GameRoot.getClientCharacter();
                if (clientCharacter == null) return;

                var clientCharacterHash = clientCharacter.usernameHash;

                ulong characterHash = __instance.characterHash;
                if (characterHash != clientCharacterHash) return;

                if (_currentTerrainMode == TerrainMode.Ignore) return;

                if (shouldRemove == null)
                {
                    var terrainTypes = ItemTemplateManager.getAllTerrainTemplates();

                    shouldRemove = new List<bool>
                    {
                        false, // Air
                        false // ???
                    };

                    foreach (var terrainType in terrainTypes)
                    {
                        shouldRemove.Add(terrainType.Value.destructible);
                        //log.LogFormat(string.Format("Terrain {0} {1} {2} {3} {4}", terrainType.Value.name, terrainType.Value.identifier, terrainType.Value.id, terrainType.Value._isOre(), terrainType.Value._isDestructible()));
                    }
                }

                var useDestroyMode = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                if (_currentTerrainMode == TerrainMode.Destroy || _currentTerrainMode == TerrainMode.DestroyTerrainOnly) useDestroyMode = !useDestroyMode;

                if (GameRoot.IsMultiplayerEnabled) useDestroyMode = false;

                var pos = __instance.demolitionAreaAABB_pos;
                var size = __instance.demolitionAreaAABB_size;
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

                ChunkManager.getChunkCoordsFromWorldCoords(pos.x, pos.z, out var fromChunkX, out var fromChunkZ);
                ChunkManager.getChunkCoordsFromWorldCoords(pos.x + size.x - 1, pos.z + size.z - 1, out var toChunkX, out var toChunkZ);
                for (var chunkZ = fromChunkZ; chunkZ <= toChunkZ; chunkZ++)
                {
                    for (var chunkX = fromChunkX; chunkX <= toChunkX; chunkX++)
                    {
                        var chunkFromX = chunkX * Chunk.CHUNKSIZE_XZ;
                        var chunkFromZ = chunkZ * Chunk.CHUNKSIZE_XZ;
                        var chunkToX = chunkFromX + Chunk.CHUNKSIZE_XZ - 1;
                        var chunkToZ = chunkFromZ + Chunk.CHUNKSIZE_XZ - 1;
                        var fromX = Mathf.Max(pos.x, chunkFromX);
                        var fromZ = Mathf.Max(pos.z, chunkFromZ);
                        var toX = Mathf.Min(pos.x + size.x - 1, chunkToX);
                        var toZ = Mathf.Min(pos.z + size.z - 1, chunkToZ);

                        for (int z = fromZ; z <= toZ; ++z)
                        {
                            for (int y = 0; y < size.y; ++y)
                            {
                                for (int x = fromX; x <= toX; ++x)
                                {
                                    var coords = new Vector3Int(x, pos.y + y, z);
                                    var terrainData = ChunkManager.getTerrainDataForWorldCoord(coords, out Chunk _, out uint _);
                                    if (terrainData < shouldRemove.Count && shouldRemove[terrainData])
                                    {
                                        if (useDestroyMode)
                                        {
                                            ActionManager.AddQueuedEvent(() => _queuedTerrainRemovals.Enqueue(coords));
                                        }
                                        else
                                        {
                                            ActionManager.AddQueuedEvent(() => GameRoot.addLockstepEvent(new Character.RemoveTerrainEvent(characterHash, coords, 0)));
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            private static readonly FieldInfo _gameInitDone = typeof(GameRoot).GetField("_gameInitDone", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo exitToMainMenuCalled = typeof(GameRoot).GetField("exitToMainMenuCalled", BindingFlags.Instance | BindingFlags.NonPublic);
            [HarmonyPatch(typeof(GameRoot), "updateInternal")]
            [HarmonyPrefix]
            private static void GameRoot_updateInternal(GameRoot __instance)
            {
                if (!(bool)_gameInitDone.GetValue(__instance) || (bool)exitToMainMenuCalled.GetValue(__instance)) return;
                if (!_queuedTerrainRemovals.TryPeek(out var _)) return;
                if (Time.time < _lastTerrainRemovalUpdate + 0.1f) return;
                _lastTerrainRemovalUpdate = Time.time;

                var clientCharacter = GameRoot.getClientCharacter();
                if (clientCharacter == null) return;

                var characterHash = clientCharacter.usernameHash;

                while (_queuedTerrainRemovals.TryPeek(out var coords))
                {
                    _queuedTerrainRemovals.Dequeue();

                    GameRoot.addLockstepEvent(new Character.RemoveTerrainEvent(characterHash, coords, ulong.MaxValue - 1UL));
                }
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

            [HarmonyPatch(typeof(Character.RemoveTerrainEvent), nameof(Character.RemoveTerrainEvent.processEvent))]
            [HarmonyPrefix]
            private static bool RemoveTerrainEvent_processEvent(Character.RemoveTerrainEvent __instance)
            {
                if (__instance.terrainRemovalPlaceholderId == ulong.MaxValue - 1UL)
                {
                    __instance.terrainRemovalPlaceholderId = 0ul;

                    ChunkManager.getChunkIdxAndTerrainArrayIdxFromWorldCoords(__instance.worldPos.x, __instance.worldPos.y, __instance.worldPos.z, out ulong chunkIndex, out uint blockIndex);

                    byte terrainType = 0;
                    ChunkManager.chunks_removeTerrainBlock(chunkIndex, blockIndex, ref terrainType);

                    return false;
                }

                return true;
            }

            private static readonly FieldInfo _HandheldTabletHH_currentlySetMode = typeof(HandheldTabletHH).GetField("currentlySetMode", BindingFlags.NonPublic | BindingFlags.Instance);
            private static readonly FieldInfo _GameRoot_bulkDemolitionState = typeof(GameRoot).GetField("bulkDemolitionState", BindingFlags.NonPublic | BindingFlags.Instance);
            [HarmonyPatch(typeof(HandheldTabletHH), nameof(HandheldTabletHH._updateBehavoir))]
            [HarmonyPostfix]
            private static void HandheldTabletHH_updateBehavoir()
            {
                var gameRoot = GameRoot.getSingleton();
                if (gameRoot == null) return;

                var clientCharacter = GameRoot.getClientCharacter();
                if (clientCharacter == null) return;

                if (clientCharacter.clientData.isBulkDemolitionModeActive() && !GlobalStateManager.checkIfCursorIsRequired())
                {
                    if (Input.GetKeyDown(configChangeModeKey.Get()))
                    {
                        switch (_currentTerrainMode)
                        {
                            case TerrainMode.Collect: _currentTerrainMode = GameRoot.IsMultiplayerEnabled ? TerrainMode.Ignore : TerrainMode.Destroy; break;
                            case TerrainMode.Destroy: _currentTerrainMode = TerrainMode.Ignore; break;
                            case TerrainMode.Ignore: _currentTerrainMode = TerrainMode.CollectTerrainOnly; break;
                            case TerrainMode.CollectTerrainOnly: _currentTerrainMode = GameRoot.IsMultiplayerEnabled ? TerrainMode.Collect : TerrainMode.DestroyTerrainOnly; break;
                            case TerrainMode.DestroyTerrainOnly: _currentTerrainMode = TerrainMode.Collect; break;
                        }
                    }

                    var infoText = GameRoot.getSingleton().uiText_infoText.tmp.text;
                    switch (_currentTerrainMode)
                    {
                        case TerrainMode.Collect:
                        case TerrainMode.Destroy:
                        case TerrainMode.Ignore:
                            infoText += $"\nTerrain Mode: {_currentTerrainMode}.";
                            break;

                        case TerrainMode.CollectTerrainOnly:
                            infoText += $"\nTerrain Mode: Collect Terrain. Ignore Buildings.";
                            break;

                        case TerrainMode.DestroyTerrainOnly:
                            infoText += $"\nTerrain Mode: Destroy Terrain. Ignore Buildings.";
                            break;
                    }
                    if (!GameRoot.IsMultiplayerEnabled && (int)_GameRoot_bulkDemolitionState.GetValue(GameRoot.getSingleton()) == 2)
                    {
                        switch (_currentTerrainMode)
                        {
                            case TerrainMode.Collect:
                            case TerrainMode.CollectTerrainOnly:
                                infoText += " Hold [ALT] to destroy.";
                                break;

                            case TerrainMode.Destroy:
                            case TerrainMode.DestroyTerrainOnly:
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
