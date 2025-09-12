#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

public static class EditorIconHelper
{
    // Dentro de EditorIconHelper (mismo archivo)
    static Texture2D _pinOnTex, _pinOffTex;
    // EditorIconHelper.cs
    static GUIContent _pinOnGc, _pinOffGc;

    public static GUIContent GetStarIcon()
    {
        // Intenta varios nombres icónicos; algunos devuelven GUIContent null en ciertas versiones/skins.
        var names = new[] { "Pin", "Pinned", "LockIcon", "SceneViewOrtho", "InspectorLock", "Favorite Icon" };

        foreach (var n in names)
        {
            try
            {
                var gc = EditorGUIUtility.IconContent(n);
                if (gc != null && (gc.image != null || !string.IsNullOrEmpty(gc.text)))
                    return gc; // válido
            }
            catch { /* ignorar */ }
        }

        // Último fallback SIEMPRE válido
        return new GUIContent("?");
    }


    public static void GetPinIcons(out GUIContent pinOn, out GUIContent pinOff)
    {
        // Icono base de estrella (puede variar de nombre según la skin/versión).
        var star = TryIcon("Favorite", "d_Favorite", "Favorite Icon", "d_Favorite Icon");
        if (star == null)
        {
            // Fallback a un simple "?" en caso extremo
            pinOn = new GUIContent("?");
            pinOff = new GUIContent("?");
            return;
        }

        // Clonamos el mismo texture dos veces con distintos colores.
        var tex = star.image as Texture2D;
        if (tex == null)
        {
            pinOn = new GUIContent("?");
            pinOff = new GUIContent("?");
            return;
        }

        // Creamos dos copias tintadas
        var yellow = TintTexture(tex, new Color(1f, 0.85f, 0.1f, 1f));
        var gray = TintTexture(tex, new Color(0.6f, 0.6f, 0.6f, 1f));

        pinOn = new GUIContent(yellow, "Tooltip fijado");
        pinOff = new GUIContent(gray, "Fijar tooltip");
    }

    /// <summary> Crea una copia tintada de una textura. </summary>
    static Texture2D TintTexture(Texture2D src, Color tint)
    {
        var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = src.filterMode,
            wrapMode = src.wrapMode
        };
        var pixels = src.GetPixels32();
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            // multiplicamos por el tinte manteniendo alpha
            pixels[i] = new Color(
                p.r / 255f * tint.r,
                p.g / 255f * tint.g,
                p.b / 255f * tint.b,
                p.a / 255f * tint.a
            );
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }


    // Puedes dejar este TryIcon tal cual lo tengas (params string[] names)
    public static GUIContent TryIcon(params string[] names)
    {
        foreach (var n in names)
        {
            var tex = EditorGUIUtility.FindTexture(n);
            if (tex != null) return new GUIContent(tex);
        }
        return null;
    }

    // Igual que te pasé antes: generamos dos pins de distinto color
    static Texture2D MakePinTex(bool on)
    {
        var t = new Texture2D(16, 16, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        var bg = new Color32(0, 0, 0, 0);
        var head = on ? new Color32(220, 80, 80, 255) : new Color32(140, 140, 140, 255);
        var body = on ? new Color32(240, 240, 240, 255) : new Color32(180, 180, 180, 255);

        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                t.SetPixel(x, y, bg);

        // cabecita (disco)
        for (int y = 4; y <= 7; y++)
            for (int x = 7; x <= 10; x++)
                if ((x - 8.5f) * (x - 8.5f) + (y - 5.5f) * (y - 5.5f) <= 3.2f) t.SetPixel(x, y, head);

        // cuerpo diagonal
        t.SetPixel(8, 8, body);
        t.SetPixel(9, 9, body);
        t.SetPixel(10, 10, body);
        t.SetPixel(11, 11, body);

        t.Apply(false, false);
        return t;
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
