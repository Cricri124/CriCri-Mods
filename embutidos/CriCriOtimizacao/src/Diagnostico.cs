#if OTIM_DIAG
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriCriMods
{
    /*
      SÓ EXISTE NO APK DE TESTE (-p:DefineConstants=OTIM_DIAG). O DLL que vai para os jogadores não tem esta classe
      nem o botão dela.

      (11/11, noite, ele: "o que falta pra chegar no ultra?") Falta saber ONDE o tempo vai NO CELULAR. No PC a sombra
      por raios pesa 24 a 48% nos motores e os pets são a peça mais cara de cada jogador, mas PC não é celular: lá a
      placa de vídeo é outra e o processador é outro. Este botão ("Diagnóstico", em Mais opções) faz no aparelho o
      que o piloto fazia no PC: parado no lugar onde a pessoa está, tira UMA coisa por vez durante 3 s, mede o tempo
      do quadro e devolve, com uma medida normal (a "base") entre uma coisa e outra. Em uns 50 s o registro
      (LogOutput.log) fica com a tabela daquele lugar:

        a outra sombra | sem a luz | pets dos outros parados | sem desenhar os jogadores |
        sem animar os outros jogadores | sem as animações do mapa | sem desenhar o mapa | sem o HUD

      Cada linha diz quantos ms o quadro levou, quanto isso é a menos que a base (o teto do que dá para ganhar
      mexendo naquela peça) e quantas coletas de lixo o jogo fez naqueles segundos. Com isso em duas ou três salas
      (motor, reator, uma sala cheia de gente) dá para escolher o que otimizar sem adivinhar.

      O QUE ELE MEXE, E POR QUE NÃO ESTRAGA A PARTIDA: só desenho, animação e a luz DESTE aparelho, por 3 s cada, e
      devolve do jeito que achou. Nada é enviado, ninguém mais vê diferença, posição e rede não são tocadas, e nenhum
      objeto do jogo é desligado inteiro (um jogador desligado perderia um recado da rede).
      "Sem desenhar" NÃO mexe em quem está visível ou escondido: tira a CAMADA dos jogadores (ou do mapa) do que as
      câmeras desenham e põe de volta. Assim quem entrou num duto ou morreu durante os 3 s continua como o jogo
      deixou (religar desenho por desenho mostraria quem o jogo escondeu no meio).
      Durante os 50 s a tela de quem apertou pisca (o mapa some por 3 s, os outros somem por 3 s): é um teste, não é
      para jogar com ele rodando. Reunião aberta, troca de mapa ou sair da partida param o teste e devolvem tudo.

      Se o aparelho já está no limite de quadros da tela, as diferenças não aparecem (o quadro espera a tela): o
      registro avisa. O lugar certo de medir é onde o FPS cai.
    */
    internal static class Diagnostico
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        sealed class Passo
        {
            public string Nome;
            public Func<bool> Por;     // false = não se aplica aqui (o passo é pulado)
            public Action Tirar;
            public bool Base, Rodou;
            public float Ms, Pior;
            public int Quadros, Lixo;
        }

        const float Assentar = 0.5f, Janela = 2.5f;

        static List<Passo> _passos;
        static int _i = -1;
        static bool _posto;
        static float _desde, _tempo, _pior;
        static int _quadros, _lixoNoComeco;
        static IntPtr _nave;
        static string _aviso;
        static float _avisoAte;
        static int _gcAntes;

        internal static bool Rodando => _i >= 0;

        internal static string Rotulo()
        {
            if (Rodando) return "Diagnóstico: " + (_i + 1) + " de " + _passos.Count;
            if (_aviso != null && Time.unscaledTime < _avisoAte) return _aviso;
            return "Diagnóstico (1 min)";
        }

        static void Avisar(string texto)
        {
            _aviso = texto;
            _avisoAte = Time.unscaledTime + 30f;
        }

        /// <summary>O toque no botão: começa, ou para e devolve tudo se já estava rodando.</summary>
        internal static void Botao()
        {
            if (Rodando) { Parar("parado pelo botao"); Avisar("Diagnóstico: parado"); return; }
            var eu = PlayerControl.LocalPlayer;
            var nave = ShipStatus.Instance;
            if (eu == null || nave == null) { Avisar("Diagnóstico: só na partida"); return; }
            if (MeetingHud.Instance != null) { Avisar("Diagnóstico: fora da reunião"); return; }

            _nave = nave.Pointer;
            _passos = Montar(eu, nave);
            _gcAntes = Gc();
            Cabecalho(eu, nave);
            _i = 0;
            Entrar();
        }

        // Todo quadro, do Update da Otimizacao. Fora de um diagnóstico sai na primeira linha.
        internal static void Quadro()
        {
            if (_i < 0) return;
            var nave = ShipStatus.Instance;
            if (nave == null || nave.Pointer != _nave || PlayerControl.LocalPlayer == null) { Parar("saiu da partida"); return; }
            if (MeetingHud.Instance != null) { Parar("abriu a reuniao"); Avisar("Diagnóstico: parado"); return; }

            float agora = Time.unscaledTime;
            if (agora - _desde < Assentar) { _lixoNoComeco = Gc(); return; }
            float dt = Time.unscaledDeltaTime;
            _quadros++;
            _tempo += dt;
            if (dt > _pior) _pior = dt;
            if (_tempo < Janela) return;

            var p = _passos[_i];
            p.Rodou = true;
            p.Quadros = _quadros;
            p.Ms = _tempo * 1000f / _quadros;
            p.Pior = _pior * 1000f;
            int lixo = Gc();
            p.Lixo = lixo >= 0 && _lixoNoComeco >= 0 ? lixo - _lixoNoComeco : -1;
            Sair();
            _i++;
            if (_i >= _passos.Count) { Fechar(); return; }
            Entrar();
        }

        // Põe o passo da vez (os que não se aplicam são pulados) e zera a conta.
        static void Entrar()
        {
            while (_i < _passos.Count)
            {
                var p = _passos[_i];
                bool vale = true;
                if (p.Por != null)
                {
                    try { vale = p.Por(); }
                    catch (Exception e) { vale = false; Log.LogWarning("[diag] " + p.Nome + ": " + e.GetType().Name + ": " + e.Message); }
                }
                if (vale) { _posto = true; break; }
                Desfazer(p);   // um passo que não se aplica não deixa nada mexido
                _i++;
            }
            if (_i >= _passos.Count) { Fechar(); return; }
            _desde = Time.unscaledTime;
            _quadros = 0;
            _tempo = _pior = 0f;
            _lixoNoComeco = Gc();
        }

        static void Sair()
        {
            if (!_posto || _i < 0 || _passos == null || _i >= _passos.Count) return;
            _posto = false;
            Desfazer(_passos[_i]);
        }

        static void Desfazer(Passo p)
        {
            if (p.Tirar == null) return;
            try { p.Tirar(); }
            catch (Exception e) { Log.LogWarning("[diag] devolver " + p.Nome + ": " + e.GetType().Name + ": " + e.Message); }
        }

        static void Parar(string porque)
        {
            Sair();
            _i = -1;
            _passos = null;
            Log.LogInfo("[diag] interrompido (" + porque + "): tudo devolvido");
        }

        // ---------------- os passos ----------------

        static List<Passo> Montar(PlayerControl eu, ShipStatus nave)
        {
            var passos = new List<Passo>();
            passos.Add(new Passo { Nome = "base", Base = true });
            // cada passo leva uma base logo depois: ele é comparado com a média das duas bases vizinhas, porque a
            // cena muda enquanto se mede (gente andando) e uma base só, lá no começo, não serviria de régua
            void Mais(Passo p) { passos.Add(p); passos.Add(new Passo { Nome = "base", Base = true }); }

            // 1) a outra sombra: no celular a do jogo é a de raios (processador) e a outra é a da placa de vídeo
            var luz = eu.lightSource;
            LightSourceRendererType tinha = LightSourceRendererType.Auto, pedido = LightSourceRendererType.Auto;
            bool trocou = false;
            Mais(new Passo
            {
                Nome = "a outra sombra",
                Por = () =>
                {
                    if (luz == null) return false;
                    var r = luz.renderer;
                    if (r == null) return false;
                    tinha = r.Type;
                    pedido = luz.rendererType;
                    var outra = tinha == LightSourceRendererType.GPU ? LightSourceRendererType.Raycast : LightSourceRendererType.GPU;
                    if (outra == LightSourceRendererType.GPU && !Sombra.PodeNaPlaca(luz)) return false;
                    luz.rendererType = outra;
                    luz.UpdateShadowType();
                    trocou = true;
                    Log.LogInfo("[diag] sombra em uso " + tinha + "; medindo com " + outra);
                    return true;
                },
                Tirar = () =>
                {
                    if (!trocou || luz == null) return;
                    trocou = false;
                    luz.rendererType = tinha;
                    luz.UpdateShadowType();
                    luz.rendererType = pedido;   // o campo volta ao que era (no celular o jogo deixa em "automático")
                },
            });

            // 2) sem a luz: o teto do que qualquer mexida na sombra pode dar
            bool apagou = false;
            Mais(new Passo
            {
                Nome = "sem a luz (sombra parada)",
                Por = () => { if (luz == null || !luz.enabled) return false; luz.enabled = false; apagou = true; return true; },
                Tirar = () => { if (apagou && luz != null) luz.enabled = true; apagou = false; },
            });

            // os outros jogadores (o objeto do jogador nunca é desligado)
            var outros = new List<PlayerControl>();
            var todos = PlayerControl.AllPlayerControls;
            for (int i = 0; i < todos.Count; i++)
                if (todos[i] != null && todos[i].Pointer != eu.Pointer) outros.Add(todos[i]);

            // 3) os pets: o script que segue o dono, o corpo de física e a animação (no PC era a peça mais cara do jogador)
            var scriptsDosPets = new List<PetBehaviour>();
            var corposDosPets = new List<Rigidbody2D>();
            var animsDosPets = new List<Animator>();
            Mais(new Passo
            {
                Nome = "pets dos outros parados (script, fisica e animacao)",
                Por = () =>
                {
                    foreach (var pc in outros)
                    {
                        if (pc == null) continue;
                        var roupa = pc.cosmetics;
                        var pet = roupa != null ? roupa.currentPet : null;
                        if (pet == null || !pet.isActiveAndEnabled) continue;
                        pet.enabled = false;
                        scriptsDosPets.Add(pet);
                        var corpo = pet.GetComponent<Rigidbody2D>();
                        if (corpo != null && corpo.simulated) { corpo.simulated = false; corposDosPets.Add(corpo); }
                        Desligar(pet.GetComponentsInChildren<Animator>(false), animsDosPets);
                    }
                    return scriptsDosPets.Count > 0;
                },
                Tirar = () =>
                {
                    for (int i = 0; i < scriptsDosPets.Count; i++) if (scriptsDosPets[i] != null) scriptsDosPets[i].enabled = true;
                    scriptsDosPets.Clear();
                    for (int i = 0; i < corposDosPets.Count; i++) if (corposDosPets[i] != null) corposDosPets[i].simulated = true;
                    corposDosPets.Clear();
                    Religar(animsDosPets);
                },
            });

            // 4) sem desenhar os jogadores (e os pets): a camada deles sai do que as câmeras desenham
            int camadasDosJogadores = 0;
            foreach (var pc in outros)
            {
                if (pc == null) continue;
                camadasDosJogadores |= Camadas(pc.GetComponentsInChildren<Renderer>(true));
                var roupa = pc.cosmetics;
                var pet = roupa != null ? roupa.currentPet : null;
                if (pet != null) camadasDosJogadores |= Camadas(pet.GetComponentsInChildren<Renderer>(true));
            }
            var camerasSemJogadores = new List<Camera>();
            var tiradoDosJogadores = new List<int>();
            Mais(new Passo
            {
                Nome = "sem desenhar os jogadores",
                Por = () => TirarCamadas(camadasDosJogadores, camerasSemJogadores, tiradoDosJogadores),
                Tirar = () => DevolverCamadas(camerasSemJogadores, tiradoDosJogadores),
            });

            // 5) sem animar os outros jogadores (corpo, roupa, brilho)
            var anims = new List<Animator>();
            Mais(new Passo
            {
                Nome = "sem animar os outros jogadores",
                Por = () =>
                {
                    foreach (var pc in outros) if (pc != null) Desligar(pc.GetComponentsInChildren<Animator>(false), anims);
                    return anims.Count > 0;
                },
                Tirar = () => Religar(anims),
            });

            // 6) o mapa: as animações
            var animsDoMapa = new List<Animator>();
            Mais(new Passo
            {
                Nome = "sem as animacoes do mapa",
                Por = () => { Desligar(nave.GetComponentsInChildren<Animator>(false), animsDoMapa); return animsDoMapa.Count > 0; },
                Tirar = () => Religar(animsDoMapa),
            });

            // 7) o mapa: o desenho (as camadas do mapa que não são as dos jogadores)
            int camadasDoMapa = Camadas(nave.GetComponentsInChildren<Renderer>(true)) & ~camadasDosJogadores;
            var camerasSemMapa = new List<Camera>();
            var tiradoDoMapa = new List<int>();
            Mais(new Passo
            {
                Nome = "sem desenhar o mapa",
                Por = () => TirarCamadas(camadasDoMapa, camerasSemMapa, tiradoDoMapa),
                Tirar = () => DevolverCamadas(camerasSemMapa, tiradoDoMapa),
            });

            // 8) o HUD (botões, tarefas, nomes da interface): a câmera dele parada
            Camera hud = null;
            bool parouHud = false;
            Mais(new Passo
            {
                Nome = "sem o HUD",
                Por = () =>
                {
                    if (!HudManager.InstanceExists) return false;
                    hud = HudManager.Instance.UICamera;
                    if (hud == null || !hud.enabled) return false;
                    hud.enabled = false;
                    parouHud = true;
                    return true;
                },
                Tirar = () => { if (parouHud && hud != null) hud.enabled = true; parouHud = false; },
            });

            return passos;
        }

        // As camadas (bits) em que estes desenhos estão.
        static int Camadas(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Renderer> itens)
        {
            int m = 0;
            if (itens == null) return 0;
            for (int i = 0; i < itens.Length; i++)
            {
                var r = itens[i];
                if (r != null) m |= 1 << r.gameObject.layer;
            }
            return m;
        }

        // Tira as camadas de toda câmera ligada que as desenha; guarda, por câmera, só os bits que tirou.
        static bool TirarCamadas(int camadas, List<Camera> cameras, List<int> tirado)
        {
            if (camadas == 0) return false;
            var todas = Camera.allCameras;
            for (int i = 0; i < todas.Length; i++)
            {
                var c = todas[i];
                if (c == null) continue;
                int tem = c.cullingMask & camadas;
                if (tem == 0) continue;
                c.cullingMask &= ~tem;
                cameras.Add(c);
                tirado.Add(tem);
            }
            return cameras.Count > 0;
        }

        static void DevolverCamadas(List<Camera> cameras, List<int> tirado)
        {
            for (int i = 0; i < cameras.Count; i++)
                if (cameras[i] != null) cameras[i].cullingMask |= tirado[i];
            cameras.Clear();
            tirado.Clear();
        }

        // Desliga os que estão ligados e guarda só esses, para religar exatamente os mesmos.
        static void Desligar(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<Animator> itens, List<Animator> guardados)
        {
            if (itens == null) return;
            for (int i = 0; i < itens.Length; i++)
            {
                var a = itens[i];
                if (a == null || !a.enabled) continue;
                a.enabled = false;
                guardados.Add(a);
            }
        }

        static void Religar(List<Animator> guardados)
        {
            for (int i = 0; i < guardados.Count; i++) if (guardados[i] != null) guardados[i].enabled = true;
            guardados.Clear();
        }

        // ---------------- o que vai para o registro ----------------

        static int Gc()
        {
            try { return Il2CppSystem.GC.CollectionCount(0); } catch { return -1; }
        }

        static int Teto()
        {
            int alvo = Application.targetFrameRate;
            if (alvo > 0) return alvo;
            try { return (int)Math.Round(Screen.currentResolution.refreshRateRatio.value); } catch { return 0; }
        }

        static void Cabecalho(PlayerControl eu, ShipStatus nave)
        {
            try
            {
                Vector2 p = eu.GetTruePosition();
                var todos = PlayerControl.AllPlayerControls;
                int naTela = 0, comPet = 0;
                var cam = Camera.main;
                float meiaA = cam != null ? cam.orthographicSize : 3f, meiaL = meiaA * (cam != null ? cam.aspect : 1.78f);
                Vector3 centro = cam != null ? cam.transform.position : (Vector3)p;
                for (int i = 0; i < todos.Count; i++)
                {
                    var pc = todos[i];
                    if (pc == null) continue;
                    Vector3 q = pc.transform.position;
                    if (Math.Abs(q.x - centro.x) <= meiaL + 0.5f && Math.Abs(q.y - centro.y) <= meiaA + 0.5f) naTela++;
                    var roupa = pc.cosmetics;
                    if (roupa != null && roupa.currentPet != null) comPet++;
                }
                var luz = eu.lightSource;
                var r = luz != null ? luz.renderer : null;
                Log.LogInfo("[diag] COMECO: " + nave.name + " em " + p.x.ToString("0.0", Inv) + "," + p.y.ToString("0.0", Inv)
                    + " | jogadores " + todos.Count + " (" + naTela + " na tela, " + comPet + " com pet)"
                    + " | tela " + Screen.width + "x" + Screen.height + ", limite " + Teto() + " fps"
                    + " | sombra em uso " + (r != null ? r.Type.ToString() : "nenhuma"));
                Log.LogInfo("[diag] aparelho " + SystemInfo.deviceModel + ", " + SystemInfo.processorType + " x" + SystemInfo.processorCount + ", " + SystemInfo.systemMemorySize + " MB, "
                    + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ") | opcoes: " + Opcoes.Atual.Descrever());
            }
            catch (Exception e) { Log.LogInfo("[diag] COMECO (sem detalhes: " + e.GetType().Name + ")"); }
        }

        static void Fechar()
        {
            var passos = _passos;
            _i = -1;
            _passos = null;
            _posto = false;
            if (passos == null) return;

            // a base é a média das bases que rodaram (começo, meio e fim): se elas diferem muito, o lugar mudou no meio
            float soma = 0f, menor = float.MaxValue, maior = 0f;
            int n = 0;
            foreach (var p in passos)
            {
                if (!p.Base || !p.Rodou) continue;
                soma += p.Ms; n++;
                if (p.Ms < menor) menor = p.Ms;
                if (p.Ms > maior) maior = p.Ms;
            }
            if (n == 0) { Log.LogInfo("[diag] FIM sem base: nada medido"); Avisar("Diagnóstico: não mediu"); return; }
            float baseMs = soma / n;
            int teto = Teto();
            bool noTeto = teto > 0 && 1000f / baseMs >= teto * 0.97f;
            float balanco = baseMs > 0.01f ? (maior - menor) / baseMs * 100f : 0f;

            string F(float x) => x.ToString("0.00", Inv);
            var bases = new System.Text.StringBuilder();
            for (int k = 0; k < passos.Count; k++)
            {
                var p = passos[k];
                if (p.Base) { if (p.Rodou) bases.Append(bases.Length > 0 ? " " : "").Append(F(p.Ms)); continue; }
                if (!p.Rodou) { Log.LogInfo("[diag] " + p.Nome + ": nao se aplica aqui"); continue; }
                // a régua deste passo: a base de antes e a de depois dele
                float regua = 0f;
                int quantas = 0;
                for (int a = k - 1; a >= 0; a--) if (passos[a].Base && passos[a].Rodou) { regua += passos[a].Ms; quantas++; break; }
                for (int d = k + 1; d < passos.Count; d++) if (passos[d].Base && passos[d].Rodou) { regua += passos[d].Ms; quantas++; break; }
                regua = quantas > 0 ? regua / quantas : baseMs;
                float dif = (p.Ms / regua - 1f) * 100f;
                Log.LogInfo("[diag] " + p.Nome + ": " + F(p.Ms) + " ms (" + (1000f / p.Ms).ToString("0", Inv) + " fps) contra " + F(regua) + " ms das bases ao lado = "
                    + (dif >= 0f ? "+" : "") + dif.ToString("0", Inv) + "% | pior quadro " + p.Pior.ToString("0", Inv) + " ms" + (p.Lixo >= 0 ? ", lixo " + p.Lixo : ""));
            }
            Log.LogInfo("[diag] as bases, na ordem (ms): " + bases);
            int gc = Gc();
            Log.LogInfo("[diag] FIM: base " + F(baseMs) + " ms (" + (1000f / baseMs).ToString("0", Inv) + " fps); as bases variaram " + balanco.ToString("0", Inv) + "%"
                + (balanco > 12f ? " (MUITO: o lugar mudou no meio, repita parado)" : "")
                + (noTeto ? "; NO LIMITE da tela (" + teto + " fps): aqui as diferencas nao aparecem, meca onde o FPS cai" : "")
                + (gc >= 0 && _gcAntes >= 0 ? "; coletas de lixo do jogo no teste: " + (gc - _gcAntes) : "")
                + "; tudo devolvido");
            Avisar(noTeto ? "Diagnóstico: no limite" : "Diagnóstico: pronto");
        }

#if OTIM_TESTE
        // Só no piloto do PC: o estado para conferir que tudo voltou.
        internal static string TesteEstado() => Rodando ? "rodando passo " + (_i + 1) + " de " + _passos.Count + " (" + _passos[_i].Nome + ")" : "parado (" + (Rotulo()) + ")";
#endif
    }
}
#endif
