#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using System.Text.RegularExpressions;


[CustomEditor(typeof(GameObjectNotes))]
public class GameObjectNotesEditor : Editor
{
    private GameObjectNotes tgt;

    // ===== UI =====
    private GUIStyle squareIconBtn;
    private Texture2D _btnBgNormal, _btnBgHover, _btnBgActive;
    private GUIStyle cardStyle, notesAreaStyle, ttTitleStyle, ttBodyStyle;
    private static Texture2D solidTex;
    private static Color lastCardBg = new Color(0, 0, 0, 0);
    private bool stylesReady;

    // Constantes de layout
    const float HEADER_STRIP = 4f;
    const float PADDING = 10f;
    const float ICON = 16f;
    const float ICON_PAD = 6f;

    static readonly GUIContent DiscoverHeaderContent = new GUIContent("Discover (documentación visual)");
    static readonly GUIContent DiscoverTitleContent = new GUIContent("Título");
    static readonly GUIContent DiscoverCategoryContent = new GUIContent("Categoría Discover");
    static readonly GUIContent DiscoverImageContent = new GUIContent("Imagen principal");
    static readonly GUIContent DiscoverSummaryContent = new GUIContent("Resumen");
    static readonly GUIContent DiscoverSectionsContent = new GUIContent("Secciones");
    static readonly GUIContent ActionsLabelContent = new GUIContent("Acciones");

    // Edición en TextArea (plana con scrollbar)
    private const string NotesControlName = "NOTES_TEXTAREA_CONTROL";
    private const int EDIT_MIN_LINES = 3;     // min visible
    private const int EDIT_MAX_LINES = 15;    // max visible
                                              // Posición exacta del último clic en pantalla para el calendario
    private Vector2? _calendarClickScreen;

    private Vector2 _editScroll;

    // Cache de vista fija (interpretada al dibujar)
    class PreviewCache
    {
        public int textHash, metaHash;
        public float width;
        public Color bg, accent;

        public GUIContent titleGC, bodyGC;
        public float titleH, bodyH, innerWidth;
        public Texture icon;
        public bool preferDark;

        public LinkMarkup.VisibleIndexMap indexMap;
        public readonly List<LinkMarkup.LinkSpan> links = new List<LinkMarkup.LinkSpan>();
        public readonly List<LinkMarkup.ChecklistSpan> checks = new List<LinkMarkup.ChecklistSpan>();
        public readonly List<LinkMarkup.ImageSpan> images = new List<LinkMarkup.ImageSpan>();

        public readonly List<(LinkMarkup.ImageSpan img, Texture2D tex, Rect uv, float extraBefore, float width, float height, Rect drawRect)>
            imgLayout = new List<(LinkMarkup.ImageSpan, Texture2D, Rect, float, float, float, Rect)>();
    }

    static readonly Dictionary<int, PreviewCache> s_preview = new Dictionary<int, PreviewCache>();
    static readonly Dictionary<int, bool> s_fixedBodyCollapsed = new Dictionary<int, bool>();

    static int s_lastUndoEventId = -1;

    void OnEnable() => tgt = (GameObjectNotes)target;

