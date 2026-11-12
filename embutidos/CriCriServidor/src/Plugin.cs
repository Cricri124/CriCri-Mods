#if AMIGOS_SERVIDOR_TESTE || AMIGOS_PUBLICO
#define AMIGOS_REAL
#endif
using System;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriCriMods.Servidor
{
    // The CRICRI BR server in the game's region list (the production one only: the test server is for the team).
    // Data from the server's own definition (CRICRI-BR src/CricriBr.Contas/RegiaoDoJogo.cs): StaticHttpRegionInfo,
    // PingServer "cricribr.com.br", one server "http-1" at https://cricribr.com.br:443, no DTLS, no translation.
    // Only ADDS the region (the official ones stay); nothing here touches the game's HTTP calls or sends packets.
    // Split from CriCriOtimizacao on 08/11/2026, same code (confirmed working on the CriCri's phone that night).
    [BepInPlugin(Id, "CriCri Mods - servidor CRICRI BR", Versao)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "br.com.cricribr.servidor";
        public const string Versao = "0.4.1";
        internal static BepInEx.Logging.ManualLogSource Registro;

        public override void Load()
        {
            // (11/11, ele: "tão falando que demora muito pra carregar") O tempo deste Load vai para o registro, separado em
            // "tipos" (registrar as classes no jogo e criar o objeto) e "ganchos" (os dois do Harmony): com o LogOutput.log de um
            // celular lento na mão dá para saber se a demora é daqui, sem adivinhar.
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            Registro = Log;
            // First of all: the account's CRICRI BR mod key leaves the process environment (other mods share it).
            SalaApi.PegarChave();
            // Android rule: per-frame objects for the work (Unity messages always fire); Harmony only on Unity
            // messages (OnEnable), which are real functions, never inlined.
            ClassInjector.RegisterTypeInIl2Cpp<Regiao>();
            ClassInjector.RegisterTypeInIl2Cpp<AbaSala>();
#if AMIGOS_DE_MENTIRA || AMIGOS_REAL
            ClassInjector.RegisterTypeInIl2Cpp<AmigosCricriBr>();
#endif
#if (AMIGOS_SERVIDOR_TESTE || AMIGOS_PUBLICO) && !CRICRI_PC
            ClassInjector.RegisterTypeInIl2Cpp<LigarNaVerificacao>();
#endif
            var objeto = new GameObject("CriCriServidor");
            UnityEngine.Object.DontDestroyOnLoad(objeto);
            objeto.hideFlags = HideFlags.HideAndDontSave;
            objeto.AddComponent<Regiao>();
            objeto.AddComponent<AbaSala>();
#if AMIGOS_DE_MENTIRA || AMIGOS_REAL
            objeto.AddComponent<AmigosCricriBr>();
#endif
#if (AMIGOS_SERVIDOR_TESTE || AMIGOS_PUBLICO) && !CRICRI_PC
            objeto.AddComponent<LigarNaVerificacao>();
#endif

            long tipos = relogio.ElapsedMilliseconds;
            var h = new HarmonyLib.Harmony(Id);
            Prender(h, typeof(GameSettingMenu), nameof(DepoisMenuDono));
            Prender(h, typeof(LobbyViewSettingsPane), nameof(DepoisMenuVer));
            long total = relogio.ElapsedMilliseconds;
            Log.LogInfo("CriCri Mods servidor " + Versao + ": regiao " + Regiao.Nome + "; opcoes da sala "
                + (SalaApi.TemChave ? "com a chave da conta" : "sem chave (cole no app pra ver)")
                + "; carregou em " + total + " ms (tipos " + tipos + ", ganchos " + (total - tipos) + ")");
        }

        void Prender(HarmonyLib.Harmony h, Type tipo, string posfixo)
        {
            try
            {
                var alvo = HarmonyLib.AccessTools.Method(tipo, "OnEnable");
                if (alvo == null) { Log.LogWarning("[sala] nao achei " + tipo.Name + ".OnEnable"); return; }
                h.Patch(alvo, postfix: new HarmonyLib.HarmonyMethod(typeof(Plugin), posfixo));
            }
            catch (Exception e) { Log.LogWarning("[sala] gancho em " + tipo.Name + ": " + e.GetType().Name + ": " + e.Message); }
        }

        static void DepoisMenuDono(GameSettingMenu __instance) => AbaSala.MenuDonoAbriu(__instance);
        static void DepoisMenuVer(LobbyViewSettingsPane __instance) => AbaSala.MenuVerAbriu(__instance);
    }

    internal sealed class Regiao : MonoBehaviour
    {
        public Regiao(IntPtr ptr) : base(ptr) { }

        public const string Nome = "CriCri BR";
        const string Endereco = "cricribr.com.br";
#if AMIGOS_SERVIDOR_TESTE
        const ushort Porta = 22034;   // the test server: this build never reaches the catalog (mods-de-teste.js)
#else
        const ushort Porta = 443;
#endif
        bool _feito, _avisou;
        float _prox;

        void Update()
        {
            if (_feito) return;
            float agora = Time.unscaledTime;
            if (agora < _prox) return;
            _prox = agora + 1f;
            try { Garantir(); }
            catch (Exception e)
            {
                if (!_avisou) { _avisou = true; Plugin.Registro.LogWarning("[servidor] " + e.GetType().Name + ": " + e.Message); }
            }
        }

        // On the main menu, once per launch (AddOrUpdateRegion keeps it in the game's saved list too).
        void Garantir()
        {
            if (SceneManager.GetActiveScene().name != "MainMenu") return;
            if (!DestroyableSingleton<ServerManager>.InstanceExists) return;
            var servidores = DestroyableSingleton<ServerManager>.Instance;
            var regioes = servidores.AvailableRegions;
            if (regioes != null)
                for (int i = 0; i < regioes.Length; i++)
                    if (regioes[i] != null && regioes[i].Name == Nome)
                    {
                        // phone 08/11: the game keeps the region it saved, so a build for another port kept talking to the old server
                        // (11/11, revisão: "a região que some ou duplica", "vulnerabilidades") O endereço guardado também é conferido,
                        // não só a porta: uma região salva com este NOME e a porta certa, mas apontando para outro lugar (arquivo de
                        // regiões antigo, editado na mão ou escrito por outro programa), era aceita como boa, e o jogo mandava o login
                        // da pessoa para lá achando que era o CRICRI BR. Agora ela é trocada pela de verdade, uma vez, ao abrir o menu.
                        var salvos = regioes[i].Servers;
                        if (salvos != null && salvos.Length > 0 && salvos[0] != null && salvos[0].Port == Porta && EnderecoCerto(salvos[0].Ip)) { _feito = true; return; }
                        break;
                    }

            bool estavaNela = servidores.CurrentRegion != null && servidores.CurrentRegion.Name == Nome;
            var lista = new Il2CppReferenceArray<ServerInfo>(new[] { new ServerInfo("http-1", "https://" + Endereco, Porta, false) });
            var regiao = new StaticHttpRegionInfo(Nome, StringNames.NoTranslation, Endereco, lista, null);
            servidores.AddOrUpdateRegion(regiao.Cast<IRegionInfo>());
            if (estavaNela) servidores.SetRegion(regiao.Cast<IRegionInfo>());
            _feito = true;
            Plugin.Registro.LogInfo("[servidor] " + Nome + " acrescentado na lista de regioes");
        }

        // "https://cricribr.com.br", sem ligar para maiúscula nem para uma barra no fim
        static bool EnderecoCerto(string ip) =>
            ip != null && string.Equals(ip.TrimEnd('/'), "https://" + Endereco, StringComparison.OrdinalIgnoreCase);
    }
}
