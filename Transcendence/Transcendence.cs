using System;
using System.IO;
using System.Collections;
using Modding;
using Modding.Menu;
using Modding.Menu.Config;
using UnityEngine;
using SFCore;
using ItemChanger;
using ItemChanger.Modules;
using ItemChanger.Locations;
using ItemChanger.Items;
using ItemChanger.Tags;
using ItemChanger.Placements;
using ItemChanger.UIDefs;
using MenuChanger;
using MenuChanger.MenuElements;
using MenuChanger.MenuPanels;
using MenuChanger.Extensions;
using RandomizerMod;
using RandomizerMod.Menu;
using RandomizerMod.Settings;
using RandomizerMod.Logging;
using RandomizerMod.RandomizerData;
using RandomizerMod.RC;
using RandomizerCore;
using RandomizerCore.Logic;
using RandomizerCore.LogicItems;

namespace Transcendence
{
    public class Transcendence : Mod, ILocalSettings<SaveSettings>, IGlobalSettings<GlobalSettings>, ICustomMenuMod
    {
        private static List<Charm> Charms = new() 
        {
            AntigravityAmulet.Instance,
            BluemothWings.Instance,
            LemmsStrength.Instance,
            FloristsBlessing.Instance,
            // needs to hook after the previous two so that the player can't negate
            // the drawback of Snail Slash with them
            SnailSlash.Instance,
            SnailSoul.Instance,
            ShamanAmp.Instance,
            NitroCrystal.Instance,
            Crystalmaster.Instance,
            DisinfectantFlask.Instance,
            MillibellesBlessing.Instance,
            Greedsong.Instance,
            MarissasAudience.Instance,
            ChaosOrb.Instance
        };

        internal static Transcendence Instance;

        private Dictionary<string, Func<bool, bool>> BoolGetters = new();
        private Dictionary<string, Action<bool>> BoolSetters = new();
        private Dictionary<string, Func<int, int>> IntGetters = new();
        private Dictionary<(string, string), Action<PlayMakerFSM>> FSMEdits = new();
        private List<(int Period, Action Func)> Tickers = new();

