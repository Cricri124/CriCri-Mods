using System;
using System.Collections.Generic;
using AmongUs.Data;
using TMPro;
using UnityEngine;

namespace CriCriMods
{
    // The mod's options INSIDE the game's Settings (main menu and in a match), the way the CriCri asked:
    // - "Gráficos": the resolution as a slider like the PC's (name on the left, size under the bar), then two
    //   columns of on/off buttons (optimization, FPS counter, FPS limit, flash mode, smooth movement, textures),
    //   then the game's own "Imagem tremida".
    // - "Geral": the colorblind name colors right beside the game's colorblind button (only with it on), and
    //   streamer mode (08/11: "não faz sentido ter o modo streamer em gráficos"; on PC it lives in Geral) in the
    //   free band of the tab.
    //
    // First phone test (08/11): a NEW tab doesn't fit — in a match the menu shows only "Geral" and "Gráficos" (Data
    // and Help are hidden) and the tab copied from Help stayed hidden; the Graphics tab holds a single button
    // ("Imagem tremida") in the middle. So the options are copies of the game's own buttons and slider. The game
    // positions its buttons only after the menu opens (at Start they all read (0,0,0)), so the layout is measured
    // while the tab is on screen and redone every half second.
    internal static class AbaCriCri
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        sealed class Opcao
        {
            public Func<string> Rotulo;
            public Func<bool> Ligada;      // green border when true (like the game's "Sim")
            public Action Clique;
            public Func<bool> Visivel;     // null = always
            public GameObject Objeto;
            public TextMeshPro Texto;
            public SpriteRenderer Fundo;
        }

        // A stop on the resolution slider: Valor is what goes in Opcoes.Resolucao (0 = the game's own size).
        sealed class Passo
        {
            public int Valor, Largura, Altura;
            public string Texto;
        }

        sealed class Barra
        {
            public GameObject Objeto;
            public SlideBar Slide;
            public TextMeshPro Titulo, Valor;
            public List<Passo> Passos;
            public int Indice = -1, Aplicado = -1;
            public float MudouEm;
            public bool Centralizou;
        }

        sealed class Montagem
        {
            public OptionsMenuBehaviour Menu;
            public GameObject Conteudo;          // the game's GraphicsTab content
            public Transform Ancora;             // the game's button in that tab ("Imagem tremida")
            public readonly List<Opcao> Opcoes = new List<Opcao>();
            public Barra Resolucao;
            public bool Posicionou;
            // General tab: "nome na cor" right beside the game's colorblind button, in the right column (where the
            // friend-invites button is), only while the game's colorblind mode is on (the CriCri's layout).
            public Opcao Daltonico, Streamer;
            public Transform BotaoDaltonico, ColunaDireita, Geral;
            public float VaoLogado = -1f;
            // (11/11) No PC as opções ficam numa aba própria (MontarPc): a aba, a aba do jogo que serviu de molde e
            // o lugar dela na lista do jogo.
            public bool Pc;
            public TabGroup Aba, MoldeDaAba;
            public int IndiceDaAba = -1;
            public bool PosicionouPc;
            public string RotuloDaAba;
        }

        static readonly Dictionary<IntPtr, Montagem> _montagens = new Dictionary<IntPtr, Montagem>();
        static readonly HashSet<IntPtr> _falhas = new HashSet<IntPtr>();
        static bool _logouEstrutura;
        static bool _mais;   // "Mais opções" aberto
        static readonly Color Verde = new Color(0.1f, 1f, 0.1f, 1f);
        static readonly Color Apagado = new Color(1f, 1f, 1f, 0.45f);

        // The game's OptionsMenuBehaviour.Start (Harmony postfix): once it is seen firing, the half-second search for
        // open menus stops (08/11: FindObjectsOfType walks every object of the ship twice a second, for nothing).
        static bool _startFunciona;
        public static void PeloStart(OptionsMenuBehaviour menu)
        {
            if (!_startFunciona)
            {
                _startFunciona = true;
                Log.LogInfo("[aba] o menu de opcoes avisa quando abre (Start); paro a busca de meio em meio segundo");
            }
            if (Plataforma.Pc) Pendente(menu);   // a aba do PC espera a caixa parar (MontarPendentesPc)
            else Construir(menu);
        }

        // PC: os menus que ainda esperam a aba.
        static readonly List<OptionsMenuBehaviour> _pendentesPc = new List<OptionsMenuBehaviour>();
        static readonly Dictionary<IntPtr, float> _larguraPc = new Dictionary<IntPtr, float>();

        static void Pendente(OptionsMenuBehaviour menu)
        {
            if (menu == null) return;
            IntPtr p = menu.Pointer;
            if (_montagens.ContainsKey(p) || _falhas.Contains(p)) return;
            for (int i = 0; i < _pendentesPc.Count; i++)
                if (_pendentesPc[i] != null && _pendentesPc[i].Pointer == p) return;
            _pendentesPc.Add(menu);
        }

        /* A aba do PC só é montada com a caixa das Definições aberta e do mesmo tamanho em dois tiques seguidos (meio
           segundo). Dois motivos: no meio da animação de abrir as medidas ainda não valem; e outro mod que também põe
           aba ali tem de ir PRIMEIRO (o QoL do dono monta a dele assim que a caixa para por dois quadros). Montando
           antes, a nossa aba, a última da lista, virava o molde da dele: a dele saía com um quarto do tamanho e fora
           da caixa (visto na rodada 15). */
        static void MontarPendentesPc()
        {
            for (int i = _pendentesPc.Count - 1; i >= 0; i--)
            {
                var menu = _pendentesPc[i];
                if (menu == null) { _pendentesPc.RemoveAt(i); continue; }
                if (!menu.isActiveAndEnabled || menu.Background == null) continue;
                IntPtr p = menu.Pointer;
                float w = menu.Background.bounds.size.x;
                _larguraPc.TryGetValue(p, out float antes);
                _larguraPc[p] = w;
                if (w < 4f || Math.Abs(w - antes) > 0.005f) continue;
                _pendentesPc.RemoveAt(i);
                _larguraPc.Remove(p);
                Construir(menu);
            }
        }

        // Every 0.5 s from Otimizacao.Update.
        public static void Procurar()
        {
            if (!_startFunciona)
            {
                var menus = UnityEngine.Object.FindObjectsOfType<OptionsMenuBehaviour>();
                for (int i = 0; i < menus.Length; i++)
                    if (menus[i] != null && menus[i].isActiveAndEnabled) { if (Plataforma.Pc) Pendente(menus[i]); else Construir(menus[i]); }
            }
            if (Plataforma.Pc && _pendentesPc.Count > 0) MontarPendentesPc();

            var mortos = new List<IntPtr>();
            foreach (var par in _montagens)
            {
                var m = par.Value;
                if (m.Menu == null || m.Conteudo == null) { mortos.Add(par.Key); continue; }
                if (m.Pc)
                {
                    // PC: a aba própria. Nada do mod entra em Gráficos nem em Geral.
                    PosicionarAbaPc(m);
                    if (!m.Conteudo.activeInHierarchy) continue;
                    PosicionarPc(m);
                    Atualizar(m);
                    continue;
                }
                PosicionarDaltonico(m);
                PosicionarStreamer(m);
                if (!m.Conteudo.activeInHierarchy) continue;
                Posicionar(m);
                Atualizar(m);
                AtualizarBarra(m);
            }
            foreach (var p in mortos) _montagens.Remove(p);
        }

