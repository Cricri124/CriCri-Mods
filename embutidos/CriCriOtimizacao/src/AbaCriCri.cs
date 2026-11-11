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
        }

        static readonly Dictionary<IntPtr, Montagem> _montagens = new Dictionary<IntPtr, Montagem>();
        static readonly HashSet<IntPtr> _falhas = new HashSet<IntPtr>();
        static bool _logouEstrutura;
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
            Construir(menu);
        }

        // Every 0.5 s from Otimizacao.Update.
        public static void Procurar()
        {
            if (!_startFunciona)
            {
                var menus = UnityEngine.Object.FindObjectsOfType<OptionsMenuBehaviour>();
                for (int i = 0; i < menus.Length; i++)
                    if (menus[i] != null && menus[i].isActiveAndEnabled) Construir(menus[i]);
            }

            var mortos = new List<IntPtr>();
            foreach (var par in _montagens)
            {
                var m = par.Value;
                if (m.Menu == null || m.Conteudo == null) { mortos.Add(par.Key); continue; }
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

            // row 1
            m.Opcoes.Add(new Opcao
            {
                // to compare: "Não" = the game exactly as it comes (the FPS counter stays)
                Rotulo = () => "Otimização: " + (o.Ligada ? "Sim" : "Não (jogo normal)"),
                Ligada = () => o.Ligada,
                Clique = () =>
                {
                    o.Ligada = !o.Ligada;
                    // CriCri, 08/11: turning it on brings the recommended size; the slider moves to it on the next tick
                    if (o.Ligada)
                    {
                        o.Resolucao = Opcoes.Recomendada();
                        if (m.Resolucao != null) m.Resolucao.Indice = -1;
                    }
                    Salvar();
                },
            });
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Contador de FPS: " + Sn(o.ContadorFps),
                Ligada = () => o.ContadorFps,
                Clique = () => { o.ContadorFps = !o.ContadorFps; Salvar(); },
            });
            // row 2
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Limite de FPS: " + (o.Fps > 0 ? o.Fps.ToString() : "do jogo"),
                Ligada = () => o.Ligada && o.Fps > 0,
                Clique = () => { o.Fps = Proximo(quadros, o.Fps); Salvar(); },
            });
            m.Opcoes.Add(new Opcao
            {
                Rotulo = () => "Modo flash: " + Sn(o.ModoFlash),
                Ligada = () => o.Ligada && o.ModoFlash,
                Clique = () => { o.ModoFlash = !o.ModoFlash; Salvar(); },
            });
            // row 3 (no anti-aliasing: phone test 08/11, MSAA 4x left Freeplay with a black screen; the game's
            // lights and shadows render through textures that break with it)
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
                Visivel = EstiloMenu.ModInstalado,
            });
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
