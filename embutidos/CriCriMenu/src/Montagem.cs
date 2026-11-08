using System;
using System.Collections.Generic;
using AmongUs.Data;
using TMPro;
using UnityEngine;

namespace CriCriMods.Menu
{
    // One style built on one MainMenuManager instance.
    //
    // painel / hub ("cobre"): a full-screen layer of ours drawn just in front of the game's menu (same sorting layer,
    // the menu's highest order, a little closer to the camera than its closest piece). The game's menu stays exactly
    // as it is underneath; only the colliders of its buttons are switched off while our layer is on screen (and back
    // on the moment it leaves). Whenever the game opens anything (Jogar's modes, online, account, settings, news,
    // inventory, shop, friends, any new popup) our layer steps aside and the game's own menu is there, untouched.
    //
    // classico: the game's menu as it is, plus an "Amigos" card in the free space under the Replay's button, drawn at
    // the menu's own depth (anything the game opens covers it, like the game's own buttons).
    //
    // Nothing of the game is moved, scaled or recolored: what we change is the collider.enabled list (restored) and
    // our own objects (destroyed). The original stamp (ModManager.ModStamp) is untouched; a copy of its sprite is
    // drawn on top of our layer at the same place on screen, so it stays visible in every style.
    internal sealed class Montagem
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;
        static bool _logouEstrutura;
        static readonly HashSet<string> _avisos = new HashSet<string>();

        public readonly string Estilo;
        public readonly MainMenuManager Menu;
        readonly bool _cobre;

        GameObject _raiz;
        Camera _cam;
        float _f = 1f;               // +1 when the camera looks toward +z
        float _zRaiz;
        readonly Desenho _d = new Desenho();
        readonly Grade _g = new Grade();

        readonly List<Placa> _placas = new List<Placa>();
        readonly List<Rotulo> _rotulos = new List<Rotulo>();
        readonly List<Botao> _botoes = new List<Botao>();
        Placa _fundoTela;
        Boneco _boneco;
        SpriteRenderer _selo;
        Rotulo _nome, _nivel;
        Placa _chip, _pilula, _avatar;
        Botao _amizades;

        // what tells us the game opened something
        readonly List<KeyValuePair<GameObject, string>> _submenus = new List<KeyValuePair<GameObject, string>>();
        readonly List<KeyValuePair<GameObject, string>> _popups = new List<KeyValuePair<GameObject, string>>();
        readonly List<GameObject> _telas = new List<GameObject>();     // options, inventory, shop, freeplay (found by type)
        readonly HashSet<IntPtr> _telasNaMontagem = new HashSet<IntPtr>();
        readonly HashSet<IntPtr> _presentesNaMontagem = new HashSet<IntPtr>();   // clickable things already there
        bool _menuUiNaMontagem, _tintNaMontagem;
        string _popupNovo;

        // painel/hub: the game's colliders we switched off (restored when our layer leaves or on teardown)
        readonly Dictionary<IntPtr, Collider2D> _desligados = new Dictionary<IntPtr, Collider2D>();

        bool _visivel, _mostrouUmaVez;
        float _livreDesde = -1f;
        string _motivo = "";
        float _montadaEm, _proxDispor, _proxVarredura, _proxTelas, _proxTextos, _ocupadoDesde;
        int _telaL, _telaA;
        Rect _seguro;

        // "Online / Local / Treino": Jogar first (the game's own path), then the mode's button once it is on screen
        Func<PassiveButton> _pendente;
        string _pendenteNome;
        float _pendenteDesde;

        Montagem(MainMenuManager m, string estilo)
        {
            Menu = m;
            Estilo = estilo;
            _cobre = estilo == Escolha.Painel || estilo == Escolha.Hub;
        }

