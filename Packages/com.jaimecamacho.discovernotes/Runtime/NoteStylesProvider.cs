#if UNITY_EDITOR
using System;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public static class NoteStylesProvider
{
    // === Ruta relativa: una carpeta por encima del script que crea el asset ===
    public static string StylesAssetPath => ComputeAssetPath();

    static string[] s_cachedDisciplineNames = new string[0];
        s_cachedDisciplineNames = new string[0];
    // Cachés: evitan tocar AssetDatabase en cada repintado del inspector
    static NoteStyles s_cachedStyles;
    static string[] s_cachedAuthors = new string[0];
    static string[] s_cachedCategoryNames = new string[0];
    static string s_cachedScriptPath;
    static string s_lastCategoryForFind;
    static NoteCategory s_lastCategory;

    [InitializeOnLoadMethod]
    static void Init()
    {
        // Cuando cambie el proyecto/asset DB, invalidamos cachés
        EditorApplication.projectChanged += ClearCaches;
        AssemblyReloadEvents.beforeAssemblyReload += ClearCaches;
        // Asegurar que exista el asset
        GetOrCreateStyles();
    }

    static void ClearCaches()
    {
        s_cachedStyles = null;
        s_cachedAuthors = new string[0];
        s_cachedCategoryNames = new string[0];
        s_cachedScriptPath = null;
        s_lastCategory = null;
        s_lastCategoryForFind = null;
    }

    public static NoteStyles GetOrCreateStyles()
    {
        if (s_cachedStyles != null) return s_cachedStyles;

        var path = StylesAssetPath;
        var styles = AssetDatabase.LoadAssetAtPath<NoteStyles>(path);

        if (styles == null)
        {
            // ¿Existe en otro sitio? (auto-migración)
            var anyGuid = AssetDatabase.FindAssets("t:NoteStyles").FirstOrDefault();
            if (!string.IsNullOrEmpty(anyGuid))
            {
                var existingPath = AssetDatabase.GUIDToAssetPath(anyGuid);
                var existing = AssetDatabase.LoadAssetAtPath<NoteStyles>(existingPath);
                if (existing != null)
                {
                    EnsureFolderExists(Path.GetDirectoryName(path).Replace("\\", "/"));
                    if (existingPath != path)
                    {
                        var err = AssetDatabase.MoveAsset(existingPath, path);
                        if (!string.IsNullOrEmpty(err))
                        {
                            Debug.LogWarning($"NoteStyles: no se pudo mover automáticamente: {err}. Usando el existente en {existingPath}");
                            s_cachedStyles = existing;
                            BuildSimpleCaches();
                            return s_cachedStyles;
                        }
                    }
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    s_cachedStyles = existing;
                    BuildSimpleCaches();
                    return s_cachedStyles;
                }
            }

            // Crear nuevo
            EnsureFolderExists(Path.GetDirectoryName(path).Replace("\\", "/"));
            styles = ScriptableObject.CreateInstance<NoteStyles>();
            AssetDatabase.CreateAsset(styles, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        s_cachedStyles = styles;
        BuildSimpleCaches();
        return s_cachedStyles;
    }

    static void BuildSimpleCaches()
    {
        if (s_cachedStyles == null) return;
        s_cachedAuthors = (s_cachedStyles.authors != null && s_cachedStyles.authors.Count > 0)
            ? s_cachedStyles.authors.Where(a => !string.IsNullOrEmpty(a)).ToArray()
        var values = DiscoverCategoryUtility.Values;
        if (values != null && values.Count > 0)
        {
            var names = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                string overrideName = (s_cachedStyles.discoverDisciplines != null && i < s_cachedStyles.discoverDisciplines.Count)
                    ? s_cachedStyles.discoverDisciplines[i]
                    : null;
                names[i] = !string.IsNullOrWhiteSpace(overrideName)
                    ? overrideName.Trim()
                    : DiscoverCategoryUtility.GetDisplayName(values[i]);
            }
            s_cachedDisciplineNames = names;
        }
        else
        {
            s_cachedDisciplineNames = new string[0];
        }

    public static string[] GetDisciplineNamesCopy()
    {
        if (s_cachedDisciplineNames == null || s_cachedDisciplineNames.Length == 0)
            return DiscoverCategoryUtility.GetDisplayNamesCopy();

        var copy = new string[s_cachedDisciplineNames.Length];
        Array.Copy(s_cachedDisciplineNames, copy, copy.Length);
        return copy;
    }

    public static string GetDisciplineDisplayName(DiscoverCategory category)
    {
        int idx = (int)category;
        if (s_cachedDisciplineNames != null && idx >= 0 && idx < s_cachedDisciplineNames.Length)
        {
            string name = s_cachedDisciplineNames[idx];
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return DiscoverCategoryUtility.GetDisplayName(category);
    }

            : new[] { "Anónimo" };

        s_cachedCategoryNames = (s_cachedStyles.categories != null)
            ? s_cachedStyles.categories.Where(c => c != null && !string.IsNullOrEmpty(c.name)).Select(c => c.name).ToArray()
            : new string[0];

        // Invalida última categoría cacheada
        s_lastCategory = null;
        s_lastCategoryForFind = null;
    }

    public static NoteCategory FindCategory(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            var s = GetOrCreateStyles();
            if (s == null || s.categories == null || s.categories.Count == 0) return null;
            return s.categories[0];
        }

        if (s_lastCategory != null && s_lastCategoryForFind == name) return s_lastCategory;

        var styles = GetOrCreateStyles();
        if (styles == null || styles.categories == null || styles.categories.Count == 0) return null;

        var c = styles.categories.FirstOrDefault(x => x != null && x.name == name) ?? styles.categories[0];
        s_lastCategory = c;
        s_lastCategoryForFind = name;
        return c;
    }

    public static string[] GetCategoryNames() => s_cachedCategoryNames ?? new string[0];

    public static string[] GetAuthors() => s_cachedAuthors ?? new[] { "Anónimo" };

    [MenuItem("Tools/Notes/Open NoteStyles")]
    public static void SelectStylesAsset()
    {
        var s = GetOrCreateStyles();
        Selection.activeObject = s;
        EditorGUIUtility.PingObject(s);
    }

    // ---------- Helpers ----------
    static string ComputeAssetPath()
    {
        // Cachea ruta del propio script para evitar búsquedas repetidas
        if (string.IsNullOrEmpty(s_cachedScriptPath))
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript NoteStylesProvider");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass() == typeof(NoteStylesProvider))
                {
                    s_cachedScriptPath = path;
                    break;
                }
            }
            if (string.IsNullOrEmpty(s_cachedScriptPath))
                return "Assets/Notes/NoteStyles.asset"; // fallback
        }

        var dir = Path.GetDirectoryName(s_cachedScriptPath).Replace("\\", "/");   // .../Core
        var parent = Path.GetDirectoryName(dir).Replace("\\", "/");               // .../Notes (carpeta de encima)
        if (!string.IsNullOrEmpty(parent))
            return $"{parent}/NoteStyles.asset";

        return "Assets/Notes/NoteStyles.asset";
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        var parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            var next = parts[i];
            var combined = $"{current}/{next}";
            if (!AssetDatabase.IsValidFolder(combined))
                AssetDatabase.CreateFolder(current, next);
            current = combined;
        }
    }
}
#endif
