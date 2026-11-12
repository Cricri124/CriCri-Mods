using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriCriMods
{
    /*
      (11/11, ele: "fazer uma ULTRA MELHORIA na otimização, pelo código do jogo... O que eu já vejo que trava: várias
      pessoas andando; animações do mapa, como a dos motores, reator, etc.")

      O QUE O JOGO FAZ (contado no jogo): cada animação do mapa (motor, reator, telas, ventilador, duto, porta: 76 na
      Skeld, 50 na Polus, 106 na Fungle) e cada peça de cada jogador (corpo, roupa, brilho, pet: 4 ligadas por
      jogador) é um Animator da Unity, e o jogo deixa todos em "sempre animar": o mapa inteiro e os 15 jogadores são
      animados em todo quadro, mesmo o que está do outro lado do mapa. A câmera só mostra um pedaço pequeno.

      O QUE ISTO FAZ: troca o modo desses Animators para "fora da tela não escreve" (CullUpdateTransforms). O relógio
      de cada animação continua correndo igual (medido no jogo, nos três mapas: a animação longe avança o mesmo tanto
      com e sem isto), e é o relógio que o jogo consulta ("a animação acabou?", para duto, escada e nascimento); só o
      desenho deixa de ser trocado enquanto câmera nenhuma vê. Na tela nada muda.

      O QUE ISTO NUNCA FAZ: esconder, mostrar, mover ou apressar nada. Posição, física, rede e tempo do jogo não são
      tocados, e nada é enviado. Animação que pode mexer em parede (Animator sem SpriteAnim com colisor ou corpo de
      física embaixo, ou em modo de física: 1 na Skeld, 4 na Polus, como a broca) fica como o jogo deixou. Tudo é
      devolvido quando a opção desliga.

      (11/11, medido) Duas coisas saíram depois de medir:
      - "parar de vez" (CullCompletely): com a nave INTEIRA parando de vez o ganho a mais ficou dentro do ruído, e com
        ele o relógio para (a animação de uma vez só tocaria atrasada para quem chega);
      - pausar partícula longe: Skeld e Fungle não têm partícula em laço, e a única da Polus é a neve, que cobre o
        mapa inteiro a partir de um ponto só (pela distância até esse ponto ela congelaria na tela).

      Sem gancho do Harmony (no celular o jogo embute os métodos e o gancho pode não disparar): roda do Update da
      Otimizacao, com relógio próprio. A varredura cara (GetComponentsInChildren no mapa) acontece ao entrar no mapa,
      6 s depois e então a cada 30 s; os jogadores vão de 3 em 3 a cada meio segundo.
    */
    internal static class Leve
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        sealed class Peca
        {
            public Animator A;
            public AnimatorCullingMode DoJogo;   // o que o jogo tinha, para devolver
        }

        static readonly Dictionary<IntPtr, Peca> _doMapa = new Dictionary<IntPtr, Peca>();
        static readonly Dictionary<IntPtr, Peca> _dosBonecos = new Dictionary<IntPtr, Peca>();
        static readonly HashSet<IntPtr> _deixados = new HashSet<IntPtr>();   // do mapa, vistos e deixados como o jogo quer
        static readonly List<IntPtr> _mortos = new List<IntPtr>();
        static readonly Dictionary<string, int> _falhas = new Dictionary<string, int>();

        static IntPtr _raiz;
        static float _prox, _varrerEm, _proxBonecos, _proxFaxina;
        static int _varreduras, _vez, _modoPosto = -1;
        static bool _logouBonecos;

#if OTIM_TESTE
        // Só no build de teste do PC: o piloto força o modo (-1 = o das opções; 0 = como o jogo; qualquer outro =
        // ligado; 9 = o mapa inteiro parando de vez, só para medir o teto).
        internal static int NivelForcado = -1;
        internal static int NoMapa => _doMapa.Count;
        internal static int Deixados => _deixados.Count;
        internal static int NosBonecos => _dosBonecos.Count;
#endif

        // De 4 em 4 vezes por segundo, do Update da Otimizacao.
        internal static void Quadro()
        {
            float agora = Time.unscaledTime;
            if (agora < _prox) return;
            _prox = agora + 0.25f;

            int modo = Opcoes.Atual.ForaDaTelaEfetivo ? 1 : 0;   // 0 = tudo como o jogo deixou
#if OTIM_TESTE
            if (NivelForcado >= 0) modo = NivelForcado == 0 ? 0 : NivelForcado == 9 ? 9 : 1;
#endif
            if (modo != _modoPosto)
            {
                _modoPosto = modo;
                Parte("repor", () => Repor(modo));
            }
            if (modo == 0) return;

            Parte("mapa", () => Mapa(agora, modo));
            if (agora >= _proxBonecos)
            {
                _proxBonecos = agora + 0.5f;
                Parte("jogadores", () => Bonecos(modo));
            }
            if (agora >= _proxFaxina)
            {
                _proxFaxina = agora + 20f;
                Parte("faxina", Faxina);
            }
        }

        // Cada parte falha sozinha; a que falhar 3 vezes fica desligada até o jogo fechar (num aparelho em que a
        // Unity não tem a função, tentar de novo a cada quarto de segundo só gastaria).
        static void Parte(string nome, Action acao)
        {
            if (_falhas.TryGetValue(nome, out int n) && n >= 3) return;
            try { acao(); }
            catch (Exception e)
            {
                _falhas[nome] = n + 1;
                Log.LogWarning("[leve] " + nome + ": " + e.GetType().Name + ": " + e.Message + (n + 1 >= 3 ? " (desisto desta parte)" : ""));
            }
        }

        // ---------------- o mapa ----------------

        static void Mapa(float agora, int modo)
        {
            // ShipStatus.Instance e LobbyBehaviour.Instance são campos estáticos: ler não cria nada.
            Component raiz = null;
            var nave = ShipStatus.Instance;
            if (nave != null) raiz = nave;
            else
            {
                var sala = LobbyBehaviour.Instance;
                if (sala != null) raiz = sala;
            }
            IntPtr p = raiz != null ? raiz.Pointer : IntPtr.Zero;
            if (p != _raiz)
            {
                // mapa novo (ou saiu do mapa): o antigo foi destruído pelo jogo, não há o que devolver
                _raiz = p;
                _doMapa.Clear();
                _deixados.Clear();
                _varreduras = 0;
                _varrerEm = agora + 1.5f;
            }
            if (raiz == null || agora < _varrerEm) return;
            _varreduras++;
            _varrerEm = agora + (_varreduras == 1 ? 6f : 30f);
            Varrer(raiz, modo);
        }

        static void Varrer(Component raiz, int modo)
        {
            int novas = 0, deixadas = 0, jaDoJogo = 0;
            var anims = raiz.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                var a = anims[i];
                if (a == null) continue;
                IntPtr p = a.Pointer;
                if (_doMapa.ContainsKey(p) || _deixados.Contains(p)) continue;
                if (!SoDesenho(a)) { _deixados.Add(p); deixadas++; continue; }
                var x = new Peca { A = a, DoJogo = a.cullingMode };
                _doMapa[p] = x;
                Por(x, modo);
                novas++;
                if (x.DoJogo != AnimatorCullingMode.AlwaysAnimate) jaDoJogo++;
            }
            if (novas > 0 || deixadas > 0)
                Log.LogInfo("[leve] " + raiz.name + ": " + novas + " animacoes do mapa (" + jaDoJogo + " o jogo ja cortava fora da tela), "
                    + deixadas + " deixadas como o jogo quer (podem mexer em parede)");
        }

        /* Só mexe no que é desenho. A animação do SpriteAnim (74 das 76 da Skeld) troca a figura de um SpriteRenderer:
           não move nada. Animator puro pode girar ou mover um objeto: se houver colisor ou corpo de física embaixo
           dele, ou se ele anima no passo da física, fora da tela ele deixaria de mover uma parede. Esses ficam. */
        static bool SoDesenho(Animator a)
        {
            try
            {
                if (a.updateMode == AnimatorUpdateMode.AnimatePhysics) return false;
                if (a.GetComponent<PowerTools.SpriteAnim>() != null) return true;
                if (a.GetComponentInChildren<Collider2D>(true) != null) return false;
                if (a.GetComponentInChildren<Rigidbody2D>(true) != null) return false;
                return true;
            }
            catch { return false; }
        }

        // O modo: o do jogo (opção desligada) ou "fora da tela não escreve". O que o jogo já deixou mais econômico
        // não é afrouxado.
        static void Por(Peca x, int modo)
        {
            if (x.A == null) return;
            var alvo = modo > 0 && x.DoJogo == AnimatorCullingMode.AlwaysAnimate ? AnimatorCullingMode.CullUpdateTransforms : x.DoJogo;
#if OTIM_TESTE
            if (modo == 9) alvo = AnimatorCullingMode.CullCompletely;   // teto de medida, só no piloto
#endif
            if (x.A.cullingMode != alvo) x.A.cullingMode = alvo;
        }

        // ---------------- os jogadores ----------------

        // Corpo, roupa, brilho e pet de cada jogador: fora da tela não escrevem. 3 jogadores por vez, todos em ~2,5 s
        // (roupa trocada no meio da partida entra na volta seguinte).
        static void Bonecos(int modo)
        {
            var lista = PlayerControl.AllPlayerControls;
            if (lista == null) return;
            int n = lista.Count;
            if (n == 0) return;
            int novas = 0;
            for (int k = 0; k < 3 && k < n; k++)
            {
                _vez++;
                if (_vez >= n) _vez = 0;
                var pc = lista[_vez];
                if (pc == null) continue;
                novas += Vestir(pc, modo);
                // o pet anda solto atrás do dono (não é filho dele)
                var roupa = pc.cosmetics;
                var pet = roupa != null ? roupa.currentPet : null;
                if (pet != null) novas += Vestir(pet, modo);
            }
            if (novas > 0 && !_logouBonecos)
            {
                _logouBonecos = true;
                Log.LogInfo("[leve] jogadores: animacoes de corpo e roupa param de escrever fora da tela (" + novas + " no primeiro jogador visto)");
            }
        }

        static int Vestir(Component dono, int modo)
        {
            int novas = 0;
            var anims = dono.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                var a = anims[i];
                if (a == null) continue;
                IntPtr p = a.Pointer;
                if (_dosBonecos.ContainsKey(p)) continue;
                var x = new Peca { A = a, DoJogo = a.cullingMode };
                _dosBonecos[p] = x;
                // o piloto mede o teto só no mapa: jogador nunca para de vez
                Por(x, modo == 9 ? 1 : modo);
                novas++;
            }
            return novas;
        }

        // ---------------- devolver e limpar ----------------

        // A opção mudou: reaplica em tudo o que já foi visto (0 = devolve o que o jogo tinha).
        static void Repor(int modo)
        {
            foreach (var x in _doMapa.Values) Por(x, modo);
            foreach (var x in _dosBonecos.Values) Por(x, modo == 9 ? 1 : modo);
            Log.LogInfo("[leve] " + (modo == 0 ? "desligado (animacoes como o jogo deixou)" : "ligado") + ": " + _doMapa.Count + " do mapa, " + _dosBonecos.Count + " de jogadores");
        }

        // Roupa trocada e jogador que saiu deixam Animators destruídos na lista: saem a cada 20 s.
        static void Faxina()
        {
            _mortos.Clear();
            foreach (var par in _dosBonecos)
                if (par.Value.A == null) _mortos.Add(par.Key);
            for (int i = 0; i < _mortos.Count; i++) _dosBonecos.Remove(_mortos[i]);
            _mortos.Clear();
        }
    }
}
