#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System.Collections.Generic;

[InitializeOnLoad]
public static class NotesHierarchyHover
{
    static readonly List<NotesTooltipWindow> s_Windows = new List<NotesTooltipWindow>();

    static NotesHierarchyHover()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        EditorApplication.playModeStateChanged += _ => CloseAll();
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        static void OnBeforeAssemblyReload() => CloseAll(false);
    }

    static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
    {
        var e = Event.current;
        if (e == null) return;
        if (e.type != EventType.Repaint && e.type != EventType.MouseMove && e.type != EventType.Layout) return;

        float viewW = EditorGUIUtility.currentViewWidth;
        var fullRowRect = new Rect(0f, selectionRect.y, viewW, selectionRect.height);

        if (!fullRowRect.Contains(e.mousePosition))
        {
            // Si no hay hover, cierra las no pineadas
            //CloseAll(unpinnedOnly: true);
            return;
        }

        var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (go == null) return;

        var notesAll = go.GetComponents<GameObjectNotes>();
        if (notesAll == null || notesAll.Length == 0) { CloseAll(unpinnedOnly: true); return; }

        // Ancla: área del nombre con indent correcto
        var anchorLocalRect = selectionRect;
        Vector2 topLeft = GUIUtility.GUIToScreenPoint(anchorLocalRect.position);
        var baseAnchor = new Rect(topLeft.x, topLeft.y, anchorLocalRect.width, anchorLocalRect.height);

        // Asegurar suficientes ventanas
        EnsureWindowPool(notesAll.Length);

        // Mostrar una por nota, apiladas (offset vertical por índice)
        const float STACK_GAP = 20f;
        for (int i = 0; i < notesAll.Length; i++)
        {
            var w = s_Windows[i];
            var offsetAnchor = new Rect(baseAnchor.x, baseAnchor.y + i * (anchorLocalRect.height + STACK_GAP), baseAnchor.width, baseAnchor.height);
            if (w == null) { w = NotesTooltipWindow.Create(); s_Windows[i] = w; }
            w.ShowFor(notesAll[i], offsetAnchor);
        }

        // Cerrar sobrantes sin pin
        for (int i = notesAll.Length; i < s_Windows.Count; i++)
        {
            var w = s_Windows[i];
            if (w != null && !w.IsPinned) { try { w.Close(); } catch { } s_Windows[i] = null; }
        }
    }

    static void EnsureWindowPool(int count)
    {
        while (s_Windows.Count < count) s_Windows.Add(null);
    }

    static void CloseAll(bool unpinnedOnly = false)
    {
        for (int i = 0; i < s_Windows.Count; i++)
        {
            var w = s_Windows[i];
            if (w == null) continue;
            if (unpinnedOnly && w.IsPinned) continue;

            try { w.Close(); } catch { }
            s_Windows[i] = null;
        }
    }
}
#endif
