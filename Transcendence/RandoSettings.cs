namespace Transcendence
{
    public class RandoSettings
    {
        public bool AddCharms;
        
        [MenuChanger.Attributes.MenuRange(0, 14)]
        public int IncreaseMaxCharmCostBy;

        public LogicSettings Logic = new();

        public RandoSettings() {}

        public RandoSettings(GlobalSettings rs)
        {
            AddCharms = rs.AddCharms;
            IncreaseMaxCharmCostBy = rs.IncreaseMaxCharmCostBy;
            if (Modding.ModHooks.GetMod("Randomizer 4") != null)
            {
                Logic = LoadLogic(rs);
            }
        }

        private LogicSettings LoadLogic(GlobalSettings rs) =>
            RandomizerMod.RandomizerData.JsonUtil.DeserializeString<LogicSettings>(rs.LogicSettings);

        public bool Enabled() => AddCharms;
    }
}