        public override void Initialize()
        {
            Log("Initializing");
            Instance = this;
            foreach (var charm in Charms)
            {
                var num = CharmHelper.AddSprites(EmbeddedSprites.Get(charm.Sprite))[0];
                charm.Num = num;
                if (!(charm == ChaosOrb.Instance))
                {
                    ChaosOrb.Instance.AddCustomCharm(num);
                }
                var settings = charm.Settings;
                IntGetters[$"charmCost_{num}"] = _ => (Equipped(ChaosOrb.Instance) && ChaosOrb.Instance.GivingCharm(num)) ? 0 : settings(Settings).Cost;
                AddTextEdit($"CHARM_NAME_{num}", "UI", charm.Name);
                AddTextEdit($"CHARM_DESC_{num}", "UI", () => charm.Description);
                BoolGetters[$"equippedCharm_{num}"] = _ => settings(Settings).Equipped || (Equipped(ChaosOrb.Instance) && ChaosOrb.Instance.GivingCharm(num));
                BoolSetters[$"equippedCharm_{num}"] = charm == ChaosOrb.Instance ?
                    (value => settings(Settings).Equipped = value || Settings.ChaosMode)
                     : (value => settings(Settings).Equipped = value);
                BoolGetters[$"gotCharm_{num}"] = _ => settings(Settings).Got;
                BoolSetters[$"gotCharm_{num}"] = value => settings(Settings).Got = value;
                BoolGetters[$"newCharm_{num}"] = _ => settings(Settings).New;
                BoolSetters[$"newCharm_{num}"] = value => settings(Settings).New = value;
                charm.Hook();
                foreach (var edit in charm.FsmEdits)
                {
                    AddFsmEdit(edit.obj, edit.fsm, edit.edit);
                }
                Tickers.AddRange(charm.Tickers);

                var item = new ItemChanger.Items.CharmItem() { 
                    charmNum = charm.Num,
                    name = charm.Name.Replace(" ", "_"),
                    UIDef = new MsgUIDef() { 
                        name = new LanguageString("UI", $"CHARM_NAME_{charm.Num}"),
                        shopDesc = new LanguageString("UI", $"CHARM_DESC_{charm.Num}"),
                        sprite = new EmbeddedSprite() { key = charm.Sprite }
                    }};
                // Tag the item for ConnectionMetadataInjector, so that MapModS and
                // other mods recognize the items we're adding as charms.
                var mapmodTag = item.AddTag<InteropTag>();
                mapmodTag.Message = "RandoSupplementalMetadata";
                mapmodTag.Properties["ModSource"] = GetName();
                mapmodTag.Properties["PoolGroup"] = "Charms";
                Finder.DefineCustomItem(item);
            }
            for (var i = 1; i <= 40; i++)
            {
                var num = i; // needed for closure to capture a different copy of the variable each time
                BoolGetters[$"equippedCharm_{num}"] = value => value || (Equipped(ChaosOrb.Instance) && ChaosOrb.Instance.GivingCharm(num));
                IntGetters[$"charmCost_{num}"] = value => (Equipped(ChaosOrb.Instance) && ChaosOrb.Instance.GivingCharm(num)) ? 0 : value;
            }

            ModHooks.GetPlayerBoolHook += ReadCharmBools;
            ModHooks.SetPlayerBoolHook += WriteCharmBools;
            ModHooks.GetPlayerIntHook += ReadCharmCosts;
            ModHooks.LanguageGetHook += GetCharmStrings;
            // This will run after Rando has already set up its item placements.
            On.UIManager.StartNewGame += PlaceItems;
            On.PlayMakerFSM.OnEnable += EditFSMs;
            // This hook is set before ItemChanger's, so AutoSalubraNotches will take our charms into account.
            On.PlayerData.CountCharms += CountOurCharms;
            On.PlayerData.UnequipCharm += BlockChaosOrbUnequip;
            StartTicking();

            if (ModHooks.GetMod("Randomizer 4") != null)
            {
                // The code that references rando needs to be in a separate method
                // so that the mod will still load without it installed
                // (trying to run a method whose code references an unavailable
                // DLL will fail even if the code in question isn't actually run)
                HookRando();
            }
            if (ModHooks.GetMod("DebugMod") != null)
            {
                DebugModHook.GiveAllCharms(() => {
                    GrantAllOurCharms();
                    PlayerData.instance.CountCharms();
                });
            }
        }

        // breaks infinite loop when reading equippedCharm_X
        private bool Equipped(Charm c) => c.Settings(Settings).Equipped;

        private Dictionary<(string Key, string Sheet), Func<string>> TextEdits = new();

        internal void AddTextEdit(string key, string sheetName, string text)
        {
            TextEdits.Add((key, sheetName), () => text);
        }

        internal void AddTextEdit(string key, string sheetName, Func<string> text)
        {
            TextEdits.Add((key, sheetName), text);
        }

        public override string GetVersion() => "1.2";

        internal SaveSettings Settings = new();

        public void OnLoadLocal(SaveSettings s)
        {
            Settings = s;
            FloristsBlessing.Instance.Broken = s.FloristsBlessingBroken;
            ChaosOrb.Instance.GivenCharms = s.ChaosOrbGivenCharms;
        }

        public SaveSettings OnSaveLocal()
        {
            Settings.FloristsBlessingBroken = FloristsBlessing.Instance.Broken;
            Settings.ChaosOrbGivenCharms = ChaosOrb.Instance.GivenCharms;
            return Settings;
        }

        internal GlobalSettings ModSettings = new();

        public void OnLoadGlobal(GlobalSettings s)
        {
            ModSettings = s;
            RandoSettings = new(s);
        }

        public GlobalSettings OnSaveGlobal()
        {
            ModSettings.AddCharms = RandoSettings.AddCharms;
            ModSettings.IncreaseMaxCharmCostBy = RandoSettings.IncreaseMaxCharmCostBy;
            return ModSettings;
        }

        private bool ReadCharmBools(string boolName, bool value)
        {
            if (BoolGetters.TryGetValue(boolName, out var f))
            {
                return f(value);
            }
            return value;
        }

        private bool WriteCharmBools(string boolName, bool value)
        {
            if (BoolSetters.TryGetValue(boolName, out var f))
            {
                f(value);
            }
            return value;
        }

        private int ReadCharmCosts(string intName, int value)
        {
            if (IntGetters.TryGetValue(intName, out var cost))
            {
                return cost(value);
            }
            return value;
        }