        public static void Construir(OptionsMenuBehaviour menu)
        {
            if (menu == null) return;
            IntPtr chave = menu.Pointer;
            if (_montagens.ContainsKey(chave) || _falhas.Contains(chave)) return;
            try
            {
                var m = Montar(menu);
                if (m != null) _montagens[chave] = m;
                else _falhas.Add(chave);
            }
            catch (Exception e)
            {
                _falhas.Add(chave);
                Log.LogWarning("[aba] nao pus as opcoes em Graficos: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            }
        }

        static Montagem Montar(OptionsMenuBehaviour menu)
        {
            var abas = menu.Tabs;
            if (abas == null || abas.Length == 0) { Log.LogWarning("[aba] o menu de opcoes nao tem abas"); return null; }
            LogEstrutura(menu);
            // (11/11) No PC a aba Gráficos e a Geral do jogo são outras: as opções ganham uma aba própria
            if (Plataforma.Pc) return MontarPc(menu);

            TabGroup graficos = null;
            Transform geral = null;
            for (int i = 0; i < abas.Length; i++)
            {
                if (abas[i] == null || abas[i].Content == null) continue;
                if (graficos == null && abas[i].name.IndexOf("Graphic", StringComparison.OrdinalIgnoreCase) >= 0) graficos = abas[i];
                if (geral == null && menu.ColorBlindButton != null && menu.ColorBlindButton.transform.IsChildOf(abas[i].Content.transform))
                    geral = abas[i].Content.transform;
            }
            if (graficos == null) { Log.LogWarning("[aba] nao achei a aba Graficos"); return null; }

            // The model: a two-column-size button from the General tab (the Graphics one is wider).
            ToggleButtonBehaviour modelo = null;
            foreach (var b in new[] { menu.CensorChatButton, menu.EnableFriendInvitesButton, menu.ColorBlindButton })
                if (b != null) { modelo = b; break; }
            if (modelo == null) { Log.LogWarning("[aba] nenhum botao liga/desliga do jogo pra copiar"); return null; }

            // The game's own button already in the Graphics tab (Screenshake): our grid goes around it.
            // (picked when the tab is on screen: only then is it known which of its buttons the phone shows)
            var m = new Montagem { Menu = menu, Conteudo = graficos.Content, Geral = geral };
            CriarOpcoes(m);
            for (int i = 0; i < m.Opcoes.Count; i++)
                Copiar(m, m.Opcoes[i], modelo, graficos.Content.transform, "CriCriOpcao" + i);

            // The resolution slider: a copy of the game's sound-effects slider.
            SlideBar barraModelo = menu.SoundSlider ?? menu.MusicSlider ?? menu.JoystickSizeSlider;
            if (barraModelo != null)
            {
                try { m.Resolucao = CriarBarra(barraModelo, graficos.Content.transform); }
                catch (Exception e) { Log.LogWarning("[aba] barra de resolucao: " + e.GetType().Name + ": " + e.Message); }
            }
            if (m.Resolucao == null) Log.LogWarning("[aba] sem barra de resolucao (nenhum controle deslizante pra copiar)");

            // General tab: the colorblind colors option beside the game's colorblind button, and streamer mode.
            if (menu.ColorBlindButton != null)
            {
                var o = Opcoes.Atual;
                m.Daltonico = new Opcao
                {
                    Rotulo = () => "Nome na cor do jogador: " + (o.DaltonicoCorNoNome ? "Sim" : "Não"),
                    Ligada = () => o.DaltonicoCorNoNome,
                    Clique = () => { o.DaltonicoCorNoNome = !o.DaltonicoCorNoNome; Salvar(); },
                    Visivel = () => DataManager.IsSettingsLoaded && DataManager.Settings.Accessibility.ColorBlindMode,
                };
                m.BotaoDaltonico = menu.ColorBlindButton.transform;
                m.ColunaDireita = menu.EnableFriendInvitesButton != null ? menu.EnableFriendInvitesButton.transform : null;
                Copiar(m, m.Daltonico, menu.ColorBlindButton, m.BotaoDaltonico.parent, "CriCriDaltonico");

                // the game has streamer mode on phones too (hides the room code), just no button for it
                m.Streamer = new Opcao
                {
                    Rotulo = () => "Modo streamer: " + (Streamer() ? "Sim" : "Não"),
                    Ligada = Streamer,
                    Clique = () =>
                    {
                        if (!DataManager.IsSettingsLoaded) return;
                        bool novo = !DataManager.Settings.Gameplay.StreamerMode;
                        DataManager.Settings.Gameplay.StreamerMode = novo;
                        try { if (m.Menu.StreamerModeButton != null) m.Menu.StreamerModeButton.UpdateText(novo); } catch { }
                    },
                };
                Copiar(m, m.Streamer, menu.ColorBlindButton, m.BotaoDaltonico.parent, "CriCriStreamer");
            }
            Log.LogInfo("[aba] " + m.Opcoes.Count + " opcoes em Graficos (modelo " + modelo.name + ")"
                + (m.Resolucao != null ? " + barra de resolucao (" + m.Resolucao.Passos.Count + " tamanhos, copia de " + barraModelo.name + ")" : "")
                + (m.Daltonico != null ? " + cor no nome e modo streamer em Geral" : ""));
            return m;
        }

        // A copy of one of the game's on/off buttons, without the game's behaviour, calling ours.
        static void Copiar(Montagem m, Opcao op, ToggleButtonBehaviour modelo, Transform pai, string nome)
        {
            var go = UnityEngine.Object.Instantiate(modelo.gameObject, pai);
            go.name = nome;
            go.transform.localScale = Div(modelo.transform.lossyScale, pai.lossyScale);
            go.SetActive(false);   // shown once laid out
            var tbb = go.GetComponent<ToggleButtonBehaviour>();
            op.Texto = tbb != null ? tbb.Text : go.GetComponentInChildren<TextMeshPro>(true);
            op.Fundo = tbb != null ? tbb.Background : go.GetComponentInChildren<SpriteRenderer>(true);
            if (tbb != null) UnityEngine.Object.DestroyImmediate(tbb);   // its Start/ResetText would rewrite our text
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) UnityEngine.Object.DestroyImmediate(tr);
            // a positioning component copied from the model would move the copy back onto the model
            foreach (var ap in go.GetComponentsInChildren<AspectPosition>(true)) UnityEngine.Object.DestroyImmediate(ap);

            var pb = go.GetComponent<PassiveButton>() ?? go.GetComponentInChildren<PassiveButton>(true);
            if (pb != null)
            {
                pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                var esta = op;
                pb.OnClick.AddListener((Action)(() => Clicar(m, esta)));
            }
            op.Objeto = go;
        }

        // ---------------- the resolution slider ----------------

        static Barra CriarBarra(SlideBar modelo, Transform pai)
        {
            var go = UnityEngine.Object.Instantiate(modelo.gameObject, pai);
            go.name = "CriCriResolucao";
            go.SetActive(false);
            var s = go.GetComponent<SlideBar>();
            // The copy must hold its own bar, dot and title: a reference to the model's parts would move the SOUND
            // slider (or rename it) instead of ours.
            if (s == null || s.Dot == null || s.Bar == null || !s.Dot.transform.IsChildOf(go.transform) || !s.Bar.transform.IsChildOf(go.transform))
            {
                Log.LogWarning("[aba] a barra copiada nao tem as proprias pecas; sem barra de resolucao");
                UnityEngine.Object.Destroy(go);
                return null;
            }
            go.transform.localScale = Div(modelo.transform.lossyScale, pai.lossyScale);
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) UnityEngine.Object.DestroyImmediate(tr);
            foreach (var ap in go.GetComponentsInChildren<AspectPosition>(true)) UnityEngine.Object.DestroyImmediate(ap);

            var b = new Barra { Objeto = go, Slide = s, Passos = PassosDaResolucao() };
            // the game's listener (UpdateSfxVolume on the menu) would change the sound volume
            s.OnValueChange = new UnityEngine.Events.UnityEvent();
            s.OnValueChange.AddListener((Action)(() => BarraMexeu(b)));

            TextMeshPro titulo = s.Title != null && s.Title.transform.IsChildOf(go.transform) ? s.Title.GetComponent<TextMeshPro>() : null;
            if (titulo == null) titulo = go.GetComponentInChildren<TextMeshPro>(true);
            if (titulo != null)
            {
                titulo.text = "Resolução";
                b.Titulo = titulo;
                // centered over the bar: the slider's own place, left of the bar, stuck out of the window on the phone
                titulo.alignment = TextAlignmentOptions.Center;
                var rt = titulo.rectTransform;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(Math.Max(rt.sizeDelta.x, 4f), rt.sizeDelta.y);
                // the size under the bar, like the PC's "1920x1080"
                var copia = UnityEngine.Object.Instantiate(titulo.gameObject, go.transform);
                copia.name = "CriCriResolucaoValor";
                b.Valor = copia.GetComponent<TextMeshPro>();
            }
            return b;
        }

