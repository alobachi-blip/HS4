using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using AIChara;
using HarmonyLib;
using Manager;
using UnityEngine;

namespace HS2OrbitAndExciter.Patches
{
    /// <summary>
    /// Some mod animations omit a female-state key from their BreathPtn table.
    /// Keep VoiceTour on its current stage and reuse the last breath pattern/voice
    /// instead of letting Dictionary.get_Item abort HScene.Update every frame.
    /// </summary>
    [HarmonyPatch(typeof(HVoiceCtrl), nameof(HVoiceCtrl.BreathProc))]
    internal static class VoiceTourBreathFallbackPatch
    {
        private static readonly HVoiceCtrl.BreathPtn?[] LastPatterns = new HVoiceCtrl.BreathPtn?[2];
        private static readonly HVoiceCtrl.BreathPtn?[] CandidatePatterns = new HVoiceCtrl.BreathPtn?[2];
        private static readonly HVoiceCtrl.VoiceListInfo?[] LastVoices = new HVoiceCtrl.VoiceListInfo?[2];
        private static readonly bool[] UseLastVoice = new bool[2];
        private static readonly int[] LastPersonality = { int.MinValue, int.MinValue };
        private static readonly BlockKey[] CurrentKeys = new BlockKey[2];
        private static readonly HashSet<BlockKey> MissingWithoutFallback = new HashSet<BlockKey>();
        private static readonly HashSet<BlockKey> LoggedFallbacks = new HashSet<BlockKey>();
        private static int _sceneId;