        private string GetCharmStrings(string key, string sheetName, string orig)
        {
            if (TextEdits.TryGetValue((key, sheetName), out var text))
            {
                return text();
            }
            return orig;
        }

        internal void AddFsmEdit(string objName, string fsmName, Action<PlayMakerFSM> edit)
        {
            var key = (objName, fsmName);
            var newEdit = edit;
            if (FSMEdits.TryGetValue(key, out var orig))
            {
                newEdit = fsm => {
                    orig(fsm);
                    edit(fsm);
                };
            }
            FSMEdits[key] = newEdit;
        }

        private void EditFSMs(On.PlayMakerFSM.orig_OnEnable orig, PlayMakerFSM fsm)
        {
            orig(fsm);
            if (FSMEdits.TryGetValue((fsm.gameObject.name, fsm.FsmName), out var edit))
            {
                edit(fsm);
            }
        }

        private void StartTicking()
        {
            // Use our own object to hold timers so that GameManager.StopAllCoroutines
            // does not kill them.
            var timerHolder = new GameObject("Timer Holder");
            GameObject.DontDestroyOnLoad(timerHolder);
            var timers = timerHolder.AddComponent<DummyMonoBehaviour>();
            foreach (var t in Tickers)
            {
                IEnumerator ticker()
                {
                    while (true)
                    {
                        try
                        {
                            t.Func();
                        }
                        catch (Exception ex)
                        {
                            LogError(ex);
                        }
                        yield return new WaitForSeconds(t.Period);
                    }
                }

                timers.StartCoroutine(ticker());
            }
        }

        private void CountOurCharms(On.PlayerData.orig_CountCharms orig, PlayerData self)
        {
            orig(self);
            self.SetInt("charmsOwned", self.GetInt("charmsOwned") + Charms.Count(c => c.Settings(Settings).Got));
        }

        private void PlaceItems(On.UIManager.orig_StartNewGame orig, UIManager self, bool permaDeath, bool bossRush)
        {
            Settings.ChaosMode = ModSettings.ChaosMode;

            ItemChangerMod.CreateSettingsProfile(overwrite: false, createDefaultModules: false);
            if (ModHooks.GetMod("Randomizer 4") != null && IsRandoActive())
            {
                PlaceItemsRando();
                
            }
            else
            {
                ConfigureICModules();
                PlaceCharmsAtFixedPositions();
                PlaceFloristsBlessingRepair();
                SetDefaultNotchCosts();
            }
            // Even in rando, we want to add the starting Chaos Orb directly rather
            // than going through the RequestBuilder because doing it that way would
            // cause placements to change.
            if (Settings.ChaosMode)
            {
                GrantFreeChaosOrb();
            }

            if (bossRush)
            {
                GrantAllOurCharms();
            }
            
            orig(self, permaDeath, bossRush);
        }

        private void PlaceItemsRando()
        {
            if (RandomizerMod.RandomizerMod.RS.GenerationSettings.MiscSettings.RandomizeNotchCosts)
            {
                RandomizeNotchCosts(RandomizerMod.RandomizerMod.RS.GenerationSettings.Seed);
            }
            else
            {
                SetDefaultNotchCosts();
            }

            if (RandomizerMod.RandomizerMod.RS.GenerationSettings.PoolSettings.Charms)
            {
                if (RandoSettings.AddCharms)
                {
                    PlaceFloristsBlessingRepair();
                }
            }
            else
            {
                PlaceCharmsAtFixedPositions();
            }
        }

        private static void PlaceCharmsAtFixedPositions()
        {
            var placements = new List<AbstractPlacement>();
            foreach (var charm in Charms)
            {
                var name = charm.Name.Replace(" ", "_");
                placements.Add(
                    new CoordinateLocation() { x = charm.X, y = charm.Y, elevation = 0, sceneName = charm.Scene, name = name }
                    .Wrap()
                    .Add(Finder.GetItem(name)));
            }
            ItemChangerMod.AddPlacements(placements, conflictResolution: PlacementConflictResolution.Ignore);
        }

