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
            // No cerramos aquí para respetar chinchetas (CloseAll lo hace en otros hooks)
            return;
        }

        var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (go == null) return;

        var comps = go.GetComponents<GameObjectNotes>();
        if (comps == null || comps.Length == 0) { CloseAll(true); return; }

        // Reúne todas las notas vivas (no "borradas" = Fixed + vacío)
        var noteRefs = new List<(GameObjectNotes owner, GameObjectNotes.NoteData note, int noteIndex)>();
        for (int c = 0; c < comps.Length; c++)
        {
            var comp = comps[c];
            var list = comp.NotesList;
            if (list == null) continue;
            for (int i = 0; i < list.Count; i++)
            {
                var n = list[i];
                if (n == null) continue;
                if (GameObjectNotes.IsDeleted(n)) continue;

                // ✅ Filtros de visibilidad: 👁️ y 📌 deben estar activos
                if (!n.showInHierarchy) continue;
                if (!n.tooltipPinned) continue;

                noteRefs.Add((comp, n, i));
            }
        }

        // Si no hay notas elegibles, cerramos tooltips no pineados y salimos
        if (noteRefs.Count == 0) { CloseAll(true); return; }

        // Ancla = área del nombre (corrigiendo indent)
        var anchorLocalRect = selectionRect;
        Vector2 topLeft = GUIUtility.GUIToScreenPoint(anchorLocalRect.position);
        var baseAnchor = new Rect(topLeft.x, topLeft.y, anchorLocalRect.width, anchorLocalRect.height);

        EnsureWindowPool(noteRefs.Count);

        // Muestra una ventana por nota (apiladas)
        const float STACK_GAP = 20f;
        for (int i = 0; i < noteRefs.Count; i++)
        {
            var w = s_Windows[i];
            var offsetAnchor = new Rect(baseAnchor.x, baseAnchor.y + i * (anchorLocalRect.height + STACK_GAP), baseAnchor.width, baseAnchor.height);
            if (w == null) { w = NotesTooltipWindow.Create(); s_Windows[i] = w; }
            w.ShowFor(noteRefs[i].owner, noteRefs[i].note, noteRefs[i].noteIndex, offsetAnchor);
        }

        // Cierra sobrantes (no pineadas)
        for (int i = noteRefs.Count; i < s_Windows.Count; i++)
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
