using System;
using System.Reflection;
using HarmonyLib;

namespace HS2OrbitAndExciter
{
    internal enum OrbitPoseUnlockCheckKind
    {
        MotionLimit,
        MotionLimitRecover,
        AutoMotionLimit
    }

    internal readonly struct OrbitPosePoolStats
    {
        internal readonly int Total;
        internal readonly int AfterUnlock;
        internal readonly int AfterFaintness;

        internal OrbitPosePoolStats(int total, int afterUnlock, int afterFaintness)
        {
            Total = total;
            AfterUnlock = afterUnlock;
            AfterFaintness = afterFaintness;
        }
    }

    internal static class OrbitPoseUnlockPolicy
    {
        private static readonly MethodInfo? CheckEventLimitMethod =
            AccessTools.Method(typeof(HSceneSprite), "CheckEventLimit");

        private static OrbitPosePoolStats _lastPosePoolStats;
        private static int _relaxedMotionLimit;
        private static int _relaxedMotionLimitRecover;
        private static int _relaxedAutoMotionLimit;
        private static int _unsafeRejectCount;
        private static int _errorCount;

        internal static OrbitPosePoolStats LastPosePoolStats => _lastPosePoolStats;
        internal static int RelaxedMotionLimitCount => _relaxedMotionLimit;
        internal static int RelaxedMotionLimitRecoverCount => _relaxedMotionLimitRecover;
        internal static int RelaxedAutoMotionLimitCount => _relaxedAutoMotionLimit;
        internal static int UnsafeRejectCount => _unsafeRejectCount;
        internal static int ErrorCount => _errorCount;

        internal static void RecordPosePoolStats(int total, int afterUnlock, int afterFaintness)
        {
            _lastPosePoolStats = new OrbitPosePoolStats(total, afterUnlock, afterFaintness);
        }

        internal static bool TryRelaxSafeChecks(
            HSceneSprite? sprite,
            HScene.AnimationListInfo? info,
            OrbitPoseUnlockCheckKind kind,
            ref bool result)
        {
            if (result || sprite == null || info == null)
                return false;
            if (HS2OrbitAndExciter.EnableSafePoseUnlock?.Value == false)
                return false;

            try
            {
                int eventNo = ReadIntField(sprite, "EventNo", -1);
                int eventPeep = ReadIntField(sprite, "EventPeep", -1);
                if (!WouldUnsafeChecksPass(sprite, info, eventNo, eventPeep))
                {
                    _unsafeRejectCount++;
                    return false;
                }

                result = true;
                Increment(kind);
                return true;
            }
            catch (Exception ex)
            {
                _errorCount++;
                HS2OrbitAndExciter.Log?.LogDebug($"Orbit pose unlock skipped: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static void Increment(OrbitPoseUnlockCheckKind kind)
        {
            switch (kind)
            {
                case OrbitPoseUnlockCheckKind.MotionLimit:
                    _relaxedMotionLimit++;
                    break;
                case OrbitPoseUnlockCheckKind.MotionLimitRecover:
                    _relaxedMotionLimitRecover++;
                    break;
                case OrbitPoseUnlockCheckKind.AutoMotionLimit:
                    _relaxedAutoMotionLimit++;
                    break;
            }
        }

        private static int ReadIntField(HSceneSprite sprite, string fieldName, int fallback)
        {
            try
            {
                return Traverse.Create(sprite).Field(fieldName).GetValue<int>();
            }
            catch
            {
                return fallback;
            }
        }

        private static bool WouldUnsafeChecksPass(
            HSceneSprite sprite,
            HScene.AnimationListInfo info,
            int eventNo,
            int eventPeep)
        {
            if (!HasRequiredActors(sprite, info))
                return false;
            if (!PassesEventNo19SpecialCase(info, eventNo))
                return false;
            if (!PassesEventLimit(sprite, info))
                return false;
            if (!PassesPeepingEventMatch(info, eventNo, eventPeep))
                return false;
            if (!sprite.CheckPlace(info))
                return false;
            if (!sprite.CheckAppendEV(info, eventNo))
                return false;
            return true;
        }

        private static bool HasRequiredActors(HSceneSprite sprite, HScene.AnimationListInfo info)
        {
            int item1 = info.ActionCtrl.Item1;
            if (item1 == 4 || item1 == 5)
                return HasActor(Traverse.Create(sprite).Field("chaFemales").GetValue() as Array, 1);
            if (item1 == 6)
                return HasActor(Traverse.Create(sprite).Field("chaMales").GetValue() as Array, 1);
            return true;
        }

        private static bool HasActor(Array? actors, int index)
        {
            if (actors == null || actors.Length <= index)
                return false;
            return actors.GetValue(index) != null;
        }

        private static bool PassesEventNo19SpecialCase(HScene.AnimationListInfo info, int eventNo)
        {
            if (eventNo != 19)
                return true;
            int item1 = info.ActionCtrl.Item1;
            if (item1 == 4 || item1 == 5 || item1 == 6)
                return false;
            return !(item1 == 3 && info.id == 0);
        }

        private static bool PassesEventLimit(HSceneSprite sprite, HScene.AnimationListInfo info)
        {
            if (CheckEventLimitMethod == null)
                return false;
            object? value = CheckEventLimitMethod.Invoke(sprite, new object[] { info.Event });
            return value is bool ok && ok;
        }

        private static bool PassesPeepingEventMatch(HScene.AnimationListInfo info, int eventNo, int eventPeep)
        {
            if (info.ActionCtrl.Item1 != 3)
                return true;

            int item2 = info.ActionCtrl.Item2;
            if (item2 != 5 && item2 != 6)
                return true;
            if (info.Event == null)
                return false;

            foreach (int[] item in info.Event)
            {
                if (item == null || item.Length < 2)
                    continue;
                if (item[1] == -1)
                {
                    if (item[0] == eventNo)
                        return true;
                }
                else if (item[0] == eventNo && item[1] == eventPeep)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
