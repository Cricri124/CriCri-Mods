using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods
{
    // "Modo flash": cut the waiting that is not loading, without touching the game's logic.
    //
    // Measured on the CriCri's Redmi 14C (08/11, 31 s from the icon to the main menu):
    // - the opening screen had the main menu loaded at 39.9 s and only showed it at 44.6 s. SplashManager.Update
    //   (disassembled) only checks doneLoadingRefdata && !startedSceneLoad && Time.time - startTime >
    //   minimumSecondsBeforeSceneChange before SceneChanger.AllowFinishLoadingScene(): the minimum is a fixed wait,
    //   zeroing it keeps every other condition (data loaded, scene loaded).
    // - joining a room has no fixed wait (CoJoinOnlineGameFromCode: EOS login, matchmaker HTTP, UDP connect, then
    //   the scene and the map load). The loading part is Unity's async loading, which by default only gets a few ms
    //   per frame: where nobody walks (menus), in the first seconds of the lobby/Freeplay and while the map loads at
    //   the start of a round, it gets 50 ms per frame (ThreadPriority.High) and 8 ms of texture upload. Walking (lobby,
    //   Freeplay, a round) the game's own values come back, so a cosmetic loading never steals frames.
    internal static class Flash
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        static bool _aberturaResolvida;
        static float _proxAbertura, _proxCarregamento;
        static bool _guardouDoJogo;
        static ThreadPriority _prioridadeDoJogo;
        static int _fatiaDoJogo;
        static int _acelerando = -1;   // -1 unknown, 0 game's values, 1 accelerated

        // Every frame from Otimizacao.Update (each part runs on its own clock).
        internal static void Quadro()
        {
            float agora = Time.unscaledTime;
            if (!_aberturaResolvida && agora >= _proxAbertura)
            {
                _proxAbertura = agora + 0.1f;
                Abertura();
            }
            if (agora >= _proxCarregamento)
            {
                _proxCarregamento = agora + 0.25f;
                Carregamento();
            }
        }

        static void Abertura()
        {
            string cena = SceneManager.GetActiveScene().name;
            // Past the boot scenes: the opening is over (with or without us).
            if (!string.IsNullOrEmpty(cena) && cena != "EarlyInitialization" && cena != "SplashIntro")
            {
                _aberturaResolvida = true;
                return;
            }
            if (cena != "SplashIntro" || !Opcoes.Atual.FlashEfetivo) return;
            var abertura = UnityEngine.Object.FindObjectOfType<SplashManager>();
            if (abertura == null) return;
            float antes = abertura.minimumSecondsBeforeSceneChange;
            abertura.minimumSecondsBeforeSceneChange = 0f;
            _aberturaResolvida = true;
            Log.LogInfo("[flash] abertura: espera minima " + antes.ToString("0.0") + " s -> 0 (carregou: "
                + abertura.doneLoadingRefdata + ", " + Time.realtimeSinceStartup.ToString("0.0") + " s de processo)");
        }

        static void Carregamento()
        {
            if (!_guardouDoJogo)
            {
                _prioridadeDoJogo = Application.backgroundLoadingPriority;
                _fatiaDoJogo = QualitySettings.asyncUploadTimeSlice;
                _guardouDoJogo = true;
                Log.LogInfo("[flash] carregamento do jogo: prioridade " + _prioridadeDoJogo + ", upload " + _fatiaDoJogo + " ms por quadro");
            }
            // Phone test 08/11 04:00: stutters walking in the lobby. There the fast loading was on (no ShipStatus), and
            // every player who joins loads their cosmetics, with up to 50 ms of a frame each. So it is on only where
            // nobody walks (menus), in the first seconds of a scene (the lobby or Freeplay arriving) and while the map
            // loads at the start of a round (game started, ship not there yet).
            // ShipStatus.Instance and AmongUsClient.Instance are plain static fields (reading them creates nothing,
            // unlike HudManager.Instance).
            string cena = SceneManager.GetActiveScene().name;
            if (cena != _cenaVista) { _cenaVista = cena; _cenaDesde = Time.unscaledTime; }
            bool emRodada = ShipStatus.Instance != null;
            bool andando = cena == "OnlineGame" || cena == "Tutorial";
            bool chegando = Time.unscaledTime - _cenaDesde < 6f;
            var cliente = AmongUsClient.Instance;
            bool mapaCarregando = andando && !emRodada && cliente != null && cliente.GameState.ToString() == "Started";
            int acelerar = Opcoes.Atual.FlashEfetivo && !emRodada && (!andando || chegando || mapaCarregando) ? 1 : 0;
            if (acelerar == _acelerando) return;
            _acelerando = acelerar;
            if (acelerar == 1)
            {
                Application.backgroundLoadingPriority = ThreadPriority.High;
                QualitySettings.asyncUploadTimeSlice = Math.Max(_fatiaDoJogo, 8);
                Log.LogInfo("[flash] carregamento rapido (" + (mapaCarregando ? "mapa carregando" : chegando && andando ? "chegando em " + cena : "menu") + "): prioridade High, upload " + QualitySettings.asyncUploadTimeSlice + " ms");
            }
            else
            {
                Application.backgroundLoadingPriority = _prioridadeDoJogo;
                QualitySettings.asyncUploadTimeSlice = _fatiaDoJogo;
                Log.LogInfo("[flash] carregamento do jogo de volta (" + (!Opcoes.Atual.FlashEfetivo ? "modo flash desligado" : emRodada ? "rodada na tela" : "andando em " + cena) + ")");
            }
        }
        static string _cenaVista = "";
        static float _cenaDesde;
    }
}
