#if CRICRI_PC
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CriCriMods.Servidor
{
    // The PC half of the in-game linking in AmigosCricriBr: the same two calls the phone build has in LigarNaVerificacao, with the key kept in PcCofre
    // instead of the app. The key is never logged.
    internal static class LigarNaVerificacao
    {
        // POST without the key: {status, body}.
        internal static async Task<(int status, string corpo)> Postar(string rota, string json)
        {
            // (11/11, revisão) o cliente sem chave de sempre: não segue redirecionamento e a resposta tem teto
            using var http = SalaApi.ClienteSemChave();
            using var r = await http.PostAsync(SalaApi.Site + rota, new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            int status = (int)r.StatusCode;
            return (status, status == 200 ? await r.Content.ReadAsStringAsync().ConfigureAwait(false) : null);
        }

        // Main thread only: starts using the key now and keeps it for the next launches.
        internal static bool Guardar(string chave)
        {
            // (11/11, revisão) a mesma regra do COLAR (PcCofre.Limpar): o que não tem cara de chave não entra em uso nem no cofre
            if (!SalaApi.ChaveValida(chave)) { Plugin.Registro.LogWarning("[ligar] o servidor mandou uma chave fora do formato: nao usei"); return false; }
            SalaApi.TrocarChave(chave);
            bool guardou = PcCofre.Salvar(chave);
            if (guardou) Plugin.Registro.LogInfo("[ligar] conta ligada");
            else Plugin.Registro.LogWarning("[ligar] a chave vale nesta sessao, mas nao foi guardada");
            return guardou;
        }
    }
}
#endif