        // 50% to 100% of the screen in steps of 10, plus the size the game picks by itself ("padrão").
        static List<Passo> PassosDaResolucao()
        {
            var lista = new List<Passo>();
            int jogoL = 0, jogoA = 0;
            bool temDoJogo = Otimizacao.TamanhoDoJogo(out int l0, out int a0);
            if (temDoJogo) { jogoL = Math.Max(l0, a0); jogoA = Math.Min(l0, a0); }
            foreach (int p in new[] { 50, 60, 70, 80, 90, 100 })
            {
                Otimizacao.Medidas(p, out int l, out int a);
                if (temDoJogo && Math.Abs(a - jogoA) <= 4) continue;   // the game's own size stands for it
                lista.Add(new Passo { Valor = p, Largura = l, Altura = a, Texto = l + "x" + a + (p == Opcoes.Recomendada() ? " (recomendada)" : "") });
            }
            if (temDoJogo) lista.Add(new Passo { Valor = 0, Largura = jogoL, Altura = jogoA, Texto = jogoL + "x" + jogoA + " (padrão)" });
            else lista.Insert(0, new Passo { Valor = 0, Texto = "padrão do jogo" });
            lista.Sort((x, y) => x.Altura.CompareTo(y.Altura));
            return lista;
        }

        static int IndiceAtual(Barra b)
        {
            int atual = Opcoes.Atual.Resolucao;
            for (int i = 0; i < b.Passos.Count; i++) if (b.Passos[i].Valor == atual) return i;
            // a value from an older version (75%): the nearest stop
            Otimizacao.Medidas(atual, out _, out int altura);
            int melhor = 0;
            for (int i = 1; i < b.Passos.Count; i++)
                if (Math.Abs(b.Passos[i].Altura - altura) < Math.Abs(b.Passos[melhor].Altura - altura)) melhor = i;
            return melhor;
        }

        static float ValorDoIndice(Barra b, int i) => b.Passos.Count > 1 ? i / (float)(b.Passos.Count - 1) : 0f;

        // While dragging: only the text follows. The size is applied once the finger rests (AtualizarBarra), since
        // every Screen.SetResolution recreates the phone's drawing surface.
        static void BarraMexeu(Barra b)
        {
            try
            {
                int n = b.Passos.Count;
                int i = Mathf.Clamp(Mathf.RoundToInt(b.Slide.Value * (n - 1)), 0, n - 1);
                if (i == b.Indice) return;
                b.Indice = i;
                b.MudouEm = Time.unscaledTime;
                EscreverValor(b);
            }
            catch (Exception e) { Log.LogWarning("[aba] barra: " + e.GetType().Name + ": " + e.Message); }
        }

        // Phone tests 08/11: on this Unity the size takes only when the game starts (requests made later are ignored,
        // and remaking the drawing surface froze the picture), so a size that isn't on screen yet says so.
        static void EscreverValor(Barra b)
        {
            if (b.Valor == null || b.Indice < 0) return;
            var p = b.Passos[b.Indice];
            string texto = p.Texto;
            if (Opcoes.Atual.Ligada && p.Largura > 0 && (Screen.width != p.Largura || Screen.height != p.Altura))
                texto += "  <size=70%>(vale ao reabrir o jogo)</size>";
            if (b.Valor.text != texto) b.Valor.text = texto;
            Color cor = Opcoes.Atual.Ligada ? Color.white : Apagado;   // optimization off = the game's size anyway
            if (b.Valor.color != cor) b.Valor.color = cor;
        }

        static void AtualizarBarra(Montagem m)
        {
            var b = m.Resolucao;
            if (b == null || b.Objeto == null) return;
            if (b.Indice < 0)
            {
                b.Indice = b.Aplicado = IndiceAtual(b);
                b.Slide.SetValue(ValorDoIndice(b, b.Indice));
            }
            EscreverValor(b);
            if (b.Indice != b.Aplicado && Time.unscaledTime - b.MudouEm >= 0.6f)
            {
                b.Aplicado = b.Indice;
                b.Slide.SetValue(ValorDoIndice(b, b.Indice));   // snap the dot to the stop (no callback)
                var p = b.Passos[b.Indice];
                Opcoes.Atual.Resolucao = p.Valor;
                Log.LogInfo("[aba] resolucao na barra: " + p.Texto + " (" + (p.Valor > 0 ? p.Valor + "%" : "do jogo") + ")");
                Salvar();
            }
        }

        // ---------------- General tab ----------------

        // While the General tab is on screen: show the colors option beside the colorblind button (same row,
        // right column) only with colorblind mode on, and keep its text/border current.
        static void PosicionarDaltonico(Montagem m)
        {
            var op = m.Daltonico;
            if (op == null || op.Objeto == null || m.BotaoDaltonico == null || !m.BotaoDaltonico.gameObject.activeInHierarchy) return;
            bool visivel = op.Visivel();
            if (op.Objeto.activeSelf != visivel) op.Objeto.SetActive(visivel);
            if (!visivel) return;
            var pai = op.Objeto.transform.parent;
            Vector3 linha = pai.InverseTransformPoint(m.BotaoDaltonico.position);
            float x;
            if (m.ColunaDireita != null && m.ColunaDireita.gameObject.activeInHierarchy)
                x = pai.InverseTransformPoint(m.ColunaDireita.position).x;
            else
            {
                var f = op.Fundo;
                float largura = f != null && f.bounds.size.x > 0.01f ? f.bounds.size.x / Math.Max(0.0001f, Math.Abs(pai.lossyScale.x)) : 2.1f;
                x = linha.x + largura * 1.08f;
            }
            var alvo = new Vector3(x, linha.y, linha.z);
            if ((op.Objeto.transform.localPosition - alvo).sqrMagnitude > 0.0001f) op.Objeto.transform.localPosition = alvo;
            Pintar(op);
        }

