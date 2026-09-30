using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace CollectorDeployer
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class CollectorDeployerPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.dspmod.collectordeployer";
        public const string PLUGIN_NAME = "轨道采集器一键部署 (Collector Deployer)";
        public const string PLUGIN_VERSION = "1.0.0";

        private static readonly Harmony Harmony = new Harmony(PLUGIN_GUID);
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Harmony.PatchAll(typeof(UIFunctionPanelPatch));
            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载");
        }

        private void OnDestroy()
        {
            UIFunctionPanelPatch.DestroyButton();
            Harmony.UnpatchSelf();
        }
    }
}