        private void GrantFreeChaosOrb()
        {
            // Use MergeKeepingOld so that we don't conflict with any starting items
            // that rando gives.
            ItemChangerMod.AddPlacements(new List<AbstractPlacement>()
            {
                Finder.GetLocation("Start").Wrap().Add(Finder.GetItem(ChaosOrb.Instance.InternalName)),
            }, conflictResolution: PlacementConflictResolution.MergeKeepingOld);
            Settings.ChaosOrb.Cost = 0;
            Settings.ChaosOrb.Equipped = true;
            PlayerData.instance.EquipCharm(ChaosOrb.Instance.Num);
            ChaosOrb.Instance.RerollCharms();
            PlayerData.instance.CountCharms();
        }

        private void BlockChaosOrbUnequip(On.PlayerData.orig_UnequipCharm orig, PlayerData pd, int charmNum)
        {
            if (!(charmNum == ChaosOrb.Instance.Num && Settings.ChaosMode))
            {
                orig(pd, charmNum);
            }
        }

        private static void PlaceFloristsBlessingRepair()
        {
            var repairPlacement = new CoordinateLocation() { x = 72.0f, y = 3.4f, elevation = 0, sceneName = "RestingGrounds_12", name = "Florist's_Blessing_Repair" }.Wrap() as MutablePlacement;
            repairPlacement.Cost = new RecurringGeoCost(FloristsBlessing.RepairCost);
            repairPlacement.Add(new FloristsBlessingRepairItem());
            ItemChangerMod.AddPlacements(new List<AbstractPlacement>() {repairPlacement}, conflictResolution: PlacementConflictResolution.Ignore);
        }

        private const int MinTotalCost = 22;
        private const int MaxTotalCost = 35;

        private void RandomizeNotchCosts(int seed)
        {
            // This log statement is here to help diagnose a possible bug where charms cost more than
            // they ever should.
            var rng = new System.Random(seed);
            var total = rng.Next(MinTotalCost, MaxTotalCost + 1);
            Log($"Randomizing notch costs; total cost = {total}");
            for (var i = 0; i < total; i++)
            {
                var possiblePicks = Charms.Select(x => x.Settings(Settings)).Where(s => s.Cost < 6).ToList();
                if (possiblePicks.Count == 0)
                {
                    break;
                }
                var pick = rng.Next(possiblePicks.Count);
                possiblePicks[pick].Cost++;
            }
        }

        private void HookRando()
        {
            RequestBuilder.OnUpdate.Subscribe(-498, DefineCharmsForRando);
            RequestBuilder.OnUpdate.Subscribe(-200, IncreaseMaxCharmCost);
            RequestBuilder.OnUpdate.Subscribe(50, AddCharmsToPool);
            RCData.RuntimeLogicOverride.Subscribe(50, DefineLogicItems);
            RandomizerMenuAPI.AddMenuPage(BuildMenu, BuildButton);
            SettingsLog.AfterLogSettings += LogRandoSettings;
        }

        // This is actually a MenuPage, but we can't use that as the static type because then this mod won't
        // load without MenuChanger installed because the runtime can't load the type of the field.
        private object SettingsPage;
        private RandoSettings RandoSettings = new(new GlobalSettings());

        private void BuildMenu(MenuPage landingPage)
        {
            var sp = new MenuPage(GetName(), landingPage);
            SettingsPage = sp;
            var factory = new MenuElementFactory<RandoSettings>(sp, RandoSettings);
            new VerticalItemPanel(sp, new(0, 300), 75f, true, factory.Elements);
        }

        private bool BuildButton(MenuPage landingPage, out SmallButton settingsButton)
        {
            settingsButton = new(landingPage, GetName());
            settingsButton.AddHideAndShowEvent(landingPage, (MenuPage)SettingsPage);
            return true;
        }

        private void LogRandoSettings(LogArguments args, TextWriter w)
        {
            w.WriteLine("Logging Transcendence settings:");
            w.WriteLine(JsonUtil.Serialize(RandoSettings));
        }

        bool ICustomMenuMod.ToggleButtonInsideMenu => false;