        // Streamer mode goes in the tallest empty band of the General tab above the first row of on/off buttons
        // (on the phone, between "Dimensões" and "Som"), centered between the two columns. Measured from what is on
        // screen, so it follows the layout (joystick or touch controls, menu or match).
        static void PosicionarStreamer(Montagem m)
        {
            var op = m.Streamer;
            if (op == null || op.Objeto == null || m.BotaoDaltonico == null || !m.BotaoDaltonico.gameObject.activeInHierarchy) return;
            var modelo = m.Menu.ColorBlindButton != null ? m.Menu.ColorBlindButton.Background : null;
            if (modelo == null || modelo.bounds.size.y < 0.001f) return;
            float altura = modelo.bounds.size.y;
            Transform raiz = m.Geral != null ? m.Geral : m.BotaoDaltonico.parent;
            // the first row of on/off buttons (censor chat / friend invites)
            float linhaDeBotoes = float.MinValue;
            foreach (var b in new[] { m.Menu.CensorChatButton, m.Menu.EnableFriendInvitesButton, m.Menu.ColorBlindButton })
                if (b != null && b.isActiveAndEnabled && b.Background != null) linhaDeBotoes = Math.Max(linhaDeBotoes, b.Background.bounds.max.y);
            if (linhaDeBotoes == float.MinValue) return;

            var faixas = new List<Vector2>();   // (top, bottom) in world units
            foreach (var r in raiz.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                var t = r.transform;
                if (t.IsChildOf(op.Objeto.transform) || (m.Daltonico != null && m.Daltonico.Objeto != null && t.IsChildOf(m.Daltonico.Objeto.transform))) continue;
                var bb = r.bounds;
                if (bb.size.y < 0.001f || bb.size.y > altura * 4f) continue;   // empty texts and big backgrounds
                if (bb.min.y < linhaDeBotoes - 0.001f) continue;               // the button rows and below
                faixas.Add(new Vector2(bb.max.y, bb.min.y));
            }
            faixas.Add(new Vector2(linhaDeBotoes, linhaDeBotoes));
            faixas.Sort((a, b) => b.x.CompareTo(a.x));
            float vao = 0f, meio = 0f, cursor = float.NaN;
            foreach (var f in faixas)
            {
                if (!float.IsNaN(cursor) && cursor - f.x > vao) { vao = cursor - f.x; meio = (cursor + f.x) * 0.5f; }
                cursor = float.IsNaN(cursor) ? f.y : Math.Min(cursor, f.y);
            }
            if (vao <= 0f) return;

            float cx = m.BotaoDaltonico.position.x;
            if (m.ColunaDireita != null && m.ColunaDireita.gameObject.activeInHierarchy) cx = (cx + m.ColunaDireita.position.x) * 0.5f;
            var alvo = new Vector3(cx, meio, m.BotaoDaltonico.position.z);
            if (!op.Objeto.activeSelf) op.Objeto.SetActive(true);
            if ((op.Objeto.transform.position - alvo).sqrMagnitude > 0.00001f) op.Objeto.transform.position = alvo;
            if (Math.Abs(m.VaoLogado - vao) > 0.01f)
            {
                m.VaoLogado = vao;
                Log.LogInfo("[aba] modo streamer em Geral: vao de " + vao.ToString("0.00") + " (botao " + altura.ToString("0.00") + ")"
                    + (vao < altura * 1.05f ? " APERTADO" : ""));
            }
            Pintar(op);
        }

        static void Pintar(Opcao op)
        {
            string texto = op.Rotulo();
            if (op.Texto != null && op.Texto.text != texto) op.Texto.text = texto;
            if (op.Fundo != null && op.Ligada != null)
            {
                Color cor = op.Ligada() ? Verde : Color.white;
                if (op.Fundo.color != cor) op.Fundo.color = cor;
            }
        }

        // ---------------- Graphics tab layout ----------------

        // The slider on top (like the PC), then two columns of the model's width right above the game's button.
        static void Posicionar(Montagem m)
        {
            var conteudo = m.Conteudo.transform;
            if (m.Opcoes.Count == 0 || m.Opcoes[0].Objeto == null) return;
            // visibility first: a renderer only has a size once its object is on screen
            int visiveis = 0;
            foreach (var op in m.Opcoes)
            {
                bool visivel = op.Visivel == null || op.Visivel();
                if (op.Objeto != null && op.Objeto.activeSelf != visivel) op.Objeto.SetActive(visivel);
                if (visivel) visiveis++;
            }
            var fundo = m.Opcoes[0].Fundo;
            Vector3 tamanho = fundo != null ? fundo.bounds.size : Vector3.zero;
            float ex = Math.Abs(conteudo.lossyScale.x) > 0.0001f ? Math.Abs(conteudo.lossyScale.x) : 1f;
            float ey = Math.Abs(conteudo.lossyScale.y) > 0.0001f ? Math.Abs(conteudo.lossyScale.y) : 1f;
            // fallback = what the first phone test measured on screen (a General button is ~2.1 x 0.33 units)
            float largura = tamanho.x > 0.01f ? tamanho.x / ex : 2.1f;
            float altura = tamanho.y > 0.01f ? tamanho.y / ey : 0.33f;
            // The game's button that is VISIBLE in this tab ("Imagem tremida"). Phone test 08/11: the first one found
            // was FullScreenButton (PC only: active itself, hidden on the phone) and the grid went over the tabs.
            if (m.Ancora == null || !m.Ancora.gameObject.activeInHierarchy)
            {
                m.Ancora = null;
                foreach (var t in m.Conteudo.GetComponentsInChildren<ToggleButtonBehaviour>(false))
                    if (t != null && t.isActiveAndEnabled) { m.Ancora = t.transform; break; }
                if (m.Ancora != null) Log.LogInfo("[aba] referencia em Graficos: " + m.Ancora.name);
            }
            Vector3 centro = m.Ancora != null ? conteudo.InverseTransformPoint(m.Ancora.position) : Vector3.zero;
            float passoY = altura * 1.6f;
            float meiaColuna = largura * 0.5f + largura * 0.04f;
            // Six above the game's button (three rows; a fourth would reach the tabs), the rest below it; a lone one in
            // its row is centered. The resolution slider goes under all of them.
            const int Acima = 6;
            int filas = (Math.Min(visiveis, Acima) + 1) / 2;
            int abaixo = Math.Max(0, visiveis - Acima);

            int k = 0;
            for (int i = 0; i < m.Opcoes.Count; i++)
            {
                var op = m.Opcoes[i];
                if (op.Objeto == null || !op.Objeto.activeSelf) continue;
                Vector3 alvo;
                if (k < Acima)
                {
                    int linha = k / 2, coluna = k % 2;
                    alvo = new Vector3(centro.x + (coluna == 0 ? -meiaColuna : meiaColuna), centro.y + passoY * (filas - linha), centro.z);
                }
                else
                {
                    int j = k - Acima, linha = j / 2, coluna = j % 2;
                    bool sozinha = coluna == 0 && j == abaixo - 1;
                    float x = sozinha ? centro.x : centro.x + (coluna == 0 ? -meiaColuna : meiaColuna);
                    alvo = new Vector3(x, centro.y - passoY * (1 + linha), centro.z);
                }
                k++;
                if ((op.Objeto.transform.localPosition - alvo).sqrMagnitude > 0.0001f) op.Objeto.transform.localPosition = alvo;
            }
            PosicionarBarra(m, conteudo, centro, centro.y - passoY * ((abaixo + 1) / 2), altura * 1.3f);
            if (!m.Posicionou)
            {
                m.Posicionou = true;
                Log.LogInfo("[aba] opcoes posicionadas: centro " + centro + ", botao " + largura.ToString("0.00") + "x" + altura.ToString("0.00"));
            }
        }

