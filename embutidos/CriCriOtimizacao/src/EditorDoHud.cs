using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CriCriMods
{
    /*
      (12/11, ele: "nas configurações você clica lá, aparece um menu com todos os botões ... dando pra clicar no botão, na parte de
      cima aparece uma barra de volume pra mudar o tamanho, opacidade, ou tecla pra ativar ... e também dá pra arrastar coisas como
      aquela parte do ping, etc, e dá pra salvar preset e voltar pro padrão")
      (12/11, depois: "deixa mais bonito e fácil de saber e editar e conseguindo mudar o lugar da barra de task e lista de tarefas")

      O EDITOR DO HUD. Abre pelo botão "Personalizar HUD" das Definições, com o HUD na tela (sala, Freeplay ou partida). Enquanto
      ele está aberto:
      - todos os botões aparecem (os que o papel da pessoa não usa ficam ligados só enquanto edita), cada um com um contorno de
        cantos redondos; o que não tem nome escrito (joystick, mapa, ping, lista e barra de tarefas...) ganha uma etiqueta;
      - tocar escolhe (amarelo); arrastar move; no painel: Tamanho e Opacidade (as barras), Tecla (botões) e Visual ("do jogo"
        ou "escuro"); sem nada escolhido (toque fora de tudo), o botão "Modo escuro" troca tudo de uma vez: normal, só o chat,
        completo (12/11);
      - o botão do chat aparece mesmo onde o jogo o esconde (Freeplay, partida); escolher ele (ou a janela, ou um botão dela)
        abre o chat, para os botões dele se arrumarem também (12/11, ele: "no hud aparece o botão do chat pra mudar também e
        indo nele aparece o chat também pra mudar botões do chat");
      - embaixo: Padrão (dois toques), os presets 1, 2 e 3 (tocar usa; "Salvar preset" e o número guarda), e Pronto;
      - o painel fica em cima e desce quando o item escolhido está na metade de cima; some enquanto o dedo arrasta;
      - os botões do jogo não funcionam (os colisores deles ficam desligados) e o boneco não anda: editar não aperta "Reportar"
        sem querer. Tudo volta ao fechar (Pronto, o Voltar do celular, uma reunião ou a troca de cena).

      O painel é desenhado pelo mod (caixas e a letra do jogo) e lê o dedo direto (Input), sem os botões do jogo: assim nenhum
      clique dele chega no jogo. O que muda vai para o LayoutDoHud, que é quem põe no jogo (o mesmo caminho de fora do editor).
    */
    internal static class EditorDoHud
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        const int Camada = 5;          // a camada que a câmera do HUD desenha
        const int Ordem = 3000;        // por cima de tudo do HUD

        // as cores (o azul-escuro dos painéis do jogo, o amarelo do contorno de "usar", o verde dos botões ligados)
        static readonly Color FundoDoPainel = new Color(0.055f, 0.07f, 0.10f, 0.93f);
        static readonly Color BordaDoPainel = new Color(1f, 1f, 1f, 0.22f);
        static readonly Color FundoDoBotao = new Color(0.17f, 0.20f, 0.26f, 1f);
        static readonly Color BordaDoBotao = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color Verde = new Color(0.30f, 0.86f, 0.42f, 1f);
        static readonly Color VerdeEscuro = new Color(0.13f, 0.50f, 0.25f, 1f);
        static readonly Color VermelhoEscuro = new Color(0.58f, 0.17f, 0.17f, 1f);
        static readonly Color Amarelo = new Color(1f, 0.86f, 0.18f, 1f);
        static readonly Color Cinza = new Color(0.68f, 0.72f, 0.78f, 1f);
        static readonly Color Apagado = new Color(1f, 1f, 1f, 0.32f);
        static readonly Color ContornoDeLeve = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color FundoDaEtiqueta = new Color(0.055f, 0.07f, 0.10f, 0.85f);

        sealed class Botao
        {
            public GameObject Go;
            public SpriteRenderer Fundo, Borda;
            public TextMeshPro Texto;
            public Vector2 Centro, Tamanho;      // no espaço do painel
            public Action Acao;
        }

        sealed class Barra
        {
            public GameObject Go;
            public SpriteRenderer Trilho, Cheio, Pino;
            public TextMeshPro Rotulo;
            public Vector2 Centro, Tamanho;
            public float Min, Max;
            public string Nome;
        }

        sealed class Marca
        {
            public GameObject Go;
            public SpriteRenderer Contorno;
            public SpriteRenderer FundoDaEtiqueta;
            public TextMeshPro Etiqueta;
        }

        static GameObject _raiz, _painelGo;
        static float _painelY = 1.05f, _painelX;
        static HudManager _h;
        static Camera _cam;
        static TextMeshPro _modelo;
        static float _w, _metadeL, _metadeA;
        const float AlturaDoPainel = 2.25f;

        static TextMeshPro _nome, _dica;
        static Barra _tamanho, _opacidade;
        static Botao _tecla, _visual, _modo, _padrao, _salvarPreset, _pronto;
        static float _xCtx, _cMeio;
        static bool _chatEscondido, _abriuChat;
        static readonly Botao[] _presets = new Botao[3];
        static readonly List<Botao> _botoes = new List<Botao>();
        static readonly Dictionary<string, Marca> _marcas = new Dictionary<string, Marca>();

        // o que foi mexido no jogo para editar (tudo volta no Fechar)
        static readonly List<GameObject> _ligados = new List<GameObject>();
        static readonly List<Collider2D> _colisores = new List<Collider2D>();
        static readonly List<Behaviour> _parados = new List<Behaviour>();
        static PlayerControl _jogador;
        static bool _andavaAntes;

        // estado
        static string _sel;
        static bool _esperandoTecla, _salvandoPreset;
        static float _confirmarPadraoAte, _avisoAte;
        static string _aviso;
        static int _pegou;            // 0 nada, 1 tamanho, 2 opacidade, 3 botão do painel, 4 peça do HUD
        static Botao _botaoPegado;
        static Vector2 _ponteiroInicio, _pivoInicio, _deslocInicio;
        static bool _arrastou, _segurava;
        static KeyCode[] _todasAsTeclas;

        internal static bool Aberto => _raiz != null;

        /// <summary>Dá para abrir agora? (o HUD tem de estar na tela)</summary>
        internal static bool PodeAbrir(out string porque)
        {
            porque = null;
            if (!HudManager.InstanceExists || HudManager.Instance.UICamera == null) { porque = "Entre numa sala primeiro"; return false; }
            if (MeetingHud.Instance != null) { porque = "Fora da reunião"; return false; }
            return true;
        }

        internal static void Abrir()
        {
            if (Aberto) return;
            if (!PodeAbrir(out _)) return;
            try
            {
                _h = HudManager.Instance;
                _cam = _h.UICamera;
                _modelo = Desenho.ModeloDoHud(_h);
                Travar();
                Montar();
                _sel = null; _esperandoTecla = false; _salvandoPreset = false; _pegou = 0; _segurava = false;
                Log.LogInfo("[hud] editor do HUD aberto (" + _ligados.Count + " botoes ligados so para editar, " + _colisores.Count + " colisores desligados)");
            }
            catch (Exception e)
            {
                Log.LogWarning("[hud] nao abri o editor: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                Fechar(false);
            }
        }

        internal static void Fechar(bool salvar = true)
        {
            if (salvar) LayoutDoHud.Salvar();
            Destravar();
            if (_raiz != null) UnityEngine.Object.Destroy(_raiz);
            _raiz = null;
            _painelGo = null;
            _botoes.Clear();
            _marcas.Clear();
            _h = null;
            _cam = null;
            Log.LogInfo("[hud] editor do HUD fechado" + (salvar ? " (salvo)" : ""));
        }

        // ---------------- travar o jogo enquanto edita ----------------

        static void Travar()
        {
            _ligados.Clear(); _colisores.Clear(); _parados.Clear();
            // os botões de ação que o papel não usa aparecem só para a pessoa poder arrumar
            foreach (var b in new Component[] { _h.KillButton, _h.SabotageButton, _h.ImpostorVentButton, _h.AbilityButton, _h.SecondaryAbilityButton })
                if (b != null && !b.gameObject.activeSelf) { b.gameObject.SetActive(true); _ligados.Add(b.gameObject); }
            // o botão do chat aparece (no Freeplay e no meio da partida o jogo o esconde), e nada do chat responde ao toque
            _chatEscondido = false; _abriuChat = false;
            try
            {
                var ch = _h.Chat;
                if (ch != null && ch.chatButton != null && !ch.chatButton.gameObject.activeInHierarchy) { ch.SetVisible(true); _chatEscondido = true; }
                if (ch != null && ch.chatScreen != null)
                    foreach (var c in ch.chatScreen.GetComponentsInChildren<Collider2D>(true))
                        if (c != null && c.enabled) { c.enabled = false; _colisores.Add(c); }
            }
            catch (Exception e) { Log.LogWarning("[hud] chat no editor: " + e.Message); }
            // nenhum botão do HUD funciona enquanto edita
            foreach (var peca in LayoutDoHud.Pecas)
            {
                List<GameObject> objs;
                try { objs = peca.Objetos(_h); } catch { continue; }
                foreach (var go in objs)
                {
                    if (go == null) continue;
                    foreach (var c in go.GetComponentsInChildren<Collider2D>(true))
                        if (c != null && c.enabled) { c.enabled = false; _colisores.Add(c); }
                }
            }
            // nem o joystick, nem as teclas do PC (o KeyboardJoystick é quem lê WASD e as teclas dos botões)
            try
            {
                var j = _h.joystick != null ? _h.joystick.TryCast<MonoBehaviour>() : null;
                if (j != null && j.enabled) { j.enabled = false; _parados.Add(j); }
                var js = _h.Joysticks;
                if (js != null)
                    for (int i = 0; i < js.Length; i++)
                    {
                        var vj = js[i] != null ? js[i].GetComponent<VirtualJoystick>() : null;
                        if (vj != null && vj.enabled) { vj.enabled = false; _parados.Add(vj); }
                    }
            }
            catch { }
            // e o boneco fica parado (só neste aparelho: nada é enviado)
            _jogador = PlayerControl.LocalPlayer;
            if (_jogador != null) { _andavaAntes = _jogador.moveable; _jogador.moveable = false; }
        }

        static void Destravar()
        {
            try
            {
                var ch = _h != null ? _h.Chat : null;
                if (ch != null)
                {
                    if (_abriuChat && ch.IsOpenOrOpening) ch.ForceClosed();
                    if (_chatEscondido) ch.SetVisible(false);
                }
            }
            catch { }
            _abriuChat = false; _chatEscondido = false;
            foreach (var go in _ligados) if (go != null) go.SetActive(false);
            foreach (var c in _colisores) if (c != null) c.enabled = true;
            foreach (var b in _parados) if (b != null) b.enabled = true;
            if (_jogador != null) _jogador.moveable = _andavaAntes;
            _ligados.Clear(); _colisores.Clear(); _parados.Clear();
            _jogador = null;
        }

        // ---------------- o painel ----------------

        static void Montar()
        {
            _raiz = new GameObject("CriCriEditorDoHud");
            _raiz.layer = Camada;
            _raiz.transform.SetParent(_h.transform, false);
            var lossy = _h.transform.lossyScale;
            _raiz.transform.localScale = new Vector3(1f / Math.Max(0.0001f, Math.Abs(lossy.x)), 1f / Math.Max(0.0001f, Math.Abs(lossy.y)), 1f);
            Posicionar();

            _metadeA = _cam.orthographicSize;
            _metadeL = _metadeA * (_cam.aspect > 0.01f ? _cam.aspect : 1.78f);
            // estreito e um pouco para a esquerda: os botões do jogo ficam nas beiradas (os de ação e os de cima à direita,
            // o joystick embaixo à esquerda), e assim o painel cobre o mínimo deles
            _w = Math.Min(7.2f, _metadeL * 2f - 0.4f);
            _painelX = Mathf.Clamp(-1.1f, -_metadeL + _w / 2f + 0.2f, _metadeL - _w / 2f - 0.2f);
            _painelGo = new GameObject("Painel");
            _painelGo.layer = Camada;
            _painelGo.transform.SetParent(_raiz.transform, false);
            _painelY = 1.05f;
            _painelGo.transform.localPosition = new Vector3(_painelX, _painelY, 0f);
            var t = _painelGo.transform;
            Desenho.Caixa(t, "Fundo", _w, AlturaDoPainel, FundoDoPainel, Ordem, Camada);
            Desenho.Caixa(t, "Borda", _w, AlturaDoPainel, BordaDoPainel, Ordem + 1, Camada, true);

            // linha 1: o nome (e a dica) | Tamanho | Opacidade | Tecla ou Visual
            float y1 = 0.52f, y2 = -0.55f;
            float esq = -_w / 2f + 0.18f, util = _w - 0.36f;
            float cNome = util * 0.21f, cBarra = util * 0.235f, cCtx = util * 0.32f;
            float x = esq;
            _nome = Desenho.Texto(t, "Nome", _modelo, 0.25f, Color.white, Ordem + 3, Camada);
            _nome.transform.localPosition = new Vector3(x + cNome / 2f, y1 + 0.13f, 0f);
            _nome.rectTransform.sizeDelta = new Vector2(cNome, 0.5f);
            Encolher(_nome);
            _dica = Desenho.Texto(t, "Dica", _modelo, 0.15f, Cinza, Ordem + 3, Camada);
            _dica.transform.localPosition = new Vector3(x + cNome / 2f, y1 - 0.2f, 0f);
            _dica.rectTransform.sizeDelta = new Vector2(cNome, 0.4f);
            _dica.enableWordWrapping = true;
            x += cNome;
            _tamanho = NovaBarra(t, "Tamanho", new Vector2(x + cBarra / 2f, y1), cBarra - 0.3f, 0.5f, 2f);
            x += cBarra;
            _opacidade = NovaBarra(t, "Opacidade", new Vector2(x + cBarra / 2f, y1), cBarra - 0.3f, 0.2f, 1f);
            x += cBarra;
            _xCtx = x; _cMeio = cCtx / 2f;
            _tecla = NovoBotao(t, "Tecla", new Vector2(x + _cMeio / 2f, y1), new Vector2(_cMeio - 0.1f, 0.78f), ApertarTecla);
            _visual = NovoBotao(t, "Visual", new Vector2(x + _cMeio * 1.5f, y1), new Vector2(_cMeio - 0.1f, 0.78f), ApertarVisual);
            _modo = NovoBotao(t, "ModoEscuro", new Vector2(x + _cMeio, y1), new Vector2(cCtx - 0.1f, 0.78f), ApertarModoEscuro);

            // linha 2: Padrão | 1 | 2 | 3 | Salvar preset | Pronto
            float[] larg = { 0.17f, 0.10f, 0.10f, 0.10f, 0.26f, 0.27f };
            x = esq;
            var acoes = new Action[] { ApertarPadrao, () => ApertarPreset(0), () => ApertarPreset(1), () => ApertarPreset(2), ApertarSalvarPreset, () => Fechar(true) };
            var nomes = new[] { "Padrao", "Preset1", "Preset2", "Preset3", "SalvarPreset", "Pronto" };
            var criados = new Botao[6];
            for (int i = 0; i < 6; i++)
            {
                float c = util * larg[i];
                criados[i] = NovoBotao(t, nomes[i], new Vector2(x + c / 2f, y2), new Vector2(c - 0.12f, 0.8f), acoes[i]);
                x += c;
            }
            _padrao = criados[0];
            _presets[0] = criados[1]; _presets[1] = criados[2]; _presets[2] = criados[3];
            _salvarPreset = criados[4];
            _pronto = criados[5];
            AtualizarTextos();
        }

        // muda o lugar e a largura de um botão do painel (a Tecla e o Visual dividem o espaço, ou um fica com ele todo)
        static void Encaixar(Botao b, float cx, float largura)
        {
            if (Math.Abs(b.Centro.x - cx) < 0.001f && Math.Abs(b.Tamanho.x - largura) < 0.001f) return;
            b.Centro = new Vector2(cx, b.Centro.y);
            b.Tamanho = new Vector2(largura, b.Tamanho.y);
            b.Go.transform.localPosition = new Vector3(cx, b.Centro.y, 0f);
            Desenho.Tamanho(b.Fundo, largura, b.Tamanho.y);
            Desenho.Tamanho(b.Borda, largura, b.Tamanho.y);
            b.Texto.rectTransform.sizeDelta = new Vector2(largura - 0.12f, b.Tamanho.y);
        }

        // o texto que não cabe diminui (até 60%) em vez de sair da caixa
        static void Encolher(TextMeshPro t)
        {
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = t.fontSize * 0.6f;
            t.enableAutoSizing = true;
            t.overflowMode = TextOverflowModes.Overflow;
        }

        static void Posicionar()
        {
            if (_raiz == null || _cam == null) return;
            var centro = _cam.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, 10f));
            // bem perto da câmera do HUD (na frente de tudo dele), dentro do que ela enxerga
            float z = _cam.transform.position.z + _cam.nearClipPlane + 1f;
            var alvo = new Vector3(centro.x, centro.y, z);
            if ((_raiz.transform.position - alvo).sqrMagnitude > 0.00001f) _raiz.transform.position = alvo;
        }

        static Botao NovoBotao(Transform pai, string nome, Vector2 centro, Vector2 tamanho, Action acao)
        {
            var b = new Botao { Centro = centro, Tamanho = tamanho, Acao = acao };
            b.Go = new GameObject("Botao" + nome);
            b.Go.layer = Camada;
            b.Go.transform.SetParent(pai, false);
            b.Go.transform.localPosition = new Vector3(centro.x, centro.y, 0f);
            b.Fundo = Desenho.Caixa(b.Go.transform, "Fundo", tamanho.x, tamanho.y, FundoDoBotao, Ordem + 1, Camada);
            b.Borda = Desenho.Caixa(b.Go.transform, "Borda", tamanho.x, tamanho.y, BordaDoBotao, Ordem + 2, Camada, true);
            b.Texto = Desenho.Texto(b.Go.transform, "Texto", _modelo, 0.21f, Color.white, Ordem + 3, Camada);
            b.Texto.rectTransform.sizeDelta = new Vector2(tamanho.x - 0.12f, tamanho.y);
            b.Texto.enableWordWrapping = true;
            Encolher(b.Texto);
            _botoes.Add(b);
            return b;
        }

        const float Encolhe = 0.3f;

        static Barra NovaBarra(Transform pai, string nome, Vector2 centro, float largura, float min, float max)
        {
            var b = new Barra { Centro = centro, Tamanho = new Vector2(largura, 0.8f), Min = min, Max = max, Nome = nome };
            b.Go = new GameObject("Barra" + nome);
            b.Go.layer = Camada;
            b.Go.transform.SetParent(pai, false);
            b.Go.transform.localPosition = new Vector3(centro.x, centro.y, 0f);
            // a caixa redonda tem no mínimo 0,5 de altura: desenhada maior e encolhida por igual, vira um trilho de 0,15 com as pontas redondas
            b.Trilho = Desenho.Caixa(b.Go.transform, "Trilho", largura / Encolhe, 0.5f, new Color(1f, 1f, 1f, 0.16f), Ordem + 1, Camada);
            b.Trilho.transform.localPosition = new Vector3(0f, -0.17f, 0f);
            b.Trilho.transform.localScale = new Vector3(Encolhe, Encolhe, 1f);
            b.Cheio = Desenho.Caixa(b.Go.transform, "Cheio", largura / Encolhe, 0.5f, Verde, Ordem + 2, Camada);
            b.Cheio.transform.localScale = new Vector3(Encolhe, Encolhe, 1f);
            b.Pino = Desenho.Retangulo(b.Go.transform, "Pino", 0.36f, 0.36f, Color.white, Ordem + 3, Camada);
            b.Pino.sprite = Desenho.Circulo();
            b.Rotulo = Desenho.Texto(b.Go.transform, "Rotulo", _modelo, 0.19f, Color.white, Ordem + 3, Camada);
            b.Rotulo.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            b.Rotulo.rectTransform.sizeDelta = new Vector2(largura + 0.2f, 0.4f);
            return b;
        }

        static void PorBarra(Barra b, float valor, bool ativa)
        {
            float f = Mathf.Clamp01((valor - b.Min) / (b.Max - b.Min));
            float largura = b.Tamanho.x;
            float cheio = Math.Max(0.5f * Encolhe, largura * f);
            Desenho.Tamanho(b.Cheio, cheio / Encolhe, 0.5f);
            b.Cheio.transform.localPosition = new Vector3(-largura / 2f + cheio / 2f, -0.17f, 0f);
            b.Pino.transform.localPosition = new Vector3(-largura / 2f + largura * f, -0.17f, 0f);
            string texto = b.Nome + "  <b>" + Mathf.RoundToInt(valor * 100f) + "%</b>";
            if (b.Rotulo.text != texto) b.Rotulo.text = texto;
            b.Rotulo.color = ativa ? Color.white : Apagado;
            b.Pino.color = ativa ? Color.white : Apagado;
            b.Cheio.color = ativa ? Verde : Apagado;
        }

        // ---------------- todo quadro ----------------

        /// <summary>Do LateUpdate da Otimizacao, depois do LayoutDoHud (os contornos seguem o lugar posto neste quadro).</summary>
        internal static void Quadro()
        {
            if (!Aberto) return;
            if (!HudManager.InstanceExists || HudManager.Instance.Pointer != _h.Pointer || _cam == null) { Fechar(true); return; }
            if (MeetingHud.Instance != null) { Fechar(true); return; }
            try
            {
                Posicionar();
                if (Input.GetKeyDown(KeyCode.Escape)) { Fechar(true); return; }   // o Voltar do celular também chega como Escape
                if (_esperandoTecla) EsperarTecla();
                Ponteiro();
                if (!Aberto) return;   // (12/11, celular) o "Pronto" fecha o editor no meio do quadro: o resto não tem mais onde desenhar
                Marcas();
                LugarDoPainel();
                AtualizarTextos();
            }
            catch (Exception e)
            {
                Log.LogWarning("[hud] editor: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                Fechar(true);
            }
        }

        // O painel foge: fica em cima, e desce quando o item escolhido está na metade de cima; some enquanto o dedo arrasta.
        static void LugarDoPainel()
        {
            bool arrastando = _pegou == 4 && _arrastou;
            if (_painelGo.activeSelf == arrastando) _painelGo.SetActive(!arrastando);
            float alvo = 1.05f;
            var peca = PecaSel();
            if (peca != null && CaixaDaPeca(peca, out var caixa, out _) && LocalDoMundo(caixa.center).y > 0.1f) alvo = -1.05f;
            if (Math.Abs(alvo - _painelY) > 0.001f)
            {
                _painelY = alvo;
                _painelGo.transform.localPosition = new Vector3(_painelX, _painelY, 0f);
            }
        }

        static Vector2 Local(Vector2 telaPx)
        {
            var w = _cam.ScreenToWorldPoint(new Vector3(telaPx.x, telaPx.y, 10f));
            var c = _raiz.transform.position;
            return new Vector2(w.x - c.x, w.y - c.y);
        }

        static Vector2 LocalDoMundo(Vector3 mundo)
        {
            var c = _raiz.transform.position;
            return new Vector2(mundo.x - c.x, mundo.y - c.y);
        }

        // do espaço da câmera para o do painel (que sobe e desce)
        static Vector2 NoPainel(Vector2 p) => new Vector2(p.x - _painelX, p.y - _painelY);

        static bool Dentro(Vector2 p, Vector2 centro, Vector2 tam, float folga = 0f) =>
            Math.Abs(p.x - centro.x) <= tam.x / 2f + folga && Math.Abs(p.y - centro.y) <= tam.y / 2f + folga;

        static void Ponteiro()
        {
            // o dedo (celular) ou o mouse (PC): desceu, segurando, subiu, e onde
            bool desceu = false, segura = false, subiu = false;
            Vector2 tela = default;
            if (Input.touchCount > 0)
            {
                var toque = Input.GetTouch(0);
                tela = toque.position;
                desceu = toque.phase == TouchPhase.Began;
                subiu = toque.phase == TouchPhase.Ended || toque.phase == TouchPhase.Canceled;
                segura = !subiu;
            }
            else if (Plataforma.Pc)
            {
                tela = Input.mousePosition;
                desceu = Input.GetMouseButtonDown(0);
                segura = Input.GetMouseButton(0);
                subiu = Input.GetMouseButtonUp(0);
            }
            else if (_segurava) subiu = true;   // o dedo saiu da tela sem o "Ended" chegar

            if (!desceu && !segura && !subiu) { _segurava = false; return; }
            var p = subiu && tela == default ? _ponteiroInicio : Local(tela);

            if (desceu || (segura && !_segurava))
            {
                _segurava = true;
                _ponteiroInicio = p;
                _arrastou = false;
                Descer(p);
            }
            else if (segura) Arrastar(p);
            if (subiu)
            {
                Soltar(p);
                _segurava = false;
                _pegou = 0;
                _botaoPegado = null;
            }
        }

        static void Descer(Vector2 p)
        {
            _pegou = 0;
            // primeiro os controles do painel (ele fica por cima); no vão entre eles o toque passa para o HUD de baixo
            var pp = NoPainel(p);
            if (_painelGo.activeSelf && Dentro(pp, Vector2.zero, new Vector2(_w, AlturaDoPainel)))
            {
                if (_sel != null && Dentro(pp, _tamanho.Centro, _tamanho.Tamanho, 0.12f)) { _pegou = 1; Arrastar(p); return; }
                if (_sel != null && Dentro(pp, _opacidade.Centro, _opacidade.Tamanho, 0.12f)) { _pegou = 2; Arrastar(p); return; }
                foreach (var b in _botoes)
                    if (b.Go.activeSelf && Dentro(pp, b.Centro, b.Tamanho, 0.05f)) { _pegou = 3; _botaoPegado = b; return; }
            }
            // depois as peças do HUD: a menor que tem o ponto (com uma folga para o dedo)
            LayoutDoHud.Peca melhor = null;
            float area = float.MaxValue;
            Vector2 pivo = default;
            foreach (var peca in LayoutDoHud.Pecas)
            {
                if (!CaixaDaPeca(peca, out var caixa, out var piv)) continue;
                var c = LocalDoMundo(caixa.center);
                var tam = new Vector2(caixa.size.x, caixa.size.y);
                if (!Dentro(p, c, tam, 0.15f)) continue;
                float a = tam.x * tam.y;
                if (a < area) { area = a; melhor = peca; pivo = piv; }
            }
            if (melhor == null)
            {
                // fora de tudo (e fora do painel): nada escolhido, e o painel mostra o Modo escuro
                if (!_painelGo.activeSelf || !Dentro(pp, Vector2.zero, new Vector2(_w, AlturaDoPainel))) Escolher(null);
                return;
            }
            Escolher(melhor.Chave);
            _pegou = 4;
            _pivoInicio = pivo;
        }

        static void Escolher(string chave)
        {
            if (_sel != chave) _esperandoTecla = false;
            _sel = chave;
            var peca = PecaSel();
            ChatDoEditor(peca != null && peca.DoChat);
            var it = chave != null ? LayoutDoHud.Item(chave) : null;
            _deslocInicio = it != null && it.Movido ? new Vector2(it.X, it.Y) : Vector2.zero;
        }

        // o chat abre quando uma peça dele é escolhida, e fecha quando outra é (só se fui eu que abri)
        static void ChatDoEditor(bool aberto)
        {
            try
            {
                var ch = _h != null ? _h.Chat : null;
                if (ch == null) return;
                if (aberto && !ch.IsOpenOrOpening) { ch.Toggle(); _abriuChat = true; }
                else if (!aberto && _abriuChat && ch.IsOpenOrOpening) { ch.ForceClosed(); _abriuChat = false; }
            }
            catch (Exception e) { Log.LogWarning("[hud] chat no editor: " + e.Message); }
        }

        static void Arrastar(Vector2 p)
        {
            if (_pegou == 1 || _pegou == 2)
            {
                var b = _pegou == 1 ? _tamanho : _opacidade;
                float f = Mathf.Clamp01((NoPainel(p).x - (b.Centro.x - b.Tamanho.x / 2f)) / b.Tamanho.x);
                float v = b.Min + (b.Max - b.Min) * f;
                v = (float)Math.Round(v / 0.05f) * 0.05f;   // de 5 em 5%
                var it = LayoutDoHud.Item(_sel, true);
                if (_pegou == 1) it.Escala = v; else it.Opacidade = v;
                return;
            }
            if (_pegou != 4 || _sel == null) return;
            var d = p - _ponteiroInicio;
            if (!_arrastou && d.magnitude < 0.08f) return;
            _arrastou = true;
            Mover(PecaSel(), d);
        }

        static void Mover(LayoutDoHud.Peca peca, Vector2 d)
        {
            if (peca == null) return;
            var it = LayoutDoHud.Item(peca.Chave, true);
            if (peca.Deslocamento)
            {
                // a lista de tarefas anda pelo deslocamento do lugar do jogo (ver LayoutDoHud.Peca)
                it.X = Mathf.Clamp(_deslocInicio.x + d.x, -_metadeL * 2f, _metadeL * 2f);
                it.Y = Mathf.Clamp(_deslocInicio.y + d.y, -_metadeA * 2f, _metadeA * 2f);
                it.Movido = true;
                return;
            }
            var novo = _pivoInicio + d;
            // não sai da tela
            novo.x = Mathf.Clamp(novo.x, -_metadeL + 0.2f, _metadeL - 0.2f);
            novo.y = Mathf.Clamp(novo.y, -_metadeA + 0.2f, _metadeA - 0.2f);
            var c = _raiz.transform.position;
            LayoutDoHud.Guardar(it, new Vector2(novo.x + c.x, novo.y + c.y), _cam);
        }

        static void Soltar(Vector2 p)
        {
            if (_pegou == 3 && _botaoPegado != null && Dentro(NoPainel(p), _botaoPegado.Centro, _botaoPegado.Tamanho, 0.15f))
            {
                try { _botaoPegado.Acao?.Invoke(); }
                catch (Exception e) { Log.LogWarning("[hud] botao do editor: " + e.Message); }
            }
        }

        // a caixa (no mundo) e o pivô (no espaço do painel) de uma peça; false se ela não está na tela
        static bool CaixaDaPeca(LayoutDoHud.Peca peca, out Bounds caixa, out Vector2 pivo)
        {
            caixa = default;
            pivo = default;
            if (peca.SoVisual) return false;
            List<GameObject> objs;
            try { objs = peca.Objetos(_h); } catch { return false; }
            bool tem = false;
            foreach (var go in objs)
            {
                if (go == null || !go.activeInHierarchy) continue;
                Bounds b;
                // (a janela do chat passa de 6 unidades: o fundo dela conta)
                if (!Desenho.Caixa(go, out b, peca.Chave == "chatjanela" ? 14f : 6f))
                {
                    // botão sem desenho (habilidade sem papel): um quadrado do tamanho de um botão, onde ele fica
                    float s = 0.8f * Math.Max(0.3f, Math.Abs(go.transform.lossyScale.x)) / 0.7f;
                    b = new Bounds(go.transform.position, new Vector3(s, s, 0.1f));
                }
                if (!tem) { caixa = b; pivo = LocalDoMundo(go.transform.position); tem = true; }
                else caixa.Encapsulate(b);
            }
            return tem;
        }

        // o contorno de cada peça e, nas que não têm nome escrito, a etiqueta
        static readonly Dictionary<string, Rect> _caixas = new Dictionary<string, Rect>();
        static readonly List<Rect> _etiquetas = new List<Rect>();
        const float AlturaDaEtiqueta = 0.31f;

        static void Marcas()
        {
            // 1) onde está cada peça (no espaço do editor), para as etiquetas não cobrirem nenhuma
            _caixas.Clear();
            foreach (var peca in LayoutDoHud.Pecas)
                if (CaixaDaPeca(peca, out var caixa, out _))
                {
                    var c = LocalDoMundo(caixa.center);
                    float w = caixa.size.x + 0.16f, h = caixa.size.y + 0.16f;
                    _caixas[peca.Chave] = new Rect(c.x - w / 2f, c.y - h / 2f, w, h);
                }
            _etiquetas.Clear();

            // 2) o contorno e a etiqueta
            foreach (var peca in LayoutDoHud.Pecas)
            {
                _marcas.TryGetValue(peca.Chave, out var m);
                if (!_caixas.TryGetValue(peca.Chave, out var r))
                {
                    if (m != null && m.Go.activeSelf) m.Go.SetActive(false);
                    continue;
                }
                if (m == null)
                {
                    m = new Marca { Go = new GameObject("Marca_" + peca.Chave) };
                    m.Go.layer = Camada;
                    m.Go.transform.SetParent(_raiz.transform, false);
                    m.Contorno = Desenho.Caixa(m.Go.transform, "Contorno", 1f, 1f, ContornoDeLeve, Ordem - 10, Camada, true);
                    if (!TemNomeEscrito(peca))
                    {
                        m.FundoDaEtiqueta = Desenho.Caixa(m.Go.transform, "FundoDaEtiqueta", 1f, 0.5f, FundoDaEtiqueta, Ordem - 9, Camada);
                        m.Etiqueta = Desenho.Texto(m.Go.transform, "Etiqueta", _modelo, 0.15f, Color.white, Ordem - 8, Camada);
                        m.Etiqueta.text = peca.Nome;
                        m.Etiqueta.rectTransform.sizeDelta = new Vector2(3f, 0.3f);
                    }
                    _marcas[peca.Chave] = m;
                }
                if (!m.Go.activeSelf) m.Go.SetActive(true);
                var centro = r.center;
                m.Go.transform.localPosition = new Vector3(centro.x, centro.y, 0f);
                bool escolhida = peca.Chave == _sel;
                Desenho.Tamanho(m.Contorno, r.width, r.height);
                var cor = escolhida ? Amarelo : ContornoDeLeve;
                if (m.Contorno.color != cor) m.Contorno.color = cor;
                if (m.Etiqueta == null) continue;

                float largura = Math.Min(3f, m.Etiqueta.GetPreferredValues(peca.Nome).x + 0.3f);
                var lugar = LugarDaEtiqueta(peca.Chave, r, largura);
                _etiquetas.Add(new Rect(lugar.x - largura / 2f, lugar.y - AlturaDaEtiqueta / 2f, largura, AlturaDaEtiqueta));
                var local = new Vector3(lugar.x - centro.x, lugar.y - centro.y, 0f);
                m.Etiqueta.transform.localPosition = local;
                m.FundoDaEtiqueta.transform.localPosition = local;
                Desenho.Tamanho(m.FundoDaEtiqueta, largura / 0.62f, 0.5f);
                m.FundoDaEtiqueta.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
                var corTexto = escolhida ? Amarelo : Color.white;
                if (m.Etiqueta.color != corTexto) m.Etiqueta.color = corTexto;
            }
        }

        // em cima, embaixo, à direita ou à esquerda da peça: o primeiro lugar que cabe na tela e não cobre outra peça nem outra
        // etiqueta; se nenhum serve, o que cobre menos
        static Vector2 LugarDaEtiqueta(string chave, Rect r, float largura)
        {
            float a = AlturaDaEtiqueta, folga = 0.06f;
            var tentativas = new[]
            {
                new Vector2(r.center.x, r.yMax + a / 2f + folga),
                new Vector2(r.center.x, r.yMin - a / 2f - folga),
                new Vector2(r.xMax + largura / 2f + folga, r.center.y),
                new Vector2(r.xMin - largura / 2f - folga, r.center.y),
            };
            var melhor = new Vector2(r.center.x, r.yMax - a / 2f - folga);   // dentro da peça, no alto (se nada couber na tela)
            float menos = float.MaxValue;
            foreach (var p in tentativas)
            {
                var e = new Rect(p.x - largura / 2f, p.y - a / 2f, largura, a);
                if (e.xMin < -_metadeL || e.xMax > _metadeL || e.yMin < -_metadeA || e.yMax > _metadeA) continue;
                float cobre = 0f;
                foreach (var kv in _caixas) if (kv.Key != chave) cobre += Cobre(kv.Value, e);
                foreach (var o in _etiquetas) cobre += Cobre(o, e) * 2f;   // texto em cima de texto é o pior
                if (cobre <= 0f) return p;
                if (cobre < menos) { menos = cobre; melhor = p; }
            }
            return melhor;
        }

        static float Cobre(Rect x, Rect y)
        {
            float w = Math.Min(x.xMax, y.xMax) - Math.Max(x.xMin, y.xMin), h = Math.Min(x.yMax, y.yMax) - Math.Max(x.yMin, y.yMin);
            return w > 0f && h > 0f ? w * h : 0f;
        }

        // os botões de ação do jogo já têm o nome escrito (USAR, MATAR...): a etiqueta só atrapalharia
        static bool TemNomeEscrito(LayoutDoHud.Peca peca)
        {
            try
            {
                foreach (var go in peca.Objetos(_h))
                    if (go != null && go.GetComponent<ActionButton>() != null) return true;
            }
            catch { }
            return false;
        }

        // ---------------- os botões do painel ----------------

        static LayoutDoHud.Peca PecaSel()
        {
            if (_sel == null) return null;
            foreach (var p in LayoutDoHud.Pecas) if (p.Chave == _sel) return p;
            return null;
        }

        // Tecla: o primeiro toque espera uma tecla; com tecla, o toque tira
        static void ApertarTecla()
        {
            var peca = PecaSel();
            if (peca == null || !peca.TemTecla) return;
            var it = LayoutDoHud.Item(_sel, true);
            if (_esperandoTecla) { _esperandoTecla = false; return; }
            if (!string.IsNullOrEmpty(it.Tecla)) { it.Tecla = ""; return; }
            _esperandoTecla = true;
        }

        // Visual: do jogo <-> escuro
        static void ApertarVisual()
        {
            var peca = PecaSel();
            if (peca == null || !peca.TemVisual) return;
            var it = LayoutDoHud.Item(_sel, true);
            it.Visual = it.Visual == 1 ? 0 : 1;
        }

        // sem nada escolhido: o modo escuro de tudo (normal > só o chat > completo)
        static void ApertarModoEscuro()
        {
            int m = LayoutDoHud.ProximoModoEscuro();
            LayoutDoHud.PorModoEscuro(m);
            Avisar("Modo escuro: " + LayoutDoHud.NomeDoModoEscuro(m));
        }

        static void EsperarTecla()
        {
            if (_todasAsTeclas == null)
            {
                var lista = new List<KeyCode>();
                foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
                {
                    if (k == KeyCode.None || k == KeyCode.Escape) continue;
                    if (k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) continue;
                    lista.Add(k);
                }
                _todasAsTeclas = lista.ToArray();
            }
            foreach (var k in _todasAsTeclas)
            {
                if (!Input.GetKeyDown(k)) continue;
                var it = LayoutDoHud.Item(_sel, true);
                it.Tecla = k.ToString();
                _esperandoTecla = false;
                Log.LogInfo("[hud] tecla de " + _sel + ": " + it.Tecla);
                return;
            }
        }

        static void ApertarPadrao()
        {
            float agora = Time.unscaledTime;
            if (agora < _confirmarPadraoAte)
            {
                LayoutDoHud.VoltarAoPadrao();
                _confirmarPadraoAte = 0f;
                Avisar("Tudo como o jogo");
                return;
            }
            _confirmarPadraoAte = agora + 3f;
        }

        static void ApertarSalvarPreset() => _salvandoPreset = !_salvandoPreset;

        static void ApertarPreset(int i)
        {
            if (_salvandoPreset)
            {
                LayoutDoHud.SalvarPreset(i);
                _salvandoPreset = false;
                Avisar("Salvo no " + (i + 1));
                return;
            }
            if (!LayoutDoHud.TemPreset(i)) { Avisar("O " + (i + 1) + " está vazio"); return; }
            LayoutDoHud.UsarPreset(i);
            Avisar("Usando o " + (i + 1));
        }

        static void Avisar(string texto)
        {
            _aviso = texto;
            _avisoAte = Time.unscaledTime + 2.5f;
        }

        static void Pintar(Botao b, string texto, Color fundo, bool destaque = false, bool ativo = true)
        {
            if (b.Texto.text != texto) b.Texto.text = texto;
            if (b.Fundo.color != fundo) b.Fundo.color = fundo;
            var borda = destaque ? Amarelo : BordaDoBotao;
            if (b.Borda.color != borda) b.Borda.color = borda;
            var corTexto = ativo ? Color.white : Apagado;
            if (b.Texto.color != corTexto) b.Texto.color = corTexto;
        }

        static void AtualizarTextos()
        {
            var peca = PecaSel();
            var it = _sel != null ? LayoutDoHud.Item(_sel) : null;
            float agora = Time.unscaledTime;
            bool aviso = agora < _avisoAte;
            string nome = aviso ? _aviso : peca != null ? peca.Nome : "Toque num item";
            if (_nome.text != nome) _nome.text = nome;
            _nome.color = peca != null || aviso ? Amarelo : Color.white;
            string dica = peca != null ? (peca.DoChat ? "Arraste; o chat abre para editar" : "Arraste para mover") : "Tudo com contorno se edita";
            if (_dica.text != dica) _dica.text = dica;

            PorBarra(_tamanho, it != null ? it.Escala : 1f, peca != null);
            PorBarra(_opacidade, it != null ? it.Opacidade : 1f, peca != null);

            // os botões da direita mudam com o item: Tecla e Visual (lado a lado, ou um só com o espaço todo); sem item, o Modo escuro
            bool mTecla = peca != null && peca.TemTecla, mVisual = peca != null && peca.TemVisual, mModo = peca == null;
            if (_tecla.Go.activeSelf != mTecla) _tecla.Go.SetActive(mTecla);
            if (_visual.Go.activeSelf != mVisual) _visual.Go.SetActive(mVisual);
            if (_modo.Go.activeSelf != mModo) _modo.Go.SetActive(mModo);
            float largo = _cMeio * 2f - 0.1f, estreito = _cMeio - 0.1f;
            if (mTecla && mVisual) { Encaixar(_tecla, _xCtx + _cMeio / 2f, estreito); Encaixar(_visual, _xCtx + _cMeio * 1.5f, estreito); }
            else if (mTecla) Encaixar(_tecla, _xCtx + _cMeio, largo);
            else if (mVisual) Encaixar(_visual, _xCtx + _cMeio, largo);
            if (mVisual)
            {
                bool escuro = it != null && it.Visual == 1;
                Pintar(_visual, "Visual\n<b>" + LayoutDoHud.NomeDoVisual(escuro ? 1 : 0) + "</b>", escuro ? VerdeEscuro : FundoDoBotao);
            }
            if (mTecla)
            {
                if (_esperandoTecla) Pintar(_tecla, "Aperte uma tecla", FundoDoBotao, true);
                else
                {
                    bool tem = it != null && !string.IsNullOrEmpty(it.Tecla);
                    Pintar(_tecla, "Tecla\n<b>" + LayoutDoHud.NomeDaTecla(tem ? it.Tecla : "") + "</b>", tem ? VerdeEscuro : FundoDoBotao);
                }
            }
            if (mModo)
            {
                int m = LayoutDoHud.ModoEscuro();
                Pintar(_modo, "Modo escuro\n<b>" + LayoutDoHud.NomeDoModoEscuro(m) + "</b>", m != LayoutDoHud.EscuroNormal ? VerdeEscuro : FundoDoBotao);
            }

            bool confirmando = agora < _confirmarPadraoAte;
            Pintar(_padrao, confirmando ? "Toque de novo" : "Padrão", confirmando ? VermelhoEscuro : FundoDoBotao, confirmando);
            for (int i = 0; i < 3; i++)
            {
                bool salvo = LayoutDoHud.TemPreset(i);
                Pintar(_presets[i], (i + 1).ToString(), _salvandoPreset ? FundoDoBotao : salvo ? VerdeEscuro : FundoDoBotao, _salvandoPreset, _salvandoPreset || salvo);
            }
            Pintar(_salvarPreset, _salvandoPreset ? "Toque no número" : "Salvar preset", FundoDoBotao, _salvandoPreset);
            Pintar(_pronto, "<b>Pronto</b>", VerdeEscuro);
        }

#if OTIM_TESTE
        // Só no piloto do PC: o que o dedo faria.
        internal static void TesteEscolher(string chave) => Escolher(chave);
        internal static void TesteTamanho(float v) { if (_sel != null) LayoutDoHud.Item(_sel, true).Escala = v; }
        internal static void TesteOpacidade(float v) { if (_sel != null) LayoutDoHud.Item(_sel, true).Opacidade = v; }
        internal static void TesteMover(float dx, float dy)
        {
            var peca = PecaSel();
            if (peca == null || !CaixaDaPeca(peca, out _, out var piv)) return;
            _pivoInicio = piv;
            Mover(peca, new Vector2(dx, dy));
        }
        internal static void TesteTecla(string tecla) { if (_sel != null) LayoutDoHud.Item(_sel, true).Tecla = tecla; }
        internal static void TesteBotao(string qual)
        {
            foreach (var b in _botoes.ToArray()) if (b.Go != null && b.Go.name == "Botao" + qual) { b.Acao?.Invoke(); return; }
        }
        internal static string TesteEstado() => Aberto ? "aberto, escolhido " + (_sel ?? "nada") + ", " + _ligados.Count + " ligados, " + _colisores.Count + " colisores desligados, chat " + (_abriuChat ? "aberto pelo editor" : "-") + ", modo escuro " + LayoutDoHud.NomeDoModoEscuro(LayoutDoHud.ModoEscuro()) : "fechado";
#endif
    }
}
