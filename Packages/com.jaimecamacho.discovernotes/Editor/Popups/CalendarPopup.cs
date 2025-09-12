#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

public class CalendarPopup : PopupWindowContent
{
    DateTime monthCursor; // primer día del mes visible
    readonly Action<DateTime> onPick;
    DateTime selected;
    readonly DateTime today;

    public CalendarPopup(DateTime initial, Action<DateTime> onPick)
    {
        this.onPick = onPick;
        this.selected = initial.Date;
        this.today = DateTime.Now.Date;
        this.monthCursor = new DateTime(initial.Year, initial.Month, 1);
    }

    public override Vector2 GetWindowSize() => new Vector2(320, 300);

    public override void OnGUI(Rect rect)
    {
        GUILayout.BeginVertical();
        GUILayout.Space(4);

        // Header: salto de año («/») y mes (‹/›), popup de mes y campo de año
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("«", GUILayout.Width(28))) monthCursor = monthCursor.AddYears(-1);
        if (GUILayout.Button("‹", GUILayout.Width(28))) monthCursor = monthCursor.AddMonths(-1);

        GUILayout.FlexibleSpace();

        string[] months = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;
        Array.Resize(ref months, 12);
        int monthIndex = monthCursor.Month - 1;
        int newMonthIndex = EditorGUILayout.Popup(monthIndex, months, GUILayout.MaxWidth(140));
        if (newMonthIndex != monthIndex)
            monthCursor = new DateTime(monthCursor.Year, newMonthIndex + 1, 1);

        int year = monthCursor.Year;
        int newYear = EditorGUILayout.IntField(year, GUILayout.Width(64));
        if (newYear != year && newYear >= 1 && newYear <= 9999)
            monthCursor = new DateTime(newYear, monthCursor.Month, 1);

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("›", GUILayout.Width(28))) monthCursor = monthCursor.AddMonths(1);
        if (GUILayout.Button("»", GUILayout.Width(28))) monthCursor = monthCursor.AddYears(1);
        GUILayout.EndHorizontal();

        GUILayout.Space(2);
        GUILayout.Label(monthCursor.ToString("MMMM yyyy"), EditorStyles.boldLabel, GUILayout.Height(18));
        GUILayout.Space(6);

        // Semana (L a D)
        GUILayout.BeginHorizontal();
        string[] w = { "L", "M", "X", "J", "V", "S", "D" };
        foreach (var d in w) GUILayout.Label(d, EditorStyles.miniLabel, GUILayout.Height(16), GUILayout.Width(40));
        GUILayout.EndHorizontal();

        // Cuadrícula regular
        DrawCalendarGrid(rect);

        GUILayout.Space(6);
        if (GUILayout.Button("Hoy", GUILayout.Height(22)))
        {
            selected = today;
            onPick?.Invoke(selected);
            editorWindow.Close();
        }

        GUILayout.EndVertical();
    }

    void DrawCalendarGrid(Rect rect)
    {
        float margin = 6f;
        float gap = 4f;
        float innerW = rect.width - margin * 2f;
        float cellW = Mathf.Floor((innerW - gap * 6f) / 7f);
        float cellH = 26f;

        Rect gridRect = GUILayoutUtility.GetRect(innerW, 6 * (cellH + gap), GUILayout.ExpandWidth(false));
        gridRect.x = rect.x + margin;

        DateTime first = new DateTime(monthCursor.Year, monthCursor.Month, 1);
        int start = ((int)first.DayOfWeek + 6) % 7; // Monday=0
        int days = DateTime.DaysInMonth(first.Year, first.Month);

        int totalCells = Mathf.CeilToInt((start + days) / 7f) * 7; // filas completas
        int rows = totalCells / 7;

        int dayNum = 1;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                Rect cell = new Rect(
                    gridRect.x + c * (cellW + gap),
                    gridRect.y + r * (cellH + gap),
                    cellW,
                    cellH
                );

                bool isRealDay = (r * 7 + c) >= start && dayNum <= days;
                if (!isRealDay)
                {
                    // botón vacío deshabilitado (para relleno)
                    using (new EditorGUI.DisabledScope(true))
                        GUI.Button(cell, GUIContent.none, EditorStyles.miniButton);
                    continue;
                }

                DateTime d = new DateTime(first.Year, first.Month, dayNum);
                bool isToday = d == today;
                bool isSelected = d == selected;

                // Fondo fin de semana (suave)
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                    EditorGUI.DrawRect(new Rect(cell.x, cell.y + 1, cell.width, cell.height - 2), new Color(0, 0, 0, 0.05f));

                // Seleccionado -> fondo acento
                if (isSelected)
                    EditorGUI.DrawRect(new Rect(cell.x + 1, cell.y + 1, cell.width - 2, cell.height - 2), new Color(0.25f, 0.5f, 1f, 0.65f));

                var style = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = isToday ? 12 : 12,
                    fontStyle = isToday ? FontStyle.Bold : FontStyle.Normal,
                    fixedHeight = cellH
                };

                if (GUI.Button(cell, dayNum.ToString(), style))
                {
                    selected = d;
                    onPick?.Invoke(selected);
                    editorWindow.Close();
                }

                dayNum++;
            }
        }
    }
}
#endif