using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CriCriMods
{
    /*
      (12/11) Peças de desenho feitas por código, para a barra do ping (BarraDoPing) e o editor do HUD (EditorDoHud).
      Nada aqui é imagem de fora nem gerada por IA: o símbolo do wifi é desenhado pixel a pixel (um ponto e três arcos, como
      o do próprio celular), e as caixas são um quadradinho branco esticado, com a letra do próprio jogo por cima.
    */
    internal static class Desenho
    {
        static Sprite _branco;
        static readonly Sprite[] _wifi = new Sprite[4];

        /// <summary>Um quadrado branco de 1 x 1 unidade (estica com a escala, pinta com a cor).</summary>
        internal static Sprite Branco()
        {
            if (_branco != null) return _branco;
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            t.SetPixels32(px);
            t.Apply(false, true);
            t.hideFlags = HideFlags.HideAndDontSave;
            _branco = Sprite.Create(t, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            _branco.hideFlags = HideFlags.HideAndDontSave;
            return _branco;
        }

        /*
          O wifi: um ponto embaixo e três arcos por cima, de 45 a 135 graus. "acesos" = quantos arcos ficam fortes (0 a 3);
          os outros ficam fracos (28%). Branco: a cor (verde, amarelo, vermelho) vem do SpriteRenderer.color.
          A base do símbolo fica no meio de baixo (pivô), e ele tem 1 unidade de altura.
        */
        internal static Sprite Wifi(int acesos)
        {
            acesos = Math.Clamp(acesos, 0, 3);
            if (_wifi[acesos] != null) return _wifi[acesos];
            const int L = 64, A = 48;
            const float cx = 31.5f, cy = 5f;
            var t = new Texture2D(L, A, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[L * A];
            float[,] arcos = { { 12f, 18.5f }, { 22.5f, 29f }, { 33f, 39.5f } };
            for (int y = 0; y < A; y++)
                for (int x = 0; x < L; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float r = (float)Math.Sqrt(dx * dx + dy * dy);
                    float alfa = 0f;
                    // o ponto
                    alfa = Math.Max(alfa, Borda(5.5f - r));
                    // os arcos: só a fatia de 45 a 135 graus (para cima), com a ponta arredondada pela borda suave
                    if (dy > 0f)
                    {
                        float ang = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
                        float dentroDoAngulo = Borda(Math.Min(ang - 45f, 135f - ang) * r * 0.0175f);
                        for (int k = 0; k < 3; k++)
                        {
                            float a = Math.Min(Borda(r - arcos[k, 0]), Borda(arcos[k, 1] - r)) * dentroDoAngulo;
                            if (a <= 0f) continue;
                            alfa = Math.Max(alfa, a * (k < acesos ? 1f : 0.28f));
                        }
                    }
                    px[y * L + x] = new Color32(255, 255, 255, (byte)Math.Round(Math.Clamp(alfa, 0f, 1f) * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            t.hideFlags = HideFlags.HideAndDontSave;
            var s = Sprite.Create(t, new Rect(0, 0, L, A), new Vector2(0.5f, cy / A), A);
            s.hideFlags = HideFlags.HideAndDontSave;
            _wifi[acesos] = s;
            return s;
        }

        // 0 fora, 1 dentro, com 1 pixel de transição (a borda sem serrilhado)
        static float Borda(float distanciaParaDentro) => Math.Clamp(distanciaParaDentro + 0.5f, 0f, 1f);

        /*
          (12/11, ele: "deixa mais bonito") Cantos redondos que não esticam: uma textura de 64 px com o canto de 16 px, cortada em
          nove pedaços (o modo "Sliced" do SpriteRenderer, o mesmo dos painéis do jogo). Cheio (caixa) ou só a borda (contorno).
          Tamanho certo em qualquer largura: SpriteRenderer.size, em unidades; o canto fica com 0,25 unidade.
        */
        static Sprite _cheio, _aro, _circulo;

        internal static Sprite Arredondado() => _cheio ??= CriarArredondado(false);
        internal static Sprite Aro() => _aro ??= CriarArredondado(true);

        static Sprite CriarArredondado(bool soABorda)
        {
            const int L = 64;
            const float raio = 16f, grossura = 3.2f;
            var t = new Texture2D(L, L, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[L * L];
            for (int y = 0; y < L; y++)
                for (int x = 0; x < L; x++)
                {
                    // distância até a borda do retângulo arredondado (positiva dentro)
                    float fx = x + 0.5f, fy = y + 0.5f;
                    float qx = Math.Max(raio - fx, fx - (L - raio)), qy = Math.Max(raio - fy, fy - (L - raio));
                    float fora = (float)Math.Sqrt(Math.Max(qx, 0f) * Math.Max(qx, 0f) + Math.Max(qy, 0f) * Math.Max(qy, 0f)) + Math.Min(Math.Max(qx, qy), 0f);
                    float dentro = raio - fora;                      // > 0 dentro da forma
                    float a = Borda(dentro - 0.5f);
                    if (soABorda) a = Math.Min(a, Borda(grossura - dentro));
                    px[y * L + x] = new Color32(255, 255, 255, (byte)Math.Round(Math.Clamp(a, 0f, 1f) * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            t.hideFlags = HideFlags.HideAndDontSave;
            var s = Sprite.Create(t, new Rect(0, 0, L, L), new Vector2(0.5f, 0.5f), L, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        /// <summary>Um círculo branco de 1 unidade (o pino das barras do editor).</summary>
        internal static Sprite Circulo()
        {
            if (_circulo != null) return _circulo;
            const int L = 48;
            var t = new Texture2D(L, L, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[L * L];
            for (int y = 0; y < L; y++)
                for (int x = 0; x < L; x++)
                {
                    float dx = x + 0.5f - L / 2f, dy = y + 0.5f - L / 2f;
                    float a = Borda(L / 2f - 1f - (float)Math.Sqrt(dx * dx + dy * dy));
                    px[y * L + x] = new Color32(255, 255, 255, (byte)Math.Round(a * 255f));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            t.hideFlags = HideFlags.HideAndDontSave;
            _circulo = Sprite.Create(t, new Rect(0, 0, L, L), new Vector2(0.5f, 0.5f), L);
            _circulo.hideFlags = HideFlags.HideAndDontSave;
            return _circulo;
        }

        /// <summary>Uma caixa de cantos redondos (cheia, ou só a borda) de w x h unidades, filha de `pai`.</summary>
        internal static SpriteRenderer Caixa(Transform pai, string nome, float w, float h, Color cor, int ordem, int camada, bool soABorda = false)
        {
            var go = new GameObject(nome);
            go.layer = camada;
            go.transform.SetParent(pai, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = soABorda ? Aro() : Arredondado();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = new Vector2(Math.Max(0.5f, w), Math.Max(0.5f, h));
            sr.color = cor;
            sr.sortingOrder = ordem;
            return sr;
        }

        internal static void Tamanho(SpriteRenderer sr, float w, float h)
        {
            var s = new Vector2(Math.Max(0.5f, w), Math.Max(0.5f, h));
            if (sr.size != s) sr.size = s;
        }

        /// <summary>Um retângulo cheio (w x h unidades), filho de `pai`, na camada e na ordem dadas.</summary>
        internal static SpriteRenderer Retangulo(Transform pai, string nome, float w, float h, Color cor, int ordem, int camada)
        {
            var go = new GameObject(nome);
            go.layer = camada;
            go.transform.SetParent(pai, false);
            go.transform.localScale = new Vector3(w, h, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Branco();
            sr.color = cor;
            sr.sortingOrder = ordem;
            return sr;
        }

        /// <summary>Quatro traços em volta de um retângulo de centro 0 (os filhos de `pai` com o nome dado).</summary>
        internal static SpriteRenderer[] Contorno(Transform pai, string nome, float w, float h, float grossura, Color cor, int ordem, int camada)
        {
            var r = new SpriteRenderer[4];
            r[0] = Retangulo(pai, nome + "C", w + grossura, grossura, cor, ordem, camada);
            r[1] = Retangulo(pai, nome + "B", w + grossura, grossura, cor, ordem, camada);
            r[2] = Retangulo(pai, nome + "E", grossura, h + grossura, cor, ordem, camada);
            r[3] = Retangulo(pai, nome + "D", grossura, h + grossura, cor, ordem, camada);
            AjustarContorno(r, w, h, grossura);
            return r;
        }

        internal static void AjustarContorno(SpriteRenderer[] r, float w, float h, float grossura)
        {
            if (r == null || r.Length < 4) return;
            r[0].transform.localPosition = new Vector3(0f, h / 2f, 0f);
            r[1].transform.localPosition = new Vector3(0f, -h / 2f, 0f);
            r[2].transform.localPosition = new Vector3(-w / 2f, 0f, 0f);
            r[3].transform.localPosition = new Vector3(w / 2f, 0f, 0f);
            r[0].transform.localScale = r[1].transform.localScale = new Vector3(w + grossura, grossura, 1f);
            r[2].transform.localScale = r[3].transform.localScale = new Vector3(grossura, h + grossura, 1f);
        }

        /// <summary>
        /// Um texto com a letra do jogo (copiada de `modelo`), centralizado em (0,0) do objeto, com `altura` unidades de
        /// letra. Fora de qualquer máscara (o material sem estêncil, como o contador de FPS).
        /// </summary>
        internal static TextMeshPro Texto(Transform pai, string nome, TextMeshPro modelo, float altura, Color cor, int ordem, int camada,
            TextAlignmentOptions alinhamento = TextAlignmentOptions.Center)
        {
            var go = new GameObject(nome);
            go.layer = camada;
            go.transform.SetParent(pai, false);
            var t = go.AddComponent<TextMeshPro>();
            if (modelo != null && modelo.font != null)
            {
                t.font = modelo.font;
                try
                {
                    var mat = modelo.font.material;
                    if (mat != null) t.fontSharedMaterial = mat;
                    mat = t.fontSharedMaterial;
                    if (mat != null && mat.HasProperty("_StencilComp") && mat.GetFloat("_StencilComp") != 8f)
                    {
                        mat = t.fontMaterial;
                        mat.SetFloat("_StencilComp", 8f);
                        if (mat.HasProperty("_Stencil")) mat.SetFloat("_Stencil", 0f);
                    }
                }
                catch { }
            }
            t.richText = true;
            t.enableWordWrapping = false;
            t.alignment = alinhamento;
            t.color = cor;
            t.fontSize = altura * Otimizacao.FatorFonte(t.font);
            t.rectTransform.sizeDelta = new Vector2(20f, altura * 1.6f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = ordem;
            return t;
        }

        /// <summary>Um texto do HUD que serve de modelo da letra (o do nome da sala, o do ping, ou qualquer um).</summary>
        internal static TextMeshPro ModeloDoHud(HudManager h)
        {
            try
            {
                if (h != null && h.roomTracker != null && h.roomTracker.text != null && h.roomTracker.text.font != null) return h.roomTracker.text;
                if (h != null)
                    foreach (var t in h.GetComponentsInChildren<TextMeshPro>(true))
                        if (t != null && t.font != null) return t;
            }
            catch { }
            return null;
        }

        /// <summary>A caixa (no mundo) que os desenhos ligados de um objeto ocupam; false se não há nenhum.</summary>
        internal static bool Caixa(GameObject go, out Bounds caixa, float maior = 6f)
        {
            caixa = default;
            bool tem = false;
            if (go == null || !go.activeInHierarchy) return false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                if (b.size.x < 0.001f && b.size.y < 0.001f) continue;
                if (b.size.x > maior || b.size.y > maior) continue;   // fundos e painéis inteiros não contam
                if (!tem) { caixa = b; tem = true; }
                else caixa.Encapsulate(b);
            }
            return tem;
        }

        // feito à mão: o ColorUtility pode não existir no jogo (a Unity tira o que o jogo não usa)
        internal static string Hex(Color c) =>
            "#" + ((int)Math.Round(Math.Clamp(c.r, 0f, 1f) * 255f)).ToString("X2") + ((int)Math.Round(Math.Clamp(c.g, 0f, 1f) * 255f)).ToString("X2")
            + ((int)Math.Round(Math.Clamp(c.b, 0f, 1f) * 255f)).ToString("X2");
    }
}
