using C3.ModKit;
using HarmonyLib;
using Unfoundry;
using System.Collections.Generic;
using UnityEngine;

namespace BulkDemolishTerrain
{
    [UnfoundryMod(Plugin.GUID)]
    public class Plugin : UnfoundryPlugin
    {
        public const string
            MODNAME = "BulkDemolishTerrain",
            AUTHOR = "erkle64",
            GUID = AUTHOR + "." + MODNAME,
            VERSION = "1.3.0";

        public static LogSource log;

        public Plugin()
        {
            log = new LogSource(MODNAME);
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
                Debug.Log("BulkDemolishTerrain processBulkDemolishBuildingEvent");

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
                        shouldRemove.Add(terrainType.Value.destructible);
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
        }
    }
}
