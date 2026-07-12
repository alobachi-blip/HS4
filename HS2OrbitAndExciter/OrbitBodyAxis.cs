using AIChara;
using UnityEngine;

namespace HS2OrbitAndExciter
{
    /// <summary>
    /// §11：以女角軀幹軸（頭−骨盆）＋面向建立身體空間，供環視相對角／俯仰使用。
    /// </summary>
    internal static class OrbitBodyAxis
    {
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

        /// <summary>亂數相對角改變量：|Δ|≥60°、非整數、避免近 180° 幾乎原地。</summary>
        internal static float RollRelativeDeltaDegrees()
        {
            float mag = UnityEngine.Random.Range(MinRelativeDeltaDegrees, MaxRelativeDeltaDegrees);
            // 帶小數，避開整數檔
            mag += UnityEngine.Random.Range(0.07f, 0.93f);
            if (UnityEngine.Random.value < 0.5f)
                mag = -mag;
            return mag;
        }

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
                // 缺軸時用角色 up／forward 退化
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
            // 面向投影到垂直軀幹軸的平面
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

        /// <summary>
        /// 身體空間方位角 → 建議的相機 Euler（相對世界，之後再轉成 CamDat.Rot 時由呼叫端對齊 transBase）。
        /// azimuthDegrees：繞軀幹軸的方位；pitchDegrees：相對水平面的小幅俯仰（對準部位）。
        /// </summary>
        internal static Vector3 DirectionFromBodyAzimuth(Basis basis, float azimuthDegrees, float pitchDegrees)
        {
            // 從「背對面向」開始繞 torsoUp 轉
            Quaternion yaw = Quaternion.AngleAxis(azimuthDegrees, basis.TorsoUp);
            Vector3 radial = yaw * (-basis.Facing);
            Quaternion pitch = Quaternion.AngleAxis(pitchDegrees, basis.Right);
            radial = pitch * radial;
            return radial.normalized;
        }

        /// <summary>依焦點給小幅俯仰：頭略俯視、骨盆略仰視。</summary>
        internal static float PitchForFocus(int focusIndex)
        {
            int part = focusIndex % 3;
            if (part == 0) return 8f;   // 頭
            if (part == 1) return 2f;   // 胸
            return -6f;                 // 骨盆
        }
    }
}
