using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// CameraControl_Ver2.OnTriggerExit clears the whole collider list, even when
    /// only one collider leaves the camera capsule. During orbit that makes every
    /// occluder visible again for a moment. Track collider instances independently.
    /// </summary>
    internal static class OrbitCameraVanishTriggerPatch
    {
        private static readonly FieldInfo? ColliderListField =
            AccessTools.Field(typeof(CameraControl_Ver2), "listCollider");

        internal static bool IsOrbitCamera(CameraControl_Ver2 camera) =>
            camera != null && OrbitController.IsOrbitActive();

        internal static bool CanTrack => ColliderListField != null;

        internal static bool TrackEnter(CameraControl_Ver2 camera, Collider other)
        {
            if (!IsOrbitCamera(camera) || other == null || ColliderListField == null)
                return false;

            if (!(ColliderListField.GetValue(camera) is List<Collider> colliders))
                return false;

            for (int i = colliders.Count - 1; i >= 0; i--)
            {
                if (colliders[i] == null)
                    colliders.RemoveAt(i);
            }

            if (!colliders.Contains(other))
                colliders.Add(other);
            return true;
        }

        internal static bool TrackExit(CameraControl_Ver2 camera, Collider other)
        {
            if (!IsOrbitCamera(camera) || ColliderListField == null)
                return false;

            if (ColliderListField.GetValue(camera) is List<Collider> colliders)
            {
                for (int i = colliders.Count - 1; i >= 0; i--)
                {
                    if (colliders[i] == null || colliders[i] == other)
                        colliders.RemoveAt(i);
                }
            }

            // Vanilla clears the entire list here. Returning false keeps the other
            // colliders active while the camera is still inside them.
            return true;
        }
    }

    [HarmonyPatch(typeof(CameraControl_Ver2), "OnTriggerEnter")]
    internal static class OrbitCameraVanishOnTriggerEnterPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CameraControl_Ver2 __instance, Collider other)
        {
            if (!OrbitCameraVanishTriggerPatch.IsOrbitCamera(__instance))
                return true;

            if (!OrbitCameraVanishTriggerPatch.CanTrack || other == null)
                return true;

            OrbitCameraVanishTriggerPatch.TrackEnter(__instance, other);
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraControl_Ver2), "OnTriggerStay")]
    internal static class OrbitCameraVanishOnTriggerStayPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CameraControl_Ver2 __instance, Collider other)
        {
            if (!OrbitCameraVanishTriggerPatch.IsOrbitCamera(__instance))
                return true;

            if (!OrbitCameraVanishTriggerPatch.CanTrack || other == null)
                return true;

            OrbitCameraVanishTriggerPatch.TrackEnter(__instance, other);
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraControl_Ver2), "OnTriggerExit")]
    internal static class OrbitCameraVanishOnTriggerExitPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CameraControl_Ver2 __instance, Collider other)
        {
            if (!OrbitCameraVanishTriggerPatch.IsOrbitCamera(__instance))
                return true;

            if (!OrbitCameraVanishTriggerPatch.CanTrack)
                return true;

            OrbitCameraVanishTriggerPatch.TrackExit(__instance, other);
            return false;
        }
    }

    /// <summary>
    /// The vanilla vanish path deactivates the object that owns the renderer. For
    /// injected orbit entries this can also deactivate its collider and immediately
    /// undo the occlusion decision. Hide renderers only and keep colliders alive.
    /// </summary>
    [HarmonyPatch(typeof(CameraControl_Ver2), "visibleFroceVanish")]
    internal static class OrbitCameraRendererVanishPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(CameraControl_Ver2.VisibleObject _obj, bool _visible)
        {
            if (!OrbitController.IsOrbitActive()
                || !OrbitMapVanishAssist.IsInjectedEntry(_obj))
            {
                return true;
            }

            OrbitMapVanishAssist.SetInjectedEntryVisibility(_obj, _visible);
            return false;
        }
    }
}
