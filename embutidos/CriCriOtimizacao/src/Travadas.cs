using System;
using UnityEngine;

namespace CriCriMods
{
    // The stutter meter (the CriCri, 08/11: "quando eu ando ele da umas travadas"). Before fixing, find WHAT stutters:
    // every 10 s where people walk (lobby, Freeplay, a round) one log line with the frames over 33 ms and 50 ms, the
    // worst frame, and how many garbage collections ran in that time, the game's (IL2CPP) and the mods' (.NET). A
    // spike with a GC in the same 10 s points at memory; spikes with no GC and a low average point at the GPU
    // (resolution) or at work done in the frame. The first 3 s of a scene are loading and are left out.
    // No allocation per frame.
    internal static class Travadas
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        static string _cena = "";
        static float _cenaDesde, _tempo, _pior;
        static int _quadros, _acima33, _acima50;
        static int _gcJogo = -1, _gcMods = -1;
        static bool _logouGc;

        internal static void Quadro(string cena)
        {
            float agora = Time.unscaledTime;
            if (!ReferenceEquals(cena, _cena) && cena != _cena) { _cena = cena; _cenaDesde = agora; Zerar(); }
            bool andando = _cena == "OnlineGame" || _cena == "Tutorial";
            if (!andando || agora - _cenaDesde < 3f) return;

            float ms = Time.unscaledDeltaTime * 1000f;
            _quadros++;
            _tempo += ms;
            if (ms > 33.4f) _acima33++;
            if (ms > 50f) _acima50++;
            if (ms > _pior) _pior = ms;
            if (_tempo >= 10000f) Relatar();
        }

        static void Relatar()
        {
            int gcJogo = -1, gcMods = GC.CollectionCount(0);
            try { gcJogo = Il2CppSystem.GC.CollectionCount(0); } catch { }
            string gc = _gcJogo < 0 ? "" : ", GC do jogo +" + (gcJogo - _gcJogo) + ", GC dos mods +" + (gcMods - _gcMods);
            bool rodada = false;
            try { rodada = ShipStatus.Instance != null; } catch { }
            Log.LogInfo("[travadas] 10 s " + (rodada ? "na rodada" : "em " + _cena) + ": media " + (_quadros * 1000f / _tempo).ToString("0")
                + " fps, " + _acima33 + " quadros > 33 ms, " + _acima50 + " > 50 ms, pior " + _pior.ToString("0") + " ms" + gc
                + " (tela " + Screen.width + "x" + Screen.height + ")");
            if (!_logouGc)
            {
                _logouGc = true;
                try
                {
                    Log.LogInfo("[travadas] GC do jogo: incremental " + UnityEngine.Scripting.GarbageCollector.isIncremental
                        + ", modo " + UnityEngine.Scripting.GarbageCollector.GCMode
                        + ", fatia " + UnityEngine.Scripting.GarbageCollector.incrementalTimeSliceNanoseconds / 1000 + " us");
                }
                catch (Exception e) { Log.LogInfo("[travadas] GC do jogo: " + e.GetType().Name); }
            }
            _gcJogo = gcJogo;
            _gcMods = gcMods;
            Zerar();
        }

        static void Zerar()
        {
            _quadros = _acima33 = _acima50 = 0;
            _tempo = _pior = 0f;
        }
    }
}
