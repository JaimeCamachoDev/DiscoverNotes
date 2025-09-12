#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public class NotesTooltipWindow : EditorWindow
{
    // ====== Estilos y layout ======
    GUIStyle titleStyle, bodyStyle;
    const float MIN_W = 160f;
    const float MAX_W = 1024f; // permitir títulos largos en una sola línea
    const float SCREEN_MARGIN = 12f;
    const float ANCHOR_GAP = 4f;
    const float PADDING = 10f;
    const float HEADER_STRIP = 4f;
    const float GAP = 2f;
    const float ICON = 16f;
    const float ICON_PAD = 6f;

    Texture iconTex;
    GameObjectNotes data;
    GUIContent titleGC, bodyGC;
    float boxW, boxH;
    Color accent, bg;
    double lastMouseOverTS;
    double lastAnchorHoverTS;
    double nextHierarchyRepaintTS;

    const string DATE_FMT = "dd/MM/yyyy";
    // Bloqueo de colocación para evitar jitter
    int _currentTargetId = -1;
    bool _sideLocked = false;
    bool _sidePlaceRight = true; // lado elegido al abrir (true=derecha, false=izquierda)


    // ====== Enlaces clicables ======
    // [NombreVisible](GlobalObjectId)
    static readonly Regex RX_LINK = new Regex(@"\[(?<name>[^\]]+)\]\((?<id>[^)]+)\)", RegexOptions.Compiled);

    class IndexMap
    {
        public int[] str2vis;   // len = text.Length + 1
        public int[] vis2str;   // len = visibleLen + 1
        public int visibleLen;
        public string text;
    }

    class LinkInfo
    {
        public string name;
        public string id;

        // Rango en el STRING PINTADO (con nuestros <color>/<b>)
        public int strStart;
        public int strEnd;

        // Rango en índices VISIBLES post-rich
        public int vStart;
        public int vEnd;

        // Zonas de click por línea
        public readonly List<Rect> hitRects = new List<Rect>();
    }

    readonly List<LinkInfo> _links = new List<LinkInfo>();
    IndexMap _indexMap;

    public static NotesTooltipWindow Create()
    {
        var w = CreateInstance<NotesTooltipWindow>();
        w.hideFlags = HideFlags.DontSave;
        return w;
    }

    void EnsureStyles()
    {
        if (titleStyle != null) return;
        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            richText = true,
            wordWrap = false,               // ← UNA SOLA LÍNEA
            clipping = TextClipping.Clip,   // ← no envolver, recortar si hiciera falta
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

    public void ShowFor(GameObjectNotes notes, Rect anchorScreenRect)
    {
        // Si cambia el objeto/nota, reseteamos el bloqueo de lado
        bool targetChanged = _currentTargetId != notes.GetInstanceID();
        if (targetChanged)
        {
            _currentTargetId = notes.GetInstanceID();
            _sideLocked = false;
        }

        EnsureStyles();
        data = notes;

        var cat = NoteStylesProvider.FindCategory(data.Category);
        accent = cat != null ? cat.tooltipAccentBar : new Color(0.25f, 0.5f, 1f, 1f);
        bg = cat != null ? cat.tooltipBackground : new Color(0.12f, 0.12f, 0.14f, 0.985f);
        bg.a = 1f; // opaco

        iconTex = (cat != null && cat.icon != null) ? cat.icon : (EditorIconHelper.GetCategoryIcon(data.Category)?.image);

        PrepareContentAndLinks(anchorScreenRect);   // o tu variante con 1 línea en cabecera
        CalculateSize();

        // Decidir lado solo la PRIMERA vez; luego queda bloqueado hasta que se cierre o cambie de target
        float screenW = Screen.currentResolution.width;
        bool computedPlaceRight = (anchorScreenRect.xMax + ANCHOR_GAP + boxW) <= (screenW - SCREEN_MARGIN);
        if (!_sideLocked)
        {
            _sidePlaceRight = computedPlaceRight;
            _sideLocked = true;
        }

        // ¡OJO! ahora pasamos el lado forzado para evitar alternancias
        PositionWindow(anchorScreenRect, _sidePlaceRight);

        if (!hasFocus) ShowPopup();
        Repaint();

        lastAnchorHoverTS = EditorApplication.timeSinceStartup;
        lastMouseOverTS = lastAnchorHoverTS;
        wantsMouseMove = true;

    }

    void PrepareContentAndLinks(Rect anchorScreenRect)
    {
        string raw = (data.NotesText ?? string.Empty).Trim();
        if (raw.Length > 2000) raw = raw.Substring(0, 2000) + "…";

        string author = string.IsNullOrEmpty(data.Author) ? "Anónimo" : data.Author;
        string date = string.IsNullOrEmpty(data.DateCreated) ? DateTime.Now.ToString(DATE_FMT) : data.DateCreated;
        string catName = string.IsNullOrEmpty(data.Category) ? "Nota" : data.Category;

        float screenW = Screen.currentResolution.width;
        float maxTitleWidth = Mathf.Max(50f, screenW - SCREEN_MARGIN * 2f - PADDING * 2f - (iconTex != null ? (ICON + ICON_PAD) : 0f));

        // Construir SIEMPRE en una sola línea, sin parsear el rich text
        string oneLine = BuildOneLineTitle(catName, author, date, maxTitleWidth, titleStyle);
        titleGC = new GUIContent(oneLine);

        // Cuerpo con enlaces cliqueables (estilo + mapa + links)
        BuildStyledAndLinksAndMap(raw, out string displayStyled, _links, out _indexMap);
        bodyGC = new GUIContent(displayStyled);
    }
    string BuildOneLineTitle(string category, string author, string date, float maxW, GUIStyle st)
    {
        string Sep = "  •  ";
        string Bold(string s) => $"<b>{s}</b>";
        string Compose(string c, string a, string d) => $"{Bold(c)}{Sep}{a}{Sep}{d}";

        string c = category, a = author, d = date;

        // Si ya cabe, listo
        if (st.CalcSize(new GUIContent(Compose(c, a, d))).x <= maxW)
            return Compose(c, a, d);

        // 1) Reducir autor
        a = ShrinkPart(a, (ax) => Compose(c, ax, d), maxW, st);
        if (st.CalcSize(new GUIContent(Compose(c, a, d))).x <= maxW)
            return Compose(c, a, d);

        // 2) Reducir categoría
        c = ShrinkPart(c, (cx) => Compose(cx, a, d), maxW, st);
        if (st.CalcSize(new GUIContent(Compose(c, a, d))).x <= maxW)
            return Compose(c, a, d);

        // 3) Reducir fecha
        d = ShrinkPart(d, (dx) => Compose(c, a, dx), maxW, st);
        return Compose(c, a, d);
    }

    // Reduce por binaria con “…” el texto de una parte concreta
    string ShrinkPart(string original, Func<string, string> composeWithPart, float maxW, GUIStyle st)
    {
        if (string.IsNullOrEmpty(original)) return original;

        // Si ya cabe, no tocar
        if (st.CalcSize(new GUIContent(composeWithPart(original))).x <= maxW)
            return original;

        int lo = 0, hi = original.Length; // buscamos el máximo prefijo que cabe con “…”
        int best = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            string cand = (mid <= 0) ? "…" : original.Substring(0, mid) + "…";
            float w = st.CalcSize(new GUIContent(composeWithPart(cand))).x;
            if (w <= maxW) { best = mid; lo = mid + 1; } else { hi = mid - 1; }
        }
        if (best <= 0) return "…";
        return original.Substring(0, best) + "…";
    }


    // Recorta (con …) primero el autor, luego la categoría y por último la fecha
    string FitTitleToWidth(string richTitle, float maxW, GUIStyle st)
    {
        // Esperamos "<b>cat</b>  •  autor  •  fecha"
        ExtractTitleParts(richTitle, out string cat, out string author, out string date);
        string BuildRich(string c, string a, string d) => $"<b>{c}</b>  •  {a}  •  {d}";

        string cur = BuildRich(cat, author, date);
        if (st.CalcSize(new GUIContent(cur)).x <= maxW) return cur;

        // 1) recortar autor
        string a2 = EllipsizeToWidth(author, maxW - st.CalcSize(new GUIContent(BuildRich(cat, "", date))).x, st);
        cur = BuildRich(cat, a2, date);
        if (st.CalcSize(new GUIContent(cur)).x <= maxW) return cur;

        // 2) recortar categoría
        string c2 = EllipsizeToWidth(cat, maxW - st.CalcSize(new GUIContent(BuildRich("", a2, date))).x, st);
        cur = BuildRich(c2, a2, date);
        if (st.CalcSize(new GUIContent(cur)).x <= maxW) return cur;

        // 3) si aún no cabe, recortar fecha
        string d2 = EllipsizeToWidth(date, maxW - st.CalcSize(new GUIContent(BuildRich(c2, a2, ""))).x, st);
        return BuildRich(c2, a2, d2);
    }

    void ExtractTitleParts(string richTitle, out string cat, out string author, out string date)
    {
        // "<b>cat</b>  •  author  •  date"
        cat = ""; author = ""; date = "";
        try
        {
            int b1 = richTitle.IndexOf("<b>", StringComparison.Ordinal);
            int b2 = richTitle.IndexOf("</b>", StringComparison.Ordinal);
            if (b1 >= 0 && b2 > b1) cat = richTitle.Substring(b1 + 3, b2 - (b1 + 3));

            int after = b2 + 4;
            string rest = (after < richTitle.Length) ? richTitle.Substring(after) : "";
            var parts = rest.Split(new string[] { "•" }, StringSplitOptions.None);
            if (parts.Length >= 2)
            {
                author = parts[0].Replace("  ", "").Replace("·", "").Trim();
                date = parts[1].Trim();
            }
        }
        catch { }
    }

    string EllipsizeToWidth(string text, float maxW, GUIStyle st)
    {
        if (maxW <= 0f) return "…";
        if (string.IsNullOrEmpty(text)) return text;
        var gc = new GUIContent(text);
        if (st.CalcSize(gc).x <= maxW) return text;

        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            string cand = text.Substring(0, mid) + "…";
            if (st.CalcSize(new GUIContent(cand)).x <= maxW) lo = mid;
            else hi = mid - 1;
        }
        if (lo <= 0) return "…";
        return text.Substring(0, lo) + "…";
    }

    void CalculateSize()
    {
        float screenW = Screen.currentResolution.width;

        // Título en UNA sola línea
        Vector2 ts = titleStyle.CalcSize(titleGC);
        float titleW = ts.x;
        float titleH = Mathf.Max(ts.y, ICON);

        // Ancho preferido del cuerpo
        float bodyPrefW = MeasureBodyPreferredWidth(bodyGC.text);

        // Ancho total
        boxW = Mathf.Clamp(
            (iconTex != null ? (ICON + ICON_PAD) : 0f) + Mathf.Max(titleW, bodyPrefW) + PADDING * 2f,
            MIN_W, Mathf.Min(MAX_W, screenW - SCREEN_MARGIN * 2f)
        );

        // Alturas
        float innerW = boxW - PADDING * 2f - (iconTex != null ? (ICON + ICON_PAD) : 0f);
        float bodyH = bodyStyle.CalcHeight(bodyGC, innerW);
        boxH = titleH + bodyH + PADDING * 2f + HEADER_STRIP + 6f;
    }

    void PositionWindow(Rect anchorScreenRect, bool placeRight)
    {
        float screenW = Screen.currentResolution.width;
        float screenH = Screen.currentResolution.height;

        bool flipUp = (anchorScreenRect.yMax + GAP + boxH) > (screenH - SCREEN_MARGIN);

        float x = placeRight
            ? (anchorScreenRect.xMax + ANCHOR_GAP)
            : (anchorScreenRect.xMin - boxW - ANCHOR_GAP);

        float y = flipUp
            ? (anchorScreenRect.y - boxH - GAP)
            : (anchorScreenRect.yMax + GAP);


        // Clamp + coord enteras para estabilidad
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

        if (!overSelf && !overAnchorRecently) Close();
    }

    const double CLOSE_DELAY = 0.18;

    void OnGUI()
    {
        EnsureStyles();

        TooltipRenderer.DrawBackground(position, bg, accent);

        bool preferDarkText = (0.2126f * bg.r + 0.7152f * bg.g + 0.0722f * bg.b) > 0.5f;
        titleStyle.normal.textColor = preferDarkText ? Color.black : Color.white;
        bodyStyle.normal.textColor = preferDarkText ? new Color(0.12f, 0.12f, 0.12f, 1f)
                                                     : new Color(0.95f, 0.95f, 0.97f, 1f);

        var inner = new Rect(PADDING, PADDING + HEADER_STRIP, position.width - PADDING * 2f, position.height - PADDING * 2f - HEADER_STRIP);

        // ---- Título: icono + texto en UNA sola línea
        Vector2 ts = titleStyle.CalcSize(titleGC);
        float titleH = Mathf.Max(ts.y, ICON);
        Rect titleR = new Rect(inner.x, inner.y, inner.width, titleH);

        if (iconTex != null)
        {
            var iconR = new Rect(titleR.x, titleR.y + Mathf.Floor((titleH - ICON) * 0.5f), ICON, ICON);
            GUI.DrawTexture(iconR, iconTex, ScaleMode.ScaleToFit, true);
            titleR.x += ICON + ICON_PAD;
            titleR.width -= ICON + ICON_PAD;
        }

        // No envolver: si hubo que truncar, ya viene con “…”
        GUI.Label(titleR, titleGC, titleStyle);

        // ---- Cuerpo (richText + clicks)
        var bodyR = new Rect(inner.x, inner.y + titleH + 4f, inner.width, inner.height - titleH - 4f);
        GUI.Label(bodyR, bodyGC, bodyStyle);

        // Clickables
        LayoutLinkHitRects(bodyR);
        DrawLinkButtons();
        HandleClickFallback(bodyR);

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
            Close();
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

    // ====== Construcción de estilo + mapa de índices + enlaces ======
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

    // Reconoce etiquetas IMGUI reales
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

    // ====== Layout y click ======
    void LayoutLinkHitRects(Rect bodyR)
    {
        foreach (var l in _links) l.hitRects.Clear();
        if (_links.Count == 0 || string.IsNullOrEmpty(bodyGC.text)) return;

        float lineH = bodyStyle.lineHeight > 0 ? bodyStyle.lineHeight : bodyStyle.CalcSize(new GUIContent("Ay")).y;

        foreach (var li in _links)
        {
            int vStart = Mathf.Clamp(li.vStart, 0, _indexMap.visibleLen);
            int vEnd = Mathf.Clamp(li.vEnd, 0, _indexMap.visibleLen);
            if (vEnd <= vStart) continue;

            bool hasCurrent = false;
            float curY = 0f, minX = 0f, maxX = 0f;

            for (int v = vStart; v < vEnd; v++)
            {
                Vector2 a = bodyStyle.GetCursorPixelPosition(bodyR, bodyGC, v);
                Vector2 b = bodyStyle.GetCursorPixelPosition(bodyR, bodyGC, v + 1);

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

    void DrawLinkButtons()
    {
        foreach (var li in _links)
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

    void HandleClickFallback(Rect bodyR)
    {
        var e = Event.current;
        if (e.type != EventType.MouseUp || e.button != 0) return;

        int vClick = bodyStyle.GetCursorStringIndex(bodyR, bodyGC, e.mousePosition); // índice VISIBLE

        foreach (var li in _links)
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
                // NO cambiar selección. Solo ping.
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

}
#endif
