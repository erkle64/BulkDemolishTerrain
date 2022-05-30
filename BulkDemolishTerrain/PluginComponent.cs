using System;
using System.Collections.Generic;
using UnhollowerBaseLib;
using HarmonyLib;
using UnityEngine;

namespace BulkDemolishTerrain
{
    public class PluginComponent : MonoBehaviour
    {
        private static Queue<GameRoot.LockstepEvent> queuedEvents = new Queue<GameRoot.LockstepEvent>();

        public PluginComponent(IntPtr ptr) : base(ptr)
        {
        }

        public static void Update()
        {
            int toProcess = queuedEvents.Count;
            if (toProcess > 20) toProcess = 20;
            if (toProcess > 0) BepInExLoader.log.LogMessage(string.Format("Processing {0} events", toProcess));
            for (int i = 0; i < toProcess; ++i) GameRoot.addLockstepEvent(queuedEvents.Dequeue());
        }
        
        [HarmonyPostfix]
        public static void processBulkDemolishBuildingEvent(Character.BulkDemolishBuildingEvent __instance)
        {
            ulong characterHash = 0;
            for (var en = CharacterManager.singleton.list_charactersInWorld.System_Collections_IEnumerable_GetEnumerator().Current.Cast<Il2CppSystem.Collections.Generic.LinkedListNode<Character>>(); en != null && en.item != null; en = en.Next)
            {
                var character = en.item;
                if (character.sessionOnly_isClientCharacter)
                {
                    characterHash = character.usernameHash;
                    break;
                }
            }
            if (characterHash == 0)
            {
                BepInExLoader.log.LogMessage("Failed to find client character");
                return;
            }

            var pos = __instance.demolitionAreaAABB_pos;
            var size = __instance.demolitionAreaAABB_size;
            for (int z = 0; z < size.z; ++z)
            {
                for (int y = 0; y < size.y; ++y)
                {
                    for (int x = 0; x < size.x; ++x)
                    {
                        queuedEvents.Enqueue(new Character.RemoveTerrainEvent(characterHash, pos + new Vector3Int(x, y, z), 0));
                    }
                }
            }
        }
    }
}
