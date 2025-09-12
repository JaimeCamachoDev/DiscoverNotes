#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

[CustomEditor(typeof(GameObjectNotes))]
public class GameObjectNotesEditor : Editor
{
    private GameObjectNotes tgt;
    private GUIStyle squareIconBtn;
    private Texture2D _btnBgNormal, _btnBgHover, _btnBgActive;

    // ---- Card background (modo Edición) ----
    private GUIStyle cardStyle;
    private static Texture2D solidTex;
    private static Color lastCardBg = new Color(0, 0, 0, 0);

    // ---- Estilos ----
    private GUIStyle notesAreaStyle;     // Edición (sin rich)
    private GUIStyle ttTitleStyle;       // Fijo
    private GUIStyle ttBodyStyle;        // Fijo (richText, wrap)
    private bool stylesReady;

    // ---- Constantes visuales ----
    const float HEADER_STRIP = 4f;
    const float PADDING = 10f;
    const float ICON = 16f;
    const float ICON_PAD = 6f;

    private const string NotesControlName = "NOTES_TEXTAREA_CONTROL";

    // === Config de edición (sin recentrado automático) ===
    const int EDIT_VISIBLE_LINES = 8;     // cambia a 3 si prefieres
    const float EDIT_WHEEL_MULT = 1.0f;  // nº de líneas por “tick” de rueda

    // --- Enlaces markdown: [Nombre](id)
    static readonly Regex RX_LINK = new Regex(@"\[(?<name>[^\]]+)\]\((?<id>[^)]+)\)", RegexOptions.Compiled);

    // ---- Cache de preview por instancia ----
    class PreviewCache
    {
        public int textHash;
        public int metaHash;
        public float width;
        public Color bg, accent;

        public GUIContent titleGC;
        public float titleH;
        public Texture icon;
        public bool preferDark;

        public GUIContent bodyGC;
        public float bodyH;

        public float innerWidth;

        public IndexMap indexMap;

        public readonly List<LinkInfo> links = new List<LinkInfo>();
    }

    class IndexMap
    {
        public int[] str2vis;
        public int[] vis2str;
        public int visibleLen;
        public string text;
    }

    class LinkInfo
    {
        public string name;
        public string id;
        public int strStart;
        public int strEnd;
        public int vStart;
        public int vEnd;
        public readonly List<Rect> hitRects = new List<Rect>();
    }

    static readonly Dictionary<int, PreviewCache> s_preview = new Dictionary<int, PreviewCache>();
    static readonly Dictionary<int, bool> s_fixedBodyCollapsed = new Dictionary<int, bool>();

    void OnEnable() => tgt = (GameObjectNotes)target;

    void EnsureStyles()
    {
        if (stylesReady) return;

        if (solidTex == null)
        {
            solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            solidTex.SetPixel(0, 0, Color.white);
            solidTex.Apply(false, false);
        }

        cardStyle = new GUIStyle
        {
            normal = { background = solidTex },
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(10, 10, 12, 12)
        };

        notesAreaStyle = new GUIStyle(EditorStyles.textArea)
        {
            richText = false,
            wordWrap = true,
            fontSize = 12,
            padding = new RectOffset(10, 10, 10, 10)
        };

        ttTitleStyle = new GUIStyle(EditorStyles.boldLabel) { richText = true, wordWrap = true, fontSize = 13 };
        ttBodyStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true, fontSize = 12 };

        if (squareIconBtn == null)
        {
            bool pro = EditorGUIUtility.isProSkin;
            Color n = pro ? new Color(1, 1, 1, 0.06f) : new Color(0, 0, 0, 0.06f);
            Color h = pro ? new Color(1, 1, 1, 0.10f) : new Color(0, 0, 0, 0.10f);
            Color a = pro ? new Color(1, 1, 1, 0.16f) : new Color(0, 0, 0, 0.16f);

            _btnBgNormal = MakeTex(n);
            _btnBgHover = MakeTex(h);
            _btnBgActive = MakeTex(a);

            squareIconBtn = new GUIStyle(GUIStyle.none)
            {
                fixedWidth = 20f,
                fixedHeight = 20f,
                alignment = TextAnchor.MiddleCenter,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
            squareIconBtn.normal.background = _btnBgNormal;
            squareIconBtn.hover.background = _btnBgHover;
            squareIconBtn.active.background = _btnBgActive;
        }

        stylesReady = true;
    }

