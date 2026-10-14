using UnityEngine;
using UnityEngine.UI;

namespace ZeroSpace.Showcase
{
    public sealed partial class BomTrailerIntro
    {
        GameObject layout;
        CanvasGroup titleGroup;
        CanvasGroup nameGroup;
        RectTransform namePanel;
        RectTransform titlePanel;
        Image transition;
        Text characterTitle;

        void BuildLayout(Camera camera)
        {
            if (layout) return;
            layout = new GameObject("BOM cinematic intro", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(layout, gameObject.scene);
            var canvas = layout.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = .4f;
            canvas.sortingOrder = 60;
            var scaler = layout.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var bars = Panel(layout.transform, "Cinematic bars", Vector2.zero, new Vector2(1920, 1080));
            Flat(bars, "Top", new Vector2(0, 506), new Vector2(1920, 68), Color.black);
            Flat(bars, "Bottom", new Vector2(0, -506), new Vector2(1920, 68), Color.black);
            titlePanel = Panel(layout.transform, "BOM title card", Vector2.zero, new Vector2(1920, 1080));
            titleGroup = titlePanel.gameObject.AddComponent<CanvasGroup>();
            Flat(titlePanel, "Midnight", Vector2.zero, new Vector2(1920, 1080), new Color(.018f, .025f, .055f));
            var titleArt = Panel(titlePanel, "Title strokes", Vector2.zero, new Vector2(1920, 1080));
            titleArt.gameObject.AddComponent<BomTrailerGraphics>();
            Label(titlePanel, "BOM", new Vector2(12, -5), 260, new Color(1, .12f, .45f));
            Label(titlePanel, "BOM", new Vector2(-9, 7), 260, new Color(.05f, .85f, 1));
            Label(titlePanel, "BOM", Vector2.zero, 260, Color.white);
            Label(titlePanel, "Battle of meme", new Vector2(0, -170), 54, Color.white);
            Label(titlePanel, "P E P E   /   C H A R A C T E R   T R A I L E R",
                new Vector2(0, -272), 23, new Color(.36f, .91f, 1));
            Flat(titlePanel, "Title underline", new Vector2(0, -225), new Vector2(220, 4),
                new Color(1, .18f, .48f));
            namePanel = Panel(layout.transform, "PEPE freeze frame", Vector2.zero, new Vector2(1920, 1080));
            nameGroup = namePanel.gameObject.AddComponent<CanvasGroup>();
            var paint = Panel(namePanel, "Pepe paint splash", new Vector2(550, -180), new Vector2(1150, 650));
            var graphic = paint.gameObject.AddComponent<BomTrailerGraphics>();
            graphic.nameCard = true;
            characterTitle = Label(namePanel, "PEPE", new Vector2(470, -95), 200, Color.white);
            characterTitle.rectTransform.localRotation = Quaternion.Euler(0, 0, 9);
            Label(namePanel, "BOM  /  Battle of meme", new Vector2(480, -275), 31, Color.white);
            Label(namePanel, "P E P E   H A S   E N T E R E D   T H E   A R E N A",
                new Vector2(440, -346), 19, new Color(.55f, .95f, 1));
            transition = Flat(layout.transform, "Transition", Vector2.zero, new Vector2(1920, 1080), Color.black);
        }

        void DisplayLayout(float seconds)
        {
            float titleIn = Mathf.SmoothStep(0, 1, Mathf.Clamp01(seconds / .45f));
            float titleOut = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - 3.3f) / .7f));
            titleGroup.alpha = titleOut;
            titlePanel.localScale = Vector3.one * Mathf.Lerp(1.09f, 1, Mathf.Clamp01(seconds / 3.3f));
            float card = Mathf.Clamp01((seconds - FreezeStart) / .24f);
            nameGroup.alpha = Mathf.SmoothStep(0, 1, card);
            namePanel.anchoredPosition = new Vector2((1 - Mathf.SmoothStep(0, 1, card)) * 240, 0);
            characterTitle.text = showcase.CharacterName;
            float fade = Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - FadeStart) / .6f));
            transition.color = new Color(0, 0, 0, Mathf.Max(1 - titleIn, fade));
        }

        static RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(.5f, .5f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = rect.anchorMin;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        static Image Flat(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var root = Panel(parent, name, position, size);
            var image = root.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text Label(Transform parent, string value, Vector2 position, int size, Color color)
        {
            var root = Panel(parent, value, position, new Vector2(1600, size * 1.6f));
            var text = root.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.text = value;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var shadow = root.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(.025f, .025f, .09f, .8f);
            shadow.effectDistance = new Vector2(5, -7);
            return text;
        }
    }
}
