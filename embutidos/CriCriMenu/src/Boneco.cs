using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriCriMods.Menu
{
    // The player's crewmate with their cosmetics: a copy of a PoolablePlayer the menu already has (the account tab's
    // picture), dressed again from the save data by the game's own UpdateFromDataManager. Never a new prefab.
    // The copy belongs to the menu scene and dies with it (the Replay's rule: no PoolablePlayer left behind in a match).
    internal sealed class Boneco
    {
        static BepInEx.Logging.ManualLogSource Log => Plugin.Registro;

        public GameObject Objeto;
        public PoolablePlayer Jogador;
        public Rect Px;
        public float Frente;
        float _alturaNatural, _larguraNatural;   // world size at scale 1 (measured once the sprites are in)
        readonly Dictionary<IntPtr, int> _ordemBase = new Dictionary<IntPtr, int>();
        int _minBase = int.MaxValue;
        bool _logou, _avisouVazio;
        float _criadoEm;

        public static Boneco Novo(Transform pai, Desenho d, Rect px, float frente, out string origem)
        {
            origem = null;
            var fonte = Achar(out origem);
            if (fonte == null) return null;
            var go = UnityEngine.Object.Instantiate(fonte.gameObject, pai);
            go.name = "CriCriBoneco";
            foreach (var ap in go.GetComponentsInChildren<AspectPosition>(true)) UnityEngine.Object.DestroyImmediate(ap);
            foreach (var c in go.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
            Pecas.Camada(go, d.Camada);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            if (!go.activeSelf) go.SetActive(true);
            var pp = go.GetComponent<PoolablePlayer>();
            if (pp == null) { UnityEngine.Object.Destroy(go); return null; }
            try { pp.UpdateFromDataManager(PlayerMaterial.MaskType.None); }
            catch (Exception e) { Log.LogWarning("[menu] boneco: UpdateFromDataManager: " + e.GetType().Name + ": " + e.Message); }
            try { pp.ToggleName(false); } catch { }
            try { pp.TogglePet(false); } catch { }
            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                try { sr.maskInteraction = SpriteMaskInteraction.None; } catch { }
            return new Boneco { Objeto = go, Jogador = pp, Px = px, Frente = frente, _criadoEm = Time.unscaledTime };
        }

        // The account tab's picture first (the game keeps it dressed); any other PoolablePlayer of the scene otherwise.
        static PoolablePlayer Achar(out string origem)
        {
            origem = null;
            try
            {
                if (DestroyableSingleton<AccountManager>.InstanceExists)
                {
                    var tab = DestroyableSingleton<AccountManager>.Instance.accountTab;
                    if (tab != null && tab.playerImage != null) { origem = "AccountTab.playerImage"; return tab.playerImage; }
                }
            }
            catch { }
            try
            {
                foreach (var p in UnityEngine.Object.FindObjectsOfType<PoolablePlayer>(true))
                    if (p != null && !p.name.StartsWith("CriCri")) { origem = Pecas.Caminho(p.transform); return p; }
            }
            catch { }
            return null;
        }

        // Bounds of what is drawn now (the pet is off), in world units.
        bool Medir(out Bounds b)
        {
            b = default;
            bool achou = false;
            foreach (var sr in Objeto.GetComponentsInChildren<SpriteRenderer>(false))
            {
                if (sr == null || !sr.enabled || sr.sprite == null) continue;
                var bb = sr.bounds;
                if (bb.size.x < 0.0001f || bb.size.y < 0.0001f) continue;
                if (!achou) { b = bb; achou = true; } else b.Encapsulate(bb);
            }
            return achou;
        }

        public void Dispor(Grade g, Desenho d)
        {
            if (Objeto == null) return;
            Vector2 caixa = g.Tamanho(Px);
            Vector3 centro = g.Centro(Px, Frente);
            var t = Objeto.transform;
            // the natural size is taken once the cosmetics have had a moment to load (hats come in late)
            if (_alturaNatural <= 0f || Time.unscaledTime - _criadoEm < 2f)
            {
                float k0 = Math.Abs(t.lossyScale.y) > 0.0001f ? Math.Abs(t.lossyScale.y) : 1f;
                if (Medir(out Bounds b0))
                {
                    _alturaNatural = b0.size.y / k0;
                    _larguraNatural = b0.size.x / k0;
                }
            }
            if (_alturaNatural <= 0.0001f)
            {
                if (!_avisouVazio && Time.unscaledTime - _criadoEm > 3f) { _avisouVazio = true; Log.LogWarning("[menu] boneco sem desenho (nenhum sprite visivel)"); }
                t.localPosition = centro;
                return;
            }
            float k = Math.Min(caixa.y * 0.92f / _alturaNatural, caixa.x * 0.92f / Math.Max(0.0001f, _larguraNatural));
            var escala = new Vector3(k, k, 1f);
            if ((t.localScale - escala).sqrMagnitude > 1e-8f) t.localScale = escala;
            // the drawing's center on the box's center (the pivot is not in the middle of the body)
            t.localPosition = centro;
            if (Medir(out Bounds b))
            {
                var pai = t.parent;
                Vector3 meio = pai != null ? pai.InverseTransformPoint(b.center) : b.center;
                t.localPosition = new Vector3(centro.x - (meio.x - centro.x), centro.y - (meio.y - centro.y), centro.z);
            }
            Ordenar(d);
            if (!_logou && Time.unscaledTime - _criadoEm > 2f)
            {
                _logou = true;
                Log.LogInfo("[menu] boneco: tamanho natural " + _larguraNatural.ToString("0.00") + "x" + _alturaNatural.ToString("0.00")
                    + ", escala " + k.ToString("0.00") + ", " + _ordemBase.Count + " desenho(s), ordem base " + (_minBase == int.MaxValue ? "-" : _minBase.ToString()));
            }
        }

        // The crewmate's own pieces keep their order among themselves but never fall below our card.
        void Ordenar(Desenho d)
        {
            var lista = Objeto.GetComponentsInChildren<Renderer>(true);
            foreach (var r in lista)
            {
                if (r == null) continue;
                IntPtr p = r.Pointer;
                if (!_ordemBase.ContainsKey(p))
                {
                    _ordemBase[p] = r.sortingOrder;
                    if (r.sortingOrder < _minBase) _minBase = r.sortingOrder;
                }
            }
            if (_minBase == int.MaxValue) return;
            int desvio = _minBase < d.Ordem ? d.Ordem - _minBase : 0;
            foreach (var r in lista)
            {
                if (r == null || !_ordemBase.TryGetValue(r.Pointer, out int b)) continue;
                int alvo = b + desvio;
                if (r.sortingLayerID != d.LayerId) r.sortingLayerID = d.LayerId;
                if (r.sortingOrder != alvo) r.sortingOrder = alvo;
            }
        }

        public void Atualizar()
        {
            try { Jogador?.UpdateFromDataManager(PlayerMaterial.MaskType.None); } catch { }
            try { Jogador?.TogglePet(false); } catch { }
            try { Jogador?.ToggleName(false); } catch { }
            _criadoEm = Time.unscaledTime;   // measure again (another hat)
            _alturaNatural = 0f;
        }
    }
}
