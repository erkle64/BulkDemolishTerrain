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
            VERSION = "1.4.1";

        public static LogSource log;

        public static TypedConfigEntry<KeyCode> configChangeModeKey;
        public static TypedConfigEntry<bool> playerPlacedOnly;
        public static TypedConfigEntry<bool> removeLiquids;
        public static TypedConfigEntry<bool> ignoreMiningLevel;
        public static TypedConfigEntry<TerrainMode> currentTerrainMode;

        private static readonly Queue<Vector3Int> _queuedTerrainRemovals = new Queue<Vector3Int>();
        private static float _lastTerrainRemovalUpdate = 0.0f;

        private static List<bool> shouldRemove = null;
        private static List<bool> isOre = null;
        private static readonly List<BuildableObjectGO> bogoQueryResult = new List<BuildableObjectGO>(0);

        private static bool _confirmationFrameOpen = false;

        public enum TerrainMode
        {
            Collect,
            Destroy,
            Ignore,
            CollectTerrainOnly,
            DestroyTerrainOnly,
            LiquidOnly
        }

        public Plugin()
        {
            log = new LogSource(MODNAME);

            new Config(GUID)
                .Group("General")
                    .Entry(out playerPlacedOnly, "playerPlacedOnly", false, OnPlayerPlacedOnlyChanged, "Only allow demolishing player placed terrain (Includes concrete).")
                    .Entry(out removeLiquids, "removeLiquids", true, "Remove all liquids, water, when demolishing.")
                    .Entry(out ignoreMiningLevel, "ignoreMiningLevel", false, "Ignore mining level research and remove terrain anyway.")
                .EndGroup()
                .Group("Modes")
                    .Entry(out currentTerrainMode, "currentTerrainMode", TerrainMode.Collect, "Collect, Destroy, Ignore, CollectTerrainOnly, DestroyTerrainOnly, LiquidOnly")
                .EndGroup()
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

            if (!removeLiquids.Get() && currentTerrainMode.Get() == TerrainMode.LiquidOnly)
            {
                currentTerrainMode.Set(TerrainMode.Collect);
            }
        }

        private void OnPlayerPlacedOnlyChanged(bool oldValue, bool newValue)
        {
            shouldRemove = null;
        }

        public override void Load(Mod mod)
        {
            log.Log($"Loading {MODNAME}");
        }

        [FoundryRPC]
        public static void DestroyTerrainRPC(int worldPosX, int worldPosY, int worldPosZ)
        {
            try
            {
                ChunkManager.getChunkIdxAndTerrainArrayIdxFromWorldCoords(worldPosX, worldPosY, worldPosZ, out ulong chunkIndex, out uint blockIndex);

                byte terrainType = 0;
                ChunkManager.chunks_removeTerrainBlock(chunkIndex, blockIndex, ref terrainType);
            }
            catch(Exception ex)
            {
                log.LogWarning(ex.ToString());
            }
        }

    [FoundryRPC]
        public static void DestroyBuildingRPC(ulong entityId)
        {
            try
            {
                if (BuildingManager.buildingManager_getEntityPtr(entityId) != 0UL)
                {
                    BuildingManager.buildingManager_demolishBuildingEntityForDynamite(entityId);
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex.ToString());
            }
        }

        private static void GenerateShouldRemoveArray(bool force)
        {
            var miningLevel = ResearchSystem.getUnlockedMiningHardnessLevel();
            if (force || shouldRemove == null)
            {
                var terrainTypes = ItemTemplateManager.getAllTerrainTemplates();

                shouldRemove = new List<bool>
                        {
                            false, // Air
                            false // ???
                        };

                if (playerPlacedOnly.Get())
                {
                    foreach (var terrainType in terrainTypes)
                    {
                        shouldRemove.Add(
                            terrainType.Value.destructible
                            && terrainType.Value.yieldItemOnDig_template != null
                            && terrainType.Value.yieldItemOnDig_template.buildableObjectTemplate != null
                            && terrainType.Value.parentBOT != null
                            && (ignoreMiningLevel.Get() || terrainType.Value.requiredMiningHardnessLevel <= miningLevel));
                    }
                }
                else
                {
                    foreach (var terrainType in terrainTypes)
                    {
                        shouldRemove.Add(
                            terrainType.Value.destructible
                            && (ignoreMiningLevel.Get() || terrainType.Value.requiredMiningHardnessLevel <= miningLevel));
                    }
                }
            }
        }

        private static void GenerateIsOreArray()
        {
            var miningLevel = ResearchSystem.getUnlockedMiningHardnessLevel();
            if (isOre == null)
            {
                var terrainTypes = ItemTemplateManager.getAllTerrainTemplates();

                isOre = new List<bool>
                        {
                            false, // Air
                            false // ???
                        };

                foreach (var terrainType in terrainTypes)
                {
                    isOre.Add(
                        terrainType.Value.flags.HasFlagNonAlloc(TerrainBlockType.TerrainTypeFlags.Ore)
                        || terrainType.Value.flags.HasFlagNonAlloc(TerrainBlockType.TerrainTypeFlags.OreVeinMineable)
                        || terrainType.Value.flags.HasFlagNonAlloc(TerrainBlockType.TerrainTypeFlags.OreVeinCore)
                        || terrainType.Value.flags.HasFlagNonAlloc(TerrainBlockType.TerrainTypeFlags.OreVeinExterior));
                }
            }
        }

        [HarmonyPatch]
        public class Patch
        {
            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), MethodType.Constructor, new Type[] { typeof(ulong), typeof(Vector3Int), typeof(Vector3Int) })]
            [HarmonyPostfix]
            public static void BulkDemolishBuildingEventConstructor(Character.BulkDemolishBuildingEvent __instance, Vector3Int demolitionAreaAABB_size)
            {
                var currentTerrainMode = Plugin.currentTerrainMode.Get();
                switch (currentTerrainMode)
                {
                    case TerrainMode.CollectTerrainOnly:
                    case TerrainMode.DestroyTerrainOnly:
                    case TerrainMode.LiquidOnly:
                        __instance.demolitionAreaAABB_size = -demolitionAreaAABB_size;
                        break;
                }
            }

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), nameof(Character.BulkDemolishBuildingEvent.processEvent))]
            [HarmonyPrefix]
            public static bool processBulkDemolishBuildingEventPrefix(Character.BulkDemolishBuildingEvent __instance)
            {
                if (__instance.demolitionAreaAABB_size.x < 0)
                {
                    __instance.demolitionAreaAABB_size = new Vector3Int(Mathf.Abs(__instance.demolitionAreaAABB_size.x), Mathf.Abs(__instance.demolitionAreaAABB_size.y), Mathf.Abs(__instance.demolitionAreaAABB_size.z));
                    //processBulkDemolishBuildingEvent(__instance);
                    return false;
                }

                return true;
            }

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), nameof(Character.BulkDemolishBuildingEvent.processEvent))]
            [HarmonyPostfix]
            public static void processBulkDemolishBuildingEvent(Character.BulkDemolishBuildingEvent __instance)
            {
                if (GlobalStateManager.isDedicatedServer) return;

                log.Log($"processBulkDemolishBuildingEvent: {__instance.demolitionAreaAABB_pos} {__instance.demolitionAreaAABB_size}");

                var clientCharacter = GameRoot.getClientCharacter();
                if (clientCharacter == null) return;

                var clientCharacterHash = clientCharacter.usernameHash;

                ulong characterHash = __instance.characterHash;
                if (characterHash != clientCharacterHash) return;

                var currentTerrainMode = Plugin.currentTerrainMode.Get();
                var pos = __instance.demolitionAreaAABB_pos;
                var size = __instance.demolitionAreaAABB_size;
                if (currentTerrainMode != TerrainMode.Ignore && currentTerrainMode != TerrainMode.LiquidOnly)
                {
                    GenerateShouldRemoveArray(false);
                    GenerateIsOreArray();

                    var useDestroyMode = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
                    if (currentTerrainMode == TerrainMode.Destroy || currentTerrainMode == TerrainMode.DestroyTerrainOnly) useDestroyMode = !useDestroyMode;

                    //if (GameRoot.IsMultiplayerEnabled) useDestroyMode = false;

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
                                    ActionManager.AddQueuedEvent(() => Rpc.Lockstep.Run(DestroyBuildingRPC, bogo.relatedEntityId));
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
                    var hasOre = false;
                    var hasNonOre = false;
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
                                        if (terrainData < isOre.Count && isOre[terrainData])
                                        {
                                            hasOre = true;
                                        }
                                        else if (terrainData > 1)
                                        {
                                            hasNonOre = true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (!_confirmationFrameOpen && useDestroyMode && !hasNonOre && hasOre)
                    {
                        _confirmationFrameOpen = true;
                        GlobalStateManager.addCursorRequirement();
                        ConfirmationFrame.Show("Destroy ore blocks?", () =>
                        {
                            GlobalStateManager.removeCursorRequirement();
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
                                                if (terrainData < isOre.Count && isOre[terrainData])
                                                {
                                                    ActionManager.AddQueuedEvent(() => _queuedTerrainRemovals.Enqueue(coords));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            _confirmationFrameOpen = false;
                        }, () =>
                        {
                            GlobalStateManager.removeCursorRequirement();
                            _confirmationFrameOpen = false;
                        });
                    }
                }

                if (removeLiquids.Get())
                {
                    ActionManager.AddQueuedEvent(() =>
                    {
                        var liquidSystem = GameRoot.World.Systems.Get<LiquidSystem>();
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

                                var chunkIndex = ChunkManager.calculateChunkIdx(chunkX, chunkZ);
                                byte[] liquidAmounts = null;
                                if (liquidSystem.tryGetLiquidChunk(chunkIndex, out var liquidChunk))
                                {
                                    liquidChunk.getDecompressedArrays(out var _, out liquidAmounts);
                                }

                                for (int z = fromZ; z <= toZ; ++z)
                                {
                                    for (int y = 0; y < size.y; ++y)
                                    {
                                        for (int x = fromX; x <= toX; ++x)
                                        {
                                            var coords = new Vector3Int(x, pos.y + y, z);
                                            if (liquidAmounts != null)
                                            {
                                                var tx = (uint)(coords.x - chunkX * Chunk.CHUNKSIZE_XZ);
                                                var ty = (uint)coords.y;
                                                var tz = (uint)(coords.z - chunkZ * Chunk.CHUNKSIZE_XZ);
                                                var terrainIndex = Chunk.getTerrainArrayIdx(tx, ty, tz);
                                                if (liquidAmounts[terrainIndex] > 0)
                                                {
                                                    GameRoot.addLockstepEvent(new SetLiquidCellEvent(coords.x, coords.y, coords.z, 0, 0));
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    });
                }
            }

            [HarmonyPatch(typeof(GameRoot), "updateInternal")]
            [HarmonyPrefix]
            private static void GameRoot_updateInternal(GameRoot __instance)
            {
                if (!GameRoot.IsGameInitDone) return;
                if (GameRoot.ExitToMainMenuCalled)
                {
                    _queuedTerrainRemovals.Clear();
                    return;
                }

                if (!_queuedTerrainRemovals.TryPeek(out var _)) return;
                if (Time.time < _lastTerrainRemovalUpdate + 0.1f) return;
                _lastTerrainRemovalUpdate = Time.time;

                var clientCharacter = GameRoot.getClientCharacter();
                if (clientCharacter == null) return;

                var characterHash = clientCharacter.usernameHash;

                while (_queuedTerrainRemovals.TryPeek(out var coords))
                {
                    _queuedTerrainRemovals.Dequeue();

                    Rpc.Lockstep.Run(DestroyTerrainRPC, coords.x, coords.y, coords.z);
                }
            }

            [HarmonyPatch(typeof(ResearchSystem), "onResearchFinished")]
            [HarmonyPostfix]
            static void ResearchSystem_onResearchFinished(ResearchTemplate rt)
            {
                if (shouldRemove != null) GenerateShouldRemoveArray(true);
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
                        switch (currentTerrainMode.Get())
                        {
                            case TerrainMode.Collect: currentTerrainMode.Set(/*GameRoot.IsMultiplayerEnabled ? TerrainMode.Ignore : */TerrainMode.Destroy); break;
                            case TerrainMode.Destroy: currentTerrainMode.Set(TerrainMode.Ignore); break;
                            case TerrainMode.Ignore: currentTerrainMode.Set(TerrainMode.CollectTerrainOnly); break;
                            case TerrainMode.CollectTerrainOnly: currentTerrainMode.Set(/*GameRoot.IsMultiplayerEnabled ? TerrainMode.Collect : */TerrainMode.DestroyTerrainOnly); break;
                            case TerrainMode.DestroyTerrainOnly: currentTerrainMode.Set(removeLiquids.Get() ? TerrainMode.LiquidOnly : TerrainMode.Collect); break;
                            case TerrainMode.LiquidOnly: currentTerrainMode.Set(TerrainMode.Collect); break;
                        }
                    }

                    var infoText = GameRoot.getSingleton().uiText_infoText.tmp.text;
                    if (!infoText.Contains("Terrain Mode:"))
                    {
                        switch (currentTerrainMode.Get())
                        {
                            case TerrainMode.Collect:
                            case TerrainMode.Destroy:
                            case TerrainMode.Ignore:
                                infoText += $"\nTerrain Mode: {currentTerrainMode.Get()}.";
                                break;

                            case TerrainMode.CollectTerrainOnly:
                                infoText += $"\nTerrain Mode: Collect Terrain. Ignore Buildings.";
                                break;

                            case TerrainMode.DestroyTerrainOnly:
                                infoText += $"\nTerrain Mode: Destroy Terrain. Ignore Buildings.";
                                break;

                            case TerrainMode.LiquidOnly:
                                infoText += $"\nTerrain Mode: Liquid Only.";
                                break;
                        }
                        if (/*!GameRoot.IsMultiplayerEnabled && */(int)_GameRoot_bulkDemolitionState.GetValue(GameRoot.getSingleton()) == 2)
                        {
                            switch (currentTerrainMode.Get())
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
}
