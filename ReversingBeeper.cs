using BepInEx;
using HarmonyLib;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using static Ship;

namespace ReversingBeeper
{
    [BepInPlugin("quakec.ReversingBeeper", "Reversing Beeper", "1.0.0")]
    [BepInProcess("valheim.exe")]
    public class ReversingBeeper : BaseUnityPlugin
    {
        private readonly Harmony _harmony = new Harmony("quakec.ReversingBeeper");

        private const string ResourceName = "ReversingBeeper.Resources.reverse_beep.wav";
        public static AudioClip BeeperClip;

        void Awake()
        {
            LoadWav();

            _harmony.PatchAll();
            Logger.LogInfo("Reversing Beeper Mod Loaded");
        }

        private static void LoadWav()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
                throw new Exception("Embedded WAV not found: " + ResourceName);

            byte[] data;

            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                data = ms.ToArray();
            }

            // parse header
            ushort channels = BitConverter.ToUInt16(data, 22);
            int sampleRate = BitConverter.ToInt32(data, 24);
            ushort bitDepth = BitConverter.ToUInt16(data, 34);

            if (bitDepth != 16)
                throw new Exception("Only 16-bit PCM WAV supported");

            // find "data" chunk
            int offset = 12;
            while (!(data[offset] == 'd' && data[offset + 1] == 'a' &&
                     data[offset + 2] == 't' && data[offset + 3] == 'a'))
            {
                int chunkSize = BitConverter.ToInt32(data, offset + 4);
                offset += 8 + chunkSize;
            }

            int dataSize = BitConverter.ToInt32(data, offset + 4);
            int sampleCount = dataSize / 2;

            float[] samples = new float[sampleCount];
            int start = offset + 8;

            for (int i = 0; i < sampleCount; i++)
            {
                short s = BitConverter.ToInt16(data, start + i * 2);
                samples[i] = s / 32768f;
            }

            BeeperClip = AudioClip.Create("ReverseBeeper", sampleCount / channels, channels, sampleRate, false);
            BeeperClip.SetData(samples, 0);
        }

        // ship audio source
        private static AudioSource GetOrAddBeeper(Ship ship)
        {
            Transform t = ship.transform.Find("ReverseBeeper");
            if (t != null)
                return t.GetComponent<AudioSource>();

            GameObject go = new GameObject("ReverseBeeper");
            go.transform.SetParent(ship.transform, false);

            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = BeeperClip;
            src.loop = true;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 8f;        
            src.maxDistance = 100f;       
            src.dopplerLevel = 0.2f;        
            src.reverbZoneMix = 0f;       
            src.spatialize = false;       
            src.playOnAwake = false;
            return src;
        }

        private static void RPC_PlayStop(long sender, long userID, uint id, bool play)
        {
            ZDOID zdoid = new ZDOID(userID, id);

            GameObject go = ZNetScene.instance.FindInstance(zdoid);
            if (go == null) return;

            ZNetView view = go.GetComponent<ZNetView>();
            if (view == null) return;

            Ship ship = view.GetComponent<Ship>();
            if (ship == null) return;

            AudioSource src = GetOrAddBeeper(ship);

            if (play && !src.isPlaying)
                src.Play();
            else if (!play && src.isPlaying)
                src.Stop();
        }

        // **************** patches ****************

        // RPC registration
        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        class ZNetSceneAwakePatch
        {
            static void Postfix()
            {
                ZRoutedRpc.instance.Register("QB_ReverseBeeper", new Action<long, long, uint, bool>(ReversingBeeper.RPC_PlayStop));
            }
        }

        // ship controls
        [HarmonyPatch(typeof(Ship), nameof(Ship.Forward))]
        public static class ForwardPatch
        {
            public static void Postfix(Ship __instance)
            {
                ZNetView nview = __instance.GetComponent<ZNetView>();
                ZDO zdo = nview.GetZDO();
                ZDOID zdoid = new ZDOID(zdo.m_uid.UserID, zdo.m_uid.ID);
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "QB_ReverseBeeper", zdoid.UserID, zdoid.ID, false);
            }
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.Stop))]
        public static class StopPatch
        {
            public static void Postfix(Ship __instance)
            {
                ZNetView nview = __instance.GetComponent<ZNetView>();
                ZDO zdo = nview.GetZDO();
                ZDOID zdoid = new ZDOID(zdo.m_uid.UserID, zdo.m_uid.ID);
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "QB_ReverseBeeper", zdoid.UserID, zdoid.ID, false);
            }
        }

        [HarmonyPatch(typeof(Ship), nameof(Ship.Backward))]
        public static class BackwardPatch
        {
            public static void Postfix(Ship __instance, ref Speed ___m_speed)
            {
                if (___m_speed == Speed.Back)
                {
                    ZNetView nview = __instance.GetComponent<ZNetView>();
                    ZDO zdo = nview.GetZDO();
                    ZDOID zdoid = new ZDOID(zdo.m_uid.UserID, zdo.m_uid.ID);
                    ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "QB_ReverseBeeper", zdoid.UserID, zdoid.ID, true);
                }
            }
        }
    }
}