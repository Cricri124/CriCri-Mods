using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BepInEx;
using TMPro;
using UnityEngine;

namespace CriCriMods
{
    /*
      (12/11, ele: "eu pedi pra adicionar uma opção pra mobile de personalizar hud, nas configurações você clica lá, aparece um
      menu com todos os botões, analógico, usar, que muda também o carinho de pet, kill, habilidade 1 e 2, tipo detetive que tem o
      interrogar e a aba dele, todos os botões que tem de uma forma fácil de configurar, dando pra clicar no botão, na parte de cima
      aparece uma barra de volume pra mudar o tamanho, opacidade, ou tecla pra ativar (opcional e sincroniza com as teclas do pc) e
      também dá pra arrastar coisas como aquela parte do ping, etc, e dá pra salvar preset e voltar pro padrão")

      O LAYOUT: para cada peça do HUD, o que a pessoa mudou (lugar, tamanho, opacidade, tecla). O que ela não mudou fica como o
      jogo põe. Guardado em BepInEx/config/cricri-mods-hud.json, com 3 presets.

      AS PEÇAS são os botões do próprio jogo (HudManager): usar (e o carinho no pet, que o jogo troca no mesmo lugar), matar,
      reportar, sabotar, duto, habilidade 1 e 2 (o botão das funções, como o interrogar do Detetive), admin do Esconde-esconde,
      mapa, menu, informações da partida, chat, o joystick do celular, o nome da sala e a barra do ping. Nada é recriado: o mod só
      muda o lugar, o tamanho e a transparência do botão do jogo, e a tecla chama o mesmo clique do dedo.

      COMO O LUGAR É POSTO, sem brigar com o jogo (lido no código dele em 12/11):
      - os botões de ação ficam numa grade (Hud/Buttons/BottomRight, GridArrange) que só arruma quando um botão aparece ou some;
        o botão arrastado ganha o lugar dele no fim de cada quadro (e é ali que o dedo o acha: ninguém mais o move);
      - os que têm AspectPosition próprio (mapa, menu, info, chat, joystick) são movidos pela distância da borda do PRÓPRIO jogo
        (DistanceFromEdge): o jogo mesmo os põe no lugar novo, inclusive quando a tela muda de tamanho. É o que garante que o
        toque cai no botão onde ele aparece;
      - o lugar é guardado em relação ao canto mais perto da tela (em "alturas de tela"), para valer em celular de formato diferente.
      O tamanho multiplica o que o jogo põe (o tamanho do joystick das Definições do jogo continua valendo), e a opacidade
      multiplica a cor que o jogo põe (o botão apagado do jogo continua mais apagado).
    */
    internal sealed class ItemDoHud
    {
        public bool Movido { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public int AncX { get; set; }      // -1 esquerda, 0 meio, 1 direita
        public int AncY { get; set; }      // -1 embaixo, 0 meio, 1 em cima
        public float Escala { get; set; } = 1f;
        public float Opacidade { get; set; } = 1f;
        public string Tecla { get; set; } = "";
        // (12/11) 0 = o desenho do jogo; 1 = escuro (a lista e a barra de tarefas: fundo escuro de cantos redondos; o resto: o
        // modo escuro do Escuro.cs)
        public int Visual { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool Padrao => !Movido && Math.Abs(Escala - 1f) < 0.001f && Math.Abs(Opacidade - 1f) < 0.001f && string.IsNullOrEmpty(Tecla) && Visual == 0;

        public ItemDoHud Copia() => (ItemDoHud)MemberwiseClone();
    }

    internal sealed class ArquivoDoHud
    {
        public int Versao { get; set; } = 1;
        public Dictionary<string, ItemDoHud> Atual { get; set; } = new Dictionary<string, ItemDoHud>();
        public List<Dictionary<string, ItemDoHud>> Presets { get; set; } = new List<Dictionary<string, ItemDoHud>> { null, null, null };
    }

    internal static class LayoutDoHud
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;
#if OTIM_TESTE
        // o build de teste roda no PC do dono: o arquivo dele na pasta do jogo nunca é tocado
        static string Caminho => Path.Combine(Path.GetTempPath(), "cricri-mods-hud-teste.json");
#else
        static string Caminho => Path.Combine(Paths.ConfigPath, "cricri-mods-hud.json");
#endif

        static ArquivoDoHud _arquivo;
        internal static ArquivoDoHud Arquivo => _arquivo ??= Ler();

        // ---------------- as peças ----------------

        internal sealed class Peca
        {
            public string Chave, Nome;
            public bool TemTecla = true;
            public bool TemVisual;        // tem o visual escuro
            public bool DoChat;           // é do chat: a janela abre no editor quando ela é escolhida
            public bool GrupoChat;        // conta como "o chat" no modo escuro "só o chat"
            public bool SoVisual;         // não aparece no editor (só o modo escuro mexe nela: a reunião)
            public Func<HudManager, List<GameObject>> Extras;   // o que escurece junto mas não muda de lugar
            public bool VisualSoNosExtras;   // o escuro vai só nos Extras (os Objetos só mudam de lugar, tamanho e opacidade)
            // (12/11) A lista de tarefas: o jogo a põe no lugar TODO quadro (ela desliza para abrir e fechar), a partir de dois
            // pontos dela (openPosition e closedPosition). Ela anda pelo deslocamento desses dois pontos, guardado em X e Y (em
            // unidades, a partir do lugar do jogo), e o jogo mesmo a desenha e a toca no lugar novo.
            public bool Deslocamento;
            public Func<HudManager, List<GameObject>> Objetos;
        }

        internal static readonly Peca[] Pecas =
        {
            new Peca { Chave = "usar", Nome = "Usar / carinho", Objetos = h => L(h.UseButton, h.PetButton) },
            new Peca { Chave = "matar", Nome = "Matar", Objetos = h => L(h.KillButton) },
            new Peca { Chave = "reportar", Nome = "Reportar", Objetos = h => L(h.ReportButton) },
            new Peca { Chave = "sabotar", Nome = "Sabotar", Objetos = h => L(h.SabotageButton) },
            new Peca { Chave = "duto", Nome = "Duto", Objetos = h => L(h.ImpostorVentButton) },
            new Peca { Chave = "habilidade1", Nome = "Habilidade 1", Objetos = h => L(h.AbilityButton) },
            new Peca { Chave = "habilidade2", Nome = "Habilidade 2", Objetos = h => L(h.SecondaryAbilityButton) },
            new Peca { Chave = "admin", Nome = "Admin", Objetos = h => L(h.AdminButton) },
            new Peca { Chave = "mapa", Nome = "Mapa", TemVisual = true, Objetos = h => L(h.MapButton) },
            new Peca { Chave = "menu", Nome = "Menu", TemVisual = true, Objetos = h => L(h.SettingsButton != null ? h.SettingsButton.transform : null) },
            new Peca { Chave = "info", Nome = "Info da partida", TemVisual = true, Objetos = h => L(h.MatchInfoButton) },
            new Peca { Chave = "chat", Nome = "Botão do chat", TemVisual = true, DoChat = true, GrupoChat = true, Objetos = h => L(h.Chat != null ? h.Chat.chatButton : null) },
            new Peca { Chave = "joystick", Nome = "Joystick", TemTecla = false, TemVisual = true, Objetos = Joysticks },
            new Peca { Chave = "sala", Nome = "Nome da sala", TemTecla = false, Objetos = h => L(h.roomTracker) },
            // (12/11, ele: "conseguindo mudar o lugar da barra de task e lista de tarefas e mudar elas deixando mais bonita")
            new Peca { Chave = "tarefas", Nome = "Lista de tarefas", TemTecla = false, TemVisual = true, Deslocamento = true, Objetos = h => L(h.TaskPanel) },
            new Peca { Chave = "barra", Nome = "Barra de tarefas", TemTecla = false, TemVisual = true, Objetos = BarraDeTarefas },
            new Peca { Chave = "ping", Nome = "Ping e FPS", TemTecla = false, Objetos = h => { var r = BarraDoPing.Raiz; return r != null ? new List<GameObject> { r } : new List<GameObject>(); } },
            /* (12/11, ele: "no hud aparece o botão do chat pra mudar também e indo nele aparece o chat também pra mudar botões do
               chat") O CHAT: a janela inteira e os botões dela. Andam pelo deslocamento a partir do lugar do jogo (como a lista de
               tarefas): os botões são filhos da janela, então andam junto quando ela anda. A notificação de mensagem nova e o menu
               do chat rápido escurecem junto com a janela. */
            // a raiz da janela (ChatScreenRoot) o jogo põe no lugar no LateUpdate dele (medido em 12/11: mexer nela não adiantava);
            // quem anda é a caixa de dentro (ChatScreenContainer), que tem tudo (fundo, balões, campo, botões)
            new Peca { Chave = "chatjanela", Nome = "Janela do chat", TemTecla = false, TemVisual = true, DoChat = true, GrupoChat = true, Deslocamento = true,
                VisualSoNosExtras = true,
                Objetos = h => O(CaixaDoChat(h)),
                Extras = h => O(h.Chat != null ? h.Chat.chatScreen : null, h.Chat != null ? h.Chat.chatNotification : null, h.Chat != null ? h.Chat.quickChatMenu : null) },
            new Peca { Chave = "chatcampo", Nome = "Campo de digitar", TemTecla = false, DoChat = true, Deslocamento = true,
                Objetos = h => O(h.Chat != null ? h.Chat.freeChatField : null, h.Chat != null ? h.Chat.quickChatField : null) },
            new Peca { Chave = "chatbanir", Nome = "Banir / denunciar", TemTecla = false, DoChat = true, Deslocamento = true, Objetos = h => O(h.Chat != null ? h.Chat.banButton : null) },
            new Peca { Chave = "chatteclado", Nome = "Abrir o teclado", TemTecla = false, DoChat = true, Deslocamento = true, Objetos = h => O(h.Chat != null ? h.Chat.openKeyboardButton : null) },
            new Peca { Chave = "chatrapido", Nome = "Chat rápido", TemTecla = false, DoChat = true, Deslocamento = true, Objetos = h => O(h.Chat != null ? h.Chat.quickChatButton : null) },
            // a tela da reunião: só o modo escuro "completo" mexe nela (o editor não abre durante a reunião)
            new Peca { Chave = "reuniao", Nome = "Reunião", TemTecla = false, TemVisual = true, SoVisual = true, Objetos = h => O(MeetingHud.Instance != null ? MeetingHud.Instance.gameObject : null) },
        };

        static GameObject CaixaDoChat(HudManager h)
        {
            if (h.Chat == null || h.Chat.chatScreen == null) return null;
            var t = h.Chat.chatScreen.transform.Find("ChatScreenContainer");
            return t != null ? t.gameObject : h.Chat.chatScreen;
        }

        // qualquer coisa do jogo (um GameObject ou um componente) -> o GameObject dela
        static List<GameObject> O(params UnityEngine.Object[] os)
        {
            var l = new List<GameObject>();
            foreach (var o in os)
            {
                if (o == null) continue;
                var go = o.TryCast<GameObject>();
                if (go == null) { var c = o.TryCast<Component>(); if (c != null) go = c.gameObject; }
                if (go != null) l.Add(go);
            }
            return l;
        }

        // ---------------- o modo escuro (12/11) ----------------

        internal const int EscuroNormal = 0, EscuroSoChat = 1, EscuroCompleto = 2, EscuroPersonalizado = 3;

        /// <summary>Como está agora, olhando peça por peça (normal, só o chat, completo, ou cada uma de um jeito).</summary>
        internal static int ModoEscuro()
        {
            bool todoChat = true, algumChat = false, todoResto = true, algumResto = false;
            foreach (var p in Pecas)
            {
                if (!p.TemVisual) continue;
                var it = Item(p.Chave);
                bool e = it != null && it.Visual == 1;
                if (p.GrupoChat) { todoChat &= e; algumChat |= e; }
                else { todoResto &= e; algumResto |= e; }
            }
            if (!algumChat && !algumResto) return EscuroNormal;
            if (todoChat && !algumResto) return EscuroSoChat;
            if (todoChat && todoResto) return EscuroCompleto;
            return EscuroPersonalizado;
        }

        /// <summary>O próximo do botão: normal > só o chat > completo > normal (o personalizado volta para o normal).</summary>
        internal static int ProximoModoEscuro()
        {
            int m = ModoEscuro();
            return m == EscuroNormal ? EscuroSoChat : m == EscuroSoChat ? EscuroCompleto : EscuroNormal;
        }

        internal static void PorModoEscuro(int m)
        {
            foreach (var p in Pecas)
            {
                if (!p.TemVisual) continue;
                bool e = m == EscuroCompleto || (m == EscuroSoChat && p.GrupoChat);
                var it = Item(p.Chave, e);
                if (it != null) it.Visual = e ? 1 : 0;
            }
            Log.LogInfo("[hud] modo escuro: " + NomeDoModoEscuro(m));
        }

        internal static string NomeDoModoEscuro(int m) => m == EscuroSoChat ? "só o chat" : m == EscuroCompleto ? "completo" : m == EscuroPersonalizado ? "do seu jeito" : "normal";
        internal static string NomeDoVisual(int v) => v == 1 ? "escuro" : "do jogo";

        static List<GameObject> L(params Component[] cs)
        {
            var l = new List<GameObject>();
            foreach (var c in cs) if (c != null) l.Add(c.gameObject);
            return l;
        }

        // a barra "Total de tarefas concluídas" (Hud/TaskDisplay/ProgressTracker, com AspectPosition LeftTop)
        static List<GameObject> BarraDeTarefas(HudManager h)
        {
            var l = new List<GameObject>();
            try
            {
                var raiz = h.TaskStuff != null ? h.TaskStuff.transform : h.transform;
                var pt = raiz.GetComponentInChildren<ProgressTracker>(true);
                if (pt != null) l.Add(pt.gameObject);
            }
            catch { }
            return l;
        }

        /* O ANALÓGICO do celular (o VirtualJoystick do jogo; o do PC é de teclado e não tem desenho). (12/11, celular: "faça eu
           conseguir mudar o analógico também") Ele não aparecia no editor: só era procurado na lista HudManager.Joysticks, com o
           VirtualJoystick no próprio objeto. Agora vale qualquer VirtualJoystick ligado: o que o jogo está usando (h.joystick),
           os da lista (no objeto ou num filho) e, de 2 em 2 s, os que estiverem embaixo do HUD. Fica o objeto do VirtualJoystick
           (o que o jogo põe no lugar com a AspectPosition dele, se tiver; senão o mod põe). */
        static readonly List<GameObject> _joysticks = new List<GameObject>();
        static float _proxBuscaJoystick;
        static IntPtr _hudDosJoysticks;

        static List<GameObject> Joysticks(HudManager h)
        {
            float agora = Time.unscaledTime;
            if (h.Pointer != _hudDosJoysticks || agora >= _proxBuscaJoystick)
            {
                _hudDosJoysticks = h.Pointer;
                _proxBuscaJoystick = agora + 2f;
                _joysticks.Clear();
                var vistos = new HashSet<IntPtr>();
                void Por(VirtualJoystick vj)
                {
                    if (vj == null || !vistos.Add(vj.gameObject.Pointer)) return;
                    if (vj.GetComponentInChildren<SpriteRenderer>(true) == null) return;
                    _joysticks.Add(vj.gameObject);
                }
                try { var j = h.joystick != null ? h.joystick.TryCast<VirtualJoystick>() : null; Por(j); } catch { }
                try
                {
                    var js = h.Joysticks;
                    if (js != null)
                        for (int i = 0; i < js.Length; i++)
                            if (js[i] != null) Por(js[i].GetComponentInChildren<VirtualJoystick>(true));
                }
                catch { }
                try { foreach (var vj in h.GetComponentsInChildren<VirtualJoystick>(false)) Por(vj); } catch { }
            }
            var l = new List<GameObject>();
            foreach (var go in _joysticks) if (go != null && go.activeInHierarchy) l.Add(go);
            return l;
        }

        internal static ItemDoHud Item(string chave, bool criar = false)
        {
            var a = Arquivo.Atual;
            if (a.TryGetValue(chave, out var it)) return it;
            if (!criar) return null;
            it = new ItemDoHud();
            a[chave] = it;
            return it;
        }

        // ---------------- guardar ----------------

        static ArquivoDoHud Ler()
        {
            try
            {
                if (File.Exists(Caminho))
                {
                    var a = JsonSerializer.Deserialize<ArquivoDoHud>(File.ReadAllText(Caminho));
                    if (a != null)
                    {
                        a.Atual ??= new Dictionary<string, ItemDoHud>();
                        a.Presets ??= new List<Dictionary<string, ItemDoHud>>();
                        while (a.Presets.Count < 3) a.Presets.Add(null);
                        Limpar(a.Atual);
                        foreach (var p in a.Presets) if (p != null) Limpar(p);
                        return a;
                    }
                }
            }
            catch (Exception e) { Log?.LogWarning("[hud] nao li " + Caminho + ": " + e.Message); }
            return new ArquivoDoHud();
        }

        // um arquivo editado na mão (ou de versão futura) não pode pôr o botão fora da tela nem do tamanho de um prédio
        static void Limpar(Dictionary<string, ItemDoHud> d)
        {
            foreach (var it in d.Values)
            {
                if (it == null) continue;
                it.Escala = Finito(it.Escala, 1f, 0.4f, 2.5f);
                it.Opacidade = Finito(it.Opacidade, 1f, 0.1f, 1f);
                it.X = Finito(it.X, 0f, -14f, 14f);
                it.Y = Finito(it.Y, 0f, -6f, 6f);
                it.Visual = Math.Clamp(it.Visual, 0, 1);
                it.AncX = Math.Clamp(it.AncX, -1, 1);
                it.AncY = Math.Clamp(it.AncY, -1, 1);
                if (it.Tecla == null || it.Tecla.Length > 24 || !Enum.TryParse<KeyCode>(it.Tecla, out _)) it.Tecla = "";
            }
        }

        static float Finito(float v, float padrao, float min, float max) => float.IsFinite(v) ? Math.Clamp(v, min, max) : padrao;

        internal static void Salvar()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Caminho));
                var limpo = new ArquivoDoHud { Versao = 1, Presets = Arquivo.Presets };
                foreach (var par in Arquivo.Atual) if (par.Value != null && !par.Value.Padrao) limpo.Atual[par.Key] = par.Value;
                File.WriteAllText(Caminho, JsonSerializer.Serialize(limpo, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e) { Log.LogWarning("[hud] nao salvei: " + e.Message); }
        }

