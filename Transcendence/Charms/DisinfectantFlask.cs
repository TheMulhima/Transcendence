using Modding;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using USM = UnityEngine.SceneManagement;

namespace Transcendence
{
    internal class DisinfectantFlask : Charm
    {
        public static readonly DisinfectantFlask Instance = new();

        private DisinfectantFlask() {}

        public override string Sprite => "DisinfectantFlask.png";
        public override string Name => "Disinfectant Flask";
        public override string Description => "A vessel containing pure, concentrated lifeblood.\n\nCleans infected areas around the bearer.";
        public override int DefaultCost => 1;
        public override string Scene => "Deepnest_East_15";
        public override float X => 30.8f;
        public override float Y => 4.4f;

        public override CharmSettings Settings(SaveSettings s) => s.DisinfectantFlask;

        public override void Hook()
        {
            ModHooks.GetPlayerBoolHook += DisinfectCrossroads;
            USM.SceneManager.activeSceneChanged += DisinfectOtherAreas;
        }

        private bool DisinfectCrossroads(string boolName, bool value)
        {
            if (boolName == "crossroadsInfected")
            {
                value = value && !Equipped();
            }
            return value;
        }

        private static readonly HashSet<string> DisinfectedScenes = new() {
            "Abyss_17",
            "Abyss_19",
            "Abyss_20",
            "Crossroads_21",
            "Crossroads_22",
            "Waterways_03",
            "Fungus3_39",
            "Room_Final_Boss_Atrium",
            "Room_Final_Boss_Core"
        };

        private static readonly List<string> DisinfectedPrefixes = new() {
            "infected_vine",
            "infected_large_blob",
            "infected_orange_drip",
            "infected_floor_",
            "infected_crossroads_particles",
            "infected_dark_blob",
            "Infected Flag",
            "Audio Orange Pulse",
            "Pulse Audio",
            "Parasite Balloon",
            "Lesser Mawlek",
            "Mawlek Turret",
            "Scuttler Spawn",
            "Scuttler Group",
            "Battle Gate Deepnest",
            "wispy smoke BG",
        };

        private Texture OriginalMossProphetTexture;

        private void DisinfectOtherAreas(USM.Scene from, USM.Scene to)
        {
            if (Equipped() && DisinfectedScenes.Contains(to.name))
            {
                foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    foreach (var p in DisinfectedPrefixes)
                    {
                        if (obj.name.StartsWith(p))
                        {
                            UnityEngine.Object.Destroy(obj);
                        }
                    }
                }
                if (to.name == "Crossroads_22")
                {
                    var aspidMother = GameObject.Find(AspidMotherName);
                    // Use the string form of GetComponent to avoid taking an otherwise-unnecessary
                    // dependency on UnityEngine.AnimationModule.
                    var aspidAnimator = aspidMother?.GetComponent("Animator");
                    if (aspidAnimator == null)
                    {
                        Transcendence.Instance.LogWarn("Aspid Mother Animator not found");
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(aspidAnimator);
                        var newAnim = aspidMother.AddComponent<DummyMonoBehaviour>();
                        var sr = aspidMother.GetComponent<SpriteRenderer>();

                        IEnumerator Animate()
                        {
                            var steps = new List<(string, float)>()
                            {
                                ("AspidMother0.png", 0.8f),
                                ("AspidMother1.png", 0.1f),
                                ("AspidMother2.png", 0.1f),
                                ("AspidMother3.png", 0.1f)
                            };
                            while (true)
                            {
                                foreach (var (sprite, duration) in steps)
                                {
                                    sr.sprite = EmbeddedSprites.Get(sprite, 64);
                                    yield return new WaitForSeconds(duration);
                                }
                            }
                        }

                        newAnim.StartCoroutine(Animate());
                    }
                }
            }

            // We have to manually restore the original Moss Prophet sprites when this charm is
            // not in use, as changes to the material's main texture persist through room loads.
            if (to.name == "Fungus3_39")
            {
                var prophetSprite = GameObject.Find("Moss Cultist")?.GetComponent<tk2dSprite>();
                if (prophetSprite == null)
                {
                    Transcendence.Instance.LogWarn("Moss Cultist tk2dSprite not found, cannot reskin");
                }
                else if (Equipped())
                {
                    if (OriginalMossProphetTexture == null)
                    {
                        OriginalMossProphetTexture = prophetSprite.GetCurrentSpriteDef().material.mainTexture;
                    }
                    prophetSprite.GetCurrentSpriteDef().material.mainTexture = EmbeddedSprites.Get("MossProphet.png").texture;
                }
                else if (OriginalMossProphetTexture != null)
                {
                    prophetSprite.GetCurrentSpriteDef().material.mainTexture = OriginalMossProphetTexture;
                }
                else
                {
                    Transcendence.Instance.LogWarn("Moss Cultist original sprite not available");
                }
            }
        }

        private const string AspidMotherName = "giant_hatcher_corpse0003";
    }
}