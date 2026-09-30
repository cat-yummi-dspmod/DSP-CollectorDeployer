using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CollectorDeployer
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class CollectorDeployerPlugin : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.yummi.dsp.collectordeployer";
        public const string PLUGIN_NAME = "轨道采集器一键部署 (Collector Deployer)";
        public const string PLUGIN_VERSION = "1.1.0";

        private static readonly Harmony Harmony = new Harmony(PLUGIN_GUID);
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> EnableGalaxyDeploy;

        private void Awake()
        {
            Log = Logger;
            EnableGalaxyDeploy = Config.Bind("General", "EnableGalaxyDeploy", false,
                "Show a second button that deploys 40 orbital collectors on every gas giant in the galaxy, including ones you have never visited.\n" +
                "显示第二个按钮，在全银河所有气态巨星（包括没去过的）上各部署 40 个轨道采集器。");

            Harmony.PatchAll(typeof(UIFunctionPanelPatch));
            Log.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} 已加载");
        }

        private void OnDestroy()
        {
            UIFunctionPanelPatch.DestroyButtons();
            Harmony.UnpatchSelf();
        }
    }
}