        internal static void SalvarPreset(int i)
        {
            var d = new Dictionary<string, ItemDoHud>();
            foreach (var par in Arquivo.Atual) if (par.Value != null && !par.Value.Padrao) d[par.Key] = par.Value.Copia();
            while (Arquivo.Presets.Count < 3) Arquivo.Presets.Add(null);
            Arquivo.Presets[i] = d;
            Salvar();
            Log.LogInfo("[hud] preset " + (i + 1) + " salvo (" + d.Count + " pecas mudadas)");
        }

        internal static bool TemPreset(int i) => i >= 0 && i < Arquivo.Presets.Count && Arquivo.Presets[i] != null;

        internal static void UsarPreset(int i)
        {
            if (!TemPreset(i)) return;
            var novo = new Dictionary<string, ItemDoHud>();
            foreach (var par in Arquivo.Presets[i]) if (par.Value != null) novo[par.Key] = par.Value.Copia();
            Arquivo.Atual = novo;
            Salvar();
            Log.LogInfo("[hud] preset " + (i + 1) + " em uso (" + novo.Count + " pecas mudadas)");
        }

        internal static void VoltarAoPadrao()
        {
            Arquivo.Atual = new Dictionary<string, ItemDoHud>();
            Salvar();
            Log.LogInfo("[hud] HUD de volta ao padrao do jogo");
        }