    static Texture2D MakeTex(Color c)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        t.SetPixel(0, 0, c);
        t.Apply(false, false);
        return t;
    }

    public override void OnInspectorGUI()
    {
        EnsureStyles();
        serializedObject.Update();

        var pMode = serializedObject.FindProperty("displayMode");
        var mode = (GameObjectNotes.DisplayMode)pMode.enumValueIndex;

        if (mode == GameObjectNotes.DisplayMode.Edit)
        {
            var catEdit = NoteStylesProvider.FindCategory(tgt.Category);
            var cardBg = catEdit != null ? catEdit.cardBackground : Color.white;

            if (lastCardBg != cardBg)
            {
                solidTex.SetPixel(0, 0, cardBg);
                solidTex.Apply(false, false);
                lastCardBg = cardBg;
            }

            GUILayout.BeginVertical(cardStyle);

            DrawHeaderToolbar(pMode, true);
            DrawEditableMeta();
            DrawEditableNotes_NoAutoCenter(); // SIN recentrado

            GUILayout.EndVertical();
        }
        else
        {
            DrawFixedLikeTooltip_WithCollapse(pMode);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void DrawHeaderToolbar(SerializedProperty pMode, bool showTitle)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (showTitle) EditorGUILayout.LabelField("Notas", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            bool isEdit = pMode.enumValueIndex == (int)GameObjectNotes.DisplayMode.Edit;
            var icon = GetModeLockIcon(isEdit);
            var tip = isEdit ? "Fijar (cerrar candado)" : "Editar (abrir candado)";
            var content = new GUIContent(icon.image, tip);

            Rect r = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f), GUILayout.Height(20f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            if (GUI.Button(r, content, squareIconBtn))
            {
                pMode.enumValueIndex = isEdit ? (int)GameObjectNotes.DisplayMode.Fixed : (int)GameObjectNotes.DisplayMode.Edit;
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);
                s_preview.Remove(tgt.GetInstanceID());
            }
        }
        GUILayout.Space(4);
    }

    GUIContent GetModeLockIcon(bool isEditNow)
    {
        if (isEditNow)
        {
            var gc = EditorIconHelper.TryIcon(
                "d_LockIcon", "LockIcon",
                "d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow"
            );
            if (gc != null && gc.image != null) return gc;
        }
        else
        {
            var gc = EditorIconHelper.TryIcon(
                "d_LockIcon-On", "LockIcon-On",
                "d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow"
            );
            if (gc != null && gc.image != null) return gc;
        }
        return EditorIconHelper.TryIcon("d_LockIcon", "LockIcon");
    }

    GUIContent GetModeToggleIcon(bool isEditNow)
    {
        if (isEditNow)
        {
            var gc = EditorIconHelper.TryIcon(
                "d_LockIcon-On", "LockIcon-On",
                "d_LockIcon", "LockIcon"
            );
            if (gc != null && gc.image != null) return gc;
        }
        else
        {
            var gc = EditorIconHelper.TryIcon(
                "d_EditCollider", "EditCollider",
                "d_editicon", "editicon"
            );
            if (gc != null && gc.image != null) return gc;
        }
        return EditorIconHelper.TryIcon("d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow");
    }

    // --- MODO EDICIÓN: meta editable ---
    void DrawEditableMeta()
    {
        var pAuthor = serializedObject.FindProperty("author");
        var pCategory = serializedObject.FindProperty("category");

        float rowH = Mathf.Max(EditorGUIUtility.singleLineHeight + 6f, 22f);
        Rect row = EditorGUILayout.GetControlRect(false, rowH);

        float gap = 8f;
        float half = (row.width - gap) * 0.5f;

        // AHORA: izquierda = Categoría, derecha = Autor
        Rect left = new Rect(row.x, row.y, half, row.height);
        Rect right = new Rect(left.xMax + gap, row.y, half, row.height);

        // Cat a la izquierda
        EditorIconHelper.DrawIconPopupCategory(left, pCategory, NoteStylesProvider.GetCategoryNames());
        // Autor a la derecha
        EditorIconHelper.DrawIconPopupAuthor(right, pAuthor, NoteStylesProvider.GetAuthors());

        // Fecha debajo (igual que antes)
        var pDate = serializedObject.FindProperty("dateCreated");
        Rect row2 = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4);
        string label = string.IsNullOrEmpty(pDate.stringValue) ? DateTime.Now.ToString("dd/MM/yyyy") : pDate.stringValue;

        if (GUI.Button(row2, new GUIContent(label, EditorIconHelper.GetCalendarIcon().image)))
        {
            DateTime initial = ParseDateOrToday(label);
            var screenPos = GUIUtility.GUIToScreenPoint(new Vector2(row2.x, row2.y));
            PopupWindow.Show(new Rect(screenPos, row2.size), new CalendarPopup(initial, picked =>
            {
                pDate.stringValue = picked.ToString("dd/MM/yyyy");
                serializedObject.ApplyModifiedProperties();
            }));
        }
    }


    // --- MODO EDICIÓN: TextArea de 4 líneas, scroll SOLO con la rueda (sin recentrar) ---
    void DrawEditableNotes_NoAutoCenter()
    {
        var pNotes = serializedObject.FindProperty("notes");
        EditorGUILayout.LabelField("Contenido (soporta rich text y enlaces a otros assets)", EditorStyles.boldLabel);

        string text = pNotes.stringValue ?? string.Empty;

        // 1) Altura fija = N líneas visibles
        float lineH = GetStyleLineHeight(notesAreaStyle);
        float viewHeight = notesAreaStyle.padding.vertical + lineH * EDIT_VISIBLE_LINES;

        // 2) Rect fijo con ese alto
        Rect areaRect = GUILayoutUtility.GetRect(0, viewHeight, GUILayout.ExpandWidth(true));

        // 3) Alto real del contenido con el ancho real del rect
        float contentHeight = notesAreaStyle.CalcHeight(new GUIContent(text), areaRect.width);

        // 4) Rueda del ratón -> mover scroll interno (dirección corregida)
        HandleMouseWheelOverTextArea(areaRect, contentHeight, viewHeight, lineH);

        // 5) Dibujar TextArea (sin cambios de vista automáticos)
        GUI.SetNextControlName(NotesControlName);
        string after = GUI.TextArea(areaRect, text, notesAreaStyle);

        if (after != text)
        {
            pNotes.stringValue = after;
            // No recalculamos ni tocamos scroll aquí: ningún recentrado automático
        }

        // 6) DnD de objetos a formato [Nombre](GlobalObjectId)
        HandleDragAndDropIntoTextArea(areaRect, pNotes);
    }

    float GetStyleLineHeight(GUIStyle style)
    {
        if (style.lineHeight > 0f) return style.lineHeight;
        return style.CalcSize(new GUIContent("Ay")).y;
    }

    // Rueda del ratón -> desplazamos TextEditor.scrollOffset.y
    void HandleMouseWheelOverTextArea(Rect areaRect, float contentHeight, float viewHeight, float lineH)
    {
        var e = Event.current;
        if (e.type != EventType.ScrollWheel) return;
        if (!areaRect.Contains(e.mousePosition)) return;

        var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
        if (te == null) return;

        float step = lineH * EDIT_WHEEL_MULT;

        // Dirección corregida: rueda abajo -> baja el contenido (aumenta offset)
        float target = te.scrollOffset.y + (e.delta.y * step);

        // Clamp SOLO cuando el usuario scrollea (no en edición)
        float max = Mathf.Max(0f, contentHeight - viewHeight);
        te.scrollOffset = new Vector2(te.scrollOffset.x, Mathf.Clamp(target, 0f, max));

        e.Use();
        GUI.changed = true;
    }

    // DnD idéntico al que teníamos pero usando el rect fijo de la TextArea
    void HandleDragAndDropIntoTextArea(Rect areaRect, SerializedProperty pNotes)
    {
        var e = Event.current;
        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !areaRect.Contains(e.mousePosition))
            return;

        var refs = DragAndDrop.objectReferences;
        if (refs == null || refs.Length == 0) return;

        var pairs = new List<(string display, string gid)>();
        foreach (var obj in refs)
        {
            if (obj == null) continue;
            if (TryBuildLinkForObject(obj, out string display, out string gid))
                pairs.Add((display, gid));
        }

        if (pairs.Count == 0)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            GUI.FocusControl(NotesControlName);
            var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);

            int start = 0, end = 0;
            string cur = pNotes.stringValue ?? string.Empty;

            if (te != null) { start = Mathf.Min(te.cursorIndex, te.selectIndex); end = Mathf.Max(te.cursorIndex, te.selectIndex); }

            string before = cur.Substring(0, start);
            string after = cur.Substring(end);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append('[').Append(pairs[i].display).Append(']').Append('(').Append(pairs[i].gid).Append(')');
            }

            string insert = sb.ToString();
            string composed = before + insert + after;

            pNotes.stringValue = composed;

            if (te != null)
                te.cursorIndex = te.selectIndex = (before.Length + insert.Length);

            e.Use();
            GUI.changed = true;
        }
    }

    static bool TryBuildLinkForObject(UnityEngine.Object obj, out string display, out string gid)
    {
        display = null;
        gid = null;

        try
        {
            gid = UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(gid)) return false;

        display = GetNiceDisplayName(obj);
        return !string.IsNullOrEmpty(display);
    }

    static string GetNiceDisplayName(UnityEngine.Object obj)
    {
        if (obj is Component comp)
            return $"{comp.gameObject.name}.{comp.GetType().Name}";

        if (obj is GameObject go)
            return go.name;

        string path = UnityEditor.AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(path))
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path))
                return System.IO.Path.GetFileName(path).TrimEnd('/') + "/";

            return obj.name;
        }

        return obj.name ?? "(objeto)";
    }

    // ======== MODO FIJO: tooltip + colapsar ========
    GUIContent GetCollapseIcon(bool collapsed)
    {
        if (collapsed)
        {
            var gc = EditorIconHelper.TryIcon(
                "d_scenevis_visible", "scenevis_visible",
                "d_Eye", "Eye"
            );
            if (gc != null && gc.image != null) return gc;
        }
        else
        {
            var gc = EditorIconHelper.TryIcon(
                "d_scenevis_hidden", "scenevis_hidden",
                "d_Toolbar Minus", "Toolbar Minus"
            );
            if (gc != null && gc.image != null) return gc;
        }
        return EditorIconHelper.TryIcon("d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow");
    }

    void DrawFixedLikeTooltip_WithCollapse(SerializedProperty pMode)
    {
        var cat = NoteStylesProvider.FindCategory(tgt.Category);
        var bg = (cat != null ? cat.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 0.985f));
        var accent = (cat != null ? cat.tooltipAccentBar : new Color(0.25f, 0.5f, 1f, 1f));
        bg.a = 1f;

        string author = string.IsNullOrEmpty(tgt.Author) ? "Anónimo" : tgt.Author;
        string date = string.IsNullOrEmpty(tgt.DateCreated) ? DateTime.Now.ToString("dd/MM/yyyy") : tgt.DateCreated;
        string catName = string.IsNullOrEmpty(tgt.Category) ? "Nota" : tgt.Category;

        var pNotes = serializedObject.FindProperty("notes");
        string raw = pNotes.stringValue ?? string.Empty;

        int id = tgt.GetInstanceID();
        if (!s_preview.TryGetValue(id, out var cache)) { cache = new PreviewCache(); s_preview[id] = cache; }

        int textHash = raw.GetHashCode();
        int metaHash = (catName + "|" + author + "|" + date).GetHashCode();
        float availWidth = EditorGUIUtility.currentViewWidth - 20f;
        if (availWidth <= 0f) availWidth = 400f;

        if (cache.textHash != textHash || cache.metaHash != metaHash
            || Mathf.Abs(cache.width - availWidth) > 0.5f
            || cache.bg != bg || cache.accent != accent)
        {
            cache.textHash = textHash;
            cache.metaHash = metaHash;
            cache.width = availWidth;
            cache.bg = bg;
            cache.accent = accent;

            cache.icon = (cat != null && cat.icon != null) ? cat.icon : (EditorIconHelper.GetCategoryIcon(tgt.Category)?.image);
            cache.titleGC = new GUIContent($"<b>{catName}</b>  •  {author}  •  {date}");

            string displayStyled;
            BuildStyledAndLinksAndMap(raw, out displayStyled, cache.links, out cache.indexMap);

            cache.bodyGC = new GUIContent(displayStyled);

            float innerW = availWidth - (PADDING * 2f);
            float titleAvailW = innerW - (cache.icon != null ? (ICON + ICON_PAD) : 0f);

            cache.titleH = ttTitleStyle.CalcHeight(cache.titleGC, Mathf.Max(50f, titleAvailW));
            if (cache.icon != null) cache.titleH = Mathf.Max(cache.titleH, ICON);

            cache.bodyH = ttBodyStyle.CalcHeight(cache.bodyGC, innerW);
            cache.preferDark = (0.2126f * cache.bg.r + 0.7152f * cache.bg.g + 0.0722f * cache.bg.b) > 0.5f;

            cache.innerWidth = innerW;
        }

        bool collapsed = s_fixedBodyCollapsed.TryGetValue(id, out var v) && v;

        float effectiveBodyH = collapsed ? 0f : cache.bodyH;
        float totalH = HEADER_STRIP + PADDING * 2f + cache.titleH + 4f + effectiveBodyH;

        Rect outer = GUILayoutUtility.GetRect(0, totalH, GUILayout.ExpandWidth(true));
        if (outer.width <= 1f) return;

        GUI.BeginGroup(outer);
        DrawTooltipBackground(new Rect(0, 0, outer.width, totalH), cache.bg, cache.accent);

        ttTitleStyle.normal.textColor = cache.preferDark ? Color.black : Color.white;
        ttBodyStyle.normal.textColor = cache.preferDark ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.95f, 0.95f, 0.97f, 1f);

        Rect inner = new Rect(PADDING, PADDING + HEADER_STRIP, outer.width - PADDING * 2f, totalH - PADDING * 2f - HEADER_STRIP);

        // Título
        Rect titleR = new Rect(inner.x, inner.y, inner.width, cache.titleH);
        if (cache.icon != null)
        {
            var iconR = new Rect(titleR.x, titleR.y + Mathf.Floor((cache.titleH - ICON) * 0.5f), ICON, ICON);
            GUI.DrawTexture(iconR, cache.icon, ScaleMode.ScaleToFit, true);
            titleR.x += ICON + ICON_PAD;
            titleR.width -= ICON + ICON_PAD;
        }

        // Botones (colapsar + candado)
        {
            bool isEdit = pMode.enumValueIndex == (int)GameObjectNotes.DisplayMode.Edit;
            const float BTN = 20f;
            float btnX = inner.x + inner.width - BTN;

            // Candado
            var lockIcon = GetModeLockIcon(isEdit);
            Rect lockR = new Rect(btnX, titleR.y + Mathf.Floor((cache.titleH - BTN) * 0.5f), BTN, BTN);
            EditorGUIUtility.AddCursorRect(lockR, MouseCursor.Link);
            if (GUI.Button(lockR, new GUIContent(lockIcon.image, isEdit ? "Fijar" : "Editar"), squareIconBtn))
            {
                pMode.enumValueIndex = isEdit ? (int)GameObjectNotes.DisplayMode.Fixed : (int)GameObjectNotes.DisplayMode.Edit;
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);
                s_preview.Remove(tgt.GetInstanceID());
            }

            // Colapsar (a la izquierda)
            btnX -= (BTN + 6f);
            var collapseIcon = GetCollapseIcon(collapsed);
            Rect colR = new Rect(btnX, lockR.y, BTN, BTN);
            EditorGUIUtility.AddCursorRect(colR, MouseCursor.Link);
            if (GUI.Button(colR, new GUIContent(collapseIcon.image, collapsed ? "Mostrar cuerpo" : "Ocultar cuerpo"), squareIconBtn))
            {
                s_fixedBodyCollapsed[id] = !collapsed;
            }

            // Evitar solape con botones
            titleR.width -= (BTN * 2f + 12f);
        }

        GUI.Label(titleR, cache.titleGC, ttTitleStyle);

        if (!collapsed)
        {
            Rect bodyR = new Rect(inner.x, inner.y + cache.titleH + 4f, inner.width, cache.bodyH);
            GUI.Label(bodyR, cache.bodyGC, ttBodyStyle);

            LayoutLinkHitRects(bodyR, cache);
            DrawLinkButtons(cache);
            HandleClickFallback(bodyR, cache);
        }

        GUI.EndGroup();
    }

    void DrawTooltipBackground(Rect r, Color bg, Color accent)
    {
        EditorGUI.DrawRect(new Rect(0, 0, r.width, r.height), bg);
        EditorGUI.DrawRect(new Rect(0, 0, r.width, HEADER_STRIP), accent);
        var border = new Color(1f, 1f, 1f, 0.08f);
        EditorGUI.DrawRect(new Rect(0, 0, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(0, r.height - 1, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(0, 0, 1, r.height), border);
        EditorGUI.DrawRect(new Rect(r.width - 1, 0, 1, r.height), border);
    }

    void BuildStyledAndLinksAndMap(string raw, out string displayStyled, List<LinkInfo> linksOut, out IndexMap map)
    {
        linksOut.Clear();
        var sb = new StringBuilder(raw.Length + 64);

        int last = 0;
        foreach (Match m in RX_LINK.Matches(raw))
        {
            if (m.Index > last) sb.Append(raw, last, m.Index - last);

            string rawName = m.Groups["name"].Value;
            string id = m.Groups["id"].Value;

            const string prefix = "<color=#4EA3FF><b>";
            const string suffix = "</b></color>";

            int strStart = sb.Length + prefix.Length;
            sb.Append(prefix).Append(rawName).Append(suffix);
            int strEnd = strStart + rawName.Length;

            linksOut.Add(new LinkInfo
            {
                name = rawName,
                id = id,
                strStart = strStart,
                strEnd = strEnd
            });

            last = m.Index + m.Length;
        }
        if (last < raw.Length) sb.Append(raw, last, raw.Length - last);

        displayStyled = sb.ToString();
        map = BuildVisibleIndexMapForIMGUI(displayStyled);

        foreach (var li in linksOut)
        {
            li.vStart = SafeStrToVis(map, li.strStart);
            li.vEnd = SafeStrToVis(map, li.strEnd);
        }
    }

    static int SafeStrToVis(IndexMap map, int strIndex)
    {
        strIndex = Mathf.Clamp(strIndex, 0, map.str2vis.Length - 1);
        return map.str2vis[strIndex];
    }

    static IndexMap BuildVisibleIndexMapForIMGUI(string text)
    {
        var map = new IndexMap { text = text };
        int n = text.Length;
        map.str2vis = new int[n + 1];

        List<int> vis2strList = new List<int>(n + 1);

        int i = 0;
        int v = 0;
        vis2strList.Add(0);

        while (i < n)
        {
            if (TryGetValidIMGUIRichTag(text, i, out int tagLen))
            {
                for (int k = 0; k < tagLen; k++) map.str2vis[i + k] = v;
                i += tagLen;
                continue;
            }

            map.str2vis[i] = v;
            i++;
            v++;
            vis2strList.Add(i);
        }

        map.str2vis[n] = v;
        vis2strList.Add(n);

        map.vis2str = vis2strList.ToArray();
        map.visibleLen = v;
        return map;
    }

    static bool TryGetValidIMGUIRichTag(string text, int i, out int tagLen)
    {
        tagLen = 0;
        if (text[i] != '<') return false;
        int gt = text.IndexOf('>', i + 1);
        if (gt < 0) return false;

        string body = text.Substring(i + 1, gt - (i + 1));
        string lower = body.Trim().ToLowerInvariant();

        if (lower == "b" || lower == "/b" || lower == "i" || lower == "/i" ||
            lower == "/color" || lower == "/size") { tagLen = gt - i + 1; return true; }

        if (lower.StartsWith("size=")) { tagLen = gt - i + 1; return true; }

        if (lower.StartsWith("color="))
        {
            var val = lower.Substring("color=".Length).Trim().Trim('\'', '"');
            if (LooksLikeValidColorToken(val)) { tagLen = gt - i + 1; return true; }
            return false;
        }

        return false;
    }

    static bool LooksLikeValidColorToken(string v)
    {
        if (string.IsNullOrEmpty(v)) return false;
        if (v[0] == '#') { int L = v.Length; return (L == 4 || L == 5 || L == 7 || L == 9); }
        switch (v)
        {
            case "red":
            case "green":
            case "blue":
            case "black":
            case "white":
            case "yellow":
            case "cyan":
            case "magenta":
            case "grey":
            case "gray":
                return true;
            default: return false;
        }
    }

    void LayoutLinkHitRects(Rect bodyR, PreviewCache cache)
    {
        foreach (var l in cache.links) l.hitRects.Clear();

        if (cache.links.Count == 0 || string.IsNullOrEmpty(cache.bodyGC.text))
            return;

        float lineH = ttBodyStyle.lineHeight > 0 ? ttBodyStyle.lineHeight : ttBodyStyle.CalcSize(new GUIContent("Ay")).y;

        foreach (var li in cache.links)
        {
            int vStart = Mathf.Clamp(li.vStart, 0, cache.indexMap.visibleLen);
            int vEnd = Mathf.Clamp(li.vEnd, 0, cache.indexMap.visibleLen);
            if (vEnd <= vStart) continue;

            bool hasCurrent = false;
            float curY = 0f, minX = 0f, maxX = 0f;

            for (int v = vStart; v < vEnd; v++)
            {
                Vector2 a = ttBodyStyle.GetCursorPixelPosition(bodyR, cache.bodyGC, v);
                Vector2 b = ttBodyStyle.GetCursorPixelPosition(bodyR, cache.bodyGC, v + 1);

                float y = a.y;
                float w = b.x - a.x;
                bool newLine = (Mathf.Abs(b.y - a.y) > 0.001f) || (w <= 0.001f);

                if (!hasCurrent)
                {
                    hasCurrent = true;
                    curY = y;
                    minX = a.x;
                    maxX = Mathf.Max(a.x, b.x);
                }
                else if (newLine || Mathf.Abs(y - curY) > 0.5f)
                {
                    if (maxX > minX) li.hitRects.Add(new Rect(minX, curY, maxX - minX, lineH));
                    curY = y;
                    minX = a.x;
                    maxX = Mathf.Max(a.x, b.x);
                }
                else
                {
                    maxX = Mathf.Max(maxX, b.x);
                }
            }

            if (hasCurrent && maxX > minX)
                li.hitRects.Add(new Rect(minX, curY, maxX - minX, lineH));

            foreach (var r in li.hitRects)
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
        }
    }

    void DrawLinkButtons(PreviewCache cache)
    {
        foreach (var li in cache.links)
        {
            foreach (var r in li.hitRects)
            {
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    SelectLink(li);
                    return;
                }
            }
        }
    }

    void HandleClickFallback(Rect bodyR, PreviewCache cache)
    {
        var e = Event.current;
        if (e.type != EventType.MouseUp || e.button != 0) return;

        int vClick = ttBodyStyle.GetCursorStringIndex(bodyR, cache.bodyGC, e.mousePosition);

        foreach (var li in cache.links)
        {
            if (vClick >= li.vStart && vClick < li.vEnd)
            {
                SelectLink(li);
                e.Use();
                return;
            }
        }
    }

    void SelectLink(LinkInfo li)
    {
        if (UnityEditor.GlobalObjectId.TryParse(li.id, out var goid))
        {
            var obj = UnityEditor.GlobalObjectId.GlobalObjectIdentifierToObjectSlow(goid);
            if (obj != null)
            {
                var toPing = (obj is Component c) ? (UnityEngine.Object)c.gameObject : obj;
                EditorGUIUtility.PingObject(toPing);
            }
            else
            {
                Debug.LogWarning($"No se pudo resolver el GlobalObjectId: {li.id}");
            }
        }
        else
        {
            Debug.LogWarning($"Formato de GlobalObjectId inválido: {li.id}");
        }
    }

    DateTime ParseDateOrToday(string s)
    {
        if (DateTime.TryParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)) return d;
        return DateTime.Now.Date;
    }
}
#endif
