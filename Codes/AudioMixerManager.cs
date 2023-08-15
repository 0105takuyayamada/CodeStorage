using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Audio;
using UnityEngine.LowLevel;

namespace Audio
{
    public enum AudioMixerGroupEnum
    {
        Master = 0,
        BGM = 1,
        SE = 2,
        UI = 3,
        Voice = 4,
    }
    public enum AudioMixerParameterEnum
    {
        MasterVolume = 0,
        BgmVolume = 1,
        SeVolume = 2,
        UiVolume = 3,
        VoiceVolume = 4,
    }
    public enum AudioMixerSnapshotEnum
    {
        Snapshot = 0,
    }
    public static class AudioMixerManager
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static async void Init()
        {
            var playerLoop = PlayerLoop.GetCurrentPlayerLoop();
            PlayerLoopHelper.Initialize(ref playerLoop);
            AudioMixer = await Addressables.LoadAssetAsync<AudioMixer>(nameof(AudioMixer));

            foreach (AudioMixerGroupEnum groupEnum in Enum.GetValues(typeof(AudioMixerGroupEnum)))
            {
                var groupName = groupEnum.ToString();
                var groupArray = AudioMixer.FindMatchingGroups(groupName);
                foreach (var group in groupArray)
                {
                    if (group.name != groupName) continue;
                    GroupDict.Add(groupEnum, groupArray[0]);
                    break;
                }
            }

            foreach (AudioMixerSnapshotEnum snapshotEnum in Enum.GetValues(typeof(AudioMixerSnapshotEnum)))
            {
                var snapshot = AudioMixer.FindSnapshot(snapshotEnum.ToString());
                if (snapshot != null) SnapshotDict.Add(snapshotEnum, snapshot);
            }
        }
        private static AudioMixer AudioMixer = null;
        private static Dictionary<AudioMixerGroupEnum, AudioMixerGroup> GroupDict = new();
        private static Dictionary<AudioMixerSnapshotEnum, AudioMixerSnapshot> SnapshotDict = new();

        public static AudioMixerGroup GetMixerGroup(AudioMixerGroupEnum groupEnum)
        {
            if (GroupDict.TryGetValue(groupEnum, out var mixerGroup)) return mixerGroup;
            Debug.LogError($"{nameof(AudioMixerGroup)} named {groupEnum} does not exist in {nameof(AudioMixer)}.");
            return null;
        }
        public static AudioMixerGroup[] GetMixerGroups(AudioMixerGroupEnum groupEnum)
        {
            var groupArray = AudioMixer.FindMatchingGroups(groupEnum.ToString());
            if (groupArray.Length > 0) return groupArray;
            Debug.LogError($"{nameof(AudioMixerGroup)} named {groupEnum} does not exist in {nameof(AudioMixer)}.");
            return null;
        }

        #region Snapshot
        public static AudioMixerSnapshot GetSnapshot(AudioMixerSnapshotEnum snapshotEnum)
        {
            if (SnapshotDict.TryGetValue(snapshotEnum, out var snapshot)) return snapshot;
            Debug.LogError($"{nameof(AudioMixerSnapshot)} named {snapshotEnum} does not exist in {nameof(AudioMixer)}.");
            return null;
        }

        public static void TransitionToSnapshot(AudioMixerSnapshotEnum snapshotEnum, float weight, float timeToreach)
            => AudioMixer.TransitionToSnapshots(new AudioMixerSnapshot[] { SnapshotDict[snapshotEnum] }, new float[] { weight }, timeToreach);
        public static void TransitionToSnapshots(AudioMixerSnapshotEnum[] snapshotEnums, float[] weights, float timeToreach)
        {
            var snapshots = new AudioMixerSnapshot[snapshotEnums.Length];
            for (int i = 0; i < snapshotEnums.Length; i++) snapshots[i] = SnapshotDict[snapshotEnums[i]];
            AudioMixer.TransitionToSnapshots(snapshots, weights, timeToreach);
        }
        public static void TransitionToSnapshots(float weight, float timeToreach, params AudioMixerSnapshotEnum[] snapshotEnums)
        {
            var snapshots = new AudioMixerSnapshot[snapshotEnums.Length];
            for (int i = 0; i < snapshotEnums.Length; i++) snapshots[i] = SnapshotDict[snapshotEnums[i]];
            AudioMixer.TransitionToSnapshots(snapshots, Enumerable.Range(0, snapshotEnums.Length).Select(_ => weight).ToArray(), timeToreach);
        }
        public static void TransitionBeTweenSnapshot(AudioMixerSnapshotEnum currentSnapshot, AudioMixerSnapshotEnum nextSnapshot, float timeToreach)
        {
            AudioMixer.TransitionToSnapshots(
                new AudioMixerSnapshot[] { SnapshotDict[currentSnapshot], SnapshotDict[nextSnapshot] },
                new float[] {0f, 1f}, timeToreach);
        }
        #endregion

        #region Parameter
        public static void SetParameter(AudioMixerParameterEnum parameter, float value)
        {
            if (AudioMixer.SetFloat(parameter.ToString(), value)) return;
            Debug.LogError($"AudioMixer has no {parameter} parameter");
        }
        public static float GetParameter(AudioMixerParameterEnum parameter)
        {
            if (AudioMixer.GetFloat(parameter.ToString(), out var value)) return value;
            Debug.LogError($"AudioMixer has no {parameter} parameter");
            return 0f;
        }

        public static void SetVolume(AudioMixerParameterEnum parameter, float volume)
        {
            if (AudioMixer.SetFloat(parameter.ToString(), VolumeToDecibel(volume))) return;
            Debug.LogError($"AudioMixer has no {parameter} parameter");
        }
        public static float GetVolume(AudioMixerParameterEnum parameter)
        {
            if (AudioMixer.GetFloat(parameter.ToString(), out var value)) return DecibelToVolume(value);
            Debug.LogError($"AudioMixer has no {parameter} parameter");
            return 0f;
        }

        private static float VolumeToDecibel(float volume) => Mathf.Clamp(20f * Mathf.Log10(Mathf.Clamp(volume, 0f, 1f)), -80f, 0f);
        private static float DecibelToVolume(float decibel) => Mathf.Clamp(Mathf.Pow(10f, Mathf.Clamp(decibel, -80f, 0f) * 0.05f), 0f, 1f);
        #endregion
    }
}
