using AIChara;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// 三種繞法（輪替、與上次不同）：
    /// 1 軀幹軸（頭−骨盆）、2 世界鉛垂、3 身體側向（左−右）。
    /// 每段再加任意起始方位角；不做俯仰／zoom 搖晃。
    /// </summary>
    internal static class OrbitBodyAxis
    {
        internal enum OrbitAxisMode : byte
        {
            /// <summary>繞頭−骨盆（身體長軸）。</summary>
            Torso = 0,
            /// <summary>繞世界鉛垂。</summary>
            WorldVertical = 1,
            /// <summary>繞身體左右軸（翻轉看上／下）。</summary>
            BodyLateral = 2,
        }

        internal const float MinRelativeDeltaDegrees = 60f;
        internal const float MaxRelativeDeltaDegrees = 170f;

        internal readonly struct Basis
        {
            internal readonly Vector3 FocusWorld;
            internal readonly Vector3 TorsoUp;
            internal readonly Vector3 Facing;
            internal readonly Vector3 Right;
            internal readonly bool Valid;

            internal Basis(Vector3 focusWorld, Vector3 torsoUp, Vector3 facing, Vector3 right, bool valid)
            {
                FocusWorld = focusWorld;
                TorsoUp = torsoUp;
                Facing = facing;
                Right = right;
                Valid = valid;
            }
        }

        internal static OrbitAxisMode PickNextMode(OrbitAxisMode previous)
        {
            // 三選一，排除上次
            int skip = (int)previous;
            int pick = UnityEngine.Random.Range(0, 2); // 0 or 1 among the two remaining
            int mode = 0;
            int seen = 0;
            for (int i = 0; i < 3; i++)
            {
                if (i == skip)
                    continue;
                if (seen == pick)
                {
                    mode = i;
                    break;
                }
                seen++;
            }
            return (OrbitAxisMode)mode;
        }

        /// <summary>任意起始方位角（非整數，0～360）。</summary>
        internal static float RollAnyAzimuthDegrees()
        {
            float a = UnityEngine.Random.Range(0f, 360f);
            a += UnityEngine.Random.Range(0.07f, 0.93f);
            return a % 360f;
        }

        /// <summary>亂數相對角改變量（舊路徑備用）。</summary>
        internal static float RollRelativeDeltaDegrees()
        {
            float mag = UnityEngine.Random.Range(MinRelativeDeltaDegrees, MaxRelativeDeltaDegrees);
            mag += UnityEngine.Random.Range(0.07f, 0.93f);
            if (UnityEngine.Random.value < 0.5f)
                mag = -mag;
            return mag;
        }

        internal static string ModeLabel(OrbitAxisMode mode) => mode switch
        {
            OrbitAxisMode.Torso => "軀幹軸",
            OrbitAxisMode.WorldVertical => "鉛垂軸",
            OrbitAxisMode.BodyLateral => "側向軸",
            _ => "軸"
        };

        internal static Basis TryBuild(
            ChaControl[]? chaFemales,
            int focusIndex,
            Vector3? previousFocusWorld)
        {
            if (chaFemales == null || chaFemales.Length == 0)
                return default;

            int femaleIdx = focusIndex < 3 ? 0 : 1;
            if (femaleIdx >= chaFemales.Length || chaFemales[femaleIdx] == null)
                femaleIdx = 0;
            var cha = chaFemales[femaleIdx];
            if (cha == null)
                return default;

            var head = OrbitHelpers.GetBonePosition(chaFemales, femaleIdx, OrbitHelpers.BoneHead);
            var pelvis = OrbitHelpers.GetBonePosition(chaFemales, femaleIdx, OrbitHelpers.BonePelvis);
            var chest = OrbitHelpers.GetBonePosition(chaFemales, femaleIdx, OrbitHelpers.BoneChest);

            Vector3 focusWorld;
            string bone = focusIndex % 3 == 0 ? OrbitHelpers.BoneHead
                : focusIndex % 3 == 1 ? OrbitHelpers.BoneChest
                : OrbitHelpers.BonePelvis;
            var focus = OrbitHelpers.GetBonePosition(chaFemales, femaleIdx, bone);
            if (focus.HasValue)
                focusWorld = focus.Value;
            else if (previousFocusWorld.HasValue)
                focusWorld = previousFocusWorld.Value;
            else if (chest.HasValue)
                focusWorld = chest.Value;
            else
                return default;

            if (!head.HasValue || !pelvis.HasValue)
            {
                Transform t = cha.objBodyBone != null ? cha.objBodyBone.transform : cha.transform;
                Vector3 up = t.up;
                Vector3 facingFallback = t.forward;
                Vector3 right = Vector3.Cross(up, facingFallback).normalized;
                if (right.sqrMagnitude < 1e-6f)
                    right = t.right;
                facingFallback = Vector3.Cross(right, up).normalized;
                return new Basis(focusWorld, up.normalized, facingFallback, right, true);
            }

            Vector3 torsoUp = (head.Value - pelvis.Value).normalized;
            if (torsoUp.sqrMagnitude < 1e-6f)
                torsoUp = Vector3.up;

            Transform body = cha.objBodyBone != null ? cha.objBodyBone.transform : cha.transform;
            Vector3 facingRaw = body.forward;
            Vector3 facingAxis = Vector3.ProjectOnPlane(facingRaw, torsoUp);
            if (facingAxis.sqrMagnitude < 1e-4f)
                facingAxis = Vector3.ProjectOnPlane(body.right, torsoUp);
            if (facingAxis.sqrMagnitude < 1e-4f)
                facingAxis = Vector3.Cross(torsoUp, Vector3.right);
            facingAxis.Normalize();

            Vector3 rightAxis = Vector3.Cross(torsoUp, facingAxis).normalized;
            if (rightAxis.sqrMagnitude < 1e-6f)
                rightAxis = body.right;
            facingAxis = Vector3.Cross(rightAxis, torsoUp).normalized;

            return new Basis(focusWorld, torsoUp, facingAxis, rightAxis, true);
        }

        internal static Vector3 SpinAxis(Basis basis, OrbitAxisMode mode)
        {
            switch (mode)
            {
                case OrbitAxisMode.WorldVertical:
                    return Vector3.up;
                case OrbitAxisMode.BodyLateral:
                    return basis.Right.sqrMagnitude > 1e-6f ? basis.Right.normalized : Vector3.right;
                default:
                    return basis.TorsoUp.sqrMagnitude > 1e-6f ? basis.TorsoUp.normalized : Vector3.up;
            }
        }

        /// <summary>繞指定軸的方位方向（無俯仰搖晃）。</summary>
        internal static Vector3 DirectionFromAxisAzimuth(Basis basis, OrbitAxisMode mode, float azimuthDegrees)
        {
            Vector3 axis = SpinAxis(basis, mode);
            Vector3 radial0 = Vector3.ProjectOnPlane(-basis.Facing, axis);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.ProjectOnPlane(basis.TorsoUp, axis);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.Cross(axis, Vector3.right);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.forward;
            radial0.Normalize();
            return (Quaternion.AngleAxis(azimuthDegrees, axis) * radial0).normalized;
        }

        /// <summary>相機 up：盡量用軀幹上，投影到視線垂直面，減少滾轉搖晃。</summary>
        internal static Vector3 CameraUp(Basis basis, Vector3 lookDirWorld)
        {
            Vector3 up = Vector3.ProjectOnPlane(basis.TorsoUp, lookDirWorld);
            if (up.sqrMagnitude < 1e-4f)
                up = Vector3.ProjectOnPlane(Vector3.up, lookDirWorld);
            if (up.sqrMagnitude < 1e-4f)
                up = Vector3.up;
            return up.normalized;
        }
    }
}
