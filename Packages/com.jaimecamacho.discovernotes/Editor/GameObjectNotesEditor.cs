#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using static System.Collections.Specialized.BitVector32;
using static UnityEngine.GraphicsBuffer;


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
    private GUIContent pinIcon;

    private static readonly Color PinOnColor = new Color(1f, 0.85f, 0.1f, 1f);
    private static readonly Color PinOffColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    // Constantes de layout
    const float HEADER_STRIP = 4f;
    const float PADDING = 10f;
    const float ICON = 16f;
    const float ICON_PAD = 2f;
    const float SECTION_CONTROL_MIN_WIDTH = 100f;

    static readonly GUIContent NoteTitleContent = new GUIContent(
        "Título de la nota",
        "Nombre corto y descriptivo que se mostrará en la tarjeta, el tooltip y el encabezado Discover.");
    static readonly GUIContent DiscoverDisciplineContent = new GUIContent(
        "Área",
        "Disciplina o equipo responsable de la nota (Gameplay, FX, Audio, etc.).");
    static readonly GUIContent DiscoverSectionsContent = new GUIContent("Secciones");
    static readonly GUIContent SectionTitleContent = new GUIContent("Título de la sección");
    static readonly GUIContent RemoveSectionButtonContent = new GUIContent("Eliminar");
    static readonly GUIContent AddSectionButtonContent = new GUIContent("Añadir sección");
    static readonly GUIContent NoteActionsContent = new GUIContent("Acciones de la nota");
    static readonly GUIContent AddNoteButtonContent = new GUIContent("Añadir nota");
    static readonly GUIContent RemoveNoteButtonContent = new GUIContent("Eliminar nota");

    // Edición en TextArea (plana con scrollbar)
    private const string NotesControlName = "NOTES_TEXTAREA_CONTROL";
    private const string SectionControlName = "SECTION_TEXTAREA_CONTROL";
    private const int EDIT_MIN_LINES = 3;     // min visible
    private const int EDIT_MAX_LINES = 15;    // max visible

    private Vector2 _editScroll;

    // Cache de vista fija (interpretada al dibujar)
    class PreviewCache
    {
        public int textHash, metaHash;
        public float width;
        public Color bg, accent;

        public GUIContent titleGC, bodyGC;
        GUIContent metaGC;               // línea de metadatos ya montada y encogida
        string _metaCat, _metaAuthor, _metaDate; // piezas en bruto para construir meta
        public float titleH, bodyH, innerWidth;
        public Texture icon;
        public float headerIconSize;
        public bool preferDark;

        public LinkMarkup.VisibleIndexMap indexMap;
        public readonly List<LinkMarkup.LinkSpan> links = new List<LinkMarkup.LinkSpan>();
        public readonly List<LinkMarkup.ChecklistSpan> checks = new List<LinkMarkup.ChecklistSpan>();
        public readonly List<LinkMarkup.ImageSpan> images = new List<LinkMarkup.ImageSpan>();

        public readonly List<(LinkMarkup.ImageSpan img, Texture2D tex, Rect uv, float extraBefore, float width, float height, Rect drawRect)>
            imgLayout = new List<(LinkMarkup.ImageSpan, Texture2D, Rect, float, float, float, Rect)>();
    }

    static readonly Dictionary<int, PreviewCache> s_preview = new Dictionary<int, PreviewCache>();
    static readonly Dictionary<int, PreviewCache> s_sectionPreview = new Dictionary<int, PreviewCache>();
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
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(AddNoteButtonContent, GUILayout.Width(160f)))
                {
                    AddNoteAtIndex(pList, 0);
                    serializedObject.Update();
                    pList = serializedObject.FindProperty("notesList");
                }
                GUILayout.FlexibleSpace();
            }

            if (pList == null || pList.arraySize == 0)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }
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

                DrawHeaderToolbar_PerNote(pNote, pMode);
                DrawEditableMeta_PerNote(pNote);
                DrawEditableNotes_PlainAutoHeight_PerNote(pNote, i);
                DrawDiscoverContent_Edit(pNote, pList, i);

                GUILayout.EndVertical();
            }
            else
            {

                bool collapsed = DrawFixedLikeTooltip_WithCollapse_AndRich_PerNote(pNote, i);
                if (!collapsed)
                {
                    DrawDiscoverContent_Fixed(pNote);
                }
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

        if (pinIcon == null || (pinIcon.image == null && string.IsNullOrEmpty(pinIcon.text)))
        {
            pinIcon = EditorIconHelper.GetStarIcon();
            if (pinIcon == null || (pinIcon.image == null && string.IsNullOrEmpty(pinIcon.text)))
            {
                pinIcon = new GUIContent("★");
            }
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
    void DrawHeaderToolbar_PerNote(SerializedProperty pNote, SerializedProperty pMode)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            bool isEdit = pMode.enumValueIndex == (int)GameObjectNotes.DisplayMode.Edit;
            var pShowHierarchy = pNote.FindPropertyRelative("showInHierarchy");
            bool showInHierarchy = pShowHierarchy == null || pShowHierarchy.boolValue;
            if (!isEdit) // 🔴 solo mostramos texto si NO está en modo edición
            {
                string title = pNote.FindPropertyRelative("discoverName")?.stringValue;
                if (string.IsNullOrWhiteSpace(title))
                    title = pNote.FindPropertyRelative("category")?.stringValue ?? "Nota";

                var pDiscipline = pNote.FindPropertyRelative("discoverCategory");
                var discipline = pDiscipline != null
                    ? NoteStylesProvider.GetDisciplineDisplayName((DiscoverCategory)pDiscipline.enumValueIndex)
                    : NoteStylesProvider.GetDisciplineDisplayName(DiscoverCategory.Other);

                string severity = pNote.FindPropertyRelative("category")?.stringValue;
                if (string.IsNullOrWhiteSpace(severity)) severity = "Info";

                var badgeContent = new GUIContent($"{severity} • {discipline}", DiscoverDisciplineContent.tooltip);
                var badgeStyle = EditorStyles.miniLabel;
                var badgeSize = badgeStyle.CalcSize(badgeContent);

                using (new GUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                    GUILayout.Space(1f);
                    GUILayout.Label(badgeContent, badgeStyle, GUILayout.Width(badgeSize.x + 4f));
                }
            }

            GUILayout.FlexibleSpace();
            Rect pinRect = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(26f), GUILayout.Height(20f));
            if (DrawHierarchyPinToggle(pinRect, showInHierarchy,
                                       "Ocultar tooltip en la Jerarquía",
                                       "Mostrar tooltip en la Jerarquía"))
            {
                bool newValue = !showInHierarchy;
                if (pShowHierarchy != null)
                {
                    pShowHierarchy.boolValue = newValue;
                    serializedObject.ApplyModifiedProperties();
                }
                GUI.FocusControl(null);
                showInHierarchy = newValue;
                Repaint();
                EditorApplication.RepaintHierarchyWindow();
            }
            // 🔒 Botón candado (siempre visible)
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
                int key = NoteCacheKey(tgt.GetInstanceID(), pMode.propertyPath);
                s_preview.Remove(key);
                Repaint();
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
    bool DrawHierarchyPinToggle(Rect rect, bool isPinned, string tooltipWhenPinned, string tooltipWhenUnpinned)
    {
        var baseIcon = pinIcon ?? new GUIContent("★");
        var pinContent = new GUIContent(baseIcon.image, isPinned ? tooltipWhenPinned : tooltipWhenUnpinned);
        if (pinContent.image == null)
        {
            pinContent.text = string.IsNullOrEmpty(baseIcon.text) ? "★" : baseIcon.text;
        }

        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        var prevColor = GUI.color;
        GUI.color = isPinned ? PinOnColor : PinOffColor;
        bool clicked = GUI.Button(rect, pinContent, squareIconBtn ?? GUIStyle.none);
        GUI.color = prevColor;
        return clicked;
    }
    // ---------- Meta ----------
    void DrawEditableMeta_PerNote(SerializedProperty pNote)
    {
        var pTitle = pNote.FindPropertyRelative("discoverName");
        if (pTitle != null)
        {
            EditorGUILayout.PropertyField(pTitle, NoteTitleContent);
        }
        var pAuthor = pNote.FindPropertyRelative("author");
        var pCategory = pNote.FindPropertyRelative("category");
        var pDiscipline = pNote.FindPropertyRelative("discoverCategory");

        float rowH = Mathf.Max(EditorGUIUtility.singleLineHeight + 6f, 22f);
        Rect row = EditorGUILayout.GetControlRect(false, rowH);

        float gap = 8f;
        float third = Mathf.Max(10f, (row.width - gap * 2f) / 3f);

        Rect severityRect = new Rect(row.x, row.y, third, row.height);
        Rect disciplineRect = new Rect(severityRect.xMax + gap, row.y, third, row.height);
        Rect authorRect = new Rect(disciplineRect.xMax + gap, row.y, third, row.height);

        EditorIconHelper.DrawIconPopupCategory(severityRect, pCategory, NoteStylesProvider.GetCategoryNames());
        DrawDiscoverDisciplinePopup(disciplineRect, pDiscipline);
        EditorIconHelper.DrawIconPopupAuthor(authorRect, pAuthor, NoteStylesProvider.GetAuthors());

    }


    void DrawDiscoverDisciplinePopup(Rect r, SerializedProperty pDiscipline)
    {
        if (pDiscipline == null)
        {
            EditorGUI.LabelField(r, DiscoverDisciplineContent, new GUIContent(""));
            return;
        }

        EditorGUI.BeginProperty(r, DiscoverDisciplineContent, pDiscipline);

        string[] names = NoteStylesProvider.GetDisciplineNamesCopy();
        if (names.Length == 0)
        {
            pDiscipline.enumValueIndex = (int)DiscoverCategory.Other;
            EditorGUI.EndProperty();
            return;
        }

        int current = Mathf.Clamp(pDiscipline.enumValueIndex, 0, names.Length - 1);

        const float iconW = 18f;
        Rect iconRect = new Rect(r.x, r.y + 1, iconW, EditorGUIUtility.singleLineHeight);
        Rect popupRect = new Rect(iconRect.xMax + 4, r.y, r.width - iconW - 4, EditorGUIUtility.singleLineHeight);

        // (opcional: icono genérico de disciplina)
        GUI.Label(iconRect, EditorGUIUtility.IconContent("d_FilterByType"));

        EditorGUI.BeginChangeCheck();
        int newIdx = EditorGUI.Popup(popupRect, current, names);
        if (EditorGUI.EndChangeCheck())
            pDiscipline.enumValueIndex = newIdx;

        EditorGUI.EndProperty();
    }

    // ---------- Edición: TextArea PLANA (min/max líneas + ScrollView) ----------
    void DrawEditableNotes_PlainAutoHeight_PerNote(SerializedProperty pNote, int noteIndex)
    {
        var pBody = pNote.FindPropertyRelative("notes");
        DrawPlainTextArea(
            pBody,
            new GUIContent("Descripcion (texto plano; se interpreta al mostrar)"),
            EditorStyles.boldLabel,
            $"{NotesControlName}_{noteIndex}",
            "Edit Notes"
        );
    }

    void DrawPlainTextArea(SerializedProperty property, GUIContent label, GUIStyle labelStyle,
                           string controlName, string undoLabel, float minWidth = 100f)
    {
        if (property == null) return;

        if (label != null)
            EditorGUILayout.LabelField(label, labelStyle ?? EditorStyles.boldLabel);

        string text = property.stringValue ?? string.Empty;
        float viewW = Mathf.Max(minWidth, EditorGUIUtility.currentViewWidth - 36f);
        float targetH = notesAreaStyle.CalcHeight(new GUIContent(string.IsNullOrEmpty(text) ? " " : text), viewW);

        Rect areaRect = EditorGUILayout.GetControlRect(false, targetH, GUILayout.ExpandWidth(true));

        ArmUndoIfTyping(controlName, undoLabel);

        GUI.SetNextControlName(controlName);
        EditorGUI.BeginChangeCheck();
        string after = EditorGUI.TextArea(areaRect, text, notesAreaStyle);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(tgt, undoLabel);
            property.stringValue = after;
            serializedObject.ApplyModifiedProperties();
        }

        HandleDragAndDropIntoTextArea(areaRect, property, undoLabel);
        if (Event.current.type == EventType.ExecuteCommand && Event.current.commandName == "UndoRedoPerformed")
            Repaint();
    }

    void ArmUndoIfTyping(string controlName, string undoLabel)
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
                Undo.RecordObject(tgt, undoLabel);
                s_lastUndoEventId = evtId;
            }
        }
    }


    void HandleDragAndDropIntoTextArea(Rect areaRect, SerializedProperty property, string undoLabel)
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

            string cur = property.stringValue ?? string.Empty;

            // SIEMPRE al principio + con salto de línea al final de lo insertado
            string insert = sb.ToString();
            if (!insert.EndsWith("\n")) insert += "\n";
            string composed = insert + cur;

            Undo.RecordObject(tgt, undoLabel);
            property.stringValue = composed;
            serializedObject.ApplyModifiedProperties();

            e.Use();
            GUI.changed = true;
        }
    }


    void DrawDiscoverSectionsEdit(SerializedProperty pNote, int noteIndex)
    {
        if (pNote == null) return;

        var pSections = pNote.FindPropertyRelative("discoverSections");

        Rect headerRect = EditorGUILayout.GetControlRect();
        Rect indentedRect = EditorGUI.IndentedRect(headerRect);
        bool sectionsValid = pSections != null;
        const float sectionButtonWidth = 140f;
        const float sectionButtonSpacing = 6f;
        Rect sectionLabelRect = new Rect(
            indentedRect.x,
            indentedRect.y,
            Mathf.Max(0f, indentedRect.width - sectionButtonWidth - sectionButtonSpacing),
            indentedRect.height
        );
        Rect sectionButtonRect = new Rect(
            indentedRect.xMax - sectionButtonWidth,
            indentedRect.y,
            sectionButtonWidth,
            indentedRect.height
        );

        EditorGUI.LabelField(sectionLabelRect, DiscoverSectionsContent, EditorStyles.boldLabel);

        using (new EditorGUI.DisabledGroupScope(!sectionsValid))
        {
            if (GUI.Button(sectionButtonRect, AddSectionButtonContent, EditorStyles.miniButton))
            {
                AddSection(pSections);
                return;
            }
        }

        if (!sectionsValid)
        {
            EditorGUILayout.HelpBox("No se pudieron cargar las secciones.", MessageType.Info);
            return;
        }

        GUILayout.Space(2f);

        int removeIndex = -1;
        using (new EditorGUI.IndentLevelScope())
        {
            for (int i = 0; i < pSections.arraySize; i++)
            {
                var pSection = pSections.GetArrayElementAtIndex(i);
                if (pSection == null) continue;

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var pName = pSection.FindPropertyRelative("sectionName");
                    var pContent = pSection.FindPropertyRelative("sectionContent");

                    DrawSectionHeaderWithRemove(pName, i, ref removeIndex);

                    GUILayout.Space(2f);

                    DrawPlainTextArea(
                        pContent,
                        new GUIContent("Contenido (texto plano; se interpreta al mostrar)"),
                        EditorStyles.miniBoldLabel,
                        $"{SectionControlName}_{noteIndex}_{i}",
                        "Edit Section",
                        SECTION_CONTROL_MIN_WIDTH
                    );
                }

                GUILayout.Space(6f);

                if (removeIndex >= 0) break;
            }

        }

        if (removeIndex >= 0)
        {
            RemoveSectionAt(pSections, removeIndex);
            return;
        }
    }

    void AddSection(SerializedProperty pSections)
    {
        if (pSections == null) return;

        Undo.RecordObject(tgt, "Añadir sección");
        int newIndex = Mathf.Max(0, pSections.arraySize);
        pSections.InsertArrayElementAtIndex(newIndex);

        var pNew = pSections.GetArrayElementAtIndex(newIndex);
        if (pNew != null)
        {
            var pName = pNew.FindPropertyRelative("sectionName");
            if (pName != null) pName.stringValue = string.Empty;

            var pContent = pNew.FindPropertyRelative("sectionContent");
            if (pContent != null) pContent.stringValue = string.Empty;

            var pActions = pNew.FindPropertyRelative("actions");
            if (pActions != null && pActions.isArray)
                pActions.arraySize = 0;
        }

        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
        s_sectionPreview.Clear();
    }

    void RemoveSectionAt(SerializedProperty pSections, int removeIndex)
    {
        if (pSections == null || removeIndex < 0 || removeIndex >= pSections.arraySize) return;

        Undo.RecordObject(tgt, "Eliminar sección");
        pSections.DeleteArrayElementAtIndex(removeIndex);
        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
        s_sectionPreview.Clear();
    }

    void DrawDiscoverContent_Edit(SerializedProperty pNote, SerializedProperty pList, int noteIndex)
    {
        GUILayout.Space(8);
        DrawDiscoverSectionsEdit(pNote, noteIndex);

        GUILayout.Space(12f);
        EditorGUILayout.LabelField(NoteActionsContent, EditorStyles.miniBoldLabel);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(RemoveNoteButtonContent, GUILayout.Width(140f)))
            {
                if (RemoveNoteAtIndex(pList, noteIndex))
                    return;
                GUIUtility.ExitGUI();
            }

            GUILayout.Space(8f);

            if (GUILayout.Button(AddNoteButtonContent, GUILayout.Width(140f)))
            {
                AddNoteAtIndex(pList, noteIndex + 1);
                GUIUtility.ExitGUI();
            }

            GUILayout.FlexibleSpace();
        }
    }


    void DrawDiscoverContent_Fixed(SerializedProperty pNote)
    {
        var pSections = pNote.FindPropertyRelative("discoverSections");

        bool hasSections = pSections != null && pSections.isArray && pSections.arraySize > 0;
        if (!hasSections) return;

        GUILayout.Space(6);

        // Fondo de bloque con el color de la categoría
        string categoryName = pNote.FindPropertyRelative("category")?.stringValue;
        var cat = NoteStylesProvider.FindCategory(categoryName);
        var blockBg = (cat != null ? cat.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 1f));
        blockBg.a = 1f;
        if (lastCardBg != blockBg)
        {
            solidTex.SetPixel(0, 0, blockBg);
            solidTex.Apply(false, false);
            lastCardBg = blockBg;
        }

        GUILayout.BeginVertical(cardStyle);

        // (QUITADO) — No pintamos un título interno para evitar el "Humo" duplicado

        DrawDiscoverSectionsFixed(pSections);

        GUILayout.EndVertical();
    }



    // === REEMPLAZAR COMPLETO ===
    void DrawDiscoverSectionsFixed(SerializedProperty pSections)
    {
        if (pSections == null || !pSections.isArray || pSections.arraySize == 0)
            return;

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Secciones", EditorStyles.boldLabel);

        for (int i = 0; i < pSections.arraySize; i++)
        {
            var pSection = pSections.GetArrayElementAtIndex(i);
            if (pSection == null) continue;

            var pName = pSection.FindPropertyRelative("sectionName");
            var pContent = pSection.FindPropertyRelative("sectionContent");

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Título de la sección
                var title = (pName != null && !string.IsNullOrWhiteSpace(pName.stringValue))
                            ? pName.stringValue
                            : "Sección";
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

                GUILayout.Space(2f);

                DrawSectionRichContent(pContent);
            }

            GUILayout.Space(6);
        }
    }

    void DrawSectionHeaderWithRemove(SerializedProperty pName, int sectionIndex, ref int removeIndex)
    {
        Rect headerRect = EditorGUILayout.GetControlRect();
        const float spacing = 4f;
        const float removeWidth = 70f;

        Rect buttonRect = new Rect(headerRect.xMax - removeWidth, headerRect.y + 1f, removeWidth, EditorGUIUtility.singleLineHeight);
        Rect fieldRect = new Rect(headerRect.x, headerRect.y, headerRect.width - removeWidth - spacing, headerRect.height);

        if (fieldRect.width < 50f)
            fieldRect.width = Mathf.Max(0f, headerRect.width - removeWidth);

        if (pName != null)
        {
            EditorGUI.BeginProperty(fieldRect, SectionTitleContent, pName);
            EditorGUI.PropertyField(fieldRect, pName, SectionTitleContent);
            EditorGUI.EndProperty();
        }

        if (GUI.Button(buttonRect, RemoveSectionButtonContent, EditorStyles.miniButton))
            removeIndex = sectionIndex;
    }

    void AddNoteAtIndex(SerializedProperty pList, int insertIndex)
    {
        if (pList == null) return;

        insertIndex = Mathf.Clamp(insertIndex, 0, Math.Max(0, pList.arraySize));

        Undo.RecordObject(tgt, "Añadir nota");
        pList.InsertArrayElementAtIndex(insertIndex);

        var pNewNote = pList.GetArrayElementAtIndex(insertIndex);
        ResetNoteProperty(pNewNote);

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(tgt);
    }

    bool RemoveNoteAtIndex(SerializedProperty pList, int noteIndex)
    {
        if (pList == null || noteIndex < 0 || noteIndex >= pList.arraySize)
            return false;

        Undo.RecordObject(tgt, "Eliminar nota");
        pList.DeleteArrayElementAtIndex(noteIndex);

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(tgt);

        serializedObject.Update();
        var refreshedList = serializedObject.FindProperty("notesList");
        bool hasNotes = refreshedList != null && refreshedList.arraySize > 0;

        if (!hasNotes)
        {
            var component = tgt;
            EditorApplication.delayCall += () =>
            {
                if (component != null)
                    Undo.DestroyObjectImmediate(component);
            };
            GUIUtility.ExitGUI();
            return true;
        }

        return false;
    }

    void ResetNoteProperty(SerializedProperty pNote)
    {
        if (pNote == null) return;

        var pAuthor = pNote.FindPropertyRelative("author");
        var pDate = pNote.FindPropertyRelative("dateCreated");
        var pCategory = pNote.FindPropertyRelative("category");
        var pDiscoverName = pNote.FindPropertyRelative("discoverName");
        var pDiscoverCategory = pNote.FindPropertyRelative("discoverCategory");
        var pDiscoverSummary = pNote.FindPropertyRelative("discoverSummary");
        var pDiscoverSections = pNote.FindPropertyRelative("discoverSections");
        var pNotes = pNote.FindPropertyRelative("notes");
        var pDisplayMode = pNote.FindPropertyRelative("displayMode");
        var pShowInHierarchy = pNote.FindPropertyRelative("showInHierarchy");

        var authors = NoteStylesProvider.GetAuthors();
        if (pAuthor != null)
            pAuthor.stringValue = (authors != null && authors.Length > 0) ? authors[0] : string.Empty;

        if (pDate != null)
            pDate.stringValue = DateTime.Now.ToString("dd/MM/yyyy");

        var categories = NoteStylesProvider.GetCategoryNames();
        if (pCategory != null)
            pCategory.stringValue = (categories != null && categories.Length > 0) ? categories[0] : "Info";

        if (pDiscoverName != null)
            pDiscoverName.stringValue = tgt != null && tgt.gameObject != null ? tgt.gameObject.name : string.Empty;

        if (pDiscoverCategory != null)
            pDiscoverCategory.enumValueIndex = (int)DiscoverCategory.Other;

        if (pDiscoverSummary != null)
            pDiscoverSummary.stringValue = string.Empty;

        if (pNotes != null)
            pNotes.stringValue = string.Empty;

        if (pDisplayMode != null)
            pDisplayMode.enumValueIndex = (int)GameObjectNotes.DisplayMode.Edit;

        if (pShowInHierarchy != null)
            pShowInHierarchy.boolValue = true;

        if (pDiscoverSections != null && pDiscoverSections.isArray)
        {
            for (int i = pDiscoverSections.arraySize - 1; i >= 0; i--)
                pDiscoverSections.DeleteArrayElementAtIndex(i);
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

    bool DrawFixedLikeTooltip_WithCollapse_AndRich_PerNote(SerializedProperty pNote, int noteIndex)

    {
        string category = pNote.FindPropertyRelative("category").stringValue ?? "Info";
        string author = pNote.FindPropertyRelative("author").stringValue;
        string title = pNote.FindPropertyRelative("discoverName")?.stringValue;
        if (string.IsNullOrWhiteSpace(title)) title = category;

        var pDiscipline = pNote.FindPropertyRelative("discoverCategory");
        var discipline = pDiscipline != null
            ? NoteStylesProvider.GetDisciplineDisplayName((DiscoverCategory)pDiscipline.enumValueIndex)
            : NoteStylesProvider.GetDisciplineDisplayName(DiscoverCategory.Other);

        string authorLabel = string.IsNullOrEmpty(author) ? "Anónimo" : author;
        var pBody = pNote.FindPropertyRelative("notes");
        string raw = pBody.stringValue ?? string.Empty;
        var pShowHierarchy = pNote.FindPropertyRelative("showInHierarchy");
        bool showInHierarchy = pShowHierarchy == null || pShowHierarchy.boolValue;

        var cat = NoteStylesProvider.FindCategory(category);
        var bg = (cat != null ? cat.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 0.985f));
        var accent = (cat != null ? cat.tooltipAccentBar : new Color(0.25f, 0.5f, 1f, 1f));
        bg.a = 1f;

        Texture headerIconCandidate = (cat != null && cat.icon != null)
            ? cat.icon
            : (EditorIconHelper.GetCategoryIcon(category)?.image);
        int headerIconId = headerIconCandidate != null ? headerIconCandidate.GetInstanceID() : 0;

        int key = NoteCacheKey(tgt.GetInstanceID(), pNote.propertyPath);
        if (!s_preview.TryGetValue(key, out var cache)) { cache = new PreviewCache(); s_preview[key] = cache; }

        int textHash = raw.GetHashCode();
        int metaHash = (category + "|" + authorLabel + "|" + discipline + "|" + title).GetHashCode();
        metaHash = unchecked(metaHash * 397) ^ headerIconId;
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

            cache.icon = headerIconCandidate;
            cache.headerIconSize = cache.icon != null ? ICON : 0f;
            cache.titleGC = new GUIContent($"<b>{title}</b>\n{category} • {discipline} • {authorLabel}");

            cache.links.Clear(); cache.checks.Clear(); cache.images.Clear();
            string displayStyled = LinkMarkup.BuildStyled(raw, cache.links, cache.checks, cache.images, out cache.indexMap);
            cache.bodyGC = new GUIContent(displayStyled);

            float innerW = availWidth - (PADDING * 2f);
            float iconBlock = cache.icon != null ? (cache.headerIconSize + ICON_PAD) : 0f;
            float titleAvailW = innerW - iconBlock;

            cache.titleH = ttTitleStyle.CalcHeight(cache.titleGC, Mathf.Max(50f, titleAvailW));
            if (cache.icon != null) cache.titleH = Mathf.Max(cache.titleH, cache.headerIconSize);

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
        float titleH = collapsed
            ? Mathf.Max(titleLineH * 2f + 2f, (cache.icon != null ? cache.headerIconSize : 0f))
            : cache.titleH;
        float headerH = HEADER_STRIP;
        float padTop = collapsed ? 4f : PADDING;
        float padBot = collapsed ? 4f : PADDING;
        float padL = collapsed ? 8f : PADDING;
        float padR = collapsed ? 12f : PADDING;
        float titleGap = collapsed ? 0f : 4f;
        float effectiveBodyH = collapsed ? 0f : cache.bodyH;
        float totalH = headerH + padTop + padBot + titleH + titleGap + effectiveBodyH;

        Rect outer = GUILayoutUtility.GetRect(0, totalH, GUILayout.ExpandWidth(true));
        if (outer.width <= 1f) return collapsed;

        GUI.BeginGroup(outer);
        DrawTooltipBackground(new Rect(0, 0, outer.width, totalH), cache.bg, cache.accent, accentLeft: false);

        ttTitleStyle.normal.textColor = cache.preferDark ? Color.black : Color.white;
        ttBodyStyle.normal.textColor = cache.preferDark ? new Color(0.12f, 0.12f, 0.12f, 1f)
                                                         : new Color(0.95f, 0.95f, 0.97f, 1f);

        Rect inner = new Rect(padL, padTop + headerH, outer.width - padL - padR, totalH - padTop - padBot - headerH);

        Rect titleR = new Rect(inner.x, inner.y, inner.width, titleH);
        if (cache.icon != null)
        {
            float iconSize = cache.headerIconSize;
            var iconR = new Rect(titleR.x, titleR.y + Mathf.Floor((titleH - iconSize) * 0.5f), iconSize, iconSize);
            GUI.DrawTexture(iconR, cache.icon, ScaleMode.ScaleToFit, true);
            titleR.x += iconSize + ICON_PAD;
            titleR.width -= iconSize + ICON_PAD;
        }

        // Botonera (candado + colapso)
        {
            bool isEdit = pNote.FindPropertyRelative("displayMode").enumValueIndex == (int)GameObjectNotes.DisplayMode.Edit;
            const float BTN = 20f;
            const float BTN_GAP = 6f;
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
                Repaint();
            }


            btnX -= (BTN + BTN_GAP);
            Rect pinR = new Rect(btnX, lockR.y, BTN, BTN);
            if (DrawHierarchyPinToggle(pinR, showInHierarchy,
                                       "Ocultar tooltip en la Jerarquía",
                                       "Mostrar tooltip en la Jerarquía"))
            {
                bool newValue = !showInHierarchy;
                if (pShowHierarchy != null)
                {
                    pShowHierarchy.boolValue = newValue;
                    serializedObject.ApplyModifiedProperties();
                }
                GUI.FocusControl(null);
                showInHierarchy = newValue;
                Repaint();
                EditorApplication.RepaintHierarchyWindow();
            }
            btnX -= (BTN + 6f);

            var collapseIcon = EditorIconHelper.TryIcon("d_scenevis_hidden", "scenevis_hidden") ?? EditorIconHelper.TryIcon("d_Toolbar Minus");
            Rect colR = new Rect(btnX, lockR.y, BTN, BTN);
            EditorGUIUtility.AddCursorRect(colR, MouseCursor.Link);
            if (GUI.Button(colR, new GUIContent(collapseIcon?.image, collapsed ? "Mostrar cuerpo" : "Ocultar cuerpo"), squareIconBtn))
            {
                s_fixedBodyCollapsed[key] = !collapsed;
                Repaint();
            }

            titleR.width -= (BTN * 3f + BTN_GAP * 2f);
        }

        // Título + Teaser en una sola línea cuando está colapsado
        if (collapsed)
        {
            // Cabecera en dos líneas: <b>Título</b> \n Severidad • Área • Autor • Fecha
            bool prevWrap = ttTitleStyle.wordWrap;
            var prevClip = ttTitleStyle.clipping;
            bool prevRich = ttTitleStyle.richText;

            ttTitleStyle.wordWrap = true;               // permitimos salto de línea
            ttTitleStyle.clipping = TextClipping.Overflow;
            ttTitleStyle.richText = true;               // mantiene el <b> del título

            GUI.Label(titleR, cache.titleGC, ttTitleStyle);

            // restaurar
            ttTitleStyle.wordWrap = prevWrap;
            ttTitleStyle.clipping = prevClip;
            ttTitleStyle.richText = prevRich;
        }
        else
        {
            // (lo que ya tenías para no colapsado)
            GUI.Label(titleR, cache.titleGC, ttTitleStyle);
            Rect bodyR = new Rect(inner.x, inner.y + titleH + titleGap, inner.width, cache.bodyH);
            RenderBodyWithInlineImages(cache, bodyR);
            HandleLinksClicks(cache);
            HandleChecklistClicksForProperty(bodyR, cache, pBody, key, k => s_preview.Remove(k));
            HandleImageClicks(cache);
        }

        GUI.EndGroup();
        return collapsed;
    }

    int NoteCacheKey(int instanceId, string propertyPath)
        => unchecked((instanceId * 397) ^ propertyPath.GetHashCode());

    int SectionCacheKey(int instanceId, string propertyPath)
        => unchecked((instanceId * 397) ^ propertyPath.GetHashCode());

    void DrawSectionRichContent(SerializedProperty pContent)
    {
        if (pContent == null) return;

        string raw = pContent.stringValue ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            GUILayout.Space(2f);
            return;
        }

        int key = SectionCacheKey(tgt.GetInstanceID(), pContent.propertyPath);
        if (!s_sectionPreview.TryGetValue(key, out var cache) || cache == null)
        {
            cache = new PreviewCache();
            s_sectionPreview[key] = cache;
        }

        float availWidth = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 60f);
        int textHash = raw.GetHashCode();
        if (cache.textHash != textHash || Mathf.Abs(cache.width - availWidth) > 0.5f)
        {
            RefreshSectionCache(cache, raw, availWidth);
        }

        Rect layoutRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                                                   GUILayout.ExpandWidth(true),
                                                   GUILayout.Height(cache.bodyH));
        Rect bodyRect = layoutRect;
        bodyRect.x += 4f;
        bodyRect.width = Mathf.Max(10f, bodyRect.width - 8f);
        bodyRect.height = cache.bodyH;

        var prevColor = ttBodyStyle.normal.textColor;
        ttBodyStyle.normal.textColor = EditorStyles.label.normal.textColor;

        RenderBodyWithInlineImages(cache, bodyRect);
        HandleLinksClicks(cache);
        HandleChecklistClicksForProperty(bodyRect, cache, pContent, key, k => s_sectionPreview.Remove(k));
        HandleImageClicks(cache);

        ttBodyStyle.normal.textColor = prevColor;
    }

    void RefreshSectionCache(PreviewCache cache, string raw, float availWidth)
    {
        if (cache == null) return;

        cache.textHash = raw != null ? raw.GetHashCode() : 0;
        cache.width = availWidth;

        cache.links.Clear();
        cache.checks.Clear();
        cache.images.Clear();

        string styled = LinkMarkup.BuildStyled(raw ?? string.Empty, cache.links, cache.checks, cache.images, out cache.indexMap);
        cache.bodyGC = new GUIContent(styled);

        float innerWidth = Mathf.Max(40f, availWidth - 16f);
        cache.innerWidth = innerWidth;

        float textH = ttBodyStyle.CalcHeight(cache.bodyGC, innerWidth);
        ComputeImageLayout(cache, innerWidth, ttBodyStyle, out float extraH);
        cache.bodyH = Mathf.Max(18f, textH + extraH);
    }

    void HandleChecklistClicksForProperty(Rect bodyR, PreviewCache cache, SerializedProperty property,
                                          int cacheKey, Action<int> invalidateCache)
    {
        if (property == null || cache == null) return;

        var e = Event.current;
        foreach (var ck in cache.checks)
        {
            foreach (var r in ck.hitRects)
            {
                bool newVal = GUI.Toggle(r, ck.isChecked, GUIContent.none);
                if (newVal != ck.isChecked)
                {
                    string cur = property.stringValue ?? string.Empty;
                    string updated = LinkMarkup.ToggleChecklistAt(cur, ck.rawStateCharIndex, newVal);
                    if (updated != cur)
                    {
                        Undo.RecordObject(tgt, "Toggle Checklist");
                        property.stringValue = updated;
                        serializedObject.ApplyModifiedProperties();
                        invalidateCache?.Invoke(cacheKey);
                    }
                    e.Use();
                    return;
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
}
#endif
