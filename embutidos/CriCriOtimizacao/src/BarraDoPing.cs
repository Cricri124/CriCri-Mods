using System;
using TMPro;
using UnityEngine;

namespace CriCriMods
{
    /*
      (12/11, ele: "faça o mostrar fps ao invés de ficar na tela ali, ele fica lá na parte de baixo aonde fica o Ping, e ter como
      personalizar o ping com um símbolo de wifi que muda quando tá bom, médio ou ruim, e colocar tipo assim: (Wifi verde) 23ms
      (Verde) 60 Fps ... e também com a contagem de fps mais estável ... e também personalizar mais aparecendo o servidor que você
      está, ou tirar completamente isso")

      O jogo escreve "PING: <b>120 ms</b>" num texto do HUD (PingTracker, em Hud/SimpleTextLabels, embaixo no meio; no Freeplay ele
      fica desligado: não há rede). Esta barra fica no MESMO lugar e no lugar dele: o wifi (3 arcos verdes até 100 ms, 2 amarelos
      até 200, 1 vermelho acima), os milissegundos, o FPS (verde, amarelo, vermelho, as faixas que ele escolheu em 08/11) e, se a
      pessoa quiser, o nome do servidor. O texto do jogo fica escondido enquanto a barra existe. Nada é enviado; o ping é o número
      que o próprio jogo mede (InnerNetClient.Ping).

      Os modos (Opcoes.BarraPing, um botão só em Definições): 0 só o ping, 1 ping e FPS, 2 ping, FPS e servidor, 3 escondido
      (nem o do jogo aparece). No Freeplay e onde o jogo não mostra ping, a barra mostra só o FPS (nos modos que têm FPS).
      No menu principal não existe o texto do ping: lá o FPS continua no contador de antes (Otimizacao.AtualizarContador).

      O editor do HUD (EditorDoHud) arrasta esta barra como os botões: ela é o elemento "ping" do LayoutDoHud.
    */
    internal static class BarraDoPing
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        internal const int SoPing = 0, PingEFps = 1, ComServidor = 2, Escondido = 3;

        internal static string NomeDoModo(int modo) => modo switch
        {
            SoPing => "só o ping",
            PingEFps => "com FPS",
            ComServidor => "FPS e servidor",
            Escondido => "escondido",
            _ => "com FPS",
        };

        internal static readonly Color Verde = new Color32(92, 226, 92, 255);
        internal static readonly Color Amarelo = new Color32(246, 206, 72, 255);
        internal static readonly Color Vermelho = new Color32(244, 96, 84, 255);
        static readonly Color Cinza = new Color32(205, 205, 205, 255);

        static GameObject _raiz;
        static TextMeshPro _texto;
        static SpriteRenderer _wifi;
        static PingTracker _ping;
        static IntPtr _hud;
        static float _proxTexto, _proxProcura, _alturaDaLinha;
        static string _textoPosto;
        static bool _avisou;
        static float _desvio;   // quanto a barra desce (no mundo) para não cair em cima do nome da sala

        /// <summary>A barra que existe agora (para o editor do HUD), ou null.</summary>
        internal static GameObject Raiz => _raiz != null && _raiz.activeSelf ? _raiz : null;

        /// <summary>O texto do ping do jogo nesta cena (o lugar da barra), ou null.</summary>
        internal static PingTracker PingDoJogo => _ping;

