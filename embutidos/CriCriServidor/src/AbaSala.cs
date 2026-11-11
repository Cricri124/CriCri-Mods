using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using AmongUs.GameOptions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
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
            public bool Dono, Aberto;
            public GameObject Botao, Conteudo;
            public Transform Origem;               // the game's container our content sits on
            public readonly List<GameObject> Linhas = new List<GameObject>();
            // the value text and the check mark of each option row, to update them without rebuilding
            public readonly List<KeyValuePair<OpcaoSala, TextMeshPro>> Valores = new List<KeyValuePair<OpcaoSala, TextMeshPro>>();
            public readonly List<KeyValuePair<OpcaoSala, SpriteRenderer>> Marcas = new List<KeyValuePair<OpcaoSala, SpriteRenderer>>();
            // The host's tab uses the game's own scrolling list: its rows are hidden while ours show, and live rows are the models.
            public readonly List<GameObject> DoJogo = new List<GameObject>();
            public float MaxDoJogo, XCabecalho, XLinha, ZLinha, TopoLista;
            public GameObject MoldeCabecalho, MoldeLiga, MoldeValor;
            public int Desenhado = -2;
            public bool Visivel => Aberto;
        }

        static GameSettingMenu _menuDono;
        static LobbyViewSettingsPane _menuVer;
        static Painel _painelDono, _painelVer;
        static readonly HashSet<string> _avisos = new HashSet<string>();
        float _proxLeitura;
        static float _gravarEm = -1f;

        internal static void MenuDonoAbriu(GameSettingMenu m) => _menuDono = m;
        internal static void MenuVerAbriu(LobbyViewSettingsPane p) => _menuVer = p;

        void Update()
        {
            while (SalaApi.Respostas.TryDequeue(out var acao)) Tentar("resposta", acao);
            bool aqui = false;
            Tentar("sala", () => aqui = NaSalaCricriBr());
            Tentar("dono", () => Garantir(ref _painelDono, true, aqui));
            Tentar("ver", () => Garantir(ref _painelVer, false, aqui));
            if (!aqui) { if (_gravarEm > 0f) { _gravarEm = -1f; _etag = null; } return; }   // a tap not sent: the next read must show the real value

            float agora = Time.unscaledTime;
            bool aberto = (_painelDono != null && _painelDono.Visivel) || (_painelVer != null && _painelVer.Visivel);
            if (agora >= _proxLeitura && !SalaApi.Ocupado)
            {
                _proxLeitura = agora + (aberto ? 5f : 30f);
                int geracao = _geracao;
                if (SalaApi.TemChave) SalaApi.Ler(_etag, l =>
                {
                    if (geracao == _geracao) { Recebeu(l); return; }
                    l.Corpo?.Dispose();
                    _proxLeitura = 0f;
                });
            }
            if (_gravarEm > 0 && agora >= _gravarEm && !_gravando)
            {
                _gravarEm = -1f;
                var mudou = SoOQueMudou();
                if (mudou.Count == 0) return;
                foreach (var p in mudou) _doServidor[p.Key] = p.Value;   // already on its way: tapping back has to count as a change
                _gravando = true;
                SalaApi.Gravar(false, mudou, (codigo, corpo) =>
                {
                    _gravando = false;
                    _geracao++;
                    Recado(AvisoDaGravacao(codigo, corpo, null));
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
            if (_gravando || _gravarEm > 0f) { l.Corpo?.Dispose(); return; }   // this answer is older than the taps being sent: the next read gets them
            if (l.Codigo == 304) return;
            if (l.Codigo != 200 || l.Corpo == null)
            {
                _temDados = false;
                _etag = null;
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
                _doServidor.Clear();
                foreach (var p in _valores) _doServidor[p.Key] = p.Value;
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
            if (o.TryGetProperty("min", out var min) && min.ValueKind == JsonValueKind.Number) r.Min = min.GetDouble();
            if (o.TryGetProperty("max", out var max) && max.ValueKind == JsonValueKind.Number) r.Max = max.GetDouble();
            if (o.TryGetProperty("passo", out var passo) && passo.ValueKind == JsonValueKind.Number && passo.GetDouble() > 0) r.Passo = passo.GetDouble();
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
                    string fora = v as string ?? "";   // the server can answer a value outside the choices (live.aviso = "mod")
                    return fora == "mod" ? "No CriCri CC" : fora;
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

        // Only what the host changed goes back: a locked option or a value outside the choices would make the server refuse the whole list (400/403).
        static readonly Dictionary<string, object> _doServidor = new Dictionary<string, object>();
        static bool _gravando;
        static int _geracao;

        static Dictionary<string, object> SoOQueMudou()
        {
            var d = new Dictionary<string, object>();
            foreach (var o in _opcoes)
            {
                if (!o.Pode || !_valores.TryGetValue(o.Id, out var v)) continue;
                _doServidor.TryGetValue(o.Id, out var antes);
                if (!Equals(antes, v)) d[o.Id] = v;
            }
            return d;
        }

        static Dictionary<string, object> ParaOPadrao()
        {
            var d = new Dictionary<string, object>();
            foreach (var o in _opcoes)
            {
                if (!o.Pode || !_valores.TryGetValue(o.Id, out var v)) continue;
                if (o.Tipo != "liga" && o.Tipo != "numero" && o.Escolhas.FindIndex(e => Equals(e.Key, v as string)) < 0) continue;
                d[o.Id] = v;
            }
            return d;
        }

        // 200 can still carry "recusas": [{ "id", "motivo" }], the options that did not go through while the rest did.
        static void Recado(string texto)
        {
            if (!string.IsNullOrEmpty(texto) && DestroyableSingleton<HudManager>.InstanceExists) DestroyableSingleton<HudManager>.Instance.Notifier.AddDisconnectMessage(texto);
        }

        static string AvisoDaGravacao(int codigo, string corpo, string certo)
        {
            if (codigo != 200 && codigo != 204) return SalaApi.Aviso(codigo);
            if (string.IsNullOrEmpty(corpo)) return certo;
            try
            {
                using var doc = JsonDocument.Parse(corpo);
                if (!doc.RootElement.TryGetProperty("recusas", out var recusas) || recusas.ValueKind != JsonValueKind.Array || recusas.GetArrayLength() == 0) return certo;
                var nomes = new List<string>();
                foreach (var r in recusas.EnumerateArray())
                {
                    string id = Texto(r, "id");
                    var op = _opcoes.Find(x => x.Id == id);
                    nomes.Add(op != null ? op.Nome : id);
                }
                return "Salvo, menos: " + string.Join(", ", nomes) + ".";
            }
            catch { return certo; }
        }

        void SalvarPadrao()
        {
            if (!_temDados) return;
            SalaApi.Gravar(true, ParaOPadrao(), (codigo, corpo) =>
            {
                Recado(AvisoDaGravacao(codigo, corpo, "Salvo como padrão das suas salas."));
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
            if (dono) { AjustarColuna(_menuDono, painel); LinhaDePredefinicao(_menuDono); }
#if CRICRI_PC
            if (dono) ArrumarQoL(_menuDono, painel);
#endif
            if (painel.Visivel && painel.Desenhado != _versao + (_temDados ? 0 : 100000) + _aviso.GetHashCode()) Desenhar(painel);
        }

        GameSettingMenu _colunaAjustada;

#if CRICRI_PC
        // On the PC the QoL mod puts AVANCADO and MODS on the row that became the CRICRI BR tab, and its option rows are copies of a game button whose
        // decoration only looks right inside the game's own pop-up. Nothing here runs on the phone, where that mod does not exist.
        const float FolgaDaFileira = 0.05f;
        Transform _filaDoAvancado;
        float _proxQoL;

        void ArrumarQoL(GameSettingMenu m, Painel p)
        {
            if (Time.unscaledTime < _proxQoL) return;
            _proxQoL = Time.unscaledTime + 0.4f;
            foreach (var b in m.GetComponentsInChildren<PassiveButton>(true))
            {
                if (b == null || b.name != "CriCri_Botao") continue;
                foreach (var sr in b.GetComponentsInChildren<SpriteRenderer>(true))
                    if (sr != null && sr.drawMode != SpriteDrawMode.Sliced && sr.enabled) sr.enabled = false;
            }

            var avancado = m.transform.Find("CriCri_AVANCADO");
            var mods = m.transform.Find("CriCri_MODS");
            if (avancado == null || mods == null || avancado == _filaDoAvancado || m.RoleSettingsButton == null) return;
            _filaDoAvancado = avancado;

            float cheia = LarguraCheia(m.RoleSettingsButton.gameObject), w = (cheia - 2f * FolgaDaFileira) / 3f;
            float cx = m.RoleSettingsButton.transform.localPosition.x, y = p.Botao.transform.localPosition.y;
            float letra = 0f;
            foreach (var t in avancado.GetComponentsInChildren<TextMeshPro>(true)) if (t != null) letra = t.fontSize * 0.82f;
            Encolher(avancado, w, cx - w - FolgaDaFileira, y, letra);
            Encolher(mods, w, cx, y, letra);
            Encolher(p.Botao.transform, w, cx + w + FolgaDaFileira, y, letra);
            Log.LogInfo("[sala] fileira do PC: AVANCADO, MODS e CRICRI BR lado a lado");
        }

        static float LarguraCheia(GameObject go)
        {
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr != null && sr.drawMode == SpriteDrawMode.Sliced && sr.size.x > 0.5f) return sr.size.x * go.transform.localScale.x;
            return 2.76f;
        }

        static void Encolher(Transform b, float largura, float x, float y, float letra)
        {
            var p = b.localPosition;
            b.localPosition = new Vector3(x, y, p.z);
            float k = Mathf.Abs(b.localScale.x) < 0.0001f ? 1f : b.localScale.x;
            foreach (var sr in b.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr != null && sr.drawMode == SpriteDrawMode.Sliced) sr.size = new Vector2(largura / k, sr.size.y);
            foreach (var c in b.GetComponentsInChildren<BoxCollider2D>(true))
                if (c != null) { c.size = new Vector2(largura / k, c.size.y); c.offset = Vector2.zero; }
            foreach (var t in b.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (t == null) continue;
                t.enableAutoSizing = false;
                if (letra > 0f) t.fontSize = letra;
                t.enableWordWrapping = false;
                t.alignment = TextAlignmentOptions.Center;
                var q = t.transform.localPosition;
                t.transform.localPosition = new Vector3(0f, q.y, q.z);
                t.rectTransform.sizeDelta = new Vector2(largura / k, t.rectTransform.sizeDelta.y);
            }
        }
#endif

        // CriCri, 09/11: only the big tabs on the side, the game's size and place: Game settings, Role settings and CRICRI BR. The presets became a row of the game settings.
        void AjustarColuna(GameSettingMenu m, Painel p)
        {
            if (_colunaAjustada == m) return;
            var a = m.GamePresetsButton;
            var b = m.GameSettingsButton;
            var c = m.RoleSettingsButton;
            if (a == null || b == null || c == null || p.Botao == null) return;
            _colunaAjustada = m;
            Vector3 lugarA = a.transform.localPosition, lugarB = b.transform.localPosition, lugarC = c.transform.localPosition;
            b.transform.localPosition = lugarA;
            c.transform.localPosition = lugarB;
            p.Botao.transform.localPosition = lugarC;
            a.gameObject.SetActive(false);
            if (m.PresetsTab != null && m.PresetsTab.gameObject.activeSelf) m.ChangeTab(1, false);
        }

        // ---------------- the presets, as a row under the map ----------------

        static readonly RulesPresets[] Predefinicoes = { RulesPresets.Standard, RulesPresets.StandardRoles, RulesPresets.Flashlight };
        static readonly StringNames[] NomesDasPredefinicoes = { StringNames.GameModeStandard, StringNames.GameModeStandardRoles, StringNames.GameModeFlashlight };
        GameOptionsMenu _predefNoMenu;
        TextMeshPro _predefValor;
        float _proxPredef;

        static string Frase(StringNames nome) => DestroyableSingleton<TranslationController>.Instance.GetString(nome, new Il2CppReferenceArray<Il2CppSystem.Object>(0));

        static string NomeDaPredefinicaoAtual()
        {
            var opcoes = GameOptionsManager.Instance.CurrentGameOptions;
            int i = Array.IndexOf(Predefinicoes, opcoes.RulesPreset);
            return Frase(i >= 0 ? NomesDasPredefinicoes[i] : StringNames.GameModeCustom);
        }

        // Between the map and "Impostors": everything from that header down moves one row. A live row is the model, with its mask and click area ready.
        void LinhaDePredefinicao(GameSettingMenu m)
        {
            var tab = m.GameSettingsTab;
            if (tab == null || tab.Children == null || tab.Children.Count == 0) return;
            if (_predefNoMenu != tab)
            {
                _predefNoMenu = tab;
                _predefValor = null;
                if (GameOptionsManager.Instance.CurrentGameOptions.GameMode != GameModes.Normal) return;   // Hide and Seek has one preset only
                StringOption modelo = null;
                foreach (var filho in tab.Children) { modelo = filho.TryCast<StringOption>(); if (modelo != null) break; }
                float topo = float.MinValue;
                var conteiner = tab.settingsContainer;
                for (int i = 0; i < conteiner.childCount; i++)
                    if (conteiner.GetChild(i).GetComponent<CategoryHeaderMasked>() != null) topo = Mathf.Max(topo, conteiner.GetChild(i).localPosition.y);
                if (modelo == null || topo == float.MinValue) return;
                float passo = GameOptionsMenu.SPACING_Y;
                for (int i = 0; i < conteiner.childCount; i++)
                {
                    var t = conteiner.GetChild(i);
                    if (t.localPosition.y <= topo + 0.01f) t.localPosition += new Vector3(0f, -passo, 0f);
                }
                var linha = Instantiate(modelo, conteiner);
                linha.name = "CriCriBrPredefinicao";
                linha.transform.localPosition = new Vector3(modelo.transform.localPosition.x, topo, modelo.transform.localPosition.z);
                var titulo = linha.TitleText;
                _predefValor = linha.ValueText;
                var menos = linha.MinusBtn;
                var mais = linha.PlusBtn;
                DestroyImmediate(linha);
                foreach (var tr in titulo.transform.parent.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(tr);
                titulo.text = Frase(StringNames.GamePresetsLabel);
                Ligar(menos, () => PedirPredefinicao(tab, -1));
                Ligar(mais, () => PedirPredefinicao(tab, 1));
                tab.scrollBar.SetYBoundsMax(tab.scrollBar.ContentYBounds.max + passo);
            }
            if (_predefValor == null || Time.unscaledTime < _proxPredef) return;
            _proxPredef = Time.unscaledTime + 0.25f;
            string nome = NomeDaPredefinicaoAtual();
            if (_predefValor.text != nome) _predefValor.text = nome;
        }

        // A preset replaces every setting of the room, so it keeps the game's confirmation.
        void PedirPredefinicao(GameOptionsMenu tab, int sentido)
        {
            int i = Array.IndexOf(Predefinicoes, GameOptionsManager.Instance.CurrentGameOptions.RulesPreset);
            i = i < 0 ? (sentido > 0 ? 0 : Predefinicoes.Length - 1) : ((i + sentido) % Predefinicoes.Length + Predefinicoes.Length) % Predefinicoes.Length;
            var escolhida = Predefinicoes[i];
            GameObject popup = null;
            popup = AmigosCricriBr.NovoPopup("CriCriBrPredefinicaoPopup", Frase(StringNames.GamePresetsPopupContinue), () =>
            {
                if (popup != null) Destroy(popup);
                if (tab != null) tab.ClickPresetButton(escolhida);
            }, Frase(StringNames.GamePresetsPopupBack), () => { if (popup != null) Destroy(popup); }, out var texto);
            if (popup == null) { tab.ClickPresetButton(escolhida); return; }
            texto.text = Frase(NomesDasPredefinicoes[i]) + "\n" + Frase(StringNames.GamePresetsPopupText);
            popup.SetActive(true);
        }

        Painel MontarDono(GameSettingMenu m)
        {
            var p = new Painel { Dono = true };
            var tab = m.GameSettingsTab;
            if (tab == null || m.RoleSettingsButton == null || m.GameSettingsButton == null) return Falha("menu do dono sem as pecas esperadas");
            p.Botao = CopiarBotao(m.RoleSettingsButton.gameObject, m.GameSettingsButton.transform, "CriCriBrAba", () => Mostrar(p, true));
            p.Origem = tab.settingsContainer;
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

        // A copy of a tab button of the game, one step below the last one, with our name and our click.
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
                pb.SelectButton(false);   // the model may be the tab that is selected right now
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
                    if (m.RoleSettingsTab != null) m.RoleSettingsTab.gameObject.SetActive(false);
                    if (m.PresetsTab != null) m.PresetsTab.gameObject.SetActive(false);
                    m.GameSettingsTab.gameObject.SetActive(true);
                    if (!p.Aberto) GuardarOJogo(p, m.GameSettingsTab);
                }
                else
                {
                    if (p.Origem != null) p.Origem.gameObject.SetActive(false);
                    p.Conteudo.SetActive(true);
                }
                p.Aberto = true;
                MarcarAba(p, dono);
                p.Desenhado = -2;
                _proxLeitura = 0f;   // read now
                Desenhar(p);
            }
            catch (Exception e) { Erro("mostrar", e); }
        }

        // The white outline of a tab is the game's SelectButton: ours takes it and theirs lose it, like GameSettingMenu.ChangeTab does.
        void MarcarAba(Painel p, bool dono)
        {
            MarcarNossa(p.Botao, true);
            var deles = dono
                ? new[] { _menuDono.GamePresetsButton, _menuDono.GameSettingsButton, _menuDono.RoleSettingsButton }
                : new[] { _menuVer.taskTabButton, _menuVer.rolesTabButton };
            foreach (var b in deles) if (b != null) b.SelectButton(false);
        }

        static void Esconder(Painel p, bool dono)
        {
            try
            {
                if (dono && p.Aberto) DevolverOJogo(p);
                p.Aberto = false;
                if (p.Conteudo != null) p.Conteudo.SetActive(false);
                if (!dono && p.Origem != null) p.Origem.gameObject.SetActive(true);
                MarcarNossa(p.Botao, false);
            }
            catch { }
        }

        // Our tab borrows the game's settings list: same scrolling, same mask, same click area. The game's rows are put away and come back as they were.
        static void GuardarOJogo(Painel p, GameOptionsMenu tab)
        {
            p.DoJogo.Clear();
            var conteiner = tab.settingsContainer;
            for (int i = 0; i < conteiner.childCount; i++)
            {
                var filho = conteiner.GetChild(i).gameObject;
                if (filho.activeSelf) { filho.SetActive(false); p.DoJogo.Add(filho); }
            }
            p.MaxDoJogo = tab.scrollBar.ContentYBounds.max;
            tab.scrollBar.ScrollToTop();
        }

        static void DevolverOJogo(Painel p)
        {
            foreach (var l in p.Linhas) if (l != null) Destroy(l);
            p.Linhas.Clear();
            p.Valores.Clear();
            p.Marcas.Clear();
            p.Desenhado = -2;
            foreach (var g in p.DoJogo) if (g != null) g.SetActive(true);
            p.DoJogo.Clear();
            var tab = _menuDono != null ? _menuDono.GameSettingsTab : null;
            if (tab == null) return;
            tab.scrollBar.SetYBoundsMax(p.MaxDoJogo);
            tab.scrollBar.ScrollToTop();
        }

        static void MarcarNossa(GameObject botao, bool marcada)
        {
            var pb = botao != null ? botao.GetComponent<PassiveButton>() : null;
            if (pb != null) pb.SelectButton(marcada);
        }

        // ---------------- rows ----------------

        void Desenhar(Painel p)
        {
            foreach (var l in p.Linhas) if (l != null) Destroy(l);
            p.Linhas.Clear();
            p.Valores.Clear();
            p.Marcas.Clear();
            p.Desenhado = _versao + (_temDados ? 0 : 100000) + _aviso.GetHashCode();

            string topo = !SalaApi.TemChave
#if CRICRI_PC
                ? "Ligue sua conta do CRICRI BR no botão CONTA BR do menu principal pra ver as opções desta sala."
#else
                ? "Sua conta do CRICRI BR liga sozinha em alguns segundos. Depois as opções desta sala aparecem aqui."
#endif
                : !_temDados && string.IsNullOrEmpty(_aviso) ? "Carregando as opções da sala..." : _aviso;
            if (p.Dono) { DesenharDono(p, topo); return; }

            int i = 0;
            if (!string.IsNullOrEmpty(topo)) Linha(p, i++, topo, null);
            if (!SalaApi.TemChave || !_temDados) return;
            string grupo = null;
            foreach (var o in _opcoes)
            {
                if (!string.IsNullOrEmpty(o.Grupo) && o.Grupo != grupo)
                {
                    grupo = o.Grupo;
                    Linha(p, i++, grupo.ToUpperInvariant(), null);
                }
                Linha(p, i++, o.Nome, o);
            }
        }

        // ---------------- the host's tab: the game's own list ----------------

        const string GrupoSala = "Sala", GrupoCriador = "Criador";

        // The server decides who receives the creator options and refuses the others; here an option that cannot be changed is simply not drawn.
        static bool SouCriador => _temDados && _souHost && _opcoes.Exists(o => o.Grupo == GrupoCriador && o.Pode);

        // What is used together stays together. An option the mod does not know yet still shows, under "OUTRAS".
        static readonly string[] Lobby = { "lobby.npcs", "lobby.rank", "lobby.mods", "tags.outros" };
        static readonly string[] QuemEntra = { "sala.trancada", "sala.escondida", "sala.discord", "sala.inscritos", "sala.subs" };
        static readonly string[] Live = { "live", "live.twitch", "live.youtube", "live.logados", "live.aviso" };

        // Live rows, never the prefabs: they already carry the list's mask, material and click area.
        static bool PrepararMoldes(Painel p, GameOptionsMenu tab)
        {
            if (p.MoldeCabecalho != null && p.MoldeValor != null) return true;
            float topo = float.MinValue;
            var conteiner = tab.settingsContainer;
            for (int i = 0; i < conteiner.childCount; i++)
            {
                var t = conteiner.GetChild(i);
                if (t.GetComponent<CategoryHeaderMasked>() == null || t.localPosition.y <= topo) continue;
                topo = t.localPosition.y;
                p.MoldeCabecalho = t.gameObject;
            }
            foreach (var filho in tab.Children)
            {
                if (p.MoldeLiga == null && filho.TryCast<ToggleOption>() != null) { p.MoldeLiga = filho.gameObject; }
                if (p.MoldeValor == null && filho.TryCast<StringOption>() != null) { p.MoldeValor = filho.gameObject; }
            }
            if (p.MoldeCabecalho == null || p.MoldeValor == null) return false;
            p.XCabecalho = p.MoldeCabecalho.transform.localPosition.x;
            p.XLinha = p.MoldeValor.transform.localPosition.x;
            p.ZLinha = p.MoldeValor.transform.localPosition.z;
            bool comPredefinicao = conteiner.Find("CriCriBrPredefinicao") != null;
            p.TopoLista = topo + (comPredefinicao ? GameOptionsMenu.SPACING_Y : 0f) + GameOptionsMenu.MAP_PICKER_HEIGHT;
            return true;
        }

        void DesenharDono(Painel p, string topo)
        {
            var tab = _menuDono.GameSettingsTab;
            if (!PrepararMoldes(p, tab)) { p.Desenhado = -2; return; }
            float y = p.TopoLista;
            if (!string.IsNullOrEmpty(topo)) AvisoDono(p, ref y, topo);
            if (SalaApi.TemChave && _temDados)
            {
                Secao(p, ref y, "SALA", _opcoes.FindAll(o => o.Grupo == GrupoSala));
                var grupos = new List<string>();
                foreach (var o in _opcoes)
                    if (!string.IsNullOrEmpty(o.Grupo) && o.Grupo != GrupoSala && o.Grupo != GrupoCriador && !grupos.Contains(o.Grupo)) grupos.Add(o.Grupo);
                foreach (var g in grupos) Secao(p, ref y, g.ToUpperInvariant(), _opcoes.FindAll(o => o.Grupo == g));
                if (SouCriador)
                {
                    var faltam = _opcoes.FindAll(o => o.Grupo == GrupoCriador);
                    Secao(p, ref y, "LOBBY", Tirar(faltam, Lobby));
                    Secao(p, ref y, "QUEM ENTRA", Tirar(faltam, QuemEntra));
                    Secao(p, ref y, "LIVE", Tirar(faltam, Live));
                    Secao(p, ref y, "OUTRAS", faltam);
                }
                if (_souHost) AcaoDono(p, ref y, "Salvar como padrão", "Salvar", SalvarPadrao);
            }
            tab.scrollBar.SetYBoundsMax(Mathf.Max(0f, -y - 1.65f));
        }

        static List<OpcaoSala> Tirar(List<OpcaoSala> faltam, string[] ids)
        {
            var achadas = new List<OpcaoSala>();
            foreach (var id in ids)
            {
                var o = faltam.Find(x => x.Id == id);
                if (o == null) continue;
                faltam.Remove(o);
                achadas.Add(o);
            }
            return achadas;
        }

        void Secao(Painel p, ref float y, string titulo, List<OpcaoSala> opcoes)
        {
            if (opcoes.Count == 0) return;
            var go = Copiar(p, p.MoldeCabecalho, p.XCabecalho, y);
            go.GetComponent<CategoryHeaderMasked>().Title.text = titulo;
            go.SetActive(true);
            y -= GameOptionsMenu.HEADER_HEIGHT;
            foreach (var o in opcoes) LinhaDono(p, ref y, o);
        }

        void AvisoDono(Painel p, ref float y, string texto)
        {
            var go = Copiar(p, p.MoldeCabecalho, p.XCabecalho, y);
            var c = go.GetComponent<CategoryHeaderMasked>();
            if (c.Background != null) c.Background.gameObject.SetActive(false);
            if (c.Divider != null) c.Divider.gameObject.SetActive(false);
            var t = c.Title;
            t.enableAutoSizing = false;
            t.fontSize = 2.4f;
            t.enableWordWrapping = true;
            t.rectTransform.sizeDelta = new Vector2(7.5f, 1f);
            t.text = texto;
            go.SetActive(true);
            y -= GameOptionsMenu.HEADER_HEIGHT * 1.6f;
        }

        static GameObject Copiar(Painel p, GameObject molde, float x, float y)
        {
            var go = Instantiate(molde, molde.transform.parent);
            go.name = "CriCriBrLinha" + p.Linhas.Count;
            go.transform.localPosition = new Vector3(x, y, molde.transform.localPosition.z);
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(tr);
            p.Linhas.Add(go);
            return go;
        }

        void LinhaDono(Painel p, ref float y, OpcaoSala o)
        {
            bool pode = _souHost && o.Pode;
            TextMeshPro titulo;
            if (o.Tipo == "liga" && p.MoldeLiga != null)
            {
                var go = Copiar(p, p.MoldeLiga, p.XLinha, y);
                var linha = go.GetComponent<ToggleOption>();
                titulo = linha.TitleText;
                var marca = linha.CheckMark;
                var botao = go.GetComponentInChildren<PassiveButton>(true);
                DestroyImmediate(linha);   // the game's behaviour would bind the row to a game option
                if (marca != null) { marca.enabled = Ligado(o); p.Marcas.Add(new KeyValuePair<OpcaoSala, SpriteRenderer>(o, marca)); }
                Clique(botao, pode ? () => Mudar(o, 1) : (Action)null);
                go.SetActive(true);
            }
            else
            {
                var go = Copiar(p, p.MoldeValor, p.XLinha, y);
                var linha = go.GetComponent<StringOption>();
                titulo = linha.TitleText;
                var valor = linha.ValueText;
                var menos = linha.MinusBtn;
                var mais = linha.PlusBtn;
                DestroyImmediate(linha);
                Ligar(menos, pode ? () => Mudar(o, -1) : (Action)null);
                Ligar(mais, pode ? () => Mudar(o, 1) : (Action)null);
                valor.text = Mostrar(o);
                p.Valores.Add(new KeyValuePair<OpcaoSala, TextMeshPro>(o, valor));
                go.SetActive(true);
            }
            titulo.text = o.Nome;
            if (!pode) titulo.color = new Color(1f, 1f, 1f, 0.55f);   // a locked option: no − and +, and its long reason does not fit in the row
            y -= GameOptionsMenu.SPACING_Y;
        }

        void AcaoDono(Painel p, ref float y, string titulo, string acao, Action clique)
        {
            var go = Copiar(p, p.MoldeValor, p.XLinha, y);
            var linha = go.GetComponent<StringOption>();
            var t = linha.TitleText;
            var valor = linha.ValueText;
            var menos = linha.MinusBtn;
            var mais = linha.PlusBtn;
            DestroyImmediate(linha);
            Ligar(menos, null);
            Ligar(mais, clique);
            BotaoNoValor(mais, valor);
            t.text = titulo;
            valor.text = acao;
            go.SetActive(true);
            y -= GameOptionsMenu.SPACING_Y;
        }

        static bool Ligado(OpcaoSala o) => _valores.TryGetValue(o.Id, out var v) && v is bool b && b;

        static void Clique(PassiveButton b, Action acao)
        {
            if (b == null) return;
            b.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            if (acao != null) { b.OnClick.AddListener(acao); return; }
            foreach (var c in b.GetComponents<Collider2D>()) c.enabled = false;
        }

        void AtualizarValores()
        {
            foreach (var p in new[] { _painelDono, _painelVer })
            {
                if (p == null) continue;
                foreach (var par in p.Valores)
                    if (par.Value != null) par.Value.text = Mostrar(par.Key);
                foreach (var par in p.Marcas)
                    if (par.Value != null) par.Value.enabled = Ligado(par.Key);
            }
        }

        // One row of "Ver configurações": a copy of the read-only info panel. Without an option it is a line of text.
        void Linha(Painel p, int indice, string titulo, OpcaoSala o)
        {
            var info = Instantiate(_menuVer.infoPanelOrigin, p.Conteudo.transform);
            var t = info.titleText;
            var v = info.settingText;
            var go = info.gameObject;
            go.transform.localPosition = new Vector3(LobbyViewSettingsPane.START_POS_X + (indice % 2) * LobbyViewSettingsPane.SPACING_X, LobbyViewSettingsPane.START_POS_Y - (indice / 2) * LobbyViewSettingsPane.SPACING_Y, -2f);
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) DestroyImmediate(tr);
            go.name = "CriCriBrLinha" + indice;
            go.SetActive(true);
            if (t != null) t.text = titulo;
            if (v != null) v.text = o != null ? Mostrar(o) : "";
            if (o != null) p.Valores.Add(new KeyValuePair<OpcaoSala, TextMeshPro>(o, v));
            p.Linhas.Add(go);
            ForaDaMascara(go);
        }

        // The rows are copies of the game's options, cut by the mask of the scrolling list. Ours are not inside that list, so with the cut on they drew nothing.
        static void ForaDaMascara(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var sprite = r.TryCast<SpriteRenderer>();
                if (sprite != null) sprite.maskInteraction = SpriteMaskInteraction.None;
                var texto = r.GetComponent<TextMeshPro>();
                var m = texto != null ? texto.fontMaterial : r.material;
                if (m.HasProperty("_MaskLayer")) m.SetFloat("_MaskLayer", 0f);
                if (m.HasProperty("_StencilComp")) m.SetFloat("_StencilComp", 8f);
                if (m.HasProperty("_Stencil")) m.SetFloat("_Stencil", 0f);
            }
        }

        // "Salvar" is an action, not a value: no − and +. The hidden + keeps the click and its area grows to cover the box.
        static void BotaoNoValor(PassiveButton mais, TextMeshPro valor)
        {
            var caixa = mais != null ? mais.GetComponent<BoxCollider2D>() : null;
            if (caixa == null || valor == null) return;
            foreach (var r in mais.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            var escala = mais.transform.lossyScale;
            caixa.offset = mais.transform.InverseTransformPoint(valor.transform.position);
            caixa.size = new Vector2(1.1f / escala.x, 0.43f / escala.y);
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
