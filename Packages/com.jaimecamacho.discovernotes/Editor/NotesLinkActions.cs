#if UNITY_EDITOR
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class NotesLinkActions
{
    public static void ShowContextMenu(string linkText, string linkIdRaw)
    {
        var menu = new GenericMenu();
        bool isExternal = IsExternal(linkIdRaw);
        UnityEngine.Object target = isExternal ? null : TryResolveAll(linkIdRaw);

        if (!isExternal)
        {
            if (target != null) menu.AddItem(new GUIContent("Seleccionar objeto"), false, () => SelectObject(target));
            else menu.AddDisabledItem(new GUIContent("Seleccionar objeto"));
            menu.AddSeparator("");
        }
        else
        {
            menu.AddItem(new GUIContent("Abrir enlace externo"), false, () => Application.OpenURL(linkIdRaw));
            menu.AddSeparator("");
        }

        menu.AddItem(new GUIContent("Copiar nombre"), false, () => EditorGUIUtility.systemCopyBuffer = linkText ?? string.Empty);
        menu.AddItem(new GUIContent("Copiar ID"), false, () => EditorGUIUtility.systemCopyBuffer = linkIdRaw ?? string.Empty);
        menu.AddItem(new GUIContent("Copiar Markdown"), false, () => EditorGUIUtility.systemCopyBuffer = $"[{linkText}]({linkIdRaw})");

        if (target is GameObject go)
        {
            string scene = string.IsNullOrEmpty(go.scene.name) ? "(Sin escena)" : go.scene.name;
            string path = GetHierarchyPath(go.transform);
            menu.AddItem(new GUIContent("Copiar ruta jerárquica"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = $"{scene}/{path}";
            });
        }

        menu.ShowAsContext();
    }
    static string SanitizeId(string id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;

        // recorta y elimina caracteres invisibles/problemáticos más comunes
        // ZWSP, ZWNJ, ZWJ, NBSP, BOM, LRM/RLM, saltos/retornos, tabs…
        var span = id.Trim().AsSpan();
        var sb = new System.Text.StringBuilder(span.Length);

        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            // excluir controles y separadores “raros”
            bool drop =
                c == '\u200B' || // ZWSP
                c == '\u200C' || // ZWNJ
                c == '\u200D' || // ZWJ
                c == '\uFEFF' || // BOM
                c == '\u00A0' || // NBSP
                c == '\u200E' || // LRM
                c == '\u200F' || // RLM
                c == '\u202A' || c == '\u202B' || c == '\u202C' || c == '\u202D' || c == '\u202E' || // bidi
                c == '\r' || c == '\n' || c == '\t';

            if (!drop) sb.Append(c);
        }
        return sb.ToString();
    }
    public static bool IsExternal(string rawId)
    {
        string id = SanitizeId(rawId);
        if (string.IsNullOrEmpty(id)) return false;
        id = id.ToLowerInvariant();
        return id.StartsWith("http://") || id.StartsWith("https://") || id.StartsWith("file://");
    }

    public static UnityEngine.Object TryResolveAll(string rawId)
    {
        string id = SanitizeId(rawId);
        if (string.IsNullOrEmpty(id)) return null;

        if (GlobalObjectId.TryParse(id, out var gid))
        {
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (obj != null) return obj;
        }

        if (id.Length == 32 && IsHex(id))
        {
            string path = AssetDatabase.GUIDToAssetPath(id);
            if (!string.IsNullOrEmpty(path))
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object >(path);
                if (obj != null) return obj;
            }
        }

        if (id.StartsWith("assets/", System.StringComparison.OrdinalIgnoreCase))
            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object >(id);

        return null;
    }

    static bool IsHex(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }

    static void SelectObject(UnityEngine.Object target)
    {
        if (target == null) return;
        Selection.activeObject = target;
        //EditorGUIUtility.PingObject(target);
    }

    static string GetHierarchyPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
        return sb.ToString();
    }
}
#endif