    public override void OnInspectorGUI()
    {
        EnsureStyles();
        serializedObject.Update();

        // Lista de notas
        var pList = serializedObject.FindProperty("notesList");
        if (pList == null)
        {
            EditorGUILayout.HelpBox("Este componente usa varias notas. Vuelve a compilar si acabas de actualizar el script.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        if (pList.arraySize == 0)
        {
            EditorGUILayout.HelpBox("Sin notas. RMB en el header del componente → "Añadir nota".", MessageType.Info);
        }

        // Dibuja cada nota como una tarjeta independiente
        for (int i = 0; i < pList.arraySize; i++)
        {
            var pNote = pList.GetArrayElementAtIndex(i);
            var pMode = pNote.FindPropertyRelative("displayMode");
            var mode = (GameObjectNotes.DisplayMode)pMode.enumValueIndex;

            GUILayout.Space(6);

            if (mode == GameObjectNotes.DisplayMode.Edit)
            {
                // Fondo de edición (como ahora)
                var catEdit = NoteStylesProvider.FindCategory(pNote.FindPropertyRelative("category").stringValue);
                var bg = catEdit != null ? catEdit.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 0.985f);
                bg.a = 1f;
                if (lastCardBg != bg)
                {
                    solidTex.SetPixel(0, 0, bg);
                    solidTex.Apply(false, false);
                    lastCardBg = bg;
                }

                GUILayout.BeginVertical(cardStyle);

                DrawHeaderToolbar_PerNote(pMode, showTitle: true);
                DrawEditableMeta_PerNote(pNote);
                DrawEditableNotes_PlainAutoHeight_PerNote(pNote, i);
                DrawDiscoverContent_Edit(pNote);

                GUILayout.EndVertical();
            }
            else
            {
                DrawFixedLikeTooltip_WithCollapse_AndRich_PerNote(pNote, i);
                DrawDiscoverContent_Fixed(pNote);
            }

            GUILayout.Space(4);

        }

        serializedObject.ApplyModifiedProperties();
    }


    // ---------- Estilos ----------
    void EnsureStyles()
    {
        if (stylesReady) return;

        if (solidTex == null)
        {
            solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            solidTex.SetPixel(0, 0, Color.white);
            solidTex.Apply(false, false);
        }

        cardStyle = new GUIStyle
        {
            normal = { background = solidTex },
            margin = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(10, 10, 12, 12)
        };

        // TextArea sin richText, solo texto plano
        notesAreaStyle = new GUIStyle(EditorStyles.textArea)
        {
            richText = false,
            wordWrap = true,
            fontSize = 12,
            padding = new RectOffset(8, 8, 8, 8)
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

    // ---------- Header ----------
    void DrawHeaderToolbar_PerNote(SerializedProperty pMode, bool showTitle)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (showTitle) EditorGUILayout.LabelField("Nota", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            bool isEdit = pMode.enumValueIndex == (int)GameObjectNotes.DisplayMode.Edit;
            var icon = GetModeLockIcon(isEdit);
            var tip = isEdit ? "Fijar (cerrar candado)" : "Editar (abrir candado)";
            var content = new GUIContent(icon.image, tip);

            Rect r = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f), GUILayout.Height(20f));
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

            if (GUI.Button(r, content, squareIconBtn))
            {
                pMode.enumValueIndex = isEdit ? (int)GameObjectNotes.DisplayMode.Fixed
                                              : (int)GameObjectNotes.DisplayMode.Edit;
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);
                // Limpia caché sólo de esta nota
                int key = NoteCacheKey(tgt.GetInstanceID(), pMode.propertyPath);
                s_preview.Remove(key);
            }
        }
        GUILayout.Space(4);
    }


    GUIContent GetModeLockIcon(bool isEditNow)
    {
        if (isEditNow)
        {
            var gc = EditorIconHelper.TryIcon("d_LockIcon", "LockIcon",
                                              "d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow");
            if (gc != null && gc.image != null) return gc;
        }
        else
        {
            var gc = EditorIconHelper.TryIcon("d_LockIcon-On", "LockIcon-On",
                                              "d_UnityEditor.InspectorWindow", "UnityEditor.InspectorWindow");
            if (gc != null && gc.image != null) return gc;
        }
        return EditorIconHelper.TryIcon("d_LockIcon", "LockIcon");
    }

    // ---------- Meta ----------
    void DrawEditableMeta_PerNote(SerializedProperty pNote)
    {
        var pAuthor = pNote.FindPropertyRelative("author");
        var pCategory = pNote.FindPropertyRelative("category");
        var pDate = pNote.FindPropertyRelative("dateCreated");

        float rowH = Mathf.Max(EditorGUIUtility.singleLineHeight + 6f, 22f);
        Rect row = EditorGUILayout.GetControlRect(false, rowH);

        float gap = 8f;
        float half = (row.width - gap) * 0.5f;

        Rect left = new Rect(row.x, row.y, half, row.height);
        Rect right = new Rect(left.xMax + gap, row.y, half, row.height);

        EditorIconHelper.DrawIconPopupCategory(left, pCategory, NoteStylesProvider.GetCategoryNames());
        EditorIconHelper.DrawIconPopupAuthor(right, pAuthor, NoteStylesProvider.GetAuthors());

        // Botón fecha con popup anclado al click real
        Rect row2 = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 4);
        string label = string.IsNullOrEmpty(pDate.stringValue) ? DateTime.Now.ToString("dd/MM/yyyy") : pDate.stringValue;

        var e = Event.current;
        if (e.type == EventType.MouseDown && row2.Contains(e.mousePosition))
            _calendarClickScreen = GUIUtility.GUIToScreenPoint(e.mousePosition);

        if (GUI.Button(row2, new GUIContent(label, EditorIconHelper.GetCalendarIcon().image)))
        {
            DateTime initial = ParseDateOrToday(label);
            Rect anchor;
            if (_calendarClickScreen.HasValue)
            {
                Vector2 p = _calendarClickScreen.Value;
                anchor = new Rect(p.x, p.y, 1f, 1f);
            }
            else
            {
                Vector2 tl = GUIUtility.GUIToScreenPoint(new Vector2(row2.x, row2.y));
                anchor = new Rect(tl.x + row2.width * 0.5f, tl.y + row2.height, 1f, 1f);
            }
            _calendarClickScreen = null;

            PopupWindow.Show(anchor, new CalendarPopup(initial, picked =>
            {
                pDate.stringValue = picked.ToString("dd/MM/yyyy");
                serializedObject.ApplyModifiedProperties();
            }));
        }
    }


    // ---------- Edición: TextArea PLANA (min/max líneas + ScrollView) ----------
    void DrawEditableNotes_PlainAutoHeight_PerNote(SerializedProperty pNote, int noteIndex)
    {
        var pBody = pNote.FindPropertyRelative("notes");
        EditorGUILayout.LabelField("Contenido (texto plano; se interpreta al mostrar)", EditorStyles.boldLabel);

        string text = pBody.stringValue ?? string.Empty;
        float viewW = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 36f);
        float targetH = notesAreaStyle.CalcHeight(new GUIContent(string.IsNullOrEmpty(text) ? " " : text), viewW);

        Rect areaRect = EditorGUILayout.GetControlRect(false, targetH, GUILayout.ExpandWidth(true));

        string controlName = $"{NotesControlName}_{noteIndex}";
        ArmUndoIfTyping_PerNote(areaRect, controlName);

        GUI.SetNextControlName(controlName);
        EditorGUI.BeginChangeCheck();
        string after = EditorGUI.TextArea(areaRect, text, notesAreaStyle);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(tgt, "Edit Notes");
            pBody.stringValue = after;
            serializedObject.ApplyModifiedProperties();
        }

        HandleDragAndDropIntoTextArea(areaRect, pBody);
        if (Event.current.type == EventType.ExecuteCommand && Event.current.commandName == "UndoRedoPerformed")
            Repaint();
    }

    void ArmUndoIfTyping_PerNote(Rect areaRect, string controlName)
    {
        var e = Event.current;
        bool focused = GUI.GetNameOfFocusedControl() == controlName;
        if (!focused) return;

        bool isKeyEvt = (e.type == EventType.KeyDown);
        bool isEditCmd =
            (e.type == EventType.ExecuteCommand || e.type == EventType.ValidateCommand) &&
            (e.commandName == "Paste" || e.commandName == "Cut" || e.commandName == "Delete" || e.commandName == "Duplicate");

        if (isKeyEvt || isEditCmd)
        {
            int evtId = e.GetHashCode();
            if (evtId != s_lastUndoEventId)
            {
                Undo.RecordObject(tgt, "Edit Notes");
                s_lastUndoEventId = evtId;
            }
        }
    }


    void HandleDragAndDropIntoTextArea(Rect areaRect, SerializedProperty pNotes)
    {
        var e = Event.current;
        if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !areaRect.Contains(e.mousePosition))
            return;

        var refs = DragAndDrop.objectReferences;
        if (refs == null || refs.Length == 0) return;

        var sb = new StringBuilder();
        for (int i = 0; i < refs.Length; i++)
        {
            var obj = refs[i];
            if (obj == null) continue;

            string gid;
            try { gid = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString(); }
            catch { continue; }

            bool isImage = (obj is Texture2D) || (obj is Sprite);
            if (sb.Length > 0) sb.Append(' ');
            if (isImage) sb.Append("[img](").Append(gid).Append(")");
            else sb.Append('[').Append(GetNiceDisplayName(obj)).Append("](").Append(gid).Append(')');
        }

        if (sb.Length == 0)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (e.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();

            string cur = pNotes.stringValue ?? string.Empty;

            // SIEMPRE al principio + con salto de línea al final de lo insertado
            string insert = sb.ToString();
            if (!insert.EndsWith("\n")) insert += "\n";
            string composed = insert + cur;

            Undo.RecordObject(tgt, "Insert DnD");
            pNotes.stringValue = composed;
            serializedObject.ApplyModifiedProperties();

            e.Use();
            GUI.changed = true;
        }
    }


    void DrawDiscoverContent_Edit(SerializedProperty pNote)
    {
        GUILayout.Space(8);
        EditorGUILayout.LabelField(DiscoverHeaderContent, EditorStyles.boldLabel);
        using (new EditorGUI.IndentLevelScope())
        {
            EditorGUILayout.PropertyField(pNote.FindPropertyRelative("discoverName"), DiscoverTitleContent);
            EditorGUILayout.PropertyField(pNote.FindPropertyRelative("discoverCategory"), DiscoverCategoryContent);
            EditorGUILayout.PropertyField(pNote.FindPropertyRelative("discoverImage"), DiscoverImageContent);
            EditorGUILayout.PropertyField(pNote.FindPropertyRelative("discoverSummary"), DiscoverSummaryContent);
            EditorGUILayout.PropertyField(pNote.FindPropertyRelative("discoverSections"), DiscoverSectionsContent, true);
        }
    }

    void DrawDiscoverContent_Fixed(SerializedProperty pNote)
    {
        var pName = pNote.FindPropertyRelative("discoverName");
        var pCategory = pNote.FindPropertyRelative("discoverCategory");
        var pImage = pNote.FindPropertyRelative("discoverImage");
        var pSummary = pNote.FindPropertyRelative("discoverSummary");
        var pSections = pNote.FindPropertyRelative("discoverSections");

        bool hasName = pName != null && !string.IsNullOrWhiteSpace(pName.stringValue);
        bool hasSummary = pSummary != null && !string.IsNullOrWhiteSpace(pSummary.stringValue);
        bool hasImage = pImage != null && pImage.objectReferenceValue != null;
        bool hasSections = pSections != null && pSections.isArray && pSections.arraySize > 0;

        if (!hasName && !hasSummary && !hasImage && !hasSections)
            return;

        GUILayout.Space(6);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Discover", EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                if (hasName)
                    EditorGUILayout.LabelField(pName.stringValue, EditorStyles.label);

                string catLabel = pCategory != null
                    ? ((DiscoverCategory)pCategory.enumValueIndex).ToString()
                    : DiscoverCategory.Other.ToString();
                EditorGUILayout.LabelField("Categoría", catLabel, EditorStyles.miniLabel);

                if (hasImage)
                {
                    var tex = pImage.objectReferenceValue as Texture2D;
                    if (tex != null)
                    {
                        float maxHeight = 140f;
                        GUILayout.Label(tex, GUILayout.Height(Mathf.Min(maxHeight, tex.height)));
                    }
                }

                if (hasSummary)
                {
                    EditorGUILayout.LabelField(pSummary.stringValue, EditorStyles.wordWrappedLabel);
                }

                DrawDiscoverSectionsFixed(pSections);
            }
        }
    }

    void DrawDiscoverSectionsFixed(SerializedProperty pSections)
    {
        if (pSections == null || !pSections.isArray || pSections.arraySize == 0) return;

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Secciones", EditorStyles.boldLabel);

        for (int i = 0; i < pSections.arraySize; i++)
        {
            var pSection = pSections.GetArrayElementAtIndex(i);
            if (pSection == null) continue;

            var pSecName = pSection.FindPropertyRelative("sectionName");
            var pSecImage = pSection.FindPropertyRelative("image");
            var pSecContent = pSection.FindPropertyRelative("sectionContent");
            var pActions = pSection.FindPropertyRelative("actions");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string secName = (pSecName != null && !string.IsNullOrWhiteSpace(pSecName.stringValue))
                    ? pSecName.stringValue
                    : $"Sección {i + 1}";
                EditorGUILayout.LabelField(secName, EditorStyles.boldLabel);

                if (pSecImage != null && pSecImage.objectReferenceValue is Texture2D secTex)
                {
                    GUILayout.Label(secTex, GUILayout.Height(Mathf.Min(100f, secTex.height)));
                }

                if (pSecContent != null && !string.IsNullOrWhiteSpace(pSecContent.stringValue))
                {
                    EditorGUILayout.LabelField(pSecContent.stringValue, EditorStyles.wordWrappedLabel);
                }

                DrawDiscoverActionsFixed(pActions);
            }
        }
    }

    void DrawDiscoverActionsFixed(SerializedProperty pActions)
    {
        if (pActions == null || !pActions.isArray || pActions.arraySize == 0) return;

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField(ActionsLabelContent, EditorStyles.miniBoldLabel);

        for (int i = 0; i < pActions.arraySize; i++)
        {
            var pAction = pActions.GetArrayElementAtIndex(i);
            if (pAction == null) continue;

            var pDesc = pAction.FindPropertyRelative("description");
            var pTarget = pAction.FindPropertyRelative("target");
            var pHint = pAction.FindPropertyRelative("hint");

            string desc = (pDesc != null && !string.IsNullOrWhiteSpace(pDesc.stringValue))
                ? pDesc.stringValue
                : $"Acción {i + 1}";
            EditorGUILayout.LabelField(desc, EditorStyles.label);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(GUIContent.none, pTarget?.objectReferenceValue, typeof(GameObject), true);
                }

                using (new EditorGUI.DisabledScope(pTarget == null || pTarget.objectReferenceValue == null))
                {
                    if (GUILayout.Button("Ir", GUILayout.Width(40f)))
                    {
                        var go = pTarget.objectReferenceValue as GameObject;
                        if (go != null)
                        {
                            Selection.activeObject = go;
                            if (SceneView.lastActiveSceneView != null)
                                SceneView.lastActiveSceneView.FrameSelected();
                        }
                    }
                }
            }

            if (pHint != null && !string.IsNullOrWhiteSpace(pHint.stringValue))
            {
                EditorGUILayout.LabelField(pHint.stringValue, EditorStyles.wordWrappedMiniLabel);
            }
        }
    }


    static string GetNiceDisplayName(UnityEngine.Object obj)
    {
        if (obj is Component comp) return $"{comp.gameObject.name}.{comp.GetType().Name}";
        if (obj is GameObject go) return go.name;
        string path = AssetDatabase.GetAssetPath(obj);
        if (!string.IsNullOrEmpty(path))
        {
            if (AssetDatabase.IsValidFolder(path)) return System.IO.Path.GetFileName(path).TrimEnd('/') + "/";
            return obj.name;
        }
        return obj.name ?? "(objeto)";
    }

    void DrawFixedLikeTooltip_WithCollapse_AndRich_PerNote(SerializedProperty pNote, int noteIndex)
    {
        string category = pNote.FindPropertyRelative("category").stringValue ?? "Info";
        string author = pNote.FindPropertyRelative("author").stringValue;
        string date = pNote.FindPropertyRelative("dateCreated").stringValue;
        var pBody = pNote.FindPropertyRelative("notes");
        string raw = pBody.stringValue ?? string.Empty;

        var cat = NoteStylesProvider.FindCategory(category);
        var bg = (cat != null ? cat.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 0.985f));
        var accent = (cat != null ? cat.tooltipAccentBar : new Color(0.25f, 0.5f, 1f, 1f));
        bg.a = 1f;

        int key = NoteCacheKey(tgt.GetInstanceID(), pNote.propertyPath);
        if (!s_preview.TryGetValue(key, out var cache)) { cache = new PreviewCache(); s_preview[key] = cache; }

        int textHash = raw.GetHashCode();
        int metaHash = (category + "|" + author + "|" + date).GetHashCode();
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

            cache.icon = (cat != null && cat.icon != null) ? cat.icon : (EditorIconHelper.GetCategoryIcon(category)?.image);
            cache.titleGC = new GUIContent($"<b>{category}</b>  •  {(string.IsNullOrEmpty(author) ? "Anónimo" : author)}  •  {(string.IsNullOrEmpty(date) ? DateTime.Now.ToString("dd/MM/yyyy") : date)}");

            cache.links.Clear(); cache.checks.Clear(); cache.images.Clear();
            string displayStyled = LinkMarkup.BuildStyled(raw, cache.links, cache.checks, cache.images, out cache.indexMap);
            cache.bodyGC = new GUIContent(displayStyled);

            float innerW = availWidth - (PADDING * 2f);
            float titleAvailW = innerW - (cache.icon != null ? (ICON + ICON_PAD) : 0f);

            cache.titleH = ttTitleStyle.CalcHeight(cache.titleGC, Mathf.Max(50f, titleAvailW));
            if (cache.icon != null) cache.titleH = Mathf.Max(cache.titleH, ICON);

            float textH = ttBodyStyle.CalcHeight(cache.bodyGC, innerW);
            ComputeImageLayout(cache, innerW, ttBodyStyle, out float extraH);

            cache.bodyH = textH + extraH;
            cache.preferDark = (0.2126f * cache.bg.r + 0.7152f * cache.bg.g + 0.0722f * cache.bg.b) > 0.5f;
            cache.innerWidth = innerW;
        }

        bool collapsed = s_fixedBodyCollapsed.TryGetValue(key, out var v) && v;

        ttTitleStyle.fontSize = collapsed ? 12 : 13;
        ttBodyStyle.fontSize = collapsed ? 11 : 12;

        // En colapsado forzamos altura compacta de una línea para el título
        float titleLineH = Mathf.Max(16f, GetStyleLineHeight(ttTitleStyle));
        float titleH = collapsed ? Mathf.Max(titleLineH, (cache.icon != null ? ICON : 0f)) : cache.titleH;

        float headerH = collapsed ? 0f : HEADER_STRIP;
        float padTop = collapsed ? 4f : PADDING;
        float padBot = collapsed ? 4f : PADDING;
        float padL = collapsed ? 8f : PADDING;
        float padR = collapsed ? 12f : PADDING;
        float titleGap = collapsed ? 0f : 4f;
        float effectiveBodyH = collapsed ? 0f : cache.bodyH;
        float totalH = headerH + padTop + padBot + titleH + titleGap + effectiveBodyH;

        Rect outer = GUILayoutUtility.GetRect(0, totalH, GUILayout.ExpandWidth(true));
        if (outer.width <= 1f) return;

        GUI.BeginGroup(outer);
        DrawTooltipBackground(new Rect(0, 0, outer.width, totalH), cache.bg, cache.accent, accentLeft: collapsed);

        ttTitleStyle.normal.textColor = cache.preferDark ? Color.black : Color.white;
        ttBodyStyle.normal.textColor = cache.preferDark ? new Color(0.12f, 0.12f, 0.12f, 1f)
                                                         : new Color(0.95f, 0.95f, 0.97f, 1f);

        Rect inner = new Rect(padL, padTop + headerH, outer.width - padL - padR, totalH - padTop - padBot - headerH);

        Rect titleR = new Rect(inner.x, inner.y, inner.width, titleH);
        if (cache.icon != null)
        {
            var iconR = new Rect(titleR.x, titleR.y + Mathf.Floor((titleH - ICON) * 0.5f), ICON, ICON);
            GUI.DrawTexture(iconR, cache.icon, ScaleMode.ScaleToFit, true);
            titleR.x += ICON + ICON_PAD;
            titleR.width -= ICON + ICON_PAD;
        }

        // Botonera (candado + colapso)
        {
            bool isEdit = pNote.FindPropertyRelative("displayMode").enumValueIndex == (int)GameObjectNotes.DisplayMode.Edit;
            const float BTN = 20f;
            float btnX = inner.x + inner.width - BTN;

            var lockIcon = GetModeLockIcon(isEdit);
            Rect lockR = new Rect(btnX, titleR.y + Mathf.Floor((titleH - BTN) * 0.5f), BTN, BTN);
            EditorGUIUtility.AddCursorRect(lockR, MouseCursor.Link);
            if (GUI.Button(lockR, new GUIContent(lockIcon.image, isEdit ? "Fijar" : "Editar"), squareIconBtn))
            {
                pNote.FindPropertyRelative("displayMode").enumValueIndex = isEdit
                    ? (int)GameObjectNotes.DisplayMode.Fixed
                    : (int)GameObjectNotes.DisplayMode.Edit;
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);
                s_preview.Remove(key);
            }

            btnX -= (BTN + 6f);
            var collapseIcon = EditorIconHelper.TryIcon("d_scenevis_hidden", "scenevis_hidden") ?? EditorIconHelper.TryIcon("d_Toolbar Minus");
            Rect colR = new Rect(btnX, lockR.y, BTN, BTN);
            EditorGUIUtility.AddCursorRect(colR, MouseCursor.Link);
            if (GUI.Button(colR, new GUIContent(collapseIcon?.image, collapsed ? "Mostrar cuerpo" : "Ocultar cuerpo"), squareIconBtn))
                s_fixedBodyCollapsed[key] = !collapsed;

            titleR.width -= (BTN * 2f + 12f);
        }

        // Título + Teaser en una sola línea cuando está colapsado
        if (collapsed)
        {
            // Dibujamos el título SIN wrap y con clip
            bool prevWrap = ttTitleStyle.wordWrap;
            var prevClip = ttTitleStyle.clipping;
            bool prevRich = ttTitleStyle.richText;
            ttTitleStyle.wordWrap = false;
            ttTitleStyle.clipping = TextClipping.Clip;
            ttTitleStyle.richText = true; // mantenemos negrita del <b>

            GUI.Label(titleR, cache.titleGC, ttTitleStyle);

            // Medimos el ancho real del título (texto sin tags)
            string titlePlain = StripRichTags(cache.titleGC.text);
            var measureTitleStyle = new GUIStyle(ttTitleStyle) { richText = false };
            float titleW = measureTitleStyle.CalcSize(new GUIContent(titlePlain)).x;
            titleW = Mathf.Min(titleW, titleR.width);

            // Calculamos el área restante para el teaser
            const string SEP = "           ";
            var teaserStyle = new GUIStyle(ttBodyStyle)
            {
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
                alignment = TextAnchor.MiddleLeft
            };
            teaserStyle.normal.textColor = cache.preferDark ? new Color(0f, 0f, 0f, 0.70f)
                                                            : new Color(1f, 1f, 1f, 0.75f);

            float sepW = teaserStyle.CalcSize(new GUIContent(SEP)).x;
            float remain = titleR.width - titleW - sepW;

            if (remain > 40f) // si no hay espacio razonable, no pintamos teaser
            {
                // Construimos teaser desde el RAW (plano) y lo ellipsizamos al ancho disponible
                string teaserPlain = BuildTeaserFromRaw(raw);
                string teaserFit = EllipsizeToWidth(teaserStyle, teaserPlain, remain);

                // Pinta separador + teaser
                var sepR = new Rect(titleR.x + titleW, titleR.y, sepW, titleR.height);
                var tzR = new Rect(sepR.xMax, titleR.y, remain, titleR.height);

                GUI.Label(sepR, SEP, teaserStyle);
                GUI.Label(tzR, teaserFit, teaserStyle);
            }

            // Restaurar estilo
            ttTitleStyle.wordWrap = prevWrap;
            ttTitleStyle.clipping = prevClip;
            ttTitleStyle.richText = prevRich;
        }
        else
        {
            // Modo no colapsado: igual que antes
            GUI.Label(titleR, cache.titleGC, ttTitleStyle);

            Rect bodyR = new Rect(inner.x, inner.y + titleH + titleGap, inner.width, cache.bodyH);
            RenderBodyWithInlineImages(cache, bodyR);
            HandleLinksClicks(cache);
            HandleChecklistClicks_PerNote(bodyR, cache, pBody, key);
            HandleImageClicks(cache);
        }

        GUI.EndGroup();
    }
    static string BuildTeaserFromRaw(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        // 1) Quitar imágenes: [img](...)
        string s = Regex.Replace(raw, @"\[img\]\([^)]*\)", "", RegexOptions.IgnoreCase);

        // 2) Reescritura inteligente de [token](inner)
        //    - Etiquetas conocidas -> inner (o (inner) para tag)
        //    - Enlace normal -> texto visible (el contenido de [ ... ])
        s = Regex.Replace(
            s,
            @"\[(?<tok>[^\]]+)\]\((?<inner>[^)]*)\)",
            new MatchEvaluator(m =>
            {
                string tok = m.Groups["tok"].Value;
                string inner = m.Groups["inner"].Value;

                // token puede venir como "keyword" o "keyword=param"
                string keyword = tok;
                int eq = keyword.IndexOf('=');
                if (eq >= 0) keyword = keyword.Substring(0, eq).Trim();
                keyword = keyword.Trim().ToLowerInvariant();

                switch (keyword)
                {
                    case "bold":
                    case "italics":
                    case "color":
                    case "size":
                        // formato: mostrar solo el contenido
                        return inner;
                    case "tag":
                        return "@" + inner;
                    case "check":
                    case "checkx":
                        return (keyword == "check") ? "☐ " + inner : "☑ " + inner;

                    default:
                        // enlace normal: mostrar el texto visible (lo que había en [ ... ])
                        return tok;
                }
            }),
            RegexOptions.IgnoreCase
        );

        // 3) Quitar separadores tipo [hr] o líneas con ---
        s = Regex.Replace(s, @"(\[hr\])|(^\s*-{3,}\s*$)", "", RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // 4) Normalizar espacios/nuevas líneas a una sola línea
        s = s.Replace("\r", " ").Replace("\n", " ");
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();

        return s;
    }


    static string StripRichTags(string styled)
    {
        if (string.IsNullOrEmpty(styled)) return "";
        // Quita tags de richtext <...>
        string s = Regex.Replace(styled, @"<[^>]+>", "");
        // Colapsa espacios
        s = Regex.Replace(s, @"\s{2,}", " ").Trim();
        return s;
    }

    static string EllipsizeToWidth(GUIStyle style, string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var gc = new GUIContent(text);
        float w = style.CalcSize(gc).x;
        if (w <= maxWidth) return text;

        const string ell = "…";
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            var g = new GUIContent(text.Substring(0, mid) + ell);
            float mw = style.CalcSize(g).x;
            if (mw <= maxWidth) lo = mid; else hi = mid - 1;
        }
        return (lo <= 0) ? ell : text.Substring(0, lo) + ell;
    }


    int NoteCacheKey(int instanceId, string propertyPath)
        => unchecked((instanceId * 397) ^ propertyPath.GetHashCode());

    void HandleChecklistClicks_PerNote(Rect bodyR, PreviewCache cache, SerializedProperty pBody, int cacheKey)
    {
        var e = Event.current;
        foreach (var ck in cache.checks)
        {
            foreach (var r in ck.hitRects)
            {
                bool newVal = GUI.Toggle(r, ck.isChecked, GUIContent.none);
                if (newVal != ck.isChecked)
                {
                    string cur = pBody.stringValue ?? string.Empty;
                    string updated = LinkMarkup.ToggleChecklistAt(cur, ck.rawStateCharIndex, newVal);
                    if (updated != cur)
                    {
                        Undo.RecordObject(tgt, "Toggle Checklist");
                        pBody.stringValue = updated;
                        serializedObject.ApplyModifiedProperties();
                        s_preview.Remove(cacheKey);
                    }
                    e.Use(); return;
                }
            }
        }
    }


    void RenderBodyWithInlineImages(PreviewCache cache, Rect bodyR)
    {
        foreach (var l in cache.links) l.hitRects.Clear();
        foreach (var c in cache.checks) c.hitRects.Clear();
        cache.imgLayout.Clear();

        string full = cache.bodyGC.text ?? string.Empty;

        var orderedImgs = new List<LinkMarkup.ImageSpan>(cache.images);
        orderedImgs.Sort((a, b) => a.strMarkerIndex.CompareTo(b.strMarkerIndex));

        float curY = bodyR.y;
        int curStr = 0;

        float contentW = bodyR.width;
        float lineH = GetStyleLineHeight(ttBodyStyle);

        int FindLineStartStr(int strIdx) => GetLineStartIndex(full, strIdx);
        int FindLineEndStr(int strIdx)
        {
            int nl = full.IndexOf('\n', strIdx);
            return (nl < 0) ? full.Length : (nl + 1);
        }

        foreach (var im in orderedImgs)
        {
            int segStartStr = curStr;
            int lineStartStr = FindLineStartStr(im.strMarkerIndex);

            if (lineStartStr > segStartStr)
            {
                string segText = full.Substring(segStartStr, lineStartStr - segStartStr);
                float segH = ttBodyStyle.CalcHeight(new GUIContent(segText), contentW);

                GUI.Label(new Rect(bodyR.x, curY, contentW, segH), segText, ttBodyStyle);

                var segMap = LinkMarkup.BuildVisibleIndexMapForIMGUI(segText);
                int segVisStart = cache.indexMap.str2vis[segStartStr];
                int segVisEnd = cache.indexMap.str2vis[lineStartStr];

                {
                    var tmp = new List<LinkMarkup.LinkSpan>();
                    foreach (var li in cache.links)
                        if (li.vStart >= segVisStart && li.vEnd <= segVisEnd && li.vEnd > li.vStart)
                            tmp.Add(new LinkMarkup.LinkSpan { name = li.name, id = li.id, vStart = li.vStart - segVisStart, vEnd = li.vEnd - segVisStart, isExternal = li.isExternal, isBroken = li.isBroken });

                    if (tmp.Count > 0)
                    {
                        LinkMarkup.LayoutLinkHitRects(new Rect(bodyR.x, curY, contentW, segH),
                            ttBodyStyle, new GUIContent(segText), segMap, tmp);
                        foreach (var t in tmp)
                        {
                            var real = cache.links.Find(x => x.id == t.id && x.name == t.name &&
                                                             x.vStart + segVisStart == t.vStart + segVisStart);
                            if (real != null) real.hitRects.AddRange(t.hitRects);
                        }
                    }
                }
                {
                    var tmp = new List<LinkMarkup.ChecklistSpan>();
                    foreach (var ck in cache.checks)
                        if (ck.vContentStart >= segVisStart && ck.vContentStart < segVisEnd)
                            tmp.Add(new LinkMarkup.ChecklistSpan { isChecked = ck.isChecked, rawStateCharIndex = ck.rawStateCharIndex, vContentStart = ck.vContentStart - segVisStart });

                    if (tmp.Count > 0)
                    {
                        LinkMarkup.LayoutChecklistHitRects(new Rect(bodyR.x, curY, contentW, segH),
                            ttBodyStyle, new GUIContent(segText), segMap, tmp);
                        foreach (var t in tmp)
                        {
                            var real = cache.checks.Find(x => x.rawStateCharIndex == t.rawStateCharIndex);
                            if (real != null) real.hitRects.AddRange(t.hitRects);
                        }
                    }
                }

                curY += segH;
            }

            if (im.src == "__HR__")
            {
                float hrW = contentW * 0.85f;
                float hrX = bodyR.x + ((contentW - hrW) * 0.5f);
                float hrH = 1f;
                var col = cache.preferDark ? new Color(0f, 0f, 0f, 0.35f) : new Color(1f, 1f, 1f, 0.15f);
                Rect hr = new Rect(hrX, curY + 4f, hrW, hrH);
                EditorGUI.DrawRect(hr, col);
                curY += hrH + 8f;
            }
            else if (LinkMarkup.TryResolveTextureOrSprite(im.src, out var tex, out var uv, out var _isExt) && tex != null)
            {
                // Antes 200px; ahora, si hay altura fija, usamos todo el contentW.
                float maxW = (im.height > 0f) ? contentW : Mathf.Min(contentW, 200f);

                float aspect = (uv.width > 0f && uv.height > 0f)
                    ? (uv.height / Mathf.Max(0.0001f, uv.width))
                    : ((float)tex.height / Mathf.Max(1f, (float)tex.width));

                float w, h;
                if (im.height > 0f)
                {
                    h = im.height;
                    w = h / Mathf.Max(0.0001f, aspect);

                    if (w > maxW)
                    {
                        float s = maxW / w;
                        w = maxW;
                        h = Mathf.Max(1f, h * s);
                    }
                }
                else
                {
                    w = maxW;
                    h = maxW * aspect;
                }

                float x = bodyR.x + ((contentW - w) * 0.5f);
                var dest = new Rect(x, curY, w, h);

                if (uv.width > 0f && uv.height > 0f && (uv.width != 1f || uv.height != 1f))
                    GUI.DrawTextureWithTexCoords(dest, tex, uv);
                else
                    GUI.DrawTexture(dest, tex, ScaleMode.ScaleToFit, true);

                cache.imgLayout.Add((im, tex, uv, 0f, w, h, dest));
                curY += h;
            }
            else
            {
                curY += lineH;
            }

            int lineEndStr = FindLineEndStr(lineStartStr);
            curStr = lineEndStr;
        }

        if (curStr < full.Length)
        {
            string segText = full.Substring(curStr);
            float segH = ttBodyStyle.CalcHeight(new GUIContent(segText), contentW);
            GUI.Label(new Rect(bodyR.x, curY, contentW, segH), segText, ttBodyStyle);

            var segMap = LinkMarkup.BuildVisibleIndexMapForIMGUI(segText);
            int segVisStart = cache.indexMap.str2vis[curStr];
            int segVisEnd = cache.indexMap.visibleLen;

            {
                var tmp = new List<LinkMarkup.LinkSpan>();
                foreach (var li in cache.links)
                    if (li.vStart >= segVisStart && li.vEnd <= segVisEnd && li.vEnd > li.vStart)
                        tmp.Add(new LinkMarkup.LinkSpan { name = li.name, id = li.id, vStart = li.vStart - segVisStart, vEnd = li.vEnd - segVisStart, isExternal = li.isExternal, isBroken = li.isBroken });

                if (tmp.Count > 0)
                {
                    LinkMarkup.LayoutLinkHitRects(new Rect(bodyR.x, curY, contentW, segH),
                        ttBodyStyle, new GUIContent(segText), segMap, tmp);
                    foreach (var t in tmp)
                    {
                        var real = cache.links.Find(x => x.id == t.id && x.name == t.name &&
                                                         x.vStart + segVisStart == t.vStart + segVisStart);
                        if (real != null) real.hitRects.AddRange(t.hitRects);
                    }
                }
            }
            {
                var tmp = new List<LinkMarkup.ChecklistSpan>();
                foreach (var ck in cache.checks)
                    if (ck.vContentStart >= segVisStart && ck.vContentStart < segVisEnd)
                        tmp.Add(new LinkMarkup.ChecklistSpan { isChecked = ck.isChecked, rawStateCharIndex = ck.rawStateCharIndex, vContentStart = ck.vContentStart - segVisStart });

                if (tmp.Count > 0)
                {
                    LinkMarkup.LayoutChecklistHitRects(new Rect(bodyR.x, curY, contentW, segH),
                        ttBodyStyle, new GUIContent(segText), segMap, tmp);
                    foreach (var t in tmp)
                    {
                        var real = cache.checks.Find(x => x.rawStateCharIndex == t.rawStateCharIndex);
                        if (real != null) real.hitRects.AddRange(t.hitRects);
                    }
                }
            }
        }
    }


    void DrawTooltipBackground(Rect r, Color bg, Color accent, bool accentLeft = false)
    {
        // Fondo
        EditorGUI.DrawRect(new Rect(0, 0, r.width, r.height), bg);

        // Acento: arriba (strip) o izquierda (barra vertical)
        if (!accentLeft)
        {
            // Banda superior
            EditorGUI.DrawRect(new Rect(0, 0, r.width, HEADER_STRIP), accent);
        }
        else
        {
            // Banda izquierda
            EditorGUI.DrawRect(new Rect(0, 0, HEADER_STRIP, r.height), accent);
        }

        // Borde fino
        var border = new Color(1f, 1f, 1f, 0.08f);
        EditorGUI.DrawRect(new Rect(0, 0, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(0, r.height - 1, r.width, 1), border);
        EditorGUI.DrawRect(new Rect(0, 0, 1, r.height), border);
        EditorGUI.DrawRect(new Rect(r.width - 1, 0, 1, r.height), border);
    }


    void ComputeImageLayout(PreviewCache cache, float innerW, GUIStyle bodyStyle, out float extraHeight)
    {
        cache.imgLayout.Clear();
        extraHeight = 0f;

        float lineH = GetStyleLineHeight(bodyStyle);
        const float IMG_VPAD = 15f;

        cache.images.Sort((a, b) => a.strMarkerIndex.CompareTo(b.strMarkerIndex));

        float accumulated = 0f;
        foreach (var im in cache.images)
        {
            if (im.src == "__HR__")
            {
                float h = 1f + 8f;
                float extra = h - lineH;
                accumulated += extra;
                continue;
            }

            if (!LinkMarkup.TryResolveTextureOrSprite(im.src, out var tex, out var uv, out var isExternal) || tex == null)
                continue;

            // Antes: Mathf.Min(innerW, 200f). Ahora: si hay altura fija, usar innerW completo.
            float maxW = (im.height > 0f) ? innerW : Mathf.Min(innerW, 200f);

            float aspect = (uv.width > 0f && uv.height > 0f)
                ? (uv.height / Mathf.Max(0.0001f, uv.width))
                : ((float)tex.height / Mathf.Max(1f, (float)tex.width));

            float w, h2;
            if (im.height > 0f)
            {
                h2 = im.height;
                w = h2 / Mathf.Max(0.0001f, aspect);

                if (w > maxW)
                {
                    float s = maxW / w;
                    w = maxW;
                    h2 = Mathf.Max(1f, h2 * s);
                }
            }
            else
            {
                w = maxW;
                h2 = maxW * aspect;
            }

            float extra2 = (h2 + IMG_VPAD) - lineH;

            cache.imgLayout.Add((im, tex, uv, accumulated, w, h2, default));
            accumulated += extra2;
        }
        extraHeight = accumulated;
    }


    float GetStyleLineHeight(GUIStyle style)
        => (style.lineHeight > 0f) ? style.lineHeight : style.CalcSize(new GUIContent("Ay")).y;

    int GetLineStartIndex(string s, int strIndex)
    {
        int p = Mathf.Clamp(strIndex, 0, (s != null ? s.Length : 0));
        int nl = (s != null && p > 0) ? s.LastIndexOf('\n', p - 1) : -1;
        return (nl < 0) ? 0 : (nl + 1);
    }

    // ---- Interacción ----
    void HandleLinksClicks(PreviewCache cache)
    {
        var e = Event.current;
        if (cache == null || cache.links == null || cache.links.Count == 0) return;

        int kLinkHash = "NotesLinkCtrl".GetHashCode();

        foreach (var li in cache.links)
        {
            if (li == null || li.hitRects == null) continue;

            foreach (var r in li.hitRects)
            {
                int id = GUIUtility.GetControlID(kLinkHash, FocusType.Passive, r);
                var typeForCtrl = e.GetTypeForControl(id);

                if (typeForCtrl == EventType.Repaint)
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

                switch (typeForCtrl)
                {
                    case EventType.MouseDown:
                        if (!r.Contains(e.mousePosition)) break;

                        if (e.button == 1)
                        {
                            NotesLinkActions.ShowContextMenu(li.name, li.id);
                            GUIUtility.keyboardControl = 0;
                            e.Use();
                            return;
                        }

                        if (e.button == 0)
                        {
                            GUIUtility.hotControl = id;
                            GUIUtility.keyboardControl = 0;
                            e.Use();
                            return;
                        }
                        break;

                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id) e.Use();
                        break;

                    case EventType.MouseUp:
                        if (GUIUtility.hotControl != id) break;

                        GUIUtility.hotControl = 0;
                        if (e.button == 0 && r.Contains(e.mousePosition))
                        {
                            PingLink(li.id); // Inspector: pingea/abre
                        }
                        e.Use();
                        return;
                }
            }
        }
    }


    void HandleImageClicks(PreviewCache cache)
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown) return;

        foreach (var it in cache.imgLayout)
        {
            if (it.img.src == "__HR__") continue; // HR no es interactivo

            var r = it.drawRect;
            if (!r.Contains(e.mousePosition)) continue;

            if (e.button == 1)
            {
                NotesLinkActions.ShowContextMenu("Imagen", it.img.src);
                e.Use(); return;
            }
            if (e.button == 0)
            {
                PingLink(it.img.src); // solo ping
                e.Use(); return;
            }
        }
    }



    void PingLink(string id)
    {
        if (NotesLinkActions.IsExternal(id)) { Application.OpenURL(id); return; }

        var obj = NotesLinkActions.TryResolveAll(id);
        if (obj != null)
        {
            var toPing = (obj is Component c) ? (UnityEngine.Object)c.gameObject : obj;
            EditorGUIUtility.PingObject(toPing);
        }
        else
        {
            Debug.LogWarning($"Enlace no resuelto: {id}");
        }
    }

    void HandleChecklistClicks(Rect bodyR, PreviewCache cache, SerializedProperty pNotes)
    {
        var e = Event.current;
        foreach (var ck in cache.checks)
        {
            foreach (var r in ck.hitRects)
            {
                bool newVal = GUI.Toggle(r, ck.isChecked, GUIContent.none);
                if (newVal != ck.isChecked)
                {
                    ToggleChecklistAtRawIndexRobust(pNotes, ck.rawStateCharIndex, newVal);
                    e.Use(); return;
                }
            }
        }
    }

    void ToggleChecklistAtRawIndexRobust(SerializedProperty pNotes, int rawStateCharIndex, bool newStateChecked)
    {
        string cur = pNotes.stringValue ?? string.Empty;
        string updated = LinkMarkup.ToggleChecklistAt(cur, rawStateCharIndex, newStateChecked);
        if (updated == cur) return;

        Undo.RecordObject(tgt, "Toggle Checklist");
        pNotes.stringValue = updated;
        serializedObject.ApplyModifiedProperties();
        s_preview.Remove(tgt.GetInstanceID());
    }



    DateTime ParseDateOrToday(string s)
    {
        if (DateTime.TryParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)) return d;
        return DateTime.Now.Date;
    }
}
#endif
