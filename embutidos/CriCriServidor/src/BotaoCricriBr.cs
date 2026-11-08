using System;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods.Servidor
{
    // The CRICRI BR button on the game's own main menu (CriCri, 08/11/2026: "tire esse mod dos menus mesmo e so coloque o menu
    // normal com os botoes do cricri br ... em baixo do botao de replays do mesmo estilo, se a pessoa nao tiver o mod de replay
    // ele fica no lugar do botao de replay"). One button: CRICRI BR region + the game's Online screen.
    //
    // Same build as the CriCri Replay's "Replays" button (replay\src\BotaoMenu.cs): a clone of the menu's own button under the
    // Play button (same art, sound and hover), outside the panel's mask, without AspectPosition (it would pull the copy back
    // onto the model) and without the translator (it would rewrite the label). With the Replay's button on screen this one goes
    // right under it (and under its version label); without it (no Replay, or its button did not fit), in the Replay's place:
    // the empty space right of the panel, measured from the Play button. Hidden while a submenu covers the menu, like the Replay's.
    // No Harmony: an Update that looks for the menu 4x/s only in the MainMenu scene until the button exists.
    internal sealed class BotaoCricriBr : MonoBehaviour
    {
        public BotaoCricriBr(IntPtr ptr) : base(ptr) { }

        const string NomeDoBotao = "CriCriBr_Botao";
        const string Rotulo = "CRICRI BR";
        const string BotaoDoReplay = "CriCriReplay_Botao";
        const string IdDoReplay = "com.cricri.replay";

        MainMenuManager _menu;
        GameObject _botao;
        GameObject[] _telas;
        TMPro.TextMeshPro[] _textos;
        float _prox, _desde = -1f, _reforcarAte, _pendenteDesde = -1f;
        bool _falhou;

        void Update()
        {
            float agora = Time.unscaledTime;
            if (_pendenteDesde >= 0f) TiquePendente(agora);
            if (_botao != null)
            {
                Visibilidade();
                if (agora < _reforcarAte && _textos != null)
                    foreach (var t in _textos) try { if (t != null && t.text != Rotulo) t.text = Rotulo; } catch { }
                return;
            }
            if (agora < _prox) return;
            _prox = agora + 0.25f;
            if (SceneManager.GetActiveScene().name != "MainMenu") { _menu = null; _desde = -1f; _falhou = false; return; }
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
                Plugin.Registro.LogWarning("[menu] botao CRICRI BR: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static bool ReplayCarregado()
        {
            try { return IL2CPPChainloader.Instance.Plugins.ContainsKey(IdDoReplay); } catch { return false; }
        }

        void Criar(MainMenuManager m, GameObject replay)
        {
            var jogar = m.playButton;
            if (jogar == null) { _falhou = true; Plugin.Registro.LogWarning("[menu] menu sem botao Jogar: sem botao CRICRI BR"); return; }

            // the model: the large button right under Play (the Replay's choice), or Play itself
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

            // world scale of the model (the parent changed)
            var ls = modelo.transform.lossyScale;
            var ps = m.transform.lossyScale;
            clone.transform.localScale = new Vector3(
                Mathf.Abs(ps.x) > 0.0001f ? ls.x / ps.x : ls.x,
                Mathf.Abs(ps.y) > 0.0001f ? ls.y / ps.y : ls.y,
                Mathf.Abs(ps.z) > 0.0001f ? ls.z / ps.z : ls.z);

            float largura = Largura(modelo.gameObject), altura = Altura(modelo.gameObject);
            if (largura < 0.5f) largura = 2.98f;
            if (altura < 0.2f) altura = 0.65f;
            float z = modelo.transform.position.z;
            string onde;
            if (replay != null && Caixa(replay, out Bounds br))
            {
                // under the Replay's button and its version label (both inside its bounds), same gap as a menu column
                clone.transform.position = new Vector3(replay.transform.position.x, br.min.y - altura * 0.25f - altura / 2f, replay.transform.position.z);
                onde = "embaixo do Replays";
            }
            else
            {
                // the Replay's place (BotaoMenu.cs): right of the panel, top aligned a little above Play
                float x = jx + Largura(jogar.gameObject) / 2f + 0.85f + largura / 2f;
                float y = jy + Altura(jogar.gameObject) / 2f + 0.53f - altura / 2f;
                clone.transform.position = new Vector3(x, y, z);
                onde = replay == null && ReplayCarregado() ? "no lugar do Replays (o botao dele nao apareceu)" : "no lugar do Replays (sem o mod Replay)";
            }

            // outside the panel: no mask on the sprites, no stencil on the text; the model's small icon off
            foreach (var sr in clone.GetComponentsInChildren<SpriteRenderer>(true)) try { sr.maskInteraction = SpriteMaskInteraction.None; } catch { }
            EsconderIcone(clone);
            _textos = clone.GetComponentsInChildren<TMPro.TextMeshPro>(true);
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
            if (passivo == null) { Destroy(clone); _falhou = true; Plugin.Registro.LogWarning("[menu] clone sem PassiveButton: sem botao CRICRI BR"); return; }
            try { passivo.ClickMask = null; } catch { }
            passivo.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            passivo.OnClick.AddListener((Action)Clicou);

            clone.SetActive(true);
            _botao = clone;
            _telas = new[] { m.gameModeButtons, m.onlineButtons, m.accountButtons, m.enterCodeButtons };
            Plugin.Registro.LogInfo("[menu] botao CRICRI BR " + onde + " em (" + clone.transform.position.x.ToString("0.00") + ", "
                + clone.transform.position.y.ToString("0.00") + "), modelo " + modelo.name);
        }

        // CRICRI BR region, then the game's own path to Online: Play, and the Online button once it is on screen.
        void Clicou()
        {
            try
            {
                Plugin.Registro.LogInfo("[menu] clique: CRICRI BR");
                EscolherRegiao();
                var m = _menu;
                if (m == null) return;
                if (m.playButton != null) m.playButton.OnClick.Invoke();
                else m.OpenGameModeMenu();
                _pendenteDesde = Time.unscaledTime;
            }
            catch (Exception e) { Plugin.Registro.LogWarning("[menu] clique CRICRI BR: " + e.GetType().Name + ": " + e.Message); }
        }

        static void EscolherRegiao()
        {
            if (!DestroyableSingleton<ServerManager>.InstanceExists) return;
            var sm = DestroyableSingleton<ServerManager>.Instance;
            var atual = sm.CurrentRegion;
            if (atual != null && atual.Name == Regiao.Nome) return;
            var regioes = sm.AvailableRegions;
            if (regioes == null) return;
            for (int i = 0; i < regioes.Length; i++)
            {
                if (regioes[i] == null || regioes[i].Name != Regiao.Nome) continue;
                sm.SetRegion(regioes[i]);
                Plugin.Registro.LogInfo("[menu] regiao: " + Regiao.Nome);
                return;
            }
            Plugin.Registro.LogWarning("[menu] a regiao " + Regiao.Nome + " ainda nao esta na lista do jogo");
        }

        void TiquePendente(float agora)
        {
            var m = _menu;
            float passou = agora - _pendenteDesde;
            PassiveButton online = null;
            try { online = m != null ? m.PlayOnlineButton : null; } catch { }
            bool animando = false;
            try { animando = m != null && m.animating; } catch { }
            if (online != null && online.isActiveAndEnabled && passou > 0.05f && (!animando || passou > 0.8f))
            {
                _pendenteDesde = -1f;
                try { online.OnClick.Invoke(); } catch (Exception e) { Plugin.Registro.LogWarning("[menu] Online: " + e.Message); }
            }
            else if (passou > 2.5f)
            {
                _pendenteDesde = -1f;
                Plugin.Registro.LogInfo("[menu] o botao Online nao apareceu; fica a tela de modos do jogo");
            }
        }

        void Visibilidade()
        {
            if (_telas == null) return;
            bool ocupado = false;
            foreach (var t in _telas) try { if (t != null && t.activeInHierarchy) { ocupado = true; break; } } catch { }
            try { if (_botao.activeSelf == ocupado) _botao.SetActive(!ocupado); } catch { }
        }

        // The model's small icon (left of center, much smaller than the background) means nothing on this button.
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
    }
}
