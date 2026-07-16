using System;
using System.Collections.Generic;
using System.IO;
using Actor;
using AIChara;
using BepInEx;
using BepInEx.Configuration;
using Illusion;
using Illusion.Game;
using Manager;
using UnityEngine;

namespace HS2DirectHLauncher
{
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class DirectHLauncherPlugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _alwaysEnabled = null!;
        private ConfigEntry<int> _mapId = null!;
        private ConfigEntry<float> _pollInterval = null!;
        private ConfigEntry<string> _femaleDirectory = null!;
        private ConfigEntry<string> _maleDirectory = null!;
        private ConfigEntry<bool> _recursive = null!;
        private readonly CardPicker _cardPicker = new CardPicker();
        private string _runMarkerPath = string.Empty;
        private bool _armed;
        private bool _requested;
        private float _nextPoll;

        private void Awake()
        {
            _alwaysEnabled = Config.Bind("Launcher", "AlwaysEnabled", false,
                "Always jump directly to HScene. The one-click launcher does not require this setting.");
            _mapId = Config.Bind("Launcher", "MapId", 3,
                "Map loaded for the direct H scene. 3 is the standard room.");
            _pollInterval = Config.Bind("Launcher", "ReadyPollSeconds", 0.02f,
                new ConfigDescription("How often startup readiness is checked.", new AcceptableValueRange<float>(0.01f, 0.5f)));
            _femaleDirectory = Config.Bind("Cards", "FemaleDirectory", "UserData\\chara\\female",
                "Absolute path or path relative to the game root.");
            _maleDirectory = Config.Bind("Cards", "MaleDirectory", "UserData\\chara\\male",
                "Absolute path or path relative to the game root.");
            _recursive = Config.Bind("Cards", "SearchSubdirectories", true,
                "Include cards in subdirectories.");

            _runMarkerPath = Path.Combine(Paths.ConfigPath, PluginInfo.RunMarkerFileName);
            _armed = _alwaysEnabled.Value || File.Exists(_runMarkerPath);
            if (!_armed)
            {
                enabled = false;
                return;
            }

            Logger.LogInfo("Direct-H launcher armed; waiting only for required game singletons.");
        }

        private void Update()
        {
            if (!_armed || _requested || Time.unscaledTime < _nextPoll)
                return;
            if (HSceneManager.isHScene)
            {
                _requested = true;
                ConsumeRunMarker();
                return;
            }

            _nextPoll = Time.unscaledTime + _pollInterval.Value;
            if (!CanEnterHScene())
                return;

            try
            {
                EnterHScene();
                _requested = true;
                ConsumeRunMarker();
            }
            catch (Exception ex)
            {
                Logger.LogError("Direct-H request failed; will retry: " + ex);
            }
        }

        private static bool CanEnterHScene()
        {
            return Singleton<Game>.IsInstance() &&
                   Singleton<HSceneManager>.IsInstance() &&
                   Singleton<Character>.IsInstance() &&
                   !Scene.IsFadeNow;
        }

        private void EnterHScene()
        {
            var game = Singleton<Game>.Instance;
            var manager = Singleton<HSceneManager>.Instance;
            string femaleDir = ResolveDirectory(_femaleDirectory.Value);
            string maleDir = ResolveDirectory(_maleDirectory.Value);

            string[] females = _cardPicker.PickTwo(femaleDir, _recursive.Value);
            string[] males = _cardPicker.PickTwo(
                maleDir,
                _recursive.Value,
                game.saveData?.playerChara?.FileName ?? string.Empty,
                game.saveData?.secondPlayerChara?.FileName ?? string.Empty);

            if (string.IsNullOrEmpty(females[0]))
                Logger.LogWarning("No female cards found; HS2 will create its default first female and multi-female poses stay unavailable.");
            else if (string.Equals(females[0], females[1], StringComparison.OrdinalIgnoreCase))
                Logger.LogWarning("Only one female card found; loading it into both female slots.");
            if (string.IsNullOrEmpty(males[0]))
                Logger.LogWarning("No male cards or saved male cards found; HS2 will create its default first male and multi-male poses stay unavailable.");
            else if (string.Equals(males[0], males[1], StringComparison.OrdinalIgnoreCase))
                Logger.LogWarning("Only one male card found; loading it into both male slots.");

            int mapId = _mapId.Value;
            game.eventNo = -1;
            game.peepKind = -1;
            game.isConciergeAngry = false;
            game.mapNo = mapId;
            game.heroineList = BuildHeroineList(females);
            if (game.saveData != null)
                game.saveData.BeforeFemaleName = string.Empty;

            manager.mapID = mapId;
            manager.player = null;
            manager.females[0] = null;
            manager.females[1] = null;
            manager.pngFemales[0] = females[0];
            manager.pngFemales[1] = females[1];
            manager.pngMale = males[0];
            manager.pngMaleSecond = males[1];
            manager.bFutanari = false;
            manager.bFutanariSecond = false;
            manager.SecondSitori = false;

            Logger.LogInfo($"Loading HScene immediately: map={mapId}, female1={Path.GetFileName(females[0])}, female2={Path.GetFileName(females[1])}, male1={Path.GetFileName(males[0])}, male2={Path.GetFileName(males[1])}");
            Scene.LoadReserve(new Scene.Data
            {
                levelName = "HScene",
                fadeType = FadeCanvas.Fade.None
            }, isLoadingImageDraw: false);
        }

        private static List<Heroine> BuildHeroineList(IEnumerable<string> paths)
        {
            var result = new List<Heroine>();
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                try
                {
                    var file = new ChaFileControl();
                    if (file.LoadCharaFile(path, 1, noLoadPng: true))
                        result.Add(new Heroine(file, isRandomize: false));
                }
                catch
                {
                    // HScene will retry from manager.pngFemales and safely fall back if a card is invalid.
                }
            }
            return result;
        }

        private static string ResolveDirectory(string configured)
        {
            if (Path.IsPathRooted(configured))
                return configured;
            return Path.GetFullPath(Path.Combine(Paths.GameRootPath, configured));
        }

        private void ConsumeRunMarker()
        {
            try
            {
                if (File.Exists(_runMarkerPath))
                    File.Delete(_runMarkerPath);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not consume one-shot run marker: " + ex.Message);
            }
        }
    }
}
