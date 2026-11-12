#if OTIM_TESTE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods
{
    /*
      SÓ EXISTE NO BUILD DE TESTE (OTIM_TESTE), no PC do dono: nunca entra no app nem no DLL dos jogadores.

      (11/11) A missão pede medir antes e depois na mesma cena. Não há celular ligado, então o piloto dirige o jogo do
      PC sozinho: abre as opções (confere os botões do nível), abre o Freeplay na Skeld, conta o que anima e o que
      está fora da tela (raio-x), mede o tempo de quadro com cada nível no mesmo lugar (refeitório, motor de cima,
      reator), confere que o relógio das animações fora da tela continua andando, e cria bonecos vestidos que andam
      em círculo (só o corpo local deles: Freeplay, sem rede) para o caso "várias pessoas andando".
      Tudo sai no registro com [piloto], [raiox] e [medida]; as fotos vão para a pasta de fotos do teste.

      Não grava nada na pasta do jogo e não muda resolução nem FPS guardados (no build de teste a Otimizacao não
      aplica nada disso; ver AplicarAgora).
    */
    internal static class Piloto
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;
        static void Diz(string s) => Log.LogInfo("[piloto] " + s);
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;

        static string PastaFotos => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "CriCri Oficina Teste", "fotos");

        static IEnumerator<float> _roteiro;
        static float _acordarEm;
        static bool _acabou;

        // medida: intervalo entre um Update e o seguinte, pelo relógio do processador
        static bool _medindo;
        static long _tickAnterior;
        static readonly float[] _ms = new float[120000];
        static int _n;

        // bonecos que andam
        static readonly List<Rigidbody2D> _corpos = new List<Rigidbody2D>();
        static bool _andar;

        internal static void Quadro(string cena)
        {
            long t = Stopwatch.GetTimestamp();
            if (_medindo && _tickAnterior != 0 && _n < _ms.Length) _ms[_n++] = (float)((t - _tickAnterior) * 1000.0 / Stopwatch.Frequency);
            _tickAnterior = t;
            if (_acabou || Time.unscaledTime < _acordarEm) return;
            _roteiro ??= Roteiro();
            bool segue;
            try { segue = _roteiro.MoveNext(); }
            catch (Exception e)
            {
                _acabou = true;
                Diz("ROTEIRO QUEBROU: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                return;
            }
            if (!segue) { _acabou = true; Diz("FIM em " + Time.realtimeSinceStartup.ToString("0", Inv) + " s de processo"); return; }
            _acordarEm = Time.unscaledTime + _roteiro.Current;
        }

        internal static void Fisica()
        {
            if (!_andar) return;
            float t = Time.fixedTime;
            for (int i = 0; i < _corpos.Count; i++)
            {
                var corpo = _corpos[i];
                if (corpo == null) continue;
                float a = t * 1.6f + i * 0.9f;
                corpo.velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f;
            }
        }

        static void Seguro(string oque, Action acao)
        {
            try { acao(); }
            catch (Exception e) { Diz("ERRO em " + oque + ": " + e.GetType().Name + ": " + e.Message); }
        }

        // ---------------- o roteiro ----------------

        /* O que cada rodada fez (os roteiros antigos estão em trabalho/otimizacao/roteiroN.txt e trabalho/eu/roteiroN.cs.txt):
           1 a 16: medidas, sombra, Diagnóstico, mapas, Definições do PC, raio-x do HUD;
           17: a barra do ping e o editor do HUD; 18: a grade do celular; 19: raio-x da lista e da barra de tarefas;
           20: o editor novo e a lista e a barra de tarefas; 21: raio-x do chat e da reunião; 22: o modo escuro;
           23 (esta, 12/11, depois do celular): os botões movidos (menu, mapa) e o ANALÓGICO de toque (ligado no PC pelo
              SetTouchType) não podem andar com o boneco; o analógico no editor e escuro; a barra do ping no meio. */
        const bool FazOpcoes = false;
        const int QualMapa = 0;   // 0 Skeld, 1 Mira, 2 Polus, 4 Airship, 5 Fungle

        static IEnumerator<float> Roteiro()
        {
            while (SceneManager.GetActiveScene().name != "MainMenu") yield return 0.5f;
            // o jogo.ps1 espera esta frase para saber que o menu chegou
            Diz("no menu em " + Time.realtimeSinceStartup.ToString("0.0", Inv) + " s");
            yield return 5f;
            Seguro("hud limpo", LayoutDoHud.VoltarAoPadrao);
            Opcoes.Atual.BarraPing = BarraDoPing.PingEFps;
            bool abriu = false;
            Seguro("abrir freeplay", () => abriu = AbrirFreeplay(QualMapa));
            if (!abriu) { Diz("NAO ABRIU O FREEPLAY"); yield break; }
            float limite = Time.unscaledTime + 60f;
            while ((ShipStatus.Instance == null || PlayerControl.LocalPlayer == null) && Time.unscaledTime < limite) yield return 0.5f;
            if (ShipStatus.Instance == null || PlayerControl.LocalPlayer == null) { Diz("SEM NAVE depois de 60 s"); yield break; }
            yield return 8f;
            Seguro("monitor 2", CalarMonitor2);
            yield return 1f;
            var h = HudManager.Instance;

            // o analógico do celular, ligado no PC
            Seguro("analogico", () => h.SetTouchType(ControlTypes.VirtualJoystick));
            yield return 1.5f;
            Seguro("joysticks", () =>
            {
                Diz("joystick do jogo: " + (h.joystick != null ? (h.joystick.TryCast<VirtualJoystick>() != null ? "VirtualJoystick" : "outro") : "nulo"));
                var js = h.Joysticks;
                for (int i = 0; js != null && i < js.Length; i++) if (js[i] != null) Diz("Joysticks[" + i + "] " + Caminho(js[i].transform) + " ativo " + js[i].gameObject.activeInHierarchy + " vj " + (js[i].GetComponentInChildren<VirtualJoystick>(true) != null));
                foreach (var go in LayoutDoHud.Pecas[Array.FindIndex(LayoutDoHud.Pecas, x => x.Chave == "joystick")].Objetos(h)) { Diz("peca joystick: " + Caminho(go.transform) + " aspect " + (go.GetComponent<AspectPosition>() != null)); _linhas = 0; Arvore(go.transform, 0, 3); }
            });
            Foto("eu23-1-analogico");
            yield return 0.3f;
            // o ping online em cima do nome da sala (a foto do celular): a barra tem de descer
            BarraDoPing.TesteMs = 45;
            yield return 1.2f;
            Seguro("sala", () => Diz("barra online com a sala: " + Barra() + " | desvio " + BarraDoPing.TesteDesvio.ToString("0.00", Inv) + " | sala " + (h.roomTracker != null && h.roomTracker.text != null ? "[" + h.roomTracker.text.text + "] " + V(h.roomTracker.text.GetComponent<MeshRenderer>().bounds.center) : "-") + " | " + MeioDaBarra()));
            Foto("eu23-1b-ping-e-sala");
            yield return 0.3f;
            BarraDoPing.TesteMs = -1;

            // o editor: o analógico, o menu e o mapa em outro lugar; e escuro completo
            Seguro("abrir editor", EditorDoHud.Abrir);
            yield return 1.2f;
            Foto("eu23-2-editor");
            yield return 0.3f;
            Seguro("mexer", () =>
            {
                EditorDoHud.TesteEscolher("joystick"); EditorDoHud.TesteMover(1.6f, 0.9f); EditorDoHud.TesteTamanho(1.2f);
                EditorDoHud.TesteEscolher("menu"); EditorDoHud.TesteMover(-2.5f, -1.2f);
                EditorDoHud.TesteEscolher("mapa"); EditorDoHud.TesteMover(-1.2f, -0.6f);
                EditorDoHud.TesteEscolher(null);
                LayoutDoHud.PorModoEscuro(LayoutDoHud.EscuroCompleto);
            });
            yield return 1f;
            Diz("editor: " + EditorDoHud.TesteEstado());
            Foto("eu23-3-editor-mexido");
            yield return 0.3f;
            Seguro("pronto", () => EditorDoHud.TesteBotao("Pronto"));
            yield return 1.5f;
            Seguro("na tela", () => NaTela("parado"));
            Foto("eu23-4-parado");
            yield return 0.3f;

            // andar: o boneco vai para outro canto do mapa (a câmera segue); os botões têm de ficar onde estavam na TELA
            var inicio = PlayerControl.LocalPlayer.transform.position;
            Seguro("andar 1", () => PlayerControl.LocalPlayer.NetTransform.SnapTo((Vector2)inicio + new Vector2(7f, -2f)));
            yield return 3f;
            Seguro("na tela", () => NaTela("depois de andar 7"));
            Seguro("joystick escuro", () => { foreach (var go in LayoutDoHud.Pecas[Array.FindIndex(LayoutDoHud.Pecas, x => x.Chave == "joystick")].Objetos(h)) { _linhas = 0; Arvore(go.transform, 0, 3); } });
            Foto("eu23-5-depois-de-andar");
            yield return 0.3f;
            Seguro("andar 2", () => PlayerControl.LocalPlayer.NetTransform.SnapTo((Vector2)inicio + new Vector2(-6f, 4f)));
            yield return 3f;
            Seguro("na tela", () => NaTela("depois de andar -6"));
            // a barra do ping com rede de mentira (o wifi) no meio
            BarraDoPing.TesteMs = 45;
            yield return 1f;
            Seguro("barra", () => Diz("barra: " + Barra() + " | " + MeioDaBarra()));
            Foto("eu23-6-andou-de-novo-e-barra");
            yield return 0.3f;
            Seguro("limpar", LayoutDoHud.VoltarAoPadrao);
            yield return 1f;
            Seguro("na tela", () => NaTela("padrao"));
            Foto("eu23-7-padrao");
            yield return 0.3f;
        }

        // onde cada peça movida está na TELA (0 a 1); não pode mudar quando o boneco anda
        static void NaTela(string quando)
        {
            var h = HudManager.Instance;
            var cam = h.UICamera;
            var sb = new System.Text.StringBuilder(quando + " |");
            foreach (var chave in new[] { "joystick", "menu", "mapa", "info" })
            {
                var peca = LayoutDoHud.Pecas[Array.FindIndex(LayoutDoHud.Pecas, x => x.Chave == chave)];
                foreach (var go in peca.Objetos(h))
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    var v = cam.WorldToViewportPoint(go.transform.position);
                    var col = go.GetComponentInChildren<Collider2D>(false);
                    var vc = col != null ? cam.WorldToViewportPoint(col.bounds.center) : Vector3.zero;
                    sb.Append(' ').Append(chave).Append(" tela (").Append(v.x.ToString("0.000", Inv)).Append(',').Append(v.y.ToString("0.000", Inv)).Append(')')
                      .Append(col != null ? " toque (" + vc.x.ToString("0.000", Inv) + "," + vc.y.ToString("0.000", Inv) + ")" : "").Append(" |");
                }
            }
            sb.Append(" boneco ").Append(V(PlayerControl.LocalPlayer.transform.position));
            Diz(sb.ToString());
        }

        static string MeioDaBarra()
        {
            var r = BarraDoPing.Raiz;
            if (r == null) return "sem barra";
            var cam = HudManager.Instance.UICamera;
            float min = float.MaxValue, max = float.MinValue;
            foreach (var x in r.GetComponentsInChildren<Renderer>(false)) { if (x == null) continue; var b = x.bounds; min = Math.Min(min, b.min.x); max = Math.Max(max, b.max.x); }
            var t = r.GetComponentInChildren<TMPro.TextMeshPro>(false);
            if (t != null) { t.ForceMeshUpdate(); var tb = t.textBounds; var c = t.transform.TransformPoint(tb.center); var e = t.transform.TransformVector(tb.extents); min = Math.Min(min, c.x - e.x); max = Math.Max(max, c.x + e.x); }
            float meio = (min + max) / 2f;
            var ping = BarraDoPing.PingDoJogo;
            return "meio da barra na tela " + cam.WorldToViewportPoint(new Vector3(meio, r.transform.position.y, r.transform.position.z)).x.ToString("0.000", Inv)
                + " (o ping do jogo fica em " + (ping != null ? cam.WorldToViewportPoint(ping.transform.position).x.ToString("0.000", Inv) : "-") + ")";
        }

        static int _linhas;
        static string N(Component c) => c == null ? "nulo" : c.name + (c.gameObject.activeInHierarchy ? "" : "(desl)") + " " + V(c.transform.position);
        static string N(GameObject g) => g == null ? "nulo" : N(g.transform);

        static void Arvore(Transform t, int nivel, int max)
        {
            if (t == null || nivel > max || _linhas > 170) return;
            _linhas++;
            var sr = t.GetComponent<SpriteRenderer>();
            var tm = t.GetComponent<TMPro.TextMeshPro>();
            var col = t.GetComponent<Collider2D>();
            var ap = t.GetComponent<AspectPosition>();
            var comps = new System.Text.StringBuilder();
            foreach (var c in t.GetComponents<Component>()) if (c != null) { var n = c.GetIl2CppType().Name; if (n != "Transform") comps.Append(n).Append(','); }
            string spr = "";
            if (sr != null)
            {
                var s = sr.sprite;
                string pack = "";
                if (s != null) { try { pack = s.packed ? (" pack " + s.packingMode + "/" + s.packingRotation) : " solto"; } catch { pack = " ?"; } }
                spr = " | sprite " + (s != null ? s.name + " tex " + (s.texture != null ? s.texture.name + " " + s.texture.width + "x" + s.texture.height : "-") + pack : "-")
                    + " cor " + Cor(sr.color) + " modo " + sr.drawMode + " shader " + (sr.sharedMaterial != null && sr.sharedMaterial.shader != null ? sr.sharedMaterial.shader.name : "-") + (sr.enabled ? "" : " (off)");
            }
            Diz(new string(' ', nivel * 2) + t.name + (t.gameObject.activeSelf ? "" : " (desl)") + " | " + comps + spr
                + (tm != null ? " | texto [" + (tm.text.Length > 30 ? tm.text.Substring(0, 30) : tm.text) + "] cor " + Cor(tm.color) : "")
                + (col != null ? " | colisor" : "") + (ap != null ? " | aspect " + ap.Alignment + " " + V(ap.DistanceFromEdge) : ""));
            for (int i = 0; i < t.childCount; i++) Arvore(t.GetChild(i), nivel + 1, max);
        }

        static string Cor(Color c) => "(" + c.r.ToString("0.00", Inv) + "," + c.g.ToString("0.00", Inv) + "," + c.b.ToString("0.00", Inv) + "," + c.a.ToString("0.00", Inv) + ")";

        static string Caminho(Transform t)
        {
            var partes = new List<string>();
            for (var x = t; x != null && partes.Count < 8; x = x.parent) partes.Add(x.name);
            partes.Reverse();
            return string.Join("/", partes);
        }

        static string Barra()
        {
            var r = BarraDoPing.Raiz;
            if (r == null) return "sem barra";
            var t = r.GetComponentInChildren<TMPro.TextMeshPro>(true);
            var w = r.GetComponentInChildren<SpriteRenderer>(true);
            return "texto [" + (t != null ? t.text : "-") + "] wifi " + (w != null && w.gameObject.activeSelf ? "sim" : "nao") + " lugar " + V(r.transform.position)
                + " | ping do jogo " + (BarraDoPing.PingDoJogo != null && BarraDoPing.PingDoJogo.text != null ? (BarraDoPing.PingDoJogo.text.enabled ? "aparecendo" : "escondido") : "-");
        }

        // onde cada peça mexida aparece e onde o toque a acha (o colisor); têm de bater
        static void Lugares(string quando)
        {
            var h = HudManager.Instance;
            foreach (var peca in LayoutDoHud.Pecas)
            {
                if (peca.Chave != "matar" && peca.Chave != "mapa" && peca.Chave != "reportar" && peca.Chave != "usar" && peca.Chave != "ping") continue;
                foreach (var go in peca.Objetos(h))
                {
                    if (go == null || !go.activeInHierarchy) continue;
                    var col = go.GetComponentInChildren<Collider2D>(false);
                    var ap = go.GetComponent<AspectPosition>();
                    var sr = go.GetComponentInChildren<SpriteRenderer>(false);
                    Diz(quando + " | " + peca.Chave + " '" + go.name + "' lugar " + V(go.transform.position) + " escala " + V(go.transform.localScale)
                        + " | colisor " + (col != null ? (col.enabled ? "ligado " : "DESLIGADO ") + V(col.bounds.center) : "nenhum")
                        + " | aspect " + (ap != null ? V(ap.DistanceFromEdge) : "nenhum") + " | cor " + (sr != null ? sr.color.a.ToString("0.00", Inv) : "-"));
                }
            }
        }

        // Os bonecos param e a cena assenta (usado por outros roteiros).
        static IEnumerable<float> PararBonecos()
        {
            _andar = false;
            for (int i = 0; i < _corpos.Count; i++) if (_corpos[i] != null) _corpos[i].velocity = Vector2.zero;
            yield return 1.5f;
        }

        static string V(Vector3 v) => "(" + v.x.ToString("0.00", Inv) + "," + v.y.ToString("0.00", Inv) + "," + v.z.ToString("0.00", Inv) + ")";

        // O que está ligado agora: o que o Diagnóstico desliga e tem de devolver.
        static string Retrato()
        {
            var sb = new StringBuilder();
            try
            {
                var eu = PlayerControl.LocalPlayer;
                var todos = PlayerControl.AllPlayerControls;
                int desenhos = 0, anims = 0, pets = 0, animsDosPets = 0, comPet = 0, scripts = 0, corpos = 0;
                for (int i = 0; i < todos.Count; i++)
                {
                    var pc = todos[i];
                    if (pc == null || pc.Pointer == eu.Pointer) continue;
                    foreach (var r in pc.GetComponentsInChildren<Renderer>(false)) if (r != null && r.enabled) desenhos++;
                    foreach (var a in pc.GetComponentsInChildren<Animator>(false)) if (a != null && a.enabled) anims++;
                    var pet = pc.cosmetics != null ? pc.cosmetics.currentPet : null;
                    if (pet == null) continue;
                    comPet++;
                    if (pet.enabled) scripts++;
                    var corpo = pet.GetComponent<Rigidbody2D>();
                    if (corpo != null && corpo.simulated) corpos++;
                    foreach (var r in pet.GetComponentsInChildren<Renderer>(false)) if (r != null && r.enabled) pets++;
                    foreach (var a in pet.GetComponentsInChildren<Animator>(false)) if (a != null && a.enabled) animsDosPets++;
                }
                int mapa = 0, animsMapa = 0;
                var nave = ShipStatus.Instance;
                foreach (var r in nave.GetComponentsInChildren<Renderer>(false)) if (r != null && r.enabled) mapa++;
                foreach (var a in nave.GetComponentsInChildren<Animator>(false)) if (a != null && a.enabled) animsMapa++;
                var luz = eu.lightSource;
                var rl = luz != null ? luz.renderer : null;
                var hud = HudManager.InstanceExists ? HudManager.Instance.UICamera : null;
                sb.Append("outros ").Append(todos.Count - 1).Append(": desenhos ").Append(desenhos).Append(", animators ").Append(anims)
                  .Append(" | pets ").Append(comPet).Append(": desenhos ").Append(pets).Append(", animators ").Append(animsDosPets)
                  .Append(", scripts ").Append(scripts).Append(", corpos ").Append(corpos)
                  .Append(" | mapa: desenhos ").Append(mapa).Append(", animators ").Append(animsMapa)
                  .Append(" | luz ").Append(luz != null && luz.enabled ? "ligada" : "DESLIGADA").Append(", em uso ").Append(rl != null ? rl.Type.ToString() : "nenhuma")
                  .Append(", pedida ").Append(luz != null ? luz.rendererType.ToString() : "-")
                  .Append(" | HUD ").Append(hud != null && hud.enabled ? "ligado" : "DESLIGADO");
                var cams = new List<string>();
                foreach (var c in Camera.allCameras) if (c != null) cams.Add(c.name + "=" + c.cullingMask.ToString("X") + (c.enabled ? "" : "(parada)"));
                cams.Sort(string.CompareOrdinal);
                sb.Append(" | cameras ").Append(cams.Count).Append(": ").Append(string.Join(", ", cams));
            }
            catch (Exception e) { sb.Append("ERRO ").Append(e.GetType().Name).Append(": ").Append(e.Message); }
            return sb.ToString();
        }

        static SystemTypes[] SalasDoMapa(int mapa)
        {
            switch (mapa)
            {
                case 5: return new[] { SystemTypes.Jungle, SystemTypes.Reactor, SystemTypes.Kitchen };
                case 2: return new[] { SystemTypes.Laboratory, SystemTypes.Electrical, SystemTypes.Specimens };
                case 4: return new[] { SystemTypes.Engine, SystemTypes.MainHall, SystemTypes.Kitchen };
                case 1: return new[] { SystemTypes.Reactor, SystemTypes.Greenhouse, SystemTypes.Launchpad };
                default: return new[] { SystemTypes.UpperEngine, SystemTypes.Reactor };
            }
        }
        // ---------------- as chaves do mapa (rodada 4) ----------------

        static LightSource _luz;
        static readonly List<Animator> _animsMapa = new List<Animator>();
        static readonly List<MonoBehaviour> _enfeites = new List<MonoBehaviour>();

        static (string nome, string foto, Action<bool> liga)[] ChavesDoMapa() => new (string, string, Action<bool>)[]
        {
            ("sombra por RAIOS (a do celular)", "raios", v => PorSombra(v ? LightSourceRendererType.GPU : LightSourceRendererType.Raycast)),
            ("sem a luz (LightSource desligado)", null, v => { if (_luz != null) _luz.enabled = v; }),
            ("sem os Animators do mapa", null, v => Cada(_animsMapa, a => a.enabled = v)),
            ("sem os scripts de enfeite", null, v => Cada(_enfeites, s => s.enabled = v)),
        };

        static void AcharLuz()
        {
            _luz = PlayerControl.LocalPlayer.lightSource;
            if (_luz == null) { Diz("luz: o jogador nao tem LightSource"); return; }
            var r = _luz.renderer;
            Log.LogInfo("[raiox] luz: tipo pedido " + _luz.rendererType + ", em uso " + (r != null ? r.Type.ToString() : "nenhum") + ", alcance " + _luz.viewDistance.ToString("0.00", Inv)
                + ", raios minimos " + _luz.raycastMinRayCount + ", tolerancia " + _luz.raycastTolerance.ToString("0.0000", Inv) + ", mapa de sombra " + _luz.gpuShadowmapResolution
                + " " + _luz.gpuPreferredRTFormat + ", geometria na placa " + SystemInfo.supportsGeometryShaders + ", plataforma " + Application.platform);
        }

        // A sombra do jogo tem dois caminhos, e o próprio jogo escolhe pela plataforma (LightSource.UpdateShadowType:
        // Android = Raycast, o resto = GPU). Aqui o piloto pede um deles para medir o do celular no PC.
        static void PorSombra(LightSourceRendererType tipo)
        {
            if (_luz == null) return;
            _luz.rendererType = tipo;
            _luz.UpdateShadowType();
            var r = _luz.renderer;
            Diz("sombra: pedi " + tipo + ", em uso " + (r != null ? r.Type.ToString() : "nenhum"));
        }

        static void DizerRaios()
        {
            if (_luz == null || _luz.renderer == null) return;
            var rr = _luz.renderer.TryCast<LightSourceRaycastRenderer>();
            if (rr == null) { Diz("raios: a sombra em uso nao e a de raios"); return; }
            Vector2 eu = PlayerControl.LocalPlayer.GetTruePosition();
            Log.LogInfo("[raiox] sombra por raios em " + eu.x.ToString("0.0", Inv) + "," + eu.y.ToString("0.0", Inv) + ": " + rr.vertCount + " pontos na malha da luz neste quadro");
        }

        static void JuntarDoMapa()
        {
            _animsMapa.Clear(); _enfeites.Clear();
            var nave = ShipStatus.Instance;
            foreach (var a in nave.GetComponentsInChildren<Animator>(false))
                if (a != null && a.isActiveAndEnabled) _animsMapa.Add(a);
            var quais = new HashSet<string> { "SteamBehaviour", "ChainBehaviour", "StarGen", "SpriteParticle", "TumbleBoxBehaviour", "EngineBehaviour" };
            var tipos = new Dictionary<string, int>();
            foreach (var s in nave.GetComponentsInChildren<MonoBehaviour>(false))
            {
                if (s == null || !s.isActiveAndEnabled) continue;
                string n = s.GetIl2CppType().Name;
                if (!quais.Contains(n)) continue;
                _enfeites.Add(s);
                tipos[n] = tipos.TryGetValue(n, out int c0) ? c0 + 1 : 1;
            }
            Log.LogInfo("[raiox] chaves do mapa: " + _animsMapa.Count + " Animators ligados, " + _enfeites.Count + " scripts de enfeite (" + Junta(tipos) + ")");
        }

        static Camera _principal;
        static Renderer _longeMapa, _pertoMapa, _longeJog;
        static Animator _longeAnim;

        static void Ver(string quando)
        {
            Seguro("ver", () =>
            {
                string S(Renderer r) => r == null ? "-" : r.name + " visivel=" + r.isVisible;
                Log.LogInfo("[sonda] " + quando + ": mapa longe " + S(_longeMapa) + " | mapa perto " + S(_pertoMapa) + " | jogador longe " + S(_longeJog));
            });
        }

        // Todas as câmeras carregadas, ligadas ou não (o Camera.allCameras só dá as ligadas).
        static List<Camera> TodasAsCameras()
        {
            var l = new List<Camera>();
            foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Camera>()))
            {
                var c = o != null ? o.TryCast<Camera>() : null;
                if (c != null) l.Add(c);
            }
            return l;
        }

        static void ListarCameras()
        {
            foreach (var c in TodasAsCameras())
            {
                if (c == null) continue;
                var rt = c.targetTexture;
                var p = c.transform.position;
                string cena = "?";
                try { cena = c.gameObject.scene.name; } catch { }
                string pai = c.transform.parent != null ? c.transform.parent.name : "-";
                Log.LogInfo("[sonda] camera " + c.name + " (pai " + pai + ", cena " + cena + "): ativa " + c.gameObject.activeInHierarchy + ", ligada " + c.enabled + ", tela " + c.targetDisplay
                    + ", orto " + c.orthographic + " " + c.orthographicSize.ToString("0.00", Inv) + ", aspecto " + c.aspect.ToString("0.00", Inv) + ", pos " + p.x.ToString("0.0", Inv) + "," + p.y.ToString("0.0", Inv)
                    + ", mascara " + c.cullingMask.ToString("X") + ", alvo " + (rt != null ? rt.width + "x" + rt.height : "a tela") + ", oclusao " + c.useOcclusionCulling
                    + ", longe " + c.farClipPlane.ToString("0", Inv) + ", retangulo " + c.rect);
            }
            var m = Camera.main;
            if (m != null)
            {
                var cm = m.cullingMatrix;
                var pm = m.projectionMatrix * m.worldToCameraMatrix;
                Log.LogInfo("[sonda] principal: matriz de corte m00 " + cm.m00.ToString("0.0000", Inv) + " m11 " + cm.m11.ToString("0.0000", Inv) + " | projecao*vista m00 " + pm.m00.ToString("0.0000", Inv) + " m11 " + pm.m11.ToString("0.0000", Inv));
            }
        }

        static void EscolherAlvos()
        {
            var nave = ShipStatus.Instance;
            Vector2 eu = PlayerControl.LocalPlayer.GetTruePosition();
            float maior = 0f, menor = float.MaxValue;
            foreach (var r in nave.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if (b.size.x > 12f || b.size.y > 12f) continue;   // fundos enormes não servem de prova
                float d = ((Vector2)b.center - eu).magnitude;
                if (d > maior) { maior = d; _longeMapa = r; }
                if (d < menor) { menor = d; _pertoMapa = r; }
            }
            float maiorA = 0f;
            foreach (var a in nave.GetComponentsInChildren<Animator>(false))
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                float d = ((Vector2)a.transform.position - eu).magnitude;
                if (d > maiorA) { maiorA = d; _longeAnim = a; }
            }
            float maiorJ = 0f;
            var lista = PlayerControl.AllPlayerControls;
            for (int i = 0; i < lista.Count; i++)
            {
                var pc = lista[i];
                if (pc == null || pc.AmOwner) continue;
                float d = (pc.GetTruePosition() - eu).magnitude;
                if (d <= maiorJ) continue;
                var corpo = pc.cosmetics != null && pc.cosmetics.currentBodySprite != null ? pc.cosmetics.currentBodySprite.BodySprite : null;
                if (corpo == null) continue;
                maiorJ = d; _longeJog = corpo;
            }
            string D(Renderer r)
            {
                if (r == null) return "-";
                var b = r.bounds;
                string caminho = r.name;
                for (var t = r.transform.parent; t != null && caminho.Length < 160; t = t.parent) caminho = t.name + "/" + caminho;
                return caminho + " centro " + b.center.x.ToString("0.0", Inv) + "," + b.center.y.ToString("0.0", Inv) + " tamanho " + b.size.x.ToString("0.0", Inv) + "x" + b.size.y.ToString("0.0", Inv)
                    + " camada " + r.gameObject.layer + " estatico " + r.gameObject.isStatic + " loteEstatico " + r.isPartOfStaticBatch + " tipo " + r.GetIl2CppType().Name;
            }
            Log.LogInfo("[sonda] eu em " + eu.x.ToString("0.0", Inv) + "," + eu.y.ToString("0.0", Inv));
            Log.LogInfo("[sonda] alvo do mapa LONGE (" + maior.ToString("0.0", Inv) + "): " + D(_longeMapa));
            Log.LogInfo("[sonda] alvo do mapa PERTO (" + menor.ToString("0.0", Inv) + "): " + D(_pertoMapa));
            Log.LogInfo("[sonda] alvo jogador LONGE (" + maiorJ.ToString("0.0", Inv) + "): " + D(_longeJog));
            Log.LogInfo("[sonda] animacao LONGE (" + maiorA.ToString("0.0", Inv) + "): " + (_longeAnim != null ? _longeAnim.name : "-"));
        }

        static void Grupos()
        {
            var nave = ShipStatus.Instance;
            var grupos = nave.GetComponentsInChildren<UnityEngine.Rendering.SortingGroup>(true);
            var sb = new StringBuilder();
            foreach (var g in grupos) if (g != null && sb.Length < 600) sb.Append(g.name).Append(g.isActiveAndEnabled ? "" : "(desl)").Append("; ");
            Log.LogInfo("[sonda] SortingGroup na nave: " + grupos.Length + ": " + sb);
            var eu = PlayerControl.LocalPlayer;
            var gj = eu.GetComponentsInChildren<UnityEngine.Rendering.SortingGroup>(true);
            Log.LogInfo("[sonda] SortingGroup no jogador: " + gj.Length);
            int estaticos = 0, lote = 0, total = 0;
            foreach (var r in nave.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null) continue;
                total++;
                if (r.gameObject.isStatic) estaticos++;
                if (r.isPartOfStaticBatch) lote++;
            }
            Log.LogInfo("[sonda] desenhos da nave: " + total + ", estaticos " + estaticos + ", em lote estatico " + lote);
        }

        // O relógio de até 8 animações a mais de 12 unidades de mim, em cada nível (1,5 s por nível).
        static IEnumerable<float> RelogioPorDistancia()
        {
            var amostra = new List<Animator>();
            var nomes = new List<string>();
            Seguro("amostra", () =>
            {
                Vector2 eu = PlayerControl.LocalPlayer.GetTruePosition();
                int lacos = 0, outras = 0;
                foreach (var a in ShipStatus.Instance.GetComponentsInChildren<Animator>(false))
                {
                    if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                    if (((Vector2)a.transform.position - eu).magnitude < 12f) continue;
                    bool laco = a.GetCurrentAnimatorStateInfo(0).loop;
                    if (laco && lacos < 4) { lacos++; amostra.Add(a); nomes.Add("laco:" + a.name); }
                    else if (!laco && outras < 4) { outras++; amostra.Add(a); nomes.Add("termina:" + a.name); }
                }
            });
            if (amostra.Count == 0) { Diz("relogio: nenhuma animacao longe"); yield break; }
            foreach (int nivel in new[] { 0, 2, 3, 9, 0 })
            {
                Leve.NivelForcado = nivel;
                yield return 1.2f;
                var antes = new float[amostra.Count];
                Seguro("relogio antes", () => { for (int i = 0; i < amostra.Count; i++) antes[i] = amostra[i] != null ? amostra[i].GetCurrentAnimatorStateInfo(0).normalizedTime : -1f; });
                yield return 1.5f;
                var sb = new StringBuilder("[sonda] RELOGIO longe, nivel " + nivel + ", avanco em 1,5 s: ");
                Seguro("relogio depois", () =>
                {
                    for (int i = 0; i < amostra.Count; i++)
                    {
                        float agora = amostra[i] != null ? amostra[i].GetCurrentAnimatorStateInfo(0).normalizedTime : -1f;
                        sb.Append(nomes[i]).Append('=').Append((agora - antes[i]).ToString("0.00", Inv)).Append(' ').Append(amostra[i] != null ? amostra[i].cullingMode.ToString() : "?").Append("; ");
                    }
                });
                Log.LogInfo(sb.ToString());
            }
        }

        // ---------------- peças dos jogadores, para as chaves de medida ----------------

        static readonly List<Animator> _animsJog = new List<Animator>();
        static readonly List<Animator> _glows = new List<Animator>();
        static readonly List<GameObject> _pets = new List<GameObject>();
        static readonly List<GameObject> _nomes = new List<GameObject>();
        static readonly List<PowerTools.SpriteAnimNodeSync> _syncs = new List<PowerTools.SpriteAnimNodeSync>();

        static void Cada<T>(List<T> lista, Action<T> acao)
        {
            for (int i = 0; i < lista.Count; i++)
            {
                try { if (lista[i] != null) acao(lista[i]); } catch { }
            }
        }

        static void JuntarPecas()
        {
            _animsJog.Clear(); _glows.Clear(); _pets.Clear(); _nomes.Clear(); _syncs.Clear();
            var lista = PlayerControl.AllPlayerControls;
            var tipos = new Dictionary<string, int>();
            int desenhos = 0, desenhosLigados = 0;
            for (int i = 0; i < lista.Count; i++)
            {
                var pc = lista[i];
                if (pc == null) continue;
                foreach (var a in pc.GetComponentsInChildren<Animator>(false))
                {
                    if (a == null || !a.isActiveAndEnabled) continue;
                    _animsJog.Add(a);
                    if (a.name == "SpawnInGlow") _glows.Add(a);
                }
                foreach (var s in pc.GetComponentsInChildren<PowerTools.SpriteAnimNodeSync>(false))
                    if (s != null && s.isActiveAndEnabled) _syncs.Add(s);
                foreach (var r in pc.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    desenhos++;
                    if (r.enabled && r.gameObject.activeInHierarchy) desenhosLigados++;
                }
                if (i == 1)
                    foreach (var s in pc.GetComponentsInChildren<MonoBehaviour>(false))
                    {
                        if (s == null || !s.isActiveAndEnabled) continue;
                        string n = s.GetIl2CppType().Name;
                        tipos[n] = tipos.TryGetValue(n, out int c0) ? c0 + 1 : 1;
                    }
                var roupa = pc.cosmetics;
                if (roupa != null)
                {
                    if (roupa.nameTextContainer != null) _nomes.Add(roupa.nameTextContainer);
                    var pet = roupa.currentPet;
                    if (pet != null)
                    {
                        _pets.Add(pet.gameObject);
                        foreach (var a in pet.GetComponentsInChildren<Animator>(false))
                            if (a != null && a.isActiveAndEnabled) _animsJog.Add(a);
                    }
                }
            }
            Log.LogInfo("[raiox] pecas dos " + lista.Count + " jogadores: " + _animsJog.Count + " Animators ligados (" + _glows.Count + " SpawnInGlow), " + _pets.Count + " pets, "
                + _syncs.Count + " SpriteAnimNodeSync, " + _nomes.Count + " nomes, " + desenhos + " desenhos (" + desenhosLigados + " ligados); scripts ligados no jogador 1: " + Junta(tipos, 40));
        }

        // Só nesta sessão de teste: o objeto do Spectator (monitor 2) e as câmeras dele ficam desligados.
        static void CalarMonitor2()
        {
            var gerente = GameObject.Find("SpectatorModManager");
            if (gerente != null) gerente.SetActive(false);
            int cams = 0;
            foreach (var c in Camera.allCameras)
                if (c != null && c.targetDisplay != 0) { c.gameObject.SetActive(false); cams++; }
            Diz("monitor 2 desligado nesta sessao: gerente " + (gerente != null) + ", " + cams + " cameras");
        }

        static void RaioXCurto()
        {
            void L(string s) => Log.LogInfo("[raiox] " + s);
            foreach (var c in Camera.allCameras)
            {
                if (c == null) continue;
                var rt = c.targetTexture;
                L("camera " + c.name + ": tela " + c.targetDisplay + ", orto " + c.orthographicSize.ToString("0.00", Inv) + ", mascara " + c.cullingMask.ToString("X")
                    + ", alvo " + (rt != null ? rt.width + "x" + rt.height : "a tela"));
            }
            var nave = ShipStatus.Instance;
            int total = 0, ligados = 0, naTela = 0;
            foreach (var r in nave.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                total++;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                ligados++;
                if (r.isVisible) naTela++;
            }
            L("mapa, desenhos (Renderer): " + total + ", " + ligados + " ligados, " + naTela + " na tela");
        }

        // ---------------- as opções (o nível) ----------------

        static IEnumerable<float> TestarOpcoes()
        {
            OptionsMenuBehaviour menu = null;
            Seguro("abrir opcoes", () =>
            {
                var menus = UnityEngine.Object.FindObjectsOfType<OptionsMenuBehaviour>(true);
                Diz("menus de opcoes na cena: " + menus.Length);
                if (menus.Length > 0) { menu = menus[0]; menu.Open(); }
            });
            if (menu == null) yield break;
            yield return 1.2f;
            Seguro("aba graficos", () =>
            {
                var abas = menu.Tabs;
                int g = -1;
                for (int i = 0; i < abas.Length; i++)
                    if (abas[i] != null && abas[i].name.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0) g = i;
                Diz("aba Graficos = " + g + " de " + abas.Length + "; montagens " + AbaCriCri.TesteMontagens);
                if (g >= 0) menu.OpenTabGroup(g);
            });
            yield return 1.6f;
            Diz("opcoes ao abrir: " + AbaCriCri.TesteRotulos());
            Foto("otim-opcoes-1");
            yield return 0.3f;
            Seguro("mais opcoes", () => AbaCriCri.TesteClicar(3));
            yield return 1.2f;
            Diz("opcoes com Mais: " + AbaCriCri.TesteRotulos());
            Foto("otim-opcoes-2-mais");
            yield return 0.3f;
            for (int k = 1; k <= 4; k++)
            {
                Seguro("toque no nivel", () => AbaCriCri.TesteClicar(0));
                yield return 0.9f;
                Diz("toque " + k + " no nivel: " + AbaCriCri.TesteRotulos() + " >> " + Opcoes.Atual.Descrever());
            }
            // o botão 7 é o "Sombra do PC (teste)": liga e desliga
            for (int k = 1; k <= 2; k++)
            {
                Seguro("toque na sombra", () => AbaCriCri.TesteClicar(7));
                yield return 0.9f;
                Diz("toque " + k + " na sombra: " + AbaCriCri.TesteRotulos() + " >> " + Opcoes.Atual.Descrever());
                if (k == 1) Foto("otim-opcoes-3-sombra");
            }
            Seguro("fechar mais", () => AbaCriCri.TesteClicar(3));
            yield return 0.8f;
            Diz("opcoes ao fechar Mais: " + AbaCriCri.TesteRotulos());
            Seguro("fechar opcoes", () => menu.Close());
            yield return 1f;
        }

        // ---------------- dirigir o jogo ----------------

        // O caminho do próprio jogo para o Freeplay (o mesmo do CriCri Replay): o computador do Freeplay fica desligado
        // até abrirem o submenu Jogar, e corotina em objeto desligado a Unity recusa em silêncio.
        static bool AbrirFreeplay(int mapa)
        {
            var pops = UnityEngine.Object.FindObjectsOfType<FreeplayPopover>(true);
            Diz("FreeplayPopover na cena: " + pops.Length);
            if (pops.Length == 0) return false;
            var pop = pops[0];
            Ligar(pop.transform);
            try { pop.Show(); } catch (Exception e) { Diz("popover.Show: " + e.Message); }
            var host = pop.hostGameButton;
            if (host != null) Ligar(host.transform);
            if (host == null || !host.isActiveAndEnabled) { Diz("botao de comecar desligado"); return false; }
            pop.PlayMap((MapNames)mapa);
            Diz("PlayMap(" + (MapNames)mapa + ")");
            return true;
        }

        static void Ligar(Transform t)
        {
            var cadeia = new List<Transform>();
            for (var x = t; x != null; x = x.parent) cadeia.Add(x);
            for (int i = cadeia.Count - 1; i >= 0; i--)
                if (!cadeia[i].gameObject.activeSelf) cadeia[i].gameObject.SetActive(true);
        }

        static bool Ir(SystemTypes sala)
        {
            var nave = ShipStatus.Instance;
            var eu = PlayerControl.LocalPlayer;
            if (nave == null || eu == null) return false;
            var salas = nave.AllRooms;
            for (int i = 0; i < salas.Length; i++)
            {
                var s = salas[i];
                if (s == null || s.RoomId != sala || s.roomArea == null) continue;
                Vector2 centro = s.roomArea.bounds.center;
                eu.NetTransform.SnapTo(centro);
                Diz("fui para " + sala + " em " + centro.x.ToString("0.0", Inv) + "," + centro.y.ToString("0.0", Inv));
                return true;
            }
            Diz("nao achei a sala " + sala);
            return false;
        }

        // Quadro solto (sem vsync, sem teto): o tempo do quadro passa a ser o trabalho do quadro.
        static void Solto()
        {
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != 1000) Application.targetFrameRate = 1000;
        }

        // ---------------- medir ----------------

        static IEnumerable<float> Janela(string rotulo, int nivel, float segundos)
        {
            Leve.NivelForcado = nivel;
            Solto();
            yield return 1.0f;   // o Leve aplica em até 0,25 s e o vigia em até 0,5 s
            _n = 0;
            _medindo = true;
            yield return segundos;
            _medindo = false;
            Relatar(rotulo, nivel);
        }

        static void Relatar(string rotulo, int nivel)
        {
            int n = _n;
            if (n < 10) { Log.LogInfo("[medida] " + rotulo + " nivel=" + nivel + ": so " + n + " quadros"); return; }
            var v = new float[n];
            Array.Copy(_ms, v, n);
            Array.Sort(v);
            double soma = 0;
            for (int i = 0; i < n; i++) soma += v[i];
            float media = (float)(soma / n);
            string F(float x) => x.ToString("0.000", Inv);
            Log.LogInfo("[medida] " + rotulo + " | nivel=" + nivel + " | quadros=" + n + " | media=" + F(media) + " ms (" + (1000f / media).ToString("0", Inv) + " fps)"
                + " | mediana=" + F(v[n / 2]) + " | p10=" + F(v[n / 10]) + " | p90=" + F(v[n * 9 / 10]) + " | p99=" + F(v[n * 99 / 100]) + " | pior=" + F(v[n - 1])
                + " | mapa=" + Leve.NoMapa + " (deixadas " + Leve.Deixados + ") bonecos=" + Leve.NosBonecos);
        }

        // Quantos Animators do mapa e dos jogadores estão ligados, em que modo, e quantos têm desenho na tela agora.
        static void Contar(string rotulo)
        {
            var nave = ShipStatus.Instance;
            if (nave == null) return;
            Log.LogInfo("[raiox] CONTA " + rotulo + " | mapa: " + ContarEm(nave.GetComponentsInChildren<Animator>(true)));
            var todos = new List<Animator>();
            var lista = PlayerControl.AllPlayerControls;
            for (int i = 0; i < lista.Count; i++)
            {
                var pc = lista[i];
                if (pc == null) continue;
                foreach (var a in pc.GetComponentsInChildren<Animator>(true)) todos.Add(a);
                var pet = pc.cosmetics != null ? pc.cosmetics.currentPet : null;
                if (pet != null) foreach (var a in pet.GetComponentsInChildren<Animator>(true)) todos.Add(a);
            }
            Log.LogInfo("[raiox] CONTA " + rotulo + " | " + lista.Count + " jogadores: " + ContarEm(todos));
            int nuvens = 0, tocando = 0, pausadas = 0, vivas = 0;
            foreach (var ps in nave.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                nuvens++;
                if (ps.isPlaying) tocando++;
                if (ps.isPaused) pausadas++;
                vivas += ps.particleCount;
            }
            Log.LogInfo("[raiox] CONTA " + rotulo + " | particulas: " + nuvens + " sistemas, " + tocando + " tocando, " + pausadas + " pausados, " + vivas + " particulas vivas");
        }

        static string ContarEm(IEnumerable<Animator> anims)
        {
            int total = 0, ligados = 0, naTela = 0, sempre = 0, naoEscreve = 0, paraDeVez = 0, escrevendo = 0, avaliando = 0;
            foreach (var a in anims)
            {
                if (a == null) continue;
                total++;
                if (!a.isActiveAndEnabled) continue;
                ligados++;
                bool visivel = NaTela(a);
                if (visivel) naTela++;
                var modo = a.cullingMode;
                if (modo == AnimatorCullingMode.AlwaysAnimate) sempre++;
                else if (modo == AnimatorCullingMode.CullUpdateTransforms) naoEscreve++;
                else paraDeVez++;
                if (visivel || modo == AnimatorCullingMode.AlwaysAnimate) escrevendo++;
                if (visivel || modo != AnimatorCullingMode.CullCompletely) avaliando++;
            }
            return total + " animators, " + ligados + " ligados, " + naTela + " com desenho na tela, " + (ligados - naTela) + " fora da tela | modo: sempre " + sempre
                + ", nao escreve fora da tela " + naoEscreve + ", para de vez " + paraDeVez + " | ESCREVENDO o desenho agora: " + escrevendo + " | andando o relogio: " + avaliando;
        }

        static bool NaTela(Animator a)
        {
            foreach (var r in a.GetComponentsInChildren<Renderer>(false))
                if (r != null && r.enabled && r.isVisible) return true;
            return false;
        }

        // O relógio (normalizedTime) de animações fora da tela, em 1,5 s, em cada nível.
        static IEnumerable<float> Relogio()
        {
            var amostra = new List<Animator>();
            var nomes = new List<string>();
            Seguro("amostra", () =>
            {
                int cand = 0, outras = 0;
                foreach (var a in ShipStatus.Instance.GetComponentsInChildren<Animator>(false))
                {
                    if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null || NaTela(a)) continue;
                    bool laco = a.GetCurrentAnimatorStateInfo(0).loop;
                    bool soEnfeite = true;
                    foreach (var s in a.gameObject.GetComponents<MonoBehaviour>())
                        if (s != null && s.TryCast<PowerTools.SpriteAnim>() == null) soEnfeite = false;
                    if (soEnfeite && laco && cand < 4) { cand++; amostra.Add(a); nomes.Add("enfeite:" + a.name); }
                    else if (!soEnfeite && outras < 4) { outras++; amostra.Add(a); nomes.Add("com-script" + (laco ? "(laco)" : "(termina)") + ":" + a.name); }
                }
            });
            if (amostra.Count == 0) { Diz("relogio: nenhuma animacao fora da tela para amostrar"); yield break; }
            foreach (int nivel in new[] { 0, 2, 3, 9, 0 })
            {
                Leve.NivelForcado = nivel;
                yield return 1.2f;
                var antes = new float[amostra.Count];
                Seguro("relogio antes", () => { for (int i = 0; i < amostra.Count; i++) antes[i] = amostra[i] != null ? amostra[i].GetCurrentAnimatorStateInfo(0).normalizedTime : -1f; });
                yield return 1.5f;
                var sb = new StringBuilder("[raiox] RELOGIO fora da tela, nivel " + nivel + ", avanco em 1,5 s: ");
                Seguro("relogio depois", () =>
                {
                    for (int i = 0; i < amostra.Count; i++)
                    {
                        float agora = amostra[i] != null ? amostra[i].GetCurrentAnimatorStateInfo(0).normalizedTime : -1f;
                        sb.Append(nomes[i]).Append('=').Append((agora - antes[i]).ToString("0.00", Inv)).Append(' ').Append(amostra[i] != null ? amostra[i].cullingMode.ToString() : "?").Append("; ");
                    }
                });
                Log.LogInfo(sb.ToString());
            }
        }

        // Quantos pixels da câmera do jogo mudam em 0,4 s (prova de que o que está NA TELA continua animando).
        static IEnumerable<float> Mexeu(Action<int> resultado)
        {
            Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> a = null, b = null;
            Seguro("captura a", () => a = Captura());
            yield return 0.4f;
            Seguro("captura b", () => b = Captura());
            int mudou = 0;
            if (a != null && b != null && a.Length == b.Length)
                for (int i = 0; i < a.Length; i++)
                {
                    var p = a[i]; var q = b[i];
                    if (Math.Abs(p.r - q.r) > 10 || Math.Abs(p.g - q.g) > 10 || Math.Abs(p.b - q.b) > 10) mudou++;
                }
            resultado(mudou);
        }

        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> Captura()
        {
            const int w = 480, h = 270;
            var cam = Camera.main;
            if (cam == null) return null;
            var rt = new RenderTexture(w, h, 24);
            var antes = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = antes;
            var ativa = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = ativa;
            var px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            return px;
        }

        // Tudo o que aparece no monitor do jogo (todas as câmeras da tela 0, na ordem), em PNG pequeno.
        static void Foto(string nome)
        {
            Seguro("foto " + nome, () =>
            {
                const int w = 960, h = 540;
                Directory.CreateDirectory(PastaFotos);
                var rt = new RenderTexture(w, h, 24);
                var cams = new List<Camera>();
                foreach (var c in Camera.allCameras) if (c != null) cams.Add(c);
                cams.Sort((x, y) => x.depth.CompareTo(y.depth));
                foreach (var cam in cams)
                {
                    if (cam.targetDisplay != 0 || cam.targetTexture != null) continue;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }
                var ativa = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = ativa;
                File.WriteAllBytes(Path.Combine(PastaFotos, nome + ".png"), ImageConversion.EncodeToPNG(tex));
                UnityEngine.Object.Destroy(tex);
                rt.Release();
                Diz("foto " + nome);
            });
        }

        // ---------------- bonecos vestidos que andam (só no Freeplay, sem rede) ----------------

        // A receita do CriCri Treino (treino\src\Nascer.cs): o caminho do próprio jogo para um boneco, e o NetTransform
        // desligado para a física mandar nele.
        static void CriarBonecos(int quantos)
        {
            var cliente = AmongUsClient.Instance;
            var dados = GameData.Instance;
            var eu = PlayerControl.LocalPlayer;
            if (cliente == null || dados == null || eu == null || cliente.PlayerPrefab == null) { Diz("bonecos: falta cliente/dados"); return; }
            Vector2 centro = eu.GetTruePosition();

            // os bonecos que o Freeplay já tem também andam
            var lista = PlayerControl.AllPlayerControls;
            var donos = new List<PlayerControl>();
            for (int i = 0; i < lista.Count; i++)
                if (lista[i] != null && lista[i].Pointer != eu.Pointer) donos.Add(lista[i]);
            int doJogo = donos.Count;

            for (int i = 0; i < quantos; i++)
            {
                var pc = UnityEngine.Object.Instantiate(cliente.PlayerPrefab);
                pc.PlayerId = (byte)dados.GetAvailableId();
                dados.AddDummy(pc);
                cliente.Spawn(pc, -2, InnerNet.SpawnFlags.None);
                float a = i * (6.2832f / quantos);
                Vector2 onde = centro + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.2f;
                pc.transform.position = new Vector3(onde.x, onde.y, onde.y / 1000f);
                if (pc.MyPhysics != null) { pc.MyPhysics.ResetMoveState(true); pc.MyPhysics.ResetAnimState(); }
                pc.gameObject.SetActive(true);
                if (pc.cosmetics != null) pc.cosmetics.gameObject.SetActive(true);
                pc.Visible = true;
                try { pc.SetColor(i % 18); } catch { }
                try { pc.SetName("Boneco " + (i + 1)); } catch { }
                donos.Add(pc);
            }

            // roupa de verdade (chapéu, visor, roupa e pet), para cada jogador ter as animações que um jogador tem
            string[] chapeus = null, visores = null, roupas = null, pets = null;
            Seguro("lista de roupas", () =>
            {
                var hm = DestroyableSingleton<HatManager>.Instance;
                chapeus = Ids(hm.allHats, 40); visores = Ids(hm.allVisors, 20); roupas = Ids(hm.allSkins, 20); pets = Ids(hm.allPets, 12);
            });
            _corpos.Clear();
            for (int i = 0; i < donos.Count; i++)
            {
                var pc = donos[i];
                if (pc.NetTransform != null)
                {
                    try { pc.NetTransform.Halt(); } catch { }
                    pc.NetTransform.enabled = false;
                }
                int cor = i % 18;
                if (chapeus != null && chapeus.Length > 0) try { pc.SetHat(chapeus[(i * 7 + 3) % chapeus.Length], cor); } catch (Exception e) { if (i == 0) Diz("chapeu: " + e.Message); }
                if (visores != null && visores.Length > 0) try { pc.SetVisor(visores[(i * 5 + 1) % visores.Length], cor); } catch (Exception e) { if (i == 0) Diz("visor: " + e.Message); }
                if (roupas != null && roupas.Length > 0) try { pc.SetSkin(roupas[(i * 3 + 2) % roupas.Length], cor); } catch (Exception e) { if (i == 0) Diz("roupa: " + e.Message); }
                if (pets != null && pets.Length > 0) try { pc.SetPet(pets[(i * 5 + 1) % pets.Length]); } catch (Exception e) { if (i == 0) Diz("pet: " + e.Message); }
                var corpo = pc.MyPhysics != null ? pc.MyPhysics.body : null;
                if (corpo != null) _corpos.Add(corpo);
            }
            Diz("bonecos: " + doJogo + " do Freeplay + " + quantos + " criados = " + donos.Count + " andando (" + _corpos.Count + " corpos)");
        }

        static string[] Ids<T>(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<T> itens, int max) where T : CosmeticData
        {
            var ids = new List<string>();
            for (int i = 0; i < itens.Length && ids.Count < max; i++)
            {
                var c = itens[i];
                if (c == null) continue;
                string id = c.ProdId;
                if (!string.IsNullOrEmpty(id) && id.IndexOf("empty", StringComparison.OrdinalIgnoreCase) < 0 && id.IndexOf("EmptyPet", StringComparison.OrdinalIgnoreCase) < 0) ids.Add(id);
            }
            return ids.ToArray();
        }

        // ---------------- o raio-x: o que existe na cena ----------------

        static void RaioX()
        {
            void L(string s) => Log.LogInfo("[raiox] " + s);
            var nave = ShipStatus.Instance;

            Seguro("geral", () =>
            {
                string nivel = "?";
                try { nivel = QualitySettings.names[QualitySettings.GetQualityLevel()]; } catch { }
                L("geral: qualidade " + nivel + ", vsync " + QualitySettings.vSyncCount + ", fps alvo " + Application.targetFrameRate + ", AA " + QualitySettings.antiAliasing
                    + ", mipmap limite " + QualitySettings.globalTextureMipmapLimit + ", tela " + Screen.width + "x" + Screen.height
                    + ", fixedDeltaTime " + Time.fixedDeltaTime.ToString("0.0000", Inv) + ", maximumDeltaTime " + Time.maximumDeltaTime.ToString("0.000", Inv)
                    + ", Physics2D autoSync " + Physics2D.autoSyncTransforms + " modo " + Physics2D.simulationMode + " iteracoes " + Physics2D.velocityIterations
                    + ", GC incremental " + UnityEngine.Scripting.GarbageCollector.isIncremental
                    + ", memoria " + SystemInfo.systemMemorySize + " MB, cpu " + SystemInfo.processorCount + "x " + SystemInfo.processorType + ", gpu " + SystemInfo.graphicsDeviceName);
            });

            Seguro("cameras", () =>
            {
                foreach (var c in Camera.allCameras)
                {
                    if (c == null) continue;
                    var rt = c.targetTexture;
                    L("camera " + c.name + ": depth " + c.depth.ToString("0.#", Inv) + ", tela " + c.targetDisplay + ", orto " + c.orthographic + " " + c.orthographicSize.ToString("0.00", Inv)
                        + ", mascara " + c.cullingMask.ToString("X") + ", HDR " + c.allowHDR + ", MSAA " + c.allowMSAA + ", limpa " + c.clearFlags
                        + ", alvo " + (rt != null ? rt.width + "x" + rt.height + " " + rt.format : "a tela") + ", main " + (Camera.main != null && c.Pointer == Camera.main.Pointer));
                }
            });

            Seguro("animators do mapa", () =>
            {
                var anims = nave.GetComponentsInChildren<Animator>(true);
                var porSala = new Dictionary<string, int>();
                var porModo = new Dictionary<string, int>();
                int ligados = 0, comSpriteAnim = 0, enfeites = 0, emLaco = 0, naTela = 0, semDesenho = 0;
                var exemplos = new StringBuilder();
                foreach (var a in anims)
                {
                    if (a == null) continue;
                    string modo = a.cullingMode.ToString();
                    porModo[modo] = porModo.TryGetValue(modo, out int m0) ? m0 + 1 : 1;
                    // o filho direto da nave em que a peça mora (a sala)
                    Transform t = a.transform;
                    while (t.parent != null && t.parent.Pointer != nave.transform.Pointer) t = t.parent;
                    string sala = t.name;
                    porSala[sala] = porSala.TryGetValue(sala, out int s0) ? s0 + 1 : 1;
                    if (!a.isActiveAndEnabled) continue;
                    ligados++;
                    bool soEnfeite = true, temSa = false;
                    string scripts = "";
                    foreach (var s in a.gameObject.GetComponents<MonoBehaviour>())
                    {
                        if (s == null) continue;
                        if (s.TryCast<PowerTools.SpriteAnim>() != null) { temSa = true; continue; }
                        soEnfeite = false;
                        scripts += s.GetIl2CppType().Name + " ";
                    }
                    if (temSa) comSpriteAnim++;
                    if (soEnfeite) enfeites++;
                    bool laco = a.runtimeAnimatorController != null && a.GetCurrentAnimatorStateInfo(0).loop;
                    if (laco) emLaco++;
                    bool temDesenho = a.GetComponentsInChildren<Renderer>(true).Length > 0;
                    if (!temDesenho) semDesenho++;
                    if (NaTela(a)) naTela++;
                    if (exemplos.Length < 2600)
                        exemplos.Append(sala).Append('/').Append(a.name).Append(soEnfeite ? " [enfeite" : " [" + scripts.Trim()).Append(laco ? ", laco]" : ", termina]").Append("; ");
                }
                L("mapa " + nave.name + ": " + anims.Length + " animators, " + ligados + " ligados, " + comSpriteAnim + " com SpriteAnim, " + enfeites + " so enfeite, " + emLaco
                    + " em laco, " + semDesenho + " sem desenho nenhum, " + naTela + " com desenho na tela agora, " + (ligados - naTela) + " fora da tela");
                L("mapa, modo que o JOGO deixou: " + Junta(porModo));
                L("mapa, animators por sala: " + Junta(porSala));
                L("mapa, exemplos: " + exemplos);
            });

            Seguro("scripts do mapa", () =>
            {
                var porTipo = new Dictionary<string, int>();
                int total = 0;
                foreach (var s in nave.GetComponentsInChildren<MonoBehaviour>(false))
                {
                    if (s == null || !s.isActiveAndEnabled) continue;
                    total++;
                    string n = s.GetIl2CppType().Name;
                    porTipo[n] = porTipo.TryGetValue(n, out int c0) ? c0 + 1 : 1;
                }
                L("mapa, " + total + " scripts ligados, por tipo: " + Junta(porTipo, 45));
            });

            Seguro("desenhos do mapa", () =>
            {
                int total = 0, ligados = 0, naTela = 0;
                foreach (var r in nave.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    total++;
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    ligados++;
                    if (r.isVisible) naTela++;
                }
                L("mapa, desenhos (Renderer): " + total + ", " + ligados + " ligados, " + naTela + " na tela");
            });

            Seguro("particulas", () =>
            {
                var todas = nave.GetComponentsInChildren<ParticleSystem>(true);
                int laco = 0, tocando = 0, vivas = 0, autoCorte = 0, naTela = 0;
                var nomes = new StringBuilder();
                foreach (var ps in todas)
                {
                    if (ps == null) continue;
                    var principal = ps.main;
                    if (principal.loop) laco++;
                    if (ps.isPlaying) tocando++;
                    vivas += ps.particleCount;
                    if (ps.automaticCullingEnabled) autoCorte++;
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r != null && r.isVisible) naTela++;
                    if (nomes.Length < 900) nomes.Append(ps.name).Append('(').Append(ps.particleCount).Append('/').Append(principal.maxParticles).Append(principal.loop ? ",laco" : "").Append(ps.isPlaying ? ",toca" : "").Append(ps.automaticCullingEnabled ? ",corteAuto" : "").Append("); ");
                }
                L("mapa, particulas: " + todas.Length + " sistemas, " + laco + " em laco, " + tocando + " tocando, " + vivas + " particulas vivas, " + autoCorte + " com corte automatico da Unity, " + naTela + " na tela: " + nomes);
            });

            Seguro("jogadores", () =>
            {
                var lista = PlayerControl.AllPlayerControls;
                for (int i = 0; i < lista.Count && i < 3; i++)
                {
                    var pc = lista[i];
                    if (pc == null) continue;
                    var sb = new StringBuilder();
                    foreach (var a in pc.GetComponentsInChildren<Animator>(true))
                        sb.Append(a.name).Append(a.isActiveAndEnabled ? "" : "(desl)").Append(' ').Append(a.cullingMode).Append("; ");
                    var pet = pc.cosmetics != null ? pc.cosmetics.currentPet : null;
                    if (pet != null) foreach (var a in pet.GetComponentsInChildren<Animator>(true)) sb.Append("PET ").Append(a.name).Append(' ').Append(a.cullingMode).Append("; ");
                    var corpo = pc.MyPhysics != null ? pc.MyPhysics.body : null;
                    L("jogador " + i + " " + pc.name + (pc.AmOwner ? " (eu)" : "") + ": " + pc.GetComponentsInChildren<Renderer>(true).Length + " desenhos, "
                        + pc.GetComponentsInChildren<MonoBehaviour>(false).Length + " scripts, animators: " + sb
                        + (corpo != null ? " corpo interp " + corpo.interpolation + " dorme " + corpo.IsSleeping() : ""));
                }
                L("jogadores na cena: " + lista.Count);
            });

            Seguro("cena inteira", () =>
            {
                var anims = UnityEngine.Object.FindObjectsOfType<Animator>();
                int naNave = 0;
                foreach (var a in anims) if (a != null && a.transform.IsChildOf(nave.transform)) naNave++;
                var corpos = UnityEngine.Object.FindObjectsOfType<Rigidbody2D>();
                int acordados = 0;
                foreach (var c in corpos) if (c != null && c.IsAwake()) acordados++;
                var estrelas = UnityEngine.Object.FindObjectsOfType<StarGen>();
                string est = "";
                foreach (var e in estrelas) if (e != null) est += e.NumStars + (e.isActiveAndEnabled ? "" : "(desl)") + " ";
                L("cena inteira: " + anims.Length + " animators ligados (" + naNave + " na nave, " + (anims.Length - naNave) + " fora dela: jogadores, HUD, pets), "
                    + corpos.Length + " corpos de fisica (" + acordados + " acordados), " + UnityEngine.Object.FindObjectsOfType<TMPro.TextMeshPro>().Length + " textos TMP, "
                    + UnityEngine.Object.FindObjectsOfType<SpriteRenderer>().Length + " SpriteRenderer, StarGen: " + estrelas.Length + " [" + est.Trim() + " estrelas]");
            });
        }

        static string Junta(Dictionary<string, int> d, int max = 60)
        {
            var pares = new List<KeyValuePair<string, int>>(d);
            pares.Sort((a, b) => b.Value.CompareTo(a.Value));
            var sb = new StringBuilder();
            for (int i = 0; i < pares.Count && i < max; i++) sb.Append(pares[i].Key).Append(' ').Append(pares[i].Value).Append(", ");
            if (pares.Count > max) sb.Append("... +").Append(pares.Count - max).Append(" tipos");
            return sb.ToString();
        }
    }
}
#endif
