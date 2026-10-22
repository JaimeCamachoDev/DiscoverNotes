#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class TooltipRenderer
{
    const float HEADER_STRIP = 4f;

    public static void DrawBackground(Rect r, Color bg, Color accent)
    {
        // Caja opaca
        //var bg = new Color(0.12f, 0.12f, 0.14f, 0.985f);
        EditorGUI.DrawRect(new Rect(0, 0, r.width, r.height), bg);

        // Banda superior de acento
        EditorGUI.DrawRect(new Rect(0, 0, r.width, HEADER_STRIP), accent);

        // Borde fino
        var border = new Color(1f, 1f, 1f, 0.08f);
        EditorGUI.DrawRect(new Rect(0, 0, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(0, r.height - 1, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(0, 0, 1, r.height), border);
        EditorGUI.DrawRect(new Rect(r.width - 1, 0, 1, r.height), border);

        // Falsa sombra (halo)
        var shadow = new Color(0, 0, 0, 0.2f);
        EditorGUI.DrawRect(new Rect(-2, -2, r.width + 4, 2), shadow);
        EditorGUI.DrawRect(new Rect(-2, r.height, r.width + 4, 2), shadow);
        EditorGUI.DrawRect(new Rect(-2, 0, 2, r.height), shadow);
        EditorGUI.DrawRect(new Rect(r.width, 0, 2, r.height), shadow);
    }
}
#endif