using BepInEx;
using BepInEx.Configuration;
using UnhollowerRuntimeLib;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BulkDemolishTerrain
{
    [BepInPlugin(GUID, MODNAME, VERSION)]
    public class BepInExLoader : BepInEx.IL2CPP.BasePlugin
    {
        public const string
            MODNAME = "BulkDemolishTerrain",
            AUTHOR = "erkle64",
            GUID = "com." + AUTHOR + "." + MODNAME,
            VERSION = "1.0.0";

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
                ClassInjector.RegisterTypeInIl2Cpp<PluginComponent>();

                var go = new GameObject("Erkle64_BulkDemolishTerrain_PluginObject");
                go.AddComponent<PluginComponent>();
                Object.DontDestroyOnLoad(go);
            }
            catch
            {
                log.LogError("FAILED to Register Il2Cpp Type: PluginComponent!");
            }

            try
            {
                var harmony = new Harmony(GUID);

                var original = AccessTools.Method(typeof(Character.BulkDemolishBuildingEvent), "processEvent");
                var post = AccessTools.Method(typeof(PluginComponent), "processBulkDemolishBuildingEvent");
                harmony.Patch(original, postfix: new HarmonyMethod(post));

                original = AccessTools.Method(typeof(InputProxy), "Update");
                post = AccessTools.Method(typeof(PluginComponent), "Update");
                harmony.Patch(original, postfix: new HarmonyMethod(post));
            }
            catch
            {
                log.LogError("Harmony - FAILED to Apply Patch's!");
            }
        }
    }
}