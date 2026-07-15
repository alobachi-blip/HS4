using AIChara;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// 人物三主軸輪替（與上次不同）：軀幹／前後／側向。
    /// 每圈再對主軸做亂數錐形傾斜；方位角沿傾斜軸連續累積，換軸時對齊上一視線。
    /// </summary>
    internal static class OrbitBodyAxis
    {
        internal enum OrbitAxisMode : byte
        {
            /// <summary>繞頭−骨盆（身體長軸）。</summary>
            Torso = 0,
            /// <summary>繞面−背（人物前後軸）。</summary>
            BodyFacing = 1,
            /// <summary>繞身體左右軸（翻轉看上／下）。</summary>
            BodyLateral = 2,
        }

        internal const float MinTiltDegrees = 8f;
        internal const float MaxTiltDegrees = 28f;

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
            int skip = (int)previous;
            int pick = UnityEngine.Random.Range(0, 2);
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

        /// <summary>在主軸周圍抽錐形傾斜，回傳實際旋轉軸（世界）與傾斜角。</summary>
        internal static Vector3 RollTiltedSpinAxis(Vector3 nominalAxis, out float tiltDegrees)
        {
            Vector3 n = nominalAxis.sqrMagnitude > 1e-8f ? nominalAxis.normalized : Vector3.up;
            tiltDegrees = UnityEngine.Random.Range(MinTiltDegrees, MaxTiltDegrees);
            tiltDegrees += UnityEngine.Random.Range(0.07f, 0.93f);
            if (tiltDegrees > MaxTiltDegrees)
                tiltDegrees = MaxTiltDegrees;

            Vector3 peri = Vector3.Cross(n, Vector3.up);
            if (peri.sqrMagnitude < 1e-6f)
                peri = Vector3.Cross(n, Vector3.right);
            if (peri.sqrMagnitude < 1e-6f)
                peri = Vector3.forward;
            peri.Normalize();
            float twist = UnityEngine.Random.Range(0f, 360f);
            peri = (Quaternion.AngleAxis(twist, n) * peri).normalized;
            return (Quaternion.AngleAxis(tiltDegrees, peri) * n).normalized;
        }

        /// <summary>把世界軸存成相對 basis 的本地分量（隨鎖定身體剛體跟隨）。</summary>
        internal static Vector3 AxisToBasisLocal(Basis basis, Vector3 axisWorld)
        {
            Vector3 w = axisWorld.sqrMagnitude > 1e-8f ? axisWorld.normalized : basis.TorsoUp;
            return new Vector3(
                Vector3.Dot(w, basis.Right),
                Vector3.Dot(w, basis.TorsoUp),
                Vector3.Dot(w, basis.Facing));
        }

        internal static Vector3 AxisFromBasisLocal(Basis basis, Vector3 axisLocal)
        {
            Vector3 w = basis.Right * axisLocal.x
                        + basis.TorsoUp * axisLocal.y
                        + basis.Facing * axisLocal.z;
            if (w.sqrMagnitude < 1e-8f)
                return NominalSpinAxis(basis, OrbitAxisMode.Torso);
            return w.normalized;
        }

        /// <summary>舊路徑備用：任意起始方位角。</summary>
        internal static float RollAnyAzimuthDegrees()
        {
            float a = UnityEngine.Random.Range(0f, 360f);
            a += UnityEngine.Random.Range(0.07f, 0.93f);
            return a % 360f;
        }

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
            OrbitAxisMode.BodyFacing => "前後軸",
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

        internal static Vector3 NominalSpinAxis(Basis basis, OrbitAxisMode mode)
        {
            switch (mode)
            {
                case OrbitAxisMode.BodyFacing:
                    return basis.Facing.sqrMagnitude > 1e-6f ? basis.Facing.normalized : Vector3.forward;
                case OrbitAxisMode.BodyLateral:
                    return basis.Right.sqrMagnitude > 1e-6f ? basis.Right.normalized : Vector3.right;
                default:
                    return basis.TorsoUp.sqrMagnitude > 1e-6f ? basis.TorsoUp.normalized : Vector3.up;
            }
        }

        /// <summary>兼容舊呼叫：未傾斜時的主軸。</summary>
        internal static Vector3 SpinAxis(Basis basis, OrbitAxisMode mode) =>
            NominalSpinAxis(basis, mode);

        internal static Vector3 RadialZero(Basis basis, Vector3 axis)
        {
            Vector3 a = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.up;
            Vector3 radial0 = Vector3.ProjectOnPlane(-basis.Facing, a);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.ProjectOnPlane(basis.TorsoUp, a);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.Cross(a, Vector3.right);
            if (radial0.sqrMagnitude < 1e-4f)
                radial0 = Vector3.forward;
            return radial0.normalized;
        }

        /// <summary>繞實際傾斜軸的方位方向。</summary>
        internal static Vector3 DirectionFromAxisAzimuth(Basis basis, Vector3 spinAxis, float azimuthDegrees)
        {
            Vector3 axis = spinAxis.sqrMagnitude > 1e-8f ? spinAxis.normalized : NominalSpinAxis(basis, OrbitAxisMode.Torso);
            Vector3 radial0 = RadialZero(basis, axis);
            return (Quaternion.AngleAxis(azimuthDegrees, axis) * radial0).normalized;
        }

        internal static Vector3 DirectionFromAxisAzimuth(Basis basis, OrbitAxisMode mode, float azimuthDegrees) =>
            DirectionFromAxisAzimuth(basis, NominalSpinAxis(basis, mode), azimuthDegrees);

        /// <summary>
        /// 找出繞 <paramref name="spinAxis"/> 時，最接近 <paramref name="desiredDir"/> 的方位角（0～360）。
        /// 換軸時用來對齊上一視線，避免亂跳。
        /// </summary>
        internal static float AzimuthMatchingDirection(Basis basis, Vector3 spinAxis, Vector3 desiredDir)
        {
            Vector3 axis = spinAxis.sqrMagnitude > 1e-8f ? spinAxis.normalized : NominalSpinAxis(basis, OrbitAxisMode.Torso);
            Vector3 radial0 = RadialZero(basis, axis);
            Vector3 desired = Vector3.ProjectOnPlane(desiredDir, axis);
            if (desired.sqrMagnitude < 1e-6f)
                return 0f;
            desired.Normalize();
            float ang = Vector3.SignedAngle(radial0, desired, axis);
            if (ang < 0f)
                ang += 360f;
            return ang % 360f;
        }

        /// <summary>
        /// 相機 up：以地板／天空法線做 horizon-lock，避免畫面上方朝地面或朝腳底。
        /// 視線接近鉛垂（萬向節死區）時短暫沿用上一幀 up。
        /// </summary>
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

            float alignSky = Mathf.Abs(Vector3.Dot(look, sky));
            const float GimbalAlign = 0.96f;

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

            if (Vector3.Dot(up, sky) < 0f)
                up = -up;

            Vector3 headDir = basis.TorsoUp.sqrMagnitude > 1e-6f ? basis.TorsoUp.normalized : sky;
            Vector3 feetDir = -headDir;
            float towardFeet = Vector3.Dot(up, feetDir);
            float towardHead = Vector3.Dot(up, headDir);
            if (towardFeet > towardHead && towardFeet > 0.2f)
            {
                Vector3 flipped = -up;
                if (Vector3.Dot(flipped, sky) >= -0.05f
                    && Vector3.Dot(flipped, sky) + 0.02f >= Vector3.Dot(up, sky))
                    up = flipped;
            }

            if (Vector3.Dot(up, sky) < 0f)
                up = -up;

            previousUp = up;
            return up;
        }
    }
}
