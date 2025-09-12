#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

public static class EditorIconHelper
{
    public static GUIContent TryIcon(params string[] names)
    {
        // Método "silencioso": no usa IconContent para evitar los logs de
        // "Unable to load the icon …" cuando el nombre no existe.
        foreach (var n in names)
        {
            if (string.IsNullOrEmpty(n)) continue;
            var tex = EditorGUIUtility.FindTexture(n);
            if (tex != null) return new GUIContent(tex);
        }

        // Fallback muy estable que existe en todas las versiones.
        var fb = EditorGUIUtility.FindTexture("FilterByType");
        if (fb != null) return new GUIContent(fb);
        return GUIContent.none;
    }

    public static GUIContent GetCalendarIcon()
    {
        // Intenta algunos nombres "muy estables" (no necesariamente calendario,
        // pero evitan logs y suelen existir). Si no, usa fallback dibujado.
        var gc = TryIcon(
            "d_UnityEditor.InspectorWindow", // casi siempre disponible
            "UnityEditor.InspectorWindow",
            "d_Folder Icon", "Folder Icon"   // como plan B
        );
        if (gc != null && gc.image != null) return gc;

        return GetCalendarFallback();
    }

    static GUIContent _calendarFallback;
    static Texture2D _calendarTex;
    static GUIContent GetCalendarFallback()
    {
        if (_calendarFallback != null) return _calendarFallback;

        if (_calendarTex == null)
        {
            // Pequeño icono 16x16 tipo calendario (cabecera roja + cuerpo gris)
            _calendarTex = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    bool border = x == 0 || y == 0 || x == 15 || y == 15;
                    Color c;
                    if (y < 4) // cabecera
                        c = border ? new Color32(160, 40, 40, 255) : new Color32(200, 60, 60, 255);
                    else
                        c = border ? new Color32(70, 70, 70, 255) : new Color32(95, 95, 95, 255);
                    _calendarTex.SetPixel(x, y, c);
                }
            }
            // “anillas”
            _calendarTex.SetPixel(4, 13, Color.white);
            _calendarTex.SetPixel(11, 13, Color.white);

            _calendarTex.Apply(false, false);
        }

        _calendarFallback = new GUIContent(_calendarTex);
        return _calendarFallback;
    }


    public static GUIContent GetCategoryIcon(string categoryName)
    {
        var cat = NoteStylesProvider.FindCategory(categoryName);
        if (cat != null && cat.icon != null)
            return new GUIContent(cat.icon);
        // fallback genérico
        return TryIcon("FilterByType");
    }

    public static void DrawIconPopupAuthor(Rect r, SerializedProperty pAuthor, string[] authors)
    {
        const float iconW = 18f;
        Rect iconRect = new Rect(r.x, r.y + 1, iconW, EditorGUIUtility.singleLineHeight);
        Rect popupRect = new Rect(iconRect.xMax + 4, r.y, r.width - iconW - 4, EditorGUIUtility.singleLineHeight);

        // Avatar
        var a = EditorGUIUtility.IconContent("d_Avatar Icon");
        if (a == null || a.image == null) a = EditorGUIUtility.IconContent("Avatar Icon");
        if (a == null || a.image == null) a = EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow");
        GUI.Label(iconRect, a);

        if (authors == null || authors.Length == 0)
        {
            pAuthor.stringValue = EditorGUI.TextField(popupRect, pAuthor.stringValue); // fallback
            return;
        }

        int idx = Array.IndexOf(authors, pAuthor.stringValue);
        if (idx < 0) { pAuthor.stringValue = authors[0]; idx = 0; }

        int newIdx = EditorGUI.Popup(popupRect, idx, authors);
        if (newIdx != idx) pAuthor.stringValue = authors[newIdx];
    }

    public static void DrawIconPopupCategory(Rect r, SerializedProperty pCategory, string[] categories)
    {
        const float iconW = 18f;
        Rect iconRect = new Rect(r.x, r.y + 1, iconW, EditorGUIUtility.singleLineHeight);
        Rect popupRect = new Rect(iconRect.xMax + 4, r.y, r.width - iconW - 4, EditorGUIUtility.singleLineHeight);

        // Icono de categoría
        GUI.Label(iconRect, GetCategoryIcon(pCategory.stringValue));

        if (categories == null || categories.Length == 0)
        {
            pCategory.stringValue = EditorGUI.TextField(popupRect, pCategory.stringValue); // fallback
            return;
        }

        int idx = Array.IndexOf(categories, pCategory.stringValue);
        if (idx < 0) { pCategory.stringValue = categories[0]; idx = 0; }

        int newIdx = EditorGUI.Popup(popupRect, idx, categories);
        if (newIdx != idx) pCategory.stringValue = categories[newIdx];
    }
}
#endif
