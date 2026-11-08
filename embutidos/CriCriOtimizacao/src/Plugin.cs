using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace CriCriMods
{
    // CriCri Mods inside the game: optimization (frame rate, resolution, smooth movement, textures, anti-aliasing),
    // FPS counter, no ads, colorblind names in each player's color, and the "CriCri" tab in the game's Options where
    // the player changes all of it. Shipped inside the APK (hidden in the app's mod list).
    //
    // Android rule (the game there inlines thousands of methods): Harmony only where the analyzer shows a real
    // function (AdsManager.InitLevelPlay is "used-by-inline", OptionsMenuBehaviour.Start is a Unity message); the
    // rest runs from a MonoBehaviour Update, which always fires.
    [BepInPlugin(Id, "CriCri Mods - jogo", Versao)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "br.com.cricribr.otimizacao";
        public const string Versao = "0.4.1";
        internal static BepInEx.Logging.ManualLogSource Registro;

        public override void Load()
        {
            Registro = Log;
            ClassInjector.RegisterTypeInIl2Cpp<Otimizacao>();
            var objeto = new GameObject("CriCriOtimizacao");
            UnityEngine.Object.DontDestroyOnLoad(objeto);
            objeto.hideFlags = HideFlags.HideAndDontSave;
            objeto.AddComponent<Otimizacao>();

            var h = new Harmony(Id);
            Prender(h, typeof(AdsManager), "InitLevelPlay", prefixo: nameof(SemAnuncios));
            Prender(h, typeof(OptionsMenuBehaviour), "Start", posfixo: nameof(DepoisDasOpcoes));
            Log.LogInfo("CriCri Mods jogo " + Versao + ": " + Opcoes.Atual.Descrever());
        }

        void Prender(Harmony h, Type tipo, string metodo, string prefixo = null, string posfixo = null)
        {
            string nome = tipo.Name + "." + metodo;
            try
            {
                var alvo = AccessTools.Method(tipo, metodo);
                if (alvo == null) { Log.LogWarning("[jogo] nao achei " + nome); return; }
                h.Patch(alvo,
                    prefix: prefixo != null ? new HarmonyMethod(typeof(Plugin), prefixo) : null,
                    postfix: posfixo != null ? new HarmonyMethod(typeof(Plugin), posfixo) : null);
                Log.LogInfo("[jogo] gancho em " + nome);
            }
            catch (Exception e)
            {
                Log.LogWarning("[jogo] gancho em " + nome + " falhou: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static bool _avisouAnuncio;

        // The game's own ad SDK (ironSource/LevelPlay) never starts inside CriCri Mods (the app has no ads, and
        // Innersloth's mod policy forbids monetization inside Among Us anyway).
        static bool SemAnuncios()
        {
            if (!_avisouAnuncio) { _avisouAnuncio = true; Registro.LogInfo("[jogo] anuncios do jogo desligados"); }
            return false;
        }

        static void DepoisDasOpcoes(OptionsMenuBehaviour __instance)
        {
            try { AbaCriCri.PeloStart(__instance); }
            catch (Exception e) { Registro.LogWarning("[aba] " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
