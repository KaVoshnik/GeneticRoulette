using System;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GeneticRoulette
{
    /// <summary>
    /// Плашка в меню здоровья (клавиша R): болезни игрока, их описания и плюсы.
    /// Стоит справа: под панелью навыков и временем, над кнопками внизу справа, так что ничего не закрывает
    /// (раскладка меню рассчитана на экран 1920x1080; плашка прижата к правому краю, как панель навыков).
    /// Создаётся один раз как дочерний элемент WoundView (корень меню на весь экран) и живёт вместе с ним.
    /// Шрифт берётся у родного текста меню; если в нём нет кириллицы, показываем английские названия.
    /// </summary>
    internal static class HealthBanner
    {
        static GameObject root;
        static TextMeshProUGUI text;
        static Image border, back;
        static float refresh;
        static string lastShown;

        static readonly Color bg = new Color(0.03f, 0.03f, 0.04f, 0.93f);

        // геометрия в координатах холста 1920x1080
        const float Width = 402f;          // как у панели навыков (SkillsBack)
        const float TopOffset = 306f;      // панель навыков (до 230) + плашка времени (до 293) + зазор
        const float RightMargin = 16f;     // как у SkillsBack
        const float MaxHeight = 430f;      // до кнопок внизу справа (они начинаются примерно с 744 от верха)
        const float MinHeight = 70f;
        const float PadX = 17f;            // 3 (рамка) + 14 (отступ текста)
        const float PadY = 12f;

        static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }

        static Color Accent()
        {
            Color c;
            string hex = Plugin.CfgAccent != null ? Plugin.CfgAccent.Value : "#C93C3C";
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c)) return new Color(c.r, c.g, c.b, 1f);
            return new Color(0.79f, 0.24f, 0.24f, 1f);
        }

        static TMP_Text PickGameText(WoundView wv)
        {
            TMP_Text[] cand = { wv.painText, wv.happyText, wv.nameText, wv.bpmText, wv.tempText };
            foreach (TMP_Text t in cand) if (t != null && t.font != null) return t;
            return null;
        }

        static void Build(WoundView wv)
        {
            float scale = Plugin.CfgUiScale != null ? Mathf.Clamp(Plugin.CfgUiScale.Value, 0.5f, 2.5f) : 1f;
            root = new GameObject("GeneticRoulette_HealthBanner", typeof(RectTransform));
            root.transform.SetParent(wv.transform, false);
            RectTransform rt = root.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-RightMargin, -TopOffset);
            rt.sizeDelta = new Vector2(Width, 120f);
            rt.localScale = Vector3.one * scale;
            root.transform.SetAsLastSibling();

            RectTransform b = NewRect("Border", rt);
            Stretch(b, 0, 0, 0, 0);
            border = b.gameObject.AddComponent<Image>();
            border.color = Accent();
            border.raycastTarget = false;

            RectTransform inner = NewRect("Inner", rt);
            Stretch(inner, 3, 3, 3, 3);
            back = inner.gameObject.AddComponent<Image>();
            back.color = bg;
            back.raycastTarget = false;

            RectTransform tr = NewRect("Text", inner);
            Stretch(tr, 14, 6, 14, 6);
            text = tr.gameObject.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.enableWordWrapping = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableAutoSizing = false;
            text.fontSize = 20f;
            text.richText = true;
            text.overflowMode = TextOverflowModes.Truncate;

            TMP_Text game = PickGameText(wv);
            if (game != null)
            {
                text.font = game.font;
                text.color = new Color(game.color.r, game.color.g, game.color.b, 1f);
                if (text.color.r + text.color.g + text.color.b < 1.2f) text.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            }
            else text.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        }

        static string BuildText(bool ru)
        {
            string accentHex = ColorUtility.ToHtmlStringRGB(Accent());
            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (DiseaseDef d in Diseases.All)
            {
                if (!Plugin.Has(d.Id)) continue;
                if (!first) sb.Append("\n");
                first = false;
                sb.Append("<b><color=#").Append(accentHex).Append(">").Append(d.Name(ru)).Append("</color></b>\n");
                sb.Append(d.Desc(ru));
                string perk = d.Perk(ru);
                if (!string.IsNullOrEmpty(perk))
                    sb.Append("\n<color=#FFD94D>").Append(ru ? "+ Плюс: " : "+ Perk: ").Append(perk).Append("</color>");
                sb.Append("\n");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Высота плашки по тексту; если не влезает, уменьшаем шрифт.</summary>
        static void Fit()
        {
            RectTransform rt = root.GetComponent<RectTransform>();
            float size = 20f;
            float h;
            while (true)
            {
                text.fontSize = size;
                Vector2 pv = text.GetPreferredValues(text.text, Width - 2f * PadX, 0f);
                h = pv.y + 2f * PadY;
                if (h <= MaxHeight || size <= 12f) break;
                size -= 1f;
            }
            h = Mathf.Clamp(h, MinHeight, MaxHeight);
            rt.sizeDelta = new Vector2(Width, h);
        }

        /// <summary>Вызывается из WoundView.Update (то есть только пока меню открыто).</summary>
        internal static void Tick(WoundView wv)
        {
            if (Plugin.CfgBanner == null || !Plugin.CfgBanner.Value) { if (root != null) root.SetActive(false); return; }
            if (Plugin.Active.Count == 0) { if (root != null) root.SetActive(false); return; }

            if (root == null) Build(wv);
            if (root == null) return;
            if (root.transform.parent != wv.transform) root.transform.SetParent(wv.transform, false);

            if (!root.activeSelf) root.SetActive(true);
            refresh -= Time.unscaledDeltaTime;
            if (refresh > 0f && lastShown != null) return;
            refresh = 0.5f;

            bool ru = Plugin.Ru() && (text.font == null || Plugin.FontHasCyrillic(text));
            string s = BuildText(ru);
            if (s != lastShown)
            {
                lastShown = s;
                text.text = s;
                Fit();
            }
            if (border != null) border.color = Accent();
        }
    }

    [HarmonyPatch(typeof(WoundView), "Update")]
    static class Patch_WoundViewUpdate
    {
        static void Postfix(WoundView __instance)
        {
            try { HealthBanner.Tick(__instance); }
            catch (Exception e) { Plugin.Log.LogError("HealthBanner: " + e); }
        }
    }
}
