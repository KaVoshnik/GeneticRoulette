using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GeneticRoulette
{
    /// <summary>
    /// Интерфейс рулетки на uGUI + TextMeshPro, как в самой игре.
    /// Шрифт и цвет текста берутся у игрового текста паузы (PauseHandler.pauseText),
    /// поэтому кириллица и пиксельный стиль совпадают с остальным интерфейсом.
    /// </summary>
    internal class RouletteUi
    {
        GameObject root;
        TMP_FontAsset font;
        bool fontFromGame;
        float styleCheck;

        Color textColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        Color accent = new Color(0.79f, 0.24f, 0.24f, 1f);
        readonly Color lockedColor = new Color(1f, 0.85f, 0.3f, 1f);
        readonly Color bgColor = new Color(0.03f, 0.03f, 0.04f, 0.96f);

        CanvasGroup cgBackdrop, cgRoulette, cgList, cgToast;
        RectTransform rouletteRt, progressFill, listRt, toastRt, reelRt;
        TextMeshProUGUI tTitle, tCounter, tReel, tDesc, tListHead, tListBody, tToast;
        readonly List<Image> accentImages = new List<Image>();
        readonly List<TextMeshProUGUI> allTexts = new List<TextMeshProUGUI>();

        bool wasLocked;
        float punch;

        // ------------------------------------------------------------------
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

        Image AddImage(RectTransform rt, Color c, bool isAccent)
        {
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            if (isAccent) accentImages.Add(img);
            return img;
        }

        TextMeshProUGUI AddText(RectTransform parent, string name, float size, TextAlignmentOptions align, Color col)
        {
            RectTransform rt = NewRect(name, parent);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = col;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            t.overflowMode = TextOverflowModes.Ellipsis;
            allTexts.Add(t);
            return t;
        }

        static CanvasGroup AddGroup(RectTransform rt)
        {
            CanvasGroup g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            g.blocksRaycasts = false;
            g.interactable = false;
            return g;
        }

        // ------------------------------------------------------------------
        void ResolveStyle()
        {
            try
            {
                PauseHandler ph = PauseHandler.main;
                if (ph != null && ph.pauseText != null)
                {
                    if (ph.pauseText.font != null)
                    {
                        if (ph.pauseText.font != font)
                        {
                            font = ph.pauseText.font;
                            foreach (TextMeshProUGUI t in allTexts) if (t != null) t.font = font;
                        }
                        fontFromGame = true;
                    }
                    Color c = ph.pauseText.color;
                    if (c.a > 0.1f && (c.r + c.g + c.b) > 1.2f) textColor = new Color(c.r, c.g, c.b, 1f);
                }
            }
            catch (Exception) { }

            Color parsed;
            string hex = Plugin.CfgAccent != null ? Plugin.CfgAccent.Value : "#C93C3C";
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out parsed))
                accent = new Color(parsed.r, parsed.g, parsed.b, 1f);
            foreach (Image img in accentImages) if (img != null) img.color = accent;
        }

        void Build()
        {
            root = new GameObject("GeneticRoulette_UI");
            root.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(root);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            CanvasScaler cs = root.AddComponent<CanvasScaler>();
            cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            cs.referenceResolution = new Vector2(1920f, 1080f);
            cs.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            cs.matchWidthOrHeight = 0.5f;

            RectTransform rootRt = root.GetComponent<RectTransform>();
            float uiScale = Plugin.CfgUiScale != null ? Mathf.Clamp(Plugin.CfgUiScale.Value, 0.5f, 2.5f) : 1f;

            ResolveStyle();

            // --- затемнение, как у паузы ---
            RectTransform back = NewRect("Backdrop", rootRt);
            Stretch(back, 0, 0, 0, 0);
            AddImage(back, new Color(0f, 0f, 0f, 0.6f), false);
            cgBackdrop = AddGroup(back);

            // --- окно рулетки ---
            rouletteRt = NewRect("Roulette", rootRt);
            rouletteRt.anchorMin = rouletteRt.anchorMax = new Vector2(0.5f, 0.5f);
            rouletteRt.pivot = new Vector2(0.5f, 0.5f);
            rouletteRt.sizeDelta = new Vector2(980f, 400f);
            rouletteRt.localScale = Vector3.one * uiScale;
            cgRoulette = AddGroup(rouletteRt);

            RectTransform border = NewRect("Border", rouletteRt);
            Stretch(border, 0, 0, 0, 0);
            AddImage(border, accent, true);

            RectTransform inner = NewRect("Inner", rouletteRt);
            Stretch(inner, 4, 4, 4, 4);
            AddImage(inner, bgColor, false);

            RectTransform header = NewRect("Header", inner);
            header.anchorMin = new Vector2(0f, 1f);
            header.anchorMax = new Vector2(1f, 1f);
            header.pivot = new Vector2(0.5f, 1f);
            header.sizeDelta = new Vector2(0f, 64f);
            header.anchoredPosition = Vector2.zero;
            Image headImg = AddImage(header, accent, true);
            headImg.color = new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 1f);
            accentImages.Remove(headImg);

            tTitle = AddText(header, "Title", 30f, TextAlignmentOptions.MidlineLeft, textColor);
            Stretch(tTitle.rectTransform, 24, 0, 220, 0);
            tTitle.fontStyle = FontStyles.Bold;
            tTitle.enableWordWrapping = false;

            tCounter = AddText(header, "Counter", 26f, TextAlignmentOptions.MidlineRight, textColor);
            Stretch(tCounter.rectTransform, 600, 0, 24, 0);

            reelRt = NewRect("ReelArea", inner);
            Stretch(reelRt, 24, 150, 24, 90);
            tReel = AddText(reelRt, "Reel", 60f, TextAlignmentOptions.Center, textColor);
            Stretch(tReel.rectTransform, 0, 0, 0, 0);
            tReel.enableAutoSizing = true;
            tReel.fontSizeMin = 26f;
            tReel.fontSizeMax = 60f;
            tReel.fontStyle = FontStyles.Bold;

            RectTransform descRt = NewRect("DescArea", inner);
            Stretch(descRt, 40, 28, 40, 250);
            tDesc = AddText(descRt, "Desc", 28f, TextAlignmentOptions.Top, new Color(textColor.r * 0.85f, textColor.g * 0.85f, textColor.b * 0.85f, 1f));
            Stretch(tDesc.rectTransform, 0, 0, 0, 0);

            // полоса прогресса (время «вращения»)
            RectTransform track = NewRect("Track", inner);
            track.anchorMin = new Vector2(0f, 0f);
            track.anchorMax = new Vector2(1f, 0f);
            track.pivot = new Vector2(0.5f, 0f);
            track.sizeDelta = new Vector2(-48f, 10f);
            track.anchoredPosition = new Vector2(0f, 14f);
            AddImage(track, new Color(1f, 1f, 1f, 0.08f), false);
            progressFill = NewRect("Fill", track);
            progressFill.anchorMin = Vector2.zero;
            progressFill.anchorMax = new Vector2(0f, 1f);
            progressFill.offsetMin = Vector2.zero;
            progressFill.offsetMax = Vector2.zero;
            AddImage(progressFill, accent, true);

            // --- список болезней (слева сверху) ---
            listRt = NewRect("List", rootRt);
            listRt.anchorMin = listRt.anchorMax = new Vector2(0f, 1f);
            listRt.pivot = new Vector2(0f, 1f);
            listRt.anchoredPosition = new Vector2(24f, -24f);
            listRt.sizeDelta = new Vector2(720f, 120f);
            listRt.localScale = Vector3.one * uiScale;
            cgList = AddGroup(listRt);
            RectTransform lb = NewRect("Border", listRt);
            Stretch(lb, 0, 0, 0, 0);
            AddImage(lb, accent, true);
            RectTransform li = NewRect("Inner", listRt);
            Stretch(li, 3, 3, 3, 3);
            AddImage(li, new Color(bgColor.r, bgColor.g, bgColor.b, 0.92f), false);
            tListHead = AddText(li, "Head", 26f, TextAlignmentOptions.TopLeft, accent);
            tListHead.rectTransform.anchorMin = new Vector2(0f, 1f);
            tListHead.rectTransform.anchorMax = new Vector2(1f, 1f);
            tListHead.rectTransform.pivot = new Vector2(0.5f, 1f);
            tListHead.rectTransform.sizeDelta = new Vector2(-32f, 38f);
            tListHead.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            tListHead.fontStyle = FontStyles.Bold;
            tListBody = AddText(li, "Body", 24f, TextAlignmentOptions.TopLeft, textColor);
            Stretch(tListBody.rectTransform, 16, 8, 16, 48);

            // --- всплывающее сообщение ---
            toastRt = NewRect("Toast", rootRt);
            toastRt.anchorMin = toastRt.anchorMax = new Vector2(0.5f, 1f);
            toastRt.pivot = new Vector2(0.5f, 1f);
            toastRt.anchoredPosition = new Vector2(0f, -150f);
            toastRt.sizeDelta = new Vector2(860f, 64f);
            toastRt.localScale = Vector3.one * uiScale;
            cgToast = AddGroup(toastRt);
            RectTransform tb = NewRect("Border", toastRt);
            Stretch(tb, 0, 0, 0, 0);
            AddImage(tb, accent, true);
            RectTransform ti = NewRect("Inner", toastRt);
            Stretch(ti, 3, 3, 3, 3);
            AddImage(ti, bgColor, false);
            tToast = AddText(ti, "Text", 28f, TextAlignmentOptions.Center, textColor);
            Stretch(tToast.rectTransform, 12, 0, 12, 0);
        }

        static float Fade(float cur, float target, float dt)
        {
            return Mathf.MoveTowards(cur, target, dt * 6f);
        }

        // ------------------------------------------------------------------
        internal void Update(
            bool rouletteOn, string title, string counter, string reel, bool locked, float spinProgress, string desc,
            bool listOn, string listHead, string listBody, int listLines,
            bool toastOn, string toast)
        {
            float dt = Time.unscaledDeltaTime;
            if (root == null)
            {
                if (!rouletteOn && !listOn && !toastOn) return;
                Build();
            }

            styleCheck -= dt;
            if (styleCheck <= 0f)
            {
                styleCheck = fontFromGame ? 3f : 0.5f;
                ResolveStyle();
            }

            // роулетка
            cgBackdrop.alpha = Fade(cgBackdrop.alpha, rouletteOn ? 1f : 0f, dt);
            cgRoulette.alpha = Fade(cgRoulette.alpha, rouletteOn ? 1f : 0f, dt);
            if (rouletteOn)
            {
                if (tTitle.text != title) tTitle.text = title;
                if (tCounter.text != counter) tCounter.text = counter;
                if (tReel.text != reel) tReel.text = reel;
                if (tDesc.text != desc) tDesc.text = desc;
                tReel.color = locked ? lockedColor : new Color(textColor.r * 0.8f, textColor.g * 0.8f, textColor.b * 0.8f, 1f);
                if (locked && !wasLocked) punch = 1f;
                wasLocked = locked;
                punch = Mathf.Max(0f, punch - dt * 4f);
                reelRt.localScale = Vector3.one * (1f + 0.12f * punch);
                progressFill.anchorMax = new Vector2(Mathf.Clamp01(spinProgress), 1f);
            }
            else
            {
                wasLocked = false;
            }

            // список
            cgList.alpha = Fade(cgList.alpha, listOn ? 1f : 0f, dt);
            if (listOn)
            {
                if (tListHead.text != listHead) tListHead.text = listHead;
                if (tListBody.text != listBody) tListBody.text = listBody;
                listRt.sizeDelta = new Vector2(720f, 62f + Mathf.Max(1, listLines) * 34f);
            }

            // сообщение
            cgToast.alpha = Fade(cgToast.alpha, toastOn ? 1f : 0f, dt);
            if (toastOn && tToast.text != toast) tToast.text = toast;
        }
    }
}