        static void PosicionarBarra(Montagem m, Transform conteudo, Vector3 centro, float yUltimaLinha, float passoY)
        {
            var b = m.Resolucao;
            if (b == null || b.Objeto == null) return;
            if (!b.Objeto.activeSelf) b.Objeto.SetActive(true);
            // under the last button, top to bottom: the name, the bar, the size
            float yBarra = yUltimaLinha - passoY * 1.7f;
            Vector3 local = b.Objeto.transform.localPosition;
            if (!b.Centralizou && b.Slide.Bar != null && b.Slide.Bar.bounds.size.x > 0.01f)
            {
                local.x += centro.x - conteudo.InverseTransformPoint(b.Slide.Bar.bounds.center).x;
                b.Centralizou = true;
            }
            // the root's y so that the BAR (not the root) sits on yBarra
            if (b.Slide.Bar != null)
            {
                float barraLocal = conteudo.InverseTransformPoint(b.Slide.Bar.bounds.center).y;
                float raizLocal = conteudo.InverseTransformPoint(b.Objeto.transform.position).y;
                local.y = yBarra - (barraLocal - raizLocal);
            }
            local.z = centro.z;
            if ((b.Objeto.transform.localPosition - local).sqrMagnitude > 0.0001f) b.Objeto.transform.localPosition = local;
            if (b.Slide.Bar != null)
            {
                Vector3 barra = conteudo.InverseTransformPoint(b.Slide.Bar.bounds.center);
                void Texto(TextMeshPro t, float y)
                {
                    if (t == null) return;
                    Vector3 alvo = conteudo.TransformPoint(new Vector3(barra.x, y, barra.z));
                    if ((t.transform.position - alvo).sqrMagnitude > 0.00001f) t.transform.position = alvo;
                }
                Texto(b.Titulo, yBarra + passoY * 0.7f);
                Texto(b.Valor, yBarra - passoY * 0.7f);
            }
        }

        static void CriarOpcoes(Montagem m)
        {
            var o = Opcoes.Atual;
            int maxHz = Tela.Maxima();
            var quadros = new List<int> { 0, 30, 60 };
            if (maxHz >= 90) quadros.Add(90);
            if (maxHz >= 120) quadros.Add(120);
            string Sn(bool v) => v ? "Sim" : "Não";

            /* (12/11) DUAS PÁGINAS. Com "Mais opções" fechado: Otimização, Ping, Personalizar HUD, Modo escuro e Mais opções. Aberto: o resto, no
               lugar dos de cima (Limite de FPS, Modo flash, Movimento suave, Texturas, Sombra do PC e Voltar). Assim a grade nunca
               passa de 6 botões em cima da "Imagem tremida" (com o Ping e o HUD novos seriam 10 de uma vez). */
            Func<bool> pagina1 = () => !_mais, pagina2 = () => _mais;
            // row 1
            m.Opcoes.Add(new Opcao
            {
                Visivel = pagina1,
                /* (11/11, ele: "menos texto, menos complexidade, mais facilidade") Um botão só escolhe tudo: cada
                   toque troca o nível (Equilibrado > Rápido > Bonito > Não) e o nível escreve o tamanho da tela e as
                   texturas (Opcoes.PorNivel). "Não" = o jogo exatamente como vem, para comparar (o contador fica). */
                Rotulo = () => "Otimização: " + (o.Ligada ? Opcoes.NomeDoNivel(o.Nivel) : "Não (jogo normal)"),
                Ligada = () => o.Ligada,
                Clique = () =>
                {
                    if (!o.Ligada) o.PorNivel(Opcoes.Equilibrado);
                    else if (o.Nivel == Opcoes.Equilibrado) o.PorNivel(Opcoes.Rapido);
                    else if (o.Nivel == Opcoes.Rapido) o.PorNivel(Opcoes.Bonito);
                    else o.Ligada = false;
                    // the slider moves to the level's size on the next tick
                    if (m.Resolucao != null) m.Resolucao.Indice = -1;
                    Salvar();
                },
            });
            // (12/11) o "Contador de FPS" virou a barra do ping: o FPS vai junto do ping, embaixo
            m.Opcoes.Add(OpcaoDoPing(pagina1));
            // row 2
            m.Opcoes.Add(OpcaoDoHud(m, pagina1));
            m.Opcoes.Add(OpcaoDoEscuro(pagina1));
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Limite de FPS: " + (o.Fps > 0 ? o.Fps.ToString() : "do jogo"),
                Ligada = () => o.Ligada && o.Fps > 0,
                Clique = () => { o.Fps = Proximo(quadros, o.Fps); Salvar(); },
                Visivel = pagina2,
            });
            // row 3, only with "Mais opções" open (no anti-aliasing: phone test 08/11, MSAA 4x left Freeplay with a
            // black screen; the game's lights and shadows render through textures that break with it)
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Modo flash: " + Sn(o.ModoFlash),
                Ligada = () => o.Ligada && o.ModoFlash,
                Clique = () => { o.ModoFlash = !o.ModoFlash; Salvar(); },
#if OTIM_DIAG
                Visivel = () => false,   // no APK de teste o lugar dele é do Diagnóstico (a opção continua valendo)
#else
                Visivel = pagina2,
#endif
            });
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Movimento suave: " + Sn(o.MovimentoSuave),
                Ligada = () => o.Ligada && o.MovimentoSuave,
                Clique = () => { o.MovimentoSuave = !o.MovimentoSuave; Salvar(); },
                Visivel = pagina2,
            });
            // below "Imagem tremida", only with "Mais opções" open
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Texturas no máximo: " + Sn(o.TexturasMaximas),
                Ligada = () => o.Ligada && o.TexturasMaximas,
                Clique = () => { o.TexturasMaximas = !o.TexturasMaximas; Salvar(); },
                Visivel = pagina2,
            });
            /* (11/11) Sombra.cs: a sombra que o jogo usa no PC (placa de vídeo) no lugar da de raios do Android. É
               TESTE e nasce desligada: ninguém viu num celular ainda. (O "fora da tela não trabalha" do Leve.cs não
               tem botão: na tela nada muda, e "Otimização: Não" já desliga.) */
            m.Opcoes.Add(new Opcao
            {
                // (11/11, noite) ligar dentro da partida mede ali mesmo: 4 s com a sombra do jogo, 4 s com a do PC,
                // e o botão mostra por um minuto "Sombra do PC: 38 para 51 FPS" (Sombra.Botao)
                Rotulo = () => Sombra.Rotulo() ?? "Sombra do PC (teste): " + Sn(o.SombraDoPc),
                Ligada = () => o.Ligada && o.SombraDoPc,
                Clique = () => { Sombra.Botao(); Salvar(); },
                Visivel = pagina2,
            });
#if OTIM_DIAG
            /* SÓ NO APK DE TESTE: mede no aparelho onde o tempo do quadro vai (Diagnostico.cs). Fica no lugar do
               "Modo flash" (que só vale na abertura do jogo) para a grade continuar com as mesmas linhas. */
            m.Opcoes.Add(new Opcao
            {
                Rotulo = Diagnostico.Rotulo,
                Ligada = () => Diagnostico.Rodando,
                Clique = Diagnostico.Botao,
                Visivel = pagina2,
            });
