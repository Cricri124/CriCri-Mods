#if CRICRI_PC
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods.Servidor
{
    // PC only (built with -p:DefineConstants=CRICRI_PC). A second plugin in the same DLL so that Plugin.cs, shared with the phone,
    // stays as it is: it loads after Plugin, gives SalaApi the key kept in PcCofre, and puts the account screen on the main menu.
    [BepInPlugin(Id, "CriCri Mods - conta CRICRI BR no PC", Plugin.Versao)]
    [BepInDependency(Plugin.Id)]
    public sealed class PcPlugin : BasePlugin
    {
        public const string Id = "br.com.cricribr.servidor.pc";

        public override void Load()
        {
            string chave = PcCofre.Ler();
            if (chave != null) SalaApi.TrocarChave(chave);
            ClassInjector.RegisterTypeInIl2Cpp<PcTelaConta>();
            var objeto = new GameObject("CriCriServidorPc");
            UnityEngine.Object.DontDestroyOnLoad(objeto);
            objeto.hideFlags = HideFlags.HideAndDontSave;
            objeto.AddComponent<PcTelaConta>();
            Log.LogInfo("conta CRICRI BR no PC: " + (chave != null ? "chave guardada encontrada" : "sem chave guardada"));
        }
    }

    // A "CONTA BR" button on the main menu (the phone has the app; the PC needs a door) opens a small screen with COLAR or APAGAR.
    // The door is the clone the CriCri Replay uses for "Replays" (replay\src\BotaoMenu.cs); the screen is a private copy of the
    // game's error pop-up, made while inactive and without DisconnectPopup (a singleton would destroy a second one, and the real
    // pop-up must stay untouched for real disconnections). IMGUI does not exist in this build of the game ("Method unstripping failed").
    // The key is never drawn: only "guardada" or not, because this PC is on stream. Nothing here reads HudManager (the ghost HUD rule).
    internal sealed class PcTelaConta : MonoBehaviour
    {
        public PcTelaConta(IntPtr ptr) : base(ptr) { }

        const string NomeDoBotao = "CriCriBr_Conta";
        const string Rotulo = "CONTA BR";
        const string BotaoDoReplay = "CriCriReplay_Botao";
        const string IdDoReplay = "com.cricri.replay";
        const float AfastamentoDosBotoes = 1.25f;   // the distance the CriCri QoL's two-button pop-up uses on the PC

        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        MainMenuManager _menu;
        GameObject _botao, _popup, _botaoAcao;
        GameObject[] _telas;
        TextMeshPro[] _textos;
        TextMeshPro _textoPopup;
        float _prox, _desde = -1f, _reforcarAte;
        bool _falhou;
        string _aviso = "";

        void Update()
        {
            float agora = Time.unscaledTime;
            if (_botao != null)
            {
                Visibilidade();
                if (agora < _reforcarAte && _textos != null)
                    foreach (var t in _textos) try { if (t != null && t.text != Rotulo) t.text = Rotulo; } catch { }
                return;
            }
            if (agora < _prox) return;
            _prox = agora + 0.25f;
            if (SceneManager.GetActiveScene().name != "MainMenu")
            {
                FecharPopup();
                _menu = null;
                _desde = -1f;
                _falhou = false;
                return;
            }
            if (_falhou) return;
            try
            {
                if (_menu == null) _menu = FindObjectOfType<MainMenuManager>();
                if (_menu == null) return;
                if (_desde < 0f) _desde = agora;
                var replay = _menu.transform.Find(BotaoDoReplay);
                // the Replay makes its button in MainMenuManager.Start: give it a moment, then take its place
                if (replay == null && ReplayCarregado() && agora - _desde < 3f) return;
                Criar(_menu, replay != null && replay.gameObject.activeInHierarchy ? replay.gameObject : null);
            }
            catch (Exception e)
            {
                _falhou = true;
                Log.LogWarning("[conta] botao do menu: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static bool ReplayCarregado()
        {
            try { return IL2CPPChainloader.Instance.Plugins.ContainsKey(IdDoReplay); } catch { return false; }
        }

        // ---------------- the door on the main menu ----------------

        void Criar(MainMenuManager m, GameObject replay)
        {
            var jogar = m.playButton;
            if (jogar == null) { _falhou = true; Log.LogWarning("[conta] menu sem botao Jogar"); return; }

            PassiveButton modelo = null;
            float jx = jogar.transform.position.x, jy = jogar.transform.position.y, larguraJogar = Largura(jogar.gameObject);
            try
            {
                if (m.mainButtons != null)
                    for (int i = 0; i < m.mainButtons.Count; i++)
                    {
                        var b = m.mainButtons[i];
                        if (b == null || b == jogar || !b.gameObject.activeInHierarchy) continue;
                        if (Mathf.Abs(b.transform.position.x - jx) > 0.6f || b.transform.position.y >= jy) continue;
                        if (larguraJogar > 0.01f && Largura(b.gameObject) < larguraJogar * 0.45f) continue;
                        if (modelo == null || b.transform.position.y > modelo.transform.position.y) modelo = b;
                    }
            }
            catch { }
            if (modelo == null) modelo = jogar;

            var clone = Instantiate(modelo.gameObject, m.transform);
            clone.name = NomeDoBotao;
            foreach (var ap in clone.GetComponentsInChildren<AspectPosition>(true)) try { DestroyImmediate(ap); } catch { }
            foreach (var tr in clone.GetComponentsInChildren<TextTranslatorTMP>(true)) try { DestroyImmediate(tr); } catch { }

            var ls = modelo.transform.lossyScale;
            var ps = m.transform.lossyScale;
            clone.transform.localScale = new Vector3(
                Mathf.Abs(ps.x) > 0.0001f ? ls.x / ps.x : ls.x,
                Mathf.Abs(ps.y) > 0.0001f ? ls.y / ps.y : ls.y,
                Mathf.Abs(ps.z) > 0.0001f ? ls.z / ps.z : ls.z);

            float largura = Largura(modelo.gameObject), altura = Altura(modelo.gameObject);
            if (largura < 0.5f) largura = 2.98f;
            if (altura < 0.2f) altura = 0.65f;
            string onde;
            // the Replay button may now live in the left column; below it there is no free space
            if (replay != null && replay.transform.position.x > jx + 1f && Caixa(replay, out Bounds br))
            {
                clone.transform.position = new Vector3(replay.transform.position.x, br.min.y - altura * 0.25f - altura / 2f, replay.transform.position.z);
                onde = "embaixo do Replays";
            }
            else
            {
                // the Replay's place: right of the panel, top aligned a little above Play
                float x = jx + Largura(jogar.gameObject) / 2f + 0.85f + largura / 2f;
                float y = jy + Altura(jogar.gameObject) / 2f + 0.53f - altura / 2f;
                clone.transform.position = new Vector3(x, y, modelo.transform.position.z);
                onde = "no lugar do Replays";
            }

            // outside the panel: no mask on the sprites, no stencil on the text, and the model's small icon off
            foreach (var sr in clone.GetComponentsInChildren<SpriteRenderer>(true)) try { sr.maskInteraction = SpriteMaskInteraction.None; } catch { }
            EsconderIcone(clone);
            _textos = clone.GetComponentsInChildren<TextMeshPro>(true);
            foreach (var t in _textos)
            {
                try
                {
                    var mat = t.fontMaterial;
                    if (mat != null && mat.HasProperty("_StencilComp")) mat.SetFloat("_StencilComp", 8f);
                    if (mat != null && mat.HasProperty("_Stencil")) mat.SetFloat("_Stencil", 0f);
                    t.text = Rotulo;
                }
                catch { }
            }
            _reforcarAte = Time.unscaledTime + 3f;

            var passivo = clone.GetComponent<PassiveButton>() ?? clone.GetComponentInChildren<PassiveButton>(true);
            if (passivo == null) { Destroy(clone); _falhou = true; Log.LogWarning("[conta] clone sem PassiveButton"); return; }
            try { passivo.ClickMask = null; } catch { }
            passivo.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            passivo.OnClick.AddListener((Action)AbrirPopup);

            clone.SetActive(true);
            _botao = clone;
            _telas = new[] { m.gameModeButtons, m.onlineButtons, m.accountButtons, m.enterCodeButtons };
            Log.LogInfo("[conta] botao CONTA BR " + onde + " em (" + clone.transform.position.x.ToString("0.00") + ", "
                + clone.transform.position.y.ToString("0.00") + "), modelo " + modelo.name);
        }

        void Visibilidade()
        {
            if (_telas == null) return;
            bool ocupado = false;
            foreach (var t in _telas) try { if (t != null && t.activeInHierarchy) { ocupado = true; break; } } catch { }
            try { if (_botao.activeSelf == ocupado) _botao.SetActive(!ocupado); } catch { }
        }

        static void EsconderIcone(GameObject clone)
        {
            try
            {
                SpriteRenderer fundo = null; float area = 0f;
                var todos = clone.GetComponentsInChildren<SpriteRenderer>(true);
                foreach (var sr in todos) { float a = sr.bounds.size.x * sr.bounds.size.y; if (a > area) { area = a; fundo = sr; } }
                if (fundo == null) return;
                float cx = fundo.bounds.center.x;
                foreach (var sr in todos)
                {
                    if (sr == fundo) continue;
                    float a = sr.bounds.size.x * sr.bounds.size.y;
                    if (a < area * 0.35f && sr.bounds.center.x < cx - fundo.bounds.size.x * 0.15f) sr.enabled = false;
                }
            }
            catch { }
        }

        static bool Caixa(GameObject go, out Bounds caixa)
        {
            caixa = default;
            bool achou = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                try
                {
                    if (r == null || !r.enabled) continue;
                    if (!achou) { caixa = r.bounds; achou = true; } else caixa.Encapsulate(r.bounds);
                }
                catch { }
            }
            return achou;
        }

        static float Largura(GameObject b)
        {
            try { var r = b.GetComponentInChildren<SpriteRenderer>(); return r != null ? r.bounds.size.x : 0f; } catch { return 0f; }
        }

        static float Altura(GameObject b)
        {
            try { var r = b.GetComponentInChildren<SpriteRenderer>(); return r != null ? r.bounds.size.y : 0.5f; } catch { return 0.5f; }
        }

        // ---------------- the screen ----------------

        void AbrirPopup()
        {
            try
            {
                if (_popup == null && !MontarPopup()) { Log.LogWarning("[conta] sem o pop-up do jogo pra copiar"); return; }
                _aviso = "";
                _popup.SetActive(true);
                Redesenhar();
                Log.LogInfo("[conta] tela aberta, visivel=" + _popup.activeInHierarchy);
            }
            catch (Exception e) { Log.LogWarning("[conta] abrir: " + e.GetType().Name + ": " + e.Message); }
        }

        void FecharPopup()
        {
            try { if (_popup != null) _popup.SetActive(false); } catch { }
        }

        bool MontarPopup()
        {
            // InstanceExists first: Instance would CREATE the object when it is not there.
            if (!DestroyableSingleton<DisconnectPopup>.InstanceExists) return false;
            var modelo = DestroyableSingleton<DisconnectPopup>.Instance;
            bool estava = modelo.gameObject.activeSelf;
            if (estava) modelo.gameObject.SetActive(false);
            var copia = Instantiate(modelo.gameObject, modelo.transform.parent);
            if (estava) modelo.gameObject.SetActive(true);
            copia.name = "CriCriBrContaPopup";

            var original = copia.GetComponent<DisconnectPopup>();
            var abertura = copia.GetComponent<TransitionOpen>();
            // the game's opening animation scales it up to targetSize; without the animation the size has to be set here
            float tamanho = abertura != null && abertura.targetSize > 0f ? abertura.targetSize : 1f;
            copia.transform.localScale = Vector3.one * tamanho;
            _textoPopup = original != null ? original._textArea : null;
            PassiveButton xis = null;
            if (original != null && original.BackButton != null) xis = original.BackButton.GetComponent<PassiveButton>();
            if (xis != null) xis.gameObject.SetActive(false);

            PassiveButton ok = null;
            foreach (var b in copia.GetComponentsInChildren<PassiveButton>(true))
                if (b != null && b.gameObject.name == "OkayButton") { ok = b; break; }
            if (_textoPopup == null || ok == null)
            {
                Log.LogWarning("[conta] o pop-up do jogo nao tem as pecas esperadas");
                Destroy(copia);
                return false;
            }

            Tirar(original);
            Tirar(copia.GetComponent<ControllerNavMenu>());
            Tirar(abertura);
            foreach (var t in _textoPopup.GetComponents<TextTranslatorTMP>()) Tirar(t);

            var fechar = Instantiate(ok.gameObject, ok.transform.parent);
            fechar.name = "CriCriBrContaFechar";
            _botaoAcao = ok.gameObject;
            _botaoAcao.name = "CriCriBrContaAcao";
            Ligar(_botaoAcao, -AfastamentoDosBotoes, Acao);
            Ligar(fechar, AfastamentoDosBotoes, FecharPopup);
            Rotular(fechar, "FECHAR");
            _popup = copia;
            return true;
        }

        static void Tirar(UnityEngine.Object componente)
        {
            if (componente != null) DestroyImmediate(componente);
        }

        static void Ligar(GameObject botao, float x, Action acao)
        {
            foreach (var c in botao.GetComponents<CloseButtonConsoleBehaviour>()) Tirar(c);
            foreach (var t in botao.GetComponentsInChildren<TextTranslatorTMP>(true)) Tirar(t);
            var p = botao.transform.localPosition;
            botao.transform.localPosition = new Vector3(x, p.y, p.z);
            var pb = botao.GetComponent<PassiveButton>();
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            pb.OnClick.AddListener(acao);
        }

        static void Rotular(GameObject botao, string texto)
        {
            foreach (var t in botao.GetComponentsInChildren<TextMeshPro>(true)) t.text = texto;
        }

        void Redesenhar()
        {
            bool tem = SalaApi.TemChave;
            Rotular(_botaoAcao, tem ? "APAGAR" : "COLAR");
            string corpo = tem
                ? "A chave da sua conta está guardada neste PC, só para este usuário do Windows."
                : "Copie a chave da sua conta em cricribr.com.br (Minha conta) e clique em COLAR.";
            _textoPopup.text = "Conta do CRICRI BR\n" + (string.IsNullOrEmpty(_aviso) ? corpo : _aviso);
        }

        // COLAR with no key, APAGAR with one, like the app's button. To replace a key: APAGAR, then COLAR.
        void Acao()
        {
            try
            {
                if (SalaApi.TemChave) ApagarChave();
                else ColarChave();
                Redesenhar();
            }
            catch (Exception e) { Log.LogWarning("[conta] acao: " + e.GetType().Name); }
        }

        void ColarChave()
        {
            string lido = PcCofre.LerAreaDeTransferencia();
            if (lido == null) { _aviso = "Não consegui ler o que você copiou. Tente de novo."; return; }
            string chave = PcCofre.Limpar(lido);
            if (chave == null) { _aviso = "Isso não parece uma chave. Copie de novo em cricribr.com.br (Minha conta)."; return; }
            if (!PcCofre.Salvar(chave)) { _aviso = "Não consegui guardar a chave neste PC."; Log.LogWarning("[conta] nao gravei a chave"); return; }
            SalaApi.TrocarChave(chave);
            PcCofre.EsvaziarAreaDeTransferencia();
            _aviso = "Chave guardada. O que você copiou foi limpo.";
            Log.LogInfo("[conta] chave guardada");
        }

        void ApagarChave()
        {
            bool certo = PcCofre.Apagar();
            SalaApi.TrocarChave(null);
            _aviso = certo ? "Chave apagada deste PC." : "Tirei a chave desta sessão, mas não consegui apagar o arquivo.";
            Log.LogInfo(certo ? "[conta] chave apagada" : "[conta] nao apaguei o arquivo da chave");
        }
    }
}
#endif