        // ---------------- pôr no jogo ----------------

        sealed class Base
        {
            public Vector3 EscalaDoJogo, EscalaPosta;
            public bool TemEscala;
            public Vector3 DistanciaDoJogo, DistanciaPosta;
            public bool MexiNaDistancia;
            public bool MexiNoLugar;
            // calibração da AspectPosition: lugar = LugarRef + (distância - DistRef) * Inclinacao, no espaço do PAI do botão
            // (12/11, celular: em coordenadas do mundo, o LugarRef ficava parado enquanto o HUD anda com a câmera atrás do
            // boneco, e os botões movidos andavam junto e saíam da tela)
            public Vector3 DistRef, LugarRef;
            public Vector2 Inclinacao;
            public int ChaveDaCalibracao;
            public float ForaDoLugar;        // há quanto tempo o botão não está onde devia (segundos)
            public int Recalibrou;
            // a lista de tarefas: o lugar que o jogo pôs neste quadro e o que eu pus
            public Vector3 ListaDoJogo, ListaPosta;
            public bool MexiNaLista;
            // (12/11 noite) o que se pergunta ao jogo todo quadro, guardado uma vez (cada pergunta criava um objeto na memória)
            public Transform T, Pai;
            public AspectPosition Ap;
            public TaskPanelBehaviour Tp;
            public bool Guardou;
            public float ProxChave;
        }