#endif
            /* (11/11) O resto fica guardado atrás deste botão. (12/11) Ele troca de página: aberto, mostra o resto no lugar dos de
               cima e vira "Voltar". Fecha sozinho quando o jogo reabre. */
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => _mais ? "Voltar" : "Mais opções",
                Ligada = () => false,
                Clique = () => { _mais = !_mais; },
            });
            // below "Imagem tremida": the main-menu style of the CriCriMenu mod (4 choices, the game's own included)
            string estilo = EstiloMenu.Ler();
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Menu: " + EstiloMenu.Rotulo(estilo),
                Ligada = () => estilo != "normal",
                Clique = () =>
                {
                    estilo = EstiloMenu.Valores[(Array.IndexOf(EstiloMenu.Valores, estilo) + 1) % EstiloMenu.Valores.Length];
                    EstiloMenu.Salvar(estilo);
                },
                Visivel = () => _mais && EstiloMenu.ModInstalado(),
            });
        }

        // (12/11) A barra do ping: um botão troca o modo (só o ping, com FPS, com FPS e servidor, escondido).
        static Opcao OpcaoDoPing(Func<bool> visivel) => new Opcao
        {
            Rotulo = () => "Ping: " + BarraDoPing.NomeDoModo(Opcoes.Atual.BarraPing),
            Ligada = () => Opcoes.Atual.BarraPing != BarraDoPing.Escondido,
            Clique = () => { var o = Opcoes.Atual; o.BarraPing = (o.BarraPing + 1) % 4; Salvar(); },
            Visivel = visivel,
        };

        /* (12/11, ele: "um botão pra deixar modo escuro completo, só o chat, ou modo normal") O modo escuro: cada toque troca
           normal > só o chat > completo. É o mesmo do editor do HUD (lá dá para escolher peça por peça; aí aparece "do seu jeito"). */
        static Opcao OpcaoDoEscuro(Func<bool> visivel) => new Opcao
        {
            Rotulo = () => "Modo escuro: " + LayoutDoHud.NomeDoModoEscuro(LayoutDoHud.ModoEscuro()),
            Ligada = () => LayoutDoHud.ModoEscuro() != LayoutDoHud.EscuroNormal,
            Clique = () => { LayoutDoHud.PorModoEscuro(LayoutDoHud.ProximoModoEscuro()); LayoutDoHud.Salvar(); },
            Visivel = visivel,
        };

        // (12/11) O editor do HUD: fecha as Definições e abre o editor por cima do HUD (EditorDoHud).
        static string _avisoDoHud;
        static float _avisoDoHudAte;
        static Opcao OpcaoDoHud(Montagem m, Func<bool> visivel) => new Opcao
        {
            Rotulo = () => Time.unscaledTime < _avisoDoHudAte ? _avisoDoHud : "Personalizar HUD",
            Ligada = () => LayoutDoHud.Arquivo.Atual.Count > 0,
            Clique = () =>
            {
                if (!EditorDoHud.PodeAbrir(out string porque)) { _avisoDoHud = porque; _avisoDoHudAte = Time.unscaledTime + 3f; return; }
                try { if (m.Menu != null) m.Menu.Close(); } catch { }
                EditorDoHud.Abrir();
            },
            Visivel = visivel,
        };

        // ---------------- PC: a aba própria ----------------

        /*
          (11/11, noite, ele: "ele fica estranho nas configurações pq ele foi feito pra mobile, e no pc é diferente,
          olha e corrija") NO PC a aba Gráficos do jogo tem a Resolução DELE, Ecrã inteiro, VSync, Imagem tremida e
          Aplicar, e a aba Geral tem o "Modo para streamers" do próprio jogo. A grade do celular caía por cima das
          abas, da barra de resolução e do VSync, a nossa barra de resolução ficava em cima da "Imagem tremida", e em
          Geral dois botões nossos ficavam por cima dos do jogo (fotos da rodada 15).

          No PC as opções ganham uma ABA PRÓPRIA, "Otimização", feita com as peças do jogo: a aba é uma cópia de uma
          aba dele, entra na lista Tabs e abre pelo OpenTabGroup dele (o jeito que a aba "CriCri" do QoL já usa no PC
          do dono). Nada do mod entra em Gráficos nem em Geral. Ficam de fora o que o PC já tem ou não precisa: a
          barra de resolução (o PC tem a dele), o nível (ele só escolhia resolução e textura: vira Sim/Não), a cópia
          do modo streamer e a "Sombra do PC" (no PC a sombra já é essa).

          A aba nova tem metade do tamanho das do jogo e fica à direita da última; se outro mod já pôs uma aba ali
          (a "CriCri" do QoL), ela vai para a esquerda da primeira. As abas do jogo não são mexidas.
        */
        static Montagem MontarPc(OptionsMenuBehaviour menu)
        {
            var abas = menu.Tabs;
            ToggleButtonBehaviour modelo = null;
            foreach (var b in new[] { menu.CensorChatButton, menu.EnableFriendInvitesButton, menu.ColorBlindButton })
                if (b != null) { modelo = b; break; }
            if (modelo == null) { Log.LogWarning("[aba] PC: nenhum botao liga/desliga do jogo pra copiar"); return null; }

            // o molde: a última aba VISÍVEL que é do jogo (o menu tem uma aba escondida, e a de outro mod é encolhida)
            TabGroup molde = null;
            for (int i = abas.Length - 1; i >= 0 && molde == null; i--)
            {
                var a = abas[i];
                if (a == null || DeMod(a) || !a.gameObject.activeInHierarchy || a.Button == null || !a.Button.gameObject.activeInHierarchy) continue;
                molde = a;
            }
            if (molde == null) { Log.LogWarning("[aba] PC: nenhuma aba visivel do jogo pra copiar"); return null; }

            var aba = UnityEngine.Object.Instantiate(molde, molde.transform.parent);
            aba.name = "CriCriOtimizacao_Aba";
            foreach (var tr in aba.GetComponentsInChildren<TextTranslatorTMP>(true)) UnityEngine.Object.DestroyImmediate(tr);
            // o conteúdo copiado sai; entra um vazio no MESMO pai do conteúdo do molde (nunca dentro da aba, que é encolhida)
            if (aba.Content != null && aba.Content.transform.IsChildOf(aba.transform)) UnityEngine.Object.Destroy(aba.Content);
            var conteudo = new GameObject("CriCriOtimizacao_Conteudo");
            var lugar = molde.Content != null ? molde.Content.transform : molde.transform;
            conteudo.layer = lugar.gameObject.layer;
            conteudo.transform.SetParent(lugar.parent, false);
            conteudo.transform.localPosition = lugar.localPosition;
            conteudo.transform.localRotation = lugar.localRotation;
            conteudo.transform.localScale = lugar.localScale;
            aba.Content = conteudo;
            foreach (var tmp in aba.GetComponentsInChildren<TextMeshPro>(true))
                if (tmp != null && !tmp.transform.IsChildOf(conteudo.transform)) tmp.text = "Otimização";

            // o clique: um evento novo (o da cópia abriria a aba que serviu de molde)
            int nova = abas.Length;
            foreach (var pb in aba.GetComponentsInChildren<PassiveButton>(true))
            {
                if (pb == null || pb.transform.IsChildOf(conteudo.transform)) continue;
                pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                pb.OnClick.AddListener((Action)(() =>
                {
                    try { menu.OpenTabGroup(nova); }
                    catch (Exception e) { Log.LogWarning("[aba] PC: abrir a aba: " + e.GetType().Name + ": " + e.Message); }
                }));
            }

            // a aba entra na lista do jogo: o OpenTabGroup dele abre e fecha a nossa junto com as outras
            var lista = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<TabGroup>(abas.Length + 1);
            for (int i = 0; i < abas.Length; i++) lista[i] = abas[i];
            lista[nova] = aba;
            menu.Tabs = lista;

            var m = new Montagem { Menu = menu, Conteudo = conteudo, Pc = true, Aba = aba, MoldeDaAba = molde, IndiceDaAba = nova };
            CriarOpcoesPc(m);
            for (int i = 0; i < m.Opcoes.Count; i++)
                Copiar(m, m.Opcoes[i], modelo, conteudo.transform, "CriCriOpcao" + i);
            conteudo.SetActive(false);   // começa fechada, como as outras abas que não são a primeira
            PosicionarAbaPc(m);
            Log.LogInfo("[aba] PC: aba Otimizacao com " + m.Opcoes.Count + " opcoes (aba " + (nova + 1) + " de " + (nova + 1) + ", molde " + molde.name + ")");
            return m;
        }

        // Uma aba que não é do jogo: a nossa ou a de outro mod do CriCri (a "CriCri" do QoL).
        static bool DeMod(TabGroup a) => a.name.IndexOf("CriCri", StringComparison.OrdinalIgnoreCase) >= 0;

        // A largura de uma aba do jogo na unidade local do pai das abas (do desenho da moldura, que não depende de animação).
        static float LarguraDaAba(TabGroup molde)
        {
            float largura = 2.6f;
            var sr = molde.Button;
            if (sr != null && sr.sprite != null)
            {
                float w = sr.drawMode == SpriteDrawMode.Simple ? sr.sprite.bounds.size.x : sr.size.x;
                for (var t = sr.transform; t != null && t != molde.transform.parent; t = t.parent) w *= Mathf.Abs(t.localScale.x);
                if (w > 0.2f) largura = w;
            }
            return largura;
        }

        // A aba nova, com metade do tamanho: à direita da última aba; com a aba de outro mod já lá, à esquerda da primeira.
        static void PosicionarAbaPc(Montagem m)
        {
            if (m.Aba == null || m.MoldeDaAba == null || !m.Menu.gameObject.activeInHierarchy) return;
            var molde = m.MoldeDaAba;
            float largura = LarguraDaAba(molde);
            float escalaDoMolde = Mathf.Abs(molde.transform.localScale.x) > 0.0001f ? Mathf.Abs(molde.transform.localScale.x) : 1f;
            float esquerda = float.MaxValue, direita = float.MinValue;
            bool outroMod = false;
            var abas = m.Menu.Tabs;
            for (int i = 0; i < abas.Length; i++)
            {
                var a = abas[i];
                if (a == null || a.Pointer == m.Aba.Pointer || !a.gameObject.activeInHierarchy) continue;
                if (a.Button != null && !a.Button.gameObject.activeInHierarchy) continue;
                if (DeMod(a)) outroMod = true;
                float meia = largura * 0.5f * Mathf.Abs(a.transform.localScale.x) / escalaDoMolde;
                float x = a.transform.localPosition.x;
                if (x - meia < esquerda) esquerda = x - meia;
                if (x + meia > direita) direita = x + meia;
            }
            if (esquerda == float.MaxValue) return;
            const float Vao = 0.08f, Margem = 0.14f;
            // O tamanho: o que couber entre as abas e a borda da caixa, de metade até o tamanho inteiro de uma aba do
            // jogo (dentro da partida há só duas abas e cabe uma inteira; no menu, com três, cabe pouco mais da metade).
            float K = 0.5f;
            var caixa = m.Menu.Background;
            var pai = molde.transform.parent;
            if (caixa != null && pai != null && caixa.bounds.size.x > 4f)
            {
                float bordaEsq = pai.InverseTransformPoint(caixa.bounds.min).x + Margem;
                float bordaDir = pai.InverseTransformPoint(caixa.bounds.max).x - Margem;
                if (bordaEsq > bordaDir) { float t = bordaEsq; bordaEsq = bordaDir; bordaDir = t; }
                float sobra = outroMod ? esquerda - Vao - bordaEsq : bordaDir - direita - Vao;
                K = Mathf.Clamp(sobra / largura, 0.5f, 1f);
            }
            // numa aba pequena "Otimização" fica miúdo demais para ler: o rótulo curto
            string rotulo = K >= 0.75f ? "Otimização" : "Otim.";
            if (m.RotuloDaAba != rotulo)
            {
                m.RotuloDaAba = rotulo;
                foreach (var tmp in m.Aba.GetComponentsInChildren<TextMeshPro>(true))
                    if (tmp != null) tmp.text = rotulo;
            }
            float minha = largura * K;
            float xNovo = outroMod ? esquerda - Vao - minha * 0.5f : direita + Vao + minha * 0.5f;
            var pm = molde.transform.localPosition;
            var escala = new Vector3(molde.transform.localScale.x * K, molde.transform.localScale.y * K, molde.transform.localScale.z);
            var posicao = new Vector3(xNovo, pm.y, pm.z);
            if (m.Aba.transform.localScale != escala) m.Aba.transform.localScale = escala;
            if ((m.Aba.transform.localPosition - posicao).sqrMagnitude > 0.00001f)
            {
                m.Aba.transform.localPosition = posicao;
                Log.LogInfo("[aba] PC: aba Otimizacao " + (outroMod ? "a esquerda da primeira (ha aba de outro mod a direita)" : "a direita da ultima")
                    + " em x=" + xNovo.ToString("0.00") + " (abas do jogo de " + esquerda.ToString("0.00") + " a " + direita.ToString("0.00") + ", largura " + largura.ToString("0.00") + ")");
            }
        }

        // As opções em duas colunas, logo abaixo das abas, no passo dos botões do jogo. Só com a aba aberta.
        static void PosicionarPc(Montagem m)
        {
            var conteudo = m.Conteudo.transform;
            if (m.Opcoes.Count == 0 || m.Opcoes[0].Objeto == null) return;
            int visiveis = 0;
            foreach (var op in m.Opcoes)
            {
                bool visivel = op.Visivel == null || op.Visivel();
                if (op.Objeto != null && op.Objeto.activeSelf != visivel) op.Objeto.SetActive(visivel);
                if (visivel) visiveis++;
            }
            var fundo = m.Opcoes[0].Fundo;
            Vector3 tamanho = fundo != null ? fundo.bounds.size : Vector3.zero;
            float ex = Math.Abs(conteudo.lossyScale.x) > 0.0001f ? Math.Abs(conteudo.lossyScale.x) : 1f;
            float ey = Math.Abs(conteudo.lossyScale.y) > 0.0001f ? Math.Abs(conteudo.lossyScale.y) : 1f;
            float largura = tamanho.x > 0.01f ? tamanho.x / ex : 2.5f;
            float altura = tamanho.y > 0.01f ? tamanho.y / ey : 0.4f;

            // as duas colunas e a profundidade: as dos botões do próprio jogo na aba Geral (ficam guardadas mesmo com ela fechada)
            var menu = m.Menu;
            var esq = menu.CensorChatButton != null ? menu.CensorChatButton.transform : (menu.ColorBlindButton != null ? menu.ColorBlindButton.transform : null);
            var dir = menu.EnableFriendInvitesButton != null ? menu.EnableFriendInvitesButton.transform : null;
            float xEsq = -(largura * 0.54f), xDir = largura * 0.54f, z = -1f;
            if (esq != null)
            {
                Vector3 a = conteudo.InverseTransformPoint(esq.position);
                z = a.z;
                if (dir != null)
                {
                    Vector3 b = conteudo.InverseTransformPoint(dir.position);
                    if (b.x - a.x > largura * 0.6f) { xEsq = a.x; xDir = b.x; }
                }
            }
            // a primeira fileira logo abaixo das abas
            var molde = m.MoldeDaAba;
            Transform faixa = molde != null ? (molde.Button != null ? molde.Button.transform : molde.transform) : null;
            float yAbas = faixa != null ? conteudo.InverseTransformPoint(faixa.position).y : altura * 6f;
            float passoY = altura * 1.3f;
            float y0 = yAbas - altura * 1.9f;

            int k = 0;
            for (int i = 0; i < m.Opcoes.Count; i++)
            {
                var op = m.Opcoes[i];
                if (op.Objeto == null || !op.Objeto.activeSelf) continue;
                int linha = k / 2, coluna = k % 2;
                bool sozinha = coluna == 0 && k == visiveis - 1;
                float x = sozinha ? (xEsq + xDir) * 0.5f : (coluna == 0 ? xEsq : xDir);
                var alvo = new Vector3(x, y0 - passoY * linha, z);
                k++;
                if ((op.Objeto.transform.localPosition - alvo).sqrMagnitude > 0.0001f) op.Objeto.transform.localPosition = alvo;
            }
            if (!m.PosicionouPc)
            {
                m.PosicionouPc = true;
                Log.LogInfo("[aba] PC: opcoes posicionadas: colunas " + xEsq.ToString("0.00") + " e " + xDir.ToString("0.00") + ", primeira fileira em y=" + y0.ToString("0.00")
                    + ", passo " + passoY.ToString("0.00") + ", botao " + largura.ToString("0.00") + "x" + altura.ToString("0.00"));
            }
        }

        // O que faz sentido no PC. Sem níveis (eles só escolhiam resolução e textura), sem barra, sem a sombra.
        static void CriarOpcoesPc(Montagem m)
        {
            var o = Opcoes.Atual;
            var quadros = new List<int> { 0, 30, 60, 90, 120, 144, 165, 240 };
            string Sn(bool v) => v ? "Sim" : "Não";

            m.Opcoes.Add(new Opcao
            {
                // a chave geral, para comparar: "Não" = o jogo como vem (o contador fica)
                Rotulo = () => "Otimização: " + (o.Ligada ? "Sim" : "Não (jogo normal)"),
                Ligada = () => o.Ligada,
                Clique = () => { o.Ligada = !o.Ligada; Salvar(); },
            });
            m.Opcoes.Add(OpcaoDoPing(null));
            m.Opcoes.Add(OpcaoDoHud(m, null));
            m.Opcoes.Add(OpcaoDoEscuro(null));
            m.Opcoes.Add(new Opcao
            {
                // no PC o limite só vale com o VSync do jogo desligado (com ele ligado quem manda é o monitor), e o mod
                // não mexe no VSync: o botão dele é do jogo, na aba Gráficos
                Rotulo = () => "Limite de FPS: " + (o.Fps > 0 ? o.Fps.ToString() : "do jogo")
                    + (o.Ligada && o.Fps > 0 && QualitySettings.vSyncCount != 0 ? " (desligue o VSync)" : ""),
                Ligada = () => o.Ligada && o.Fps > 0,
                Clique = () => { o.Fps = Proximo(quadros, quadros.Contains(o.Fps) ? o.Fps : 0); Salvar(); },
            });
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Movimento suave: " + Sn(o.MovimentoSuave),
                Ligada = () => o.Ligada && o.MovimentoSuave,
                Clique = () => { o.MovimentoSuave = !o.MovimentoSuave; Salvar(); },
            });
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Texturas no máximo: " + Sn(o.TexturasMaximas),
                Ligada = () => o.Ligada && o.TexturasMaximas,
                Clique = () => { o.TexturasMaximas = !o.TexturasMaximas; Salvar(); },
            });
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Modo flash: " + Sn(o.ModoFlash),
                Ligada = () => o.Ligada && o.ModoFlash,
                Clique = () => { o.ModoFlash = !o.ModoFlash; Salvar(); },
            });
            m.Opcoes.Add(new Opcao
            {
                // só com o modo daltônico do jogo ligado (aba Geral): pinta o nome da cor de cada jogador
                Rotulo = () => "Nome na cor do jogador: " + Sn(o.DaltonicoCorNoNome),
                Ligada = () => o.DaltonicoCorNoNome,
                Clique = () => { o.DaltonicoCorNoNome = !o.DaltonicoCorNoNome; Salvar(); },
                Visivel = () => DataManager.IsSettingsLoaded && DataManager.Settings.Accessibility.ColorBlindMode,
            });
