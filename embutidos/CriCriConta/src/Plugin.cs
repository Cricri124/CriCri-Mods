using System;
using System.IO;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using GooglePlayGames;
using GooglePlayGames.Android;
using GooglePlayGames.BasicApi;
using HarmonyLib;

namespace CriCriMods.Conta
{
    // Hands the Google ID token saved by the app ("Entrar com o Google") to the game when it asks Google Play Games
    // for one. Never writes the token to the log.
    [BepInPlugin(Id, "CriCri Mods - conta", Versao)]
    public sealed class Plugin : BasePlugin
    {
        public const string Id = "br.com.cricribr.conta";
        public const string Versao = "0.2.1";
        internal static ManualLogSource Registro;

        public override void Load()
        {
            // (11/11, ele: "tão falando que demora muito pra carregar") O tempo deste Load vai para o registro, com a parte dos
            // ganchos separada: são dez ganchos do Harmony, a parte mais cara daqui. Com o LogOutput.log de um celular lento na
            // mão dá para saber quanto da demora é deste mod, sem adivinhar.
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            Registro = Log;
            // the account's outfit keeper (Roupa.cs): a per-frame object that survives scene changes
            Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<Roupa>();
            var objeto = new UnityEngine.GameObject("CriCriContaRoupa");
            UnityEngine.Object.DontDestroyOnLoad(objeto);
            objeto.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            objeto.AddComponent<Roupa>();
            var login = Login.Ler(out string motivo);
            Log.LogInfo("CriCri Mods conta " + Versao + ": " + (login != null
                ? "login do Google pronto (" + login.Descrever() + ")"
                : "sem login do Google (" + motivo + "); o jogo segue o caminho normal"));
            long antes = relogio.ElapsedMilliseconds;
            Ganchos.Instalar(new Harmony(Id));
            BepInEx.Logging.Logger.Listeners.Add(new OuvidoDoLogin());
            long total = relogio.ElapsedMilliseconds;
            Log.LogInfo("[conta] carregou em " + total + " ms (ganchos " + (total - antes) + ")");
        }
    }

    // The game logs Epic's answer to its platform login ("EOS Connect platform Login Callback <result>"). Listening
    // to it is how the plugin knows the token was refused, without hooking a method that takes a by-ref EOS struct.
    internal sealed class OuvidoDoLogin : ILogListener
    {
        public LogLevel LogLevelFilter => LogLevel.All;

        public void LogEvent(object sender, LogEventArgs e)
        {
            // (11/11, otimização) Este ouvido recebe TODA linha de registro do jogo, a sessão inteira. Só interessa enquanto um login
            // entregue espera a resposta da Epic: fora disso, sai antes de olhar o texto.
            if (!Ganchos.EsperandoResposta) return;
            if (e?.Source == null || e.Source.SourceName != "Unity") return;
            string texto = e.Data?.ToString();
            const string marca = "Connect platform Login Callback ";
            int i = texto == null ? -1 : texto.IndexOf(marca, StringComparison.Ordinal);
            if (i < 0) return;
            Ganchos.RespostaDoLogin(texto.Substring(i + marca.Length).Trim());
        }

        public void Dispose() { }
    }

    internal sealed class Login
    {
        // Among Us's Google client (the game's Play Games web client, and the one Innersloth's account site uses).
        public const string ClienteGoogle = "229679017402-kjalm0imngijq5q4vhesmsvqqgm2h5t7.apps.googleusercontent.com";
        // The app writes the file and sets this variable before the game starts (BootstrapActivity).
        public const string Variavel = "CRICRI_MODS_CONTA";
        // Don't hand over a token that expires in less than this.
        const long FolgaSegundos = 60;

        public string Token;
        public string Nome;
        public long Exp;
        // Google's id of the account (the outfit copy is kept per account)
        public string Sub;

        public string ValeAte => DateTimeOffset.FromUnixTimeSeconds(Exp).ToLocalTime().ToString("HH:mm");

        // (11/11, revisão: "vulnerabilidades") Só a inicial do nome vai para o registro. O nome é o da conta do Google da pessoa
        // (muitas vezes o nome inteiro, e boa parte dos jogadores é menor de idade), e o LogOutput.log é o arquivo que o jogador
        // manda em grupo de WhatsApp e no Discord quando pede ajuda. O nome inteiro continua só na nota que o app lê (pasta privada).
        public string Descrever() => (string.IsNullOrEmpty(Nome) ? "sem nome" : (char.IsLetterOrDigit(Nome[0]) ? Nome.Substring(0, 1) : "") + "***") + ", vale ate " + ValeAte;

