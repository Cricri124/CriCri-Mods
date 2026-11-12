using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CriCriMods
{
    /*
      (12/11, ele: "faça um modo escuro também, pra tudo ... e no chat também ter modo escuro ou deixar uma coisa escura, sem sobrar
      nada branco, um botão pra deixar modo escuro completo, só o chat, ou modo normal")

      O MODO ESCURO. O jogo desenha o chat e os botões de cima com imagens claras (o fundo branco do chat e dos balões, os quadrados
      brancos dos botões). Para escurecer sem redesenhar nada à mão, cada imagem do jogo ganha uma cópia escura, feita uma vez só:
      - SÓ a imagem que aparece clara (a imagem vezes a cor que o jogo pôs nela) é trocada: o claro vira escuro e o escuro vira
        claro (o fundo branco fica quase preto, o ícone preto fica claro). O que já é escuro fica como está (o botão "Enviar",
        a moldura dos botões, a película cinza da reunião): invertido, ele ficava claro (visto no jogo em 12/11);
      - as máscaras do jogo (o que recorta o chat e a reunião) nunca são mexidas: trocar a imagem delas mostrava coisas que o
        jogo esconde (visto no jogo em 12/11);
      - as cores (o verde, o vermelho, a cor dos jogadores) quase não mudam: só o que é cinza/branco/preto inverte;
      - a imagem do jogo não é legível direto: o pedaço que o desenho usa é copiado pela placa de vídeo (só ele, não o atlas
        inteiro, para não gastar memória no celular).
      Os textos escuros (o preto dos balões, do campo de digitar) viram claros. O boneco dos jogadores, os chapéus e as placas de
      nome (cosméticos) nunca são mexidos. Se o jogo troca a imagem ou a cor (botão aceso/apagado, balão novo), vale a dele, e ela
      ganha a cópia escura também. Desligar devolve a imagem e a cor do jogo.
    */
    internal static class Escuro
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        // sprite do jogo -> a cópia escura (null: não deu para copiar; aí o renderer só é escurecido pela cor, se for claro)
        static readonly Dictionary<IntPtr, Sprite> _copias = new Dictionary<IntPtr, Sprite>();
        static readonly Dictionary<IntPtr, float> _claridade = new Dictionary<IntPtr, float>();   // sprite do jogo -> o quanto é claro (0..1)

        sealed class Desenho_
        {
            public SpriteRenderer Sr;
            public Sprite DoJogo, Posta;
            public bool Decidido;
            public Color CorVista;            // a cor que o jogo tinha posto quando decidi (se ela muda, decido de novo)
            public bool PeloTom;              // clara mas sem cópia (não deu para copiar): escurecida pela cor
            public Color CorDoJogo, CorPosta;
        }

        sealed class Texto_
        {
            public TextMeshPro Tm;
            public Color DoJogo, Posta;
            public bool Mexido;
        }

        sealed class Raiz
        {
            public GameObject Go;
            public readonly List<Desenho_> Desenhos = new List<Desenho_>();
            public readonly List<Texto_> Textos = new List<Texto_>();
            public readonly HashSet<IntPtr> Conhecidos = new HashSet<IntPtr>();
            public float ProximaBusca;
        }

        static readonly Dictionary<IntPtr, Raiz> _raizes = new Dictionary<IntPtr, Raiz>();
        static readonly Color TextoClaro = new Color(0.92f, 0.93f, 0.95f, 1f);
        static readonly Color TomEscuro = new Color(0.16f, 0.18f, 0.22f, 1f);
        static int _avisos, _anotadas;

        /// <summary>Todo quadro, para cada coisa com o visual escuro (ligado) ou que já teve (desligado: devolve).</summary>
        internal static void Por(GameObject go, bool escuro)
        {
            if (go == null) return;
            IntPtr p = go.Pointer;
            _raizes.TryGetValue(p, out var r);
            if (!escuro)
            {
                if (r == null) return;
                Devolver(r);
                _raizes.Remove(p);
                return;
            }
            if (r == null) { r = new Raiz { Go = go }; _raizes[p] = r; }
            // (12/11, celular: "meu jogo fica travando") escondida (o chat fechado, a notificação apagada): não faz nada. No
            // quadro em que ela aparece, tudo é posto aqui no LateUpdate, antes do desenho: não pisca branco.
            if (!go.activeInHierarchy) return;
            float agora = Time.unscaledTime;
            if (agora >= r.ProximaBusca)
            {
                // de tempos em tempos, o que nasceu embaixo dela (um balão novo do chat, por exemplo); os desligados também, para
                // já estarem escuros quando aparecerem
                r.ProximaBusca = agora + 1.5f;
                Buscar(r);
            }
            for (int i = r.Desenhos.Count - 1; i >= 0; i--)
            {
                var d = r.Desenhos[i];
                if (d.Sr == null) { r.Desenhos.RemoveAt(i); continue; }
                PorDesenho(d);
            }
            for (int i = r.Textos.Count - 1; i >= 0; i--)
            {
                var t = r.Textos[i];
                if (t.Tm == null) { r.Textos.RemoveAt(i); continue; }
                PorTexto(t);
            }
        }

        /// <summary>Esquece tudo (HUD novo: os objetos antigos já foram destruídos). As cópias ficam (servem no HUD novo).</summary>
        internal static void Esquecer() => _raizes.Clear();

        static void Buscar(Raiz r)
        {
            foreach (var sr in r.Go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || !r.Conhecidos.Add(sr.Pointer)) continue;
                if (NaoMexe(sr, r.Go.transform)) continue;
                r.Desenhos.Add(new Desenho_ { Sr = sr });
            }
            foreach (var tm in r.Go.GetComponentsInChildren<TextMeshPro>(true))
            {
                if (tm == null || !r.Conhecidos.Add(tm.Pointer)) continue;
                if (DoJogador(tm.transform, r.Go.transform)) continue;
                r.Textos.Add(new Texto_ { Tm = tm });
            }
        }

        // o boneco, os cosméticos e a placa de nome dos jogadores ficam como o jogo desenha; e as máscaras e os desenhos com
        // material especial (o vidro e a cor da reunião, o tempo de recarga, o fundo esticado) também
        static bool NaoMexe(SpriteRenderer sr, Transform raiz)
        {
            try
            {
                var m = sr.sharedMaterial;
                var nome = m != null && m.shader != null ? m.shader.name : "";
                if (nome.Contains("MaskShader") || nome.Contains("PlayerShader") || nome.Contains("Cooldown") || nome.Contains("ReplaceColor") || nome.Contains("ColorStretch")) return true;
            }
            catch { return true; }
            if (DoJogador(sr.transform, raiz)) return true;
            // a placa de nome da reunião: só a padrão do jogo (votePlayerBase) escurece; as de cosmético ficam
            var pva = sr.GetComponentInParent<PlayerVoteArea>();
            if (pva != null && pva.Background != null && pva.Background.Pointer == sr.Pointer)
                return sr.sprite == null || !sr.sprite.name.StartsWith("votePlayerBase");
            return false;
        }

        static bool DoJogador(Transform t, Transform raiz)
        {
            for (var x = t; x != null; x = x.parent)
            {
                if (x.GetComponent<PoolablePlayer>() != null || x.GetComponent<CosmeticsLayer>() != null) return true;
                if (x == raiz) break;
            }
            return false;
        }

        const float Clara = 0.6f;   // de quanto de luz para cima a imagem (vezes a cor) conta como clara

        static void PorDesenho(Desenho_ d)
        {
            var sr = d.Sr;
            var s = sr.sprite;
            if (s == null) return;
            // o jogo trocou a imagem (ou é a primeira vez): a dele é a nova base
            if (d.Posta == null || s.Pointer != d.Posta.Pointer)
            {
                if (d.PeloTom && sr.color == d.CorPosta) sr.color = d.CorDoJogo;
                d.PeloTom = false;
                d.DoJogo = s;
                d.Posta = s;
                d.Decidido = false;
            }
            // a cor do jogo (se estou escurecendo pelo tom, a que ele tinha; se ele mudou por cima, a nova)
            var cor = sr.color;
            if (d.PeloTom && cor == d.CorPosta) cor = d.CorDoJogo;
            if (d.Decidido && cor == d.CorVista)
            {
                if (d.PeloTom && sr.color != d.CorPosta) sr.color = d.CorPosta;
                return;
            }
            d.Decidido = true;
            d.CorVista = cor;
            var copia = Copia(d.DoJogo);
            bool clara = Claridade(d.DoJogo) * Math.Min(Luz(cor), 1f) > Clara;
            Sprite alvo = clara && copia != null ? copia : d.DoJogo;
            if (sr.sprite.Pointer != alvo.Pointer) sr.sprite = alvo;
            d.Posta = alvo;
            // clara e sem cópia (não deu para copiar): escurece pela cor
            bool tom = clara && copia == null;
            if (tom)
            {
                d.CorDoJogo = cor;
                d.CorPosta = new Color(cor.r * TomEscuro.r, cor.g * TomEscuro.g, cor.b * TomEscuro.b, cor.a);
                sr.color = d.CorPosta;
            }
            else if (d.PeloTom && sr.color == d.CorPosta) sr.color = d.CorDoJogo;
            d.PeloTom = tom;
        }

        static void PorTexto(Texto_ t)
        {
            var c = t.Tm.color;
            if (t.Mexido && c == t.Posta) return;
            // a cor que está lá é do jogo: a escura vira clara (com a mesma transparência): cinza/preto vira quase branco, colorida
            // (o nome da cor do jogador: "Azul") fica da mesma cor, mais clara; a clara fica
            t.DoJogo = c;
            t.Mexido = true;
            if (Luz(c) < 0.45f)
            {
                t.Posta = Saturacao(c) < 0.35f ? new Color(TextoClaro.r, TextoClaro.g, TextoClaro.b, c.a) : Clarear(c);
                t.Tm.color = t.Posta;
            }
            else t.Posta = c;
        }

        static void Devolver(Raiz r)
        {
            foreach (var d in r.Desenhos)
            {
                if (d.Sr == null) continue;
                try
                {
                    if (d.DoJogo != null && d.Posta != null && d.Sr.sprite != null && d.Sr.sprite.Pointer == d.Posta.Pointer && d.Posta.Pointer != d.DoJogo.Pointer) d.Sr.sprite = d.DoJogo;
                    if (d.PeloTom && d.Sr.color == d.CorPosta) d.Sr.color = d.CorDoJogo;
                }
                catch { }
            }
            foreach (var t in r.Textos)
            {
                if (t.Tm == null) continue;
                try { if (t.Mexido && t.Tm.color == t.Posta) t.Tm.color = t.DoJogo; } catch { }
            }
        }

        // ---------------- a cópia escura de uma imagem ----------------

        static Sprite Copia(Sprite s)
        {
            IntPtr p = s.Pointer;
            if (_copias.TryGetValue(p, out var c))
            {
                if (ReferenceEquals(c, null)) return null;   // já tentei e não deu
                if (c != null) return c;                      // (destruída por fora: faz de novo)
            }
            try { c = CriarCopia(s); }
            catch (Exception e)
            {
                c = null;
                if (_avisos++ < 8) Log.LogWarning("[escuro] nao copiei '" + s.name + "': " + e.GetType().Name + ": " + e.Message);
            }
            _copias[p] = c;
            return c;
        }

        static float Claridade(Sprite s) => _claridade.TryGetValue(s.Pointer, out var v) ? v : 0f;

        static Sprite CriarCopia(Sprite s)
        {
            var tex = s.texture;
            if (tex == null) return null;
            int W = tex.width, H = tex.height;
            // o pedaço da imagem que o desenho usa (pelas coordenadas do próprio desenho)
            var uv = s.uv;
            if (uv == null || uv.Length == 0) return null;
            float minx = 1f, miny = 1f, maxx = 0f, maxy = 0f;
            for (int i = 0; i < uv.Length; i++)
            {
                var u = uv[i];
                minx = Math.Min(minx, u.x); miny = Math.Min(miny, u.y);
                maxx = Math.Max(maxx, u.x); maxy = Math.Max(maxy, u.y);
            }
            int x0 = Math.Max(0, (int)Math.Floor(minx * W)), y0 = Math.Max(0, (int)Math.Floor(miny * H));
            int x1 = Math.Min(W, (int)Math.Ceiling(maxx * W)), y1 = Math.Min(H, (int)Math.Ceiling(maxy * H));
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0 || w > 2048 || h > 2048) return null;

            var pixels = Ler(tex, x0, y0, w, h, W, H);
            if (pixels == null) return null;
            var nova = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = tex.filterMode, wrapMode = TextureWrapMode.Clamp };
            nova.hideFlags = HideFlags.HideAndDontSave;
            var px = pixels.GetPixels32();
            double soma = 0; int conta = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 8) continue;
                px[i] = Inverter(c, out float luz);
                soma += luz * c.a; conta += c.a;
            }
            float claridade = conta > 0 ? (float)(soma / conta) : 0f;
            _claridade[s.Pointer] = claridade;
            if (_anotadas++ < 40)
            {
                var meio = pixels.GetPixel(w / 2, h / 2);
                Log.LogInfo("[escuro] copia de '" + s.name + "' (" + W + "x" + H + ", pedaco " + w + "x" + h
                    + ", claridade " + claridade.ToString("0.00") + ", meio " + meio.r.ToString("0.00") + "/" + meio.g.ToString("0.00") + "/" + meio.b.ToString("0.00") + "/" + meio.a.ToString("0.00") + ")");
            }
            if (claridade <= Clara)
            {
                // escura: não precisa de cópia (fica a do jogo)
                UnityEngine.Object.Destroy(nova);
                UnityEngine.Object.Destroy(pixels);
                return null;
            }
            nova.SetPixels32(px);
            nova.Apply(false, true);
            UnityEngine.Object.Destroy(pixels);

            float ppu = s.pixelsPerUnit;
            // 1) a imagem num retângulo (o jeito de quase todas, e o único que serve para as fatiadas, com borda)
            Rect tr = default;
            bool temRet = false;
            try
            {
                if (!s.packed || s.packingMode != SpritePackingMode.Tight) { tr = s.textureRect; temRet = s.packingRotation == SpritePackingRotation.None; }
            }
            catch { temRet = false; }
            if (temRet)
            {
                var off = s.textureRectOffset;
                var piv = new Vector2((s.pivot.x - off.x) / Math.Max(1f, tr.width), (s.pivot.y - off.y) / Math.Max(1f, tr.height));
                var r = new Rect(tr.x - x0, tr.y - y0, tr.width, tr.height);
                if (r.x >= -0.5f && r.y >= -0.5f && r.xMax <= w + 0.5f && r.yMax <= h + 0.5f)
                {
                    float rx = Mathf.Clamp(r.x, 0, w), ry = Mathf.Clamp(r.y, 0, h);
                    r = new Rect(rx, ry, Math.Min(r.width, w - rx) - 0.001f, Math.Min(r.height, h - ry) - 0.001f);
                    var sp = Sprite.Create(nova, r, piv, ppu, 0, SpriteMeshType.FullRect, s.border);
                    sp.name = s.name + " (escuro)";
                    sp.hideFlags = HideFlags.HideAndDontSave;
                    return sp;
                }
            }
            // 2) recortada justa no atlas: a mesma forma, pelas coordenadas (só se não estiver girada)
            var verts = s.vertices;
            var tris = s.triangles;
            if (verts == null || tris == null || verts.Length != uv.Length) return null;
            var q = new Vector2[uv.Length];
            Vector2 pivPx = default;
            for (int i = 0; i < uv.Length; i++)
            {
                q[i] = new Vector2(Mathf.Clamp(uv[i].x * W - x0, 0, w), Mathf.Clamp(uv[i].y * H - y0, 0, h));
                var esperado = q[i] - new Vector2(verts[i].x, verts[i].y) * ppu;
                if (i == 0) pivPx = esperado;
                else if ((esperado - pivPx).sqrMagnitude > 1.5f) return null;   // girada ou espelhada no atlas
            }
            var sp2 = Sprite.Create(nova, new Rect(0, 0, w, h), new Vector2(pivPx.x / w, pivPx.y / h), ppu, 0, SpriteMeshType.Tight);
            var t2 = new ushort[tris.Length];
            for (int i = 0; i < tris.Length; i++) t2[i] = tris[i];
            sp2.OverrideGeometry(q, t2);
            sp2.name = s.name + " (escuro)";
            sp2.hideFlags = HideFlags.HideAndDontSave;
            return sp2;
        }

        // copia um pedaço da imagem pela placa de vídeo para uma imagem legível
        static Texture2D Ler(Texture tex, int x0, int y0, int w, int h, int W, int H)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var antes = RenderTexture.active;
            try
            {
                Graphics.Blit(tex, rt, new Vector2((float)w / W, (float)h / H), new Vector2((float)x0 / W, (float)y0 / H));
                RenderTexture.active = rt;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                t.Apply(false, false);
                return t;
            }
            finally
            {
                RenderTexture.active = antes;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // o claro vira escuro e o escuro vira claro, só no que é cinza; as cores ficam (quanto mais forte a cor, menos muda)
        static Color32 Inverter(Color32 c, out float luzOriginal)
        {
            float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float l = (max + min) / 2f;
            luzOriginal = l;
            float d = max - min;
            float sat = d < 0.0001f ? 0f : d / (1f - Math.Abs(2f * l - 1f) + 0.0001f);
            float peso = 1f - Suave(0.22f, 0.6f, sat);
            if (peso <= 0.001f) return c;
            // (o preto vira cinza-claro, 0,78, não branco: as bordas pretas dos campos e botões não viram linhas brancas)
            float alvo = 0.10f + 0.68f * (1f - l);
            float nl = l + (alvo - l) * peso;
            // mesma cor e saturação, com a luz nova
            float h = 0f;
            if (d > 0.0001f)
            {
                if (max == r) h = ((g - b) / d) % 6f;
                else if (max == g) h = (b - r) / d + 2f;
                else h = (r - g) / d + 4f;
                h /= 6f;
                if (h < 0f) h += 1f;
            }
            float cc = (1f - Math.Abs(2f * nl - 1f)) * Math.Min(1f, sat);
            float x = cc * (1f - Math.Abs((h * 6f) % 2f - 1f));
            float m = nl - cc / 2f;
            float rr, gg, bb;
            int setor = (int)(h * 6f) % 6;
            switch (setor)
            {
                case 0: rr = cc; gg = x; bb = 0; break;
                case 1: rr = x; gg = cc; bb = 0; break;
                case 2: rr = 0; gg = cc; bb = x; break;
                case 3: rr = 0; gg = x; bb = cc; break;
                case 4: rr = x; gg = 0; bb = cc; break;
                default: rr = cc; gg = 0; bb = x; break;
            }
            return new Color32(B(rr + m), B(gg + m), B(bb + m), c.a);
        }

        // a mesma cor, com luz suficiente para ler em cima do escuro
        static Color Clarear(Color c)
        {
            float r = c.r, g = c.g, b = c.b;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            if (max - min < 0.0001f) return new Color(TextoClaro.r, TextoClaro.g, TextoClaro.b, c.a);
            // sobe a luz misturando com branco até a luz percebida chegar a ~0,62
            for (float t = 0.1f; t <= 0.9f; t += 0.1f)
            {
                var x = Color.Lerp(c, Color.white, t);
                if (Luz(x) >= 0.62f) return new Color(x.r, x.g, x.b, c.a);
            }
            return new Color(TextoClaro.r, TextoClaro.g, TextoClaro.b, c.a);
        }

        static byte B(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
        static float Suave(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3f - 2f * t); }
        static float Luz(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        static float Saturacao(Color c)
        {
            float max = Math.Max(c.r, Math.Max(c.g, c.b)), min = Math.Min(c.r, Math.Min(c.g, c.b));
            return max < 0.0001f ? 0f : (max - min) / max;
        }
    }
}
