using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;

namespace BulkDemolishTerrain
{
    public class ModMain
    {
        public const string
            MODNAME = "BulkDemolishTerrain",
            AUTHOR = "erkle64",
            GUID = "com." + AUTHOR + "." + MODNAME,
            VERSION = "1.1.0";

        [HarmonyPatch]
        public class Patch
        {
            private delegate void QueuedEventDelegate();

            private static Queue<QueuedEventDelegate> queuedEvents = new Queue<QueuedEventDelegate>();
            private static List<bool> shouldRemove = null;

            [HarmonyPatch(typeof(TooltipFrame), "_generateItemContainer")]
            [HarmonyPostfix]
            public static void TooltipFrame__generateItemContainer(TooltipFrame __instance, ItemTemplate itemTemplate, int itemCount)
            {
                if (itemTemplate != null)
                {
                    if (!__instance.uiContainer_item_custom.activeSelf)
                    {
                        __instance.uiContainer_item_custom.SetActive(true);
                        __instance.uiText_itemContent.setText("");
                    }
                    else
                    {
                        __instance.uiText_itemContent.setText(__instance.uiText_itemContent.tmp.text);
                    }

                    var text = __instance.uiText_itemContent.tmp.text;

                    text += "\n" + string.Format("Stack: {0}", itemTemplate.stackSize);

                    if (itemTemplate.buildableObjectTemplate != null)
                    {
                        text += "\n" + string.Format("Size: {0}x{1}x{2}", itemTemplate.buildableObjectTemplate.size.x, itemTemplate.buildableObjectTemplate.size.y, itemTemplate.buildableObjectTemplate.size.z);
                    }

                    __instance.uiText_itemContent.setText(text.TrimStart('\n'));
                }
            }

            [HarmonyPatch(typeof(InputProxy), nameof(GameCamera.Update))]
            [HarmonyPrefix]
            public static void Update()
            {
                int toProcess = queuedEvents.Count;
                if (toProcess > 40) toProcess = 40;
                //if (toProcess > 0) log.LogMessage(string.Format("Processing {0} events", toProcess));
                for (int i = 0; i < toProcess; ++i) queuedEvents.Dequeue().Invoke();
            }

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
                                    queuedEvents.Enqueue(() =>
                                    {
                                        ulong chunkIndex;
                                        uint blockIndex;
                                        byte terrainType = 0;
                                        ChunkManager.getChunkIdxAndTerrainArrayIdxFromWorldCoords(coords.x, coords.y, coords.z, out chunkIndex, out blockIndex);
                                        ChunkManager.chunks_removeTerrainBlock(chunkIndex, blockIndex, ref terrainType);
                                        ChunkManager.flagChunkVisualsAsDirty(chunkIndex, true, true);
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