        // Epic refused the token: drop it (the game then falls back to a guest, as with no login) and leave the app
        // a note with just the name and the reason, so the home screen can say why.
        public static void MarcarRecusado(Login login, string motivo)
        {
            try
            {
                string caminho = Caminho();
                if (caminho == null) return;
                // (11/11, revisão) o motivo vem de uma linha do registro do jogo: na nota do app vai só o começo dela, numa linha
                if (motivo == null) motivo = "";
                int corte = motivo.IndexOfAny(new[] { '\r', '\n' });
                if (corte >= 0) motivo = motivo.Substring(0, corte);
                if (motivo.Length > 80) motivo = motivo.Substring(0, 80);
                string nota = JsonSerializer.Serialize(new { nome = login.Nome ?? "", motivo, quando = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(caminho), "cricri-conta.recusado"), nota);
                File.Delete(caminho);
            }
            catch (Exception e)
            {
                Plugin.Registro.LogWarning("[conta] nao consegui apagar o login recusado: " + e.GetType().Name);
            }
        }

        static string Caminho()
        {
            string variavel = Environment.GetEnvironmentVariable(Variavel);
            if (!string.IsNullOrEmpty(variavel)) return variavel;
            // Fallback: .../Android/data/<package>/files/... -> /data/data/<package>/no_backup/cricri-conta.json
            string raiz = (Paths.BepInExRootPath ?? "").Replace('\\', '/');
            const string marca = "/Android/data/";
            int i = raiz.IndexOf(marca, StringComparison.Ordinal);
            if (i < 0) return null;
            string resto = raiz.Substring(i + marca.Length);
            int fim = resto.IndexOf('/');
            string pacote = fim > 0 ? resto.Substring(0, fim) : resto;
            return "/data/data/" + pacote + "/no_backup/cricri-conta.json";
        }

