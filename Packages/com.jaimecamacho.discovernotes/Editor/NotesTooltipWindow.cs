#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;

public class NotesTooltipWindow : EditorWindow
{
    GUIStyle titleStyle, bodyStyle;
    const float MIN_W = 160f;
    const float MAX_W = 1024f;
    const float SCREEN_MARGIN = 12f;
    const float ANCHOR_GAP = 4f;
    const float PADDING = 10f;
    const float HEADER_STRIP = 4f;
    const float ICON = 16f;
    const float ICON_PAD = 6f;

    // Chincheta
    const float PIN_W = 20f;
    const float PIN_GAP = 4f;
    const float PIN_RESERVE = PIN_W + PIN_GAP;

    static NotesTooltipWindow _instance;
    public static NotesTooltipWindow Instance => _instance;

    [SerializeField] bool _pinned;
    public bool IsPinned => _pinned;

    GUIContent _starIcon;

    Texture iconTex;
    GameObjectNotes data;
    GUIContent titleGC, bodyGC;
    float boxW, boxH;
    Color accent, bg;
    double lastMouseOverTS;
    double lastAnchorHoverTS;
    double nextHierarchyRepaintTS;
    const double CLOSE_DELAY = 0.3;

    int _currentTargetId = -1;
    bool _sideLocked = false;
    bool _sidePlaceRight = true;

    // Spans para interacción (interpretados al render)
    readonly List<LinkMarkup.LinkSpan> _links = new List<LinkMarkup.LinkSpan>();
    readonly List<LinkMarkup.ChecklistSpan> _checks = new List<LinkMarkup.ChecklistSpan>();
    readonly List<LinkMarkup.ImageSpan> _images = new List<LinkMarkup.ImageSpan>();
    LinkMarkup.VisibleIndexMap _indexMap;

    List<(LinkMarkup.ImageSpan img, Texture2D tex, Rect uv, float extraBefore, float width, float height, Rect drawRect)> _imgLayout
        = new List<(LinkMarkup.ImageSpan, Texture2D, Rect, float, float, float, Rect)>();

    public static NotesTooltipWindow Create()
    {
        var w = CreateInstance<NotesTooltipWindow>();
        w.hideFlags = HideFlags.DontSave;
        return w;
    }

    void OnEnable()
    {
        _instance = this;
        _starIcon = EditorIconHelper.GetStarIcon();
    }