        /// <summary>Do LateUpdate da Otimizacao, todo quadro. Barato quando nada muda.</summary>
        internal static void Quadro()
        {
            int modo = Opcoes.Atual.BarraPing;
            float agora = Time.unscaledTime;
            if (!HudManager.InstanceExists) { _ping = null; _raiz = null; return; }
            var hud = HudManager.Instance;
            if (hud.Pointer != _hud || (_ping == null && agora >= _proxProcura))
            {
                _hud = hud.Pointer;
                _proxProcura = agora + 2f;
                _ping = null;
                _raiz = null;
                var achados = hud.GetComponentsInChildren<PingTracker>(true);
                if (achados != null && achados.Length > 0) _ping = achados[0];
                if (_ping != null) Montar();
            }
            if (_ping == null || _raiz == null) return;

            // o texto do jogo nunca aparece junto da barra (e some de vez no "escondido")
            var doJogo = _ping.text;
            if (doJogo != null && doJogo.enabled) doJogo.enabled = false;

            bool online = _ping.gameObject.activeInHierarchy;   // o jogo só liga o ping com rede
#if OTIM_TESTE
            if (TesteMs >= 0) online = true;   // o piloto do PC finge a rede para ver o wifi no Freeplay
#endif
            bool fps = modo == PingEFps || modo == ComServidor;
            bool mostra = modo != Escondido && (online || fps);
            if (_raiz.activeSelf != mostra) _raiz.SetActive(mostra);
            if (!mostra) return;

            // o lugar: o do ping do jogo (o editor do HUD move depois, se a pessoa arrastou)
            var t = _raiz.transform;
            var p = _ping.transform;
            var lugar = p.localPosition;
            // (12/11, visto no Freeplay) Com o ping do jogo desligado, o lugar dele fica o da sala de espera (mais alto) e a barra
            // caía em cima do nome da sala. Aí ela vai para o lugar que o jogo usa na partida (gamePos, mais baixo).
            if (!online)
            {
                try { lugar.y += _ping.gamePos.y - _ping.lobbyPos.y; } catch { }
            }
            if (_desvio > 0f)
            {
                float sy = t.parent != null ? Math.Max(0.0001f, Math.Abs(t.parent.lossyScale.y)) : 1f;
                lugar.y -= _desvio / sy;
            }
            if (t.localPosition != lugar) t.localPosition = lugar;
            if (t.localScale != p.localScale) t.localScale = p.localScale;

            if (agora < _proxTexto) return;
            _proxTexto = agora + 0.25f;
            Escrever(modo, online, fps);
        }

        static void Montar()
        {
            try
            {
                var doJogo = _ping.text;
                var pai = _ping.transform.parent;
                _raiz = new GameObject("CriCriBarraDoPing");
                _raiz.layer = _ping.gameObject.layer;
                _raiz.transform.SetParent(pai, false);
                _raiz.transform.localPosition = _ping.transform.localPosition;
                _raiz.transform.localScale = _ping.transform.localScale;

                var go = new GameObject("Texto");
                go.layer = _raiz.layer;
                go.transform.SetParent(_raiz.transform, false);
                _texto = go.AddComponent<TextMeshPro>();
                if (doJogo != null)
                {
                    _texto.font = doJogo.font;
                    _texto.fontSharedMaterial = doJogo.fontSharedMaterial;
                    _texto.fontSize = doJogo.fontSize;
                    // (12/11, celular: "o fps e ms não estão centralizados embaixo") o texto centralizado na horizontal (a altura
                    // continua a do jogo); o wifi e o texto juntos ficam no meio do lugar do ping (Escrever)
                    _texto.alignment = (TextAlignmentOptions)(((int)doJogo.alignment & ~0xFF) | (int)TextAlignmentOptions.Center & 0xFF);
                    _texto.color = Color.white;
                    _texto.rectTransform.pivot = doJogo.rectTransform.pivot;
                    _texto.rectTransform.sizeDelta = doJogo.rectTransform.sizeDelta;
                    var mrJogo = doJogo.GetComponent<MeshRenderer>();
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mrJogo != null && mr != null) { mr.sortingLayerID = mrJogo.sortingLayerID; mr.sortingOrder = mrJogo.sortingOrder; }
                }
                _texto.richText = true;
                _texto.enableWordWrapping = false;
                _alturaDaLinha = Math.Max(0.05f, _texto.GetPreferredValues("Hg").y);

                var w = new GameObject("Wifi");
                w.layer = _raiz.layer;
                w.transform.SetParent(_raiz.transform, false);
                _wifi = w.AddComponent<SpriteRenderer>();
                _wifi.sprite = Desenho.Wifi(3);
                var mrTexto = go.GetComponent<MeshRenderer>();
                if (mrTexto != null) { _wifi.sortingLayerID = mrTexto.sortingLayerID; _wifi.sortingOrder = mrTexto.sortingOrder; }
                _textoPosto = null;
                _proxTexto = 0f;
                if (!_avisou)
                {
                    _avisou = true;
                    Log.LogInfo("[ping] barra do ping montada no lugar de " + _ping.name + " (letra " + _texto.fontSize.ToString("0.0") + ", linha " + _alturaDaLinha.ToString("0.00") + ")");
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("[ping] nao montei a barra: " + e.GetType().Name + ": " + e.Message);
                if (_raiz != null) UnityEngine.Object.Destroy(_raiz);
                _raiz = null;
            }
        }

