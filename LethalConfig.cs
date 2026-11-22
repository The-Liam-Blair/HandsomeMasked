using LethalConfig;
using LethalConfig.ConfigItems;
using LethalConfig.ConfigItems.Options;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using HandsomeMasked;

namespace HandsomeMasked
{
    internal class LethalConfig
    {
        public static bool enabled
        {
            get => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("ainavt.lc.lethalconfig");
        }

        public static void SetupLethalConfigOptions()
        {
            LethalConfigManager.SetModIcon(HandsomeMasked.MainAssets.LoadAsset<Sprite>("logo.png"));
            LethalConfigManager.SetModDescription("Turns the Masked (entities and scrap) into Handsome Jack!");


            LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(HandsomeMasked.randomiseAllVoicelines, new BoolCheckBoxOptions
            {
                Section = "Audio Toggles",
                Name = "Randomise Voice Lines",
                RequiresRestart = false
            }));

            LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(HandsomeMasked.enableMaskedEntities, new BoolCheckBoxOptions
            {
                Section = "Visual Toggles",
                Name = "Mask Swap for Masked Entities",
                RequiresRestart = true
            }));

            LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(HandsomeMasked.enableMaskScrap, new BoolCheckBoxOptions
            {
                Section = "Visual Toggles",
                Name = "Mask Swap for Mask Scrap",
                RequiresRestart = true
            }));

            // todo: if/once vanilla masked audio is saved and swapped for changing configs w/o a restart, remember to
            //       set RequireRestart to false for the below settings.

            LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(HandsomeMasked.enableMaskedEntityAudio, new BoolCheckBoxOptions
            {
                Section = "Audio Toggles",
                Name = "Custom Audio for Masked Entities",
                RequiresRestart = true
            }));

            LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(HandsomeMasked.enableMaskScrapAudio, new BoolCheckBoxOptions
            {
                Section = "Audio Toggles",
                Name = "Custom Audio for Mask Scrap",
                RequiresRestart = true
            }));

            LethalConfigManager.AddConfigItem(new BoolCheckBoxConfigItem(HandsomeMasked.enableMaskScrapAudioInShip, new BoolCheckBoxOptions
            {
                Section = "Audio Toggles",
                Name = "Mask Scrap Audio While in Ship",
                RequiresRestart = true
            }));
        }
    }
}
