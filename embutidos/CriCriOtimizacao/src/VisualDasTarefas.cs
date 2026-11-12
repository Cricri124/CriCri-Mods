using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriCriMods
{
    /*
      (12/11, ele: "conseguindo mudar o lugar da barra de task e lista de tarefas e mudar elas deixando mais bonita também")

      O visual "moderno" da lista e da barra de tarefas (escolhido no editor do HUD, um toque em "Visual"):
      - a lista: o fundo cinza quadrado do jogo (Background) e a aba ("Tarefas") viram caixas escuras de cantos redondos, com uma
        borda fina; o texto das tarefas é o do jogo, igual;
      - a barra "Total de tarefas concluídas": a moldura do jogo (progress_bar) vira uma caixa escura de cantos redondos por trás do
        verde, que continua sendo o do jogo (ele é quem mede).
      O desenho do jogo não é apagado: só deixa de ser desenhado (o colisor da aba continua o mesmo, o toque abre e fecha a lista
      como sempre). A caixa nova acompanha o tamanho e o lugar da peça do jogo a cada quadro (a lista muda de tamanho com o texto).
      "Do jogo" devolve tudo.
    */
    internal static class VisualDasTarefas
    {
        static readonly Color FundoEscuro = new Color(0.055f, 0.07f, 0.095f, 0.80f);
        static readonly Color BordaClara = new Color(1f, 1f, 1f, 0.22f);

        sealed class Pele
        {
            public SpriteRenderer DoJogo, Fundo, Borda;
            public float Folga;
        }

        static readonly Dictionary<IntPtr, List<Pele>> _peles = new Dictionary<IntPtr, List<Pele>>();

        /// <summary>Do LayoutDoHud, todo quadro, para a lista ("tarefas") e a barra ("barra"). visual 0 = do jogo.</summary>
        internal static void Por(GameObject go, string chave, int visual)
        {
            IntPtr p = go.Pointer;
            _peles.TryGetValue(p, out var peles);
            if (visual == 0)
            {
                if (peles == null) return;
                foreach (var pe in peles)
                {
                    if (pe.DoJogo != null) pe.DoJogo.enabled = true;
                    if (pe.Fundo != null) UnityEngine.Object.Destroy(pe.Fundo.gameObject);
                }
                _peles.Remove(p);
                return;
            }
            if (peles == null)
            {
                peles = Montar(go, chave);
                _peles[p] = peles;
            }
            foreach (var pe in peles)
            {
                if (pe.DoJogo == null || pe.Fundo == null) continue;
                bool ligado = pe.DoJogo.gameObject.activeInHierarchy;
                if (pe.DoJogo.enabled) pe.DoJogo.enabled = false;
                if (pe.Fundo.gameObject.activeSelf != ligado) pe.Fundo.gameObject.SetActive(ligado);
                if (!ligado) continue;
                // o tamanho e o lugar do desenho do jogo, em unidades do pai da caixa nova
                var b = pe.DoJogo.bounds;
                var pai = pe.Fundo.transform.parent;
                var escala = pai.lossyScale;
                float sx = Math.Max(0.0001f, Math.Abs(escala.x)), sy = Math.Max(0.0001f, Math.Abs(escala.y));
                var centro = pai.InverseTransformPoint(new Vector3(b.center.x, b.center.y, pe.DoJogo.transform.position.z));
                var lugar = new Vector3(centro.x, centro.y, centro.z + 0.01f);
                if ((pe.Fundo.transform.localPosition - lugar).sqrMagnitude > 0.000001f) pe.Fundo.transform.localPosition = lugar;
                float w = b.size.x / sx + pe.Folga, h = b.size.y / sy + pe.Folga;
                Desenho.Tamanho(pe.Fundo, w, h);
                if (pe.Borda != null) Desenho.Tamanho(pe.Borda, w, h);
            }
        }

        static List<Pele> Montar(GameObject go, string chave)
        {
            var l = new List<Pele>();
            if (chave == "tarefas")
            {
                var tp = go.GetComponent<TaskPanelBehaviour>();
                if (tp != null)
                {
                    if (tp.background != null) l.Add(NovaPele(tp.background, go.transform, 0.06f));
                    if (tp.tab != null) l.Add(NovaPele(tp.tab, go.transform, 0.04f));
                }
            }
            else
            {
                // a moldura da barra é o SpriteRenderer "Background" (progress_bar)
                foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                    if (sr != null && sr.name == "Background") { l.Add(NovaPele(sr, go.transform, 0.12f)); break; }
            }
            return l;
        }

        static Pele NovaPele(SpriteRenderer doJogo, Transform pai, float folga)
        {
            var pe = new Pele { DoJogo = doJogo, Folga = folga };
            pe.Fundo = Desenho.Caixa(pai, "CriCriFundo_" + doJogo.name, 1f, 1f, FundoEscuro, doJogo.sortingOrder, doJogo.gameObject.layer);
            pe.Fundo.sortingLayerID = doJogo.sortingLayerID;
            pe.Borda = Desenho.Caixa(pe.Fundo.transform, "Borda", 1f, 1f, BordaClara, doJogo.sortingOrder, doJogo.gameObject.layer, true);
            pe.Borda.sortingLayerID = doJogo.sortingLayerID;
            // a borda é filha da caixa: o tamanho dela é posto junto (em unidades da caixa, que não tem escala)
            return pe;
        }

        internal static string NomeDoVisual(int v) => LayoutDoHud.NomeDoVisual(v);
    }
}
