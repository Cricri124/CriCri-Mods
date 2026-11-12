using System.Text;

namespace CriCriMods.Servidor
{
    // (11/11, revisão dos mods: "procurar erros ... vulnerabilidades") Tudo o que vem do servidor é dado, não é texto de confiança:
    // nome de amigo, nome de quem convidou, nome de opção. O TextMeshPro do jogo interpreta "<tag>" e "\n", e a janela de aviso do
    // jogo (a de desconexão, que o mod copia) tem um leitor próprio de "[...]" que já deu tela preta. Aqui o texto vira texto puro,
    // de tamanho limitado, antes de chegar em qualquer tela.
    //
    // Antes cada tela trocava "<" por um sinal (‹) que a fonte do jogo no celular não tem (saía um quadradinho) e não havia limite
    // de tamanho: um nome de 5.000 letras travava a lista. Um lugar só agora.
    internal static class TextoPuro
    {
        internal const int TamanhoDeNome = 40;
        internal const int TamanhoDeOpcao = 60;

        internal static string De(string s, int max = TamanhoDeNome)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var r = new StringBuilder(s.Length < max ? s.Length : max);
            foreach (char c in s)
            {
                if (r.Length >= max) break;
                switch (c)
                {
                    case '<': r.Append('('); break;      // sem tag do TextMeshPro
                    case '\\': r.Append('/'); break;     // sem "\n", "\t", "A"
                    case '[': r.Append('('); break;      // sem o leitor de links da janela de aviso
                    case ']': r.Append(')'); break;
                    default: r.Append(c < ' ' || c == (char)127 ? ' ' : c); break;   // quebra de linha e controle viram espaço
                }
            }
            // um par de UTF-16 cortado no meio pelo limite viraria um quadradinho
            if (r.Length > 0 && char.IsHighSurrogate(r[r.Length - 1])) r.Length--;
            return r.ToString();
        }
    }
}
