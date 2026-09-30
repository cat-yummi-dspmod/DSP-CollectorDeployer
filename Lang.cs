namespace CollectorDeployer
{
    internal static class Lang
    {
        public static bool IsChinese
        {
            get
            {
                int lcid = Localization.CurrentLanguageLCID;
                return lcid == 2052 || lcid == 1028;
            }
        }

        public static string T(string zh, string en) => IsChinese ? zh : en;

        public static string ButtonLabel => T("一键部署\n轨道采集器", "Deploy Orbital\nCollectors");
        public static string TipTitle => T("一键部署轨道采集器", "Deploy Orbital Collectors");
        public static string TipText => T(
            $"在赤道上按格点均匀部署 {CollectorDeploy.CollectorCount} 个轨道采集器，不在格点上的会被拆除重建。",
            $"Deploys {CollectorDeploy.CollectorCount} orbital collectors evenly on the equator grid; off-grid ones are rebuilt.");

        public static string GalaxyButtonLabel => T("一键部署\n全银河", "Deploy in\nWhole Galaxy");
        public static string GalaxyTipTitle => T("全银河部署轨道采集器", "Deploy Orbital Collectors in the Whole Galaxy");
        public static string GalaxyTipText => T(
            $"在全银河所有气态巨星（包括没去过的）的赤道上各部署 {CollectorDeploy.CollectorCount} 个轨道采集器，执行前会显示汇总。",
            $"Deploys {CollectorDeploy.CollectorCount} orbital collectors on the equator of every gas giant in the galaxy, including unvisited ones. A summary is shown first.");
    }
}