        static readonly Dictionary<IntPtr, Base> _bases = new Dictionary<IntPtr, Base>();
        static readonly Dictionary<IntPtr, Color> _corDoJogo = new Dictionary<IntPtr, Color>();
        static readonly Dictionary<IntPtr, Color> _corPosta = new Dictionary<IntPtr, Color>();
        // os desenhos de cada peça, já separados (o SpriteRenderer ou o texto): renovados de 2 em 2 s
        sealed class Pintavel { public IntPtr P; public SpriteRenderer Sr; public TextMeshPro Tm; }
        static readonly Dictionary<IntPtr, List<Pintavel>> _desenhos = new Dictionary<IntPtr, List<Pintavel>>();
        static readonly HashSet<IntPtr> _comOpacidade = new HashSet<IntPtr>();   // peças com a opacidade do mod posta agora
        static IntPtr _hud;
        static float _proxDesenhos;
        static readonly HashSet<string> _avisos = new HashSet<string>();
        static readonly HashSet<string> _tinhaMudanca = new HashSet<string>();

        /// <summary>Do LateUpdate da Otimizacao, todo quadro (depois da barra do ping).</summary>
        internal static void Quadro()
        {
            if (!HudManager.InstanceExists) { _hud = IntPtr.Zero; return; }
            var h = HudManager.Instance;
            var cam = h.UICamera;
            if (cam == null) return;
            if (h.Pointer != _hud)
            {
                // HUD novo (cena nova): o que foi guardado do HUD antigo não vale mais
                _hud = h.Pointer;
                _bases.Clear(); _corDoJogo.Clear(); _corPosta.Clear(); _desenhos.Clear(); _tinhaMudanca.Clear(); _comOpacidade.Clear();
                _objetosDaPeca.Clear(); _extrasDaPeca.Clear();
                Escuro.Esquecer();
            }
            float agora = Time.unscaledTime;
            bool renovarDesenhos = agora >= _proxDesenhos;
            if (renovarDesenhos) { _proxDesenhos = agora + 2f; _desenhos.Clear(); }
            if (agora >= _proxObjetos) { _proxObjetos = agora + 0.5f; _objetosDaPeca.Clear(); _extrasDaPeca.Clear(); }

            foreach (var peca in Pecas)
            {
                ItemDoHud it = Item(peca.Chave);
                bool muda = it != null && !it.Padrao;
                if (!muda && !_tinhaMudanca.Contains(peca.Chave)) continue;
                List<GameObject> objs;
                try { objs = ObjetosDe(peca, h); }
                catch { continue; }
                int visual = muda ? it.Visual : 0;
                foreach (var go in objs)
                {
                    if (go == null) continue;
                    try
                    {
                        if (!peca.SoVisual)
                        {
                            if (muda) Por(go, it, cam, peca);
                            else Devolver(go);
                        }
                        if (peca.TemVisual && !peca.VisualSoNosExtras) PorVisual(peca, go, visual);
                    }
                    catch (Exception e)
                    {
                        string q = peca.Chave + e.GetType().Name;
                        if (_avisos.Add(q)) Log.LogWarning("[hud] " + peca.Chave + ": " + e.GetType().Name + ": " + e.Message);
                    }
                }
                if (peca.Extras != null)
                {
                    try { foreach (var go in ExtrasDe(peca, h)) if (go != null) Escuro.Por(go, visual == 1); }
                    catch (Exception e)
                    {
                        string q = peca.Chave + "extras" + e.GetType().Name;
                        if (_avisos.Add(q)) Log.LogWarning("[hud] " + peca.Chave + " (junto): " + e.GetType().Name + ": " + e.Message);
                    }
                }
                if (muda) _tinhaMudanca.Add(peca.Chave);
                else _tinhaMudanca.Remove(peca.Chave);
            }
        }

