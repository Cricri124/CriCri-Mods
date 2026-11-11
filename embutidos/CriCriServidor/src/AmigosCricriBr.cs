#if AMIGOS_SERVIDOR_TESTE || AMIGOS_PUBLICO
#define AMIGOS_REAL
#endif
#if AMIGOS_DE_MENTIRA || AMIGOS_REAL
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using Pessoa = CriCriMods.Servidor.AmigosApi.Pessoa;
using Selo = CriCriMods.Servidor.AmigosApi.Selo;

namespace CriCriMods.Servidor
{
    // The CRICRI BR friends panel (CriCri, 08/11: "pode substituir todo o sistema de amizades do jogo, um design mais bonito e mostrando a
    // skin"). With the CRICRI BR region chosen, the game's friends panel shows our five tabs instead of its own: Amigos, Pedidos, Buscar,
    // Enviar, Bloqueados. On other regions the game's panel stays as it is. For now only in the test build (-p:DefineConstants=
    // AMIGOS_DE_MENTIRA), with fake people: the server's routes come after the v1 (contract in docs\CRICRI-LAUNCHER.md).
    //
    // Built from the game's own pieces, measured on the phone 08/11: tabs are copies of a game tab, the list a copy of the Lobby tab's content,
    // a row a copy of the game's friend row without its behaviour, the skin a copy of the account tab's PoolablePlayer. No Harmony.
    internal sealed class AmigosCricriBr : MonoBehaviour
    {
        public AmigosCricriBr(IntPtr ptr) : base(ptr) { }

        sealed class Visivel { public Transform T; }
        struct Estilo { public bool Ativo, Ligado; public Color Cor; }

        const int AMIGOS = 0, PEDIDOS = 1, BUSCAR = 2, BLOQUEADOS = 3;
        static readonly string[] NomesDasAbas = { "Amigos", "Pedidos", "Buscar", "Bloqueados" };
        static readonly string[] Titulos = { "AMIGOS CRICRI BR", "PEDIDOS", "PESQUISA", "BLOQUEADOS" };
        static readonly float[] XDasAbas = { -1.45f, -0.03f, 1.39f, 2.81f };
        static readonly string[] AbasDoJogo = { "Lobby Inactive", "Lobby Players", "Recent Players", "AUFriendsTab", "BlockedPlayersTab" };

        readonly List<Pessoa> _amigos = new List<Pessoa>(), _pedidos = new List<Pessoa>(), _bloqueados = new List<Pessoa>(), _outros = new List<Pessoa>();
        List<Pessoa> _resultados = new List<Pessoa>();

        FriendsListUI _ui;
        Transform _abas, _interno;
        GameObject _area, _popup, _editar, _busca, _moldura, _troca;
        static bool _mediuBoneco, _mediuRecorte;
        static bool _doJogo;   // CriCri, 08/11: "alterar entre amigos cricri br ou amigos do jogo, cricri br como padrao"
        Scroller _rol;
        Collider2D _mascara;
        TextMeshPro _titulo, _sub, _vazio, _dica, _textoPopup;
        TextBoxTMP _caixa;
        readonly GameObject[] _botoes = new GameObject[4];
        readonly SpriteRenderer[] _brilhos = new SpriteRenderer[4];
        readonly List<GameObject> _linhas = new List<GameObject>();
        readonly List<Innersloth.Assets.AddressableAsset<NamePlateViewData>> _banners = new List<Innersloth.Assets.AddressableAsset<NamePlateViewData>>();
        Estilo _sel, _normal;
        readonly TextMeshPro[] _rotulos = new TextMeshPro[4];
        readonly Dictionary<SpriteRenderer, KeyValuePair<Color, bool>> _pecasDoTema = new Dictionary<SpriteRenderer, KeyValuePair<Color, bool>>();
        static readonly Dictionary<string, Sprite> _selos = new Dictionary<string, Sprite>();
        static readonly Color Escuro = new Color32(23, 27, 38, 255), Cartao = new Color32(38, 44, 58, 255), Primaria = new Color32(42, 190, 160, 255), Neutra = new Color32(84, 94, 112, 255), Claro = new Color32(190, 199, 210, 255);
        int _aba;
        bool _montado, _falhou, _aqui, _painelEstava, _editando;
        string _aviso = "";
        float _avisoAte;
        readonly bool[] _leu = new bool[4];
        int _online = -1, _nPedidos;
        float _proxLeitura;
        float _conviteAte = -1f, _conviteEm = -1f;
        string _conviteDe;

