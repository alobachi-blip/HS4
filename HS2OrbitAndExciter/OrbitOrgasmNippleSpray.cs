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
    /// Female orgasm: reuse male semen (射精) Obi emitters on left/right nipple bones.
    /// Falls back to cloning HParticleCtrl siru ParticleSystems when Obi is unavailable.
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

        private static readonly List<ObiEmitterCtrl> ObiEmitters = new List<ObiEmitterCtrl>(4);
        private static readonly List<ObiFluidCtrl> OwnedFluidCtrls = new List<ObiFluidCtrl>(4);
        private static readonly List<ParticleSystem> ParticleClones = new List<ParticleSystem>(4);
        private static readonly List<GameObject> OwnedObjects = new List<GameObject>(8);

        private static int _boundFemaleId = -1;
        private static int _boundHSceneId = -1;
        private static string _lastStatus = "乳噴待命";

        internal static bool Enabled => HS2OrbitAndExciter.OrgasmNippleSprayEnabled?.Value ?? true;

        internal static string HudStatus => !Enabled ? "乳噴關" : _lastStatus;

        internal static void Reset()
        {
            ReleaseOwned();
            _boundFemaleId = -1;
            _boundHSceneId = -1;
            _lastStatus = Enabled ? "乳噴待命" : "乳噴關";
        }

        /// <summary>Drop and rebuild nipple emitters (after Offset/Rot change in settings).</summary>
        internal static void ForceRebuild(HScene? hScene)
        {
            ReleaseOwned();
            _boundFemaleId = -1;
            _boundHSceneId = -1;
            if (!Enabled)
            {
                _lastStatus = "乳噴關";
                return;
            }

            hScene ??= OrbitController.TryGetHScene();
            var cha = hScene != null ? OrbitHelpers.GetChaFemales(hScene)?[0] : null;
            if (hScene == null || cha == null || cha.objBodyBone == null)
            {
                _lastStatus = "乳噴待命";
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 重建乳頭噴口需要在 H 場景且有女主");
                return;
            }

            if (EnsureEmitters(hScene, cha))
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭噴口已重建（{_lastStatus}）");
            else
            {
                _lastStatus = "乳噴失敗";
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 乳頭噴口重建失敗");
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
                _lastStatus = "乳噴無女";
                return;
            }

            if (!EnsureEmitters(hScene, cha))
            {
                HS2OrbitAndExciter.Log?.LogWarning("Orbit: 乳頭射精特效無法建立（無射精 emitter／粒子）");
                return;
            }

            PlayAll(hScene, cha);
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

            if (TryBuildObiFromMaleSiru(hScene, female))
                return true;

            if (TryBuildObiByCloningMaleEmitter(hScene, female))
                return true;

            return TryBuildParticleClones(hScene, female);
        }

        private static bool TryBuildObiFromMaleSiru(HScene hScene, ChaControl female)
        {
            if (!Singleton<ObiFluidManager>.IsInstance())
                return false;

            var obiCtrl = Traverse.Create(hScene).Field("ctrlObi").GetValue() as ObiCtrl;
            if (obiCtrl == null)
                return false;

            var siruInfos = Traverse.Create(obiCtrl).Field("siruInfos").GetValue()
                as Dictionary<int, Dictionary<int, ObiCtrl.SiruObiInfo>>;
            if (siruInfos == null || !siruInfos.TryGetValue(0, out var maleSlots) || maleSlots == null || maleSlots.Count == 0)
                return false;

            var urineIds = hScene.ctrlFlag?.UrineIDs;
            ObiCtrl.SiruObiInfo? chosen = null;
            foreach (var kv in maleSlots)
            {
                if (urineIds != null && urineIds.Contains(kv.Key))
                    continue;
                chosen = kv.Value;
                break;
            }

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
                made++;
            }

            if (made > 0)
            {
                _lastStatus = $"乳噴Obi×{made}";
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭射精 Obi×{made}（siru 設定）");
                return true;
            }

            return false;
        }

        private static bool TryBuildObiByCloningMaleEmitter(HScene hScene, ChaControl female)
        {
            var obiCtrl = Traverse.Create(hScene).Field("ctrlObi").GetValue() as ObiCtrl;
            if (obiCtrl == null)
                return false;

            var maleCtrls = Traverse.Create(obiCtrl).Field("obiFluidCtrlMale").GetValue() as ObiFluidCtrl[];
            if (maleCtrls == null || maleCtrls.Length == 0 || maleCtrls[0]?.ObiEmitterCtrls == null)
                return false;

            ObiEmitterCtrl? source = null;
            var urineIds = hScene.ctrlFlag?.UrineIDs;
            var emitters = maleCtrls[0].ObiEmitterCtrls;
            for (int i = 0; i < emitters.Length; i++)
            {
                if (emitters[i] == null)
                    continue;
                if (urineIds != null && urineIds.Contains(i))
                    continue;
                source = emitters[i];
                break;
            }

            if (source == null)
                return false;

            var solver = Traverse.Create(obiCtrl).Field("solver").GetValue() as Component;
            int made = 0;
            foreach (var parent in ResolveNippleParents(female))
            {
                var go = Object.Instantiate(source.gameObject, parent);
                go.name = "OrbitNippleSiru_" + parent.name;
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

                TryRegisterParticleRenderer(source, clone);
                OwnedObjects.Add(go);
                ObiEmitters.Add(clone);
                made++;
            }

            if (solver != null && !solver.gameObject.activeSelf)
                solver.gameObject.SetActive(true);

            if (made > 0)
            {
                _lastStatus = $"乳噴Clone×{made}";
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭射精 clone Obi×{made}");
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
                HS2OrbitAndExciter.Log?.LogDebug($"Orbit: 乳頭射精 renderer 註冊略過: {ex.Message}");
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

            // Prefer PlayParticleID from male siru slot 0.
            int preferId = TryGetMalePlayParticleId(hScene);
            if (preferId >= 0 && preferId < lstParticle.Count)
                TryReadParticleEntry(lstParticle[preferId], out srcGo, out srcPs);

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
                go.name = "OrbitNippleSiruPs_" + parent.name;
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
                made++;
            }

            if (made > 0)
            {
                _lastStatus = $"乳噴粒子×{made}";
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 乳頭射精粒子×{made}");
                return true;
            }

            return false;
        }

        private static int TryGetMalePlayParticleId(HScene hScene)
        {
            try
            {
                var obiCtrl = Traverse.Create(hScene).Field("ctrlObi").GetValue() as ObiCtrl;
                if (obiCtrl == null)
                    return -1;
                var siruInfos = Traverse.Create(obiCtrl).Field("siruInfos").GetValue()
                    as Dictionary<int, Dictionary<int, ObiCtrl.SiruObiInfo>>;
                if (siruInfos == null || !siruInfos.TryGetValue(0, out var maleSlots) || maleSlots == null)
                    return -1;
                var urineIds = hScene.ctrlFlag?.UrineIDs;
                foreach (var kv in maleSlots)
                {
                    if (urineIds != null && urineIds.Contains(kv.Key))
                        continue;
                    if (kv.Value.PlayParticleID >= 0)
                        return kv.Value.PlayParticleID;
                }
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

        private static void PlayAll(HScene hScene, ChaControl female)
        {
            float height = 0.5f;
            try
            {
                height = female.GetShapeBodyValue(0);
            }
            catch
            {
                // keep default
            }

            var obiCtrl = Traverse.Create(hScene).Field("ctrlObi").GetValue() as ObiCtrl;
            var solver = obiCtrl != null
                ? Traverse.Create(obiCtrl).Field("solver").GetValue() as Component
                : null;
            if (solver != null && !solver.gameObject.activeSelf)
            {
                solver.gameObject.SetActive(true);
                Traverse.Create(obiCtrl).Field("checkWait").SetValue(true);
            }

            int played = 0;
            var parents = new System.Collections.Generic.List<Transform>();
            foreach (var p in ResolveNippleParents(female))
                parents.Add(p);

            int boneIdx = 0;
            foreach (var emitter in ObiEmitters)
            {
                if (emitter == null)
                    continue;
                try
                {
                    ApplyLocalXform(emitter.transform, parents, ref boneIdx);
                    emitter.Play(-1, height);
                    played++;
                }
                catch (System.Exception ex)
                {
                    HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 乳頭 Obi.Play 失敗: {ex.Message}");
                }
            }

            boneIdx = 0;
            foreach (var ps in ParticleClones)
            {
                if (ps == null)
                    continue;
                try
                {
                    ApplyLocalXform(ps.transform, parents, ref boneIdx);
                    if (!ps.gameObject.activeSelf)
                        ps.gameObject.SetActive(true);
                    ps.Simulate(0f, true, true);
                    ps.Play(true);
                    played++;
                }
                catch (System.Exception ex)
                {
                    HS2OrbitAndExciter.Log?.LogWarning($"Orbit: 乳頭粒子.Play 失敗: {ex.Message}");
                }
            }

            _lastStatus = played > 0 ? $"乳噴×{played}" : "乳噴失敗";
            if (played > 0)
                HS2OrbitAndExciter.Log?.LogInfo($"Orbit: 高潮乳頭射精播放 ×{played}");
        }

        private static void ApplyLocalXform(Transform t, System.Collections.Generic.List<Transform> parents, ref int boneIdx)
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

            foreach (var go in OwnedObjects)
            {
                if (go != null)
                    Object.Destroy(go);
            }

            OwnedObjects.Clear();
            ParticleClones.Clear();
        }
    }
}
