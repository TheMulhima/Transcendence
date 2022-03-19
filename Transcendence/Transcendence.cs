using Modding;
using SFCore;
using ItemChanger;
using ItemChanger.Modules;
using ItemChanger.Locations;
using ItemChanger.Items;
using ItemChanger.UIDefs;
using RandomizerMod;

namespace Transcendence
{
    public class Transcendence : Mod, ILocalSettings<SaveSettings>
    {
        private static List<Charm> Charms = new() {
            new() {
                Sprite = "AntigravityAmulet.png",
                Name = "Antigravity Amulet",
                Description = "Used by shamans to float around.\n\nDecreases the effect of gravity on the bearer, allowing them to leap to greater heights.",
                Cost = 2,
                SettingsBools = s => s.AntigravityAmulet,
                Hook = AntigravityAmulet.Hook,
                Scene = "Mines_28",
                X = 5.1f,
                Y = 27.4f
            },
            new() {
                Sprite = "BluemothWings.png",
                Name = "Bluemoth Wings",
                Description = "A charm made from the wings of a rare blue bug.\n\nAllows the bearer to jump repeatedly in the air in exchange for Geo.",
                Cost = 2,
                SettingsBools = s => s.BluemothWings,
                Hook = BluemothWings.Hook,
                Scene = "Fungus1_17",
                X = 71.5f,
                Y = 24.4f
            },
            new() {
                Sprite = "FloristsMask.png",
                Name = "Florist's Mask",
                Description = "A charm made in the image of the keepers of the Mosskin's lands.\n\nThe bearer may earn Geo by delivering flowers to the denizens of Hallownest.",
                Cost = 1,
                SettingsBools = s => s.FloristsMask,
                Hook = FloristsMask.Hook,
                Scene = "Room_Slug_Shrine",
                X = 29.2f,
                Y = 6.4f
            },
            new() {
                Sprite = "ShamanAmp.png",
                Name = "Shaman Amp",
                Description = "Forgotten shaman artifact, used by wealthy shamans to strike fear in foes.\n\nIncreases the size of spells in proportion to the amount of Geo held.",
                Cost = 4,
                SettingsBools = s => s.ShamanAmp,
                Hook = ShamanAmp.Hook,
                Scene = "Deepnest_East_04",
                X = 27.5f,
                Y = 80.4f
            },
            new() {
                Sprite = "NitroCrystal.png",
                Name = "Nitro Crystal",
                Description = "A crystal vessel filled with a dangerously explosive substance.\n\nGreatly increases the speed and damage of the bearer's Super Dashes.",
                Cost = 4,
                SettingsBools = s => s.NitroCrystal,
                Hook = NitroCrystal.Hook,
                Scene = "Mines_13",
                X = 25.6f,
                Y = 21.5f
            },
            new() {
                Sprite = "Crystalmaster.png",
                Name = "Crystalmaster",
                Description = "Bears the likeness of a crystallised bug known only as 'The Crystalmaster'.\n\nGreatly increases the running speed of the bearer, in exchange for Geo. The increase is stronger the richer they are.",
                Cost = 2,
                SettingsBools = s => s.Crystalmaster,
                Hook = Crystalmaster.Hook,
                Scene = "Mines_25",
                X = 28.1f,
                Y = 95.4f
            }
        };

        private Dictionary<string, Func<bool>> BoolGetters = new();
        private Dictionary<string, Action<bool>> BoolSetters = new();
        private Dictionary<string, int> Ints = new();
        private Dictionary<(string, string), Action<PlayMakerFSM>> FSMEdits = new();

        public override void Initialize()
        {
            foreach (var charm in Charms)
            {
                var num = CharmHelper.AddSprites(EmbeddedSprites.Get(charm.Sprite))[0];
                charm.Num = num;
                Ints[$"charmCost_{num}"] = charm.Cost;
                AddTextEdit($"CHARM_NAME_{num}", "UI", charm.Name);
                AddTextEdit($"CHARM_DESC_{num}", "UI", charm.Description);
                var bools = charm.SettingsBools;
                var equipped = () => bools(Settings).Equipped;
                BoolGetters[$"equippedCharm_{num}"] = equipped;
                BoolSetters[$"equippedCharm_{num}"] = value => bools(Settings).Equipped = value;
                BoolGetters[$"gotCharm_{num}"] = () => bools(Settings).Got;
                BoolSetters[$"gotCharm_{num}"] = value => bools(Settings).Got = value;
                BoolGetters[$"newCharm_{num}"] = () => bools(Settings).New;
                BoolSetters[$"newCharm_{num}"] = value => bools(Settings).New = value;
                charm.Hook(equipped, this);
            }

            ModHooks.GetPlayerBoolHook += ReadCharmBools;
            ModHooks.SetPlayerBoolHook += WriteCharmBools;
            ModHooks.GetPlayerIntHook += ReadCharmCosts;
            ModHooks.LanguageGetHook += GetCharmStrings;
            // This will run after Rando has already set up its item placements.
            On.UIManager.StartNewGame += PlaceItems;
            On.PlayMakerFSM.OnEnable += EditFSMs;
        }

