using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LethalConfig;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HandsomeMasked
{
    // todo: look into saving a snapshot of all all the audio lists for masked entities and scrap, so that
    //       swapping between custom and vanilla audio can be done at runtime (With LethalConfig
    //       configEntry.SettingChanged hooks for the audio config files) without needing to restart the game.

    [BepInPlugin(P_GUID, P_Name, P_Version)]
    [BepInDependency("ainavt.lc.lethalconfig", BepInDependency.DependencyFlags.SoftDependency)]
    public class HandsomeMasked : BaseUnityPlugin
    {
        public const string P_GUID = $"{P_Author}.{P_Name}";
        public const string P_Author = "RigsInRags";
        public const string P_Name = "HandsomeMasked";
        public const string P_Version = "1.0.0";

        public static AssetBundle MainAssets;

        private static List<AudioClip> roamVoicelines = new List<AudioClip>();
        private static List<AudioClip> followVoicelines = new List<AudioClip>();
        private static List<AudioClip> chaseVoicelines = new List<AudioClip>();
        private static List<AudioClip> executeVoicelines = new List<AudioClip>();


        // Instead of setting an audio clip to null for times when nothing should play, as Unity will throw a 
        // warning when PlayOneShot encounters a null AudioClip but not for an empty AudioClip, so helps keep
        // the log less congested.
        private static AudioClip silence;

        private static FieldInfo _isRunning;
        private static FieldInfo _isInKillAnimation;

        public static ConfigEntry<bool> randomiseAllVoicelines { get; set; }

        public static ConfigEntry<bool> enableMaskScrapAudio { get; set; }
        public static ConfigEntry<bool> enableMaskedEntityAudio { get; set; }

        public static ConfigEntry<bool> enableMaskScrapAudioInShip { get; set; }

        public static ConfigEntry<bool> enableMaskedEntities { get; set; }
        public static ConfigEntry<bool> enableMaskScrap { get; set; }



        public void Awake()
        {
            DebugLog.Init(Logger);

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("HandsomeMasked.HandsomeAssets"))
                MainAssets = AssetBundle.LoadFromStream(stream);

            randomiseAllVoicelines = Config.Bind("Handsome Masked Config", "randomiseAllVoicelines", false, "Set to false to make voicelines play depending on the Masked entity's current AI state. Set to true to allow any voiceline to play at any time, for more randomised but less contextualised voicelines.");
            enableMaskedEntities = Config.Bind("Handsome Masked Config", "enableMaskedEntities", true, "Set to false to disable masked entities from being made Handsome.");
            enableMaskScrap = Config.Bind("Handsome Masked Config", "enableMaskScrap", true, "Set to false to disable mask scrap items from being made Handsome.");
            enableMaskedEntityAudio = Config.Bind("Handsome Masked Config", "enableMaskedEntityAudio", true, "Set to false to disable custom audio from masked entities.");
            enableMaskScrapAudio = Config.Bind("Handsome Masked Config", "enableMaskScrapAudio", true, "Set to false to disable custom audio from mask scrap.");
            enableMaskScrapAudioInShip = Config.Bind("Handsome Masked Config", "enableMaskScrapAudioInShip", true, "Set to false to disable mask scrap audio from playing while it is in the ship, if the sounds get too annoying.");

            if (LethalConfig.enabled)
                LethalConfig.SetupLethalConfigOptions();


            Harmony harmony = new Harmony(P_GUID);

            MethodInfo targetMethod = AccessTools.Method(typeof(StartOfRound), "Start");
            MethodInfo patch = typeof(HandsomeMasked).GetMethod("OnStartOfRoundStart", BindingFlags.Public | BindingFlags.Static);
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(patch));

            targetMethod = AccessTools.Method(typeof(RandomPeriodicAudioPlayer), "PlayAudio");
            patch = typeof(HandsomeMasked).GetMethod("OnAudioPlayerPlayAudio", BindingFlags.Public | BindingFlags.Static);
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(patch));

            targetMethod = AccessTools.Method(typeof(MaskedPlayerEnemy), "KillPlayerAnimationClientRpc");
            patch = typeof(HandsomeMasked).GetMethod("OnMaskedKillPlayerAnimationStart", BindingFlags.Public | BindingFlags.Static);
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(patch));

            targetMethod = AccessTools.Method(typeof(HauntedMaskItem), "AttachToPlayerOnLocalClient");
            patch = typeof(HandsomeMasked).GetMethod("OnMaskScrapKillPlayerAnimationStart", BindingFlags.Public | BindingFlags.Static);
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(patch));

            _isRunning = AccessTools.Field(typeof(MaskedPlayerEnemy), "running");
            _isInKillAnimation = AccessTools.Field(typeof(MaskedPlayerEnemy), "inKillAnimation");


            foreach (string audioClip in MainAssets.GetAllAssetNames().Where(asset => asset.EndsWith(".wav")))
            {
                switch(audioClip)
                {
                    case string clip when clip.Contains("standard"): // Random sounds and 1 word phrases
                        followVoicelines.Add(MainAssets.LoadAsset<AudioClip>(audioClip));
                        break;

                    case string clip when clip.Contains("laugh"): // Laughs
                        roamVoicelines.Add(MainAssets.LoadAsset<AudioClip>(audioClip));
                        break;

                    case string clip when clip.Contains("funny"): // Funny jack quotes
                        chaseVoicelines.Add(MainAssets.LoadAsset<AudioClip>(audioClip));
                        followVoicelines.Add(MainAssets.LoadAsset<AudioClip>(audioClip));
                        break;

                    case string clip when clip.Contains("insult"): // Funny jack quotes but it's insults to the vault hunter/employee
                        chaseVoicelines.Add(MainAssets.LoadAsset<AudioClip>(audioClip));
                        break;

                    case string clip when clip.Contains("executegloat"): // Funny jack quotes that also work as taunts when masking a player
                        executeVoicelines.Add(MainAssets.LoadAsset<AudioClip>(audioClip));
                        break;

                }
            }

            silence = AudioClip.Create("SilentJack", 1, 1, 10000, false);
            silence.SetData(new[] { 0f }, 0);
        }

        /// <summary>
        /// Jackify the mask scrap items
        /// </summary>
        public static void OnStartOfRoundStart(StartOfRound __instance)
        {
            Item[] maskItems = new Item[0];

            if (enableMaskScrap.Value)
                maskItems = __instance.allItemsList.itemsList.Where(i => i.itemName == "Comedy" || i.itemName == "Tragedy").ToArray();
            
            foreach (Item mask in maskItems)
            {
                // High fidelity mesh
                Transform lodZero = mask.spawnPrefab.transform.GetChild(1);
                lodZero.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackMesh.fbx");
                lodZero.GetComponent<MeshRenderer>().sharedMaterial = MainAssets.LoadAsset<Material>("JackyMat.mat");


                // Low fidelity mesh
                Transform lodOne = lodZero.GetChild(0);
                lodOne.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackMesh.fbx");
                lodOne.GetComponent<MeshRenderer>().sharedMaterial = MainAssets.LoadAsset<Material>("JackyMat.mat");


                // Glowing eyes
                Transform glowEyes = lodZero.GetChild(1);
                glowEyes.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackEyes.fbx");


                // Another mask prefab that spawns when the scrap haunts the player, and their 2 LODs and glowing eyes
                Transform spawnedMask = mask.spawnPrefab.GetComponent<HauntedMaskItem>().headMaskPrefab.transform;

                lodZero = spawnedMask.transform.GetChild(2);
                lodZero.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackMesh.fbx");
                lodZero.GetComponent<MeshRenderer>().sharedMaterial = MainAssets.LoadAsset<Material>("JackyMat.mat");


                lodOne = spawnedMask.transform.GetChild(0);
                lodOne.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackMesh.fbx");
                lodOne.GetComponent<MeshRenderer>().sharedMaterial = MainAssets.LoadAsset<Material>("JackyMat.mat");


                glowEyes = lodZero.GetChild(0);
                glowEyes.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackEyes.fbx");
                glowEyes.GetComponent<MeshRenderer>().materials[0].SetInt("_CullMode", 0);



                // Mask name
                mask.itemName = "Handsome Jack's Mask";
                mask.spawnPrefab.transform.GetChild(0).GetComponent<ScanNodeProperties>().headerText = "Handsome Jack's Mask";
            }

            if (enableMaskScrap.Value)
                DebugLog.Log($"The masks have been Handsome Jacked. See you soon, kiddo!");


            if (!enableMaskedEntities.Value)
                return;

            // Search for the next masked entity reference in the levels list, then apply the same mesh and
            // material changes like seen above to the masks of the masked entities.
            foreach (var level in __instance.levels)
            {
                if (level is null) continue;

                foreach (SpawnableEnemyWithRarity enemy in level.Enemies)
                {
                    if (enemy is null) continue;

                    if (enemy.enemyType.enemyName == "Masked")
                    {
                        foreach (GameObject mask in enemy.enemyType.enemyPrefab.GetComponent<MaskedPlayerEnemy>().maskTypes)
                        {
                            Transform lodZero = mask.transform.GetChild(2);
                            lodZero.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackMesh.fbx");
                            lodZero.GetComponent<MeshRenderer>().sharedMaterial = MainAssets.LoadAsset<Material>("JackyMat.mat");

                            Transform lodOne = mask.transform.GetChild(0);
                            lodOne.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackMesh.fbx");
                            lodOne.GetComponent<MeshRenderer>().sharedMaterial = MainAssets.LoadAsset<Material>("JackyMat.mat");

                            Transform glowEyes = lodZero.GetChild(0);
                            glowEyes.GetComponent<MeshFilter>().sharedMesh = MainAssets.LoadAsset<Mesh>("JackEyes.fbx");
                        }

                        // No need to rename masked entities as Masked can't get scanned and don't have a bestiary entry

                        DebugLog.Log("The masked have been made Handsome!");

                        return;
                    }
                }
            }
        }


        /// <summary>
        /// Replace audio at runtime: Allows for going over the 4 audio clips limit by replacing the about-to-be-played
        /// audio clip at runtime.
        /// 
        /// Because only the mask scraps and masked use this script (to my knowledge...), no type checking is required.
        /// </summary>
        /// <param name="clipIndex">Index of the clip about to be played</param>
        public static void OnAudioPlayerPlayAudio(RandomPeriodicAudioPlayer __instance, int clipIndex)
        {
            // Masked entity
            if (__instance.GetComponentInParent<MaskedPlayerEnemy>() is MaskedPlayerEnemy masked)
                PlayEntityAudio(__instance, clipIndex, masked);

            // Mask scrap
            else if (__instance.GetComponent<HauntedMaskItem>() is HauntedMaskItem item)
                PlayScrapAudio(__instance, clipIndex, item);
        }

        private static void PlayEntityAudio(RandomPeriodicAudioPlayer __instance, int clipIndex, MaskedPlayerEnemy masked)
        {
            if (!enableMaskedEntityAudio.Value)
                return;

            // Masked is dead or RandomiseAllVoicelines is enabled - Play all (non execute) audio
            if (masked.isEnemyDead || randomiseAllVoicelines.Value)
                __instance.randomClips[clipIndex] = PickRandomAudioClip(roamVoicelines, chaseVoicelines, followVoicelines);

            else
            {
                switch (masked.currentBehaviourStateIndex)
                {
                    // Roaming
                    case 0:
                        __instance.randomClips[clipIndex] = roamVoicelines[UnityEngine.Random.Range(0, roamVoicelines.Count)];
                        break;

                    case 1:
                        // Chasing
                        if ((bool)_isRunning.GetValue(masked))
                            __instance.randomClips[clipIndex] = chaseVoicelines[UnityEngine.Random.Range(0, chaseVoicelines.Count)];

                        // Following
                        else
                            __instance.randomClips[clipIndex] = followVoicelines[UnityEngine.Random.Range(0, followVoicelines.Count)];

                        break;
                }
            }
        }

        private static void PlayScrapAudio(RandomPeriodicAudioPlayer __instance, int clipIndex, HauntedMaskItem item)
        {
            // Stop audio in ship room if the config has disabled it
            if (!enableMaskScrapAudioInShip.Value && item.isInShipRoom)
            {
                __instance.randomClips[clipIndex] = silence;
                return;
            }

            else if (enableMaskScrapAudio.Value)
                __instance.randomClips[clipIndex] = PickRandomAudioClip(roamVoicelines, chaseVoicelines, followVoicelines);
        }

        public static void OnMaskScrapKillPlayerAnimationStart(HauntedMaskItem __instance)
        {
            if (!enableMaskScrapAudio.Value) return;

            __instance.maskAttachAudio = executeVoicelines[UnityEngine.Random.Range(0, executeVoicelines.Count)];
            __instance.maskAttachAudioLocal = executeVoicelines[UnityEngine.Random.Range(0, executeVoicelines.Count)];

        }

        public static void OnMaskedKillPlayerAnimationStart(MaskedPlayerEnemy __instance, int playerObjectId)
        {
            if (!enableMaskedEntityAudio.Value) return;

            __instance.enemyType.audioClips[0] = executeVoicelines[UnityEngine.Random.Range(0, executeVoicelines.Count)];
        }


        private static AudioClip PickRandomAudioClip(List<AudioClip> first, List<AudioClip> second, List<AudioClip> third)
        {
            int index = UnityEngine.Random.Range(0, first.Count + second.Count + third.Count);

            if (index < first.Count) return first[index];

            index -= first.Count;
            if (index < second.Count) return second[index];

            index -= second.Count;
            return third[index];
        }
    }
}