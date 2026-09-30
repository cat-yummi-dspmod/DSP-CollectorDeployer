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
    }
}
