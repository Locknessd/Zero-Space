using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string KoReview = "GeneratedAssets/KoRefreshReview";
        const string KoFont = "Assets/UI/Fonts/Nunito/Nunito-Black.ttf";
        const float KoLetterScale = 1.65f;

        [MenuItem("Tools/Battle/Refresh knockout lettering")]
        public static void RefreshBattleKo()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(KoFont);
            if (!font)
                throw new InvalidOperationException("Nunito Black must be imported before refreshing KO.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var ko = game.uiManager.knockout;
                if (!ko || !ko.word || !ko.letterK || !ko.letterO)
                    throw new InvalidOperationException("BattleScene knockout bindings are missing.");
                Undo.RecordObject(ko, "Refresh knockout presentation");
                ko.ResetPresentation();
                ko.holdSeconds = 1.05f;
                Undo.RecordObject(ko.word, "Resize knockout word");
                ko.word.sizeDelta = new Vector2(1180, 660);
                ko.word.anchoredPosition = new Vector2(0, 24);
                ko.word.localRotation = Quaternion.Euler(0, 0, -3);
                ko.word.localScale = Vector3.one;
                if (ko.band)
                {
                    Undo.RecordObject(ko.band.gameObject, "Replace knockout band");
                    ko.band.gameObject.SetActive(false);
                }
                if (ko.burst)
                {
                    Undo.RecordObject(ko.burst.gameObject, "Enable knockout impact rays");
                    ko.burst.gameObject.SetActive(true);
                    ko.burst.Progress = 1;
                }
                var ink = ko.word.GetComponentInChildren<BattleKoInkGraphic>(true);
                if (!ink)
                {
                    var rect = BattleUiRect(ko.word, "Purple knockout paint", new Vector2(1000, 560), Vector2.zero);
                    Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add knockout paint");
                    ink = Undo.AddComponent<BattleKoInkGraphic>(rect.gameObject);
                    rect.SetAsFirstSibling();
                }
                ko.ink = ink;
                Undo.RecordObject(ink.rectTransform, "Resize knockout paint");
                ink.rectTransform.sizeDelta = new Vector2(1180, 660);
                ink.raycastTarget = false;
                ink.Progress = 0;
                ConfigureKoLetter(ko.letterK, font, "K.", new Vector2(-238, -24));
                ConfigureKoLetter(ko.letterO, font, "O.", new Vector2(238, -24));
                ConfigureKoCaption(ko.subtitle, font, "KNOCKOUT", 56, new Vector2(0, 219));
                ConfigureKoCaption(ko.defeatedLabel, font, string.Empty, 30, new Vector2(0, -254));
                EditorUtility.SetDirty(ko);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Cannot save the refreshed KO presentation.");
                Directory.CreateDirectory(KoReview);
                File.WriteAllText(KoReview + "/Design.txt",
                    "Comic/graffiti KO matching the battle's cream/gold hit effects and cyan/magenta HUD.\n" +
                    "1180x660 backplate; 300px Nunito Black at 1.65x scale, heavy ink outline and extrusion.\n" +
                    "Legacy UI FontData clamps font size to 300 on deserialization; scale preserves enlargement.\n" +
                    "Foreground K/O flight remains 180ms each; O impact and KO sound stay at 290ms.\n" +
                    "Short gold impact rays, warm flash and 3-degree stamp angle emphasize the landing.\n");
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ConfigureKoLetter(RectTransform rect, Font font, string content, Vector2 home)
        {
            var text = rect.GetComponent<Text>();
            Undo.RecordObjects(new Object[] { rect, text }, "Round knockout lettering");
            rect.sizeDelta = new Vector2(320, 340);
            rect.anchoredPosition = home;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one * KoLetterScale;
            text.font = font;
            text.fontStyle = FontStyle.Normal;
            text.fontSize = 300;
            text.text = content;
            text.color = Color.white;
            text.raycastTarget = false;
            var gradient = text.GetComponent<BattleTextGradient>();
            if (gradient)
            {
                Undo.RecordObject(gradient, "Soften knockout gradient");
                gradient.top = new Color(1, .98f, .78f);
                gradient.bottom = new Color(1, .64f, .12f);
            }
            foreach (var outline in text.GetComponents<Outline>())
            {
                Undo.RecordObject(outline, "Soften knockout outline");
                outline.effectColor = new Color(.055f, .02f, .085f, 1);
                outline.effectDistance = new Vector2(6, -6);
            }
            foreach (var shadow in text.GetComponents<Shadow>().Where(s => !(s is Outline)))
            {
                Undo.RecordObject(shadow, "Soften knockout shadow");
                shadow.effectColor = new Color(.035f, .012f, .065f, 1);
                shadow.effectDistance = new Vector2(9, -14);
            }
            EditorUtility.SetDirty(text);
        }

        static void ConfigureKoCaption(Text text, Font font, string content, int size, Vector2 position)
        {
            Undo.RecordObjects(new Object[] { text, text.rectTransform }, "Refresh knockout caption");
            text.font = font;
            text.fontStyle = FontStyle.Normal;
            text.fontSize = size;
            text.text = content;
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = new Vector2(700, size + 30);
            text.raycastTarget = false;
            EditorUtility.SetDirty(text);
        }
    }
}
