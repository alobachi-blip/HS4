using System.Collections;
using System.Collections.Generic;
using AIChara;
using HarmonyLib;
using IllusionUtility.GetUtility;
using Manager;
using Obi;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Female orgasm: reuse female urine / 潮吹 Obi emitters on left/right nipple bones.
    /// Default rhythm: ObiEmitterCtrl.Play(-1, height) (urine LoadFile splitInfos).
    /// Optional override: custom multi-burst (Bursts / Interval / Speed / Amount).
    /// Falls back to cloning female urine emitters, then HParticleCtrl urine ParticleSystems.
    /// </summary>
    internal static class OrbitOrgasmNippleSpray
    {
        private static readonly string[] NippleBoneCandidatesL =
        {
            "cf_J_Mune_Nip01_s_L",
            "cf_J_Mune_Nipacs01_L",
            "cf_J_Mune04_s_L",
            "cf_J_Mune01_L",
        };

        private static readonly string[] NippleBoneCandidatesR =
        {
            "cf_J_Mune_Nip01_s_R",
            "cf_J_Mune_Nipacs01_R",
            "cf_J_Mune04_s_R",
            "cf_J_Mune01_R",
        };

        /// <summary>HParticleCtrl urine pattern ids (game default).</summary>
        private static readonly int[] UrineParticleIds = { 2, 3, 4, 5 };

        private static readonly List<ObiEmitterCtrl> ObiEmitters = new List<ObiEmitterCtrl>(4);
        private static readonly List<ObiFluidCtrl> OwnedFluidCtrls = new List<ObiFluidCtrl>(4);
        private static readonly List<ParticleSystem> ParticleClones = new List<ParticleSystem>(4);
        private static readonly List<GameObject> OwnedObjects = new List<GameObject>(8);
        private static readonly List<float> ObiBaseSpeeds = new List<float>(4);
        private static readonly List<float> ParticleBaseSpeeds = new List<float>(4);

        private static int _boundFemaleId = -1;
        private static int _boundHSceneId = -1;
        private static string _lastStatus = "乳潮待命";
        private static Coroutine? _burstRoutine;
        private static int _burstGen;

        internal static bool Enabled => HS2OrbitAndExciter.OrgasmNippleSprayEnabled?.Value ?? true;

        /// <summary>True = Play(-1,height) urine splitInfos; false = custom Bursts/Speed/Amount.</summary>
        internal static bool UseNativeUrineRhythm =>
            HS2OrbitAndExciter.OrgasmNippleSprayUseNativeUrineRhythm?.Value ?? true;

        internal static string HudStatus => !Enabled ? "乳潮關" : _lastStatus;

        private static int BurstCount
        {
            get
            {
                int n = HS2OrbitAndExciter.OrgasmNippleSprayBursts?.Value ?? 5;
                return Mathf.Clamp(n, 2, 20);
            }
        }

        private static float BurstInterval =>
            Mathf.Clamp(HS2OrbitAndExciter.OrgasmNippleSprayBurstInterval?.Value ?? 0.35f, 0.1f, 1.5f);

        private static float SpeedStartMult =>
            Mathf.Clamp(HS2OrbitAndExciter.OrgasmNippleSpraySpeedStart?.Value ?? 1.8f, 0.5f, 4f);

        private static float SpeedEndMult =>
            Mathf.Clamp(HS2OrbitAndExciter.OrgasmNippleSpraySpeedEnd?.Value ?? 0.4f, 0.05f, 2f);

        private static float AmountOverall =>
            Mathf.Clamp(HS2OrbitAndExciter.OrgasmNippleSprayAmount?.Value ?? 1f, 0.2f, 8f);

        private static float AmountStartWeight =>
            Mathf.Clamp(HS2OrbitAndExciter.OrgasmNippleSprayAmountStart?.Value ?? 1.5f, 0.2f, 8f);

        private static float AmountEndWeight =>
            Mathf.Clamp(HS2OrbitAndExciter.OrgasmNippleSprayAmountEnd?.Value ?? 0.5f, 0.1f, 5f);

        internal static void Reset()
        {
            StopBurstRoutine();
            ReleaseOwned();
            _boundFemaleId = -1;
            _boundHSceneId = -1;
            _lastStatus = Enabled ? "乳潮待命" : "乳潮關";
        }

        /// <summary>Drop and rebuild nipple emitters (after Offset/Rot change in settings).</summary>
        internal static void ForceRebuild(HScene? hScene)
        {
            StopBurstRoutine();
            ReleaseOwned();
            _boundFemaleId = -1;
            _boundHSceneId = -1;
            if (!Enabled)
            {
                _lastStatus = "乳潮關";
                return;
            }

            hScene ??= OrbitController.TryGetHScene();
            var cha = hScene != null ? OrbitHelpers.GetChaFemales(hScene)?[0] : null;
            if (hScene == null || cha == null || cha.objBodyBone == null)
            {
                _lastStatus = "乳潮待命";
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 重建乳頭潮吹噴口需要在 H 場景且有女主");
                return;
            }

            if (EnsureEmitters(hScene, cha))
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭潮吹噴口已重建（{_lastStatus}）");
            else
            {
                _lastStatus = "乳潮失敗";
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 乳頭潮吹噴口重建失敗");
            }
        }

        internal static void OnOrgasm(HSceneFlagCtrl? ctrlFlag)
        {
            if (!Enabled || ctrlFlag == null)
                return;

            var hScene = OrbitController.TryGetHScene();
            if (hScene == null || !ReferenceEquals(hScene.ctrlFlag, ctrlFlag))
                return;

            var cha = OrbitHelpers.GetChaFemales(hScene)?[0];
            if (cha == null || cha.objBodyBone == null)
            {
                _lastStatus = "乳潮無女";
                return;
            }

            if (!EnsureEmitters(hScene, cha))
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 乳頭潮吹特效無法建立（無 urine emitter／粒子）");
                return;
            }

            StartSpray(hScene, cha);
        }

        private static bool EnsureEmitters(HScene hScene, ChaControl female)
        {
            int hId = hScene.GetInstanceID();
            int fId = female.GetInstanceID();
            if (hId == _boundHSceneId && fId == _boundFemaleId && (ObiEmitters.Count > 0 || ParticleClones.Count > 0))
                return true;

            ReleaseOwned();
            _boundHSceneId = hId;
            _boundFemaleId = fId;

            if (TryBuildObiFromFemaleUrine(hScene, female))
                return true;

            if (TryBuildObiByCloningFemaleUrine(hScene, female))
                return true;

            return TryBuildParticleClones(hScene, female);
        }

        private static bool TryGetFemaleUrineSiru(
            HScene hScene,
            out ObiCtrl? obiCtrl,
            out ObiCtrl.SiruObiInfo? chosen,
            out int urineSlotId)
        {
            obiCtrl = null;
            chosen = null;
            urineSlotId = -1;

            obiCtrl = Traverse.Create(hScene).Field("ctrlObi").GetValue() as ObiCtrl;
            if (obiCtrl == null)
                return false;

            var siruInfos = Traverse.Create(obiCtrl).Field("siruInfos").GetValue()
                as Dictionary<int, Dictionary<int, ObiCtrl.SiruObiInfo>>;
            // First female key = 1 (second female = 3)
            if (siruInfos == null || !siruInfos.TryGetValue(1, out var femaleSlots) || femaleSlots == null || femaleSlots.Count == 0)
                return false;

            var urineIds = hScene.ctrlFlag?.UrineIDs;
            if (urineIds != null)
            {
                for (int i = 0; i < urineIds.Count; i++)
                {
                    int id = urineIds[i];
                    if (femaleSlots.TryGetValue(id, out var info) && info != null)
                    {
                        chosen = info;
                        urineSlotId = id;
                        return true;
                    }
                }
            }

            // Fallback: common default urine slot 3
            if (femaleSlots.TryGetValue(3, out var fallback) && fallback != null)
            {
                chosen = fallback;
                urineSlotId = 3;
                return true;
            }

            foreach (var kv in femaleSlots)
            {
                chosen = kv.Value;
                urineSlotId = kv.Key;
                return chosen != null;
            }

            return false;
        }

        private static bool TryBuildObiFromFemaleUrine(HScene hScene, ChaControl female)
        {
            if (!Singleton<ObiFluidManager>.IsInstance())
                return false;

            if (!TryGetFemaleUrineSiru(hScene, out _, out var chosen, out _))
                return false;
            if (chosen == null)
                return false;

            int state = hScene.ctrlFlag != null && hScene.ctrlFlag.isFaintness ? 1 : 0;
            var param = chosen.Info;
            if (param == null || state >= param.Length || param[state] == null || param[state].SetupInfo == null)
                state = 0;
            if (param == null || state >= param.Length || param[state]?.SetupInfo == null)
                return false;

            var setup = param[state].SetupInfo;
            var ptn = param[state].EmitterPtn;
            var manager = Singleton<ObiFluidManager>.Instance;
            int made = 0;

            foreach (var parent in ResolveNippleParents(female))
            {
                var fluid = manager.Add(parent, LocalPos, LocalEuler, setup, 0);
                if (fluid?.ObiEmitterCtrls == null || fluid.ObiEmitterCtrls.Length == 0)
                    continue;

                var emitter = fluid.ObiEmitterCtrls[0];
                if (ptn.assetbundle != null && ptn.asset != null &&
                    !string.IsNullOrEmpty(ptn.assetbundle) && !string.IsNullOrEmpty(ptn.asset))
                {
                    emitter.LoadFile(ptn.assetbundle, ptn.asset, ptn.manifest ?? "");
                }

                OwnedFluidCtrls.Add(fluid);
                ObiEmitters.Add(emitter);
                ObiBaseSpeeds.Add(ReadObiSpeed(emitter, setup.speed > 0f ? setup.speed : 1f));
                made++;
            }

            if (made > 0)
            {
                _lastStatus = $"乳潮Obi×{made}";
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭潮吹 Obi×{made}（女 siruInfos[1] Urine）");
                return true;
            }

            return false;
        }

        private static bool TryBuildObiByCloningFemaleUrine(HScene hScene, ChaControl female)
        {
            if (!TryGetFemaleUrineSiru(hScene, out var obiCtrl, out var chosen, out int urineSlotId))
                return false;
            if (obiCtrl == null)
                return false;

            var femaleCtrls = Traverse.Create(obiCtrl).Field("obiFluidCtrlFemale").GetValue() as ObiFluidCtrl[];
            if (femaleCtrls == null || femaleCtrls.Length == 0 || femaleCtrls[0]?.ObiEmitterCtrls == null)
                return false;

            var emitters = femaleCtrls[0].ObiEmitterCtrls;
            ObiEmitterCtrl? source = null;
            if (urineSlotId >= 0 && urineSlotId < emitters.Length && emitters[urineSlotId] != null)
                source = emitters[urineSlotId];

            if (source == null)
            {
                var urineIds = hScene.ctrlFlag?.UrineIDs;
                if (urineIds != null)
                {
                    for (int i = 0; i < urineIds.Count; i++)
                    {
                        int id = urineIds[i];
                        if (id >= 0 && id < emitters.Length && emitters[id] != null)
                        {
                            source = emitters[id];
                            urineSlotId = id;
                            break;
                        }
                    }
                }
            }

            if (source == null)
                return false;

            // EmitterPtn for re-LoadFile after Instantiate (ensures splitInfos)
            string? ptnBundle = null, ptnAsset = null, ptnManifest = null;
            if (chosen?.Info != null)
            {
                int state = hScene.ctrlFlag != null && hScene.ctrlFlag.isFaintness ? 1 : 0;
                if (state >= chosen.Info.Length || chosen.Info[state] == null)
                    state = 0;
                if (state < chosen.Info.Length && chosen.Info[state] != null)
                {
                    var ptn = chosen.Info[state].EmitterPtn;
                    ptnBundle = ptn.assetbundle;
                    ptnAsset = ptn.asset;
                    ptnManifest = ptn.manifest ?? "";
                }
            }

            var solver = Traverse.Create(obiCtrl).Field("solver").GetValue() as Component;
            float srcSpeed = ReadObiSpeed(source, 1f);
            int made = 0;
            foreach (var parent in ResolveNippleParents(female))
            {
                var go = Object.Instantiate(source.gameObject, parent);
                go.name = "OrbitNippleUrine_" + parent.name;
                go.transform.localPosition = LocalPos;
                go.transform.localRotation = Quaternion.Euler(LocalEuler);
                go.transform.localScale = Vector3.one;
                go.SetActive(true);

                var clone = go.GetComponent<ObiEmitterCtrl>();
                if (clone == null)
                {
                    Object.Destroy(go);
                    continue;
                }

                if (!string.IsNullOrEmpty(ptnBundle) && !string.IsNullOrEmpty(ptnAsset))
                    clone.LoadFile(ptnBundle, ptnAsset, ptnManifest ?? "");

                TryRegisterParticleRenderer(source, clone);
                OwnedObjects.Add(go);
                ObiEmitters.Add(clone);
                ObiBaseSpeeds.Add(srcSpeed);
                made++;
            }

            if (solver != null && !solver.gameObject.activeSelf)
                solver.gameObject.SetActive(true);

            if (made > 0)
            {
                _lastStatus = $"乳潮Clone×{made}";
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭潮吹 clone Obi×{made}（female urine slot {urineSlotId}）");
                return true;
            }

            return false;
        }

        private static void TryRegisterParticleRenderer(ObiEmitterCtrl source, ObiEmitterCtrl clone)
        {
            try
            {
                if (!Singleton<ObiFluidManager>.IsInstance())
                    return;
                var manager = Singleton<ObiFluidManager>.Instance;
                var renderers = Traverse.Create(manager).Field("obiFluidRenderer").GetValue() as ObiFluidRenderer[];
                if (renderers == null || renderers.Length == 0 || renderers[0] == null)
                    return;

                var r = renderers[0];
                var list = new List<ObiParticleRenderer>(r.particleRenderers ?? new ObiParticleRenderer[0]);
                if (clone.ObiParticleRenderer != null && !list.Contains(clone.ObiParticleRenderer))
                {
                    list.Add(clone.ObiParticleRenderer);
                    r.particleRenderers = list.ToArray();
                }
            }
            catch (System.Exception ex)
            {
                HS2OrbitAndExciter.Log?.LogDebug($"Orbit: 乳頭潮吹 renderer 註冊略過: {ex.Message}");
            }
        }

        private static bool TryBuildParticleClones(HScene hScene, ChaControl female)
        {
            var particleCtrl = hScene.CtrlParticle;
            if (particleCtrl == null)
                return false;

            var lstParticle = Traverse.Create(particleCtrl).Field("lstParticle").GetValue()
                as System.Collections.IList;
            if (lstParticle == null || lstParticle.Count == 0)
                return false;

            GameObject? srcGo = null;
            ParticleSystem? srcPs = null;

            int preferId = TryGetFemaleUrinePlayParticleId(hScene);
            if (preferId >= 0 && preferId < lstParticle.Count)
                TryReadParticleEntry(lstParticle[preferId], out srcGo, out srcPs);

            if (srcPs == null)
            {
                foreach (int id in UrineParticleIds)
                {
                    if (id < lstParticle.Count && TryReadParticleEntry(lstParticle[id], out srcGo, out srcPs) && srcPs != null)
                        break;
                }
            }

            if (srcPs == null)
            {
                for (int i = 0; i < lstParticle.Count; i++)
                {
                    if (TryReadParticleEntry(lstParticle[i], out srcGo, out srcPs) && srcPs != null)
                        break;
                }
            }

            if (srcGo == null || srcPs == null)
                return false;

            int made = 0;
            foreach (var parent in ResolveNippleParents(female))
            {
                var go = Object.Instantiate(srcGo, parent);
                go.name = "OrbitNippleUrinePs_" + parent.name;
                go.transform.localPosition = LocalPos;
                go.transform.localRotation = Quaternion.Euler(LocalEuler);
                go.transform.localScale = Vector3.one;
                go.SetActive(false);

                var ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>(true);
                if (ps == null)
                {
                    Object.Destroy(go);
                    continue;
                }

                OwnedObjects.Add(go);
                ParticleClones.Add(ps);
                var main = ps.main;
                ParticleBaseSpeeds.Add(main.startSpeed.constantMax > 0.01f ? main.startSpeed.constantMax : 1f);
                made++;
            }

            if (made > 0)
            {
                _lastStatus = $"乳潮粒子×{made}";
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭潮吹粒子×{made}");
                return true;
            }

            return false;
        }

        private static int TryGetFemaleUrinePlayParticleId(HScene hScene)
        {
            try
            {
                if (!TryGetFemaleUrineSiru(hScene, out _, out var chosen, out _))
                    return -1;
                if (chosen != null && chosen.PlayParticleID >= 0)
                    return chosen.PlayParticleID;
            }
            catch
            {
                // ignore
            }

            return -1;
        }

        private static bool TryReadParticleEntry(object? entry, out GameObject? go, out ParticleSystem? ps)
        {
            go = null;
            ps = null;
            if (entry == null)
                return false;
            var t = Traverse.Create(entry);
            go = t.Field("particleCacheObj").GetValue() as GameObject;
            ps = t.Field("particle").GetValue() as ParticleSystem;
            if (ps == null && go != null)
                ps = go.GetComponent<ParticleSystem>() ?? go.GetComponentInChildren<ParticleSystem>(true);
            return ps != null;
        }

        private static IEnumerable<Transform> ResolveNippleParents(ChaControl female)
        {
            var root = female.objBodyBone.transform;
            var l = FindFirstBone(root, NippleBoneCandidatesL);
            var r = FindFirstBone(root, NippleBoneCandidatesR);
            if (l != null)
                yield return l;
            if (r != null)
                yield return r;
        }

        private static Transform? FindFirstBone(Transform root, string[] names)
        {
            foreach (var name in names)
            {
                var t = root.FindLoop(name);
                if (t != null)
                    return t;
            }

            return null;
        }

        private static Vector3 LocalPos
        {
            get
            {
                float x = HS2OrbitAndExciter.OrgasmNippleSprayOffsetX?.Value ?? 0f;
                float y = HS2OrbitAndExciter.OrgasmNippleSprayOffsetY?.Value ?? 0f;
                float z = HS2OrbitAndExciter.OrgasmNippleSprayOffsetZ?.Value ?? 0.02f;
                return new Vector3(x, y, z);
            }
        }

        private static Vector3 LocalEuler
        {
            get
            {
                float x = HS2OrbitAndExciter.OrgasmNippleSprayRotX?.Value ?? 90f;
                float y = HS2OrbitAndExciter.OrgasmNippleSprayRotY?.Value ?? 0f;
                float z = HS2OrbitAndExciter.OrgasmNippleSprayRotZ?.Value ?? 0f;
                return new Vector3(x, y, z);
            }
        }

        private static void StartSpray(HScene hScene, ChaControl female)
        {
            var host = FindHost();
            if (host == null)
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 乳頭潮吹無 Coroutine host");
                return;
            }

            StopBurstRoutine();
            EnsureSolverActive(hScene);

            // Prefer native urine Play(-1,height) when Obi emitters exist.
            if (UseNativeUrineRhythm && ObiEmitters.Count > 0)
            {
                int gen = ++_burstGen;
                _lastStatus = $"乳潮吹×{ObiEmitters.Count}";
                _burstRoutine = host.StartCoroutine(NativeUrinePlayRoutine(hScene, female, gen));
                HS2OrbitAndExciter.Log?.LogInfo("Orbit: 高潮乳頭潮吹（原生 urine Play/-1 splitInfos）");
                return;
            }

            int bursts = BurstCount;
            int genCustom = ++_burstGen;
            _lastStatus = $"乳潮連×{bursts}";
            _burstRoutine = host.StartCoroutine(BurstRoutine(hScene, female, bursts, genCustom));
            HS2OrbitAndExciter.Log?.LogInfo(
                $"Orbit: 高潮乳頭潮吹自訂連噴 {bursts} 次（速 {SpeedStartMult:F1}→{SpeedEndMult:F1}，量 {AmountOverall:F1}×{AmountStartWeight:F1}→{AmountEndWeight:F1}，間隔 {BurstInterval:F2}s）");
        }

        private static IEnumerator NativeUrinePlayRoutine(HScene hScene, ChaControl female, int gen)
        {
            var parents = new List<Transform>();
            foreach (var p in ResolveNippleParents(female))
                parents.Add(p);

            float height = 0.5f;
            try
            {
                height = female.GetShapeBodyValue(0);
            }
            catch
            {
                // keep default
            }

            int boneIdx = 0;
            for (int i = 0; i < ObiEmitters.Count; i++)
            {
                var ctrl = ObiEmitters[i];
                if (ctrl == null)
                    continue;
                try
                {
                    ApplyLocalXform(ctrl.transform, parents, ref boneIdx);
                    ctrl.Play(-1, height);
                }
                catch (System.Exception ex)
                {
                    HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 乳頭潮吹 Play 失敗: {ex.Message}");
                }
            }

            // Also pulse particle fallback if any (no splitInfos there).
            if (ParticleClones.Count > 0)
            {
                boneIdx = 0;
                for (int i = 0; i < ParticleClones.Count; i++)
                {
                    var ps = ParticleClones[i];
                    if (ps == null)
                        continue;
                    try
                    {
                        ApplyLocalXform(ps.transform, parents, ref boneIdx);
                        if (!ps.gameObject.activeSelf)
                            ps.gameObject.SetActive(true);
                        ps.Emit(Mathf.Max(4, Mathf.RoundToInt(16f * AmountOverall)));
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            _lastStatus = $"乳潮吹中×{ObiEmitters.Count}";
            // Wait roughly for urine multi-burst to finish (splitInfos vary; ~3s covers typical).
            float wait = 3.5f;
            while (wait > 0f)
            {
                if (gen != _burstGen)
                    yield break;
                wait -= Time.deltaTime;
                yield return null;
            }

            if (gen == _burstGen)
            {
                _lastStatus = "乳潮吹完";
                _burstRoutine = null;
            }
        }

        private static float[] BuildAmountRateSteps(int bursts)
        {
            // ObiEmitterCtrl uses cumulative rate where rate*0.1 = fraction of NumParticles; sum≈10 ≈ full tank.
            var steps = new float[bursts];
            float wSum = 0f;
            for (int b = 0; b < bursts; b++)
            {
                float t = bursts <= 1 ? 0f : b / (float)(bursts - 1);
                steps[b] = Mathf.Lerp(AmountStartWeight, AmountEndWeight, t);
                wSum += steps[b];
            }

            if (wSum < 0.001f)
                wSum = 1f;
            float budget = 10f * AmountOverall;
            for (int b = 0; b < bursts; b++)
                steps[b] = (steps[b] / wSum) * budget;
            return steps;
        }

        private static IEnumerator BurstRoutine(HScene hScene, ChaControl female, int bursts, int gen)
        {
            var parents = new List<Transform>();
            foreach (var p in ResolveNippleParents(female))
                parents.Add(p);

            for (int i = 0; i < ObiEmitters.Count; i++)
            {
                try { ObiEmitters[i]?.Stop(); } catch { /* ignore */ }
            }

            yield return null;
            if (gen != _burstGen)
                yield break;

            float rateAcc = 0f;
            float[] rateSteps = BuildAmountRateSteps(bursts);
            float interval = BurstInterval;
            float startM = SpeedStartMult;
            float endM = SpeedEndMult;
            float amtStart = AmountStartWeight;
            float amtEnd = AmountEndWeight;
            float amtAll = AmountOverall;

            for (int b = 0; b < bursts; b++)
            {
                if (gen != _burstGen)
                    yield break;

                float t = bursts <= 1 ? 0f : b / (float)(bursts - 1);
                float speedMult = Mathf.Lerp(startM, endM, t);
                float amountMult = Mathf.Lerp(amtStart, amtEnd, t) * amtAll;
                rateAcc += rateSteps[b];

                var targets = new List<int>(ObiEmitters.Count);
                int boneIdx = 0;
                for (int i = 0; i < ObiEmitters.Count; i++)
                {
                    var ctrl = ObiEmitters[i];
                    targets.Add(0);
                    if (ctrl?.ObiEmitter == null)
                        continue;
                    try
                    {
                        ApplyLocalXform(ctrl.transform, parents, ref boneIdx);
                        float baseSp = i < ObiBaseSpeeds.Count ? ObiBaseSpeeds[i] : 1f;
                        float speed = Mathf.Max(0.05f, baseSp * speedMult);
                        ctrl.Speed = speed;
                        ctrl.ObiEmitter.speed = speed;
                        int target = Mathf.FloorToInt(Mathf.Lerp(0f, ctrl.ObiEmitter.NumParticles, Mathf.Clamp01(rateAcc * 0.1f)));
                        targets[i] = Mathf.Clamp(target, 1, ctrl.ObiEmitter.NumParticles);
                        ctrl.ObiEmitter.playMode = ObiEmitter.PlayMode.Play;
                    }
                    catch (System.Exception ex)
                    {
                        HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 乳頭潮吹 Obi 連噴失敗: {ex.Message}");
                    }
                }

                float guard = 0f;
                while (guard < 1.5f)
                {
                    bool anyPending = false;
                    for (int i = 0; i < ObiEmitters.Count; i++)
                    {
                        var obi = ObiEmitters[i]?.ObiEmitter;
                        if (obi == null || targets[i] <= 0)
                            continue;
                        if (obi.ActiveParticles < obi.NumParticles && obi.ActiveParticles < targets[i])
                        {
                            anyPending = true;
                            break;
                        }
                    }

                    if (!anyPending)
                        break;
                    guard += Time.deltaTime;
                    yield return null;
                    if (gen != _burstGen)
                        yield break;
                }

                for (int i = 0; i < ObiEmitters.Count; i++)
                {
                    try
                    {
                        if (ObiEmitters[i]?.ObiEmitter != null)
                            ObiEmitters[i].ObiEmitter.playMode = ObiEmitter.PlayMode.Stop;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                boneIdx = 0;
                for (int i = 0; i < ParticleClones.Count; i++)
                {
                    var ps = ParticleClones[i];
                    if (ps == null)
                        continue;
                    try
                    {
                        ApplyLocalXform(ps.transform, parents, ref boneIdx);
                        float baseSp = i < ParticleBaseSpeeds.Count ? ParticleBaseSpeeds[i] : 1f;
                        var main = ps.main;
                        main.startSpeed = Mathf.Max(0.05f, baseSp * speedMult);
                        if (!ps.gameObject.activeSelf)
                            ps.gameObject.SetActive(true);
                        int emitCount = Mathf.Max(2, Mathf.RoundToInt(12f * amountMult));
                        ps.Emit(emitCount);
                    }
                    catch (System.Exception ex)
                    {
                        HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 乳頭潮吹粒子連噴失敗: {ex.Message}");
                    }
                }

                _lastStatus = $"乳潮{b + 1}/{bursts}·速{speedMult:F1}·量{amountMult:F1}";

                if (b + 1 < bursts)
                    yield return new WaitForSeconds(interval);
            }

            if (gen == _burstGen)
            {
                _lastStatus = $"乳潮連×{bursts}完";
                _burstRoutine = null;
            }
        }

        private static void EnsureSolverActive(HScene hScene)
        {
            var obiCtrl = Traverse.Create(hScene).Field("ctrlObi").GetValue() as ObiCtrl;
            var solver = obiCtrl != null
                ? Traverse.Create(obiCtrl).Field("solver").GetValue() as Component
                : null;
            if (solver != null && !solver.gameObject.activeSelf)
            {
                solver.gameObject.SetActive(true);
                Traverse.Create(obiCtrl).Field("checkWait").SetValue(true);
            }
        }

        private static float ReadObiSpeed(ObiEmitterCtrl ctrl, float fallback)
        {
            try
            {
                var obi = ctrl?.ObiEmitter;
                if (obi != null && obi.speed > 0.01f)
                    return obi.speed;
            }
            catch
            {
                // ignore
            }

            return fallback > 0.01f ? fallback : 1f;
        }

        private static MonoBehaviour? FindHost()
        {
            var go = GameObject.Find("HS2OrbitAndExciterController");
            if (go != null)
            {
                var c = go.GetComponent<OrbitController>();
                if (c != null)
                    return c;
            }

            return Object.FindObjectOfType<OrbitController>();
        }

        private static void StopBurstRoutine()
        {
            _burstGen++;
            if (_burstRoutine == null)
                return;
            var host = FindHost();
            if (host != null)
                host.StopCoroutine(_burstRoutine);
            _burstRoutine = null;

            foreach (var ctrl in ObiEmitters)
            {
                try
                {
                    ctrl?.Stop();
                }
                catch
                {
                    try
                    {
                        if (ctrl?.ObiEmitter != null)
                            ctrl.ObiEmitter.playMode = ObiEmitter.PlayMode.Stop;
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        private static void ApplyLocalXform(Transform t, List<Transform> parents, ref int boneIdx)
        {
            if (t == null)
                return;
            if (boneIdx < parents.Count && parents[boneIdx] != null && t.parent != parents[boneIdx])
                t.SetParent(parents[boneIdx], false);
            boneIdx++;
            t.localPosition = LocalPos;
            t.localRotation = Quaternion.Euler(LocalEuler);
            t.localScale = Vector3.one;
        }

        private static void ReleaseOwned()
        {
            if (Singleton<ObiFluidManager>.IsInstance())
            {
                var manager = Singleton<ObiFluidManager>.Instance;
                foreach (var fluid in OwnedFluidCtrls)
                {
                    try
                    {
                        if (fluid != null)
                            manager.Release(fluid);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            OwnedFluidCtrls.Clear();
            ObiEmitters.Clear();
            ObiBaseSpeeds.Clear();

            foreach (var go in OwnedObjects)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            OwnedObjects.Clear();
            ParticleClones.Clear();
            ParticleBaseSpeeds.Clear();
        }
    }
}