#if OTIM_DIAG
            m.Opcoes.Add(new Opcao
            {
                Rotulo = Diagnostico.Rotulo,
                Ligada = () => Diagnostico.Rodando,
                Clique = Diagnostico.Botao,
            });
#endif
        }

        static bool Streamer() => DataManager.IsSettingsLoaded && DataManager.Settings.Gameplay.StreamerMode;

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(
            Math.Abs(b.x) > 0.0001f ? a.x / b.x : 1f,
            Math.Abs(b.y) > 0.0001f ? a.y / b.y : 1f,
            Math.Abs(b.z) > 0.0001f ? a.z / b.z : 1f);

        static int Proximo(List<int> valores, int atual)
        {
            int i = valores.IndexOf(atual);
            return valores[(i + 1) % valores.Count];
        }

        static void Salvar()
        {
            Opcoes.Atual.Salvar();
            Otimizacao.Instancia?.AplicarAgora();
        }

        static void Clicar(Montagem m, Opcao op)
        {
            try
            {
                op.Clique();
                Log.LogInfo("[aba] " + op.Rotulo());
            }
            catch (Exception e) { Log.LogWarning("[aba] clique: " + e.GetType().Name + ": " + e.Message); }
            // "Mais opções" mostra e esconde botões: a grade é refeita já, sem esperar o meio segundo
            try { if (m.Conteudo != null && m.Conteudo.activeInHierarchy) { if (m.Pc) PosicionarPc(m); else Posicionar(m); } }
            catch (Exception e) { Log.LogWarning("[aba] grade: " + e.GetType().Name + ": " + e.Message); }
            Atualizar(m);
            PosicionarDaltonico(m);
            if (m.Streamer != null && m.Streamer.Objeto != null && m.Streamer.Objeto.activeSelf) Pintar(m.Streamer);
        }

        static void Atualizar(Montagem m)
        {
            foreach (var op in m.Opcoes)
            {
                if (op.Objeto == null || !op.Objeto.activeSelf) continue;
                Pintar(op);
            }
            if (m.Resolucao != null) EscreverValor(m.Resolucao);
        }

