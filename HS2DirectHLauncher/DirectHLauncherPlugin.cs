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
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HS2DirectHLauncher
{
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency("com.hs2.orbitandexciter", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class DirectHLauncherPlugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _alwaysEnabled = null!;
        private ConfigEntry<int> _mapId = null!;
        private ConfigEntry<float> _pollInterval = null!;
        private ConfigEntry<string> _femaleDirectory = null!;
        private ConfigEntry<string> _maleDirectory = null!;
        private ConfigEntry<bool> _recursive = null!;
        private readonly CardPicker _cardPicker = new CardPicker();
        private readonly Dictionary<string, ChaFileControl> _validatedFemaleFiles =
            new Dictionary<string, ChaFileControl>(StringComparer.OrdinalIgnoreCase);
        private string _runMarkerPath = string.Empty;
        private bool _armed;
        private bool _requested;
        private bool _titleRequested;
        private float _nextPoll;

        private void Awake()
        {
            _alwaysEnabled = Config.Bind("Launcher", "AlwaysEnabled", false,
                "Always jump directly to HScene. The one-click launcher does not require this setting.");
            _mapId = Config.Bind("Launcher", "MapId", 3,
                "Map loaded for the direct H scene. 3 is the standard room.");
            _pollInterval = Config.Bind("Launcher", "ReadyPollSeconds", 0.02f,
                new ConfigDescription("How often startup readiness is checked.", new AcceptableValueRange<float>(0.01f, 0.5f)));
            _femaleDirectory = Config.Bind("Cards", "FemaleDirectory", "UserData/chara/female",
                "Absolute path or path relative to the game root.");
            _maleDirectory = Config.Bind("Cards", "MaleDirectory", "UserData/chara/male",
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

            DisableLegacyDirectHDriver();
            Logger.LogInfo("Direct-H launcher armed; waiting only for required game singletons.");
        }

        private void DisableLegacyDirectHDriver()
        {
            try
            {
                Type? orbitType = Type.GetType(
                    "HS2OrbitAndExciter.HS2OrbitAndExciter, HS2OrbitAndExciter",
                    throwOnError: false);
                var field = orbitType?.GetField(
                    "EnableDirectHSmokeDriver",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);
                if (field?.GetValue(null) is ConfigEntry<bool> legacyDriver && legacyDriver.Value)
                {
                    legacyDriver.Value = false;
                    Logger.LogInfo("Disabled legacy Orbit Direct-H smoke driver for this launch.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not disable legacy Orbit Direct-H smoke driver: " + ex.Message);
            }
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
            if (!RequiredSystemsReady())
                return;

            try
            {
                if (!AdvanceToCompatibleLaunchPoint())
                    return;
                EnterHScene();
                _requested = true;
                ConsumeRunMarker();
            }
            catch (Exception ex)
            {
                _nextPoll = Time.unscaledTime + 1f;
                Logger.LogError("Direct-H request failed; will retry: " + ex);
            }
        }

        private static bool RequiredSystemsReady()
        {
            return Singleton<Game>.IsInstance() &&
                   Singleton<HSceneManager>.IsInstance() &&
                   Singleton<Character>.IsInstance() &&
                   !Scene.IsFadeNow;
        }

        private bool AdvanceToCompatibleLaunchPoint()
        {
            string activeScene = UnitySceneManager.GetActiveScene().name;
            if (string.Equals(activeScene, "Title", StringComparison.Ordinal))
                return true;
            if (!string.Equals(activeScene, "Logo", StringComparison.Ordinal))
                return false;
            if (_titleRequested)
                return false;

            var logo = FindObjectOfType<LogoScene>();
            var saveData = Singleton<Game>.Instance.saveData;
            if (logo == null || saveData == null)
                return false;

            // Keep the three save-data guards from LogoScene.Start, skip its brand
            // call and two-second wait, but still emit Title once for old plugins
            // that initialize their H-scene UI from the Title lifecycle.
            saveData.RoomListCharaExists();
            saveData.PlayerCoordinateExists();
            saveData.PlayerExists();
            logo.StopAllCoroutines();
            logo.enabled = false;
            _titleRequested = true;
            Logger.LogInfo("Logo bootstrap complete; loading compatibility Title without brand call or fade.");
            Scene.LoadReserve(new Scene.Data
            {
                levelName = "Title",
                fadeType = FadeCanvas.Fade.None
            }, isLoadingImageDraw: false);
            return false;
        }

        private void EnterHScene()
        {
            var game = Singleton<Game>.Instance;
            var manager = Singleton<HSceneManager>.Instance;
            string femaleDir = ResolveDirectory(_femaleDirectory.Value);
            string maleDir = ResolveDirectory(_maleDirectory.Value);

            string[] females = _cardPicker.PickTwo(
                femaleDir,
                _recursive.Value,
                path => Path.GetFileNameWithoutExtension(path).IndexOf("ChaF", StringComparison.OrdinalIgnoreCase) >= 0,
                path => ValidateCardSex(path, 1));
            string[] males = _cardPicker.PickTwo(
                maleDir,
                _recursive.Value,
                path => Path.GetFileNameWithoutExtension(path).IndexOf("ChaM", StringComparison.OrdinalIgnoreCase) >= 0,
                path => ValidateCardSex(path, 0),
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

        private bool ValidateCardSex(string path, byte expectedSex)
        {
            try
            {
                var file = new ChaFileControl();
                if (!file.LoadCharaFile(path, expectedSex, noLoadPng: true) || file.parameter.sex != expectedSex)
                    return false;
                if (expectedSex == 1)
                    _validatedFemaleFiles[path] = file;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private List<Heroine> BuildHeroineList(IEnumerable<string> paths)
        {
            var result = new List<Heroine>();
            foreach (string path in paths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                try
                {
                    if (!_validatedFemaleFiles.TryGetValue(path, out ChaFileControl file))
                    {
                        file = new ChaFileControl();
                        if (!file.LoadCharaFile(path, 1, noLoadPng: true) || file.parameter.sex != 1)
                            continue;
                    }
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
            configured = NormalizeConfiguredPath(configured);
            if (Path.IsPathRooted(configured))
                return configured;
            return Path.GetFullPath(Path.Combine(Paths.GameRootPath, configured));
        }

        private static string NormalizeConfiguredPath(string configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
                return string.Empty;

            // BepInEx 5 can deserialize backslash-prefixed folder names such as
            // "\\female" as a form-feed escape. Forward slashes are accepted by Windows.
            return configured
                .Replace("\b", "/b")
                .Replace("\t", "/t")
                .Replace("\n", "/n")
                .Replace("\f", "/f")
                .Replace("\r", "/r");
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