        public MenuScreen GetMenuScreen(MenuScreen prevScreen, ModToggleDelegates? dels)
        {
            var builder = MenuUtils.CreateMenuBuilderWithBackButton("Transcendence", prevScreen, out _);
            builder.AddContent(
                RegularGridLayout.CreateVerticalLayout(105f),
                c => {
                    c.AddHorizontalOption("Chaos Mode", new HorizontalOptionConfig()
                    {
                        Label = "Chaos Mode",
                        Description = new DescriptionInfo()
                        {
                            Text = "Start with 0-cost Chaos Orb permanently equipped."
                        },
                        Options = new[] { "Off", "On" },
                        ApplySetting = (_, i) =>
                        {
                            ModSettings.ChaosMode = i == 1;
                        },
                        RefreshSetting = (s, _) => s.optionList.SetOptionTo(ModSettings.ChaosMode ? 1 : 0),
                        CancelAction = _ => UIManager.instance.UIGoToDynamicMenu(prevScreen),
                        Style = HorizontalOptionStyle.VanillaStyle
                    }, out var chaosOption);
                    chaosOption.menuSetting.RefreshValueFromGameSettings();
                }
            );
            return builder.Build();
        }

        private void SetDefaultNotchCosts()
        {
            foreach (var charm in Charms)
            {
                charm.Settings(Settings).Cost = charm.DefaultCost;
            }
        }

        private static void DefineCharmsForRando(RequestBuilder rb)
        {
            if (!rb.gs.PoolSettings.Charms)
            {
                return;
            }
            var names = new HashSet<string>();
            foreach (var charm in Charms)
            {
                var name = charm.Name.Replace(" ", "_");
                names.Add(name);
                rb.EditItemRequest(name, info =>
                {
                    info.getItemDef = () => new()
                    {
                        Name = name,
                        Pool = "Charm",
                        MajorItem = false,
                        PriceCap = 666
                    };
                });
            }

            rb.OnGetGroupFor.Subscribe(0f, (RequestBuilder rb, string item, RequestBuilder.ElementType type, out GroupBuilder gb) => {
                if (names.Contains(item) && (type == RequestBuilder.ElementType.Unknown || type == RequestBuilder.ElementType.Item))
                {
                    gb = rb.GetGroupFor("Shaman_Stone");
                    return true;
                }
                gb = default;
                return false;
            });
        }

        private void IncreaseMaxCharmCost(RequestBuilder rb)
        {
            if (RandoSettings.AddCharms)
            {
                rb.gs.CostSettings.MaximumCharmCost += RandoSettings.IncreaseMaxCharmCostBy;
            }
        }

        private static void DefineLogicItems(GenerationSettings gs, LogicManagerBuilder lmb)
        {
            if (!gs.PoolSettings.Charms)
            {
                return;
            }
            foreach (var charm in Charms)
            {
                var name = charm.Name.Replace(" ", "_");
                var term = lmb.GetOrAddTerm(name);
                var oneOf = new TermValue(term, 1);
                lmb.AddItem(new CappedItem(name, new TermValue[]
                {
                    oneOf,
                    new TermValue(lmb.GetTerm("CHARMS"), 1)
                }, oneOf));
            }
        }

        private void AddCharmsToPool(RequestBuilder rb)
        {
            if (!(rb.gs.PoolSettings.Charms && RandoSettings.AddCharms))
            {
                return;
            }
            foreach (var charm in Charms)
            {
                rb.AddItemByName(charm.Name.Replace(" ", "_"));
            }
        }

        private static bool IsRandoActive() =>
            RandomizerMod.RandomizerMod.RS?.GenerationSettings != null;

        private static void ConfigureICModules()
        {
            // Just to add the hook that Chaos Orb uses to turn on Fury.
            ItemChangerMod.Modules.GetOrAdd<FixFury>();
            ItemChangerMod.Modules.GetOrAdd<LeftCityChandelier>();
            ItemChangerMod.Modules.GetOrAdd<PlayerDataEditModule>();
            ItemChangerMod.Modules.GetOrAdd<RespawnCollectorJars>();
            ItemChangerMod.Modules.GetOrAdd<TransitionFixes>();
        }

        internal static void UpdateNailDamage()
        {
            IEnumerator WaitThenUpdate()
            {
                yield return null;
                PlayMakerFSM.BroadcastEvent("UPDATE NAIL DAMAGE");
            }
            GameManager.instance.StartCoroutine(WaitThenUpdate());
        }

        private void GrantAllOurCharms()
        {
            foreach (var charm in Charms)
            {
                charm.Settings(Settings).Got = true;
            }
        }
    }
}