        // a lista e a barra de tarefas têm o desenho escuro próprio (cantos redondos); o resto, o do Escuro.cs
        static void PorVisual(Peca peca, GameObject go, int visual)
        {
            if (peca.Chave == "tarefas" || peca.Chave == "barra") VisualDasTarefas.Por(go, peca.Chave, visual);
            else Escuro.Por(go, visual == 1);
        }

        static Base BaseDe(GameObject go)
        {
            IntPtr p = go.Pointer;
            if (!_bases.TryGetValue(p, out var b)) { b = new Base(); _bases[p] = b; }
            if (!b.Guardou)
            {
                b.Guardou = true;
                b.T = go.transform;
                b.Pai = b.T.parent;
                b.Ap = go.GetComponent<AspectPosition>();
                b.Tp = go.GetComponent<TaskPanelBehaviour>();
            }
            return b;
        }

        /* (12/11 noite, medido no PC: o HUD do mod fazia lixo todo quadro, e cada coleta é uma travadinha no celular) A lista de
           objetos de cada peça é guardada por meio segundo (e esquecida a cada HUD novo): perguntar ao jogo "h.MapButton",
           ".gameObject" cria um objeto novo na memória a cada vez. A reunião (que nasce e some) é perguntada todo quadro. */
        static readonly Dictionary<string, List<GameObject>> _objetosDaPeca = new Dictionary<string, List<GameObject>>();
        static readonly Dictionary<string, List<GameObject>> _extrasDaPeca = new Dictionary<string, List<GameObject>>();
        static float _proxObjetos;

        static List<GameObject> ObjetosDe(Peca p, HudManager h)
        {
            if (p.SoVisual) return p.Objetos(h);
            if (!_objetosDaPeca.TryGetValue(p.Chave, out var l)) { l = p.Objetos(h) ?? new List<GameObject>(); _objetosDaPeca[p.Chave] = l; }
            return l;
        }