    void OnDisable()
    {
        if (_instance == this) _instance = null;
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            richText = true,
            wordWrap = false,
            clipping = TextClipping.Clip,
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            normal = { textColor = Color.white }
        };
        bodyStyle = new GUIStyle(EditorStyles.label)
        {
            richText = true,
            wordWrap = true,
            fontSize = 12,
            normal = { textColor = new Color(0.95f, 0.95f, 0.97f, 1f) }
        };
    }

    /// <summary>
    /// Muestra el tooltip para una nota. El texto subyacente SIEMPRE es plano;
    /// aquí se interpreta (links, checklists, imágenes) SOLO al render.
    /// </summary>
    public void ShowFor(GameObjectNotes notes, Rect anchorScreenRect)
    {
        bool targetChanged = _currentTargetId != notes.GetInstanceID();
        if (targetChanged) { _currentTargetId = notes.GetInstanceID(); _sideLocked = false; }

        EnsureStyles();
        data = notes;

        var cat = NoteStylesProvider.FindCategory(data.Category);
        accent = cat != null ? cat.tooltipAccentBar : new Color(0.25f, 0.5f, 1f, 1f);
        bg = cat != null ? cat.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 0.985f);
        bg.a = 1f;

        iconTex = (cat != null && cat.icon != null) ? cat.icon : (EditorIconHelper.GetCategoryIcon(data.Category)?.image);

        PrepareContent();   // <-- Interpretación en el momento de preparar contenido
        CalculateSize();

        float screenW = Screen.currentResolution.width;
        bool computedPlaceRight = (anchorScreenRect.xMax + ANCHOR_GAP + boxW) <= (screenW - SCREEN_MARGIN);
        if (!_sideLocked) { _sidePlaceRight = computedPlaceRight; _sideLocked = true; }

        PositionWindow(anchorScreenRect, _sidePlaceRight);

        if (!hasFocus) ShowPopup();
        Repaint();

        lastAnchorHoverTS = EditorApplication.timeSinceStartup;
        lastMouseOverTS = lastAnchorHoverTS;
        wantsMouseMove = true;
    }

    void PrepareContent()
    {
        // El cuerpo siempre es texto plano. Se recorta por seguridad en tooltips.
        string raw = (data.NotesText ?? string.Empty).Trim();
        if (raw.Length > 2000) raw = raw.Substring(0, 2000) + "…";

        string author = string.IsNullOrEmpty(data.Author) ? "Anónimo" : data.Author;
        string date = string.IsNullOrEmpty(data.DateCreated) ? DateTime.Now.ToString("dd/MM/yyyy") : data.DateCreated;
        string catName = string.IsNullOrEmpty(data.Category) ? "Nota" : data.Category;

        float screenW = Screen.currentResolution.width;
        float maxTitleW = Mathf.Max(50f, screenW - SCREEN_MARGIN * 2f - PADDING * 2f - (iconTex != null ? (ICON + ICON_PAD) : 0f) - PIN_RESERVE);
        titleGC = new GUIContent(BuildOneLineTitle(catName, author, date, maxTitleW, titleStyle));

        // === Interpretación modular (en tiempo de dibujo) ===
        _links.Clear(); _checks.Clear(); _images.Clear();
        string styled = LinkMarkup.BuildStyled(raw, _links, _checks, _images, out _indexMap);
        bodyGC = new GUIContent(styled);

        _imgLayout.Clear();
    }

    string BuildOneLineTitle(string category, string author, string date, float maxW, GUIStyle st)
    {
        string Sep = "  •  ";
        string Bold(string s) => $"<b>{s}</b>";
        string Compose(string c, string a, string d) => $"{Bold(c)}{Sep}{a}{Sep}{d}";

        string c = category, a = author, d = date;

        if (st.CalcSize(new GUIContent(Compose(c, a, d))).x <= maxW) return Compose(c, a, d);

        c = ShrinkPart(c, cx => Compose(cx, a, d), maxW, st);
        if (st.CalcSize(new GUIContent(Compose(c, a, d))).x <= maxW) return Compose(c, a, d);

        a = ShrinkPart(a, ax => Compose(c, ax, d), maxW, st);
        if (st.CalcSize(new GUIContent(Compose(c, a, d))).x <= maxW) return Compose(c, a, d);

        d = ShrinkPart(d, dx => Compose(c, a, dx), maxW, st);
        return Compose(c, a, d);
    }

    string ShrinkPart(string original, Func<string, string> withPart, float maxW, GUIStyle st)
    {
        if (string.IsNullOrEmpty(original)) return original;
        if (st.CalcSize(new GUIContent(withPart(original))).x <= maxW) return original;

        int lo = 0, hi = original.Length, best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            string cand = (mid <= 0) ? "…" : original.Substring(0, mid) + "…";
            float w = st.CalcSize(new GUIContent(withPart(cand))).x;
            if (w <= maxW) { best = mid; lo = mid + 1; } else { hi = mid - 1; }
        }
        if (best <= 0) return "…";
        return original.Substring(0, best) + "…";
    }

    void CalculateSize()
    {
        float screenW = Screen.currentResolution.width;

        Vector2 ts = titleStyle.CalcSize(titleGC);
        float titleW = ts.x;
        float titleH = Mathf.Max(ts.y, ICON);

        float bodyPrefW = MeasureBodyPreferredWidth(bodyGC.text);

        boxW = Mathf.Clamp(
            (iconTex != null ? (ICON + ICON_PAD) : 0f) + Mathf.Max(titleW, bodyPrefW) + PADDING * 2f + PIN_RESERVE,
            MIN_W, Mathf.Min(MAX_W, screenW - SCREEN_MARGIN * 2f)
        );

        float innerW = boxW - PADDING * 2f - PIN_RESERVE;
        float textH = bodyStyle.CalcHeight(bodyGC, innerW);

        float extra = 0f;
        float lineH = GetLineHeight(bodyStyle);
        foreach (var im in _images)
        {
            if (im.src == "__HR__")
            {
                float h = 1f + 8f; // línea + padding vertical del render
                extra += (h - lineH);
                continue;
            }

            if (!LinkMarkup.TryResolveTextureOrSprite(im.src, out var tex, out var uv, out var isExternal) || tex == null)
                continue;

            float w = Mathf.Min(innerW, 200f);
            float h2 = uv.width > 0f && uv.height > 0f
                        ? w * (uv.height / Mathf.Max(0.0001f, uv.width))
                        : tex.height * (w / Mathf.Max(1f, (float)tex.width));

            extra += (h2 - lineH);
        }

        boxH = titleH + textH + extra + PADDING * 2f + HEADER_STRIP + 6f;
    }




    float MeasureBodyPreferredWidth(string body)
    {
        if (string.IsNullOrEmpty(body)) return 0f;
        var st = new GUIStyle(bodyStyle) { wordWrap = false, richText = true };
        float max = 0f;
        var lines = body.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var size = st.CalcSize(new GUIContent(lines[i]));
            if (size.x > max) max = size.x;
        }
        return Mathf.Ceil(max);
    }

    void PositionWindow(Rect anchor, bool placeRight)
    {
        float screenW = Screen.currentResolution.width;
        float screenH = Screen.currentResolution.height;
        bool flipUp = (anchor.yMax + 2f + boxH) > (screenH - SCREEN_MARGIN);

        float x = placeRight ? (anchor.xMax + ANCHOR_GAP) : (anchor.xMin - boxW - ANCHOR_GAP);
        float y = flipUp ? (anchor.y - boxH - 2f) : (anchor.yMax + 2f);

        x = Mathf.Clamp(x, SCREEN_MARGIN, screenW - boxW - SCREEN_MARGIN);
        y = Mathf.Clamp(y, SCREEN_MARGIN, screenH - boxH - SCREEN_MARGIN);

        var r = new Rect(Mathf.Floor(x), Mathf.Floor(y), Mathf.Ceil(boxW), Mathf.Ceil(boxH));
        position = r;
        minSize = new Vector2(r.width, r.height);
        maxSize = new Vector2(r.width, r.height);
    }

    void Update()
    {
        var now = EditorApplication.timeSinceStartup;

        bool overSelf = (EditorWindow.mouseOverWindow == this);
        if (overSelf) lastMouseOverTS = now;

        bool overAnchorRecently = (now - lastAnchorHoverTS) <= CLOSE_DELAY;

        if (overAnchorRecently && now >= nextHierarchyRepaintTS)
        {
            EditorApplication.RepaintHierarchyWindow();
            nextHierarchyRepaintTS = now + 0.05;
        }

        if (!_pinned && !overSelf && !overAnchorRecently) Close();
    }

    void OnGUI()
    {
        // Cerrar si se hace click fuera
        if (Event.current.rawType == EventType.MouseDown && EditorWindow.mouseOverWindow != this)
        {
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
            Close();
            return;
        }

        EnsureStyles();

        TooltipRenderer.DrawBackground(position, bg, accent);

        bool preferDarkText = (0.2126f * bg.r + 0.7152f * bg.g + 0.0722f * bg.b) > 0.5f;
        titleStyle.normal.textColor = preferDarkText ? Color.black : Color.white;
        bodyStyle.normal.textColor = preferDarkText ? new Color(0.12f, 0.12f, 0.12f, 1f)
                                                     : new Color(0.95f, 0.95f, 0.97f, 1f);

        var inner = new Rect(PADDING, PADDING + HEADER_STRIP, position.width - PADDING * 2f, position.height - PADDING * 2f - HEADER_STRIP);

        // Título (con chincheta)
        Vector2 ts = titleStyle.CalcSize(titleGC);
        float titleH = Mathf.Max(ts.y, ICON);
        Rect titleR = new Rect(inner.x, inner.y, inner.width, titleH);

        // Reservar hueco pin
        Rect pinRect = new Rect(titleR.xMax - PIN_W, titleR.y + Mathf.Floor((titleH - PIN_W) * 0.5f), PIN_W, PIN_W);
        titleR.width -= PIN_RESERVE;

        // Icono
        if (iconTex != null)
        {
            var iconR = new Rect(titleR.x, titleR.y + Mathf.Floor((titleH - ICON) * 0.5f), ICON, ICON);
            GUI.DrawTexture(iconR, iconTex, ScaleMode.ScaleToFit, true);
            titleR.x += ICON + ICON_PAD;
            titleR.width -= ICON + ICON_PAD;
        }

        GUI.Label(titleR, titleGC, titleStyle);

        var prev = GUI.color;
        GUI.color = _pinned ? new Color(1f, 0.85f, 0.1f, 1f) : new Color(0.6f, 0.6f, 0.6f, 1f);
        if (GUI.Button(pinRect, _starIcon ?? new GUIContent("★"), GUIStyle.none)) { _pinned = !_pinned; Repaint(); }
        GUI.color = prev;

        // Cuerpo (alineado a cabecera; restamos pin)
        var bodyR = new Rect(inner.x, inner.y + titleH + 4f, inner.width - PIN_RESERVE, inner.height - titleH - 4f);

        // === NUEVO: render segmentado con imágenes inline (izquierda) y zonas clickables correctas ===
        RenderBodyWithInlineImages(bodyR);

        HandleLinksClicks();
        HandleChecklistClicks(bodyR);
        HandleImageClicks();

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            Close();
    }

    void RenderBodyWithInlineImages(Rect bodyR)
    {
        foreach (var l in _links) l.hitRects.Clear();
        foreach (var c in _checks) c.hitRects.Clear();
        _imgLayout.Clear();

        string full = bodyGC.text ?? string.Empty;

        var orderedImgs = new List<LinkMarkup.ImageSpan>(_images);
        orderedImgs.Sort((a, b) => a.strMarkerIndex.CompareTo(b.strMarkerIndex));

        float curY = bodyR.y;
        int curStr = 0;

        float contentW = bodyR.width;
        float lineH = GetLineHeight(bodyStyle);

        int FindLineStartStr(int strIdx)
        {
            int p = Mathf.Clamp(strIdx, 0, (full != null ? full.Length : 0));
            int nl = (full != null && p > 0) ? full.LastIndexOf('\n', p - 1) : -1;
            return (nl < 0) ? 0 : (nl + 1);
        }
        int FindLineEndStr(int strIdx)
        {
            int nl = full.IndexOf('\n', strIdx);
            return (nl < 0) ? full.Length : (nl + 1);
        }

        foreach (var im in orderedImgs)
        {
            // 1) texto previo
            int segStartStr = curStr;
            int lineStartStr = FindLineStartStr(im.strMarkerIndex);

            if (lineStartStr > segStartStr)
            {
                string segText = full.Substring(segStartStr, lineStartStr - segStartStr);
                float segH = bodyStyle.CalcHeight(new GUIContent(segText), contentW);

                GUI.Label(new Rect(bodyR.x, curY, contentW, segH), segText, bodyStyle);

                var segMap = LinkMarkup.BuildVisibleIndexMapForIMGUI(segText);
                int segVisStart = _indexMap.str2vis[segStartStr];
                int segVisEnd = _indexMap.str2vis[lineStartStr];

                // links
                {
                    var tmp = new List<LinkMarkup.LinkSpan>();
                    foreach (var li in _links)
                        if (li.vStart >= segVisStart && li.vEnd <= segVisEnd && li.vEnd > li.vStart)
                            tmp.Add(new LinkMarkup.LinkSpan { name = li.name, id = li.id, vStart = li.vStart - segVisStart, vEnd = li.vEnd - segVisStart, isExternal = li.isExternal, isBroken = li.isBroken });

                    if (tmp.Count > 0)
                    {
                        LinkMarkup.LayoutLinkHitRects(new Rect(bodyR.x, curY, contentW, segH),
                            bodyStyle, new GUIContent(segText), segMap, tmp);
                        foreach (var t in tmp)
                        {
                            var real = _links.Find(x => x.id == t.id && x.name == t.name &&
                                                        x.vStart + segVisStart == t.vStart + segVisStart);
                            if (real != null) real.hitRects.AddRange(t.hitRects);
                        }
                    }
                }
                // checks
                {
                    var tmp = new List<LinkMarkup.ChecklistSpan>();
                    foreach (var ck in _checks)
                        if (ck.vContentStart >= segVisStart && ck.vContentStart < segVisEnd)
                            tmp.Add(new LinkMarkup.ChecklistSpan { isChecked = ck.isChecked, rawStateCharIndex = ck.rawStateCharIndex, vContentStart = ck.vContentStart - segVisStart });

                    if (tmp.Count > 0)
                    {
                        LinkMarkup.LayoutChecklistHitRects(new Rect(bodyR.x, curY, contentW, segH),
                            bodyStyle, new GUIContent(segText), segMap, tmp);
                        foreach (var t in tmp)
                        {
                            var real = _checks.Find(x => x.rawStateCharIndex == t.rawStateCharIndex);
                            if (real != null) real.hitRects.AddRange(t.hitRects);
                        }
                    }
                }

                curY += segH;
            }

            // 2) imagen o HR
            if (im.src == "__HR__")
            {
                float hrW = contentW * 0.85f;
                float hrX = bodyR.x + ((contentW - hrW) * 0.5f);
                Rect hr = new Rect(hrX, curY + 4f, hrW, 1f);
                EditorGUI.DrawRect(hr, new Color(1f, 1f, 1f, 0.15f));
                curY += 1f + 8f;
            }
            else if (LinkMarkup.TryResolveTextureOrSprite(im.src, out var tex, out var uv, out var _isExt) && tex != null)
            {
                float w = Mathf.Min(contentW, 200f);
                float h = uv.width > 0f && uv.height > 0f
                            ? w * (uv.height / Mathf.Max(0.0001f, uv.width))
                            : tex.height * (w / Mathf.Max(1f, (float)tex.width));

                float x = bodyR.x + ((contentW - w) * 0.5f);
                var dest = new Rect(x, curY, w, h);

                if (uv.width > 0f && uv.height > 0f && (uv.width != 1f || uv.height != 1f))
                    GUI.DrawTextureWithTexCoords(dest, tex, uv);
                else
                    GUI.DrawTexture(dest, tex, ScaleMode.ScaleToFit, true);

                _imgLayout.Add((im, tex, uv, 0f, w, h, dest));
                curY += h;
            }
            else
            {
                curY += lineH;
            }

            // 3) saltar la línea del marcador
            int lineEndStr = FindLineEndStr(lineStartStr);
            curStr = lineEndStr;
        }

        // 4) resto de texto
        if (curStr < full.Length)
        {
            string segText = full.Substring(curStr);
            float segH = bodyStyle.CalcHeight(new GUIContent(segText), contentW);
            GUI.Label(new Rect(bodyR.x, curY, contentW, segH), segText, bodyStyle);

            var segMap = LinkMarkup.BuildVisibleIndexMapForIMGUI(segText);
            int segVisStart = _indexMap.str2vis[curStr];
            int segVisEnd = _indexMap.visibleLen;

            // links
            {
                var tmp = new List<LinkMarkup.LinkSpan>();
                foreach (var li in _links)
                    if (li.vStart >= segVisStart && li.vEnd <= segVisEnd && li.vEnd > li.vStart)
                        tmp.Add(new LinkMarkup.LinkSpan { name = li.name, id = li.id, vStart = li.vStart - segVisStart, vEnd = li.vEnd - segVisStart, isExternal = li.isExternal, isBroken = li.isBroken });

                if (tmp.Count > 0)
                {
                    LinkMarkup.LayoutLinkHitRects(new Rect(bodyR.x, curY, contentW, segH),
                        bodyStyle, new GUIContent(segText), segMap, tmp);
                    foreach (var t in tmp)
                    {
                        var real = _links.Find(x => x.id == t.id && x.name == t.name &&
                                                    x.vStart + segVisStart == t.vStart + segVisStart);
                        if (real != null) real.hitRects.AddRange(t.hitRects);
                    }
                }
            }
            // checks
            {
                var tmp = new List<LinkMarkup.ChecklistSpan>();
                foreach (var ck in _checks)
                    if (ck.vContentStart >= segVisStart && ck.vContentStart < segVisEnd)
                        tmp.Add(new LinkMarkup.ChecklistSpan { isChecked = ck.isChecked, rawStateCharIndex = ck.rawStateCharIndex, vContentStart = ck.vContentStart - segVisStart });

                if (tmp.Count > 0)
                {
                    LinkMarkup.LayoutChecklistHitRects(new Rect(bodyR.x, curY, contentW, segH),
                        bodyStyle, new GUIContent(segText), segMap, tmp);
                    foreach (var t in tmp)
                    {
                        var real = _checks.Find(x => x.rawStateCharIndex == t.rawStateCharIndex);
                        if (real != null) real.hitRects.AddRange(t.hitRects);
                    }
                }
            }
        }
    }





    float GetLineHeight(GUIStyle style)
        => (style.lineHeight > 0f) ? style.lineHeight : style.CalcSize(new GUIContent("Ay")).y;

    int GetLineStartIndex(string s, int strIndex)
    {
        int p = Mathf.Clamp(strIndex, 0, (s != null ? s.Length : 0));
        int nl = (s != null && p > 0) ? s.LastIndexOf('\n', p - 1) : -1;
        return (nl < 0) ? 0 : (nl + 1);
    }


    void HandleLinksClicks()
    {
        var e = Event.current;
        if (_links == null || _links.Count == 0) return;

        // Un hash estable para que cada rect obtenga siempre el mismo controlID en este frame
        int kLinkHash = "NotesLinkCtrl".GetHashCode();

        for (int i = 0; i < _links.Count; i++)
        {
            var li = _links[i];
            if (li == null || li.hitRects == null) continue;

            for (int j = 0; j < li.hitRects.Count; j++)
            {
                Rect r = li.hitRects[j];

                // Control por-rect con ID estable
                int id = GUIUtility.GetControlID(kLinkHash, FocusType.Passive, r);
                var typeForCtrl = e.GetTypeForControl(id);

                // Cursor sólo en Repaint para evitar sorpresas
                if (typeForCtrl == EventType.Repaint)
                    EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);

                switch (typeForCtrl)
                {
                    case EventType.MouseDown:
                        if (!r.Contains(e.mousePosition)) break;

                        if (e.button == 1) // Contextual inmediato con RMB
                        {
                            NotesLinkActions.ShowContextMenu(li.name, li.id);
                            GUIUtility.keyboardControl = 0;
                            e.Use();
                            return;
                        }

                        if (e.button == 0)
                        {
                            GUIUtility.hotControl = id; // Capturamos el ratón
                            GUIUtility.keyboardControl = 0;
                            e.Use();
                            return;
                        }
                        break;

                    case EventType.MouseDrag:
                        if (GUIUtility.hotControl == id) e.Use(); // Evita que arrastres "a ningún sitio"
                        break;

                    case EventType.MouseUp:
                        if (GUIUtility.hotControl != id) break;

                        GUIUtility.hotControl = 0;
                        // Activamos sólo si sueltas dentro del mismo rect (click real)
                        if (e.button == 0 && r.Contains(e.mousePosition))
                        {
                            ActivateLink(li.id);
                        }
                        e.Use();
                        return;
                }
            }
        }
    }


    void HandleImageClicks()
    {
        var e = Event.current;
        if (e.type != EventType.MouseDown) return;

        foreach (var it in _imgLayout)
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
                ActivateLink(it.img.src); // SOLO ping
                e.Use(); return;
            }
        }
    }


    void ActivateLink(string id)
    {
        if (NotesLinkActions.IsExternal(id)) { Application.OpenURL(id); return; }

        var obj = NotesLinkActions.TryResolveAll(id);
        if (obj != null)
        {
            var toPing = (obj is Component c) ? (UnityEngine.Object)c.gameObject : obj;
            EditorGUIUtility.PingObject(toPing);   // SOLO ping
        }
        else
        {
            Debug.LogWarning($"Enlace no resuelto: {id}");
        }
    }




    void HandleChecklistClicks(Rect bodyR)
    {
        var e = Event.current;

        var so = new SerializedObject(data);
        var pNotes = so.FindProperty("notes");
        string cur = pNotes.stringValue ?? string.Empty;

        foreach (var ck in _checks)
        {
            foreach (var r in ck.hitRects)
            {
                bool newVal = GUI.Toggle(r, ck.isChecked, GUIContent.none);
                if (newVal != ck.isChecked)
                {
                    string updated = LinkMarkup.ToggleChecklistAt(cur, ck.rawStateCharIndex, newVal);
                    if (updated != cur)
                    {
                        Undo.RecordObject(data, "Toggle Checklist");
                        pNotes.stringValue = updated;
                        so.ApplyModifiedProperties();

                        PrepareContent();
                        CalculateSize();
                        Repaint();
                    }
                    e.Use(); return;
                }
            }
        }
    }


}
#endif
