using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace CriCriMods.Menu
{
    // Mockup pixels (1040x456, y down from the top-left) to the overlay root's local units.
    internal sealed class Grade
    {
        public float S = 0.01316f;   // world units per mockup pixel (6 units / 456 px on the phone)
        public float X0, Y0;         // root-local position of the mockup's top-left corner
        public float F = 1f;         // +1 when the camera looks toward +z: "in front" = smaller z

        // Center of a mockup rectangle; frente = how far in front of the root it sits (world units).
        public Vector3 Centro(Rect r, float frente) =>
            new Vector3(X0 + (r.x + r.width * 0.5f) * S, Y0 - (r.y + r.height * 0.5f) * S, -frente * F);

        public Vector2 Tamanho(Rect r) => new Vector2(Math.Max(0.001f, r.width * S), Math.Max(0.001f, r.height * S));
    }

    // How our pieces are drawn: the GameObject layer the menu's camera renders, and the sorting layer/order to use.
    // Everything of ours shares one order and is stacked by z, like the game's own menu (its screens sort by z too).
    internal sealed class Desenho
    {
        public int Camada;          // GameObject.layer
        public int LayerId;         // Renderer.sortingLayerID
        public int Ordem;           // Renderer.sortingOrder
        public TMP_FontAsset Fonte;
        public Material MaterialBotao;   // the game's button text material (outlined); null = the font's own
        public float Fator = 10f;   // font points per world unit (measured)
    }

    internal sealed class Placa
    {
        public GameObject Objeto;
        public SpriteRenderer Sr;
        public Rect Px;
        public float Frente;

        public static Placa Nova(Transform pai, string nome, Desenho d, Sprite sprite, Color cor, Rect px, float frente)
        {
            var go = new GameObject(nome);
            go.layer = d.Camada;
            go.transform.SetParent(pai, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.color = cor;
            sr.sortingLayerID = d.LayerId;
            sr.sortingOrder = d.Ordem;
            return new Placa { Objeto = go, Sr = sr, Px = px, Frente = frente };
        }

        public void Dispor(Grade g, Vector3 origem)
        {
            if (Objeto == null) return;
            Objeto.transform.localPosition = g.Centro(Px, Frente) - origem;
            Sr.size = g.Tamanho(Px);
        }
    }

    internal sealed class Rotulo
    {
        public TextMeshPro T;
        public Rect Px;
        public float Frente, TamPx;
        public bool Auto;
        Desenho _d;

        public static Rotulo Novo(Transform pai, string nome, Desenho d, string texto, float tamPx, Color cor, bool contorno,
            TextAlignmentOptions alinhamento, Rect px, float frente, bool quebra = false, bool auto = true)
        {
            // made at the scene root (active) and moved under the parent only at the end: under an inactive parent
            // (a button copy stays off until laid out) the TextMeshPro's Awake does not run, and setting the font then
            // throws NullReferenceException in LoadFontAsset (phone, 08/11: Painel and Hub never built)
            var go = new GameObject(nome);
            go.layer = d.Camada;
            var t = go.AddComponent<TextMeshPro>();
            t.font = d.Fonte;
            try
            {
                var mat = contorno && d.MaterialBotao != null ? d.MaterialBotao : d.Fonte.material;
                if (mat != null) t.fontSharedMaterial = mat;
                // our own copy of the material: no stencil (outside the game's masked panels) and a white face, so the
                // vertex color below is the color on screen (a button material may carry a dark face color)
                var inst = t.fontMaterial;
                if (inst != null)
                {
                    if (inst.HasProperty("_StencilComp")) inst.SetFloat("_StencilComp", 8f);
                    if (inst.HasProperty("_Stencil")) inst.SetFloat("_Stencil", 0f);
                    if (inst.HasProperty("_FaceColor")) inst.SetColor("_FaceColor", Color.white);
                }
            }
            catch { }
            t.richText = true;
            t.enableWordWrapping = quebra;
            t.alignment = alinhamento;
            t.color = cor;
            t.text = Pecas.Seguro(d.Fonte, texto);
            if (contorno)
            {
                try { t.outlineWidth = 0.18f; t.outlineColor = new Color32(5, 7, 15, 255); } catch { }
            }
            try
            {
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) { mr.sortingLayerID = d.LayerId; mr.sortingOrder = d.Ordem; }
            }
            catch { }
            go.transform.SetParent(pai, false);
            return new Rotulo { T = t, Px = px, Frente = frente, TamPx = tamPx, Auto = auto, _d = d };
        }

        public void Texto(string s)
        {
            if (T == null) return;
            s = Pecas.Seguro(_d.Fonte, s);
            if (T.text != s) T.text = s;
        }

        public void Dispor(Grade g, Vector3 origem)
        {
            if (T == null) return;
            T.transform.localPosition = g.Centro(Px, Frente) - origem;
            T.rectTransform.sizeDelta = g.Tamanho(Px);
            float fs = TamPx * g.S * _d.Fator;
            if (Auto)
            {
                T.enableAutoSizing = true;
                T.fontSizeMax = fs;
                T.fontSizeMin = fs * 0.45f;
            }
            else
            {
                T.enableAutoSizing = false;
                T.fontSize = fs;
            }
        }

        // Width the text wants at its size, in mockup pixels.
        public float LarguraPx(Grade g)
        {
            if (T == null || g.S <= 0f) return 0f;
            try
            {
                float fs = TamPx * g.S * _d.Fator;
                bool auto = T.enableAutoSizing;
                T.enableAutoSizing = false;
                T.fontSize = fs;
                var p = T.GetPreferredValues(T.text);
                T.enableAutoSizing = auto;
                return p.x / g.S;
            }
            catch { return Px.width; }
        }
    }

    // A clone of one of the game's menu buttons (its PassiveButton: click sound, the game's tap handling) with the
    // game's art hidden and our own rounded plate and text on it. The click runs `acao`; the original button's
    // prefab click is NOT kept (a fresh ButtonClickedEvent, like the Replay and the Options tab do).
    // No game icons: the menu's icons are drawn for its light buttons and would vanish on the dark plates.
    internal sealed class Botao
    {
        public GameObject Objeto;
        public PassiveButton Passivo;
        public BoxCollider2D Caixa;
        public Placa Fundo;
        public Rotulo Texto;
        public Rect Px;
        public float Frente;
        public string Nome;
        static readonly HashSet<string> _errosDeClique = new HashSet<string>();

        public static Botao Novo(PassiveButton modelo, Transform pai, string nome, Desenho d, Action acao)
        {
            var go = UnityEngine.Object.Instantiate(modelo.gameObject, pai);
            go.name = nome;
            go.SetActive(false);   // shown once laid out (the PassiveButton caches its z when enabled)
            // a positioning component copied from the model would pull the copy back onto the model
            foreach (var ap in go.GetComponentsInChildren<AspectPosition>(true)) UnityEngine.Object.DestroyImmediate(ap);
            // the translator would rewrite the text with the model's
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) UnityEngine.Object.DestroyImmediate(tr);
            // the game's art and texts stay in the clone (the PassiveButton points at them) but never draw
            var arte = new List<Renderer>();
            foreach (var t in go.GetComponentsInChildren<TextMeshPro>(true))
                try { t.text = ""; } catch { }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = false;
                arte.Add(r);
            }
            foreach (var c in go.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;

            var pb = go.GetComponent<PassiveButton>() ?? go.GetComponentInChildren<PassiveButton>(true);
            if (pb == null)
            {
                UnityEngine.Object.Destroy(go);
                throw new InvalidOperationException("o botao modelo " + modelo.name + " nao tem PassiveButton");
            }
            var caixa = pb.gameObject.GetComponent<BoxCollider2D>() ?? pb.gameObject.AddComponent<BoxCollider2D>();
            caixa.enabled = true;
            pb.Colliders = new Il2CppReferenceArray<Collider2D>(new Collider2D[] { caixa });
            try { pb.ClickMask = null; } catch { }   // the panel's mask: outside it the click would never register
            pb.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            string rotulo = nome;
            pb.OnClick.AddListener((Action)(() =>
            {
                try
                {
                    Plugin.Registro.LogInfo("[menu] clique: " + rotulo);
                    acao();
                }
                catch (Exception e)
                {
                    if (_errosDeClique.Add(rotulo + e.GetType().Name))
                        Plugin.Registro.LogWarning("[menu] clique em " + rotulo + ": " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                }
            }));

            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return new Botao { Objeto = go, Passivo = pb, Caixa = caixa, Nome = nome, _arte = arte };
        }

        List<Renderer> _arte;

        // The game's pressed/held art of the clone may be switched back on by its own button code: off again
        // (called every frame from LateUpdate while our layer is on screen).
        public void ApagarArteDoJogo()
        {
            if (_arte == null) return;
            for (int i = 0; i < _arte.Count; i++)
            {
                var r = _arte[i];
                if (r != null && r.enabled) r.enabled = false;
            }
        }

        public Botao ComFundo(Desenho d, Sprite sprite, Color cor)
        {
            Fundo = Placa.Nova(Objeto.transform, "CriCriFundo", d, sprite, cor, Px, Frente);
            return this;
        }

        public Botao ComTexto(Desenho d, string texto, float tamPx, Color cor, bool contorno, TextAlignmentOptions alinhamento, Rect px)
        {
            Texto = Rotulo.Novo(Objeto.transform, "CriCriTexto", d, texto, tamPx, cor, contorno, alinhamento, px, Frente + 0.01f);
            return this;
        }

        public void Dispor(Grade g)
        {
            if (Objeto == null) return;
            Vector3 centro = g.Centro(Px, Frente);
            Objeto.transform.localPosition = centro;
            Vector2 tam = g.Tamanho(Px);
            if (Fundo != null) { Fundo.Px = Px; Fundo.Frente = Frente; Fundo.Dispor(g, centro); }
            // the box lives on the PassiveButton's object (the clone's root for the game's buttons)
            if (Caixa != null)
            {
                var t = Caixa.transform;
                Vector3 escala = t.lossyScale;
                Caixa.size = new Vector2(tam.x / Math.Max(0.0001f, Math.Abs(escala.x)), tam.y / Math.Max(0.0001f, Math.Abs(escala.y)));
                Vector3 local = t.InverseTransformPoint(Objeto.transform.position);
                Caixa.offset = new Vector2(local.x, local.y);
            }
            if (Texto != null) Texto.Dispor(g, centro);
        }

        public void Mostrar(bool sim)
        {
            if (Objeto != null && Objeto.activeSelf != sim) Objeto.SetActive(sim);
        }
    }

    internal static class Pecas
    {
        static Sprite _cheio, _contorno, _quadrado;
        static readonly Dictionary<IntPtr, float> _fatores = new Dictionary<IntPtr, float>();

        public static Color Cor(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
        }

        // White rounded rectangle with a near-black border: the SpriteRenderer color tints the fill, the border stays dark.
        public static Sprite Cheio()
        {
            if (_cheio == null) _cheio = Redondo(true);
            return _cheio;
        }

        // Only the border (transparent inside), for the outlined buttons ("Menu clássico").
        public static Sprite Contorno()
        {
            if (_contorno == null) _contorno = Redondo(false);
            return _contorno;
        }

        public static Sprite Quadrado()
        {
            if (_quadrado != null) return _quadrado;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[16];
            for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            _quadrado = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f, 0, SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
            _quadrado.hideFlags = HideFlags.HideAndDontSave;
            return _quadrado;
        }

        // 64 px, corner radius 16 px, border 3.5 px; 80 px per unit = radius 0.2 and border 0.044 world units whatever the size.
        static Sprite Redondo(bool cheio)
        {
            const int L = 64;
            const float RAIO = 16f, BORDA = 3.5f, PPU = 80f;
            var tex = new Texture2D(L, L, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[L * L];
            for (int y = 0; y < L; y++)
                for (int x = 0; x < L; x++)
                {
                    float fx = x + 0.5f, fy = y + 0.5f;
                    // distance from the pixel center to the shape's edge, positive inside: to the corner arc in the
                    // four corners, to the nearest straight edge everywhere else
                    bool canto = (fx < RAIO || fx > L - RAIO) && (fy < RAIO || fy > L - RAIO);
                    float dentro;
                    if (canto)
                    {
                        float cx = Mathf.Clamp(fx, RAIO, L - RAIO), cy = Mathf.Clamp(fy, RAIO, L - RAIO);
                        float dx = fx - cx, dy = fy - cy;
                        dentro = RAIO - Mathf.Sqrt(dx * dx + dy * dy);
                    }
                    else dentro = Math.Min(Math.Min(fx, L - fx), Math.Min(fy, L - fy));
                    float alfa = Mathf.Clamp01(dentro + 0.5f);
                    float miolo = Mathf.Clamp01(dentro - BORDA + 0.5f);        // 1 = fill, 0 = border
                    byte a = (byte)Mathf.RoundToInt(alfa * 255f);
                    if (cheio)
                    {
                        byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(5f, 255f, miolo));
                        byte gg = (byte)Mathf.RoundToInt(Mathf.Lerp(7f, 255f, miolo));
                        byte b = (byte)Mathf.RoundToInt(Mathf.Lerp(15f, 255f, miolo));
                        px[y * L + x] = new Color32(r, gg, b, a);
                    }
                    else
                    {
                        px[y * L + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alfa * (1f - miolo) * 255f));
                    }
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            var s = Sprite.Create(tex, new Rect(0, 0, L, L), new Vector2(0.5f, 0.5f), PPU, 0, SpriteMeshType.FullRect,
                new Vector4(RAIO + 2f, RAIO + 2f, RAIO + 2f, RAIO + 2f));
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        // The game's font has Portuguese accents; the middle dot is checked (a missing glyph draws a box).
        public static string Seguro(TMP_FontAsset fonte, string s)
        {
            if (string.IsNullOrEmpty(s) || fonte == null || s.IndexOf('·') < 0) return s;
            try
            {
                if (!fonte.HasCharacter('·', true, true)) return s.Replace('·', '-');
            }
            catch { }
            return s;
        }

        // Font points per world unit for a 3D TextMeshPro with this font (same measure as CriCriOtimizacao's FatorFonte).
        public static float FatorFonte(TMP_FontAsset fonte)
        {
            IntPtr chave = fonte != null ? fonte.Pointer : IntPtr.Zero;
            if (_fatores.TryGetValue(chave, out float fator)) return fator;
            fator = 10f;
            GameObject go = null;
            try
            {
                go = new GameObject("CriCriMenu_medida");
                var t = go.AddComponent<TextMeshPro>();
                t.font = fonte;
                t.enableWordWrapping = false;
                t.fontSize = 100f;
                var tam = t.GetPreferredValues("Hg");
                if (tam.y > 0.0001f) fator = 100f / (tam.y * 0.8f);
            }
            catch { }
            if (go != null) UnityEngine.Object.Destroy(go);
            _fatores[chave] = fator;
            return fator;
        }

        // The largest sprite of a button (its background): the button's box on screen.
        public static bool Caixa(GameObject go, out Bounds caixa)
        {
            caixa = default;
            if (go == null) return false;
            float area = 0f;
            bool achou = false;
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null) continue;
                var b = sr.bounds;
                float a = b.size.x * b.size.y;
                if (a > area) { area = a; caixa = b; achou = true; }
            }
            return achou;
        }

        public static void Camada(GameObject go, int camada)
        {
            if (go == null) return;
            go.layer = camada;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = camada;
        }

        public static bool Ativo(GameObject go)
        {
            try { return go != null && go.activeInHierarchy; } catch { return false; }
        }

        public static string Caminho(Transform t)
        {
            var s = t != null ? t.name : "-";
            for (var p = t != null ? t.parent : null; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
