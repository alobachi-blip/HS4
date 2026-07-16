using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Actor;
using AIChara;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Illusion;
using Illusion.Game;
using Manager;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace HS2DirectHLauncher
{
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency("com.hs2.orbitandexciter", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.joan6694.illusionplugins.moreaccessories", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class DirectHLauncherPlugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> _alwaysEnabled = null!;
        private ConfigEntry<int> _mapId = null!;
        private ConfigEntry<bool> _randomizeMap = null!;
        private ConfigEntry<bool> _randomizeInitialClothes = null!;
        private ConfigEntry<float> _pollInterval = null!;
        private ConfigEntry<string> _femaleDirectory = null!;
        private ConfigEntry<string> _maleDirectory = null!;
        private ConfigEntry<bool> _recursive = null!;
        private ConfigEntry<bool> _enableOrbitAssist = null!;
        private readonly CardPicker _cardPicker = new CardPicker();
        private readonly System.Random _random =
            new System.Random(unchecked(Environment.TickCount * 397 ^ Guid.NewGuid().GetHashCode()));
        private readonly Dictionary<string, ChaFileControl> _validatedFemaleFiles =
            new Dictionary<string, ChaFileControl>(StringComparer.OrdinalIgnoreCase);
        private string _runMarkerPath = string.Empty;
        private bool _armed;
        private bool _requested;
        private bool _bootstrapTakenOver;
        private bool _orbitAssistEnabled;
        private bool _initialClothesRandomized;
        private float _nextPoll;
        private float _nextOrbitAssistPoll;

        private void Awake()
        {
            _alwaysEnabled = Config.Bind("Launcher", "AlwaysEnabled", false,
                "Always jump directly to HScene. The one-click launcher does not require this setting.");
            _mapId = Config.Bind("Launcher", "MapId", 3,
                "Fallback map when random map selection is disabled or unavailable. 3 is the standard room.");
            _randomizeMap = Config.Bind("Launcher", "RandomizeMap", true,
                "Choose a random H-compatible map on every direct launch.");
            _randomizeInitialClothes = Config.Bind("Launcher", "RandomizeInitialClothes", true,
                "Randomize each loaded character's clothes state once the opening H animation is ready.");
            _pollInterval = Config.Bind("Launcher", "ReadyPollSeconds", 0.02f,
                new ConfigDescription("How often startup readiness is checked.", new AcceptableValueRange<float>(0.01f, 0.5f)));
            _femaleDirectory = Config.Bind("Cards", "FemaleDirectory", "UserData/chara/female",
                "Absolute path or path relative to the game root.");
            _maleDirectory = Config.Bind("Cards", "MaleDirectory", "UserData/chara/male",
                "Absolute path or path relative to the game root.");
            _recursive = Config.Bind("Cards", "SearchSubdirectories", true,
                "Include cards in subdirectories.");
            _enableOrbitAssist = Config.Bind("Integration", "EnableOrbitAssist", true,
                "Automatically enable HS2 Orbit and Exciter after the direct H scene is ready.");

            _runMarkerPath = Path.Combine(Paths.ConfigPath, PluginInfo.RunMarkerFileName);
            _armed = _alwaysEnabled.Value || File.Exists(_runMarkerPath);
            if (!_armed)
            {
                enabled = false;
                return;
            }

            DisableLegacyDirectHDriver();
            PatchMoreAccessoriesStartupGuard();
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

        private void PatchMoreAccessoriesStartupGuard()
        {
            try
            {
                Type? type = Type.GetType("MoreAccessoriesAI.MoreAccessories, MoreAccessories", throwOnError: false);
                var method = type?.GetMethod(
                    "UpdateHUI",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (method == null)
                    return;

                var harmony = new Harmony(PluginInfo.Guid + ".compat");
                harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(typeof(DirectHLauncherPlugin), nameof(MoreAccessoriesUpdateHuiPrefix)));
                Logger.LogInfo("Installed MoreAccessories H-UI startup guard.");
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not install MoreAccessories startup guard: " + ex.Message);
            }
        }

        private static bool MoreAccessoriesUpdateHuiPrefix()
        {
            try
            {
                if (!Singleton<HSceneManager>.IsInstance() || !Singleton<HSceneSprite>.IsInstance())
                    return false;

                var manager = Singleton<HSceneManager>.Instance;
                var hScene = manager.Hscene;
                if (hScene == null)
                    return false;

                int selected = manager.numFemaleClothCustom;
                ChaControl[] characters = selected < 2 ? hScene.GetFemales() : hScene.GetMales();
                int index = selected < 2 ? selected : selected - 2;
                return characters != null && index >= 0 && index < characters.Length && characters[index] != null;
            }
            catch
            {
                return false;
            }
        }

        private void Update()
        {
            if (!_armed)
                return;
            if (_requested)
            {
                if (TryRandomizeInitialClothes())
                    TryEnableOrbitAssist();
                return;
            }
            if (Time.unscaledTime < _nextPoll)
                return;
            if (HSceneManager.isHScene)
            {
                _requested = true;
                ConsumeRunMarker();
                if (TryRandomizeInitialClothes())
                    TryEnableOrbitAssist();
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

        private bool TryRandomizeInitialClothes()
        {
            if (_initialClothesRandomized)
                return true;
            if (!_randomizeInitialClothes.Value)
            {
                _initialClothesRandomized = true;
                return true;
            }
            if (!HSceneManager.isHScene || !Singleton<HSceneManager>.IsInstance())
                return false;

            var hScene = Singleton<HSceneManager>.Instance.Hscene;
            var info = hScene?.ctrlFlag?.nowAnimationInfo;
            if (hScene == null || info == null || info.id < 0)
                return false;

            ChaControl[] females = hScene.GetFemales();
            ChaControl[] males = hScene.GetMales();
            if (!PrimaryCharacterReady(females) || !PrimaryCharacterReady(males))
                return false;

            int changedSlots = RandomizeClothes(females);

            // HScene.LateUpdate owns male wear state, so randomize its source
            // settings as well as the currently loaded models.
            var hData = Manager.Config.HData;
            hData.Cloth = NextBool();
            hData.Accessory = NextBool();
            hData.Shoes = NextBool();
            hData.SecondCloth = NextBool();
            hData.SecondAccessory = NextBool();
            hData.SecondShoes = NextBool();
            changedSlots += RandomizeClothes(males);

            var clothUi = FindObjectOfType<HSceneSpriteClothCondition>();
            clothUi?.SetClothCharacter(init: true);
            _initialClothesRandomized = true;
            Logger.LogInfo($"Randomized opening clothes: slots={changedSlots}, male1Cloth={hData.Cloth}, male2Cloth={hData.SecondCloth}");
            return true;
        }

        private static bool PrimaryCharacterReady(ChaControl[] characters)
        {
            return characters != null && characters.Length > 0 &&
                   characters[0] != null && characters[0].loadEnd && characters[0].objBodyBone != null;
        }

        private int RandomizeClothes(IEnumerable<ChaControl> characters)
        {
            int changed = 0;
            foreach (ChaControl cha in characters)
            {
                if (cha == null || !cha.loadEnd || cha.objBodyBone == null)
                    continue;

                var slots = Enumerable.Range(0, 8)
                    .Where(cha.IsClothesStateKind)
                    .OrderBy(_ => _random.Next())
                    .ToArray();
                foreach (int slot in slots)
                {
                    cha.SetClothesState(slot, (byte)_random.Next(3));
                    changed++;
                }
            }
            return changed;
        }

        private bool NextBool()
        {
            return _random.Next(2) == 0;
        }

        private void TryEnableOrbitAssist()
        {
            if (_orbitAssistEnabled || !_enableOrbitAssist.Value || Time.unscaledTime < _nextOrbitAssistPoll)
                return;

            _nextOrbitAssistPoll = Time.unscaledTime + 0.5f;
            if (!HSceneManager.isHScene || !Singleton<HSceneManager>.IsInstance())
                return;

            var hScene = Singleton<HSceneManager>.Instance.Hscene;
            var info = hScene?.ctrlFlag?.nowAnimationInfo;
            if (hScene?.ctrlFlag?.cameraCtrl == null || info == null || info.id < 0)
                return;

            try
            {
                Type? orbitType = Type.GetType(
                    "HS2OrbitAndExciter.OrbitController, HS2OrbitAndExciter",
                    throwOnError: false);
                if (orbitType == null)
                    return;

                const System.Reflection.BindingFlags flags =
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public;
                var isActive = orbitType.GetMethod("IsOrbitActive", flags);
                if (isActive?.Invoke(null, null) is bool alreadyActive && alreadyActive)
                {
                    _orbitAssistEnabled = true;
                    Logger.LogInfo("Orbit assist was already active in the direct H scene.");
                    return;
                }

                var enable = orbitType.GetMethod("SetOrbitAssistActive", flags);
                if (enable?.Invoke(null, new object[] { true, "direct_h_launcher" }) is bool ok && ok)
                {
                    _orbitAssistEnabled = true;
                    Logger.LogInfo("Enabled Orbit assist for the direct H scene.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Could not enable Orbit assist yet; will retry: " + ex.Message);
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
            if (_bootstrapTakenOver)
                return true;

            var logo = FindObjectOfType<LogoScene>();
            var saveData = Singleton<Game>.Instance.saveData;
            if (logo == null || saveData == null)
                return false;

            // Keep LogoScene's required save-data guards, then skip its brand call,
            // fixed wait, and the compatibility Title scene. Known H-UI consumers
            // are guarded separately and HScene initializes its own resources.
            saveData.RoomListCharaExists();
            saveData.PlayerCoordinateExists();
            saveData.PlayerExists();
            logo.StopAllCoroutines();
            logo.enabled = false;
            _bootstrapTakenOver = true;
            Logger.LogInfo("Logo bootstrap complete; bypassing brand wait and compatibility Title.");
            return true;
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

            int mapId = PickOpeningMap();
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

        private int PickOpeningMap()
        {
            int fallback = _mapId.Value;
            if (!_randomizeMap.Value || BaseMap.infoTable == null)
                return fallback;

            int[] candidates = BaseMap.infoTable.Values
                .Where(map => map != null && map.No >= 0 && (map.Draw == 0 || map.Draw == 2))
                .Select(map => map.No)
                .Distinct()
                .ToArray();
            if (candidates.Length == 0)
            {
                Logger.LogWarning($"No H-compatible maps found; using fallback map {fallback}.");
                return fallback;
            }

            return candidates[_random.Next(candidates.Length)];
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
