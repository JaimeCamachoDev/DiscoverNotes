#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

[InitializeOnLoad]
public static class NotesHierarchyHover
{
    static NotesTooltipWindow s_Window;

    static NotesHierarchyHover()
    {
        EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
        EditorApplication.playModeStateChanged += _ => CloseWindow();
        AssemblyReloadEvents.beforeAssemblyReload += CloseWindow;
    }

    static void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
    {
        // Solo reaccionamos a repintado / movimiento del ratón
        var e = Event.current;
        if (e == null) return;
        if (e.type != EventType.Repaint && e.type != EventType.MouseMove) return;

        // === HOVER EN TODA LA FILA ===
        float viewW = EditorGUIUtility.currentViewWidth;
        var fullRowRect = new Rect(0f, selectionRect.y, viewW, selectionRect.height);

        if (!fullRowRect.Contains(Event.current.mousePosition))
            return;

        var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (go == null) return;

        var notes = go.GetComponent<GameObjectNotes>();
        if (notes == null) return;

        // === ANCLA DE POSICIÓN: SOLO EL ÁREA DEL NOMBRE ===
        var anchorLocalRect = selectionRect; // rect del label/nombre con la indentación correcta
        Vector2 topLeft = GUIUtility.GUIToScreenPoint(anchorLocalRect.position);
        var anchorScreenRect = new Rect(topLeft.x, topLeft.y, anchorLocalRect.width, anchorLocalRect.height);

        if (s_Window == null) s_Window = NotesTooltipWindow.Create();
        s_Window.ShowFor(notes, anchorScreenRect);

    }

    static void CloseWindow()
    {
        if (s_Window != null)
        {
            try { s_Window.Close(); } catch { }
            s_Window = null;
        }
    }
}
#endif
