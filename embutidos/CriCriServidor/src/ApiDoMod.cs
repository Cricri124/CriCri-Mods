using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace CriCriMods.Servidor
{
    // For the other CriCri Mods plugins: a GET on https://cricribr.com.br/api/mod/* with the account's mod key, without the
    // key ever leaving this plugin. The CriCri Replay uses it (by reflection: the Replay also runs on the PC, where this
    // plugin does not exist) to bring the account's CRICRI BR matches into its folder (CriCri's decision, 08/11/2026:
    // on CRICRI BR the server records and the Replay plays). Routes agreed with the CRICRI BR session, live after v1:
    //   GET /api/mod/replays                    {"partidas":[{"id","inicio","mapa","duracaoMs","jogadores"}]} (ETag)
    //   GET /api/mod/replays/{id}/filme.ndjson  the match as a Replay film (v3, header "origem":"cricri-br")
    // Same rules as the room tab: Bearer only to cricribr.com.br, never in a URL, a log or an error; 429 = wait
    // Retry-After. BLOCKING: call it off the game's main thread.
    public static class ApiDoMod
    {
        static readonly Regex CaminhoValido = new Regex("^/api/mod/[A-Za-z0-9_\\-./]{1,200}$", RegexOptions.CultureInvariant);

        /// <summary>The player pasted the CRICRI BR key in the app (Conta).</summary>
        public static bool TemChave => SalaApi.TemChave;

        /// <summary>
        /// GET https://cricribr.com.br + caminho ("/api/mod/..."). Returns {status, etag, retryAfterSeconds, text}:
        /// status "0" = no connection or refused here (bad path, no key, waiting after a 429); text = the body on 200 when
        /// arquivo is null. With arquivo, a 200 body goes to that file (at most maxBytes; bigger = status "413", the file
        /// removed) and text is null. If-None-Match goes when etag is given (304 = nothing new).
        /// </summary>
        public static string[] Get(string caminho, string etag, string arquivo, long maxBytes)
        {
            var r = new string[] { "0", null, null, null };
            if (caminho == null || !CaminhoValido.IsMatch(caminho) || caminho.Contains("..")) return r;
            var http = SalaApi.ClienteSeTiver();
            if (http == null || SalaApi.Esperando) return r;
            try
            {
                using var pedido = new HttpRequestMessage(HttpMethod.Get, "https://cricribr.com.br" + caminho);
                if (!string.IsNullOrEmpty(etag)) pedido.Headers.TryAddWithoutValidation("If-None-Match", etag);
                using var resposta = http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                r[0] = ((int)resposta.StatusCode).ToString();
                r[1] = resposta.Headers.ETag?.ToString();
                SalaApi.EsperarSePedir(resposta);
                var depois = resposta.Headers.RetryAfter?.Delta;
                if (depois != null) r[2] = ((int)depois.Value.TotalSeconds).ToString();
                if (resposta.StatusCode != HttpStatusCode.OK) return r;
                long declarado = resposta.Content.Headers.ContentLength ?? -1;
                if (maxBytes > 0 && declarado > maxBytes) { r[0] = "413"; return r; }
                using var corpo = resposta.Content.ReadAsStream();
                if (arquivo == null)
                {
                    using var mem = new MemoryStream();
                    if (!Copiar(corpo, mem, maxBytes)) { r[0] = "413"; return r; }
                    r[3] = System.Text.Encoding.UTF8.GetString(mem.GetBuffer(), 0, (int)mem.Length);
                    return r;
                }
                bool coube;
                using (var saida = new FileStream(arquivo, FileMode.Create, FileAccess.Write, FileShare.None))
                    coube = Copiar(corpo, saida, maxBytes);
                if (!coube) { try { File.Delete(arquivo); } catch { } r[0] = "413"; }
                return r;
            }
            catch
            {
                if (arquivo != null) { try { if (File.Exists(arquivo)) File.Delete(arquivo); } catch { } }
                return new string[] { "0", null, null, null };
            }
        }

        // Copies at most max bytes (counted on what arrives, not on what the server declared). false = it went past max.
        static bool Copiar(Stream de, Stream para, long max)
        {
            var buf = new byte[64 * 1024];
            long total = 0;
            int n;
            while ((n = de.Read(buf, 0, buf.Length)) > 0)
            {
                total += n;
                if (max > 0 && total > max) return false;
                para.Write(buf, 0, n);
            }
            return true;
        }
    }
}