        static void Escrever(int modo, bool online, bool fps)
        {
            var sb = new System.Text.StringBuilder(96);
            int ms = -1;
            if (online)
            {
                try { var c = AmongUsClient.Instance; if (c != null) ms = c.Ping; } catch { }
#if OTIM_TESTE
                if (TesteMs >= 0) ms = TesteMs;
#endif
                if (ms < 0) ms = 0;
                sb.Append("<color=").Append(Desenho.Hex(CorDoPing(ms))).Append('>').Append(ms).Append("ms</color>");
            }
            if (fps)
            {
                int f = Fps.Mostrado;
                if (sb.Length > 0) sb.Append("    ");
                sb.Append("<color=").Append(Desenho.Hex(CorDoFps(f))).Append('>').Append(f).Append(" FPS</color>");
            }
            if (modo == ComServidor && online)
            {
                string servidor = NomeDoServidor();
                if (!string.IsNullOrEmpty(servidor)) sb.Append("    <color=").Append(Desenho.Hex(Cinza)).Append('>').Append(servidor).Append("</color>");
            }
            string texto = sb.ToString();
            if (texto != _textoPosto)
            {
                _textoPosto = texto;
                _texto.text = texto;
            }

            // o wifi colado na frente do texto, da altura da letra, e os dois juntos no meio do lugar do ping (antes o texto ia
            // para a direita e o wifi para a ponta da caixa: o conjunto ficava torto); sem rede o wifi some
            var rect = _texto.rectTransform.rect;
            float h = _alturaDaLinha * 0.95f;
            float largura = h * (64f / 48f);
            if (_wifi.gameObject.activeSelf != online) _wifi.gameObject.SetActive(online);
            float vao = h * 0.3f;
            float larguraDoTexto = string.IsNullOrEmpty(texto) ? 0f : _texto.GetPreferredValues(texto).x;
            float desloca = online ? (largura + vao) / 2f : 0f;
            var tp = new Vector3(desloca, 0f, 0f);
            if (_texto.transform.localPosition != tp) _texto.transform.localPosition = tp;
            float esquerda = rect.center.x - (larguraDoTexto + (online ? largura + vao : 0f)) / 2f;
            DesviarDaSala();
            if (online)
            {
                int acesos = ms <= 100 ? 3 : ms <= 200 ? 2 : 1;
                var sprite = Desenho.Wifi(acesos);
                if (_wifi.sprite != sprite) _wifi.sprite = sprite;
                _wifi.color = CorDoPing(ms);
                // o sprite tem 1 unidade de altura e o pivô embaixo no meio
                _wifi.transform.localScale = new Vector3(h, h, 1f);
                _wifi.transform.localPosition = new Vector3(esquerda + largura / 2f, rect.yMax - _alturaDaLinha * 0.9f, 0f);
            }
        }