        static List<GameObject> ExtrasDe(Peca p, HudManager h)
        {
            if (!_extrasDaPeca.TryGetValue(p.Chave, out var l)) { l = p.Extras(h) ?? new List<GameObject>(); _extrasDaPeca[p.Chave] = l; }
            return l;
        }

        static void Por(GameObject go, ItemDoHud it, Camera cam, Peca peca)
        {
            var b = BaseDe(go);
            var t = b.T;

            // o tamanho: o que o jogo pôs (se ele mudou desde a última vez, vale o dele) vezes o da pessoa
            var atual = t.localScale;
            if (!b.TemEscala || atual != b.EscalaPosta) { b.EscalaDoJogo = atual; b.TemEscala = true; }
            var alvo = b.EscalaDoJogo * it.Escala;
            alvo.z = b.EscalaDoJogo.z;
            if (atual != alvo) t.localScale = alvo;
            b.EscalaPosta = t.localScale;

            // o lugar
            if (peca.Deslocamento) PorDeslocamento(go, it, b);
            else if (it.Movido) PorLugar(go, it, cam, b);
            else if (b.MexiNaDistancia || b.MexiNoLugar) DevolverLugar(go, b);

            // a opacidade
            Opacidade(go, it.Opacidade);
        }

        internal static Vector2 Mundo(ItemDoHud it, Camera cam)
        {
            float aspecto = cam.aspect > 0.01f ? cam.aspect : 1.78f;
            float ax = (it.AncX + 1) * 0.5f, ay = (it.AncY + 1) * 0.5f;
            var v = cam.ViewportToWorldPoint(new Vector3(ax + it.X / aspecto, ay + it.Y, 10f));
            return new Vector2(v.x, v.y);
        }

        /// <summary>Guarda um lugar no mundo como distância do canto mais perto (o que vai para o arquivo).</summary>
        internal static void Guardar(ItemDoHud it, Vector2 mundo, Camera cam)
        {
            float aspecto = cam.aspect > 0.01f ? cam.aspect : 1.78f;
            var v = cam.WorldToViewportPoint(new Vector3(mundo.x, mundo.y, cam.transform.position.z + 10f));
            it.AncX = v.x < 0.34f ? -1 : v.x > 0.66f ? 1 : 0;
            it.AncY = v.y < 0.34f ? -1 : v.y > 0.66f ? 1 : 0;
            float ax = (it.AncX + 1) * 0.5f, ay = (it.AncY + 1) * 0.5f;
            it.X = Math.Clamp((Math.Clamp(v.x, 0f, 1f) - ax) * aspecto, -3f, 3f);
            it.Y = Math.Clamp(Math.Clamp(v.y, 0f, 1f) - ay, -1.2f, 1.2f);
            it.Movido = true;
        }

        static void PorLugar(GameObject go, ItemDoHud it, Camera cam, Base b)
        {
            var alvo = Mundo(it, cam);
            var ap = b.Ap;
            if (ap != null && ap.enabled)
            {
                // pela distância da borda do próprio jogo (ver o comentário do topo), com a conta no espaço do pai
                if (!b.MexiNaDistancia) { b.DistanciaDoJogo = ap.DistanceFromEdge; b.MexiNaDistancia = true; }
                Calibrar(ap, b);
                var alvoLocal = NoPai(b.Pai, new Vector3(alvo.x, alvo.y, b.T.position.z));
                var d = b.DistRef;
                if (Math.Abs(b.Inclinacao.x) > 0.0001f) d.x = b.DistRef.x + (alvoLocal.x - b.LugarRef.x) / b.Inclinacao.x;
                if (Math.Abs(b.Inclinacao.y) > 0.0001f) d.y = b.DistRef.y + (alvoLocal.y - b.LugarRef.y) / b.Inclinacao.y;
                if ((ap.DistanceFromEdge - d).sqrMagnitude > 0.000001f || b.DistanciaPosta != d)
                {
                    ap.DistanceFromEdge = d;
                    ap.AdjustPosition();
                    b.DistanciaPosta = d;
                }
                Conferir(go, it, b, alvo);
                return;
            }
            var t = b.T;
            var p = t.position;
            if (Math.Abs(p.x - alvo.x) > 0.0005f || Math.Abs(p.y - alvo.y) > 0.0005f) t.position = new Vector3(alvo.x, alvo.y, p.z);
            b.MexiNoLugar = true;
        }

        static Vector3 NoPai(Transform pai, Vector3 mundo) => pai != null ? pai.InverseTransformPoint(mundo) : mundo;