#if OTIM_TESTE
        // Só no build de teste do PC: o piloto lê os rótulos e clica nos botões como o dedo faria.
        internal static string TesteRotulos()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var par in _montagens)
            {
                var m = par.Value;
                if (m.Conteudo == null) continue;
                for (int i = 0; i < m.Opcoes.Count; i++)
                {
                    var op = m.Opcoes[i];
                    bool visivel = op.Objeto != null && op.Objeto.activeSelf;
                    var lp = op.Objeto != null ? op.Objeto.transform.localPosition : Vector3.zero;
                    sb.Append(i).Append('[').Append(visivel ? "v" : "-").Append("] ").Append(op.Rotulo())
                      .Append(visivel ? " @" + lp.x.ToString("0.00") + "," + lp.y.ToString("0.00") : "").Append(" | ");
                }
                if (m.Resolucao != null && m.Resolucao.Valor != null) sb.Append("barra: ").Append(m.Resolucao.Valor.text);
                sb.Append(" || ");
            }
            return sb.ToString();
        }

        internal static void TesteClicar(int i)
        {
            foreach (var par in _montagens)
            {
                var m = par.Value;
                if (m.Conteudo == null || i >= m.Opcoes.Count) continue;
                Clicar(m, m.Opcoes[i]);
            }
        }

        internal static int TesteMontagens => _montagens.Count;
#endif

        // Once: the menu's layout in the log (names, which tab is open), for later tuning.
        static void LogEstrutura(OptionsMenuBehaviour menu)
        {
            if (_logouEstrutura) return;
            _logouEstrutura = true;
            try
            {
                var abas = menu.Tabs;
                var sb = new System.Text.StringBuilder("[aba] estrutura das opcoes: ");
                for (int i = 0; i < abas.Length; i++)
                {
                    var a = abas[i];
                    if (a == null) continue;
                    sb.Append("aba ").Append(i).Append(' ').Append(a.name).Append(a.gameObject.activeInHierarchy ? "" : " (escondida)")
                      .Append(" conteudo ").Append(a.Content != null ? a.Content.name : "-").Append("; ");
                }
                Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Log.LogWarning("[aba] estrutura: " + e.Message); }
        }
    }
}
