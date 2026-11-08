using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods.Servidor
{
    // The "CRICRI BR" tab with the room options of the CRICRI BR server (the CriCri, 08/11: "no computador das config
    // da sala" and "quem não é o dono consegue ver nas configurações igual no jogo"):
    //  - the HOST edits it in the lobby computer (GameSettingMenu), a 4th tab under the game's three;
    //  - everyone else reads it in "Ver configurações" (LobbyViewSettingsPane), a 3rd tab beside the game's two.
    // Only in a lobby of the CriCri BR region. The option list comes from the server (each type becomes a control:
    // liga = Sim/Não, numero = −/+ in steps, lista = −/+ through the choices), so a new option needs no new mod.
    // Reading: every 5 s while one of the tabs is open, every 30 s in the lobby, never during a round (ETag/304).
    // Rows are copies of the game's own option rows (StringOption for the host, ViewSettingsInfoPanel for the
    // others) with the game's behaviour removed and ours wired, like CriCriOtimizacao's Settings tab.
    internal sealed class AbaSala : MonoBehaviour
    {
        public AbaSala(IntPtr ptr) : base(ptr) { }

        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        // ---------------- what the server said ----------------

        sealed class OpcaoSala
        {
            public string Id, Nome, Tipo, Motivo, Grupo;
            public double Min, Max = 100, Passo = 1;
            public bool Pode = true;
            public readonly List<KeyValuePair<string, string>> Escolhas = new List<KeyValuePair<string, string>>();
        }

        static readonly List<OpcaoSala> _opcoes = new List<OpcaoSala>();
        static readonly Dictionary<string, object> _valores = new Dictionary<string, object>();
        static bool _souHost, _temDados, _semSala;
        static string _etag, _aviso = "";
        static int _versaoDesenhada = -1, _versao;

        // ---------------- the two menus ----------------

        sealed class Painel
        {
            public bool Dono;
            public GameObject Botao, Conteudo;
            public Transform Origem;               // the game's container our content sits on
            public readonly List<GameObject> Linhas = new List<GameObject>();
            // the value text of each option row, to update it without rebuilding
            public readonly List<KeyValuePair<OpcaoSala, TextMeshPro>> Valores = new List<KeyValuePair<OpcaoSala, TextMeshPro>>();
            public int Desenhado = -2;
            public bool Visivel => Conteudo != null && Conteudo.activeSelf;
        }

        static GameSettingMenu _menuDono;
        static LobbyViewSettingsPane _menuVer;
        static Painel _painelDono, _painelVer;
        static readonly HashSet<string> _avisos = new HashSet<string>();
        float _proxLeitura, _gravarEm = -1f;

        internal static void MenuDonoAbriu(GameSettingMenu m) => _menuDono = m;
        internal static void MenuVerAbriu(LobbyViewSettingsPane p) => _menuVer = p;

        void Update()
        {
            while (SalaApi.Respostas.TryDequeue(out var acao)) Tentar("resposta", acao);
            bool aqui = false;
            Tentar("sala", () => aqui = NaSalaCricriBr());
            Tentar("dono", () => Garantir(ref _painelDono, true, aqui));
            Tentar("ver", () => Garantir(ref _painelVer, false, aqui));
            if (!aqui) return;

            float agora = Time.unscaledTime;
            bool aberto = (_painelDono != null && _painelDono.Visivel) || (_painelVer != null && _painelVer.Visivel);
            if (agora >= _proxLeitura)
            {
                _proxLeitura = agora + (aberto ? 5f : 30f);
                if (SalaApi.TemChave) SalaApi.Ler(_etag, Recebeu);
            }
            if (_gravarEm > 0 && agora >= _gravarEm)
            {
                _gravarEm = -1f;
                SalaApi.Gravar(false, new Dictionary<string, object>(_valores), codigo =>
                {
                    _aviso = codigo == 200 || codigo == 204 ? "Opções da sala salvas." : SalaApi.Aviso(codigo);
                    _etag = null;
                    _proxLeitura = 0f;
                    Redesenhar();
                });
            }
        }

        static bool NaSalaCricriBr()
        {
            if (SceneManager.GetActiveScene().name != "OnlineGame") return false;
            if (LobbyBehaviour.Instance == null || ShipStatus.Instance != null) return false;
            if (!DestroyableSingleton<ServerManager>.InstanceExists) return false;
            var regiao = DestroyableSingleton<ServerManager>.Instance.CurrentRegion;
            return regiao != null && regiao.Name == Regiao.Nome;
        }

        // ---------------- reading ----------------

        static void Recebeu(SalaApi.Leitura l)
        {
            if (l.Codigo == 304) return;
            if (l.Codigo != 200 || l.Corpo == null)
            {
                _temDados = false;
                _aviso = SalaApi.Aviso(l.Codigo);
                Redesenhar();
                return;
            }
            using (l.Corpo)
            {
                var raiz = l.Corpo.RootElement;
                _etag = l.Etag;
                if (raiz.TryGetProperty("sala", out var sala) && sala.ValueKind == JsonValueKind.Null)
                {
                    _semSala = true;
                    _temDados = false;
                    _aviso = "O servidor ainda não vê você nesta sala. Espere uns segundos.";
                    Redesenhar();
                    return;
                }
                _semSala = false;
                _versao = raiz.TryGetProperty("versao", out var v) && v.TryGetInt32(out int n) ? n : _versao + 1;
                _souHost = raiz.TryGetProperty("souHost", out var h) && h.ValueKind == JsonValueKind.True;
                _opcoes.Clear();
                if (raiz.TryGetProperty("opcoes", out var lista) && lista.ValueKind == JsonValueKind.Array)
                    foreach (var o in lista.EnumerateArray()) _opcoes.Add(LerOpcao(o));
                _valores.Clear();
                if (raiz.TryGetProperty("valores", out var valores) && valores.ValueKind == JsonValueKind.Object)
                    foreach (var p in valores.EnumerateObject()) _valores[p.Name] = Valor(p.Value);
                foreach (var o in _opcoes)
                    if (!_valores.ContainsKey(o.Id)) _valores[o.Id] = o.Tipo == "liga" ? (object)false : o.Tipo == "numero" ? o.Min : (object)(o.Escolhas.Count > 0 ? o.Escolhas[0].Key : "");
                _temDados = true;
                _aviso = _opcoes.Count == 0 ? "Esta sala não tem opções do CRICRI BR." : "";
                Redesenhar();
            }
        }

        static OpcaoSala LerOpcao(JsonElement o)
        {
            var r = new OpcaoSala
            {
                Id = Texto(o, "id"),
                Nome = Texto(o, "nome"),
                Tipo = Texto(o, "tipo"),
                Motivo = Texto(o, "motivo"),
                Grupo = Texto(o, "grupo"),
                Pode = !o.TryGetProperty("pode", out var pode) || pode.ValueKind != JsonValueKind.False,
            };
            if (o.TryGetProperty("min", out var min) && min.TryGetDouble(out double a)) r.Min = a;
            if (o.TryGetProperty("max", out var max) && max.TryGetDouble(out double b)) r.Max = b;
            if (o.TryGetProperty("passo", out var passo) && passo.TryGetDouble(out double c) && c > 0) r.Passo = c;
            if (o.TryGetProperty("escolhas", out var es) && es.ValueKind == JsonValueKind.Array)
                foreach (var e in es.EnumerateArray()) r.Escolhas.Add(new KeyValuePair<string, string>(Texto(e, "id"), Texto(e, "nome")));
            return r;
        }

        static string Texto(JsonElement o, string nome) =>
            o.ValueKind == JsonValueKind.Object && o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

        static object Valor(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String => v.GetString(),
            _ => null,
        };

        static string Mostrar(OpcaoSala o)
        {
            _valores.TryGetValue(o.Id, out var v);
            switch (o.Tipo)
            {
                case "liga": return v is bool b && b ? "Sim" : "Não";
                case "numero": return Convert.ToDouble(v ?? o.Min, CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"));
                default:
                    foreach (var e in o.Escolhas) if (Equals(e.Key, v as string)) return e.Value;
                    return v as string ?? "";
            }
        }

        // −/+ on the host's row: the value moves locally at once and is sent 0.6 s after the last tap.
        void Mudar(OpcaoSala o, int sentido)
        {
            if (!_souHost || !o.Pode) return;
            _valores.TryGetValue(o.Id, out var v);
            switch (o.Tipo)
            {
                case "liga":
                    _valores[o.Id] = !(v is bool b && b);
                    break;
                case "numero":
                    double atual = Convert.ToDouble(v ?? o.Min, CultureInfo.InvariantCulture);
                    _valores[o.Id] = Math.Max(o.Min, Math.Min(o.Max, Math.Round((atual + sentido * o.Passo) * 1000) / 1000));
                    break;
                default:
                    if (o.Escolhas.Count == 0) return;
                    int i = o.Escolhas.FindIndex(e => Equals(e.Key, v as string));
                    i = ((i < 0 ? 0 : i + sentido) % o.Escolhas.Count + o.Escolhas.Count) % o.Escolhas.Count;
                    _valores[o.Id] = o.Escolhas[i].Key;
                    break;
            }
            _gravarEm = Time.unscaledTime + 0.6f;
            AtualizarValores();
        }

        void SalvarPadrao()
        {
            if (!_temDados) return;
            SalaApi.Gravar(true, new Dictionary<string, object>(_valores), codigo =>
            {
                _aviso = codigo == 200 || codigo == 204 ? "Salvo como padrão das suas salas." : SalaApi.Aviso(codigo);
                Redesenhar();
            });
        }

        // ---------------- building the tabs ----------------

        static void Redesenhar()
        {
            if (_painelDono != null) _painelDono.Desenhado = -2;
            if (_painelVer != null) _painelVer.Desenhado = -2;
        }

        void Garantir(ref Painel painel, bool dono, bool aqui)
        {
            Component menu = dono ? (Component)_menuDono : _menuVer;
            if (menu == null || menu.gameObject == null) { painel = null; return; }
            if (painel == null || painel.Botao == null)
            {
                painel = dono ? MontarDono(_menuDono) : MontarVer(_menuVer);
                if (painel == null) return;
            }
            if (painel.Botao.activeSelf != aqui) painel.Botao.SetActive(aqui);
            if (!aqui) { if (painel.Visivel) Esconder(painel, dono); return; }
            if (painel.Visivel && painel.Desenhado != _versao + (_temDados ? 0 : 100000) + _aviso.GetHashCode()) Desenhar(painel);
        }

        Painel MontarDono(GameSettingMenu m)
        {
            var p = new Painel { Dono = true };
            var tab = m.GameSettingsTab;
            if (tab == null || m.RoleSettingsButton == null || m.GameSettingsButton == null) return Falha("menu do dono sem as pecas esperadas");
            p.Botao = CopiarBotao(m.RoleSettingsButton.gameObject, m.GameSettingsButton.transform, "CriCriBrAba", () => Mostrar(p, true));
            p.Origem = tab.settingsContainer;
            p.Conteudo = Conteudo(m.transform, p.Origem, "CriCriBrConteudoDono");
            foreach (var b in new[] { m.GamePresetsButton, m.GameSettingsButton, m.RoleSettingsButton })
                if (b != null) b.OnClick.AddListener((Action)(() => Esconder(p, true)));
            Log.LogInfo("[sala] aba CRICRI BR no computador do lobby (dono)");
            return p;
        }

        Painel MontarVer(LobbyViewSettingsPane v)
        {
            var p = new Painel { Dono = false };
            if (v.rolesTabButton == null || v.taskTabButton == null || v.settingsContainer == null) return Falha("painel de ver sem as pecas esperadas");
            p.Botao = CopiarBotao(v.rolesTabButton.gameObject, v.taskTabButton.transform, "CriCriBrAbaVer", () => Mostrar(p, false));
            p.Origem = v.settingsContainer;
            p.Conteudo = Conteudo(v.settingsContainer.parent, p.Origem, "CriCriBrConteudoVer");
            foreach (var b in new[] { v.taskTabButton, v.rolesTabButton })
                if (b != null) b.OnClick.AddListener((Action)(() => Esconder(p, false)));
            Log.LogInfo("[sala] aba CRICRI BR em Ver configuracoes");
            return p;
        }

        static Painel Falha(string motivo)
        {
            if (_avisos.Add(motivo)) Log.LogWarning("[sala] " + motivo);
            return null;
        }

        // A copy of a tab button of the game, one step below the last one, with "CRICRI BR" and our click.
        static GameObject CopiarBotao(GameObject modelo, Transform anterior, string nome, Action clique)
        {
            var go = Instantiate(modelo, modelo.transform.parent);
            go.name = nome;
            Vector3 passo = modelo.transform.localPosition - anterior.localPosition;
            go.transform.localPosition = modelo.transform.localPosition + passo;
            foreach (var t in go.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(t);
            foreach (var a in go.GetComponentsInChildren<AspectPosition>(true)) DestroyImmediate(a);
            foreach (var t in go.GetComponentsInChildren<TextMeshPro>(true)) t.text = "CRICRI BR";
            var pb = go.GetComponent<PassiveButton>();
            if (pb != null)
            {
                pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
                pb.OnClick.AddListener(clique);
            }
            go.SetActive(false);
            return go;
        }

        // An empty object exactly where the game's settings container is, so the game's own spacing applies.
        static GameObject Conteudo(Transform pai, Transform origem, string nome)
        {
            var go = new GameObject(nome);
            go.transform.SetParent(pai, false);
            go.transform.position = origem.position;
            go.transform.rotation = origem.rotation;
            Vector3 a = origem.lossyScale, b = pai.lossyScale;
            go.transform.localScale = new Vector3(a.x / (b.x == 0 ? 1 : b.x), a.y / (b.y == 0 ? 1 : b.y), a.z / (b.z == 0 ? 1 : b.z));
            go.layer = origem.gameObject.layer;
            go.SetActive(false);
            return go;
        }

        void Mostrar(Painel p, bool dono)
        {
            try
            {
                if (dono)
                {
                    var m = _menuDono;
                    if (m.GameSettingsTab != null) m.GameSettingsTab.gameObject.SetActive(false);
                    if (m.RoleSettingsTab != null) m.RoleSettingsTab.gameObject.SetActive(false);
                    if (m.PresetsTab != null) m.PresetsTab.gameObject.SetActive(false);
                }
                else if (p.Origem != null) p.Origem.gameObject.SetActive(false);
                p.Conteudo.SetActive(true);
                p.Desenhado = -2;
                _proxLeitura = 0f;   // read now
                Desenhar(p);
            }
            catch (Exception e) { Erro("mostrar", e); }
        }

        static void Esconder(Painel p, bool dono)
        {
            try
            {
                if (p.Conteudo != null) p.Conteudo.SetActive(false);
                if (!dono && p.Origem != null) p.Origem.gameObject.SetActive(true);
            }
            catch { }
        }

        // ---------------- rows ----------------

        void Desenhar(Painel p)
        {
            foreach (var l in p.Linhas) if (l != null) Destroy(l);
            p.Linhas.Clear();
            p.Valores.Clear();
            p.Desenhado = _versao + (_temDados ? 0 : 100000) + _aviso.GetHashCode();

            string topo = !SalaApi.TemChave
                ? "Ligue sua conta do CRICRI BR no app (Conta → Colar chave) pra ver as opções desta sala."
                : _aviso;
            int i = 0;
            if (!string.IsNullOrEmpty(topo)) Linha(p, i++, topo, null, null);
            if (SalaApi.TemChave && _temDados)
            {
                string grupo = null;
                foreach (var o in _opcoes)
                {
                    // the server sends them in order; a title row whenever the group changes ("Sala", "Criador")
                    if (!string.IsNullOrEmpty(o.Grupo) && o.Grupo != grupo)
                    {
                        grupo = o.Grupo;
                        Linha(p, i++, grupo.ToUpperInvariant(), null, null);
                    }
                    var esta = o;
                    string titulo = o.Pode ? o.Nome : o.Nome + " (" + (string.IsNullOrEmpty(o.Motivo) ? "travada" : o.Motivo) + ")";
                    Linha(p, i++, titulo, o, p.Dono && _souHost && o.Pode ? (Action<int>)(s => Mudar(esta, s)) : null);
                }
                if (p.Dono && _souHost) Linha(p, i++, "Salvar como padrão das minhas salas", null, s => SalvarPadrao(), "Salvar");
            }
        }

        void AtualizarValores()
        {
            foreach (var p in new[] { _painelDono, _painelVer })
            {
                if (p == null) continue;
                foreach (var par in p.Valores)
                    if (par.Value != null) par.Value.text = Mostrar(par.Key);
            }
        }

        // One row: the host's is a copy of the game's StringOption (title, value, −/+), the others' a copy of the
        // read-only info panel. Without an option it is a line of text (notice) or the "save as default" action.
        void Linha(Painel p, int indice, string titulo, OpcaoSala o, Action<int> mudar, string valorFixo = null)
        {
            GameObject go;
            TextMeshPro t, v;
            if (p.Dono)
            {
                var origem = _menuDono.GameSettingsTab.stringOptionOrigin;
                var linha = Instantiate(origem, p.Conteudo.transform);
                linha.SetClickMask(_menuDono.GameSettingsTab.ButtonClickMask);
                t = linha.TitleText;
                v = linha.ValueText;
                var menos = linha.MinusBtn;
                var mais = linha.PlusBtn;
                go = linha.gameObject;
                DestroyImmediate(linha);   // the game's behaviour would bind the row to a game option
                Ligar(menos, mudar != null ? () => mudar(-1) : (Action)null);
                Ligar(mais, mudar != null ? () => mudar(1) : (Action)null);
                go.transform.localPosition = new Vector3(GameOptionsMenu.START_POS_X, GameOptionsMenu.START_POS_Y - indice * GameOptionsMenu.SPACING_Y, -2f);
            }
            else
            {
                var info = Instantiate(_menuVer.infoPanelOrigin, p.Conteudo.transform);
                t = info.titleText;
                v = info.settingText;
                go = info.gameObject;
                go.transform.localPosition = new Vector3(LobbyViewSettingsPane.START_POS_X + (indice % 2) * LobbyViewSettingsPane.SPACING_X, LobbyViewSettingsPane.START_POS_Y - (indice / 2) * LobbyViewSettingsPane.SPACING_Y, -2f);
            }
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(tr);
            go.name = "CriCriBrLinha" + indice;
            go.SetActive(true);
            if (t != null) t.text = titulo;
            if (v != null) v.text = valorFixo ?? (o != null ? Mostrar(o) : "");
            if (o != null) p.Valores.Add(new KeyValuePair<OpcaoSala, TextMeshPro>(o, v));
            p.Linhas.Add(go);
        }

        static void Ligar(PassiveButton b, Action acao)
        {
            if (b == null) return;
            b.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            if (acao == null) { b.gameObject.SetActive(false); return; }
            b.OnClick.AddListener(acao);
        }

        static void Tentar(string oque, Action acao)
        {
            try { acao(); }
            catch (Exception e) { Erro(oque, e); }
        }

        static void Erro(string oque, Exception e)
        {
            if (_avisos.Add(oque + e.GetType().Name)) Log.LogWarning("[sala] " + oque + ": " + e.GetType().Name + ": " + e.Message);
        }
    }
}
