using UnityEngine;

namespace CriCriMods
{
    /*
      (11/11, noite, ele: "já olhou os mods de otimização etc no pc? ele fica estranho nas configurações pq ele foi
      feito pra mobile, e no pc é diferente, olha e corrija")

      O mod nasceu para o celular. No PC o jogo é outro em três pontos que o mod tocava:
      - as Definições: a aba Gráficos do PC tem a Resolução DELE, Ecrã inteiro, VSync e Aplicar, e a aba Geral tem o
        "Modo para streamers" do jogo. A grade feita para o celular caía por cima disso tudo (AbaCriCri.MontarPc dá
        às opções uma aba própria no PC);
      - a tela: no PC o tamanho e o ecrã inteiro são do jogador, pelos controles do próprio jogo. O mod não pede
        resolução nem taxa de tela no PC (Otimizacao.AplicarAgora);
      - a sombra: no PC ela já é a da placa de vídeo, e os anúncios não existem.

      Uma pergunta só, feita uma vez. O mesmo código compila para os dois (o interop do celular ou o do PC).
    */
    internal static class Plataforma
    {
        static int _pc = -1;

        /// <summary>O jogo está rodando fora do Android (PC da Steam, Epic, Microsoft).</summary>
        internal static bool Pc
        {
            get
            {
#if OTIM_FINGE_CELULAR
                // só num build de teste do PC: a tela do celular (a grade em Gráficos) aparece no PC, para conferir as páginas
                if (_pc < 0) _pc = 0;
#else
                if (_pc < 0) _pc = Application.platform == RuntimePlatform.Android ? 0 : 1;
#endif
                return _pc == 1;
            }
        }
    }
}