        [HarmonyPrefix]
        private static bool Prefix(AnimatorStateInfo __0, int __2, ref bool __result)
        {
            if (!IsMainIndex(__2))
                return true;

            int sceneId = GetSceneId();
            EnsureScene(sceneId);
            EnsurePersonality(__2);
            CandidatePatterns[__2] = null;
            UseLastVoice[__2] = false;
            BlockKey key = BuildKey(sceneId, __2, __0.fullPathHash);
            CurrentKeys[__2] = key;

            if (LastPatterns[__2] == null && MissingWithoutFallback.Contains(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            var patternGetter = AccessTools.PropertyGetter(
                typeof(Dictionary<int, HVoiceCtrl.BreathPtn>), "Item");
            var voiceGetter = AccessTools.PropertyGetter(
                typeof(Dictionary<int, HVoiceCtrl.VoiceListInfo>), "Item");
            var patternFallback = AccessTools.Method(
                typeof(VoiceTourBreathFallbackPatch), nameof(GetPatternOrLast));
            var voiceFallback = AccessTools.Method(
                typeof(VoiceTourBreathFallbackPatch), nameof(GetVoiceOrLast));

            int patternReplacements = 0;
            int voiceReplacements = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].Calls(patternGetter))
                {
                    codes[i].opcode = OpCodes.Ldarg_3;
                    codes[i].operand = null;
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, patternFallback));
                    patternReplacements++;
                }
                else if (codes[i].Calls(voiceGetter))
                {
                    codes[i].opcode = OpCodes.Ldarg_3;
                    codes[i].operand = null;
                    codes.Insert(++i, new CodeInstruction(OpCodes.Call, voiceFallback));
                    voiceReplacements++;
                }
            }

            if (patternReplacements != 1 || voiceReplacements != 2)
                throw new InvalidOperationException(
                    $"Unexpected BreathProc dictionary layout: pattern={patternReplacements}, voice={voiceReplacements}");
            return codes;
        }

        private static HVoiceCtrl.BreathPtn GetPatternOrLast(
            Dictionary<int, HVoiceCtrl.BreathPtn> patterns,
            int requestedState,
            int main)
        {
            if (patterns != null && patterns.TryGetValue(requestedState, out HVoiceCtrl.BreathPtn pattern)
                && pattern != null)
            {
                if (IsMainIndex(main))
                {
                    CandidatePatterns[main] = pattern;
                    UseLastVoice[main] = false;
                }
                return pattern;
            }

            if (IsMainIndex(main) && LastPatterns[main] != null)
            {
                CandidatePatterns[main] = LastPatterns[main];
                UseLastVoice[main] = true;
                LogFallbackOnce(main, requestedState);
                return LastPatterns[main]!;
            }

            throw new KeyNotFoundException($"No breath pattern for state {requestedState} and no previous breath.");
        }

        private static HVoiceCtrl.VoiceListInfo GetVoiceOrLast(
            Dictionary<int, HVoiceCtrl.VoiceListInfo> voices,
            int requestedVoice,
            int main)
        {
            if (IsMainIndex(main) && UseLastVoice[main] && LastVoices[main] != null)
            {
                UseLastVoice[main] = false;
                return LastVoices[main]!;
            }

            if (voices != null && voices.TryGetValue(requestedVoice, out HVoiceCtrl.VoiceListInfo voice)
                && voice != null)
            {
                if (IsMainIndex(main))
                {
                    LastVoices[main] = voice;
                    if (CandidatePatterns[main] != null)
                        LastPatterns[main] = CandidatePatterns[main];
                    UseLastVoice[main] = false;
                }
                return voice;
            }

            if (IsMainIndex(main) && LastVoices[main] != null)
            {
                UseLastVoice[main] = false;
                LogFallbackOnce(main, requestedVoice);
                return LastVoices[main]!;
            }

            throw new KeyNotFoundException($"No breath voice {requestedVoice} and no previous breath.");
        }

        [HarmonyFinalizer]
        private static Exception? Finalizer(Exception? __exception, int __2, ref bool __result)
        {
            if (!(__exception is KeyNotFoundException) || !IsMainIndex(__2))
                return __exception;

            BlockKey key = CurrentKeys[__2];
            MissingWithoutFallback.Add(key);
            if (LoggedFallbacks.Add(key))
            {
                HS2OrbitAndExciter.Log?.LogWarning(
                    $"Orbit: 呼吸音資料為空，女角{__2 + 1}沿用目前呼吸；anim={key.AnimationHash} state={key.State}");
                OrbitStateMachineLog.Event("voice", "breath_missing_keep_last",
                    "{\"main\":" + __2 + ",\"animHash\":" + key.AnimationHash +
                    ",\"state\":" + key.State + "}");
            }
            CandidatePatterns[__2] = null;
            UseLastVoice[__2] = false;
            __result = false;
            return null;
        }

        private static void LogFallbackOnce(int main, int requested)
        {
            BlockKey key = CurrentKeys[main];
            if (!LoggedFallbacks.Add(key))
                return;
            HS2OrbitAndExciter.Log?.LogWarning(
                $"Orbit: 呼吸音缺 key={requested}，女角{main + 1}沿用最後呼吸；VoiceTour 階段不變");
            OrbitStateMachineLog.Event("voice", "breath_reuse_last",
                "{\"main\":" + main + ",\"requested\":" + requested +
                ",\"animHash\":" + key.AnimationHash + ",\"state\":" + key.State + "}");
        }

        private static void EnsureScene(int sceneId)
        {
            if (_sceneId == sceneId)
                return;
            _sceneId = sceneId;
            Array.Clear(LastPatterns, 0, LastPatterns.Length);
            Array.Clear(CandidatePatterns, 0, CandidatePatterns.Length);
            Array.Clear(LastVoices, 0, LastVoices.Length);
            Array.Clear(UseLastVoice, 0, UseLastVoice.Length);
            LastPersonality[0] = int.MinValue;
            LastPersonality[1] = int.MinValue;
            MissingWithoutFallback.Clear();
            LoggedFallbacks.Clear();
        }

        private static void EnsurePersonality(int main)
        {
            int personality = -1;
            try
            {
                if (Singleton<HSceneManager>.IsInstance())
                {
                    ChaControl[]? females = Singleton<HSceneManager>.Instance.Hscene?.GetFemales();
                    if (females != null && main < females.Length && females[main] != null)
                        personality = females[main].fileParam2?.personality ?? -1;
                }
            }
            catch { /* keep safe default */ }

            if (LastPersonality[main] == personality)
                return;
            LastPersonality[main] = personality;
            LastPatterns[main] = null;
            CandidatePatterns[main] = null;
            LastVoices[main] = null;
            UseLastVoice[main] = false;
        }

        private static BlockKey BuildKey(int sceneId, int main, int animationHash)
        {
            int state = -999;
            try
            {
                if (Singleton<HSceneManager>.IsInstance())
                {
                    ChaFileDefine.State[] states = Singleton<HSceneManager>.Instance.FemaleState;
                    if (states != null && main < states.Length)
                        state = (int)states[main];
                }
            }
            catch { /* keep sentinel */ }
            return new BlockKey(sceneId, main, animationHash, state);
        }

        private static int GetSceneId()
        {
            try
            {
                if (Singleton<HSceneManager>.IsInstance())
                    return Singleton<HSceneManager>.Instance.Hscene?.GetInstanceID() ?? 0;
            }
            catch { /* keep safe default */ }
            return 0;
        }

        private static bool IsMainIndex(int main) => main >= 0 && main < 2;

        private readonly struct BlockKey : IEquatable<BlockKey>
        {
            internal BlockKey(int sceneId, int main, int animationHash, int state)
            {
                SceneId = sceneId;
                Main = main;
                AnimationHash = animationHash;
                State = state;
            }

            internal int SceneId { get; }
            internal int Main { get; }
            internal int AnimationHash { get; }
            internal int State { get; }

            public bool Equals(BlockKey other) =>
                SceneId == other.SceneId && Main == other.Main &&
                AnimationHash == other.AnimationHash && State == other.State;

            public override bool Equals(object? obj) => obj is BlockKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = SceneId;
                    hash = (hash * 397) ^ Main;
                    hash = (hash * 397) ^ AnimationHash;
                    return (hash * 397) ^ State;
                }
            }
        }
    }
}
