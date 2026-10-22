#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class NoteStylesProvider
{
    /// <summary>
    /// Relative path where the NoteStyles asset is stored.
    /// </summary>
    public static string StylesAssetPath => ComputeAssetPath();

    static NoteStyles s_cachedStyles;
    static string[] s_cachedAuthors = Array.Empty<string>();
    static string[] s_cachedCategoryNames = Array.Empty<string>();
    static string[] s_cachedDisciplineNames = Array.Empty<string>();
    static string s_cachedScriptPath;
    static string s_lastCategoryForFind;
    static NoteCategory s_lastCategory;

    [InitializeOnLoadMethod]
    static void Init()
    {
        EditorApplication.projectChanged += ClearCaches;
        AssemblyReloadEvents.beforeAssemblyReload += ClearCaches;
        EditorApplication.delayCall += WarmupStylesCache;
    }

    static void WarmupStylesCache()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        TryLoadStyles(out _);
    }

    static void ClearCaches()
    {
        s_cachedStyles = null;
        s_cachedAuthors = Array.Empty<string>();
        s_cachedCategoryNames = Array.Empty<string>();
        s_cachedDisciplineNames = Array.Empty<string>();
        s_cachedScriptPath = null;
        s_lastCategory = null;
        s_lastCategoryForFind = null;
    }

    internal static void NotifyStylesModified(NoteStyles styles)
    {
        if (styles == null)
        {
            ClearCaches();
            return;
        }

        s_cachedStyles = styles;
        BuildSimpleCaches();
    }

    public static NoteStyles GetOrCreateStyles()
    {
        if (s_cachedStyles != null)
            return s_cachedStyles;

        var path = StylesAssetPath;
        if (TryLoadStyles(out var styles))
            return styles;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return null;

        if (File.Exists(path))
        {
            Debug.LogWarning($"NoteStyles: the asset already exists at {path}, but it is not available yet. Automatic recreation is skipped for this import cycle.");
            return null;
        }

        var anyGuid = AssetDatabase.FindAssets("t:NoteStyles").FirstOrDefault();
        if (!string.IsNullOrEmpty(anyGuid))
        {
            var existingPath = AssetDatabase.GUIDToAssetPath(anyGuid);
            var existing = AssetDatabase.LoadAssetAtPath<NoteStyles>(existingPath);
            if (existing != null)
            {
                var targetDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(targetDir))
                    EnsureFolderExists(targetDir.Replace("\\", "/"));

                if (!string.Equals(existingPath, path, StringComparison.Ordinal))
                {
                    var err = AssetDatabase.MoveAsset(existingPath, path);
                    if (!string.IsNullOrEmpty(err))
                    {
                        Debug.LogWarning($"NoteStyles: automatic move failed: {err}. Using existing asset at {existingPath}");
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

        var directory = Path.GetDirectoryName(path)?.Replace("\\", "/");
        if (!string.IsNullOrEmpty(directory))
            EnsureFolderExists(directory);

        styles = ScriptableObject.CreateInstance<NoteStyles>();
        AssetDatabase.CreateAsset(styles, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        s_cachedStyles = styles;
        BuildSimpleCaches();
        return s_cachedStyles;
    }

    static void BuildSimpleCaches()
    {
        if (s_cachedStyles == null)
        {
            s_cachedAuthors = Array.Empty<string>();
            s_cachedCategoryNames = Array.Empty<string>();
            s_cachedDisciplineNames = Array.Empty<string>();
            return;
        }

        s_cachedAuthors = (s_cachedStyles.authors != null && s_cachedStyles.authors.Count > 0)
            ? s_cachedStyles.authors
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .ToArray()
            : Array.Empty<string>();

        if (s_cachedAuthors.Length == 0)
            s_cachedAuthors = new[] { "Anonimo" };

        if (s_cachedStyles.categories != null && s_cachedStyles.categories.Count > 0)
        {
            s_cachedCategoryNames = s_cachedStyles.categories
                .Where(c => c != null && !string.IsNullOrWhiteSpace(c.name))
                .Select(c => c.name.Trim())
                .ToArray();
        }
        else
        {
            s_cachedCategoryNames = Array.Empty<string>();
        }

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
            s_cachedDisciplineNames = Array.Empty<string>();
        }

        s_lastCategory = null;
        s_lastCategoryForFind = null;
    }

    public static string[] GetDisciplineNamesCopy()
    {
        GetOrCreateStyles();

        if (s_cachedDisciplineNames == null || s_cachedDisciplineNames.Length == 0)
            return DiscoverCategoryUtility.GetDisplayNamesCopy();

        var copy = new string[s_cachedDisciplineNames.Length];
        Array.Copy(s_cachedDisciplineNames, copy, copy.Length);
        return copy;
    }

    public static string GetDisciplineDisplayName(DiscoverCategory category)
    {
        var names = GetDisciplineNamesCopy();
        int idx = (int)category;
        if (names != null && idx >= 0 && idx < names.Length)
        {
            string name = names[idx];
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return DiscoverCategoryUtility.GetDisplayName(category);
    }

    public static NoteCategory FindCategory(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            var styles = GetOrCreateStyles();
            if (styles == null || styles.categories == null || styles.categories.Count == 0)
                return null;

            return styles.categories[0];
        }

        if (s_lastCategory != null && s_lastCategoryForFind == name)
            return s_lastCategory;

        var loadedStyles = GetOrCreateStyles();
        if (loadedStyles == null || loadedStyles.categories == null || loadedStyles.categories.Count == 0)
            return null;

        var category = loadedStyles.categories.FirstOrDefault(x => x != null && x.name == name) ?? loadedStyles.categories[0];
        s_lastCategory = category;
        s_lastCategoryForFind = name;
        return category;
    }

    public static string[] GetCategoryNames()
    {
        if (s_cachedStyles == null)
            GetOrCreateStyles();

        return s_cachedCategoryNames ?? Array.Empty<string>();
    }

    public static string[] GetAuthors()
    {
        if (s_cachedStyles == null)
            GetOrCreateStyles();

        return s_cachedAuthors ?? new[] { "Anonimo" };
    }

    [MenuItem("Tools/Notes/Open NoteStyles")]
    public static void SelectStylesAsset()
    {
        var styles = GetOrCreateStyles();
        Selection.activeObject = styles;
        EditorGUIUtility.PingObject(styles);
    }

    static string ComputeAssetPath()
    {
        if (string.IsNullOrEmpty(s_cachedScriptPath))
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript NoteStylesProvider");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == typeof(NoteStylesProvider))
                {
                    s_cachedScriptPath = path;
                    break;
                }
            }

            if (string.IsNullOrEmpty(s_cachedScriptPath))
                return "Assets/5-Settings/NoteStyles.asset";
        }

        var dir = Path.GetDirectoryName(s_cachedScriptPath)?.Replace("\\", "/");
        var parent = Path.GetDirectoryName(dir ?? string.Empty)?.Replace("\\", "/");
        if (!string.IsNullOrEmpty(parent))
            return $"{parent}/NoteStyles.asset";

        return "Assets/5-Settings/NoteStyles.asset";
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

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

    static bool TryLoadStyles(out NoteStyles styles)
    {
        styles = AssetDatabase.LoadAssetAtPath<NoteStyles>(StylesAssetPath);
        if (styles == null)
            return false;

        s_cachedStyles = styles;
        BuildSimpleCaches();
        return true;
    }
}
#endif
