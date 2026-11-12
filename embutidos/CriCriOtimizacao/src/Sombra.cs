using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriCriMods
{
    /*
      (11/11, ele: "ULTRA MELHORIA na otimização, pelo código do jogo... animações do mapa, como a dos motores, reator")

      O QUE O JOGO FAZ (desmontado: LightSource.UpdateShadowType, LightSourceRenderer.Create): a sombra da visão é
      refeita em todo quadro e o jogo tem DOIS jeitos de fazer. No Android ele escolhe o de raios
      (LightSourceRaycastRenderer: lê os pontos de cada parede por perto e lança 2 a 3 raios de física por ponto, tudo
      no processador); nas outras plataformas, o da placa de vídeo (LightSourceGpuRenderer: um mapa de sombra, sem
      raios). Medido no PC, trocando só isso no mesmo lugar: o de raios deixa o quadro 5% mais caro no refeitório da
      Skeld, 9% no motor, 19% no reator e 36 a 55% na cozinha e no reator da Fungle (quanto mais parede e quina por
      perto, mais raio).

      O QUE ISTO FAZ, SÓ SE A PESSOA LIGAR ("Sombra do PC (teste)", desligado por padrão): pede ao próprio jogo o
      outro caminho dele (rendererType = GPU e o UpdateShadowType do jogo, que desfaz um e cria o outro). É a sombra
      que quem joga no PC vê: a borda fica suave em vez de dura. Não muda o alcance da visão nem o que as paredes
      tapam; não é conta nossa, é a do jogo.

      POR QUE É TESTE: não foi visto em celular nenhum. A Innersloth deixou o Android nos raios de propósito, e a
      placa de um aparelho pode desenhar o mapa errado. Por isso: só liga se a placa tem o que o caminho precisa
      (OpenGL ES 3 ou Vulkan e textura de meia precisão); confere um segundo e meio depois se o mapa de sombra nasceu
      e, se não nasceu, volta para o do jogo, desmarca a opção e não tenta de novo; desligar a opção devolve o caminho
      do jogo na hora.

      (11/11, noite, ele: "o que falta pra chegar no ultra?") O BOTÃO MEDE SOZINHO. Ligar o botão dentro de uma
      partida (ou do Freeplay) faz a conta ali mesmo, parado no mesmo lugar: 4 s com a sombra do jogo, troca, 4 s com
      a do PC. O botão mostra por um minuto "Sombra do PC: 38 para 51 FPS" (ou "igual"), e a linha [sombra] do
      registro guarda os dois números e o pior quadro de cada um. É a resposta que faltava e que só o aparelho dá:
      ajuda ou não ajuda, naquele lugar. Quem está no limite de FPS da tela nos dois lê "igual (limite)": ali a
      sombra não é o que segura.

      Sem gancho: o Tique vem do Update da Otimizacao duas vezes por segundo; o Quadro (todo quadro) só soma o tempo
      enquanto uma medida está aberta.
    */
    internal static class Sombra
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        static IntPtr _luz;                        // a luz que já olhei (uma por rodada)
        static LightSourceRendererType _doJogo;    // o caminho que o jogo escolheu para ela
        static bool _troquei;                      // está no da placa por minha conta
        static float _conferirEm;
        static bool _naoDeu;                       // neste aparelho o mapa não nasceu: não tento de novo
        static int _pode = -1;                     // -1 não olhei, 0 não, 1 sim

        // ---- a medida "antes e depois" (aberta pelo botão) ----
        enum Fase { Nada, Antes, Trocando, Depois }
        const float Assentar = 0.7f, Janela = 4f;
        static Fase _fase;
        static float _faseDesde, _tempo, _pior, _fpsAntes, _piorAntes;
        static int _quadros;
        static string _resultado;
        static float _resultadoAte;

        internal static void Tique()
        {
            var eu = PlayerControl.LocalPlayer;   // campo estático: ler não cria nada
            var luz = eu != null ? eu.lightSource : null;
            if (luz == null) { _luz = IntPtr.Zero; _troquei = false; _fase = Fase.Nada; return; }
            Olhar(luz);

            // enquanto a medida ainda conta o "antes", a troca espera
            bool quer = Opcoes.Atual.SombraDoPcEfetiva && !_naoDeu && _doJogo != LightSourceRendererType.GPU && _fase != Fase.Antes && Pode(luz);
            if (quer && !_troquei)
            {
                Por(luz, LightSourceRendererType.GPU);
                _troquei = true;
                _conferirEm = Time.unscaledTime + 1.5f;
            }
            else if (!quer && _troquei)
            {
                Por(luz, _doJogo);
                _troquei = false;
                _conferirEm = 0f;
                if (_fase != Fase.Antes) _fase = Fase.Nada;
            }
            else if (_troquei && _conferirEm > 0f && Time.unscaledTime >= _conferirEm)
            {
                _conferirEm = 0f;
                if (Nasceu(luz, out string como))
                {
                    Log.LogInfo("[sombra] conferida: " + como);
                    if (_fase == Fase.Trocando) Abrir(Fase.Depois);
                }
                else
                {
                    _naoDeu = true;
                    Por(luz, _doJogo);
                    _troquei = false;
                    Log.LogWarning("[sombra] a sombra da placa de video nao nasceu neste aparelho (" + como + "): voltei para a do jogo e nao tento de novo");
                    // a opção desmarcada conta a verdade no botão e poupa a tentativa nas próximas aberturas
                    Opcoes.Atual.SombraDoPc = false;
                    Opcoes.Atual.Salvar();
                    if (_fase != Fase.Nada) { _fase = Fase.Nada; Mostrar("Sombra do PC: não deu aqui"); }
                }
            }
        }

        // Luz nova (rodada nova): o jogo já escolheu o caminho dela.
        static void Olhar(LightSource luz)
        {
            if (luz.Pointer == _luz) return;
            _luz = luz.Pointer;
            _doJogo = EmUso(luz);
            _troquei = false;
            _conferirEm = 0f;
            _fase = Fase.Nada;
        }

        // O caminho em uso agora (o campo pode estar em "automático" antes de o jogo escolher).
        static LightSourceRendererType EmUso(LightSource luz)
        {
            var r = luz.renderer;
            return r != null ? r.Type : luz.rendererType;
        }

        // O próprio jogo troca: desfaz o caminho antigo e cria o pedido (LightSource.UpdateShadowType).
        static void Por(LightSource luz, LightSourceRendererType tipo)
        {
            luz.rendererType = tipo;
            luz.UpdateShadowType();
            Log.LogInfo("[sombra] pedi " + tipo + ", em uso " + EmUso(luz) + " (a do jogo neste aparelho: " + _doJogo + ")");
        }

        // A placa tem o que o caminho precisa? Olhado uma vez.
        static bool Pode(LightSource luz)
        {
            if (_pode >= 0) return _pode == 1;
            bool ok = false;
            string porque;
            try
            {
                var api = SystemInfo.graphicsDeviceType;
                var formato = luz.gpuPreferredRTFormat;
                bool temFormato = SystemInfo.SupportsRenderTextureFormat(formato);
                ok = api != GraphicsDeviceType.OpenGLES2 && temFormato;
                porque = "placa " + SystemInfo.graphicsDeviceName + ", " + api + ", textura " + formato + " " + (temFormato ? "tem" : "NAO tem")
                    + ", geometria " + SystemInfo.supportsGeometryShaders;
            }
            catch (Exception e) { porque = e.GetType().Name + ": " + e.Message; }
            _pode = ok ? 1 : 0;
            Log.LogInfo("[sombra] sombra da placa de video neste aparelho: " + (ok ? "pode" : "NAO pode") + " (" + porque + ")");
            return ok;
        }

        // O caminho da placa está mesmo em uso e o mapa de sombra dele foi criado?
        static bool Nasceu(LightSource luz, out string como)
        {
            como = "";
            try
            {
                var r = luz.renderer;
                if (r == null) { como = "sem desenhista"; return false; }
                var g = r.TryCast<LightSourceGpuRenderer>();
                if (g == null) { como = "em uso " + r.Type; return false; }
                var mapa = g.shadowTexture;
                if (mapa == null) { como = "sem mapa de sombra"; return false; }
                bool criado = mapa.IsCreated();
                como = "mapa de sombra " + mapa.width + "x" + mapa.height + " " + mapa.format + (criado ? "" : " NAO criado") + ", sem geometria " + g.noGeomSupport;
                return criado;
            }
            catch (Exception e) { como = e.GetType().Name + ": " + e.Message; return false; }
        }

        // ---------------- o botão e a medida ----------------

        /* O toque no botão "Sombra do PC (teste)". Desligar só desliga (o Tique devolve a sombra do jogo em até meio
           segundo). Ligar com uma luz na cena (partida, Freeplay) abre a medida; ligar no menu só marca a opção. */
        internal static void Botao()
        {
            var o = Opcoes.Atual;
            o.SombraDoPc = !o.SombraDoPc;
            _fase = Fase.Nada;
            _resultado = null;
            if (o.SombraDoPc && _naoDeu)
            {
                // já se viu nesta sessão que a placa deste aparelho não faz o mapa de sombra: o botão não finge que ligou
                o.SombraDoPc = false;
                Mostrar("Sombra do PC: não deu aqui");
                return;
            }
            if (!o.SombraDoPc || !o.Ligada) return;
            var eu = PlayerControl.LocalPlayer;
            var luz = eu != null ? eu.lightSource : null;
            if (luz == null) return;
            Olhar(luz);
            // no PC a sombra do jogo já é a da placa: não há o que comparar
            if (_doJogo == LightSourceRendererType.GPU || !Pode(luz)) return;
            if (_troquei)
            {
                // ligou, desligou e ligou em menos de meio segundo: a do jogo volta já, para o "antes" valer
                Por(luz, _doJogo);
                _troquei = false;
                _conferirEm = 0f;
            }
            Abrir(Fase.Antes);
        }

        static void Abrir(Fase fase)
        {
            _fase = fase;
            _faseDesde = Time.unscaledTime;
            _quadros = 0;
            _tempo = _pior = 0f;
        }

        // Todo quadro, do Update da Otimizacao. Fora de uma medida sai na primeira linha.
        internal static void Quadro()
        {
            if (_fase != Fase.Antes && _fase != Fase.Depois) return;
            float agora = Time.unscaledTime;
            if (agora - _faseDesde < Assentar) return;   // a troca e o toque no botão não entram na conta
            float dt = Time.unscaledDeltaTime;
            _quadros++;
            _tempo += dt;
            if (dt > _pior) _pior = dt;
            if (_tempo < Janela) return;

            float fps = _quadros / _tempo, pior = _pior * 1000f;
            if (_fase == Fase.Antes)
            {
                _fpsAntes = fps;
                _piorAntes = pior;
                _fase = Fase.Trocando;   // o Tique troca agora e confere o mapa de sombra 1,5 s depois
                Tique();
            }
            else
            {
                _fase = Fase.Nada;
                Fechar(fps, pior);
            }
        }

        static void Fechar(float fpsDepois, float piorDepois)
        {
            int antes = Mathf.RoundToInt(_fpsAntes), depois = Mathf.RoundToInt(fpsDepois);
            float ganho = _fpsAntes > 0.01f ? (fpsDepois / _fpsAntes - 1f) * 100f : 0f;
            int teto = Teto();
            bool noTeto = teto > 0 && _fpsAntes >= teto * 0.97f && fpsDepois >= teto * 0.97f;
            Mostrar(noTeto ? "Sombra do PC: igual (limite)"
                : Math.Abs(ganho) < 3f ? "Sombra do PC: igual (" + depois + " FPS)"
                : "Sombra do PC: " + antes + " para " + depois + " FPS");
            string onde = "";
            try
            {
                var eu = PlayerControl.LocalPlayer;
                Vector2 p = eu != null ? eu.GetTruePosition() : default;
                var nave = ShipStatus.Instance;
                onde = (nave != null ? nave.name : "sem nave") + " em " + p.x.ToString("0.0", Inv) + "," + p.y.ToString("0.0", Inv);
            }
            catch { }
            Log.LogInfo("[sombra] medida aqui (" + onde + ", tela " + Screen.width + "x" + Screen.height + ", limite " + teto + " fps): sombra do jogo "
                + _fpsAntes.ToString("0.0", Inv) + " fps (pior quadro " + _piorAntes.ToString("0", Inv) + " ms) x sombra do PC "
                + fpsDepois.ToString("0.0", Inv) + " fps (pior quadro " + piorDepois.ToString("0", Inv) + " ms) = " + (ganho >= 0f ? "+" : "") + ganho.ToString("0", Inv) + "%"
                + (noTeto ? " (os dois no limite da tela: aqui a sombra nao e o que segura)" : ""));
        }

        // O limite de quadros que vale agora: o alvo do jogo ou, sem alvo, a taxa da tela.
        static int Teto()
        {
            int alvo = Application.targetFrameRate;
            if (alvo > 0) return alvo;
            try { return (int)Math.Round(Screen.currentResolution.refreshRateRatio.value); } catch { return 0; }
        }

        static void Mostrar(string texto)
        {
            _resultado = texto;
            _resultadoAte = Time.unscaledTime + 60f;
        }

        /// <summary>O que o botão escreve enquanto mede e por um minuto depois; null = o rótulo de sempre.</summary>
        internal static string Rotulo()
        {
            if (_fase != Fase.Nada) return "Sombra do PC: medindo...";
            if (_resultado != null && Time.unscaledTime < _resultadoAte) return _resultado;
            return null;
        }

        /// <summary>Para o Diagnostico (só no APK de teste): a placa deste aparelho dá conta do caminho da placa de vídeo?</summary>
        internal static bool PodeNaPlaca(LightSource luz) => !_naoDeu && Pode(luz);

#if OTIM_TESTE
        /* Só no piloto do PC: finge o celular. Põe a luz no caminho de raios e esquece que já a viu, para o Tique
           seguinte tomar os raios como "a do jogo" (no PC a do jogo já é a da placa e não haveria o que trocar). */
        internal static void TesteFingirCelular()
        {
            var eu = PlayerControl.LocalPlayer;
            var luz = eu != null ? eu.lightSource : null;
            if (luz == null) return;
            luz.rendererType = LightSourceRendererType.Raycast;
            luz.UpdateShadowType();
            _luz = IntPtr.Zero;
            _troquei = false;
            _naoDeu = false;
            _fase = Fase.Nada;
        }

        internal static string TesteEstado()
        {
            var eu = PlayerControl.LocalPlayer;
            var luz = eu != null ? eu.lightSource : null;
            if (luz == null) return "sem luz";
            Nasceu(luz, out string como);
            return "em uso " + EmUso(luz) + ", a do jogo " + _doJogo + ", troquei " + _troquei + ", nao deu " + _naoDeu + ", pode " + _pode + ", fase " + _fase + "; " + como;
        }
#endif
    }
}