        void Update()
        {
            try
            {
                TiqueConvite();
#if AMIGOS_REAL
                ConferirDono();
                TiqueLigar();
#endif
                var ui = FriendsListUI.Instance;
                if (ui == null) { Esquecer(); return; }
                if (_ui != ui) { Esquecer(); _ui = ui; }
                bool regiao = NaRegiao(), aberto = ui.IsOpen;
#if AMIGOS_REAL
                if (!_rotaNoAr)
                {
                    if (regiao && (aberto || PlacaNaTela())) Sondar();
                    return;
                }
                if (!aberto && regiao && !_doJogo) LerContagem();
#endif
                if (!_montado && aberto && regiao && !_falhou) Montar();
                if (!_montado) return;
                if (_troca != null && _troca.activeSelf != regiao) _troca.SetActive(regiao);
                bool aqui = regiao && !_doJogo;
                if (aqui != _aqui)
                {
                    Alternar(aqui);
                    if (aqui && aberto) Selecionar(AMIGOS);
                }
                if (!aqui) { _painelEstava = aberto; return; }
                if (aberto && !_painelEstava) { Selecionar(_abrirEmPedidos ? PEDIDOS : AMIGOS); _abrirEmPedidos = false; }
                _painelEstava = aberto;
                if (!aberto) return;
                EsconderAbasDoJogo();
                CortarLinhas();
                if (Time.unscaledTime >= _proxLeitura) Carregar();
                if (_dica != null)
                {
                    bool mostra = _aba == BUSCAR && !_caixa.hasFocus && string.IsNullOrEmpty(_caixa.text);
                    if (_dica.enabled != mostra) _dica.enabled = mostra;
                }
            }
            catch (Exception e)
            {
                _falhou = true;
                Plugin.Registro.LogWarning("[amigos] " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        // ---------------- the "Novos pedidos" plate next to Amizades ----------------
        // CriCri, 08/11: with the CRICRI BR list chosen it counts only CRICRI BR requests and online friends; with DO JOGO it is the game's again.
        string _placaDoJogo, _placaEscrita, _etagContagem;
        bool _placaNossa, _placaFalhou, _abrirEmPedidos;
        float _proxContagem;

        void LateUpdate()
        {
            if (_placaFalhou) return;
            try { Placa(); }
            catch (Exception e)
            {
                _placaFalhou = true;
                Plugin.Registro.LogWarning("[amigos] placa de pedidos: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // The plate is a button with one text per state (NewRequestInactive, and the others): measured on the phone 08/11.
        readonly List<TextMeshPro> _textosDaPlaca = new List<TextMeshPro>();
        float _proxBuscaDaPlaca;

        bool PlacaAchada => _textosDaPlaca.Count > 0 && _textosDaPlaca[0] != null;

        bool PlacaNaTela()
        {
            if (!PlacaAchada) return false;
            foreach (var t in _textosDaPlaca) if (t != null && t.gameObject.activeInHierarchy) return true;
            return false;
        }

        void AcharPlaca()
        {
            if (PlacaAchada || Time.unscaledTime < _proxBuscaDaPlaca || ShipStatus.Instance != null) return;
            _proxBuscaDaPlaca = Time.unscaledTime + 1f;
            _textosDaPlaca.Clear();
            _placaEscrita = null;
            foreach (var x in FindObjectsOfType<TextMeshPro>())
            {
                var pai = x.transform.parent;
                if (pai == null || pai.parent == null || !pai.name.StartsWith("NewRequest")) continue;
                // one copy of the text per button state (Inactive, Active, Selected), each under its own NewRequest... piece
                var botao = pai.GetComponentInParent<PassiveButton>();
                foreach (var t in (botao != null ? botao.transform : pai.parent).GetComponentsInChildren<TextMeshPro>(true))
                    if (t.transform.parent != null && t.transform.parent.name.StartsWith("NewRequest")) _textosDaPlaca.Add(t);
                if (botao != null) botao.OnClick.AddListener((Action)(() => { if (_placaNossa) _abrirEmPedidos = true; }));
                Plugin.Registro.LogInfo("[amigos] placa achada: " + _textosDaPlaca.Count + " textos, em " + pai.parent.name + ", botao=" + (botao != null));
                return;
            }
        }

        void Placa()
        {
            AcharPlaca();
            if (!PlacaAchada) return;
            var primeiro = _textosDaPlaca[0];
            if (primeiro.text != _placaEscrita) _placaDoJogo = primeiro.text;
#if AMIGOS_REAL
            bool nossa = _rotaNoAr && NaRegiao() && !_doJogo;
#else
            bool nossa = NaRegiao() && !_doJogo;
            if (_online < 0) { _online = 5; _nPedidos = 2; }
#endif
            if (nossa)
            {
                string quero = "Pedidos: " + _nPedidos + "   Online: " + Math.Max(_online, 0);
                foreach (var t in _textosDaPlaca) if (t.text != quero) t.text = quero;
                _placaEscrita = quero;
                _placaNossa = true;
            }
            else if (_placaNossa)
            {
                _placaNossa = false;
                _placaEscrita = null;
                foreach (var t in _textosDaPlaca) t.text = _placaDoJogo;
            }
        }

#if AMIGOS_REAL
        // The panel is closed but the plate is on screen: only the two numbers are read, as often as the server says.
        void LerContagem()
        {
            if (Time.unscaledTime < _proxContagem || !PlacaNaTela()) return;
            _proxContagem = float.MaxValue;
            AmigosApi.Amigos(_etagContagem, r =>
            {
                _proxContagem = Time.unscaledTime + Math.Max(r.ProximaEmSeg, 30);
                if (r.Codigo != 200) return;
                var l = AmigosApi.LerLista(r.Corpo, "amigos");
                _nPedidos = l.Pedidos;
                _online = l.Online;
                _etagContagem = r.Etag;
            });
        }
#endif

        void Esquecer()
        {
            _ui = null; _montado = false; _falhou = false; _aqui = false; _painelEstava = false; _editando = false;
            _abas = null; _interno = null; _area = null; _rol = null;
            _linhas.Clear();
            _pecasDoTema.Clear();
        }

        static bool NaRegiao()
        {
            if (!DestroyableSingleton<ServerManager>.InstanceExists) return false;
            var r = DestroyableSingleton<ServerManager>.Instance.CurrentRegion;
            return r != null && r.Name == Regiao.Nome;
        }

        // ---------------- building the panel ----------------

        void Montar()
        {
            var ui = _ui;
            _abas = ui.transform.Find("Menu/Tabs");
            var modelo = _abas != null ? _abas.Find("AUFriendsTab") : null;
            var conteudo = ui.Tabs[0].tabObject;
            if (modelo == null || conteudo == null) { Falhar("painel sem as pecas esperadas"); return; }
            int sel = ui.selectedTab, outra = -1;
            for (int i = 0; i < ui.Tabs.Length; i++)
                if (i != sel && ui.Tabs[i].selectable && ui.Tabs[i].selectedSprites != null && ui.Tabs[i].selectedSprites.Count >= 2) outra = i;
            if (sel < 0 || sel >= ui.Tabs.Length || outra < 0 || ui.Tabs[sel].selectedSprites == null || ui.Tabs[sel].selectedSprites.Count < 2) { Falhar("sem aba do jogo pra copiar o estilo"); return; }
            _sel = Ler(ui.Tabs[sel].selectedSprites[1]);
            _normal = Ler(ui.Tabs[outra].selectedSprites[1]);

            // the list: a copy of the Lobby tab's content, without its rows
            _area = Instantiate(conteudo, conteudo.transform.parent);
            _area.name = "CriCriBrAmigos";
            _area.SetActive(true);
            _rol = _area.GetComponentInChildren<Scroller>(true);
            _interno = _rol.Inner;
            for (int i = _interno.childCount - 1; i >= 0; i--) DestroyImmediate(_interno.GetChild(i).gameObject);
            var mascara = _area.transform.Find("MaskArea");
            _mascara = mascara != null ? mascara.GetComponent<BoxCollider2D>() : null;
            var titulo = _area.transform.Find("Title");
            foreach (var t in titulo.GetComponents<TextTranslatorTMP>()) DestroyImmediate(t);
            _titulo = titulo.GetComponent<TextMeshPro>();

            for (int i = 0; i < XDasAbas.Length; i++) MontarAba(i, modelo.gameObject, titulo.gameObject);

            _sub = TextoNovo(titulo.gameObject, "CriCriBrSub", new Vector3(2.03f, 0.02f, -10f), 0.5f);
            _vazio = TextoNovo(titulo.gameObject, "CriCriBrVazio", new Vector3(2.03f, -1.2f, -10f), 0.5f);
            _vazio.color = new Color32(70, 76, 84, 255);

            // the search box: a copy of the game's "add a friend" box
            var original = ui.AddFriendArea;
            var caixa = Instantiate(original.gameObject, _area.transform);
            caixa.name = "CriCriBrCaixa";
            Vector3 a = original.transform.lossyScale, b = _area.transform.lossyScale;
            caixa.transform.localScale = new Vector3(a.x / b.x, a.y / b.y, 1f);
            caixa.transform.localPosition = new Vector3(1.36f, 0.02f, -31f);
            caixa.SetActive(true);
            _caixa = caixa.GetComponent<TextBoxTMP>();
            PosicionarCaixa(caixa);
            _caixa.OnEnter = new UnityEngine.UI.Button.ButtonClickedEvent();
            _caixa.OnChange = new UnityEngine.UI.Button.ButtonClickedEvent();
            _caixa.OnFocusLost = new UnityEngine.UI.Button.ButtonClickedEvent();
            _caixa.OnEnter.AddListener((Action)AcaoDaCaixa);
            _caixa.characterLimit = 20;
            _caixa.AllowSymbols = true;
            _caixa.allowAllCharacters = true;
            foreach (var t in caixa.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(t);

            _busca = BotaoTexto(_area.transform, "BUSCAR", new Vector3(3.72f, 0.02f, -30f), 0.62f, 1.1f, AcaoDaCaixa);
            _editar = BotaoTexto(_area.transform, "EDITAR", new Vector3(3.72f, 0.02f, -30f), 0.62f, 1.1f, () => { _editando = !_editando; Desenhar(); }, false);

            // the switch between our list and the game's, on the phone's top bar, the same place in both
            _troca = BotaoTexto(ui.transform.Find("Menu"), "DO JOGO", new Vector3(-2.95f, 3.42f, 45f), 0.5f, 0.8f, () => _doJogo = !_doJogo, false);
            if (_troca != null)
            {
                _troca.name = "CriCriBrTroca";
                var pb = _troca.GetComponent<PassiveButton>();
                if (pb != null) pb.ClickMask = null;   // outside the list's mask
            }

#if !AMIGOS_REAL
            CriarMentira();
#endif
            _montado = true;
            _aqui = false;
            Alternar(true);
            Plugin.Registro.LogInfo("[amigos] painel CRICRI BR montado; area da lista " + (_mascara != null ? _mascara.bounds.ToString() : "?"));
        }

        void Falhar(string motivo)
        {
            _falhou = true;
            Plugin.Registro.LogWarning("[amigos] " + motivo);
        }

        void MontarAba(int i, GameObject modelo, GameObject titulo)
        {
            var go = Instantiate(modelo, _abas);
            go.name = "CriCriBrTab" + i;
            go.SetActive(true);
            var p = go.transform.localPosition;
            p.x = XDasAbas[i];
            go.transform.localPosition = p;
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.name == "Gradient") _brilhos[i] = sr;   // the tab's shape; "Tab Background" is the game's friends icon
                else if (sr.name == "Tab Background") sr.enabled = false;
                else sr.gameObject.SetActive(false);
            }
            var pb = go.GetComponentInChildren<PassiveButton>(true);
            int indice = i;
            if (pb != null)
            {
                pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                pb.OnClick.AddListener((Action)(() => Selecionar(indice)));
            }
            var rotulo = Instantiate(titulo, go.transform);
            rotulo.name = "CriCriBrRotulo";
            rotulo.transform.localPosition = new Vector3(0f, 0f, -12f);
            rotulo.transform.localScale = rotulo.transform.localScale * (NomesDasAbas[i].Length > 7 ? 2.3f : 3f);   // phone 08/11: at 1 it is tiny inside the tab
            var r = rotulo.GetComponent<TextMeshPro>();
            r.text = NomesDasAbas[i];
            r.alignment = TextAlignmentOptions.Center;
            r.enableWordWrapping = false;
            r.enableAutoSizing = false;
            _rotulos[i] = r;
            var material = r.fontMaterial;   // the tab row is outside the list's mask
            if (material.HasProperty("_StencilComp")) material.SetFloat("_StencilComp", 8f);
            if (material.HasProperty("_Stencil")) material.SetFloat("_Stencil", 0f);
            if (_brilhos[i] != null)
            {
                var pintor = rotulo.GetComponent<MeshRenderer>();
                pintor.sortingLayerID = _brilhos[i].sortingLayerID;
                pintor.sortingOrder = _brilhos[i].sortingOrder + 5;
            }
            _botoes[i] = go;
        }

        // The game's box has no background of its own (phone 08/11): its pieces are centred on the place we want, with a frame behind them.
        void PosicionarCaixa(GameObject caixa)
        {
            Arvore(caixa.transform, 0);
            var alvo = _area.transform.TransformPoint(1.36f, 0.02f, -12f);
            // phone 08/11: the box's text area came with the game's own anchor and the typed text landed in the corner
            var area = caixa.GetComponent<RectTransform>();
            area.pivot = new Vector2(0.5f, 0.5f);
            area.sizeDelta = new Vector2(3.2f, 0.5f);
            caixa.transform.position = new Vector3(alvo.x, alvo.y, caixa.transform.position.z);
            foreach (var t in new[] { _caixa.outputText, _caixa.placeholderText })
            {
                if (t == null) continue;
                t.transform.position = new Vector3(alvo.x, alvo.y, t.transform.position.z);
                t.alignment = TextAlignmentOptions.Center;
                t.color = t == _caixa.placeholderText ? (Color)new Color32(136, 147, 160, 255) : Color.white;
                var m = t.fontMaterial;
                if (m.HasProperty("_StencilComp")) m.SetFloat("_StencilComp", 8f);
                if (m.HasProperty("_Stencil")) m.SetFloat("_Stencil", 0f);
            }
            _dica = TextoNovo(_titulo.gameObject, "CriCriBrDica", new Vector3(1.36f, 0.02f, -31.5f), 0.42f);
            _dica.text = "Nick ou nome#1234";
            _dica.color = new Color32(136, 147, 160, 255);
            var moldura = BotaoTexto(_area.transform, "", new Vector3(1.36f, 0.02f, -30f), 1.9f, 1.1f, FocarCaixa);
            if (moldura != null)
            {
                moldura.name = "CriCriBrMoldura";
                foreach (var sr in moldura.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = Cartao;
                // the frame is the tap area: the box's own stays with the game's original, and the copied button is known to work
                var toque = moldura.GetComponent<BoxCollider2D>();
                var lista = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider2D>(1);
                lista[0] = toque;
                _caixa.colliders = lista;
                var raiz = caixa.GetComponent<PassiveButton>();
                if (raiz != null) raiz.enabled = false;
                _moldura = moldura;
            }
        }

        void FocarCaixa()
        {
            Plugin.Registro.LogInfo("[amigos] toque na caixa");
            _caixa.GiveFocus();
            Plugin.Registro.LogInfo("[amigos] caixa com foco=" + _caixa.hasFocus + " teclado=" + (_caixa.keyboard != null));
        }

        static void Medir(string quem, Renderer r)
        {
            if (r == null) { Plugin.Registro.LogInfo("[amigos] material " + quem + ": sem"); return; }
            var m = r.material;
            string v = "";
            foreach (var p in new[] { "_Stencil", "_StencilComp", "_StencilOp", "_MaskLayer", "_MaskComp", "_MaskType", "_ColorMask" })
                if (m.HasProperty(p)) v += " " + p + "=" + m.GetFloat(p);
            Plugin.Registro.LogInfo("[amigos] material " + quem + ": " + m.shader.name + " fila=" + m.renderQueue + v + " tipo=" + r.GetIl2CppType().Name);
        }

        static void Arvore(Transform t, int nivel)
        {
            var nomes = new List<string>();
            foreach (var c in t.GetComponents<Component>()) { string n = c.GetIl2CppType().Name; if (n != "Transform") nomes.Add(n); }
            var p = t.localPosition;
            Plugin.Registro.LogInfo("[amigos] caixa " + new string(' ', nivel * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)") + " @" + p.x.ToString("0.00") + "," + p.y.ToString("0.00") + " [" + string.Join(",", nomes) + "]");
            if (nivel < 3) for (int i = 0; i < t.childCount; i++) Arvore(t.GetChild(i), nivel + 1);
        }

        TextMeshPro TextoNovo(GameObject titulo, string nome, Vector3 pos, float escala)
        {
            var go = Instantiate(titulo, _area.transform);
            go.name = nome;
            go.transform.localPosition = pos;
            go.transform.localScale = go.transform.localScale * escala;
            var t = go.GetComponent<TextMeshPro>();
            t.text = "";
            return t;
        }

        void Alternar(bool aqui)
        {
            _aqui = aqui;
            foreach (var b in _botoes) if (b != null) b.SetActive(aqui);
            Tema(aqui);
            if (_troca != null) foreach (var t in _troca.GetComponentsInChildren<TextMeshPro>(true)) t.text = aqui ? "DO JOGO" : "CRICRI BR";
            if (_area != null) _area.SetActive(aqui);
            if (aqui) return;
            foreach (var n in AbasDoJogo)   // back on another region: the game's panel again
            {
                if (n == "Lobby Players") continue;   // the game decides this one
                var t = _abas.Find(n);
                if (t != null) t.gameObject.SetActive(true);
            }
            _ui.OpenTab(_ui.selectedTab);
        }

        // Our dark look over the game's light phone. Only while the CRICRI BR panel is on: other regions get the game's own colours back.
        void Tema(bool ligado)
        {
            if (_pecasDoTema.Count == 0)
                foreach (var nome in new[] { "Menu/PhoneUI/baseGlass", "Menu/PhoneUI/White Grade", "Menu/PhoneUI/Header Grade" })
                {
                    var t = _ui.transform.Find(nome);
                    var sr = t != null ? t.GetComponent<SpriteRenderer>() : null;
                    if (sr != null) _pecasDoTema[sr] = new KeyValuePair<Color, bool>(sr.color, sr.enabled);
                }
            foreach (var par in _pecasDoTema)
            {
                if (par.Key == null) continue;
                bool fundo = par.Key.name == "baseGlass";
                par.Key.color = ligado ? (fundo ? Escuro : Cartao) : par.Value.Key;
                par.Key.enabled = ligado ? par.Key.name != "White Grade" : par.Value.Value;
            }
        }

        void EsconderAbasDoJogo()
        {
            foreach (var n in AbasDoJogo)
            {
                var t = _abas.Find(n);
                if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
            }
            foreach (var a in _ui.Tabs) if (a.tabObject != null && a.tabObject.activeSelf) a.tabObject.SetActive(false);
        }

        static Estilo Ler(SpriteRenderer s) => new Estilo { Ativo = s.gameObject.activeSelf, Ligado = s.enabled, Cor = s.color };

        static void Pintar(SpriteRenderer s, Estilo e)
        {
            if (s == null) return;
            if (s.gameObject.activeSelf != e.Ativo) s.gameObject.SetActive(e.Ativo);
            s.enabled = e.Ligado;
            s.color = e.Cor;
        }

        // ---------------- tabs and rows ----------------

        void Selecionar(int aba)
        {
            _aba = aba;
            _editando = false;
            _aviso = "";
            for (int i = 0; i < XDasAbas.Length; i++)
            {
                Pintar(_brilhos[i], i == aba ? _sel : _normal);
                if (_rotulos[i] != null) _rotulos[i].color = i == aba ? (Color)new Color32(32, 44, 52, 255) : Claro;
            }
            bool comCaixa = aba == BUSCAR;
            _caixa.gameObject.SetActive(comCaixa);
            if (_moldura != null) _moldura.SetActive(comCaixa);
            _busca.SetActive(comCaixa);
            _editar.SetActive(aba == AMIGOS);
            _caixa.SetText("", "");
            if (_caixa.placeholderText != null) _caixa.placeholderText.text = "Nick ou nome#1234";
            if (aba == BUSCAR) _resultados = new List<Pessoa>();
            _proxLeitura = 0f;
            Desenhar();
        }

        void Desenhar()
        {
            foreach (var l in _linhas) if (l != null) Destroy(l);
            _linhas.Clear();
            foreach (var b in _banners) try { b.Unload(); } catch { }
            _banners.Clear();
            _titulo.text = Titulos[_aba];
#if !AMIGOS_REAL
            _nPedidos = _pedidos.Count;
#endif
            if (_rotulos[PEDIDOS] != null) _rotulos[PEDIDOS].text = _nPedidos > 0 ? "Pedidos " + (_nPedidos > 9 ? "9+" : _nPedidos.ToString()) : "Pedidos";
            _titulo.color = Color.white;
            _sub.color = Claro;
            _vazio.color = Claro;
            foreach (var t in _editar.GetComponentsInChildren<TextMeshPro>(true)) t.text = _editando ? "PRONTO" : "EDITAR";
            int online = _online >= 0 ? _online : _amigos.Count(a => a.Estado != "offline");
            string sub = "", vazio = "";
            List<Pessoa> lista;
            switch (_aba)
            {
                case AMIGOS: lista = _amigos; sub = online + " online agora"; vazio = "Você ainda não tem amigos aqui."; break;
                case PEDIDOS: lista = _pedidos; sub = online + " amigos online agora · " + _pedidos.Count + (_pedidos.Count == 1 ? " pedido" : " pedidos"); vazio = "Nenhum pedido novo."; break;
                case BUSCAR: lista = _resultados; vazio = _aviso != "" ? _aviso : "Digite o nick ou o código da pessoa."; break;
                case BLOQUEADOS: lista = _bloqueados; vazio = "Ninguém bloqueado."; break;
                default: lista = new List<Pessoa>(); break;
            }
            if (_aba != BUSCAR && _aviso != "") sub = _aviso;
#if AMIGOS_REAL
            if (_aba != BUSCAR && !_leu[_aba]) vazio = "Carregando...";
#endif
            _sub.text = sub;
            _vazio.text = lista.Count == 0 ? vazio : "";
            for (int i = 0; i < lista.Count; i++) _linhas.Add(Linha(lista[i], i));
            _rol.ContentYBounds.min = 0f;
            _rol.ContentYBounds.max = Mathf.Max(0f, (lista.Count - 4) * _ui.YOffset);
            _rol.ScrollToTop();
        }

        void Avisar(string texto)
        {
            texto = texto.Replace("<", "‹").Replace("\\", "/").Replace("[", "(");   // it may carry a person's name
            if (!_montado || _area == null || !_area.activeInHierarchy)
            {
                if (DestroyableSingleton<DisconnectPopup>.InstanceExists) DestroyableSingleton<DisconnectPopup>.Instance.ShowCustom(texto);
                return;
            }
            _aviso = texto;
            _avisoAte = Time.unscaledTime + 3f;
            Desenhar();
        }

        GameObject Linha(Pessoa a, int i)
        {
            var barra = Instantiate(_ui.OnlineFriendBar, _interno);
            var go = barra.gameObject;
            go.name = "CriCriBrLinha" + i;
            var nome = barra.SenderName;
            var convidar = barra.LobbyInviteButton;
            DestroyImmediate(barra);   // the game's behaviour would bind this row to a game friend
            foreach (var h in go.GetComponents<UIScrollbarHelper>()) DestroyImmediate(h);
            go.SetActive(true);
            var p = go.transform.localPosition;
            // the copied Lobby list sits left of the friends list: centred between the phone's left edge and the scrollbar
            go.transform.localPosition = new Vector3(p.x + 0.63f, _ui.YStart - i * _ui.YOffset, p.z);

            Ligar(go, "PlatformBubbleProfileButton", false);
            Ligar(go, "StatusSprite", false);
            var fundo = go.transform.Find("Background").GetComponent<SpriteRenderer>();
            Ligar(go, "Inactive", false);
            fundo.color = Cartao;
            if (a.Banner != "" && !a.Banner.Contains("NoPlate")) CarregarBanner(fundo, a.Banner);
            nome.color = Color.white;
            nome.outlineWidth = 0.18f;   // readable over any banner, like the name in a meeting
            nome.outlineColor = new Color32(0, 0, 0, 255);
            var remover = go.transform.Find("Remove");
            var bloquear = go.transform.Find("Block");
            remover.gameObject.SetActive(false);
            bloquear.gameObject.SetActive(false);
            convidar.gameObject.SetActive(false);

            string linha2 = LinhaDois(a);
            nome.text = a.Nome.Replace("<", "‹").Replace("\\", "/").Replace("[", "(") + (linha2 != "" ? "\n<size=70%><color=#" + ColorUtility.ToHtmlStringRGB(CorDoTexto(a)) + ">" + linha2 + "</color></size>" : "");
            var n = nome.transform.localPosition;
            nome.transform.localPosition = new Vector3(n.x + 0.1f, n.y, n.z);
            Boneco(go.transform, a);
            if (a.Selo != null) DesenharSelo(go.transform, nome, a);
            if (a.Nivel.HasValue) DesenharNivel(go.transform, nome, a.Nivel.Value);

            switch (_aba)
            {
                case AMIGOS:
                    if (_editando)
                    {
                        Mostrar(remover, 2.12f, () => AcaoRemover(a));
                        Mostrar(bloquear, 2.86f, () => AcaoBloquear(a));
                    }
                    else
                    {
                        if (a.PodeEntrar) BotaoTexto(go.transform, "ENTRAR", new Vector3(1.75f, 0f, -1f), 0.7f, 1.15f, () => Entrar(a));
                        if (a.PodeConvidar) Mostrar(convidar.transform, 2.86f, () => Convidar(a));
                    }
                    break;
                case PEDIDOS:
                    BotaoTexto(go.transform, "ACEITAR", new Vector3(1.55f, 0f, -1f), 0.62f, 1.15f, () => AcaoAceitar(a));
                    BotaoTexto(go.transform, "RECUSAR", new Vector3(2.62f, 0f, -1f), 0.62f, 1.15f, () => AcaoRecusar(a), false);
                    break;
                case BUSCAR:
                    if (a.PodePedir) BotaoTexto(go.transform, "PEDIR", new Vector3(2.4f, 0f, -1f), 0.7f, 1.15f, () => AcaoPedir(a));
                    else if (a.Situacao == "recebido") BotaoTexto(go.transform, "ACEITAR", new Vector3(2.4f, 0f, -1f), 0.7f, 1.15f, () => AcaoAceitar(a));
                    break;
                case BLOQUEADOS:
                    BotaoTexto(go.transform, "DESBLOQUEAR", new Vector3(2.25f, 0f, -1f), 1.0f, 1.15f, () => AcaoDesbloquear(a), false);
                    break;
            }
            return go;
        }

        string LinhaDois(Pessoa a)
        {
            if (_aba == AMIGOS) return TextoDoEstado(a.Estado);
            if (_aba == BUSCAR)
                return a.Situacao == "amigo" ? "Já é seu amigo" : a.Situacao == "enviado" ? "Pedido enviado" : a.Situacao == "recebido" ? "Te mandou um pedido" : "";
            return "";
        }

        static void Ligar(GameObject linha, string nome, bool ligado)
        {
            var t = linha.transform.Find(nome);
            if (t != null) t.gameObject.SetActive(ligado);
        }

        void Mostrar(Transform botao, float x, Action acao)
        {
            botao.gameObject.SetActive(true);
            var p = botao.localPosition;
            botao.localPosition = new Vector3(x, p.y, p.z);
            Ligar(botao.GetComponent<PassiveButton>(), acao);
        }

        void Ligar(PassiveButton b, Action acao)
        {
            if (b == null) return;
            b.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            b.OnClick.AddListener(acao);
            if (_mascara != null) b.ClickMask = _mascara;
        }

        // A text button: a copy of the "Amigos Among Us" button of the game's friends tab. Scaled unevenly, so the text is scaled back.
        GameObject BotaoTexto(Transform pai, string texto, Vector3 pos, float largura, float altura, Action acao, bool principal = true)
        {
            var modelo = _ui.transform.Find("Menu/Tab Contents/AU Friends/All Friends");
            if (modelo == null) return null;
            var go = Instantiate(modelo.gameObject, pai);
            go.name = "CriCriBr" + texto;
            go.SetActive(true);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(largura, altura, 1f);
            foreach (var t in go.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(t);
            foreach (var t in go.GetComponentsInChildren<TextMeshPro>(true))
            {
                t.text = texto;
                t.transform.localScale = new Vector3(0.875f / largura, 0.875f / altura, 1f);   // the same letter size on every button
                t.color = Color.white;
            }
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = principal ? Primaria : Neutra;
            var pb = go.GetComponent<PassiveButton>();
            if (pb != null) Ligar(pb, acao);
            return go;
        }

        // The person's skin: a copy of the account tab's player, dressed from the ids the server keeps.
        void Boneco(Transform linha, Pessoa a)
        {
            if (!DestroyableSingleton<AccountManager>.InstanceExists) return;
            var tab = DestroyableSingleton<AccountManager>.Instance.accountTab;
            if (tab == null || tab.playerImage == null) return;
            var p = Instantiate(tab.playerImage, linha);
            p.name = "CriCriBrBoneco";
            p.gameObject.SetActive(true);
            p.transform.localPosition = new Vector3(-0.95f, -0.28f, -3f);
            p.transform.localScale = Vector3.one * 0.5f;   // only the top half shows, like a meeting card: RecortarLinha cuts the rest
            var roupa = new NetworkedPlayerInfo.PlayerOutfit
            {
                ColorId = Mathf.Clamp(a.Cor, 0, Palette.PlayerColors.Length - 1),
                HatId = a.Chapeu != "" ? a.Chapeu : "hat_NoHat",
                SkinId = a.Roupa != "" ? a.Roupa : "skin_None",
                VisorId = a.Visor != "" ? a.Visor : "visor_EmptyVisor",
                PetId = "pet_EmptyPet",
                NamePlateId = "nameplate_NoPlate",
            };
            p.UpdateFromPlayerOutfit(roupa, PlayerMaterial.MaskType.ScrollingUI, false, false, null, false);
            p.SetName("");
            var camada = p.GetComponentInChildren<CosmeticsLayer>(true);
            if (camada != null)
            {
                camada.SetPetVisible(false);
                camada.SetMaskType(PlayerMaterial.MaskType.ScrollingUI);   // phone 08/11: with None the copy only showed outside the list's mask
            }
            p.SetMaskLayer(RecortarLinha(linha));
            // phone 08/11: drawn behind the row's white background
            var fundo = linha.Find("Background").GetComponent<SpriteRenderer>();
            foreach (var r in p.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = linha.gameObject.layer;
                r.sortingLayerID = fundo.sortingLayerID;
                r.sortingOrder = fundo.sortingOrder + 11;
            }
            if (!_mediuBoneco)
            {
                _mediuBoneco = true;
                Medir("fundo da linha", fundo);
                var mascaraDoPainel = _ui.transform.Find("Menu/Tab Contents/FriendsListMask");
                if (mascaraDoPainel != null) Medir("mascara do painel", mascaraDoPainel.GetComponent<Renderer>());
                foreach (var r in p.GetComponentsInChildren<Renderer>(false)) if (r.name == "Normal") Medir("corpo", r);
                Plugin.Registro.LogInfo("[amigos] boneco: ativo=" + p.gameObject.activeInHierarchy + " pos=" + p.transform.position + " camada=" + p.gameObject.layer + " linha=" + linha.gameObject.layer);
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                {
                    var sp = r.TryCast<SpriteRenderer>();
                    Plugin.Registro.LogInfo("[amigos] boneco peca " + r.name + " ativo=" + r.gameObject.activeInHierarchy + " ligado=" + r.enabled + " ordem=" + r.sortingOrder + " z=" + r.transform.position.z.ToString("0.0") + (sp != null ? " sprite=" + (sp.sprite != null ? sp.sprite.name : "NENHUM") + " cor=" + sp.color + " mascara=" + sp.maskInteraction : "") + " tamanho=" + r.bounds.size);
                }
            }
        }

        // The person's banner behind the row, loaded the way the game loads a nameplate. The row keeps its plain colour until it arrives.
        // Each row gets its own mask, the same way the game cuts players in its lists: a blank sprite with Unlit/MaskShader writes this layer
        // over the row, and the skin only draws where the layer matches. Measured on the phone 08/11: the panel's list uses layer 51.
        // Rows next to each other need different layers, or a skin also draws inside the row below it (phone 08/11).
        const int CamadaDoBoneco = 60, CamadaDaLista = 51, RecortesDiferentes = 8;
        readonly Material[] _recortes = new Material[RecortesDiferentes];
        int _recorteDaVez;

        // Returns the layer the row's skin has to use.
        int RecortarLinha(Transform linha)
        {
            var fundo = linha.Find("Background").GetComponent<SpriteRenderer>();
            var modelo = _area.transform.Find("MaskArea");
            if (modelo == null) return CamadaDaLista;
            var go = Instantiate(modelo.gameObject, linha);
            go.name = "CriCriBrRecorte";
            foreach (var c in go.GetComponents<BoxCollider2D>()) DestroyImmediate(c);
            go.SetActive(true);
            // MaskShader writes depth too (read from the game's shader): anything of the row drawn after it and farther away would vanish, so it
            // sits right in front of the banner and behind everything else on the row
            var f = fundo.transform.localPosition;
            go.transform.localPosition = new Vector3(f.x, f.y, f.z - 0.02f);
            var sr = go.GetComponent<SpriteRenderer>();
            Vector3 quero = fundo.bounds.size, tem = sr.bounds.size, e = go.transform.localScale;
            go.transform.localScale = new Vector3(e.x * quero.x / tem.x, e.y * quero.y / tem.y, e.z);
            int vez = _recorteDaVez;
            _recorteDaVez = (vez + 1) % RecortesDiferentes;
            int camada = CamadaDoBoneco + vez;
            if (_recortes[vez] == null)
            {
                _recortes[vez] = sr.material;
                PlayerMaterial.SetMaskLayer(_recortes[vez], camada);   // the same call the game makes for a meeting card
                // The shader writes only where its _MaskComp passes against what is already there. Greater (5): 60-67 beat the list's 51, and lose
                // to the 200 of the panel around it, so the row's cut never spills out of the list.
                _recortes[vez].SetFloat("_MaskComp", 5f);
            }
            sr.sharedMaterial = _recortes[vez];
            if (!_mediuRecorte) { _mediuRecorte = true; Medir("recorte da linha", sr); Plugin.Registro.LogInfo("[amigos] recorte em " + sr.bounds + " fundo " + fundo.bounds + " ligado=" + sr.enabled + " ativo=" + go.activeInHierarchy + " ordem=" + sr.sortingOrder); }
            sr.sortingLayerID = fundo.sortingLayerID;
            sr.sortingOrder = fundo.sortingOrder + 5;
            return camada;
        }

        void CarregarBanner(SpriteRenderer fundo, string id)
        {
            var dado = DestroyableSingleton<HatManager>.Instance.GetNamePlateById(id);
            if (dado == null) return;
            var asset = dado.CreateAddressableAsset();
            _banners.Add(asset);
            asset.LoadAsync((Action)(() =>
            {
                if (fundo == null) return;
                var v = asset.GetAsset();
                if (v == null || v.Image == null) return;
                fundo.sprite = v.Image;
                fundo.color = new Color(0.5f, 0.5f, 0.5f, 1f);   // darker, so white text reads on light banners
            }));
        }

        // The CRICRI BR level: a bubble over the skin, like the level in a meeting card.
        void DesenharNivel(Transform linha, TextMeshPro nome, int nivel)
        {
            var fundo = linha.Find("Background").GetComponent<SpriteRenderer>();
            var go = new GameObject("CriCriBrNivel");
            go.layer = linha.gameObject.layer;
            go.transform.SetParent(linha, false);
            go.transform.localPosition = new Vector3(-1.36f, 0.23f, -4f);
            go.transform.localScale = Vector3.one * 0.36f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteDoSelo(new Selo { Nivel = "disco", Bola = "#1F9D62", V = "#1F9D62" });
            sr.sortingLayerID = fundo.sortingLayerID;
            sr.sortingOrder = fundo.sortingOrder + 14;
            var v = Instantiate(nome.gameObject, linha);
            v.name = "CriCriBrNivelTexto";
            v.transform.localPosition = new Vector3(-1.36f, 0.23f, -5f);
            var t = v.GetComponent<TextMeshPro>();
            t.text = nivel.ToString();
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize *= 0.62f;
            t.color = Color.white;
            var pintor = v.GetComponent<MeshRenderer>();
            pintor.sortingLayerID = fundo.sortingLayerID;
            pintor.sortingOrder = fundo.sortingOrder + 15;
        }

        // The creator's seal, before the name: a disc in the creator's colour with a drawn check (two for "ouro"). Drawn here because the
        // game's fonts have no check mark (phone 08/11: it came out as a letter V).
        void DesenharSelo(Transform linha, TextMeshPro nome, Pessoa a)
        {
            float largura = nome.GetPreferredValues(a.Nome.Replace("<", "‹").Replace("\\", "/").Replace("[", "(")).x;
            var n = nome.transform.localPosition;
            var go = new GameObject("CriCriBrSelo");
            go.layer = linha.gameObject.layer;
            go.transform.SetParent(linha, false);
            go.transform.localPosition = new Vector3(n.x - largura / 2f - 0.22f, n.y + 0.14f, -2f);
            go.transform.localScale = Vector3.one * 0.3f;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = SpriteDoSelo(a.Selo);
            var fundo = linha.Find("Background").GetComponent<SpriteRenderer>();
            sr.sortingLayerID = fundo.sortingLayerID;
            sr.sortingOrder = fundo.sortingOrder + 12;
        }

        static Sprite SpriteDoSelo(Selo selo)
        {
            string chave = selo.Nivel + selo.Bola + selo.V;
            if (_selos.TryGetValue(chave, out var pronto) && pronto != null) return pronto;
            const int N = 96;
            ColorUtility.TryParseHtmlString(selo.Bola, out var bola);
            ColorUtility.TryParseHtmlString(selo.V, out var marca);
            var desvios = selo.Nivel == "ouro" ? new[] { -0.1f, 0.1f } : new[] { 0f };
            var tx = new Texture2D(N, N, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
                    float borda = Mathf.Clamp01((0.5f - Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f))) * N);
                    float tinta = 0f;
                    foreach (float d in desvios)
                    {
                        float dist = Mathf.Min(Segmento(u, v, 0.27f + d, 0.52f, 0.44f + d, 0.33f), Segmento(u, v, 0.44f + d, 0.33f, 0.74f + d, 0.68f));
                        tinta = Mathf.Max(tinta, Mathf.Clamp01((0.07f - dist) * N));
                    }
                    var c = Color.Lerp(bola, marca, tinta);
                    c.a = borda;
                    tx.SetPixel(x, y, c);
                }
            tx.Apply();
            var sp = Sprite.Create(tx, new Rect(0f, 0f, N, N), new Vector2(0.5f, 0.5f), 100f);
            sp.hideFlags = HideFlags.HideAndDontSave;
            _selos[chave] = sp;
            return sp;
        }

        static float Segmento(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy));
            float px = ax + t * dx - x, py = ay + t * dy - y;
            return Mathf.Sqrt(px * px + py * py);
        }

        // Phone test 08/11: text and copied buttons ignore the panel's mask. A row leaves as a whole before it touches the header or the frame.
        void CortarLinhas()
        {
            if (_mascara == null || _area == null) return;
            float metade = _ui.YOffset * _interno.lossyScale.y * 0.42f;
            float topo = _area.transform.TransformPoint(0f, -0.33f, 0f).y, fundo = _mascara.bounds.min.y;
            foreach (var go in _linhas)
            {
                if (go == null) continue;
                float y = go.transform.position.y;
                bool dentro = y + metade <= topo && y - metade >= fundo;
                if (go.activeSelf != dentro) go.SetActive(dentro);
            }
        }

        static string TextoDoEstado(string e) => e switch
        {
            "partida" => "Em partida",
            "lobby" => "No lobby",
            "online" => "Online",
            _ => "Offline",
        };

        static Color CorDoTexto(Pessoa a) => a.Estado switch
        {
            "partida" => new Color32(255, 181, 74, 255),
            "lobby" => new Color32(111, 227, 160, 255),
            "online" => new Color32(125, 184, 255, 255),
            _ => new Color32(154, 163, 173, 255),
        };

#if !AMIGOS_REAL
        // ---------------- fake people and actions (test build) ----------------

        void Carregar() { _proxLeitura = float.MaxValue; }

        void AcaoRemover(Pessoa a) { _amigos.Remove(a); Avisar(a.Nome + " removido."); }
        void AcaoBloquear(Pessoa a) { _amigos.Remove(a); _bloqueados.Add(a); Avisar(a.Nome + " bloqueado."); }
        void AcaoDesbloquear(Pessoa a) { _bloqueados.Remove(a); Avisar(a.Nome + " desbloqueado."); }
        void AcaoRecusar(Pessoa a) { _pedidos.Remove(a); Avisar("Pedido recusado."); }
        void AcaoPedir(Pessoa a) { a.PodePedir = false; a.Situacao = "enviado"; Desenhar(); }
        void AcaoAceitar(Pessoa a)
        {
            if (_aba == BUSCAR) { a.Situacao = "amigo"; Desenhar(); return; }
            _pedidos.Remove(a); a.Estado = "online"; a.PodeConvidar = true; _amigos.Add(a); Avisar(a.Nome + " agora é seu amigo.");
        }

        void CriarMentira()
        {
            _amigos.Clear(); _pedidos.Clear(); _bloqueados.Clear(); _outros.Clear();
            var verificado = new Selo { Nivel = "verificado", Bola = "#2F80ED", V = "#FFFFFF" };
            var ouro = new Selo { Nivel = "ouro", Bola = "#F2C94C", V = "#3A2A00" };
            Pessoa P(string nome, int n, string estado, Selo selo = null)
            {
                var hm = DestroyableSingleton<HatManager>.Instance;
                return new Pessoa
                {
                    Nome = nome,
                    Estado = estado,
                    Selo = selo,
                    Cor = n % Palette.PlayerColors.Length,
                    Chapeu = hm.allHats[(20 + n * 7) % hm.allHats.Length].ProdId,
                    Roupa = hm.allSkins[(5 + n * 3) % hm.allSkins.Length].ProdId,
                    Visor = hm.allVisors[(3 + n * 5) % hm.allVisors.Length].ProdId,
                    Banner = hm.allNamePlates[(2 + n * 4) % hm.allNamePlates.Length].ProdId,
                    Nivel = n % 3 == 2 ? (int?)null : 3 + n * 7,
                };
            }
            _amigos.Add(Com(P("Amigo no lobby", 0, "lobby", ouro), true, true));
            _amigos.Add(Com(P("Amiga em partida", 1, "partida"), false, false));
            _amigos.Add(Com(P("Amigo online", 2, "online", verificado), false, true));
            _amigos.Add(Com(P("Sala trancada", 3, "lobby"), false, false));
            _amigos.Add(Com(P("Amiga no lobby", 4, "lobby"), true, true));
            _amigos.Add(P("Amigo offline", 5, "offline"));
            _amigos.Add(P("Amiga offline", 6, "offline"));
            _pedidos.Add(P("Pedido um", 7, "offline", verificado));
            _pedidos.Add(P("Pedido dois", 8, "offline"));
            _bloqueados.Add(P("Pessoa bloqueada", 9, "offline"));
            foreach (var (nome, n, selo) in new[] { ("Criador famoso", 10, ouro), ("Criadora nova", 11, verificado), ("Jogador comum", 12, (Selo)null), ("Jogadora comum", 13, (Selo)null) })
            {
                var p = P(nome, n, "offline", selo);
                p.PodePedir = true;
                _outros.Add(p);
            }
        }

        static Pessoa Com(Pessoa p, bool entrar, bool convidar) { p.PodeEntrar = entrar; p.PodeConvidar = convidar; return p; }

        void AcaoDaCaixa()
        {
            string termo = (_caixa.text ?? "").Trim();
            if (termo.Contains("#"))   // a friend code: the request goes straight, same box as the search
            {
                _resultados = new List<Pessoa>();
                _aviso = "Pedido enviado para " + termo.Replace("<", "‹").Replace("\\", "/").Replace("[", "(") + ".";
                Desenhar();
                return;
            }
            if (termo.Length < 2 || termo.Length > 10) { _resultados = new List<Pessoa>(); _aviso = "Digite de 2 a 10 letras."; Desenhar(); return; }
            _aviso = "Ninguém encontrado.";
            foreach (var a in _amigos) a.Situacao = "amigo";
            foreach (var a in _pedidos) a.Situacao = "recebido";
            _resultados = _amigos.Concat(_pedidos).Concat(_outros)
                .Where(a => a.Nome.IndexOf(termo, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(a => a.Selo == null ? 1 : 0).Take(10).ToList();
            Desenhar();
        }

        void Entrar(Pessoa a)
        {
            Plugin.Registro.LogInfo("[amigos] (teste) entrar no lobby");
            Avisar("Entrando no lobby de " + a.Nome + "...");
        }

        void Convidar(Pessoa a)
        {
            Plugin.Registro.LogInfo("[amigos] (teste) convite");
            Avisar("Convite enviado para " + a.Nome + ".");
            _conviteDe = a.Nome;
            _conviteEm = Time.unscaledTime + 3f;
        }

#endif

#if AMIGOS_REAL
        // ---------------- the real server (only on the test server for now) ----------------

        string _etagAmigos, _etagPedidos, _etagBloqueados, _etagConvites, _conviteId;
        float _proxConvites;

        bool _rotaNoAr, _sondando;
        float _proxSonda;

        // Without a key, or while the server has no friends routes, this panel stays out of the way and the game's own list opens.
        void Sondar()
        {
            if (_sondando || Time.unscaledTime < _proxSonda || !SalaApi.TemChave) return;
            _sondando = true;
            AmigosApi.Amigos(null, r =>
            {
                _sondando = false;
                _proxSonda = Time.unscaledTime + 60f;
                if (r.Codigo == 200) _rotaNoAr = true;
            });
        }

        void Carregar()
        {
            _proxLeitura = float.MaxValue;   // until the answer says when
            int aba = _aba;
            if (aba == BUSCAR) { _proxLeitura = Time.unscaledTime + 30f; return; }
            if (aba == AMIGOS) AmigosApi.Amigos(_etagAmigos, r => Lido(r, aba, "amigos", _amigos, e => _etagAmigos = e));
            else if (aba == PEDIDOS) AmigosApi.Pedidos(_etagPedidos, r => Lido(r, aba, "pedidos", _pedidos, e => _etagPedidos = e));
            else AmigosApi.Bloqueados(_etagBloqueados, r => Lido(r, aba, "bloqueados", _bloqueados, e => _etagBloqueados = e));
        }

        void Lido(AmigosApi.Resposta r, int aba, string campo, List<Pessoa> destino, Action<string> guardar)
        {
            _proxLeitura = Time.unscaledTime + r.ProximaEmSeg;
            bool primeira = !_leu[aba];
            _leu[aba] = true;
            if (r.Codigo == 304) { if (primeira && _aba == aba) Desenhar(); return; }
            if (r.Codigo != 200) { if (_aba == aba) { _aviso = AmigosApi.Aviso(r); Desenhar(); } return; }
            var l = AmigosApi.LerLista(r.Corpo, campo);
            destino.Clear();
            destino.AddRange(l.Pessoas);
            if (campo == "amigos") _nPedidos = l.Pedidos;
            else if (campo == "pedidos") _nPedidos = l.Pessoas.Count;
            if (campo != "bloqueados") _online = l.Online;   // this answer has no count: it would show "0 online" until the next change
            guardar(r.Etag);
            if (_aba == aba) { if (Time.unscaledTime >= _avisoAte) _aviso = ""; Desenhar(); }
        }

        // A write: says how it went and reads the tab again (without the ETag, since the list has just changed).
        void Feito(AmigosApi.Resposta r, string certo)
        {
            bool ok = r.Codigo == 200 || r.Codigo == 204;
            if (ok) { _etagAmigos = null; _etagPedidos = null; _etagBloqueados = null; _proxLeitura = 0f; }
            Avisar(ok ? certo : AmigosApi.Aviso(r));
        }

        void AcaoRemover(Pessoa a) => AmigosApi.Remover(a.Id, r => Feito(r, a.Nome + " removido."));
        void AcaoBloquear(Pessoa a) => AmigosApi.Bloquear(a.Id, r => Feito(r, a.Nome + " bloqueado."));
        void AcaoDesbloquear(Pessoa a) => AmigosApi.Desbloquear(a.Id, r => Feito(r, a.Nome + " desbloqueado."));
        void AcaoRecusar(Pessoa a) => AmigosApi.RecusarPedido(a.Id, r => Feito(r, "Pedido recusado."));
        void AcaoAceitar(Pessoa a) => AmigosApi.AceitarPedido(a.Id, r => Feito(r, a.Nome + " agora é seu amigo."));

        void AcaoPedir(Pessoa a) => AmigosApi.Pedir(a.Id, r =>
        {
            if (r.Codigo == 200 || r.Codigo == 204) { a.PodePedir = false; a.Situacao = "enviado"; Desenhar(); return; }
            Avisar(r.Codigo == 410 ? "Busque de novo." : AmigosApi.Aviso(r));
        });

        void AcaoDaCaixa()
        {
            string termo = (_caixa.text ?? "").Trim();
            if (termo.Contains("#"))
            {
                _resultados = new List<Pessoa>();
                AmigosApi.PedirPorCodigo(termo, r => Avisar(r.Codigo == 200 || r.Codigo == 204 ? "Pedido enviado." : AmigosApi.Aviso(r)));
                return;
            }
            if (termo.Length < 2 || termo.Length > 10) { _resultados = new List<Pessoa>(); Avisar("Digite de 2 a 10 letras."); return; }
            AmigosApi.Buscar(termo, r =>
            {
                if (r.Codigo != 200) { _resultados = new List<Pessoa>(); Avisar(AmigosApi.Aviso(r)); return; }
                _resultados = AmigosApi.LerLista(r.Corpo, "resultados").Pessoas;
                Avisar("Ninguém encontrado.");
            });
        }

        void Entrar(Pessoa a)
        {
            Avisar("Entrando no lobby de " + a.Nome + "...");
            AmigosApi.Entrar(a.Id, Chegou);
        }

        void Convidar(Pessoa a) => AmigosApi.Convidar(a.Id, r => Avisar(r.Codigo == 200 || r.Codigo == 204 ? "Convite enviado para " + a.Nome + "." : AmigosApi.Aviso(r)));

        // The answer of "entrar" and of accepting an invite. "redirect": the server moves the connected player. "entrar": the normal path of the
        // game with the room code, which is used here once and never shown or logged.
        void Chegou(AmigosApi.Resposta r)
        {
            if (r.Codigo == 410) { Avisar("Esse convite não vale mais."); return; }
            if (r.Codigo != 200 || r.Corpo == null) { Avisar(AmigosApi.Aviso(r)); return; }
            try
            {
                using var d = System.Text.Json.JsonDocument.Parse(r.Corpo);
                string modo = d.RootElement.TryGetProperty("modo", out var m) ? m.GetString() : "";
                if (modo != "entrar" || !d.RootElement.TryGetProperty("codigo", out var c)) return;
                var cliente = AmongUsClient.Instance;
                if (cliente == null) return;
                int id = InnerNet.GameCode.GameNameToInt(c.GetString());
                cliente.GameId = id;
                cliente.StartCoroutine(cliente.CoJoinOnlineGameFromCode(id, true));
            }
            catch (Exception e) { Plugin.Registro.LogWarning("[amigos] entrar: " + e.GetType().Name); }
        }

        void PerguntarConvites(float agora)
        {
            if (agora < _proxConvites || _conviteAte >= 0f || !PodeMostrarConvite() || !SalaApi.TemChave) return;
            _proxConvites = float.MaxValue;
            AmigosApi.Convites(_etagConvites, r =>
            {
                _proxConvites = Time.unscaledTime + (r.Codigo == 200 || r.Codigo == 304 ? r.ProximaEmSeg : 120f);
                if (r.Codigo != 200) return;
                _etagConvites = r.Etag;
                var lista = AmigosApi.LerConvites(r.Corpo);
                if (lista.Count == 0 || !PodeMostrarConvite()) return;
                _conviteId = lista[0].Id;
                _conviteDe = lista[0].De;
                MostrarConvite(lista[0].ExpiraEmSeg);
            });
        }
#endif

        // ---------------- the invite pop-up ----------------

        // Only in the menu with the CRICRI BR region chosen, or in a CRICRI BR lobby (the rule agreed with the server session).
        static bool PodeMostrarConvite()
        {
            if (!NaRegiao()) return false;
            if (SceneManager.GetActiveScene().name == "MainMenu") return true;
            return LobbyBehaviour.Instance != null && ShipStatus.Instance == null;
        }

        void TiqueConvite()
        {
            float agora = Time.unscaledTime;
#if AMIGOS_REAL
            PerguntarConvites(agora);
#else
            if (_conviteEm >= 0f && agora >= _conviteEm && PodeMostrarConvite())
            {
                _conviteEm = -1f;
                MostrarConvite(60);
            }
#endif
            if (_conviteAte < 0f) return;
            if (agora >= _conviteAte || !PodeMostrarConvite()) { FecharConvite(); return; }
            if (_textoPopup != null) _textoPopup.text = _conviteDe.Replace("<", "‹").Replace("\\", "/").Replace("[", "(") + " chamou você para a sala.\nQuer entrar?\n<size=70%>" + Mathf.CeilToInt(_conviteAte - agora) + " s</size>";
        }

        void MostrarConvite(int segundos)
        {
            if (_popup == null && !MontarPopup()) { Plugin.Registro.LogWarning("[amigos] sem o pop-up do jogo pra copiar"); return; }
            _conviteAte = Time.unscaledTime + segundos;
            _popup.SetActive(true);
        }

        void FecharConvite()
        {
            _conviteAte = -1f;
            if (_popup != null) _popup.SetActive(false);
        }

        // A copy of the game's error pop-up with two buttons. It is copied while inactive and loses DisconnectPopup before it ever wakes:
        // that class is a singleton and a second one would destroy itself.
        bool MontarPopup()
        {
            _popup = NovoPopup("CriCriBrConvite", "ENTRAR", () =>
            {
#if AMIGOS_REAL
                AmigosApi.AceitarConvite(_conviteId, Chegou);
#endif
                FecharConvite();
            }, "AGORA NÃO", () =>
            {
#if AMIGOS_REAL
                AmigosApi.RecusarConvite(_conviteId, r => { });
#endif
                FecharConvite();
            }, out _textoPopup);
            return _popup != null;
        }

        internal static GameObject NovoPopup(string nome, string sim, Action acaoSim, string nao, Action acaoNao, out TextMeshPro texto)
        {
            texto = null;
            if (!DestroyableSingleton<DisconnectPopup>.InstanceExists) return null;
            var modelo = DestroyableSingleton<DisconnectPopup>.Instance;
            bool estava = modelo.gameObject.activeSelf;
            if (estava) modelo.gameObject.SetActive(false);
            var popup = Instantiate(modelo.gameObject, modelo.transform.parent);
            if (estava) modelo.gameObject.SetActive(true);
            popup.name = nome;
            popup.transform.localScale = Vector3.one;   // the game's opening animation is removed below, and a closed popup rests at size zero
            // In the lobby the camera follows the player: left where it is, the popup stayed on the map, half out of the screen and solid (09/11).
            // On the HUD it follows the camera, in the HUD's layer it is no longer a wall, and z -290 is in front of the HUD, behind the fade (-300).
            if (SceneManager.GetActiveScene().name == "OnlineGame" && DestroyableSingleton<HudManager>.InstanceExists && Camera.main != null)
            {
                var hud = DestroyableSingleton<HudManager>.Instance;
                var camera = Camera.main.transform.position;
                popup.transform.SetParent(hud.transform, true);
                popup.transform.position = new Vector3(camera.x, camera.y, -290f);
                foreach (var parte in popup.GetComponentsInChildren<Transform>(true)) parte.gameObject.layer = hud.gameObject.layer;
            }
            DestroyImmediate(popup.GetComponent<DisconnectPopup>());
            DestroyImmediate(popup.GetComponent<ControllerNavMenu>());
            DestroyImmediate(popup.GetComponent<TransitionOpen>());
            var fechar = popup.transform.Find("CloseButton");
            if (fechar != null) fechar.gameObject.SetActive(false);
            texto = popup.transform.Find("Text_TMP").GetComponent<TextMeshPro>();
            var ok = popup.transform.Find("OkayButton");
            var outro = Instantiate(ok.gameObject, ok.parent).transform;
            outro.name = "RecusarButton";
            ConfigurarBotaoPopup(ok, sim, -1.7f, acaoSim);
            ConfigurarBotaoPopup(outro, nao, 1.7f, acaoNao);
            return popup;
        }

#if AMIGOS_REAL
        // CriCri, 09/11: "automatico sem ter que entrar em nenhum site, so a verificaçao de conta nova". In a CRICRI BR lobby, without a key, with an account (a guest has
        // no friend code): no question and no browser. The site sends a one-time code in a private chat line that only this account's connection receives; this reads
        // it locally, hides the line and trades it for the key. Linking here replaces the key of another device without asking. Nothing is sent over the
        // game's connection, and the id and the code are never logged. Failing says nothing: it tries again, and the app's "Ligar de outro jeito" is still there.
        enum Ligando { Nada, Iniciando, EsperandoCodigo, Resgatando }
        static readonly System.Text.RegularExpressions.Regex LinhaDoCodigo = new System.Text.RegularExpressions.Regex(@"Código do CriCri Mods: ([A-Z0-9\-]{4,16})\. Não passe", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Ligando _ligando;
        bool _perguntouLigar, _podeResgatar, _avisouSemCodigo;
        float _noLobbyDesde = -1f, _ligarAte, _proxChat, _proxPergunta, _retentarEm;
        string _ligarId, _ligarCodigo;
        readonly HashSet<string> _codigosRecusados = new HashSet<string>();

        void TiqueLigar()
        {
            bool noLobby = SceneManager.GetActiveScene().name == "OnlineGame" && LobbyBehaviour.Instance != null && ShipStatus.Instance == null && NaRegiao();
            if (_ligando == Ligando.EsperandoCodigo) { OlharChat(); return; }
            if (_perguntouLigar || _ligando != Ligando.Nada || SalaApi.TemChave || Time.unscaledTime < _proxPergunta) return;
            if (!noLobby) { _noLobbyDesde = -1f; return; }
            if (_noLobbyDesde < 0f) { _noLobbyDesde = Time.unscaledTime; return; }
            if (Time.unscaledTime - _noLobbyDesde < 4f) return;
            if (!DestroyableSingleton<EOSManager>.InstanceExists || string.IsNullOrEmpty(DestroyableSingleton<EOSManager>.Instance.FriendCode))
            {
                if (!_avisouSemCodigo) { _avisouSemCodigo = true; Plugin.Registro.LogInfo("[ligar] esperando o jogo ter o codigo de amigo"); }
                return;
            }
            _perguntouLigar = true;
            ComecarLigar();
        }

        int _falhasLigar;

        // Three tries per session, a little further apart each time: the first can come before the server has the player's character.
        void NaoLigouAgora(string onde, int status)
        {
            Plugin.Registro.LogInfo("[ligar] " + onde + ": " + status);
            if (++_falhasLigar < 3) { _perguntouLigar = false; _proxPergunta = Time.unscaledTime + 20f * _falhasLigar; }
            FimLigar(null);
        }

        void AvisoLigar(string texto)
        {
            if (DestroyableSingleton<HudManager>.InstanceExists) DestroyableSingleton<HudManager>.Instance.Notifier.AddDisconnectMessage(texto);
        }

        void FimLigar(string aviso)
        {
            _ligando = Ligando.Nada;
            _ligarId = _ligarCodigo = null;
            if (aviso != null) AvisoLigar(aviso);
        }

        static async void Chamar(string rota, string json, Action<int, string> depois)
        {
            int status = 0; string corpo = null;
            try { (status, corpo) = await LigarNaVerificacao.Postar(rota, json).ConfigureAwait(false); }
            catch (Exception) { }
            SalaApi.Respostas.Enqueue(() => depois(status, corpo));
        }

        static bool LerJson(string corpo, string campo, out System.Text.Json.JsonElement raiz, out string valor)
        {
            raiz = default;
            valor = null;
            try
            {
                raiz = System.Text.Json.JsonDocument.Parse(corpo).RootElement;
                if (raiz.TryGetProperty(campo, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String) valor = v.GetString();
            }
            catch (Exception) { }
            return !string.IsNullOrEmpty(valor);
        }

        void ComecarLigar()
        {
            _ligando = Ligando.Iniciando;
#if CRICRI_PC
            // Linking from the PC only starts once the server keeps one key per device: before that it would switch off the key of the phone. This comes before
            // "iniciar" because iniciar already puts the private line with the code in the chat.
            Chamar("/api/mod/ligar/aparelhos", "{}", (status, _) =>
            {
                if (status != 200) { Plugin.Registro.LogInfo("[ligar] servidor ainda sem chave por aparelho: " + status); FimLigar(null); return; }
                IniciarLigar();
            });
#else
            IniciarLigar();
#endif
        }

        void IniciarLigar()
        {
            // The server finds the proven connection in this room on the caller's network: the player id picks it (friend codes and names can change; CriCri, 09/11),
            // the name is for a server that does not read the id yet. Claiming another id gains nothing: the one-time code only goes to that connection. Never logged.
            string corpo = System.Text.Json.JsonSerializer.Serialize(new
            {
                codigoDaSala = InnerNet.GameCode.IntToGameName(AmongUsClient.Instance.GameId),
                puid = MeuPuid(),
                nick = AmongUs.Data.DataManager.Player.Customization.Name,
#if CRICRI_PC
                aparelho = "pc",
#endif
            });
            Chamar("/api/mod/ligar/pelo-jogo/iniciar", corpo, (status, resposta) =>
            {
                if (status == 0 || status == 429) { Plugin.Registro.LogInfo("[ligar] pelo jogo: " + status); _perguntouLigar = false; _proxPergunta = Time.unscaledTime + 60f; FimLigar(null); return; }   // no network or too fast: ask again in a while
                if (status != 200 || resposta == null) { NaoLigouAgora("pelo jogo", status); return; }
                if (!LerJson(resposta, "id", out var raiz, out _ligarId)) { NaoLigouAgora("resposta sem id", status); return; }
                _ligarCodigo = null;
                _ligarAte = Time.unscaledTime + Math.Clamp(raiz.TryGetProperty("expiraEmSeg", out var e) && e.TryGetInt32(out int x) ? x : 300, 30, 600);
                _ligando = Ligando.EsperandoCodigo;
                _codigosRecusados.Clear();
                _podeResgatar = true;   // CriCri, 09/11: no question at all, even when this replaces the key of another device
            });
        }

        // The private line is read and removed as soon as it is seen. Another player typing the same words only costs one wrong try, and the real line is still waited for.
        void OlharChat()
        {
            if (Time.unscaledTime >= _ligarAte) { NaoLigouAgora("o codigo nao chegou", 0); return; }
            if (_ligarCodigo != null) { if (_retentarEm > 0f && Time.unscaledTime >= _retentarEm) { _retentarEm = 0f; Resgatar(); } return; }
            if (Time.unscaledTime < _proxChat || !DestroyableSingleton<HudManager>.InstanceExists) return;
            _proxChat = Time.unscaledTime + 0.1f;
            var chat = DestroyableSingleton<HudManager>.Instance.Chat;
            if (chat == null || chat.chatBubblePool == null) return;
            var ativas = chat.chatBubblePool.activeChildren;
            for (int i = ativas.Count - 1; i >= 0; i--)
            {
                var bolha = ativas[i].TryCast<ChatBubble>();
                if (bolha == null || bolha.TextArea == null) continue;
                var achou = LinhaDoCodigo.Match(bolha.TextArea.text ?? "");
                if (!achou.Success || _codigosRecusados.Contains(achou.Groups[1].Value)) continue;
                _ligarCodigo = achou.Groups[1].Value;
                chat.chatBubblePool.Reclaim(bolha);
                chat.AlignAllBubbles();
                if (chat.chatNotification != null) chat.chatNotification.gameObject.SetActive(false);   // the preview shown with the chat closed
                Resgatar();
                return;
            }
        }

        void Resgatar()
        {
            if (!_podeResgatar || _ligarCodigo == null || _ligando != Ligando.EsperandoCodigo) return;
            _ligando = Ligando.Resgatando;
            string corpo = System.Text.Json.JsonSerializer.Serialize(new { id = _ligarId, codigo = _ligarCodigo });
            Chamar("/api/mod/ligar/pelo-jogo/resgatar", corpo, (status, resposta) =>
            {
                if (status == 0 || status == 429) { _ligando = Ligando.EsperandoCodigo; _retentarEm = Time.unscaledTime + (status == 0 ? 5f : 30f); return; }   // the request is still good
                if (status == 403 && _codigosRecusados.Count < 3) { _codigosRecusados.Add(_ligarCodigo); _ligarCodigo = null; _ligando = Ligando.EsperandoCodigo; return; }
                if (status != 200 || resposta == null || !LerJson(resposta, "chave", out _, out var chave)) { NaoLigouAgora("resgatar", status); return; }
                bool guardou = LigarNaVerificacao.Guardar(chave);
                _proxLeitura = 0f;
                FimLigar(guardou ? "Conta do CRICRI BR ligada." : "Conta do CRICRI BR ligada até fechar o jogo.");
            });
        }
#endif

#if AMIGOS_REAL
        // CriCri, 08/11: the key belongs to the account that is in the launcher. A browser logged in to another account hands out that account's key.
        // Only to prevent that mistake: the server decides what a key can do. The comparison is by the account's id, which never changes, so on the phone there is no
        // question: the wrong key is not used and the first lobby links the right account over it. No answer, 404 or no hash = nothing to compare.
        bool _donoConferido;
        float _proxDono;
        GameObject _popupDono;

        // The account's id in the game, or null while the login is not finished: in that state the game's getter throws, so the checks come first.
        static string MeuPuid()
        {
            try
            {
                if (!DestroyableSingleton<EOSManager>.InstanceExists) return null;
                var eos = DestroyableSingleton<EOSManager>.Instance;
                return eos != null && eos.HasFinishedLoginFlow() && eos.userId != null ? eos.ProductUserId : null;
            }
            catch (Exception) { return null; }
        }

        // The contract of /api/mod/eu: SHA-256, lowercase hex, of the UTF-8 bytes of the id in lowercase and trimmed.
        static string Sha256DaConta(string puid)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(puid.Trim().ToLowerInvariant()))).ToLowerInvariant();
        }

        void ConferirDono()
        {
            if (_donoConferido || !SalaApi.TemChave || Time.unscaledTime < _proxDono) return;
            _proxDono = Time.unscaledTime + 2f;
            if (SceneManager.GetActiveScene().name != "MainMenu" || !NaRegiao()) return;
            string meu = MeuPuid();
            if (string.IsNullOrEmpty(meu)) return;
            _donoConferido = true;
            AmigosApi.Eu(r =>
            {
                if (r.Codigo == 0) { _donoConferido = false; _proxDono = Time.unscaledTime + 60f; return; }
                if (r.Codigo != 200 || string.IsNullOrEmpty(r.Corpo)) return;
                using var doc = System.Text.Json.JsonDocument.Parse(r.Corpo);
                var raiz = doc.RootElement;
                if (!raiz.TryGetProperty("contaSha256", out var c) || c.ValueKind != System.Text.Json.JsonValueKind.String) return;   // the API never returns the id itself, only this hash
                if (string.Equals(c.GetString(), Sha256DaConta(meu), StringComparison.OrdinalIgnoreCase)) return;
#if !CRICRI_PC
                Plugin.Registro.LogInfo("[ligar] a chave guardada e de outra conta: nao vou usar");
                NaoUsarChave();
                return;
#else
                string nick = raiz.TryGetProperty("nick", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() : "outra conta";
                _popupDono = NovoPopup("CriCriBrContaDiferente", "USAR", FecharDono, "NÃO USAR", () => { FecharDono(); NaoUsarChave(); }, out var texto);
                if (_popupDono == null) return;
                string minha = AmongUs.Data.DataManager.Player.Customization.Name ?? "";
                texto.text = "Esta chave é da conta " + nick.Replace("<", "‹").Replace("\\", "/").Replace("[", "(") + ".\nVocê entrou no jogo com " + minha.Replace("<", "‹").Replace("\\", "/").Replace("[", "(") + ".\nUsar mesmo assim?";
                _popupDono.SetActive(true);
#endif
            });
        }

        void FecharDono()
        {
            if (_popupDono != null) _popupDono.SetActive(false);
        }

        // For this session: the saved key stays until the person links the account that is really here (the in-game offer and the verification link).
        void NaoUsarChave()
        {
            SalaApi.TrocarChave(null);
            _rotaNoAr = false;
            if (_montado && _aqui) Alternar(false);
        }
#endif

        static void ConfigurarBotaoPopup(Transform b, string texto, float x, Action acao)
        {
            foreach (var c in b.GetComponents<CloseButtonConsoleBehaviour>()) DestroyImmediate(c);
            foreach (var t in b.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(t);
            foreach (var t in b.GetComponentsInChildren<TextMeshPro>(true)) t.text = texto;
            var p = b.localPosition;
            b.localPosition = new Vector3(x, p.y, p.z);
            var pb = b.GetComponent<PassiveButton>();
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            pb.OnClick.AddListener(acao);
        }
    }
}
#endif
