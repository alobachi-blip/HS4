using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AIChara;
using HarmonyLib;
using IllusionUtility.GetUtility;
using Illusion;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// Helpers for orbit: get chaFemales from HScene, six-focus bone names, set focus/distance/clothes.
    /// </summary>
    public static class OrbitHelpers
    {
        public const string BoneHead = "cf_J_Head";
        public const string BoneChest = "cf_J_Mune00";
        public const string BonePelvis = "cf_J_Kokan";
        /// <summary>Left foot bone for measuring full body height (head to foot).</summary>
        private const string BoneFootL = "cf_J_Foot_L";

        public static ChaControl[]? GetChaFemales(HScene hScene)
        {
            if (hScene == null) return null;
            var t = Traverse.Create(hScene);
            var arr = t.Field("chaFemales").GetValue();
            return arr as ChaControl[];
        }

        /// <summary>
        /// <see cref="ChaInfo.animBody"/> is a property (not a field); <c>Traverse.Field("animBody")</c> returns null and breaks gates.
        /// </summary>
        public static Animator? TryGetFemaleAnimBody(ChaControl? cha)
        {
            if (cha == null) return null;
            if (cha.animBody != null) return cha.animBody;
            if (cha.objAnim != null)
                return cha.objAnim.GetComponent<Animator>();
            return null;
        }

        /// <summary>Animator state names for H-scene action loop (excitement / speed assist during orbit).</summary>
        private static readonly HashSet<string> ActionLoopStateNames = new HashSet<string>
        {
            "WLoop", "SLoop", "OLoop", "D_WLoop", "D_SLoop", "D_OLoop",
            "MLoop",
            "WIdle", "SIdle", "WAction", "SAction", "D_Action"
        };

        /// <summary>True when first female's animator layer 0 is in an action loop state (scene query only).</summary>
        public static bool IsFirstFemaleInActionLoop(HScene? hScene)
        {
            if (hScene == null) return false;
            var chaFemales = GetChaFemales(hScene);
            if (chaFemales == null || chaFemales.Length == 0) return false;
            var cha = chaFemales[0];
            if (cha == null) return false;
            var animBody = TryGetFemaleAnimBody(cha);
            if (animBody == null) return false;
            var stateInfo = animBody.GetCurrentAnimatorStateInfo(0);
            foreach (string name in ActionLoopStateNames)
            {
                if (stateInfo.IsName(name))
                    return true;
            }
            return false;
        }

        /// <summary>Get world position of a bone on female (0 or 1). Returns null if not found.</summary>
        public static Vector3? GetBonePosition(ChaControl[] chaFemales, int femaleIndex, string boneName)
        {
            if (chaFemales == null || femaleIndex < 0 || femaleIndex >= chaFemales.Length) return null;
            var cha = chaFemales[femaleIndex];
            if (cha == null || cha.objBodyBone == null) return null;
            var tr = cha.objBodyBone.transform.FindLoop(boneName);
            if (tr == null) return null;
            return tr.position;
        }

        /// <summary>Six focus indices: 0=Head, 1=Chest, 2=Pelvis (female0), 3=Head2, 4=Chest2, 5=Pelvis2 (female1).</summary>
        public static Vector3? GetFocusPosition(ChaControl[]? chaFemales, int focusIndex, Transform transBase)
        {
            if (chaFemales == null || transBase == null) return null;
            string bone;
            int femaleIdx;
            switch (focusIndex)
            {
                case 0: bone = BoneHead; femaleIdx = 0; break;
                case 1: bone = BoneChest; femaleIdx = 0; break;
                case 2: bone = BonePelvis; femaleIdx = 0; break;
                case 3: bone = BoneHead; femaleIdx = 1; break;
                case 4: bone = BoneChest; femaleIdx = 1; break;
                case 5: bone = BonePelvis; femaleIdx = 1; break;
                default: return null;
            }
            var worldPos = GetBonePosition(chaFemales, femaleIdx, bone);
            if (!worldPos.HasValue) return null;
            return transBase.InverseTransformPoint(worldPos.Value);
        }

        /// <summary>Full body height in world units (head to foot). Uses first female; fallback 1.6f if bones missing.</summary>
        public static float GetBodyHeight(ChaControl[]? chaFemales, int femaleIndex = 0)
        {
            if (chaFemales == null || femaleIndex < 0 || femaleIndex >= chaFemales.Length) return 1.6f;
            var head = GetBonePosition(chaFemales, femaleIndex, BoneHead);
            var foot = GetBonePosition(chaFemales, femaleIndex, BoneFootL);
            if (head.HasValue && foot.HasValue)
                return Mathf.Max(0.5f, head.Value.y - foot.Value.y);
            var pelvis = GetBonePosition(chaFemales, femaleIndex, BonePelvis);
            if (head.HasValue && pelvis.HasValue)
                return Mathf.Max(0.5f, (head.Value.y - pelvis.Value.y) * 2.2f);
            return 1.6f;
        }

        /// <summary>Max focus count: 6 if two females, else 3.</summary>
        public static int GetMaxFocusIndex(ChaControl[]? chaFemales)
        {
            if (chaFemales == null || chaFemales.Length == 0) return 0;
            if (chaFemales.Length > 1 && chaFemales[1] != null && chaFemales[1].objBodyBone != null)
                return 6;
            return 3;
        }

        /// <summary>Sequence 0,1,2,3,2,1 (index 0..5). Returns stage 0..3 for given index.</summary>
        public static int ClothesSequenceStage(int index)
        {
            int[] seq = { 0, 1, 2, 3, 2, 1 };
            return seq[((index % 6) + 6) % 6];
        }

        /// <summary>Infer current clothes stage 0..3 from first character; return sequence index (0..5) so next step is from current state.</summary>
        public static int GetClothesSequenceIndexFromCurrent(ChaControl[]? chaFemales)
        {
            int stage = GetCurrentClothesStage(chaFemales);
            return stage;
        }

        /// <summary>0=Full, 1=Half, 2=KeepAccessories, 3=FullOff. GetClothesState is on ChaInfo (base), not ChaControl.</summary>
        private static int GetCurrentClothesStage(ChaControl[]? chaFemales)
        {
            if (chaFemales == null || chaFemales.Length == 0) return 0;
            var c = chaFemales[0];
            if (c == null) return 0;
            var getState = GetClothesStateMethod(c.GetType());
            if (getState == null) return 0;
            try
            {
                int s0 = (int)getState.Invoke(c, new object[] { 0 });
                if (s0 == 0) return 0;
                if (s0 == 1) return 1;
                int s4 = (int)getState.Invoke(c, new object[] { 4 });
                return s4 == 0 ? 2 : 3;
            }
            catch { return 0; }
        }

        private static MethodInfo? GetClothesStateMethod(System.Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                var m = t.GetMethod("GetClothesState", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
                if (m != null) return m;
            }
            return null;
        }

        /// <summary>Clothes stage 0=Full, 1=Half, 2=KeepAccessories, 3=FullOff. Apply to all characters in HScene.</summary>
        public static void SetClothesStage(ChaControl[]? chaFemales, ChaControl[]? chaMales, int stage)
        {
            void ApplyTo(ChaControl c)
            {
                if (c == null) return;
                switch (stage)
                {
                    case 0:
                        c.SetClothesStateAll(0);
                        break;
                    case 1:
                        c.SetClothesState(0, 1); c.SetClothesState(1, 1);
                        c.SetClothesState(2, 1); c.SetClothesState(3, 1);
                        break;
                    case 2:
                        c.SetClothesState(0, 2); c.SetClothesState(1, 2);
                        c.SetClothesState(2, 2); c.SetClothesState(3, 2);
                        c.SetClothesState(4, 0); c.SetClothesState(5, 0);
                        c.SetClothesState(6, 0); c.SetClothesState(7, 0);
                        break;
                    case 3:
                        c.SetClothesStateAll(2);
                        break;
                }
            }
            if (chaFemales != null)
                foreach (var c in chaFemales) ApplyTo(c);
            if (chaMales != null)
                foreach (var c in chaMales) ApplyTo(c);
        }

        public static ChaControl[]? GetChaMales(HScene hScene)
        {
            if (hScene == null) return null;
            var t = Traverse.Create(hScene);
            var arr = t.Field("chaMales").GetValue();
            return arr as ChaControl[];
        }

        /// <summary>Collect all AnimationListInfo from lstAnimInfo (all categories).</summary>
        public static List<HScene.AnimationListInfo> GetAllPoseList()
        {
            var tables = HSceneManager.HResourceTables;
            if (tables?.lstAnimInfo == null) return new List<HScene.AnimationListInfo>();
            var list = new List<HScene.AnimationListInfo>();
            for (int i = 0; i < tables.lstAnimInfo.Length; i++)
            {
                if (tables.lstAnimInfo[i] == null) continue;
                list.AddRange(tables.lstAnimInfo[i]);
            }
            return list;
        }

        /// <summary>Pick a random pose different from current (exclude by animation id, not reference).</summary>
        public static HScene.AnimationListInfo? PickNextPose(HScene.AnimationListInfo? current, List<HScene.AnimationListInfo> all)
        {
            if (all == null || all.Count == 0) return null;
            var candidates = new List<HScene.AnimationListInfo>();
            foreach (var item in all)
            {
                if (item == null) continue;
                if (current != null && item.id == current.id) continue;
                candidates.Add(item);
            }
            if (candidates.Count == 0) return null;
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>List *.png under UserData (e.g. chara/female/, coordinate/female/).</summary>
        public static List<string> ListUserDataPngFiles(string relativeDir)
        {
            var list = new List<string>();
            string dir = UserData.Path + relativeDir;
            if (!Directory.Exists(dir))
                return list;
            Utils.File.GetAllFiles(dir, "*.png", ref list);
            return list;
        }

        /// <summary>Full path to female card in UserData from loaded character, if file name is known.</summary>
        public static string? GetUserDataFemaleCharaPath(ChaControl? cha)
        {
            if (cha?.chaFile == null)
                return null;
            string? name = cha.chaFile.charaFileName;
            if (string.IsNullOrEmpty(name))
                return null;
            return UserData.Path + "chara/female/" + name + ".png";
        }

        /// <summary>One-time index: coordinateName → full path (built when file list is cached).</summary>
        public static Dictionary<string, string> BuildCoordinateNameIndex(IReadOnlyList<string> paths)
        {
            var index = new Dictionary<string, string>(StringComparer.Ordinal);
            if (paths == null)
                return index;
            foreach (string path in paths)
                TryIndexCoordinatePath(path, index);
            return index;
        }

        /// <summary>Add one coordinate file to name→path index (returns false if load failed).</summary>
        public static bool TryIndexCoordinatePath(string path, Dictionary<string, string> nameToPath)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            var coord = new ChaFileCoordinate();
            if (!coord.LoadFile(path))
                return false;
            string name = coord.coordinateName;
            if (string.IsNullOrEmpty(name))
                return false;
            nameToPath[name] = path;
            return true;
        }

        public static string? GetCurrentCoordinatePath(ChaControl? cha, Dictionary<string, string> nameToPath)
        {
            if (cha?.nowCoordinate == null || nameToPath.Count == 0)
                return null;
            string currentName = cha.nowCoordinate.coordinateName;
            if (string.IsNullOrEmpty(currentName))
                return null;
            return nameToPath.TryGetValue(currentName, out string? path) ? path : null;
        }

        /// <summary>Randomly advance wear state on 1..N active slots per character.</summary>
        public static bool ApplyRandomWearSlots(ChaControl[]? chaFemales, ChaControl[]? chaMales)
        {
            bool any = false;
            if (chaFemales != null)
            {
                foreach (var c in chaFemales)
                    any |= ApplyRandomWearSlotsTo(c);
            }
            if (chaMales != null)
            {
                foreach (var c in chaMales)
                    any |= ApplyRandomWearSlotsTo(c);
            }
            return any;
        }

        private static bool ApplyRandomWearSlotsTo(ChaControl? c)
        {
            if (c == null || c.objBodyBone == null || !c.visibleAll)
                return false;

            var slots = new List<int>();
            for (int i = 0; i < 8; i++)
            {
                if (c.IsClothesStateKind(i))
                    slots.Add(i);
            }
            if (slots.Count == 0)
                return false;

            int count = UnityEngine.Random.Range(1, slots.Count + 1);
            for (int n = 0; n < count; n++)
            {
                int idx = UnityEngine.Random.Range(0, slots.Count);
                int slot = slots[idx];
                slots.RemoveAt(idx);
                c.SetClothesStateNext(slot);
            }
            return true;
        }

        /// <summary>Set H scene faintness state (ctrlFlag.isFaintness) and request orbit camera reapply. No-op when not in H scene.</summary>
        public static void SetGameFaintnessAndRequestViewReapply(bool value)
        {
            if (!Singleton<HSceneManager>.IsInstance())
                return;
            var hScene = Singleton<HSceneManager>.Instance.Hscene;
            if (hScene?.ctrlFlag == null)
                return;
            var t = Traverse.Create(hScene.ctrlFlag);
            // Keep engine conditions consistent: many checks treat "faintness" as (isFaintness && (FaintnessType==0||1)).
            t.Field("isFaintness").SetValue(value);
            t.Field("FaintnessType").SetValue(value ? 1 : -1);
            t.Field("isFaintnessVoice").SetValue(value);
            OrbitController.RequestViewReapply();
        }
    }
}