        // Read on every request: the player can sign in again (share the page) while the game is open.
        public static Login Ler(out string motivo)
        {
            try
            {
                string caminho = Caminho();
                if (caminho == null || !File.Exists(caminho)) { motivo = "nenhum login salvo no app"; return null; }

                string token, nome = null;
                using (var arquivo = JsonDocument.Parse(File.ReadAllText(caminho)))
                {
                    var r = arquivo.RootElement;
                    token = r.TryGetProperty("token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                    if (r.TryGetProperty("nome", out var n) && n.ValueKind == JsonValueKind.String) nome = n.GetString();
                }
                if (string.IsNullOrEmpty(token)) { motivo = "arquivo sem token"; return null; }

                if (!LerToken(token, out string iss, out string aud, out long exp, out string sub)) { motivo = "token ilegivel"; return null; }
                if (iss != "accounts.google.com" && iss != "https://accounts.google.com") { motivo = "token nao e do Google"; return null; }
                if (aud != ClienteGoogle) { motivo = "token de outro app do Google"; return null; }

                var login = new Login { Token = token, Nome = nome, Exp = exp, Sub = sub };
                if (exp - DateTimeOffset.UtcNow.ToUnixTimeSeconds() < FolgaSegundos)
                {
                    motivo = "o login venceu as " + login.ValeAte + "; entre de novo no app";
                    return null;
                }
                motivo = null;
                return login;
            }
            catch (Exception e)
            {
                motivo = "nao li o login: " + e.GetType().Name;
                return null;
            }
        }

        // A JWT is header.payload.signature; the payload is base64url JSON. Signature is checked by Epic, not here.
        static bool LerToken(string token, out string iss, out string aud, out long exp, out string sub)
        {
            iss = aud = sub = null;
            exp = 0;
            string[] partes = token.Split('.');
            if (partes.Length != 3) return false;
            string b = partes[1].Replace('-', '+').Replace('_', '/');
            switch (b.Length % 4)
            {
                case 2: b += "=="; break;
                case 3: b += "="; break;
                case 1: return false;
            }
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b)));
            var r = doc.RootElement;
            if (r.TryGetProperty("iss", out var i) && i.ValueKind == JsonValueKind.String) iss = i.GetString();
            if (r.TryGetProperty("aud", out var a) && a.ValueKind == JsonValueKind.String) aud = a.GetString();
            if (r.TryGetProperty("exp", out var e) && e.ValueKind == JsonValueKind.Number) e.TryGetInt64(out exp);
            if (r.TryGetProperty("sub", out var s) && s.ValueKind == JsonValueKind.String) sub = s.GetString();
            return exp > 0;
        }
    }

    internal static class Ganchos
    {
        static ManualLogSource Log => Plugin.Registro;

        public static void Instalar(Harmony h)
        {
            // The ones that matter: whoever answers the game's "give me the Google ID token".
            Prender(h, typeof(AndroidClient), "GetIdToken", nameof(TokenAndroidClient));
            Prender(h, typeof(PlayGamesPlatform), "GetIdToken", nameof(TokenPlataforma));
            Prender(h, typeof(AndroidTokenClient), "GetIdToken", nameof(TokenTokenClient));

            // Read-only: tell in the log which way the login went (no behavior change).
            Prender(h, typeof(EOSManager), "OnGoogleAuthenticated", nameof(StatusDoGoogle));
            Prender(h, typeof(EOSManager), "LoginWithCorrectPlatformImpl", nameof(LoginDaPlataforma));
            Prender(h, typeof(EOSManager), "BeginLoginFlowWithDeviceID", nameof(FoiProConvidado));
            Prender(h, typeof(EOSManager), "EndFinalPartsOfLoginFlowFullAccount", nameof(ContaCompleta), nameof(DepoisDaContaCompleta));
            Prender(h, typeof(EOSManager), "EndFinalPartsOfLoginFlowTempAccount", nameof(ContaTemporaria));
            Prender(h, typeof(EOSManager), "BeginMergeGuestAccountFlow", nameof(QuerJuntarConvidado));
            Prender(h, typeof(EOSManager), "OnAuthExpirationCallback", nameof(SessaoVaiVencer));
        }

        static void Prender(Harmony h, Type tipo, string metodo, string prefixo, string posfixo = null)
        {
            string nome = tipo.Name + "." + metodo;
            try
            {
                var alvo = AccessTools.Method(tipo, metodo);
                if (alvo == null) { Log.LogWarning("[conta] nao achei " + nome + " neste jogo"); return; }
                h.Patch(alvo, prefix: new HarmonyMethod(typeof(Ganchos), prefixo),
                    postfix: posfixo != null ? new HarmonyMethod(typeof(Ganchos), posfixo) : null);
                Log.LogInfo("[conta] gancho em " + nome + (posfixo != null ? " (antes e depois)" : ""));
            }
            catch (Exception e)
            {
                Log.LogWarning("[conta] gancho em " + nome + " falhou: " + e.GetType().Name + ": " + e.Message);
            }
        }

        static bool _avisouEntrega, _avisouSemLogin;
        // The login handed over that Epic hasn't answered yet, and how many times it was handed over without an answer.
        static Login _aguardando;
        static int _entregasSemResposta;
        static string _tokenRecusado;
        // On a refused token the game retries the platform login forever (seen 08/11: 169 tries, never reached the
        // menu). Stop handing it over after this many unanswered tries even if the answer never shows in the log.
        const int LimiteSemResposta = 5;

        // Returns false (skip the game's method) only when there is a valid saved login.
        static bool Entregar(string quem, ref string __result)
        {
            var login = Login.Ler(out string motivo);
            if (login == null)
            {
                if (!_avisouSemLogin) { _avisouSemLogin = true; Log.LogInfo("[conta] o jogo pediu o token do Google; " + motivo); }
                return true;
            }
            if (login.Token == _tokenRecusado) return true;
            if (_entregasSemResposta >= LimiteSemResposta)
            {
                Recusar(login, "sem resposta da Epic depois de " + _entregasSemResposta + " tentativas");
                return true;
            }
            __result = login.Token;
            _aguardando = login;
            _ultimoEntregue = login;
            _entregasSemResposta++;
            if (!_avisouEntrega)
            {
                _avisouEntrega = true;
                Log.LogInfo("[conta] entreguei ao jogo o login do Google do app (" + login.Descrever() + ") via " + quem);
            }
            return false;
        }

        // For the log listener: a handed-over login is still waiting for Epic's answer.
        internal static bool EsperandoResposta => _aguardando != null;

        // (11/11, revisão: "o login que cai para convidado") Respostas da Epic que NÃO falam do login: falam da rede ou do serviço
        // (nomes do enum Epic.OnlineServices.Result deste jogo). Antes, qualquer resposta que não fosse sucesso apagava o login
        // guardado e deixava para o app a nota "recusado": bastava abrir o jogo com a internet ruim para perder um login bom e ter
        // de entrar com o Google de novo. Agora, nesses casos, o mod só para de entregar ATÉ FECHAR O JOGO (o que evita a repetição
        // sem fim do login, a mesma trava de sempre) e o login guardado fica para a próxima abertura.
        static readonly string[] FalhasDeRede =
        {
            "NoConnection", "TimedOut", "NetworkDisconnected", "ServiceFailure", "TooManyRequests", "Canceled",
            "ConnectExternalServiceUnavailable", "ApplicationSuspended", "ShutdownInvoked",
        };

        // (11/11, revisão) Respostas que NÃO são a final: a Epic ainda vai responder de verdade na linha seguinte. Tratadas como final,
        // a resposta de verdade (até uma recusa) chegava depois e era jogada fora.
        static readonly string[] AindaRespondendo = { "OperationWillRetry", "AlreadyPending", "RequestInProgress" };
        // "UnexpectedError" e "UnrecognizedResponse" saíram da lista de cima: a Epic usa os dois como erro genérico, também para login
        // ruim. Como falha de rede, a pessoa ficaria entrando como convidada, sem aviso, a cada abertura. Voltam a contar como recusa
        // (o app avisa e pede o login de novo), que é o que acontecia antes.

        static bool Esta(string[] lista, string resultado)
        {
            foreach (string f in lista)
                if (resultado.StartsWith(f, StringComparison.Ordinal) && (resultado.Length == f.Length || !char.IsLetterOrDigit(resultado[f.Length]))) return true;
            return false;
        }

        static bool FalhaDeRede(string resultado)
        {
            foreach (string f in FalhasDeRede)
                if (resultado.StartsWith(f, StringComparison.Ordinal) && (resultado.Length == f.Length || !char.IsLetterOrDigit(resultado[f.Length]))) return true;
            return false;
        }

        // Epic's answer to the platform login, as the game logged it (ConnectResult name).
        public static void RespostaDoLogin(string resultado)
        {
            var login = _aguardando;
            if (login == null) return;
            if (resultado.StartsWith("Success", StringComparison.Ordinal))
            {
                Log.LogInfo("[conta] a Epic aceitou o login do app (" + login.Descrever() + ")");
                Respondido();
            }
            else if (resultado.StartsWith("InvalidUser", StringComparison.Ordinal))
            {
                // The Google account has no Among Us account yet: the game creates one (birthday and name).
                Log.LogInfo("[conta] login aceito; esse Google ainda nao tem conta do Among Us e o jogo vai criar uma");
                Respondido();
            }
            else if (Esta(AindaRespondendo, resultado))
            {
                Log.LogInfo("[conta] a Epic ainda esta respondendo ao login (" + resultado + "): espero a resposta final");
            }
            else if (FalhaDeRede(resultado))
            {
                Log.LogWarning("[conta] a Epic nao respondeu ao login (" + resultado + "): falha de rede, nao do login; paro de entregar ate fechar o jogo e o login guardado fica");
                _tokenRecusado = login.Token;
                Respondido();
            }
            else
            {
                Recusar(login, resultado);
            }
        }

        static void Respondido()
        {
            _aguardando = null;
            _entregasSemResposta = 0;
        }

        static void Recusar(Login login, string motivo)
        {
            Log.LogWarning("[conta] a Epic recusou o login do app (" + motivo + "); paro de entregar e o jogo segue como convidado");
            _tokenRecusado = login.Token;
            Respondido();
            Login.MarcarRecusado(login, motivo);
        }

        static bool TokenAndroidClient(ref string __result) => Entregar("AndroidClient.GetIdToken", ref __result);
        static bool TokenPlataforma(ref string __result) => Entregar("PlayGamesPlatform.GetIdToken", ref __result);
        static bool TokenTokenClient(ref string __result) => Entregar("AndroidTokenClient.GetIdToken", ref __result);

        // the login the game is on (for the outfit kept per Google account)
        static Login _ultimoEntregue;

        static void StatusDoGoogle(SignInStatus __0) => Log.LogInfo("[conta] Google Play Games respondeu " + __0);
        static void LoginDaPlataforma()
        {
            Roupa.Pausar();
            Log.LogInfo("[conta] o jogo comecou o login da plataforma (EOS)");
        }
        static void FoiProConvidado()
        {
            Roupa.Pausar();
            Log.LogInfo("[conta] o jogo foi pro login de convidado");
        }
        static void ContaCompleta()
        {
            Respondido();
            Log.LogInfo("[conta] o jogo terminou o login com conta completa");
        }
        static void DepoisDaContaCompleta() => Roupa.ContaCompleta(_ultimoEntregue?.Sub);
        static void ContaTemporaria()
        {
            Roupa.Pausar();
            Log.LogInfo("[conta] o jogo terminou o login como convidado");
        }
        static void QuerJuntarConvidado() => Log.LogWarning("[conta] o jogo abriu a juncao da conta de convidado");
        static void SessaoVaiVencer()
        {
            Roupa.Pausar();
            Log.LogInfo("[conta] a sessao EOS vai vencer (" + DateTime.Now.ToString("HH:mm") + "); o jogo vai pedir o token de novo");
        }
    }
}
