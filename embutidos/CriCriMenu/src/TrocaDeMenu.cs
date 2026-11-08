using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods.Menu
{
    // Runs every frame from a DontDestroyOnLoad object (Unity messages always fire on Android, unlike Harmony hooks on
    // inlined methods). Watches the MainMenu scene, finds each new MainMenuManager (compared by Pointer), waits for
    // the game to lay its menu out, builds the chosen style once per menu instance, and puts the game's own menu back
    // when the style changes to "normal", when the scene leaves, or when anything of ours fails.
    public sealed class TrocaDeMenu : MonoBehaviour
    {
        public TrocaDeMenu(IntPtr ptr) : base(ptr) { }

        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        // set by the "Menu clássico" button (it runs inside the game's click handling: the rebuild waits a frame)
        internal static bool PedidoDeReaplicar;

        const float ESPERA = 0.8f;       // the game positions its buttons over the first frames after Start
        const int ERROS_MAX = 5;

        bool _noMenu;
        float _proxCena, _proxBusca;
        MainMenuManager _menu;
        IntPtr _menuPtr;
        float _vistoEm;
        Montagem _montagem;
        IntPtr _falhouPtr;
        string _falhouEstilo;
        int _erros;
        bool _avisouEspera;
        readonly HashSet<string> _avisos = new HashSet<string>();

        void Update()
        {
            float agora = Time.unscaledTime;
            try { Passo(agora); }
            catch (Exception e)
            {
                Erro("passo", e);
                Desfazer("erro");
            }
        }

        // After every Update of the frame (the game's button code included): the clones' own art stays hidden.
        void LateUpdate()
        {
            if (_montagem == null) return;
            try { _montagem.DepoisDoQuadro(); }
            catch (Exception e) { Erro("depois do quadro", e); }
        }

        void Passo(float agora)
        {
            bool mudou = Escolha.Conferir(agora);
            if (PedidoDeReaplicar) { PedidoDeReaplicar = false; mudou = true; }

            if (agora >= _proxCena)
            {
                _proxCena = agora + 0.25f;
                bool antes = _noMenu;
                _noMenu = SceneManager.GetActiveScene().name == "MainMenu";
                if (antes && !_noMenu) Esquecer();
            }
            if (!_noMenu) return;

            // the menu instance (a new one after every return to the menu)
            if (!Vivo(_menu))
            {
                if (_menu != null || _montagem != null) Esquecer();
                if (agora < _proxBusca) return;
                _proxBusca = agora + 0.5f;
                var m = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
                if (m == null) return;
                _menu = m;
                _menuPtr = m.Pointer;
                _vistoEm = agora;
                _erros = 0;
                _avisouEspera = false;
                Log.LogInfo("[menu] menu principal na tela (estilo " + Escolha.Atual + ")");
                return;
            }

            string estilo = Escolha.Atual;
            if (mudou)
            {
                if (_falhouEstilo != estilo) { _falhouPtr = IntPtr.Zero; _falhouEstilo = null; }
                if (_montagem != null && _montagem.Estilo != estilo) Desfazer("estilo mudou para " + estilo);
            }

            if (_montagem == null)
            {
                if (estilo == Escolha.Normal) return;
                if (_falhouPtr == _menuPtr && _falhouEstilo == estilo) return;
                if (agora - _vistoEm < ESPERA) return;
                if (!Pronto(_menu))
                {
                    if (!_avisouEspera && agora - _vistoEm > 15f) { _avisouEspera = true; Log.LogWarning("[menu] o botao Jogar do jogo nao apareceu em 15 s; fica o menu do jogo"); }
                    return;
                }
                _montagem = Montagem.Criar(_menu, estilo);
                _erros = 0;
                if (_montagem == null)
                {
                    _falhouPtr = _menuPtr;
                    _falhouEstilo = estilo;
                    Log.LogWarning("[menu] estilo " + estilo + " nao montou; fica o menu do jogo ate o estilo mudar ou o menu abrir de novo");
                }
                return;
            }

            try { _montagem.Tique(agora); }
            catch (Exception e)
            {
                _erros++;
                Erro("tique", e);
                if (_erros >= ERROS_MAX)
                {
                    _falhouPtr = _menuPtr;
                    _falhouEstilo = estilo;
                    Desfazer(_erros + " erros seguidos");
                }
            }
        }

        static bool Vivo(MainMenuManager m)
        {
            try { return m != null && m.gameObject != null; } catch { return false; }
        }

        static bool Pronto(MainMenuManager m)
        {
            try { return m.playButton != null && m.playButton.gameObject.activeInHierarchy; } catch { return false; }
        }

        // The scene left or the menu died: Unity destroys our objects with it; only the bookkeeping is reset.
        void Esquecer()
        {
            if (_montagem != null)
            {
                try { _montagem.Desmontar(); } catch { }
                _montagem = null;
            }
            _menu = null;
            _menuPtr = IntPtr.Zero;
        }

        // The game's own menu back, on this instance.
        void Desfazer(string motivo)
        {
            if (_montagem == null) return;
            try { _montagem.Desmontar(); } catch { }
            _montagem = null;
            Log.LogInfo("[menu] menu do jogo de volta (" + motivo + ")");
        }

        void Erro(string onde, Exception e)
        {
            if (_avisos.Add(onde + e.GetType().Name + e.Message))
                Log.LogWarning("[menu] " + onde + ": " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
        }
    }
}
