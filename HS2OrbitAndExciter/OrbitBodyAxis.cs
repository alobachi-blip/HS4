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
            return DirectionFromAxisAzimuth(basis, axis, azimuthDegrees);
        }

        internal static Vector3 DirectionFromAxisAzimuth(Basis basis, Vector3 spinAxis, float azimuthDegrees)
        {
            Vector3 axis = spinAxis.sqrMagnitude > 1e-8f ? spinAxis.normalized : SpinAxis(basis, OrbitAxisMode.Torso);
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

        internal static float AzimuthMatchingDirection(Basis basis, Vector3 spinAxis, Vector3 desiredDir)
        {
            Vector3 axis = spinAxis.sqrMagnitude > 1e-8f ? spinAxis.normalized : SpinAxis(basis, OrbitAxisMode.Torso);
            Vector3 radial0 = Vector3.ProjectOnPlane(-basis.Facing, axis);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.ProjectOnPlane(basis.TorsoUp, axis);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.Cross(axis, Vector3.right);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.forward;
            radial0.Normalize();
            Vector3 desired = Vector3.ProjectOnPlane(desiredDir, axis);
            if (desired.sqrMagnitude < 1e-6f)
                return 0f;
            float angle = Vector3.SignedAngle(radial0, desired.normalized, axis);
            return angle < 0f ? angle + 360f : angle;
        }

        /// <summary>
        /// 相機 up：以地板／天空法線做 horizon-lock，避免畫面上方朝地面或朝腳底。
        /// 視線接近鉛垂（萬向節死區）時短暫沿用上一幀 up。
        /// </summary>
        /// <param name="floorSkyward">地板法線（朝天空）；可為世界 up。</param>
        /// <param name="previousUp">上一幀相機 up（可選）；成功後會寫回。</param>
        internal static Vector3 CameraUp(
            Basis basis,
            Vector3 lookDirWorld,
            Vector3 floorSkyward,
            ref Vector3? previousUp)
        {
            Vector3 look = lookDirWorld.sqrMagnitude > 1e-8f ? lookDirWorld.normalized : Vector3.forward;
            Vector3 sky = floorSkyward.sqrMagnitude > 1e-6f ? floorSkyward.normalized : Vector3.up;
            if (Vector3.Dot(sky, Vector3.up) < 0f)
                sky = -sky;

            // 視線太接近天空／地面法線 → 投影不穩，鎖上一幀
            float alignSky = Mathf.Abs(Vector3.Dot(look, sky));
            const float GimbalAlign = 0.96f; // ~16° 內視為死區

            Vector3 up;
            if (alignSky >= GimbalAlign && previousUp.HasValue && previousUp.Value.sqrMagnitude > 1e-6f)
            {
                up = Vector3.ProjectOnPlane(previousUp.Value, look);
                if (up.sqrMagnitude < 1e-5f)
                    up = previousUp.Value;
            }
            else
            {
                up = Vector3.ProjectOnPlane(sky, look);
                if (up.sqrMagnitude < 1e-4f)
                {
                    if (previousUp.HasValue && previousUp.Value.sqrMagnitude > 1e-6f)
                    {
                        up = Vector3.ProjectOnPlane(previousUp.Value, look);
                        if (up.sqrMagnitude < 1e-5f)
                            up = previousUp.Value;
                    }
                    else
                    {
                        up = Vector3.Cross(look, Vector3.right);
                        if (up.sqrMagnitude < 1e-6f)
                            up = Vector3.Cross(look, Vector3.forward);
                    }
                }
            }

            up.Normalize();

            // 禁止畫面上方朝地面（與天空法線反向）
            if (Vector3.Dot(up, sky) < 0f)
                up = -up;

            // 禁止畫面上方朝女主角腳底（相對頭→腳）
            Vector3 headDir = basis.TorsoUp.sqrMagnitude > 1e-6f ? basis.TorsoUp.normalized : sky;
            Vector3 feetDir = -headDir;
            float towardFeet = Vector3.Dot(up, feetDir);
            float towardHead = Vector3.Dot(up, headDir);
            if (towardFeet > towardHead && towardFeet > 0.2f)
            {
                Vector3 flipped = -up;
                // 翻轉後仍不得倒向地面；若翻轉更貼近天空則採用
                if (Vector3.Dot(flipped, sky) >= -0.05f
                    && Vector3.Dot(flipped, sky) + 0.02f >= Vector3.Dot(up, sky))
                    up = flipped;
            }

            // 再保險一次：翻完仍可能倒地
            if (Vector3.Dot(up, sky) < 0f)
                up = -up;

            previousUp = up;
            return up;
        }
    }
}