        /* A REDE DE SEGURANÇA (12/11, ele ficou preso: o botão das Definições saiu da tela e não dava para voltar ao padrão).
           Se um botão movido fica mais de 1 s longe de onde devia aparecer, a conta é refeita; se nem assim (3 vezes seguidas),
           ele volta para o lugar do jogo e isso fica salvo. Nenhum botão pode sumir da tela por causa do mod. */
        static void Conferir(GameObject go, ItemDoHud it, Base b, Vector2 alvo)
        {
            if (!go.activeInHierarchy) { b.ForaDoLugar = 0f; return; }
            var p = b.T.position;
            float dx = p.x - alvo.x, dy = p.y - alvo.y;
            if (dx * dx + dy * dy < 0.6f * 0.6f) { b.ForaDoLugar = 0f; b.Recalibrou = 0; return; }
            b.ForaDoLugar += Time.unscaledDeltaTime;
            if (b.ForaDoLugar < 1f) return;
            b.ForaDoLugar = 0f;
            b.ChaveDaCalibracao = 0;   // refaz a conta no próximo quadro
            if (++b.Recalibrou < 3) return;
            b.Recalibrou = 0;
            it.Movido = false;
            Log.LogWarning("[hud] " + go.name + " nao ficava no lugar escolhido (" + p.x.ToString("0.0") + "," + p.y.ToString("0.0") + " em vez de " + alvo.x.ToString("0.0") + "," + alvo.y.ToString("0.0") + "): voltou para o lugar do jogo");
            Salvar();
        }

        // a conta que leva a distância da borda ao lugar na tela (medida no próprio jogo, uma vez por formato de tela), no
        // espaço do pai do botão (que anda junto com a câmera)
        static void Calibrar(AspectPosition ap, Base b)
        {
            bool calibrado = b.ChaveDaCalibracao != 0 && (b.Inclinacao.x != 0f || b.Inclinacao.y != 0f);
            float agora = Time.unscaledTime;
            if (calibrado && agora < b.ProxChave) return;   // a tela e a câmera são conferidas 1 vez por segundo, não todo quadro
            b.ProxChave = agora + 1f;
            int chave = Screen.width * 31 + Screen.height;
            try { var c = ap.parentCam != null ? ap.parentCam : Camera.main; if (c != null) chave = chave * 31 + (int)(c.orthographicSize * 100f); } catch { }
            if (chave == 0) chave = 1;
            if (calibrado && b.ChaveDaCalibracao == chave) return;
            var antes = ap.DistanceFromEdge;
            var d0 = b.DistanciaDoJogo;
            var t = b.T ?? ap.transform;
            var pai = b.T != null ? b.Pai : t.parent;
            ap.DistanceFromEdge = d0; ap.AdjustPosition(); var p0 = NoPai(pai, t.position);
            ap.DistanceFromEdge = d0 + new Vector3(1f, 0f, 0f); ap.AdjustPosition(); var px = NoPai(pai, t.position);
            ap.DistanceFromEdge = d0 + new Vector3(0f, 1f, 0f); ap.AdjustPosition(); var py = NoPai(pai, t.position);
            ap.DistanceFromEdge = antes; ap.AdjustPosition();
            b.DistRef = d0;
            b.LugarRef = p0;
            b.Inclinacao = new Vector2(px.x - p0.x, py.y - p0.y);
            b.ChaveDaCalibracao = chave;
        }