        private Dictionary<(string Key, string Sheet), Func<string>> TextEdits = new();

        internal void AddTextEdit(string key, string sheetName, string text)
        {
            TextEdits.Add((key, sheetName), () => text);
        }

        public override string GetVersion() => "1.0";

        private SaveSettings Settings = new();

        public void OnLoadLocal(SaveSettings s)
        {
            Settings = s;
        }

        public SaveSettings OnSaveLocal() => Settings;

        private bool ReadCharmBools(string boolName, bool value)
        {
            if (BoolGetters.TryGetValue(boolName, out var f))
            {
                var v = f();
                return v;
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
            if (Ints.TryGetValue(intName, out var cost))
            {
                return cost;
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
            FSMEdits[(objName, fsmName)] = edit;
        }

        private void EditFSMs(On.PlayMakerFSM.orig_OnEnable orig, PlayMakerFSM fsm)
        {
            orig(fsm);
            if (FSMEdits.TryGetValue((fsm.gameObject.name, fsm.FsmName), out var edit))
            {
                edit(fsm);
            }
        }

        private static void PlaceItems(On.UIManager.orig_StartNewGame orig, UIManager self, bool permaDeath, bool bossRush)
        {
            ItemChangerMod.CreateSettingsProfile(overwrite: false);

            var placements = new List<AbstractPlacement>();

            foreach (var charm in Charms)
            {
                var name = charm.Name.Replace(" ", "_");
                placements.Add(
                    new CoordinateLocation() { x = charm.X, y = charm.Y, elevation = 0, sceneName = charm.Scene, name = name }
                    .Wrap()
                    .Add(new ItemChanger.Items.CharmItem() { charmNum = charm.Num, name = name, UIDef =
                        new MsgUIDef() { 
                            name = new LanguageString("UI", $"CHARM_NAME_{charm.Num}"),
                            shopDesc = new LanguageString("UI", $"CHARM_DESC_{charm.Num}"),
                            sprite = new EmbeddedSprite() { key = charm.Sprite }
                        }}));
            }

            ItemChangerMod.AddPlacements(placements, conflictResolution: PlacementConflictResolution.Ignore);

            orig(self, permaDeath, bossRush);
        }

        private static bool IsRandoActive() =>
            ModHooks.GetMod("Randomizer 4") != null && RandomizerMod.RandomizerMod.RS?.GenerationSettings != null;

        private static void ConfigureICModules()
        {
            if (!IsRandoActive())
            {
                ItemChangerMod.Modules.Remove<AutoUnlockIselda>();
                ItemChangerMod.Modules.Remove<BaldurHealthCap>();
                ItemChangerMod.Modules.Remove<CliffsShadeSkipAssist>();
                ItemChangerMod.Modules.Remove<DreamNailCutsceneEvent>();
                ItemChangerMod.Modules.Remove<FastGrubfather>();
                ItemChangerMod.Modules.Remove<GreatHopperEasterEgg>();
                ItemChangerMod.Modules.Remove<InventoryTracker>();
                ItemChangerMod.Modules.Remove<MenderbugUnlock>();
                ItemChangerMod.Modules.Remove<NonlinearColosseums>();
                ItemChangerMod.Modules.Remove<PreventLegEaterDeath>();
                ItemChangerMod.Modules.Remove<PreventZoteDeath>();
                ItemChangerMod.Modules.Remove<RemoveVoidHeartEffects>();
                ItemChangerMod.Modules.Remove<ReusableBeastsDenEntrance>();
                ItemChangerMod.Modules.Remove<ReusableCityCrestGate>();
                ItemChangerMod.Modules.Remove<ReverseBeastDenPath>();
                ItemChangerMod.Modules.Remove<RightCityPlatform>();
            }
        }
    }
}