        /* (12/11, foto do celular: "ele tá em cima do refeitório") Online, a barra fica no lugar do ping do jogo, e na partida esse
           lugar cai em cima do nome da sala (o "Refeitório" embaixo no meio). Se as duas se cruzam, a barra desce até ficar logo
           abaixo do nome (sem sair da tela); sem o nome na tela, volta para o lugar do ping. Se a pessoa arrastou a barra no editor
           do HUD, vale o lugar dela (o LayoutDoHud põe depois). Medido a cada troca do texto (4 vezes por segundo). */
        static void DesviarDaSala()
        {
            float novo = 0f;
            try
            {
                var h = HudManager.Instance;
                var sala = h != null && h.roomTracker != null ? h.roomTracker.text : null;
                var mrSala = sala != null ? sala.GetComponent<MeshRenderer>() : null;
                var mrBarra = _texto.GetComponent<MeshRenderer>();
                if (mrSala != null && mrBarra != null && sala.gameObject.activeInHierarchy && sala.enabled && sala.color.a > 0.05f
                    && !string.IsNullOrEmpty(sala.text) && !string.IsNullOrEmpty(_texto.text))
                {
                    var a = mrSala.bounds;
                    var b = mrBarra.bounds;
                    // onde a barra estaria sem o desvio
                    float baixo = b.min.y + _desvio, alto = b.max.y + _desvio;
                    float folga = (alto - baixo) * 0.15f;
                    bool cruzaX = b.max.x > a.min.x && b.min.x < a.max.x;
                    bool cruzaY = baixo < a.max.y + folga && alto > a.min.y - folga;
                    if (cruzaX && cruzaY)
                    {
                        novo = alto - (a.min.y - folga);
                        // não sai da tela: a parte de baixo da barra fica dentro da câmera do HUD
                        var cam = h.UICamera;
                        if (cam != null)
                        {
                            float chao = cam.ViewportToWorldPoint(new Vector3(0.5f, 0f, 10f)).y + folga;
                            novo = Math.Min(novo, Math.Max(0f, baixo - chao));
                        }
                    }
                }
            }
            catch { novo = 0f; }
            _desvio = Math.Max(0f, novo);
        }

#if OTIM_TESTE
        internal static int TesteMs = -1;
        internal static float TesteDesvio => _desvio;
#endif

        internal static Color CorDoPing(int ms) => ms <= 100 ? Verde : ms <= 200 ? Amarelo : Vermelho;

        // as faixas fixas dele (08/11): até 30 vermelho, 31 a 45 amarelo, 46 ou mais verde
        internal static Color CorDoFps(int fps) => fps >= 46 ? Verde : fps >= 31 ? Amarelo : Vermelho;

        static string NomeDoServidor()
        {
            try
            {
                if (!DestroyableSingleton<ServerManager>.InstanceExists) return null;
                var r = DestroyableSingleton<ServerManager>.Instance.CurrentRegion;
                string nome = r != null ? r.Name : null;
                if (string.IsNullOrEmpty(nome)) return null;
                // só texto puro: o nome vem do arquivo de regiões, que outro programa pode ter escrito
                var sb = new System.Text.StringBuilder(nome.Length);
                foreach (char c in nome)
                {
                    if (sb.Length >= 24) break;
                    sb.Append(c == '<' || c == '>' || c == '\\' || c < ' ' ? ' ' : c);
                }
                return sb.ToString().Trim();
            }
            catch { return null; }
        }
    }

    /*
      (12/11, ele: "a contagem de fps mais estável") O FPS mostrado: a média do tempo do quadro com memória de meio segundo,
      atualizada 2 vezes por segundo, e que só troca o número quando a mudança passa de 1 (o 59/60/61 piscando some). Perto do
      limite de quadros (o alvo do jogo ou da tela), mostra o limite. É só o número da tela: o jogo não muda.
    */
    internal static class Fps
    {
        static float _media = 1f / 60f;
        static float _proxMostrar;
        static int _mostrado = 60;
        static bool _comecou;

        internal static int Mostrado => _mostrado;

        /// <summary>Todo quadro (Update da Otimizacao).</summary>
        internal static void Quadro()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 1f) return;   // quadro de carregamento (tela parada) não entra na média
            if (!_comecou) { _media = dt; _comecou = true; }
            float k = 1f - (float)Math.Exp(-dt / 0.5f);
            _media += (dt - _media) * k;
            float agora = Time.unscaledTime;
            if (agora < _proxMostrar) return;
            _proxMostrar = agora + 0.5f;
            float fps = _media > 0.0001f ? 1f / _media : 0f;
            int alvo = Alvo();
            int novo = (int)Math.Round(fps);
            if (alvo > 0 && fps >= alvo - 2f) novo = alvo;
            if (Math.Abs(novo - _mostrado) > 1 || novo == alvo) _mostrado = novo;
        }

        static int Alvo()
        {
            int alvo = Application.targetFrameRate;
            int hz = 0;
            try { hz = (int)Math.Round(Screen.currentResolution.refreshRateRatio.value); } catch { }
            if (QualitySettings.vSyncCount > 0 && hz > 0) return alvo > 0 ? Math.Min(alvo, hz) : hz;
            return alvo > 0 ? alvo : 0;
        }
    }
}
