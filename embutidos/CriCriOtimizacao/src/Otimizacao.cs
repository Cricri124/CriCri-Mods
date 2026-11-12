using System;
using System.Collections.Generic;
using AmongUs.Data;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods
{
    // Runs every frame from a DontDestroyOnLoad object (Unity messages always fire on Android, unlike Harmony hooks
    // on inlined methods). Applies the options, measures FPS, smooths movement, paints colorblind names.
    public sealed class Otimizacao : MonoBehaviour
    {
        public Otimizacao(IntPtr ptr) : base(ptr) { }

        internal static Otimizacao Instancia;
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        float _proxAplicar, _proxJogadores, _proxAba, _proxLogFps = 20f;
        int _quadros;
        float _tempo, _fpsMedido;
        string _cena = "";

        // what the game had before we touched it ("do jogo")
        int _fpsDoJogo = -2, _larguraDoJogo, _alturaDoJogo, _mipmapDoJogo = -1, _aaDoJogo = -1;
        int _aaAplicado = -1;
        bool _texturasAplicadas, _logouCorpo, _coresPintadas;
        readonly HashSet<IntPtr> _corposSuaves = new HashSet<IntPtr>();
        readonly HashSet<string> _avisos = new HashSet<string>();

        TextMeshPro _contador;
        Camera _cameraDoContador;
        static readonly Action _flash = Flash.Quadro;
        static readonly Action _leve = Leve.Quadro;
        static readonly Action _sombra = Sombra.Tique;
        static readonly Action _sombraQuadro = Sombra.Quadro;
#if OTIM_DIAG
        static readonly Action _diag = Diagnostico.Quadro;
#endif

        void Start()
        {
            Instancia = this;
            GuardarDoJogo();
            LogGraficos();
            AplicarAgora();
        }

#if OTIM_TESTE
        // Só no build de teste do PC (o piloto faz os bonecos do Freeplay andarem, sem pacote).
        void FixedUpdate()
        {
            try { Piloto.Fisica(); } catch { }
        }
#endif

        // (12/11) Depois de todos os Update do jogo (o lugar dos botões, o texto do ping): a barra do ping, o HUD da pessoa e o
        // editor do HUD são postos aqui, por cima do que o jogo pôs neste quadro.
#if OTIM_TESTE
        // só no piloto do PC: os bytes que a barra do ping, o layout, o escuro e o editor alocam (medida do lixo, 12/11)
        internal static long BytesDoHud;
#endif
        void LateUpdate()
        {
#if OTIM_TESTE
            long antesDoHud = GC.GetAllocatedBytesForCurrentThread();
#endif
            try { BarraDoPing.Quadro(); }
            catch (Exception e) { if (_avisos.Add("barra" + e.GetType().Name)) Log.LogWarning("[ping] " + e.GetType().Name + ": " + e.Message); }
            try { LayoutDoHud.Quadro(); }
            catch (Exception e) { if (_avisos.Add("hud" + e.GetType().Name)) Log.LogWarning("[hud] " + e.GetType().Name + ": " + e.Message); }
            try { EditorDoHud.Quadro(); }
            catch (Exception e) { if (_avisos.Add("editor" + e.GetType().Name)) Log.LogWarning("[hud] editor: " + e.GetType().Name + ": " + e.Message); }
#if OTIM_TESTE
            BytesDoHud += GC.GetAllocatedBytesForCurrentThread() - antesDoHud;
#endif
        }

        void Update()
        {
            try { Fps.Quadro(); } catch { }
            try { LayoutDoHud.Teclas(); }
            catch (Exception e) { if (_avisos.Add("teclas" + e.GetType().Name)) Log.LogWarning("[hud] teclas: " + e.GetType().Name + ": " + e.Message); }
            _quadros++;
            _tempo += Time.unscaledDeltaTime;
            if (_tempo >= 0.5f)
            {
                _fpsMedido = _quadros / _tempo;
                _quadros = 0;
                _tempo = 0f;
                Tentar("contador", AtualizarContador);
            }

            Tentar("flash", _flash);
            Tentar("leve", _leve);
            Tentar("sombra-medida", _sombraQuadro);   // só soma o tempo enquanto o botão da sombra mede
#if OTIM_DIAG
            Tentar("diag", _diag);                    // só no APK de teste
#endif
            try { Travadas.Quadro(_cena); }
            catch (Exception e) { if (_avisos.Add("travadas" + e.GetType().Name)) Log.LogWarning("[travadas] " + e.Message); }
#if OTIM_TESTE
            try { Piloto.Quadro(_cena); }
            catch (Exception e) { if (_avisos.Add("piloto" + e.Message)) Log.LogWarning("[piloto] " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); }
#endif

            float agora = Time.unscaledTime;
            if (agora >= _proxAplicar)
            {
                _proxAplicar = agora + 2f;
                AplicarAgora();
                Tentar("cena", NovaCena);
                Tentar("selo", AjustarSelo);
            }
            if (agora >= _proxJogadores)
            {
                _proxJogadores = agora + 0.5f;
                Tentar("jogadores", Jogadores);
                Tentar("sombra", _sombra);
                // (11/11) a tela de notícias só existe no menu: na sala e na partida não há lista para vigiar
                if (_cena != "OnlineGame" && _cena != "Tutorial") Tentar("noticias", Noticias.Tique);
            }
            if (agora >= _proxAba)
            {
                _proxAba = agora + 0.5f;
                Tentar("aba", AbaCriCri.Procurar);
            }
            if (agora >= _proxLogFps)
            {
                _proxLogFps = agora + 30f;
                Log.LogInfo("[jogo] fps medido " + _fpsMedido.ToString("0") + " (alvo " + Application.targetFrameRate + ", tela "
                    + Screen.width + "x" + Screen.height + " a " + HzAtual() + " Hz, cena " + _cena + ")");
            }
        }

        // An exception here must never stop the others; each kind is logged once.
        void Tentar(string oque, Action acao)
        {
            try { acao(); }
            catch (Exception e)
            {
                if (_avisos.Add(oque + e.GetType().Name)) Log.LogWarning("[jogo] " + oque + ": " + e.GetType().Name + ": " + e.Message);
            }
        }

        internal float FpsMedido => _fpsMedido;

        static int HzAtual()
        {
            try { return (int)Math.Round(Screen.currentResolution.refreshRateRatio.value); } catch { return 0; }
        }

        void GuardarDoJogo()
        {
            _fpsDoJogo = Application.targetFrameRate;
            _larguraDoJogo = Screen.width;
            _alturaDoJogo = Screen.height;
            try { _mipmapDoJogo = QualitySettings.globalTextureMipmapLimit; } catch { }
            try { _aaDoJogo = QualitySettings.antiAliasing; } catch { }
        }

        // Logged once: what the game itself uses on this phone, to tune the defaults after a real test.
        void LogGraficos()
        {
            Tentar("graficos", () =>
            {
                string nivel = "?";
                try { nivel = QualitySettings.names[QualitySettings.GetQualityLevel()]; } catch { }
                var r = Screen.currentResolution;
                string hzs = "";
                try
                {
                    var lista = Screen.resolutions;
                    for (int i = 0; i < lista.Length; i++) hzs += (i > 0 ? "," : "") + lista[i].width + "x" + lista[i].height + "@" + Math.Round(lista[i].refreshRateRatio.value);
                }
                catch { }
                Log.LogInfo("[jogo] graficos do jogo: qualidade " + nivel + ", fps alvo " + _fpsDoJogo + ", AA " + _aaDoJogo
                    + ", mipmap limite " + _mipmapDoJogo + ", aniso " + QualitySettings.anisotropicFiltering + ", tela " + Screen.width + "x" + Screen.height
                    + ", atual " + r.width + "x" + r.height + "@" + HzAtual() + ", nativa " + Display.main.systemWidth + "x" + Display.main.systemHeight
                    + ", modos [" + hzs + "], app diz max " + Tela.Maxima() + " Hz, fixedDeltaTime " + Time.fixedDeltaTime);
            });
        }

        internal void AplicarAgora()
        {
#if OTIM_TESTE
            // Build de teste no PC do dono: resolução, FPS e texturas do jogo dele não são tocados (a Unity guarda
            // o Screen.SetResolution para a próxima abertura). O piloto solta o quadro por conta própria.
#else
            Tentar("fps", AplicarFps);
            // (11/11) No PC o tamanho da tela e o ecrã inteiro são do jogador, pelos controles do próprio jogo
            // (aba Gráficos): o mod não pede resolução lá.
            if (!Plataforma.Pc) Tentar("resolucao", AplicarResolucao);
            Tentar("qualidade", AplicarQualidade);
#endif
        }

        void AplicarFps()
        {
            var o = Opcoes.Atual;
            int atual = Application.targetFrameRate;
            // The game sets its own rate after we start (AmongUsClient.Awake): any value we didn't put there is the
            // game's, and that is what "do jogo" / the master switch off goes back to.
            if (!_fpsAplicado || atual != _fpsQueApliquei)
            {
                if (_fpsDoJogo != atual) Log.LogInfo("[jogo] fps do proprio jogo = " + atual);
                _fpsDoJogo = atual;
                _fpsAplicado = false;
            }
            int alvo = o.FpsEfetivo;
            if (alvo <= 0)
            {
                if (_fpsAplicado)
                {
                    Application.targetFrameRate = _fpsDoJogo;
                    _fpsAplicado = false;
                    Log.LogInfo("[jogo] fps de volta ao do jogo (" + _fpsDoJogo + ")");
                }
                return;
            }
            // targetFrameRate only counts with vSync off. (11/11) No PC o botão do VSync é do jogo (aba Gráficos): o mod
            // não mexe nele, e o limite simplesmente espera a pessoa desligar o VSync lá (o botão do limite avisa).
            if (!Plataforma.Pc && QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            int escolhido = alvo;
            // 08/11 (stutters walking): where people walk (lobby, Freeplay, a round) the frames follow the screen's
            // real rate (60 on the CriCri's Xiaomi in a match): frames the screen can't show are only heat, and a
            // hot phone slows down. In menus the chosen rate stays (there HyperOS raised the screen to 90 Hz).
            if (!Plataforma.Pc && (_cena == "OnlineGame" || _cena == "Tutorial") && _taxaEstado != TaxaMedindo && _taxaEstado != TaxaDeu)
            {
                int hz = HzAtual();
                if (hz >= 30 && hz < alvo) alvo = hz;
            }
            if (atual != alvo)
            {
                Application.targetFrameRate = alvo;
                Log.LogInfo("[jogo] fps alvo = " + alvo + (alvo != escolhido ? " (a tela esta a " + alvo + " Hz; escolhido " + escolhido + ")" : ""));
            }
            _fpsQueApliquei = alvo;
            _fpsAplicado = true;
            if (!Plataforma.Pc) GarantirTaxaDaTela(escolhido);   // (no PC a taxa da tela é do Windows e do jogo)
        }
        bool _fpsAplicado;
        int _fpsQueApliquei;

        // Phone test 08/11: the display WAS at 120 Hz (the app asked Android for it) and the game still ran at 60:
        // Unity paces frames by the refresh rate it believes the screen has (60, read at start) and only learns
        // about another one through Screen.SetResolution(..., RefreshRate). So: the screen's mode with the target
        // rate (from Unity's own list, e.g. 720x1640@120) is passed along whenever Unity still reports less.
        // Only at the panel's own size (the rate is honoured only for one of its modes, see AplicarResolucao) and never
        // while a resolution change of ours is pending (both in one frame: the last one wins).
        int _trocasDeTaxa;
        void GarantirTaxaDaTela(int alvo)
        {
            if (alvo <= 60 || _trocasDeTaxa >= 6) return;
            if (!EhNativa(Screen.width, Screen.height)) return;
            if (_resolucaoAplicada && (Screen.width != _larguraQueApliquei || Screen.height != _alturaQueApliquei)) return;
            double atual = Screen.currentResolution.refreshRateRatio.value;
            if (atual + 0.5 >= alvo) return;
            if (!TaxaPara(alvo, out RefreshRate taxa) || taxa.value <= atual + 0.5) return;
            _trocasDeTaxa++;
            Log.LogInfo("[jogo] unity achava a tela a " + Math.Round(atual) + " Hz; pedindo " + Math.Round(taxa.value) + " Hz (" + Screen.width + "x" + Screen.height + ")");
            Screen.SetResolution(Screen.width, Screen.height, FullScreenMode.FullScreenWindow, taxa);
        }

        // The highest refresh rate Unity lists that does not pass the target (the target itself on a 120 Hz screen).
        static bool TaxaPara(int alvo, out RefreshRate taxa)
        {
            taxa = default;
            bool achou = false;
            double melhor = 0;
            var lista = Screen.resolutions;
            for (int i = 0; i < lista.Length; i++)
            {
                var rr = lista[i].refreshRateRatio;
                double v = rr.value;
                if (v <= alvo + 0.5 && v > melhor) { melhor = v; taxa = rr; achou = true; }
            }
            return achou;
        }

        internal static void Medidas(int porcento, out int largura, out int altura)
        {
            // Among Us runs in landscape: the long side is the width
            int a = Display.main.systemWidth, b = Display.main.systemHeight;
            int nativaL = Math.Max(a, b), nativaA = Math.Min(a, b);
            largura = (int)Math.Round(nativaL * porcento / 100.0);
            altura = (int)Math.Round(nativaA * porcento / 100.0);
        }

        // Phone test 08/11 04:00: the slider asked 820x360 and Unity never took it (asked again every 2 s, a hitch each
        // time), while 1139x500 -> 1640x720 at startup did. Unity lists only the panel's modes on this phone
        // (720x1640@60/90/120) and the refresh-rate overload is honoured only for a size that is one of them. So the
        // rate goes along only at the panel's own size; any other size goes the way the game sets its own 1139x500
        // (no rate). A size that still doesn't take is given up after 6 tries, until the choice or the scene changes.
        void AplicarResolucao()
        {
            var o = Opcoes.Atual;
            int w = Screen.width, h = Screen.height;
            // A change we didn't ask for is the game's own size ("padrão" on the slider). A size of ours that didn't
            // take is NOT the game's (it was, before: 1640x720 became the "game's size").
            if (w != _vistoL || h != _vistoA)
            {
                if (_vistoL > 0 && !(_resolucaoAplicada && w == _larguraQueApliquei && h == _alturaQueApliquei))
                {
                    _larguraDoJogo = w;
                    _alturaDoJogo = h;
                }
                _vistoL = w;
                _vistoA = h;
            }
            if (o.ResolucaoEfetiva <= 0)
            {
                if (_resolucaoAplicada && _larguraDoJogo > 0)
                {
                    if (w != _larguraDoJogo || h != _alturaDoJogo)
                        Screen.SetResolution(_larguraDoJogo, _alturaDoJogo, FullScreenMode.FullScreenWindow);
                    Log.LogInfo("[jogo] resolucao de volta a do jogo (" + _larguraDoJogo + "x" + _alturaDoJogo + ")");
                    _resolucaoAplicada = false;
                }
                return;
            }
            Medidas(o.ResolucaoEfetiva, out int largura, out int altura);
            if (TaxaAlta(o, largura, altura)) return;
            if (largura != _larguraQueApliquei || altura != _alturaQueApliquei || _cena != _cenaDasTentativas)
            {
                _tentativasDeResolucao = 0;
                _cenaDasTentativas = _cena;
            }
            _larguraQueApliquei = largura;
            _alturaQueApliquei = altura;
            _resolucaoAplicada = true;
            if (w == largura && h == altura)
            {
                if (_tentativasDeResolucao > 0) Log.LogInfo("[jogo] resolucao " + largura + "x" + altura + " pegou (" + _tentativasDeResolucao + " pedido(s))");
                _tentativasDeResolucao = 0;
                return;
            }
            if (_tentativasDeResolucao >= 6) return;
            _tentativasDeResolucao++;

            bool comTaxa = false;
            RefreshRate taxa = default;
            if (EhNativa(largura, altura) && o.FpsEfetivo > 60 && _tentativasDeResolucao <= 3) comTaxa = TaxaPara(o.FpsEfetivo, out taxa);
            Log.LogInfo("[jogo] resolucao " + w + "x" + h + " -> " + largura + "x" + altura + " (" + o.ResolucaoEfetiva + "%"
                + (comTaxa ? ", com " + Math.Round(taxa.value) + " Hz" : "") + ", pedido " + _tentativasDeResolucao + ")");
            if (comTaxa) Screen.SetResolution(largura, altura, FullScreenMode.FullScreenWindow, taxa);
            else Screen.SetResolution(largura, altura, FullScreenMode.FullScreenWindow);
            if (_tentativasDeResolucao == 6)
                Log.LogWarning("[jogo] a resolucao " + largura + "x" + altura + " nao pegou em 6 pedidos; paro de pedir ate trocar a opcao ou a cena");
        }
        /*
          (12/11, celular do CriCri, Xiaomi 2409BRN2CL, tela 60/90/120 Hz: "na partida fica preso em 60fps podendo passar")
          TELA RÁPIDA COM O JOGO MENOR. O Diagnóstico mediu 16,7 ms em tudo: o jogo estava no limite da tela, não pesado. No
          "Equilibrado" o jogo é desenhado em 80% (1312x576) e a Unity só aceita pedir 90/120 Hz junto com o tamanho cheio da tela
          (08/11); então a tela ficava a 60 Hz e, na partida, o limite pela taxa da tela (AplicarFps) prendia o jogo em 60.
          O experimento, uma vez por abertura do jogo, quando o limite escolhido passa de 60 e a resolução é menor que a tela:
          1) pede a tela inteira com a taxa escolhida (o Android troca o modo da tela); 2) assim que pegou (ou em 4 s), volta para o
          tamanho menor, sem taxa; 3) mede o FPS de verdade por 8 s, sem o limite pela taxa: passou de 62, deu (fica assim); não
          passou, a tela voltou a 60 e o limite volta (sem tremida), e não tenta de novo até abrir o jogo outra vez. Tudo no registro.
        */
        const int TaxaNada = 0, TaxaPediuInteira = 1, TaxaMedindo = 2, TaxaDeu = 3, TaxaNaoDeu = 4;
        // (12/11, medido no celular dele) DESLIGADO na versão pública: no Xiaomi o próprio sistema (HyperOS) segura o app em 60 Hz
        // ("PRIORITY_MIUI_REFRESH_RATE max 60" no dumpsys display) e ignora o pedido do app; o experimento só trocava a tela na
        // abertura, sem ganho. O código fica para testar em outro celular (trocar para true num APK de teste).
        const bool TelaRapidaLigada = false;
        int _taxaEstado;
        float _taxaDesde;
        double _taxaPedida;

        bool TaxaAlta(Opcoes o, int largura, int altura)
        {
            if (Plataforma.Pc || !TelaRapidaLigada) return false;
            float agora = Time.unscaledTime;
            double hz = 0;
            try { hz = Screen.currentResolution.refreshRateRatio.value; } catch { }
            switch (_taxaEstado)
            {
                case TaxaNada:
                {
                    if (o.FpsEfetivo <= 60 || EhNativa(largura, altura)) return false;
                    if (hz + 0.5 >= o.FpsEfetivo) return false;
                    if (!TaxaPara(o.FpsEfetivo, out RefreshRate taxa) || taxa.value <= Math.Max(hz, 60) + 0.5) return false;
                    int a = Display.main.systemWidth, b = Display.main.systemHeight;
                    int L = Math.Max(a, b), A = Math.Min(a, b);
                    _taxaPedida = taxa.value;
                    _taxaEstado = TaxaPediuInteira;
                    _taxaDesde = agora;
                    Log.LogInfo("[jogo] tela rapida: 1) pedindo a tela inteira " + L + "x" + A + " a " + Math.Round(taxa.value) + " Hz (a unity acha " + Math.Round(hz) + " Hz; o jogo vai em " + largura + "x" + altura + ")");
                    Screen.SetResolution(L, A, FullScreenMode.FullScreenWindow, taxa);
                    return true;
                }
                case TaxaPediuInteira:
                {
                    bool pegou = hz + 0.5 >= _taxaPedida;
                    if (!pegou && agora - _taxaDesde < 4f) return true;
                    Log.LogInfo("[jogo] tela rapida: 2) a tela inteira ficou a " + Math.Round(hz) + " Hz" + (pegou ? "" : " (nao pegou em 4 s)") + "; voltando para " + largura + "x" + altura);
                    _taxaEstado = TaxaMedindo;
                    _taxaDesde = agora;
                    _tentativasDeResolucao = 0;
                    _cenaDasTentativas = "";
                    return false;   // o caminho normal põe o tamanho menor (sem taxa)
                }
                case TaxaMedindo:
                {
                    if (agora - _taxaDesde < 8f) return false;
                    bool deu = _fpsMedido > 62f;
                    _taxaEstado = deu ? TaxaDeu : TaxaNaoDeu;
                    Log.LogInfo("[jogo] tela rapida: 3) " + (deu ? "DEU" : "nao deu") + ": " + _fpsMedido.ToString("0") + " fps medidos em " + Screen.width + "x" + Screen.height
                        + " (a unity acha a tela a " + Math.Round(hz) + " Hz, cena " + _cena + ")" + (deu ? "" : "; o limite pela taxa da tela volta"));
                    return false;
                }
            }
            return false;
        }

        bool _resolucaoAplicada;
        int _larguraQueApliquei, _alturaQueApliquei, _vistoL, _vistoA, _tentativasDeResolucao;
        string _cenaDasTentativas = "";

        // The panel's own size, in either orientation (Unity lists it portrait, 720x1640; the game runs landscape).
        static bool EhNativa(int largura, int altura)
        {
            int a = Display.main.systemWidth, b = Display.main.systemHeight;
            return (largura == a && altura == b) || (largura == b && altura == a);
        }

        // The size the game picks by itself (1139x500 on the CriCri's phone, 08/11): a stop on the resolution slider.
        internal static bool TamanhoDoJogo(out int largura, out int altura)
        {
            var i = Instancia;
            largura = i != null ? i._larguraDoJogo : 0;
            altura = i != null ? i._alturaDoJogo : 0;
            return largura > 0 && altura > 0;
        }

        void AplicarQualidade()
        {
            var o = Opcoes.Atual;
            int aa = o.AaEfetivo == 2 || o.AaEfetivo == 4 ? o.AaEfetivo : 0;
            int alvoAa = aa > 0 ? aa : Math.Max(0, _aaDoJogo);
            if (alvoAa != _aaAplicado && QualitySettings.antiAliasing != alvoAa)
            {
                QualitySettings.antiAliasing = alvoAa;
                Log.LogInfo("[jogo] anti-serrilhado " + alvoAa + "x");
            }
            _aaAplicado = alvoAa;

            // Full-size textures only. 08/11 (stutters walking): anisotropic filtering is no longer forced. It sharpens
            // textures seen at a slant; Among Us is 2D seen from above (no slant), so it bought nothing and cost
            // texture bandwidth on the phone's GPU.
            if (o.TexturasEfetivas && !_texturasAplicadas)
            {
                if (QualitySettings.globalTextureMipmapLimit != 0) QualitySettings.globalTextureMipmapLimit = 0;
                if (QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable) QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                _texturasAplicadas = true;
                Log.LogInfo("[jogo] texturas em qualidade maxima (antes: limite " + _mipmapDoJogo + ")");
            }
            else if (!o.TexturasEfetivas && _texturasAplicadas)
            {
                if (_mipmapDoJogo >= 0) QualitySettings.globalTextureMipmapLimit = _mipmapDoJogo;
                _texturasAplicadas = false;
            }
        }

        void NovaCena()
        {
            string cena = SceneManager.GetActiveScene().name;
            if (cena == _cena) return;
            _cena = cena;
            Log.LogInfo("[jogo] cena " + cena);
            if (cena == "MainMenu")
            {
                // The game's "watch an ad" button has nothing to do in an app without ads.
                var anuncio = GameObject.Find("AdsButton");
                if (anuncio != null) anuncio.SetActive(false);
                // Innersloth's mod policy: the mod stamp must be on screen. Only if the manager exists already
                // (reading a DestroyableSingleton's Instance creates an empty one).
                if (DestroyableSingleton<ModManager>.InstanceExists) DestroyableSingleton<ModManager>.Instance.ShowModStamp();
                // (the CRICRI BR region moved to its own mod, CriCriServidor, on 08/11)
            }
        }

        // Innersloth's policy requires the mod stamp for every mod ("we require the mod stamp to be enabled for all
        // mods") but lets it be shown "any way you wish": the CriCri found it covering the map button, so it stays,
        // at 60% of its size.
        IntPtr _selo;
        Vector3 _escalaSelo;
        void AjustarSelo()
        {
            if (!DestroyableSingleton<ModManager>.InstanceExists) return;
            var selo = DestroyableSingleton<ModManager>.Instance.ModStamp;
            if (selo == null) return;
            if (selo.Pointer != _selo)
            {
                _selo = selo.Pointer;
                _escalaSelo = selo.transform.localScale * 0.6f;
                Log.LogInfo("[jogo] selo de mod em 60% do tamanho");
            }
            if (selo.transform.localScale != _escalaSelo) selo.transform.localScale = _escalaSelo;
        }

        // ---------------- players: smooth movement and colorblind names ----------------

        void Jogadores()
        {
            var o = Opcoes.Atual;
            var lista = PlayerControl.AllPlayerControls;
            if (lista == null) return;
            bool daltonico = o.DaltonicoCorNoNome && DataManager.IsSettingsLoaded && DataManager.Settings.Accessibility.ColorBlindMode;
            var cores = Palette.PlayerColors;

            for (int i = 0; i < lista.Count; i++)
            {
                var pc = lista[i];
                if (pc == null) continue;
                Suavizar(pc, o.SuaveEfetivo);
                if (daltonico) Pintar(pc.cosmetics?.colorBlindText, CorDoJogador(pc, cores));
                else if (_coresPintadas) Pintar(pc.cosmetics?.colorBlindText, Color.white);
            }

            // Meeting screen: the color name under each vote card.
            var reuniao = MeetingHud.Instance;
            if (reuniao != null && reuniao.playerStates != null && (daltonico || _coresPintadas))
            {
                var areas = reuniao.playerStates;
                for (int i = 0; i < areas.Length; i++)
                {
                    var area = areas[i];
                    if (area == null) continue;
                    Color cor = Color.white;
                    if (daltonico)
                    {
                        var info = GameData.Instance != null ? GameData.Instance.GetPlayerById(area.PlayerId.Value) : null;
                        int id = info != null && info.DefaultOutfit != null ? info.DefaultOutfit.ColorId : -1;
                        if (id >= 0 && id < cores.Length) cor = cores[id];
                    }
                    Pintar(area.ColorBlindName, cor);
                }
            }
            _coresPintadas = daltonico;
        }

        static Color CorDoJogador(PlayerControl pc, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> cores)
        {
            var roupa = pc.CurrentOutfit;   // the shapeshifted look when shapeshifted, like the name itself
            int id = roupa != null ? roupa.ColorId : -1;
            return id >= 0 && id < cores.Length ? (Color)cores[id] : Color.white;
        }

        static void Pintar(TextMeshPro texto, Color cor)
        {
            if (texto == null || !texto.isActiveAndEnabled) return;
            if (texto.color != cor) texto.color = cor;
            // dark colors (black, brown, maroon) need an outline to read on the dark map
            if (cor != Color.white && texto.outlineWidth < 0.15f)
            {
                texto.outlineWidth = 0.2f;
                texto.outlineColor = new Color32(0, 0, 0, 255);
            }
        }

        void Suavizar(PlayerControl pc, bool ligado)
        {
            var corpo = pc.MyPhysics != null ? pc.MyPhysics.body : null;
            if (corpo == null) return;
            if (!_logouCorpo && pc.AmOwner)
            {
                _logouCorpo = true;
                Log.LogInfo("[jogo] corpo do jogador: interpolation " + corpo.interpolation + ", kinematic " + corpo.isKinematic
                    + ", fixedDeltaTime " + Time.fixedDeltaTime);
            }
            IntPtr p = corpo.Pointer;
            if (ligado)
            {
                // Kinematic bodies are the replay's puppets (it moves them itself): leave them alone.
                if (corpo.interpolation == RigidbodyInterpolation2D.None && !corpo.isKinematic)
                {
                    corpo.interpolation = RigidbodyInterpolation2D.Interpolate;
                    if (_corposSuaves.Add(p) && _corposSuaves.Count == 1) Log.LogInfo("[jogo] movimento suave ligado (interpolacao nos jogadores)");
                }
            }
            else if (_corposSuaves.Remove(p))
            {
                corpo.interpolation = RigidbodyInterpolation2D.None;
            }
        }

        // ---------------- FPS counter ----------------

        void AtualizarContador()
        {
            // (12/11) Onde o jogo tem o texto do ping (sala, Freeplay, partida), o FPS vai na barra do ping (BarraDoPing). Este
            // contador de cima fica só para onde não há ping (o menu), e só nos modos da barra que têm FPS.
            int modo = Opcoes.Atual.BarraPing;
            bool querFps = modo == BarraDoPing.PingEFps || modo == BarraDoPing.ComServidor;
            if (!querFps || BarraDoPing.PingDoJogo != null)
            {
                if (_contador != null) _contador.gameObject.SetActive(false);
                return;
            }
            // Phone test 08/11: nothing showed. Among Us doesn't tag its camera "MainCamera" everywhere, so
            // Camera.main can be null: use the camera that draws the HUD texts.
            var cam = Camera.main;
            if (cam == null || !cam.isActiveAndEnabled) cam = CameraDoHud();
            if (cam == null)
            {
                if (_avisos.Add("contador-sem-camera")) Log.LogWarning("[jogo] contador: nenhuma camera achada");
                return;
            }
            if (_contador == null || _cameraDoContador == null || _cameraDoContador.Pointer != cam.Pointer) CriarContador(cam);
            if (_contador == null) return;
            if (!_contador.gameObject.activeSelf) _contador.gameObject.SetActive(true);

            int fps = Fps.Mostrado;   // (12/11) o número estável, o mesmo da barra do ping
            _contador.text = fps + " FPS";
            // Fixed bands (the CriCri's rule, 08/11): 0-30 red, 31-45 yellow, 46+ green. Relative to the target, 60
            // of 120 showed red and 30 of 30 green, which read backwards.
            _contador.color = fps >= 46 ? new Color32(120, 230, 120, 255)
                : fps >= 31 ? new Color32(250, 210, 90, 255) : new Color32(255, 110, 100, 255);
        }

        // The orthographic camera that renders the layer of an on-screen TextMeshPro (the HUD's camera).
        static Camera CameraDoHud()
        {
            var textos = FindObjectsOfType<TextMeshPro>();
            var cameras = Camera.allCameras;
            for (int i = 0; i < textos.Length; i++)
            {
                var t = textos[i];
                if (t == null || !t.isActiveAndEnabled) continue;
                int camada = 1 << t.gameObject.layer;
                for (int c = 0; c < cameras.Length; c++)
                    if (cameras[c] != null && cameras[c].isActiveAndEnabled && cameras[c].orthographic && (cameras[c].cullingMask & camada) != 0)
                        return cameras[c];
            }
            return cameras.Length > 0 ? cameras[0] : null;
        }

        void CriarContador(Camera cam)
        {
            if (_contador != null) Destroy(_contador.gameObject);
            _contador = null;
            // a text this camera draws, to copy its font, layer and depth
            TextMeshPro modelo = null;
            var textos = FindObjectsOfType<TextMeshPro>();
            for (int i = 0; i < textos.Length && modelo == null; i++)
            {
                var t0 = textos[i];
                if (t0 != null && t0.font != null && t0.isActiveAndEnabled && (cam.cullingMask & (1 << t0.gameObject.layer)) != 0) modelo = t0;
            }
            if (modelo == null)
            {
                if (_avisos.Add("contador-sem-modelo")) Log.LogWarning("[jogo] contador: nenhum texto visivel pra copiar a fonte");
                return;
            }

            var go = new GameObject("CriCriContadorFps");
            go.layer = modelo.gameObject.layer;
            go.transform.SetParent(cam.transform, false);
            float alto = cam.orthographicSize;
            float z = cam.transform.InverseTransformPoint(modelo.transform.position).z;
            if (z <= cam.nearClipPlane || z >= cam.farClipPlane) z = Mathf.Clamp(5f, cam.nearClipPlane + 0.1f, cam.farClipPlane - 0.1f);
            // top center, below the ping line
            go.transform.localPosition = new Vector3(0f, alto - 0.42f, z);

            var t = go.AddComponent<TextMeshPro>();
            t.font = modelo.font;
            try
            {
                var mat = modelo.font.material;
                if (mat != null) t.fontSharedMaterial = mat;
                mat = t.fontSharedMaterial;
                // outside any panel: no stencil mask (same rule as the replay's labels)
                if (mat != null && mat.HasProperty("_StencilComp") && mat.GetFloat("_StencilComp") != 8f)
                {
                    mat = t.fontMaterial;
                    mat.SetFloat("_StencilComp", 8f);
                    if (mat.HasProperty("_Stencil")) mat.SetFloat("_Stencil", 0f);
                }
            }
            catch { }
            t.richText = false;
            t.enableWordWrapping = false;
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = 0.2f * FatorFonte(modelo.font);
            t.outlineWidth = 0.2f;
            t.outlineColor = new Color32(0, 0, 0, 255);
            try
            {
                var mr = go.GetComponent<MeshRenderer>();
                var mrModelo = modelo.GetComponent<MeshRenderer>();
                if (mr != null && mrModelo != null)
                {
                    mr.sortingLayerID = mrModelo.sortingLayerID;
                    mr.sortingOrder = mrModelo.sortingOrder + 500;
                }
            }
            catch { }
            _contador = t;
            _cameraDoContador = cam;
            Log.LogInfo("[jogo] contador de FPS criado: camera " + cam.name + ", camada " + go.layer + ", pos " + go.transform.localPosition
                + ", letra " + t.fontSize.ToString("0.0") + " (modelo " + modelo.name + ")");
        }

        static readonly Dictionary<IntPtr, float> _fatores = new Dictionary<IntPtr, float>();

        // World units per font point for a 3D TextMeshPro with this font (same measurement as the replay's Ui.FatorFonte).
        internal static float FatorFonte(TMP_FontAsset fonte)
        {
            IntPtr chave = fonte != null ? fonte.Pointer : IntPtr.Zero;
            if (_fatores.TryGetValue(chave, out float fator)) return fator;
            fator = 10f;
            try
            {
                var go = new GameObject("CriCri_medida");
                var t = go.AddComponent<TextMeshPro>();
                t.font = fonte;
                t.enableWordWrapping = false;
                t.fontSize = 100f;
                var tam = t.GetPreferredValues("Hg");
                if (tam.y > 0.0001f) fator = 100f / (tam.y * 0.8f);
                Destroy(go);
            }
            catch { }
            _fatores[chave] = fator;
            return fator;
        }
    }
}