        // Never throws: on any failure what was made is undone and null comes back (the game's menu stays as it is).
        public static Montagem Criar(MainMenuManager m, string estilo)
        {
            var mt = new Montagem(m, estilo);
            try
            {
                if (!mt.Montar()) { mt.Desmontar(); return null; }
                return mt;
            }
            catch (Exception e)
            {
                Log.LogWarning("[menu] montagem do estilo " + estilo + " falhou: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                mt.Desmontar();
                return null;
            }
        }

        bool Montar()
        {
            var m = Menu;
            var jogar = m.playButton;
            if (jogar == null) { Log.LogWarning("[menu] o menu nao tem botao Jogar; fica o menu do jogo"); return false; }
            var modelo = m.inventoryButton ?? m.shopButton ?? jogar;

            // the camera that draws the menu's buttons, and what to draw like
            var fundoJogar = MaiorSprite(jogar.gameObject);
            int camada = fundoJogar != null ? fundoJogar.gameObject.layer : jogar.gameObject.layer;
            _cam = CameraQueDesenha(camada);
            if (_cam == null) { Log.LogWarning("[menu] nenhuma camera desenha o menu (camada " + camada + "); fica o menu do jogo"); return false; }
            _f = _cam.transform.forward.z >= 0f ? 1f : -1f;
            _d.Camada = camada;
            var textoJogar = jogar.GetComponentInChildren<TextMeshPro>(true) ?? modelo.GetComponentInChildren<TextMeshPro>(true);
            if (textoJogar == null || textoJogar.font == null) { Log.LogWarning("[menu] sem fonte do jogo pra copiar; fica o menu do jogo"); return false; }
            _d.Fonte = textoJogar.font;
            _d.MaterialBotao = textoJogar.fontSharedMaterial;
            _d.Fator = Pecas.FatorFonte(_d.Fonte);

            JuntarSinais();
            LogEstrutura();

            _raiz = new GameObject("CriCriMenu_" + Estilo);
            _raiz.layer = camada;

            if (_cobre)
            {
                if (!MedirMenu()) return false;
                _raiz.transform.position = new Vector3(_cam.transform.position.x, _cam.transform.position.y, _zRaiz);
                // the whole screen, in the mockup's background color, with a few stars
                _fundoTela = Placa.Nova(_raiz.transform, "CriCriFundoTela", _d, Pecas.Quadrado(), Pecas.Cor("#0A0E1C"), new Rect(0, 0, 1, 1), 0f);
                Estrelas();
                if (Estilo == Escolha.Painel) MontarPainel(modelo); else MontarHub(modelo);
            }
            else
            {
                // classico: the card at the depth of the game's buttons
                var refBotao = BotaoDoReplay() ?? modelo.gameObject;
                var sr = MaiorSprite(refBotao) ?? fundoJogar;
                _d.LayerId = sr != null ? sr.sortingLayerID : 0;
                _d.Ordem = sr != null ? sr.sortingOrder : 0;
                _zRaiz = refBotao.transform.position.z;
                _raiz.transform.position = new Vector3(_cam.transform.position.x, _cam.transform.position.y, _zRaiz);
                MontarClassico(modelo);
            }

            MarcarPresentes();
            _montadaEm = Time.unscaledTime;
            Dispor();
            AtualizarSelo();
            foreach (var b in _botoes) b.Mostrar(true);
            _raiz.SetActive(false);
            _visivel = false;
            Log.LogInfo("[menu] estilo " + Estilo + " montado: " + _botoes.Count + " botoes, " + _rotulos.Count + " textos, boneco "
                + (_boneco != null ? "sim" : "nao") + ", camera " + _cam.name + " (orto " + _cam.orthographicSize.ToString("0.00")
                + ", " + Screen.width + "x" + Screen.height + "), camada " + camada + ", ordem " + _d.Ordem + ", z " + _zRaiz.ToString("0.000")
                + ", letra " + _d.Fator.ToString("0.0") + " pt/unidade");
            return true;
        }

        // ---------------- measuring the game's menu ----------------

        // Our layer goes on the highest sorting layer the idle menu uses, at its highest order there, a bit closer to
        // the camera than the closest piece at that order: it covers everything of the menu, and anything the game
        // opens closer to the camera (its screens and popups) still comes in front.
        bool MedirMenu()
        {
            var lista = new List<Renderer>();
            Juntar(lista, Menu.gameObject);
            try
            {
                if (DestroyableSingleton<FriendsListManager>.InstanceExists)
                {
                    var fb = DestroyableSingleton<FriendsListManager>.Instance.FriendsListButton;
                    if (fb != null) Juntar(lista, fb.gameObject);
                }
            }
            catch { }
            try
            {
                if (DestroyableSingleton<AccountManager>.InstanceExists)
                {
                    var tab = DestroyableSingleton<AccountManager>.Instance.accountTab;
                    if (tab != null && tab.gameObject.activeInHierarchy) Juntar(lista, tab.gameObject);
                }
            }
            catch { }
            // everything clickable already on screen (its art must end up under our layer too)
            foreach (var el in UnityEngine.Object.FindObjectsOfType<PassiveUiElement>())
                if (el != null && !DeUmaTela(el.transform)) Juntar(lista, el.gameObject);

            int melhorValor = int.MinValue, layerId = 0, ordem = int.MinValue;
            var validos = new List<Renderer>();
            foreach (var r in lista)
            {
                if (!Conta(r)) continue;
                validos.Add(r);
                int v = ValorDaCamada(r.sortingLayerID);
                if (v > melhorValor) { melhorValor = v; layerId = r.sortingLayerID; }
            }
            if (validos.Count == 0)
            {
                Log.LogWarning("[menu] nao achei o desenho do menu na tela; fica o menu do jogo");
                return false;
            }
            foreach (var r in validos)
                if (ValorDaCamada(r.sortingLayerID) == melhorValor && r.sortingOrder > ordem) ordem = r.sortingOrder;
            float maisPerto = float.MaxValue;   // depth along the camera axis
            string quem = "?";
            foreach (var r in validos)
            {
                if (ValorDaCamada(r.sortingLayerID) != melhorValor || r.sortingOrder != ordem) continue;
                float p = r.bounds.center.z * _f;
                if (p < maisPerto) { maisPerto = p; quem = r.name; }
            }
            _d.LayerId = layerId;
            _d.Ordem = ordem;
            _zRaiz = (maisPerto - 0.05f) * _f;
            // never closer than the camera's near plane
            float camZ = _cam.transform.position.z * _f;
            if (_zRaiz * _f - camZ < _cam.nearClipPlane + 0.2f)
            {
                _zRaiz = (camZ + _cam.nearClipPlane + 0.2f) * _f;
                Aviso("perto", "[menu] o menu chega quase na camera; camada no limite (z " + _zRaiz.ToString("0.00") + ")");
            }
            Log.LogInfo("[menu] desenho do menu: " + validos.Count + " pecas, camada de ordem " + NomeDaCamada(layerId) + " (valor " + melhorValor
                + "), ordem " + ordem + ", peca mais perto " + quem + " em z " + (maisPerto * _f).ToString("0.000") + "; nossa camada em z " + _zRaiz.ToString("0.000"));
            return true;
        }

        void Juntar(List<Renderer> lista, GameObject go)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false)) lista.Add(r);
        }

        // A piece of the idle menu that is really on screen.
        bool Conta(Renderer r)
        {
            try
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
                if (_raiz != null && r.transform.IsChildOf(_raiz.transform)) return false;
                if (DeUmaTela(r.transform)) return false;
                if (Menu.screenTint != null && r.transform.IsChildOf(Menu.screenTint.transform)) return false;
                if (Menu.fullScreenSprite != null && r.transform.IsChildOf(Menu.fullScreenSprite.transform)) return false;
                if ((_cam.cullingMask & (1 << r.gameObject.layer)) == 0) return false;
                if (r.TryCast<SpriteMask>() != null) return false;
                var sr = r.TryCast<SpriteRenderer>();
                if (sr != null && (sr.sprite == null || sr.color.a < 0.05f)) return false;
                var b = r.bounds;
                if (b.size.x < 0.001f && b.size.y < 0.001f) return false;
                return NaTela(b);
            }
            catch { return false; }
        }

        bool NaTela(Bounds b)
        {
            if (_cam == null) return false;
            float alto = _cam.orthographicSize, largo = alto * _cam.aspect;
            var c = _cam.transform.position;
            return b.max.x > c.x - largo && b.min.x < c.x + largo && b.max.y > c.y - alto && b.min.y < c.y + alto;
        }

        // Is it inside one of the screens the game opens (sub-menus, popups)? Those never count as "the idle menu".
        bool DeUmaTela(Transform t)
        {
            if (t == null) return false;
            foreach (var kv in _submenus) if (kv.Key != null && t.IsChildOf(kv.Key.transform)) return true;
            foreach (var kv in _popups) if (kv.Key != null && t.IsChildOf(kv.Key.transform)) return true;
            foreach (var go in _telas) if (go != null && t.IsChildOf(go.transform)) return true;
            return false;
        }

        static int ValorDaCamada(int id)
        {
            try { return SortingLayer.GetLayerValueFromID(id); } catch { return 0; }
        }

        static string NomeDaCamada(int id)
        {
            try { return SortingLayer.IDToName(id); } catch { return id.ToString(); }
        }

        void JuntarSinais()
        {
            var m = Menu;
            void Sub(GameObject go, string nome) { if (go != null) _submenus.Add(new KeyValuePair<GameObject, string>(go, nome)); }
            void Pop(GameObject go, string nome) { if (go != null) _popups.Add(new KeyValuePair<GameObject, string>(go, nome)); }
            // the same four the Replay hides its button for (proven on the phone: off while the menu is idle), plus two
            Sub(m.gameModeButtons, "modos de jogo");
            Sub(m.onlineButtons, "online");
            Sub(m.accountButtons, "conta");
            Sub(m.enterCodeButtons, "codigo");
            Sub(m.creditsScreen, "creditos");
            try { if (m.createGameScreen != null) Sub(m.createGameScreen.gameObject, "criar sala"); } catch { }
            try { if (m.announcementPopUp != null) Pop(m.announcementPopUp.gameObject, "noticias"); } catch { }
            try { if (m.ejectMenu != null) Pop(m.ejectMenu.gameObject, "expulso"); } catch { }
            try { if (m.AdsPolicy != null) Pop(m.AdsPolicy.gameObject, "anuncios"); } catch { }
            Pop(m.adsMenu, "anuncios");
            try
            {
                if (DestroyableSingleton<AccountManager>.InstanceExists)
                    Pop(DestroyableSingleton<AccountManager>.Instance.accountWindow, "janela da conta");
            }
            catch { }
            AcharTelas();
            foreach (var go in _telas) if (TemAlgoNaTela(go)) _telasNaMontagem.Add(go.Pointer);
            _menuUiNaMontagem = Pecas.Ativo(m.mainMenuUI);
            try { _tintNaMontagem = m.screenTint != null && m.screenTint.isVisible && m.screenTint.gameObject.activeInHierarchy && m.screenTint.enabled && m.screenTint.color.a > 0.05f; } catch { }
        }

        // Screens found by type (they may live outside the MainMenuManager): settings, inventory, shop, freeplay maps.
        void AcharTelas()
        {
            _telas.Clear();
            try { foreach (var o in UnityEngine.Object.FindObjectsOfType<OptionsMenuBehaviour>(true)) if (o != null) _telas.Add(o.gameObject); } catch { }
            try { foreach (var o in UnityEngine.Object.FindObjectsOfType<PlayerCustomizationMenu>(true)) if (o != null) _telas.Add(o.gameObject); } catch { }
            try { foreach (var o in UnityEngine.Object.FindObjectsOfType<StoreMenu>(true)) if (o != null) _telas.Add(o.gameObject); } catch { }
            try { foreach (var o in UnityEngine.Object.FindObjectsOfType<FreeplayPopover>(true)) if (o != null) _telas.Add(o.gameObject); } catch { }
        }

        // Active and with something visible inside the camera's view (an active but empty or off-screen container
        // does not count).
        bool TemAlgoNaTela(GameObject go)
        {
            try
            {
                if (go == null || !go.activeInHierarchy) return false;
                foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null || !r.enabled) continue;
                    var sr = r.TryCast<SpriteRenderer>();
                    if (sr != null && (sr.sprite == null || sr.color.a < 0.05f)) continue;
                    var b = r.bounds;
                    if (b.size.x < 0.001f && b.size.y < 0.001f) continue;
                    if (NaTela(b)) return true;
                }
            }
            catch { }
            return false;
        }

        void MarcarPresentes()
        {
            _presentesNaMontagem.Clear();
            foreach (var el in UnityEngine.Object.FindObjectsOfType<PassiveUiElement>())
                if (el != null) _presentesNaMontagem.Add(el.Pointer);
        }

        // ---------------- painel (A) ----------------

        void MontarPainel(PassiveButton modelo)
        {
            var m = Menu;
            // top bar: the player's name and level on a chip
            _chip = NovaPlaca("CriCriChip", new Rect(20, 14, 260, 44), "#141B33", 0.02f);
            _avatar = NovaPlaca("CriCriAvatar", new Rect(26, 20, 32, 32), null, 0.03f);
            _avatar.Sr.color = CorDoJogador();
            _nome = NovoRotulo("CriCriNome", "", new Rect(66, 14, 180, 44), 18f, "#F2F4FA", true, TextAlignmentOptions.MidlineLeft, 0.03f);
            _nome.T.richText = false;
            _nivel = NovoRotulo("CriCriNivel", "", new Rect(200, 14, 70, 44), 12f, "#A9B1CC", false, TextAlignmentOptions.MidlineLeft, 0.03f);
            // Settings and "Menu clássico" on the right
            float xClassico = 1020f - 12f - 120f;
            if (m.settingsButton != null)
            {
                NovoBotao(modelo, "Definições", new Rect(908, 14, 112, 44), "#141B33", "Definições", 14f, "#F2F4FA", () => Clicar(m.settingsButton));
                xClassico = 908f - 12f - 120f;
            }
            NovoBotaoContorno(modelo, "Menu clássico", new Rect(xClassico, 16, 120, 40), "#2A3355", "Menu clássico", 13f, "#A9B1CC", ParaClassico);

            // column 1: JOGAR, the three modes, and Replays / Notícias / Conta at the foot
            NovoBotao(modelo, "JOGAR", new Rect(20, 72, 372, 132), "#FF5A36", "JOGAR", 52f, "#FFFFFF", () => Clicar(m.playButton), contorno: true);
            Modos(new Rect(20, 214, 372, 48), 17f);
            var pe = new List<Action<Rect>>();
            if (BotaoDoReplay() != null) pe.Add(r => NovoBotao(modelo, "Replays", r, "#141B33", "Replays", 15f, "#F2F4FA", ClicarReplay));
            if (m.newsButton != null) pe.Add(r => NovoBotao(modelo, "Notícias", r, "#141B33", "Notícias", 15f, "#F2F4FA", () => Clicar(m.newsButton)));
            if (m.myAccountButton != null) pe.Add(r => NovoBotao(modelo, "Conta", r, "#141B33", "Conta", 15f, "#F2F4FA", () => Clicar(m.myAccountButton)));
            Fileira(pe, new Rect(20, 382, 372, 56), 8f);

            // column 2: you
            NovaPlaca("CriCriVoce", new Rect(408, 72, 236, 366), "#10162B", 0.02f);
            CriarBoneco(new Rect(420, 86, 212, 282));
            var inv = new List<Action<Rect>>();
            if (m.inventoryButton != null) inv.Add(r => NovoBotao(modelo, "Inventário", r, "#7FE3D7", "Inventário", 15f, "#05070F", () => Clicar(m.inventoryButton)));
            if (m.shopButton != null) inv.Add(r => NovoBotao(modelo, "Loja", r, "#7FE3D7", "Loja", 15f, "#05070F", () => Clicar(m.shopButton)));
            Fileira(inv, new Rect(420, 380, 212, 46), 8f);

            // column 3: friends
            NovaPlaca("CriCriAmigos", new Rect(660, 72, 360, 366), "#10162B", 0.02f);
            NovoRotulo("CriCriAmigosTitulo", "Amigos", new Rect(674, 82, 330, 34), 22f, "#F2F4FA", true, TextAlignmentOptions.MidlineLeft, 0.03f);
            NovoRotulo("CriCriAmigosAviso", AvisoAmigos, new Rect(674, 124, 330, 70), 14f, "#A9B1CC", false, TextAlignmentOptions.TopLeft, 0.03f, quebra: true);
            BotaoAmizades(modelo, new Rect(674, 380, 332, 46), 15f);

            NovoRotulo("CriCriNaoOficial", "com mods · não oficial", new Rect(780, 438, 240, 16), 10f, "#5B6380", false, TextAlignmentOptions.MidlineRight, 0.03f);
        }

        // ---------------- hub (B) ----------------

        void MontarHub(PassiveButton modelo)
        {
            var m = Menu;
            // left rail
            NovoRotulo("CriCriAmongUs", "AMONG US", new Rect(20, 14, 200, 32), 22f, "#F2F4FA", true, TextAlignmentOptions.MidlineLeft, 0.03f);
            NovoRotulo("CriCriNaoOficial", "com mods · não oficial", new Rect(20, 44, 200, 16), 11f, "#A9B1CC", false, TextAlignmentOptions.MidlineLeft, 0.03f);
            float y = 70f;
            void Trilho(PassiveButton original, string texto, Action acao)
            {
                if (original == null && acao == null) return;
                var b = NovoBotao(modelo, texto, new Rect(20, y, 200, 52), "#141B33", texto, 17f, "#F2F4FA", acao, alinhamento: TextAlignmentOptions.MidlineLeft);
                if (b.Texto != null) b.Texto.Px = new Rect(36, y, 176, 52);
                y += 62f;
            }
            if (m.inventoryButton != null) Trilho(m.inventoryButton, "Inventário", () => Clicar(m.inventoryButton));
            if (m.shopButton != null) Trilho(m.shopButton, "Loja", () => Clicar(m.shopButton));
            if (BotaoDoReplay() != null) Trilho(null, "Replays", ClicarReplay);
            if (m.newsButton != null) Trilho(m.newsButton, "Notícias", () => Clicar(m.newsButton));
            NovoBotaoContorno(modelo, "Menu clássico", new Rect(20, 398, 200, 40), "#2A3355", "Menu clássico", 13f, "#A9B1CC", ParaClassico);

            // center: name + level, the crewmate, JOGAR and the three modes
            _nome = NovoRotulo("CriCriNome", "", new Rect(238, 14, 400, 38), 26f, "#F2F4FA", true, TextAlignmentOptions.MidlineLeft, 0.03f);
            _nome.T.richText = false;
            _pilula = NovaPlaca("CriCriNivelFundo", new Rect(600, 22, 80, 22), "#7FE3D7", 0.025f);
            _nivel = NovoRotulo("CriCriNivel", "", new Rect(600, 22, 80, 22), 12f, "#05070F", false, TextAlignmentOptions.Center, 0.03f);
            CriarBoneco(new Rect(402, 58, 200, 200));
            NovoBotao(modelo, "JOGAR", new Rect(332, 264, 340, 84), "#FF5A36", "JOGAR", 44f, "#FFFFFF", () => Clicar(m.playButton), contorno: true);
            Modos(new Rect(338, 356, 328, 40), 15f);

            // right rail: account and settings on top, friends below
            if (m.myAccountButton != null && m.settingsButton != null)
            {
                NovoBotao(modelo, "Conta", new Rect(784, 16, 96, 44), "#141B33", "Conta", 14f, "#F2F4FA", () => Clicar(m.myAccountButton));
                NovoBotao(modelo, "Definições", new Rect(888, 16, 132, 44), "#141B33", "Definições", 14f, "#F2F4FA", () => Clicar(m.settingsButton));
            }
            else if (m.settingsButton != null) NovoBotao(modelo, "Definições", new Rect(784, 16, 236, 44), "#141B33", "Definições", 14f, "#F2F4FA", () => Clicar(m.settingsButton));
            else if (m.myAccountButton != null) NovoBotao(modelo, "Conta", new Rect(784, 16, 236, 44), "#141B33", "Conta", 14f, "#F2F4FA", () => Clicar(m.myAccountButton));
            NovaPlaca("CriCriAmigos", new Rect(784, 68, 236, 370), "#10162B", 0.02f);
            NovoRotulo("CriCriAmigosTitulo", "Amigos", new Rect(796, 76, 212, 30), 19f, "#F2F4FA", true, TextAlignmentOptions.MidlineLeft, 0.03f);
            NovoRotulo("CriCriAmigosAviso", AvisoAmigos, new Rect(796, 112, 212, 90), 13f, "#A9B1CC", false, TextAlignmentOptions.TopLeft, 0.03f, quebra: true);
            BotaoAmizades(modelo, new Rect(796, 386, 212, 40), 14f);
        }

        // ---------------- classico ----------------

        // The card's own grid: 300x140 mockup px at the phone's scale (6 units per 456 px), placed in Dispor.
        void MontarClassico(PassiveButton modelo)
        {
            NovaPlaca("CriCriAmigos", new Rect(0, 0, 300, 140), "#10162B", 0f);
            NovoRotulo("CriCriAmigosTitulo", "Amigos", new Rect(14, 8, 272, 30), 20f, "#F2F4FA", true, TextAlignmentOptions.MidlineLeft, 0.01f);
            NovoRotulo("CriCriAmigosAviso", AvisoAmigos, new Rect(14, 40, 272, 44), 13f, "#A9B1CC", false, TextAlignmentOptions.TopLeft, 0.01f, quebra: true);
            BotaoAmizades(modelo, new Rect(14, 92, 272, 38), 14f, 0.01f);
        }

        const string AvisoAmigos = "Amigos online chegam com o servidor CRICRI BR";

        // ---------------- pieces ----------------

        Placa NovaPlaca(string nome, Rect px, string cor, float frente, bool contorno = false)
        {
            var p = Placa.Nova(_raiz.transform, nome, _d, contorno ? Pecas.Contorno() : Pecas.Cheio(), cor != null ? Pecas.Cor(cor) : Color.white, px, frente);
            _placas.Add(p);
            return p;
        }

        Rotulo NovoRotulo(string nome, string texto, Rect px, float tamPx, string cor, bool contorno, TextAlignmentOptions alinhamento, float frente, bool quebra = false)
        {
            var r = Rotulo.Novo(_raiz.transform, nome, _d, texto, tamPx, Pecas.Cor(cor), contorno, alinhamento, px, frente, quebra, auto: true);
            _rotulos.Add(r);
            return r;
        }

        Botao NovoBotao(PassiveButton modelo, string nome, Rect px, string corFundo, string texto, float tamPx, string corTexto, Action acao,
            bool contorno = false, TextAlignmentOptions alinhamento = TextAlignmentOptions.Center, float frente = 0.04f, bool soBorda = false)
        {
            var b = Botao.Novo(modelo, _raiz.transform, "CriCriBotao_" + nome, _d, acao);
            b.Px = px;
            b.Frente = frente;
            b.ComFundo(_d, soBorda ? Pecas.Contorno() : Pecas.Cheio(), Pecas.Cor(corFundo));
            if (!string.IsNullOrEmpty(texto))
                b.ComTexto(_d, texto, tamPx, Pecas.Cor(corTexto), contorno, alinhamento, new Rect(px.x + 6, px.y, px.width - 12, px.height));
            _botoes.Add(b);
            return b;
        }

        Botao NovoBotaoContorno(PassiveButton modelo, string nome, Rect px, string corBorda, string texto, float tamPx, string corTexto, Action acao) =>
            NovoBotao(modelo, nome, px, corBorda, texto, tamPx, corTexto, acao, soBorda: true);

        // n buttons side by side in a row, gap in px
        static void Fileira(List<Action<Rect>> botoes, Rect linha, float vao)
        {
            if (botoes.Count == 0) return;
            float w = (linha.width - vao * (botoes.Count - 1)) / botoes.Count;
            for (int i = 0; i < botoes.Count; i++) botoes[i](new Rect(linha.x + i * (w + vao), linha.y, w, linha.height));
        }

        void Modos(Rect linha, float tamPx)
        {
            var m = Menu;
            var lista = new List<Action<Rect>>();
            if (m.PlayOnlineButton != null) lista.Add(r => NovoBotao(m.inventoryButton ?? m.playButton, "Online", r, "#7FE3D7", "Online", tamPx, "#05070F", () => AbrirModo(() => Menu.PlayOnlineButton, "Online")));
            if (m.playLocalButton != null) lista.Add(r => NovoBotao(m.inventoryButton ?? m.playButton, "Local", r, "#7FE3D7", "Local", tamPx, "#05070F", () => AbrirModo(() => Menu.playLocalButton, "Local")));
            if (m.freePlayButton != null) lista.Add(r => NovoBotao(m.inventoryButton ?? m.playButton, "Treino", r, "#7FE3D7", "Treino", tamPx, "#05070F", () => AbrirModo(() => Menu.freePlayButton, "Treino")));
            Fileira(lista, linha, 8f);
        }

        void CriarBoneco(Rect px)
        {
            try
            {
                _boneco = Boneco.Novo(_raiz.transform, _d, px, 0.05f, out string origem);
                if (_boneco != null) Log.LogInfo("[menu] boneco copiado de " + origem);
                else Log.LogInfo("[menu] nenhum boneco no menu pra copiar; o espaco fica com o cartao");
            }
            catch (Exception e)
            {
                Log.LogWarning("[menu] boneco: " + e.GetType().Name + ": " + e.Message);
                _boneco = null;
            }
        }

        // "Amizades do jogo": the game's own friends list (its friends button's click, or FriendsListManager.OpenUi).
        void BotaoAmizades(PassiveButton modelo, Rect px, float tamPx, float frente = 0.04f)
        {
            if (!PodeAbrirAmizades()) { Log.LogInfo("[menu] sem lista de amizades do jogo neste menu; o cartao fica so com o aviso"); return; }
            _amizades = NovoBotao(modelo, "Amizades do jogo", px, "#141B33", "Amizades do jogo", tamPx, "#7FE3D7", AbrirAmizades, frente: frente);
        }

        static bool PodeAbrirAmizades()
        {
            try
            {
                if (!DestroyableSingleton<FriendsListManager>.InstanceExists) return false;
                var fb = DestroyableSingleton<FriendsListManager>.Instance.FriendsListButton;
                // the game hides its own friends button where friends are not allowed (guest, child account)
                if (fb != null && fb.Button != null) return fb.Button.activeInHierarchy;
                return true;
            }
            catch { return false; }
        }

        static void AbrirAmizades()
        {
            if (!DestroyableSingleton<FriendsListManager>.InstanceExists) return;
            var f = DestroyableSingleton<FriendsListManager>.Instance;
            PassiveButton pb = null;
            try { if (f.FriendsListButton != null && f.FriendsListButton.Button != null) pb = f.FriendsListButton.Button.GetComponent<PassiveButton>(); } catch { }
            if (pb != null && pb.OnClick != null) { pb.OnClick.Invoke(); return; }
            f.OpenUi();
        }

        static GameObject BotaoDoReplay(MainMenuManager m)
        {
            try
            {
                var t = m.transform.Find("CriCriReplay_Botao");
                return t != null ? t.gameObject : null;
            }
            catch { return null; }
        }

        GameObject BotaoDoReplay() => BotaoDoReplay(Menu);

        void ClicarReplay()
        {
            var go = BotaoDoReplay();
            var pb = go != null ? (go.GetComponent<PassiveButton>() ?? go.GetComponentInChildren<PassiveButton>(true)) : null;
            if (pb == null) { Log.LogWarning("[menu] o botao Replays do CriCri Replay sumiu"); return; }
            pb.OnClick.Invoke();
        }

        static void Clicar(PassiveButton original)
        {
            if (original == null) return;
            original.OnClick.Invoke();
        }

        void ParaClassico()
        {
            if (Escolha.Gravar(Escolha.Classico)) TrocaDeMenu.PedidoDeReaplicar = true;
        }

        void AbrirModo(Func<PassiveButton> alvo, string nome)
        {
            var m = Menu;
            if (m.playButton != null) m.playButton.OnClick.Invoke();
            else m.OpenGameModeMenu();
            _pendente = alvo;
            _pendenteNome = nome;
            _pendenteDesde = Time.unscaledTime;
        }

        void TiquePendente(float agora)
        {
            if (_pendente == null) return;
            PassiveButton b = null;
            try { b = _pendente(); } catch { }
            float passou = agora - _pendenteDesde;
            bool animando = false;
            try { animando = Menu.animating; } catch { }
            if (b != null && b.isActiveAndEnabled && passou > 0.05f && (!animando || passou > 0.8f))
            {
                _pendente = null;
                bool livre = true;
                try
                {
                    var cs = b.Colliders;
                    if (cs != null && cs.Length > 0)
                    {
                        livre = false;
                        foreach (var c in cs) if (c != null && c.enabled) { livre = true; break; }
                    }
                }
                catch { }
                if (!livre) { Log.LogInfo("[menu] " + _pendenteNome + ": o jogo deixou esse modo desligado; fica a tela de modos do jogo"); return; }
                Log.LogInfo("[menu] " + _pendenteNome + ": abrindo pelo botao do jogo (" + b.name + ")");
                b.OnClick.Invoke();
            }
            else if (passou > 2.5f)
            {
                _pendente = null;
                Log.LogInfo("[menu] " + _pendenteNome + ": o botao do modo nao apareceu; fica a tela de modos do jogo");
            }
        }

        // ---------------- texts that come from the game ----------------

        static string NomeDoJogador()
        {
            try
            {
                if (DataManager.IsPlayerLoaded)
                {
                    var n = DataManager.Player.Customization.Name;
                    if (!string.IsNullOrWhiteSpace(n)) return n;
                }
            }
            catch { }
            return "";
        }

        static string NivelDoJogador()
        {
            try
            {
                var p = ProgressionManager.Instance;
                if (p != null) { var s = p.CurrentVisualLevel; if (!string.IsNullOrEmpty(s)) return s; }
            }
            catch { }
            try { if (DataManager.IsPlayerLoaded) return ProgressionManager.FormatVisualLevel(DataManager.Player.Stats.Level); } catch { }
            return null;
        }

        static Color CorDoJogador()
        {
            try
            {
                if (DataManager.IsPlayerLoaded)
                {
                    int id = DataManager.Player.Customization.Color;
                    var cores = Palette.PlayerColors;
                    if (id >= 0 && id < cores.Length) return cores[id];
                }
            }
            catch { }
            return Pecas.Cor("#FF5A36");
        }

        void AtualizarTextos()
        {
            string nome = NomeDoJogador();
            string nivel = NivelDoJogador();
            if (_nome != null) _nome.Texto(nome);
            if (_nivel != null)
            {
                _nivel.Texto(nivel != null ? "nível " + nivel : "");
                _nivel.T.gameObject.SetActive(nivel != null);
                if (_pilula != null) _pilula.Objeto.SetActive(nivel != null);
            }
            if (_avatar != null) _avatar.Sr.color = CorDoJogador();
            if (_amizades != null) _amizades.Mostrar(PodeAbrirAmizades());
        }

        // ---------------- layout ----------------

        public void Dispor()
        {
            if (_raiz == null || _cam == null) return;
            _telaL = Screen.width;
            _telaA = Screen.height;
            _seguro = Screen.safeArea;
            AtualizarTextos();
            if (_cobre) DisporTela(); else DisporClassico();
            foreach (var p in _placas) p.Dispor(_g, Vector3.zero);
            foreach (var r in _rotulos) r.Dispor(_g, Vector3.zero);
            foreach (var b in _botoes) b.Dispor(_g);
            DisporLinhaDoNome();
            _boneco?.Dispor(_g, _d);
        }

        // painel/hub: the 1040x456 mockup scaled to fit inside the safe area, centered (a wider screen gets side
        // margins, a 16:9 one gets top/bottom margins); the background covers the whole camera view.
        void DisporTela()
        {
            var c = _cam.transform.position;
            _raiz.transform.position = new Vector3(c.x, c.y, _zRaiz);
            _raiz.transform.rotation = Quaternion.identity;
            _raiz.transform.localScale = Vector3.one;
            float alto = _cam.orthographicSize * 2f, largo = alto * _cam.aspect;
            // the safe area as fractions of the camera's pixel rect
            Rect pr = _cam.pixelRect;
            float eL = 0f, eR = 0f, eB = 0f, eT = 0f;
            if (pr.width > 1f && pr.height > 1f)
            {
                eL = Mathf.Clamp01((_seguro.xMin - pr.xMin) / pr.width);
                eR = Mathf.Clamp01((pr.xMax - _seguro.xMax) / pr.width);
                eB = Mathf.Clamp01((_seguro.yMin - pr.yMin) / pr.height);
                eT = Mathf.Clamp01((pr.yMax - _seguro.yMax) / pr.height);
            }
            float x0 = -largo / 2f + eL * largo, x1 = largo / 2f - eR * largo;
            float y0 = -alto / 2f + eB * alto, y1 = alto / 2f - eT * alto;
            float w = Math.Max(0.1f, x1 - x0), h = Math.Max(0.1f, y1 - y0);
            _g.S = Math.Min(h / 456f, w / 1040f);
            _g.F = _f;
            _g.X0 = (x0 + x1) / 2f - 520f * _g.S;
            _g.Y0 = (y0 + y1) / 2f + 228f * _g.S;
            if (_fundoTela != null)
            {
                _fundoTela.Objeto.transform.localPosition = Vector3.zero;
                _fundoTela.Sr.size = new Vector2(largo * 1.3f + 1f, alto * 1.3f + 1f);
            }
            for (int i = 0; i < _estrelas.Count; i++)
            {
                var e = _estrelas[i];
                if (e == null) continue;
                var p = PosEstrelas[i];
                e.transform.localPosition = new Vector3(_g.X0 + p.x * _g.S, _g.Y0 - p.y * _g.S, -0.01f * _f);
                e.size = new Vector2(p.z * _g.S, p.z * _g.S);
            }
        }

        // classico: the card under the Replay's button (and its version label), or right of the game's column.
        void DisporClassico()
        {
            var c = _cam.transform.position;
            _raiz.transform.position = new Vector3(c.x, c.y, _zRaiz);
            _raiz.transform.rotation = Quaternion.identity;
            _raiz.transform.localScale = Vector3.one;
            float alto = _cam.orthographicSize, largo = alto * _cam.aspect;
            _g.S = _cam.orthographicSize * 2f / 456f;
            _g.F = _f;
            float w = 300f * _g.S, h = 140f * _g.S;
            float esquerda, topo;
            var replay = BotaoDoReplay();
            if (replay != null && Pecas.Caixa(replay, out Bounds br) && replay.activeInHierarchy)
            {
                float baixo = br.min.y;
                // the Replay's version label sits under its button
                foreach (var t in replay.GetComponentsInChildren<TextMeshPro>(false))
                    if (t != null && t.name.Contains("Versao")) { var mr = t.GetComponent<MeshRenderer>(); if (mr != null && mr.bounds.size.y > 0.001f) baixo = Math.Min(baixo, mr.bounds.min.y); }
                esquerda = br.min.x;
                topo = baixo - 0.15f;
            }
            else if (Menu.playButton != null && Pecas.Caixa(Menu.playButton.gameObject, out Bounds bj))
            {
                esquerda = bj.max.x + 0.85f;
                topo = bj.max.y;
            }
            else
            {
                esquerda = c.x - w / 2f;
                topo = c.y + h / 2f;
            }
            // inside the screen
            esquerda = Mathf.Clamp(esquerda, c.x - largo + 0.2f, c.x + largo - w - 0.2f);
            topo = Mathf.Clamp(topo, c.y - alto + h + 0.2f, c.y + alto - 0.2f);
            _g.X0 = esquerda - c.x;
            _g.Y0 = topo - c.y;
        }

        // The name's width decides where the level goes (painel: on the chip, which grows to fit; hub: the pill).
        void DisporLinhaDoNome()
        {
            if (_nome == null || _nivel == null) return;
            float largNome = Math.Min(_nome.LarguraPx(_g), Estilo == Escolha.Hub ? 330f : 180f);
            float largNivel = _nivel.LarguraPx(_g);
            if (Estilo == Escolha.Painel)
            {
                _nivel.Px = new Rect(66 + largNome + 10, 14, largNivel + 4, 44);
                _nivel.Dispor(_g, Vector3.zero);
                if (_chip != null)
                {
                    float fim = _nivel.T.gameObject.activeSelf ? _nivel.Px.xMax : 66 + largNome;
                    _chip.Px = new Rect(20, 14, Math.Max(120f, fim + 14 - 20), 44);
                    _chip.Dispor(_g, Vector3.zero);
                }
            }
            else
            {
                // centered on the middle column (238..766): name, 8 px, level pill
                float pilula = largNivel + 16f;
                bool comNivel = _nivel.T.gameObject.activeSelf;
                float total = largNome + (comNivel ? 8f + pilula : 0f);
                float x = 502f - total / 2f;
                _nome.Px = new Rect(x, 14, largNome + 2, 38);
                _nome.Dispor(_g, Vector3.zero);
                var rp = new Rect(x + largNome + 8f, 22, pilula, 22);
                _nivel.Px = rp;
                _nivel.Dispor(_g, Vector3.zero);
                if (_pilula != null) { _pilula.Px = rp; _pilula.Dispor(_g, Vector3.zero); }
            }
        }

        static readonly Vector3[] PosEstrelas =
        {
            new Vector3(96, 70, 3), new Vector3(470, 30, 2), new Vector3(610, 400, 3), new Vector3(980, 110, 2),
            new Vector3(320, 430, 2), new Vector3(700, 90, 2), new Vector3(300, 300, 2), new Vector3(760, 360, 3),
        };
        readonly List<SpriteRenderer> _estrelas = new List<SpriteRenderer>();

        void Estrelas()
        {
            for (int i = 0; i < PosEstrelas.Length; i++)
            {
                var go = new GameObject("CriCriEstrela");
                go.layer = _d.Camada;
                go.transform.SetParent(_raiz.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = Pecas.Quadrado();
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.color = new Color(1f, 1f, 1f, 0.55f);
                sr.sortingLayerID = _d.LayerId;
                sr.sortingOrder = _d.Ordem;
                _estrelas.Add(sr);
            }
        }

        // ---------------- every frame ----------------

        public void Tique(float agora)
        {
            if (_raiz == null) throw new InvalidOperationException("a camada sumiu");
            TiquePendente(agora);

            // the screen changed size (rotation, resolution option): lay out again
            if (Screen.width != _telaL || Screen.height != _telaA || Screen.safeArea != _seguro)
            {
                Log.LogInfo("[menu] tela mudou para " + Screen.width + "x" + Screen.height + "; refazendo o lugar das pecas");
                _telaL = Screen.width;
                _telaA = Screen.height;
                _seguro = Screen.safeArea;
                _montadaEm = agora;
                _proxDispor = 0f;
            }

            if (agora >= _proxTelas)
            {
                _proxTelas = agora + 0.5f;
                AcharTelas();
            }
            if (agora >= _proxVarredura)
            {
                _proxVarredura = agora + 0.25f;
                Varrer();
            }

            // our layer comes back only after the game has been quiet for a moment: between two of its screens (or
            // while a map loads after the Freeplay choice) nothing is open for a frame or two
            bool ocupado = Ocupado(out string motivo);
            if (ocupado)
            {
                _livreDesde = -1f;
                if (_visivel || motivo != _motivo) Esconder(motivo);
            }
            else if (!_visivel)
            {
                if (_livreDesde < 0f) _livreDesde = agora;
                if (agora - _livreDesde >= 0.35f) Mostrar();
            }
            if (ocupado && agora - _ocupadoDesde > 20f && !_mostrouUmaVez)
            {
                Aviso("nunca" + motivo, "[menu] faz 20 s que o menu do jogo esta ocupado por \"" + motivo + "\" e o estilo " + Estilo + " ainda nao apareceu");
            }

            if (!_visivel) return;
            // the game lays its menu out over the first frames: measure and place again for a few seconds
            if (agora - _montadaEm < 5f && agora >= _proxDispor)
            {
                _proxDispor = agora + 0.25f;
                Dispor();
            }
            if (agora >= _proxTextos)
            {
                _proxTextos = agora + 1f;
                AtualizarTextos();
                AtualizarSelo();
                if (_boneco != null && agora - _montadaEm >= 5f) _boneco.Dispor(_g, _d);
            }
        }

        // The game opened something of its own?
        bool Ocupado(out string motivo)
        {
            var m = Menu;
            foreach (var kv in _submenus)
                if (Pecas.Ativo(kv.Key)) { motivo = kv.Value; return true; }
            if (_menuUiNaMontagem && m.mainMenuUI != null && !m.mainMenuUI.activeInHierarchy) { motivo = "menu do jogo escondido"; return true; }
            if (_popupNovo != null) { motivo = _popupNovo; return true; }
            try
            {
                var ui = FriendsListUI.Instance;
                if (ui != null && ui.isActiveAndEnabled && ui.IsOpen) { motivo = "amizades"; return true; }
            }
            catch { }
            // on the way into a room (hosting, joining, Freeplay): the menu is about to leave
            try
            {
                var c = AmongUsClient.Instance;
                if (c != null && c.GameState != InnerNet.InnerNetClient.GameStates.NotJoined) { motivo = "entrando numa sala"; return true; }
            }
            catch { }
            motivo = null;
            return false;
        }

        // Four times a second: the popups and screens (with something on screen), any new clickable thing closer to
        // the camera than the idle menu (a window the game opened), and (our layer on screen) the game's colliders
        // under it. A new clickable thing at the menu's depth or behind it (a button the game adds late, like the
        // friends button after the login) is part of the menu: covered like the rest.
        void Varrer()
        {
            _popupNovo = null;
            foreach (var kv in _popups)
                if (TemAlgoNaTela(kv.Key)) { _popupNovo = kv.Value; break; }
            if (_popupNovo == null)
                foreach (var go in _telas)
                    if (go != null && !_telasNaMontagem.Contains(go.Pointer) && TemAlgoNaTela(go)) { _popupNovo = go.name; break; }
            if (_popupNovo == null && !_tintNaMontagem)
            {
                try
                {
                    var t = Menu.screenTint;
                    if (t != null && t.gameObject.activeInHierarchy && t.enabled && t.color.a > 0.05f && NaTela(t.bounds)) _popupNovo = "tela escurecida pelo jogo";
                }
                catch { }
            }

            var elementos = UnityEngine.Object.FindObjectsOfType<PassiveUiElement>();
            Transform raiz = _raiz.transform;
            foreach (var el in elementos)
            {
                if (el == null) continue;
                var t = el.transform;
                if (t.IsChildOf(raiz) || DeUmaTela(t)) continue;
                bool presente = _presentesNaMontagem.Contains(el.Pointer);
                bool naFrente = (t.position.z - _zRaiz) * _f < 0f;
                if (!presente && naFrente)
                {
                    if (_popupNovo == null && AlgumColisorLigado(el) && TemAlgoNaTela(el.gameObject))
                    {
                        _popupNovo = "janela nova (" + el.name + ")";
                        Aviso("nova" + Pecas.Caminho(t), "[menu] janela do jogo na frente do menu: " + Pecas.Caminho(t) + " z " + t.position.z.ToString("0.00"));
                    }
                    continue;
                }
                if (_cobre && _visivel) Desligar(el);
            }
        }

        static bool AlgumColisorLigado(PassiveUiElement el)
        {
            try
            {
                var cs = el.Colliders;
                if (cs == null) return false;
                foreach (var c in cs) if (c != null && c.enabled) return true;
            }
            catch { }
            return false;
        }

        // A collider of the game's menu under our layer: off while we are on screen (only if our camera, or one that
        // draws before it, shows it: then it is really covered).
        void Desligar(PassiveUiElement el)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D> cs;
            try { cs = el.Colliders; } catch { return; }
            if (cs == null) return;
            if (!Coberto(el.gameObject.layer)) { Aviso("acima" + el.name, "[menu] " + el.name + " e desenhado por uma camera acima da nossa; fica clicavel"); return; }
            foreach (var c in cs)
            {
                if (c == null || !c.enabled) continue;
                c.enabled = false;
                _desligados[c.Pointer] = c;
            }
        }

        bool Coberto(int camada)
        {
            if ((_cam.cullingMask & (1 << camada)) != 0) return true;
            var outra = CameraQueDesenha(camada);
            return outra == null || outra.depth <= _cam.depth;
        }

        void Religar()
        {
            foreach (var c in _desligados.Values)
                try { if (c != null) c.enabled = true; } catch { }
            _desligados.Clear();
        }

        void Mostrar()
        {
            _visivel = true;
            _motivo = "";
            if (!_mostrouUmaVez) { _mostrouUmaVez = true; Log.LogInfo("[menu] estilo " + Estilo + " na tela"); }
            if (_raiz != null && !_raiz.activeSelf) _raiz.SetActive(true);
            // back from the inventory with another hat: dress the crewmate again (only when something changed)
            string roupa = Roupa();
            if (_boneco != null && _roupa != null && roupa != _roupa) _boneco.Atualizar();
            _roupa = roupa;
            _montadaEm = Math.Max(_montadaEm, Time.unscaledTime - 4f);   // a second of re-measuring
            Dispor();
            AtualizarSelo();
            if (_cobre) Varrer();
        }

        string _roupa;

        static string Roupa()
        {
            try
            {
                if (!DataManager.IsPlayerLoaded) return "";
                var c = DataManager.Player.Customization;
                return c.Color + "|" + c.Hat + "|" + c.Skin + "|" + c.Visor + "|" + c.Pet + "|" + c.NamePlate;
            }
            catch { return ""; }
        }

        void Esconder(string motivo)
        {
            bool mudou = _visivel || _motivo != motivo;
            _visivel = false;
            if (_motivo != motivo) _ocupadoDesde = Time.unscaledTime;
            _motivo = motivo;
            if (_raiz != null && _raiz.activeSelf) _raiz.SetActive(false);
            Religar();
            if (mudou && _mostrouUmaVez) Log.LogInfo("[menu] o jogo abriu \"" + motivo + "\": o menu do jogo aparece ate fechar");
            else if (mudou) Log.LogInfo("[menu] esperando o jogo fechar \"" + motivo + "\" pra mostrar o estilo " + Estilo);
        }

        // ---------------- the mod stamp ----------------

        // Innersloth's policy: the stamp stays visible. Its sprite is drawn again on top of our pieces, at the place
        // and size the original has on screen (the original is untouched; CriCriOtimizacao sizes it).
        void AtualizarSelo()
        {
            SpriteRenderer orig = null;
            try { if (DestroyableSingleton<ModManager>.InstanceExists) orig = DestroyableSingleton<ModManager>.Instance.ModStamp; } catch { }
            bool mostrar = false;
            try
            {
                if (orig != null && orig.enabled && orig.gameObject.activeInHierarchy && orig.sprite != null)
                {
                    var camSelo = CameraQueDesenha(orig.gameObject.layer);
                    if (camSelo != null && (camSelo.Pointer == _cam.Pointer || camSelo.depth <= _cam.depth))
                    {
                        Bounds b = orig.bounds;
                        Vector3 centro = b.center, tam = b.size;
                        if (camSelo.Pointer != _cam.Pointer)
                        {
                            Vector3 a0 = camSelo.WorldToScreenPoint(b.min), a1 = camSelo.WorldToScreenPoint(b.max);
                            float dz = Math.Abs(_zRaiz - _cam.transform.position.z);
                            Vector3 w0 = _cam.ScreenToWorldPoint(new Vector3(a0.x, a0.y, dz)), w1 = _cam.ScreenToWorldPoint(new Vector3(a1.x, a1.y, dz));
                            centro = (w0 + w1) / 2f;
                            tam = new Vector3(Math.Abs(w1.x - w0.x), Math.Abs(w1.y - w0.y), 0f);
                        }
                        // classico: only where our card is on top of it
                        mostrar = _cobre || CartaoCobre(centro, tam);
                        if (mostrar)
                        {
                            if (_selo == null)
                            {
                                var go = new GameObject("CriCriSelo");
                                go.layer = _d.Camada;
                                go.transform.SetParent(_raiz.transform, false);
                                _selo = go.AddComponent<SpriteRenderer>();
                                _selo.sortingLayerID = _d.LayerId;
                                _selo.sortingOrder = _d.Ordem + 40;
                                Log.LogInfo("[menu] selo de mod copiado por cima do estilo (" + orig.sprite.name + ")");
                            }
                            _selo.sprite = orig.sprite;
                            _selo.color = orig.color;
                            _selo.flipX = orig.flipX;
                            _selo.flipY = orig.flipY;
                            float nativo = orig.sprite.bounds.size.y;
                            float k = nativo > 0.0001f ? tam.y / nativo : 1f;
                            _selo.transform.localScale = new Vector3(k, k, 1f);
                            Vector3 local = _raiz.transform.InverseTransformPoint(new Vector3(centro.x, centro.y, _zRaiz));
                            Vector3 desvio = _selo.bounds.center - _selo.transform.position;   // pivot not centered
                            _selo.transform.localPosition = new Vector3(local.x - desvio.x, local.y - desvio.y, -0.08f * _f);
                        }
                    }
                }
            }
            catch (Exception e) { Aviso("selo" + e.GetType().Name, "[menu] selo: " + e.GetType().Name + ": " + e.Message); }
            if (_selo != null && _selo.gameObject.activeSelf != mostrar) _selo.gameObject.SetActive(mostrar);
        }

        bool CartaoCobre(Vector3 centro, Vector3 tam)
        {
            if (_placas.Count == 0 || _placas[0].Sr == null) return false;
            var b = _placas[0].Sr.bounds;
            return Math.Abs(b.center.x - centro.x) * 2f < b.size.x + tam.x && Math.Abs(b.center.y - centro.y) * 2f < b.size.y + tam.y;
        }

        // LateUpdate: the clones' own art (the game's pressed/held sprites) stays hidden under our plates.
        public void DepoisDoQuadro()
        {
            if (!_visivel) return;
            for (int i = 0; i < _botoes.Count; i++) _botoes[i].ApagarArteDoJogo();
        }

        // ---------------- undo ----------------

        // Everything back as the game had it. Never throws (also runs on a half-built style).
        public void Desmontar()
        {
            try { Religar(); } catch { }
            _pendente = null;
            try { if (_raiz != null) UnityEngine.Object.Destroy(_raiz); } catch { }
            _raiz = null;
            _visivel = false;
        }

        // ---------------- helpers ----------------

        static SpriteRenderer MaiorSprite(GameObject go)
        {
            if (go == null) return null;
            SpriteRenderer melhor = null;
            float area = 0f;
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null) continue;
                var b = sr.bounds;
                float a = b.size.x * b.size.y;
                if (a > area) { area = a; melhor = sr; }
            }
            return melhor;
        }

        // The last enabled orthographic camera (highest depth) that draws this layer.
        static Camera CameraQueDesenha(int camada)
        {
            Camera melhor = null;
            foreach (var c in Camera.allCameras)
            {
                if (c == null || !c.isActiveAndEnabled || !c.orthographic || c.targetTexture != null) continue;
                if ((c.cullingMask & (1 << camada)) == 0) continue;
                if (melhor == null || c.depth > melhor.depth) melhor = c;
            }
            return melhor;
        }

        static void Aviso(string chave, string texto)
        {
            if (_avisos.Add(chave)) Log.LogWarning(texto);
        }

        // Once per session: what this phone's menu looks like (names, places, cameras, stamp), for the next tuning.
        void LogEstrutura()
        {
            if (_logouEstrutura) return;
            _logouEstrutura = true;
            try
            {
                var m = Menu;
                var sb = new System.Text.StringBuilder("[menu] estrutura: tela " + Screen.width + "x" + Screen.height + ", area segura " + Screen.safeArea);
                foreach (var c in Camera.allCameras)
                    if (c != null) sb.Append("\n  camera ").Append(c.name).Append(" profundidade ").Append(c.depth).Append(" orto ").Append(c.orthographic ? c.orthographicSize.ToString("0.00") : "nao")
                        .Append(" mascara ").Append(c.cullingMask).Append(" pos ").Append(c.transform.position).Append(" rect ").Append(c.rect);
                try
                {
                    for (int i = 0; m.mainButtons != null && i < m.mainButtons.Count; i++)
                    {
                        var b = m.mainButtons[i];
                        if (b == null) continue;
                        var sr = MaiorSprite(b.gameObject);
                        sb.Append("\n  botao ").Append(b.name).Append(" pos ").Append(b.transform.position).Append(" ativo ").Append(b.gameObject.activeInHierarchy)
                          .Append(sr != null ? " ordem " + NomeDaCamada(sr.sortingLayerID) + "/" + sr.sortingOrder + " camada " + sr.gameObject.layer : "");
                    }
                }
                catch { }
                void Obj(string nome, GameObject go)
                {
                    if (go == null) { sb.Append("\n  ").Append(nome).Append(": -"); return; }
                    sb.Append("\n  ").Append(nome).Append(": ").Append(Pecas.Caminho(go.transform)).Append(" ativo ").Append(go.activeInHierarchy).Append(" pos ").Append(go.transform.position);
                }
                Obj("mainMenuUI", m.mainMenuUI);
                Obj("gameModeButtons", m.gameModeButtons);
                Obj("onlineButtons", m.onlineButtons);
                Obj("accountButtons", m.accountButtons);
                Obj("rightPanelMask", m.rightPanelMask);
                Obj("Online", m.PlayOnlineButton != null ? m.PlayOnlineButton.gameObject : null);
                Obj("Local", m.playLocalButton != null ? m.playLocalButton.gameObject : null);
                Obj("Freeplay", m.freePlayButton != null ? m.freePlayButton.gameObject : null);
                Obj("replay", BotaoDoReplay());
                try { Obj("screenTint", m.screenTint != null ? m.screenTint.gameObject : null); } catch { }
                try { Obj("announcementPopUp", m.announcementPopUp != null ? m.announcementPopUp.gameObject : null); } catch { }
                try
                {
                    if (DestroyableSingleton<FriendsListManager>.InstanceExists)
                    {
                        var fb = DestroyableSingleton<FriendsListManager>.Instance.FriendsListButton;
                        Obj("amizades", fb != null ? fb.Button : null);
                    }
                    else sb.Append("\n  amizades: sem FriendsListManager");
                }
                catch { }
                try
                {
                    if (DestroyableSingleton<AccountManager>.InstanceExists)
                    {
                        var am = DestroyableSingleton<AccountManager>.Instance;
                        Obj("accountTab", am.accountTab != null ? am.accountTab.gameObject : null);
                        Obj("accountWindow", am.accountWindow);
                    }
                }
                catch { }
                try
                {
                    if (DestroyableSingleton<ModManager>.InstanceExists)
                    {
                        var s = DestroyableSingleton<ModManager>.Instance.ModStamp;
                        if (s != null) sb.Append("\n  selo: ").Append(Pecas.Caminho(s.transform)).Append(" ativo ").Append(s.gameObject.activeInHierarchy)
                            .Append(" pos ").Append(s.transform.position).Append(" camada ").Append(s.gameObject.layer).Append(" ordem ").Append(NomeDaCamada(s.sortingLayerID)).Append("/").Append(s.sortingOrder);
                    }
                }
                catch { }
                try { sb.Append("\n  canvas na cena: ").Append(UnityEngine.Object.FindObjectsOfType<Canvas>().Length); } catch { }
                try
                {
                    sb.Append("\n  filhos do menu:");
                    for (int i = 0; i < m.transform.childCount; i++)
                    {
                        var f = m.transform.GetChild(i);
                        sb.Append(' ').Append(f.name).Append(f.gameObject.activeSelf ? "" : "(off)");
                    }
                }
                catch { }
                Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Log.LogWarning("[menu] estrutura: " + e.Message); }
        }
    }
}