        // A lista de tarefas. O jogo a põe no lugar no Update de TODO quadro (ela desliza entre fechada e aberta) e refaz os dois
        // pontos dela a cada quadro (medido no jogo em 12/11: mexer neles não adianta, e o x aberto acumulava). Por isso ela anda
        // aqui, no LateUpdate, a partir do lugar que o jogo acabou de pôr: o desenho e o toque (a aba) vão juntos. Para os lados
        // ela anda na medida em que está aberta: fechada, a aba fica na beirada esquerda, como no jogo.
        static void PorDeslocamento(GameObject go, ItemDoHud it, Base b)
        {
            var tp = b.Tp;
            var t = b.T;
            var agora = t.localPosition;
            // o que está lá não é o que eu pus: foi o jogo que pôs, é a base nova
            if (!b.MexiNaLista || (agora - b.ListaPosta).sqrMagnitude > 0.0000001f) b.ListaDoJogo = agora;
            b.MexiNaLista = true;
            float dx = it.Movido ? it.X : 0f, dy = it.Movido ? it.Y : 0f;
            float aberta = tp != null ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tp.timer)) : 1f;
            var d = new Vector3(dx * aberta, dy, 0f);
            if (b.Pai != null) d = b.Pai.InverseTransformVector(d);
            var novo = new Vector3(b.ListaDoJogo.x + d.x, b.ListaDoJogo.y + d.y, b.ListaDoJogo.z);
            if ((agora - novo).sqrMagnitude > 0.0000001f) t.localPosition = novo;
            b.ListaPosta = t.localPosition;
        }

        static void DevolverLugar(GameObject go, Base b)
        {
            if (b.MexiNaLista)
            {
                // o lugar do jogo (no próximo Update ele põe de novo, de qualquer jeito)
                if ((go.transform.localPosition - b.ListaPosta).sqrMagnitude < 0.0000001f) go.transform.localPosition = b.ListaDoJogo;
                b.MexiNaLista = false;
            }
            var ap = go.GetComponent<AspectPosition>();
            if (b.MexiNaDistancia && ap != null)
            {
                ap.DistanceFromEdge = b.DistanciaDoJogo;
                ap.AdjustPosition();
            }
            if (b.MexiNoLugar)
            {
                // a grade dos botões arruma de novo (é o que ela faz quando um botão aparece)
                var grade = go.transform.parent != null ? go.transform.parent.GetComponent<GridArrange>() : null;
                if (grade != null) { try { grade.ArrangeChilds(); } catch { } }
            }
            b.MexiNaDistancia = false;
            b.MexiNoLugar = false;
        }

        static void Devolver(GameObject go)
        {
            IntPtr p = go.Pointer;
            if (_bases.TryGetValue(p, out var b))
            {
                if (b.TemEscala && go.transform.localScale == b.EscalaPosta) go.transform.localScale = b.EscalaDoJogo;
                b.TemEscala = false;
                DevolverLugar(go, b);
            }
            Opacidade(go, 1f);
        }

        // a cor do jogo vezes a opacidade; se o jogo trocou a cor desde a última vez (botão apagado, aceso), vale a dele
        /* (12/11, celular: "meu jogo fica travando") Antes, TODA peça mudada (de lugar, de tamanho ou só escura) tinha os desenhos
           percorridos a cada quadro, um GetComponent por desenho, mesmo com a opacidade em 100%: com o modo escuro "completo" isso
           era a janela do chat inteira (centenas de desenhos) todo quadro. Agora, em 100% e sem nada para devolver, não faz nada;
           e o tipo de cada desenho é visto uma vez (a lista renova de 2 em 2 s). */
        static void Opacidade(GameObject go, float opacidade)
        {
            IntPtr chave = go.Pointer;
            bool inteira = opacidade >= 0.999f;
            if (inteira && !_comOpacidade.Contains(chave)) return;
            if (!_desenhos.TryGetValue(chave, out var rs))
            {
                rs = new List<Pintavel>();
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;
                    var sr0 = r.TryCast<SpriteRenderer>();
                    var tm0 = sr0 == null ? r.GetComponent<TextMeshPro>() : null;
                    if (sr0 != null || tm0 != null) rs.Add(new Pintavel { P = r.Pointer, Sr = sr0, Tm = tm0 });
                }
                _desenhos[chave] = rs;
            }
            if (inteira) _comOpacidade.Remove(chave); else _comOpacidade.Add(chave);
            foreach (var d in rs)
            {
                var sr = d.Sr;
                var tm = d.Tm;
                if (sr == null && tm == null) continue;
                IntPtr p = d.P;
                Color atual = sr != null ? sr.color : tm.color;
                bool tenho = _corPosta.TryGetValue(p, out var posta);
                if (!tenho || atual != posta) _corDoJogo[p] = atual;
                var doJogo = _corDoJogo[p];
                var nova = new Color(doJogo.r, doJogo.g, doJogo.b, doJogo.a * opacidade);
                if (opacidade >= 0.999f)
                {
                    if (tenho && atual == posta && atual != doJogo) { if (sr != null) sr.color = doJogo; else tm.color = doJogo; }
                    _corPosta.Remove(p);
                    continue;
                }
                if (atual != nova) { if (sr != null) sr.color = nova; else tm.color = nova; }
                _corPosta[p] = nova;
            }
        }

        // ---------------- teclas ----------------

        static readonly Dictionary<string, KeyCode> _teclas = new Dictionary<string, KeyCode>();

        /// <summary>Do Update da Otimizacao: a tecla escolhida faz o mesmo que o toque no botão.</summary>
        internal static void Teclas()
        {
            if (EditorDoHud.Aberto || !HudManager.InstanceExists) return;
            HudManager h = null;
            foreach (var peca in Pecas)
            {
                if (!peca.TemTecla) continue;
                var it = Item(peca.Chave);
                if (it == null || string.IsNullOrEmpty(it.Tecla)) continue;
                if (!_teclas.TryGetValue(it.Tecla, out var k))
                {
                    if (!Enum.TryParse(it.Tecla, out k)) continue;
                    _teclas[it.Tecla] = k;
                }
                if (!Input.GetKeyDown(k)) continue;
                h ??= HudManager.Instance;
                try
                {
                    // digitando no chat, a tecla é letra, não botão
                    if (h.Chat != null && h.Chat.IsOpenOrOpening) return;
                    Clicar(peca, h);
                }
                catch (Exception e)
                {
                    if (_avisos.Add("tecla" + peca.Chave)) Log.LogWarning("[hud] tecla de " + peca.Chave + ": " + e.Message);
                }
            }
        }

        static void Clicar(Peca peca, HudManager h)
        {
            foreach (var go in peca.Objetos(h))
            {
                if (go == null || !go.activeInHierarchy) continue;
                var ab = go.GetComponent<ActionButton>();
                if (ab != null) { ab.DoClick(); Log.LogInfo("[hud] tecla: " + peca.Nome); return; }
                var pb = go.GetComponent<PassiveButton>();
                if (pb != null && pb.enabled) { pb.OnClick.Invoke(); Log.LogInfo("[hud] tecla: " + peca.Nome); return; }
            }
        }

        /// <summary>O nome curto de uma tecla para a tela ("Q", "Espaço", "Botão 1").</summary>
        internal static string NomeDaTecla(string tecla)
        {
            if (string.IsNullOrEmpty(tecla)) return "nenhuma";
            if (tecla.StartsWith("Alpha") && tecla.Length == 6) return tecla.Substring(5);
            if (tecla.StartsWith("Keypad")) return "Num " + tecla.Substring(6);
            if (tecla.StartsWith("JoystickButton")) return "Controle " + tecla.Substring(14);
            return tecla switch
            {
                "Space" => "Espaço",
                "LeftShift" => "Shift",
                "RightShift" => "Shift dir.",
                "LeftControl" => "Ctrl",
                "RightControl" => "Ctrl dir.",
                "LeftAlt" => "Alt",
                "Return" => "Enter",
                "UpArrow" => "Cima",
                "DownArrow" => "Baixo",
                "LeftArrow" => "Esquerda",
                "RightArrow" => "Direita",
                "Tab" => "Tab",
                _ => tecla,
            };
        }
    }
}
