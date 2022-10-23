using BepInEx;
using BepInEx.Configuration;
using UnhollowerRuntimeLib;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Reflection;
using Il2CppSystem;

namespace BulkDemolishTerrain
{
    [BepInPlugin(GUID, MODNAME, VERSION)]
    public class BepInExLoader : BepInEx.IL2CPP.BasePlugin
    {
        public const string
            MODNAME = "BulkDemolishTerrain",
            AUTHOR = "erkle64",
            GUID = "com." + AUTHOR + "." + MODNAME,
            VERSION = "1.0.3";

        public static BepInEx.Logging.ManualLogSource log;

        public BepInExLoader()
        {
            log = Log;
        }

        public override void Load()
        {
            log.LogMessage("Registering PluginComponent in Il2Cpp");

            try
            {
                var harmony = new Harmony(GUID);
                harmony.PatchAll(typeof(Patch));
            }
            catch
            {
                log.LogError("Harmony - FAILED to Apply Patch's!");
            }
        }

        [HarmonyPatch]
        public class Patch
        {
            private delegate void QueuedEventDelegate();

            private static Queue<QueuedEventDelegate> queuedEvents = new Queue<QueuedEventDelegate>();
            private static List<bool> shouldRemove = null;

            [HarmonyPatch(typeof(InputProxy), nameof(InputProxy.Update))]
            [HarmonyPrefix]
            public static void Update()
            {
                int toProcess = queuedEvents.Count;
                if (toProcess > 20) toProcess = 20;
                if (toProcess > 0) BepInExLoader.log.LogMessage(string.Format("Processing {0} events", toProcess));
                for (int i = 0; i < toProcess; ++i) queuedEvents.Dequeue().Invoke();
            }

            [HarmonyPatch(typeof(Character.BulkDemolishBuildingEvent), nameof(Character.BulkDemolishBuildingEvent.processEvent))]
            [HarmonyPostfix]
            public static void processBulkDemolishBuildingEvent(Character.BulkDemolishBuildingEvent __instance)
            {
                //log.LogInfo("BulkDemolishTerrain processBulkDemolishBuildingEvent");

                var character = GameRoot.getClientCharacter();
                Debug.Assert(character != null);
                ulong characterHash = character.usernameHash;

                if (shouldRemove == null)
                {
                    var terrainTypes = ItemTemplateManager.getAllTerrainTemplates();

                    shouldRemove = new List<bool>();
                    shouldRemove.Add(false); // Air
                    shouldRemove.Add(false); // ???

                    foreach (var terrainType in terrainTypes)
                    {
                        shouldRemove.Add(terrainType.Value._isDestructible());
                        //BepInExLoader.log.LogMessage(string.Format("Terrain {0} {1} {2} {3} {4}", terrainType.Value.name, terrainType.Value.identifier, terrainType.Value.id, terrainType.Value._isOre(), terrainType.Value._isDestructible()));
                    }
                }

                var useDestroyMode = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

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
                                    queuedEvents.Enqueue(() =>
                                    {
                                        ulong chunkIndex;
                                        uint blockIndex;
                                        byte terrainType = 0;
                                        ChunkManager.getChunkIdxAndTerrainArrayIdxFromWorldCoords(coords.x, coords.y, coords.z, out chunkIndex, out blockIndex);
                                        ChunkManager.chunks_removeTerrainBlock(chunkIndex, blockIndex, ref terrainType);
                                        ChunkManager.flagChunkVisualsAsDirty(ChunkManager.getChunkByIdx(chunkIndex), true, true, true);
                                    });
                                }
                                else
                                {
                                    queuedEvents.Enqueue(() => GameRoot.addLockstepEvent(new Character.RemoveTerrainEvent(characterHash, coords, 0)));
                                }
                            }
                        }
                    }
                }

                return;
            }
        }
    }
}