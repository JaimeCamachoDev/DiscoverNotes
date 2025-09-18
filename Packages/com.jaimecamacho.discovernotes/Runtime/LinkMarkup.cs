#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using System.Globalization; // ? nuevo


/// Parser y layout de spans (links y checklists).
/// - Enlaces: [texto](id|guid|GlobalObjectId|Assets/...|http|https|file)
/// - Checklists (inicio de línea): "[] ..." o "[x] ..." (también "- [] ..." o "* [] ...")
public static class LinkMarkup
{
    // ======= Tipos =======
    public sealed class VisibleIndexMap
    {
        public int[] str2vis;
        public int[] vis2str;
        public int visibleLen;
        public string text;
    }

    public sealed class LinkSpan
    {
        public string name;
        public string id;
        public int strStart, strEnd; // en string con rich
        public int vStart, vEnd;     // en índices visibles post-rich
        public bool isExternal;
        public bool isBroken;
        public readonly List<Rect> hitRects = new List<Rect>();
    }

    public sealed class ChecklistSpan
    {
        public bool isChecked;
        public int rawStateCharIndex;  // índice del ' ' o 'x' dentro de [ ]
        public int strContentStart;    // índice STR del primer carácter del texto de la tarea
        public int vContentStart;      // índice VISIBLE correspondiente
        public readonly List<Rect> hitRects = new List<Rect>();
    }

    public sealed class ImageSpan
    {
        public string src;
        public bool isExternal;
        public int strMarkerIndex;    // posición del marcador en el string con rich text
        public int vMarkerIndex;      // posición visible correspondiente
        public readonly List<Rect> hitRects = new List<Rect>();

        // Datos para renderizado robusto
        public Texture2D texture;
        public Rect texCoords = new Rect(0, 0, 1, 1);
        public float width, height;
        public Rect drawRect;
        public bool resolved;
    }

    // Datos para renderizado de texto con imágenes intercaladas
    public sealed class TextSegment
    {
        public string text;
        public int startVisIndex;
        public int endVisIndex;
        public Rect bounds;
    }

    public sealed class RenderLayout
    {
        public List<TextSegment> textSegments = new List<TextSegment>();
        public List<ImageSpan> sortedImages = new List<ImageSpan>();
        public float totalHeight;
    }

    // ======= Regex =======
    static readonly Regex RX_LINK = new Regex(@"\[(?<name>[^\]]+)\]\((?<id>[^)]+)\)", RegexOptions.Compiled);
    // Acepta "[] ..." o "[x] ..." (y variantes con -/*). Preserva el gap tras los corchetes.
    static readonly Regex RX_CHECK = new Regex(
        @"(?m)^(?<indent>\s*(?:[-*]\s*)?)\[(?<state>[ xX])\](?<gap>\s*)(?<text>.*)$",
        RegexOptions.Compiled);

    public static string BuildStyled(
        string raw,
        List<LinkSpan> linksOut,
        List<ChecklistSpan> checksOut,
        List<ImageSpan> imagesOut,
        out VisibleIndexMap map)
    {
        linksOut?.Clear();
        checksOut?.Clear();
        imagesOut?.Clear();

        var links = linksOut ?? new List<LinkSpan>();
        var checks = checksOut ?? new List<ChecklistSpan>();
        var images = imagesOut ?? new List<ImageSpan>();

        if (raw == null) raw = string.Empty;

        var sb = new StringBuilder(raw.Length + 128);
        int i = 0;

        while (i < raw.Length)
        {
            int b = raw.IndexOf('[', i);
            if (b < 0)
            {
                AppendTextWithLinksOnly(raw.Substring(i), sb, links, null);
                break;
            }

            if (b > i) AppendTextWithLinksOnly(raw.Substring(i, b - i), sb, links, null);

            int rb = raw.IndexOf(']', b + 1);
            if (rb < 0)
            {
                AppendTextWithLinksOnly(raw.Substring(b), sb, links, null);
                break;
            }

            string tokenFull = raw.Substring(b + 1, rb - (b + 1)).Trim();
            string keyword, param;
            ParseKeywordAndParam(tokenFull, out keyword, out param);

            int lpScan = rb + 1;
            while (lpScan < raw.Length && char.IsWhiteSpace(raw[lpScan])) lpScan++;
            int lp = (lpScan < raw.Length && raw[lpScan] == '(') ? lpScan : -1;
            if (lp < 0)
            {
                AppendTextWithLinksOnly(raw.Substring(b, rb - b + 1), sb, links, null);
                i = rb + 1;
                continue;
            }

            int rp = FindClosingParen(raw, lp);
            if (rp < 0)
            {
                AppendTextWithLinksOnly(raw.Substring(b, raw.Length - b), sb, links, null);
                break;
            }

            string inner = raw.Substring(lp + 1, rp - (lp + 1));
            if (IsKeyword(keyword))
            {
                string k = keyword.ToLowerInvariant();

                if (k == "bold")
                {
                    sb.Append("<b>");
                    AppendTextWithMarkup(inner, sb, links, null);
                    sb.Append("</b>");
                    i = rp + 1; continue;
                }
                if (k == "italics")
                {
                    sb.Append("<i>");
                    AppendTextWithMarkup(inner, sb, links, null);
                    sb.Append("</i>");
                    i = rp + 1; continue;
                }
                if (k == "color")
                {
                    string col = NormalizeColorToken(param);
                    if (string.IsNullOrEmpty(col)) { AppendTextWithLinksOnly(inner, sb, links, null); }
                    else
                    {
                        sb.Append("<color=").Append(col).Append(">");
                        AppendTextWithMarkup(inner, sb, links, null);
                        sb.Append("</color>");
                    }
                    i = rp + 1; continue;
                }
                if (k == "size")
                {
                    int sizePx;
                    if (!int.TryParse(param, out sizePx)) sizePx = 12;
                    sizePx = Mathf.Clamp(sizePx, 6, 64);
                    sb.Append("<size=").Append(sizePx).Append(">");
                    AppendTextWithMarkup(inner, sb, links, null);
                    sb.Append("</size>");
                    i = rp + 1; continue;
                }
                if (k.StartsWith("check"))
                {
                    bool isChecked = false;
                    if (k == "checkx") isChecked = true;
                    else if (!string.IsNullOrEmpty(param))
                    {
                        string p = param.Trim().ToLowerInvariant();
                        isChecked = (p == "1" || p == "true" || p == "x");
                    }

                    string box = "     ";
                    int contentStart = sb.Length + box.Length;

                    sb.Append(box);
                    if (isChecked) sb.Append("<color=#444>");
                    AppendTextWithMarkup(inner, sb, links, isChecked ? "#888888" : null);
                    if (isChecked) sb.Append("</color>");

                    checks.Add(new ChecklistSpan
                    {
                        isChecked = isChecked,
                        rawStateCharIndex = b,
                        strContentStart = contentStart
                    });

                    i = rp + 1; continue;
                }
                if (k == "img")
                {
                    string id = inner.Trim();

                    // NUEVO: altura opcional en píxeles con [img=ALTURA](id)
                    int requestedHeight = 0;
                    if (!string.IsNullOrEmpty(param))
                    {
                        int.TryParse(param.Trim(), out requestedHeight);
                        requestedHeight = Mathf.Clamp(requestedHeight, 0, 4096); // 0 = auto
                    }

                    var span = new ImageSpan
                    {
                        src = id,
                        isExternal = IsExternal(id),
                        strMarkerIndex = sb.Length,
                        // Usamos 'height' como “altura solicitada” si > 0; el layout la respetará.
                        height = requestedHeight > 0 ? requestedHeight : 0f
                    };

                    images.Add(span);

                    // Marcador de línea para imagen/HR
                    sb.Append('\u200B');
                    sb.Append('\n');

                    i = rp + 1; continue;
                }
                if (k == "tag")
                {
                    const string tagColor = "#FFD54A";
                    sb.Append("<b><color=").Append(tagColor).Append(">@");
                    AppendTextWithMarkup(inner, sb, links, tagColor);
                    sb.Append("</color></b>");
                    i = rp + 1; continue;
                }

                // Desconocido ? literal
                AppendTextWithLinksOnly(raw.Substring(b, rp - b + 1), sb, links, null);
                i = rp + 1; continue;
            }

            // Enlace normal [texto](id)
            {
                string label = tokenFull;
                string id = inner.Trim();

                bool isExt = IsExternal(id);
                bool resolved = isExt || TryResolve(id, out _);

                string baseColor = isExt ? "#4EA3FF" : (resolved ? "#4EA3FF" : "#FF6A6A");
                string color = baseColor;

                const string prefixA = "<color=";
                const string prefixB = "><b>";
                const string suffix = "</b></color>";

                int strStart = sb.Length + (prefixA.Length + color.Length + prefixB.Length);
                sb.Append(prefixA).Append(color).Append(prefixB).Append(label).Append(suffix);
                int strEnd = strStart + label.Length;

                links.Add(new LinkSpan
                {
                    name = label,
                    id = id,
                    strStart = strStart,
                    strEnd = strEnd,
                    isExternal = isExt,
                    isBroken = !resolved && !isExt
                });

                i = rp + 1;
            }
        }

        string styled = sb.ToString();
        map = BuildVisibleIndexMapForIMGUI(styled);

        for (int j = 0; j < links.Count; j++)
        {
            var li = links[j];
            li.vStart = SafeStrToVis(map, li.strStart);
            li.vEnd = SafeStrToVis(map, li.strEnd);
        }
        for (int j = 0; j < checks.Count; j++)
        {
            var c = checks[j];
            c.vContentStart = SafeStrToVis(map, c.strContentStart);
        }
        for (int j = 0; j < images.Count; j++)
        {
            images[j].vMarkerIndex = SafeStrToVis(map, images[j].strMarkerIndex);
        }

        return styled;
    }



    // ======= NUEVO SISTEMA DE RENDERIZADO ROBUSTO =======

    /// <summary>
    /// Calcula el layout completo para renderizar texto con imágenes intercaladas
    /// </summary>
    public static RenderLayout CalculateRenderLayout(Rect bodyRect, GUIStyle textStyle, GUIContent textContent, List<ImageSpan> images, float maxImageWidth = 200f)
    {
        var layout = new RenderLayout();
        if (images == null || images.Count == 0)
        {
            // Solo texto, sin imágenes
            layout.textSegments.Add(new TextSegment
            {
                text = textContent.text,
                startVisIndex = 0,
                endVisIndex = textContent.text?.Length ?? 0,
                bounds = new Rect(bodyRect.x, bodyRect.y, bodyRect.width, textStyle.CalcHeight(textContent, bodyRect.width))
            });
            layout.totalHeight = layout.textSegments[0].bounds.height;
            return layout;
        }

        // Ordenar imágenes por posición
        layout.sortedImages = new List<ImageSpan>(images);
        layout.sortedImages.Sort((a, b) => a.vMarkerIndex.CompareTo(b.vMarkerIndex));

        // Preparar imágenes resueltas
        PrepareImageSizes(layout.sortedImages, bodyRect.width, maxImageWidth);

        // Calcular segmentos de texto e intercalar imágenes
        string fullText = textContent.text ?? "";
        int currentPos = 0;
        float currentY = bodyRect.y;

        foreach (var img in layout.sortedImages)
        {
            if (!img.resolved) continue;

            // Segmento de texto antes de la imagen
            if (img.vMarkerIndex > currentPos)
            {
                string segmentText = fullText.Substring(currentPos, img.vMarkerIndex - currentPos);
                if (!string.IsNullOrEmpty(segmentText))
                {
                    var segmentContent = new GUIContent(segmentText);
                    float segmentHeight = textStyle.CalcHeight(segmentContent, bodyRect.width);

                    var segment = new TextSegment
                    {
                        text = segmentText,
                        startVisIndex = currentPos,
                        endVisIndex = img.vMarkerIndex,
                        bounds = new Rect(bodyRect.x, currentY, bodyRect.width, segmentHeight)
                    };
                    layout.textSegments.Add(segment);
                    currentY += segmentHeight;
                }
            }

            // Colocar imagen
            float imgX = bodyRect.x + (bodyRect.width - img.width) * 0.5f; // centrada
            img.drawRect = new Rect(imgX, currentY, img.width, img.height);
            currentY += img.height;

            currentPos = img.vMarkerIndex;
        }

        // Segmento de texto final
        if (currentPos < fullText.Length)
        {
            string remainingText = fullText.Substring(currentPos);
            if (!string.IsNullOrEmpty(remainingText))
            {
                var segmentContent = new GUIContent(remainingText);
                float segmentHeight = textStyle.CalcHeight(segmentContent, bodyRect.width);

                var segment = new TextSegment
                {
                    text = remainingText,
                    startVisIndex = currentPos,
                    endVisIndex = fullText.Length,
                    bounds = new Rect(bodyRect.x, currentY, bodyRect.width, segmentHeight)
                };
                layout.textSegments.Add(segment);
                currentY += segmentHeight;
            }
        }

        layout.totalHeight = currentY - bodyRect.y;
        return layout;
    }

    /// <summary>
    /// Renderiza el layout calculado
    /// </summary>
    //public static void RenderLayout(RenderLayout layout, GUIStyle textStyle, GUIContent originalContent)
    //{
    //    // Renderizar segmentos de texto
    //    foreach (var segment in layout.textSegments)
    //    {
    //        if (!string.IsNullOrEmpty(segment.text))
    //        {
    //            var segmentContent = new GUIContent(segment.text);
    //            GUI.Label(segment.bounds, segmentContent, textStyle);
    //        }
    //    }

    //    // Renderizar imágenes
    //    foreach (var img in layout.sortedImages)
    //    {
    //        if (!img.resolved || img.texture == null) continue;

    //        if (img.texCoords.width > 0f && img.texCoords.height > 0f &&
    //            (img.texCoords.width != 1f || img.texCoords.height != 1f ||
    //             img.texCoords.x != 0f || img.texCoords.y != 0f))
    //        {
    //            GUI.DrawTextureWithTexCoords(img.drawRect, img.texture, img.texCoords);
    //        }
    //        else
    //        {
    //            GUI.DrawTexture(img.drawRect, img.texture, ScaleMode.ScaleToFit, true);
    //        }
    //    }
    //}

    /// <summary>
    /// Calcula las áreas clickables para los enlaces considerando el nuevo layout
    /// </summary>
    public static void LayoutLinkHitRectsWithImages(RenderLayout layout, GUIStyle textStyle, VisibleIndexMap map, List<LinkSpan> links)
    {
        foreach (var link in links) link.hitRects.Clear();
        if (links == null || links.Count == 0) return;

        float lineHeight = textStyle.lineHeight > 0 ? textStyle.lineHeight : textStyle.CalcSize(new GUIContent("Ay")).y;

        foreach (var link in links)
        {
            int vStart = Mathf.Clamp(link.vStart, 0, map.visibleLen);
            int vEnd = Mathf.Clamp(link.vEnd, 0, map.visibleLen);
            if (vEnd <= vStart) continue;

            // Encontrar en qué segmento(s) de texto aparece este enlace
            foreach (var segment in layout.textSegments)
            {
                int segStart = segment.startVisIndex;
                int segEnd = segment.endVisIndex;

                // Calcular intersección
                int overlapStart = Mathf.Max(vStart, segStart);
                int overlapEnd = Mathf.Min(vEnd, segEnd);

                if (overlapEnd <= overlapStart) continue;

                // Convertir a posiciones relativas dentro del segmento
                int relStart = overlapStart - segStart;
                int relEnd = overlapEnd - segStart;

                var segmentContent = new GUIContent(segment.text);

                // Calcular rectángulos dentro de este segmento
                CalculateLinkRectsInSegment(segment.bounds, textStyle, segmentContent, relStart, relEnd, lineHeight, link.hitRects);
            }

            // Añadir cursor para todas las áreas
            foreach (var rect in link.hitRects)
            {
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            }
        }
    }

    /// <summary>
    /// Actualiza las áreas clickables de las imágenes basándose en su drawRect
    /// </summary>
    public static void UpdateImageHitRects(List<ImageSpan> images)
    {
        foreach (var img in images)
        {
            img.hitRects.Clear();
            if (img.resolved && img.drawRect.width > 0 && img.drawRect.height > 0)
            {
                img.hitRects.Add(img.drawRect);
                EditorGUIUtility.AddCursorRect(img.drawRect, MouseCursor.Link);
            }
        }
    }

    // ======= Helpers privados =======

    private static void PrepareImageSizes(List<ImageSpan> images, float containerWidth, float maxImageWidth)
    {
        foreach (var img in images)
        {
            if (!img.resolved || img.texture == null) continue;

            // Si el usuario fija altura [img=XXX], permitimos usar todo el ancho disponible del contenedor.
            // Si no, mantenemos el ancho máximo "auto" (ej. 200px) que nos pasen por maxImageWidth.
            float maxW = (img.height > 0f) ? containerWidth : Mathf.Min(containerWidth, maxImageWidth);

            float aspect = (img.texCoords.width > 0f && img.texCoords.height > 0f)
                ? (img.texCoords.height / Mathf.Max(0.0001f, img.texCoords.width))
                : ((float)img.texture.height / Mathf.Max(1f, (float)img.texture.width));

            if (img.height > 0f)
            {
                float h = img.height;
                float w = h / Mathf.Max(0.0001f, aspect);

                if (w > maxW)
                {
                    float s = maxW / w;
                    w = maxW;
                    h = Mathf.Max(1f, h * s);
                }

                img.width = w;
                img.height = h;
            }
            else
            {
                img.width = maxW;
                img.height = maxW * aspect;
            }
        }
    }



    private static void CalculateLinkRectsInSegment(Rect segmentBounds, GUIStyle style, GUIContent segmentContent,
                                                   int relStart, int relEnd, float lineHeight, List<Rect> hitRects)
    {
        bool hasCurrent = false;
        float curY = 0f, minX = 0f, maxX = 0f;

        for (int i = relStart; i < relEnd; i++)
        {
            Vector2 a = style.GetCursorPixelPosition(segmentBounds, segmentContent, i);
            Vector2 b = style.GetCursorPixelPosition(segmentBounds, segmentContent, i + 1);

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
                if (maxX > minX) hitRects.Add(new Rect(minX, curY, maxX - minX, lineHeight));
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
            hitRects.Add(new Rect(minX, curY, maxX - minX, lineHeight));
    }

    // ==== Helper: localizar paréntesis de cierre, admite anidados y escapados \) ====
    static int FindClosingParen(string s, int lp)
    {
        if (lp < 0 || lp >= s.Length || s[lp] != '(') return -1;
        int depth = 0;
        for (int i = lp; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\') { i++; continue; }  // saltar carácter escapado
            if (c == '(') depth++;
            else if (c == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    // ==== Helper: parsear "keyword" y parámetro opcional en forma "key=param" ====
    static void ParseKeywordAndParam(string token, out string keyword, out string param)
    {
        keyword = token ?? string.Empty;
        param = null;
        int eq = keyword.IndexOf('=');
        if (eq >= 0)
        {
            param = keyword.Substring(eq + 1).Trim();
            keyword = keyword.Substring(0, eq).Trim();
        }
        if (string.Equals(keyword, "italic", StringComparison.OrdinalIgnoreCase))
            keyword = "italics";
    }

    // ==== Helper: palabra clave conocida? ====
    static bool IsKeyword(string k)
    {
        if (string.IsNullOrEmpty(k)) return false;
        k = k.Trim().ToLowerInvariant();
        switch (k)
        {
            case "bold":
            case "italics":
            case "color":
            case "size":
            case "check":
            case "checkx":
            case "img":
            case "tag":
                return true;
            default:
                return false;
        }
    }


    // ==== Helper: normalizar color -> nombre o #rrggbb(aa) ====
    static string NormalizeColorToken(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        string s = raw.Trim().Trim('\'', '"');

        if (s.StartsWith("#"))
        {
            if (s.Length == 4 || s.Length == 5 || s.Length == 7 || s.Length == 9) return s;
            if (s.Length == 3 || s.Length == 6 || s.Length == 8) return "#" + s;
            return null;
        }

        string low = s.ToLowerInvariant();
        if (LooksLikeValidColorToken(low)) return low;

        // rgb(a) o floats enteros/0-1 separados por coma -> #rrggbb(aa)
        var parts = low.Split(',');
        if (parts.Length == 3 || parts.Length == 4)
        {
            bool floats = parts[0].Contains(".");
            int r, g, b, a = 255;
            if (floats)
            {
                float rf = Mathf.Clamp01(ParseFloat(parts[0]));
                float gf = Mathf.Clamp01(ParseFloat(parts[1]));
                float bf = Mathf.Clamp01(ParseFloat(parts[2]));
                if (parts.Length == 4) a = Mathf.RoundToInt(Mathf.Clamp01(ParseFloat(parts[3])) * 255f);
                r = Mathf.RoundToInt(rf * 255f);
                g = Mathf.RoundToInt(gf * 255f);
                b = Mathf.RoundToInt(bf * 255f);
            }
            else
            {
                r = Mathf.Clamp(ParseInt(parts[0]), 0, 255);
                g = Mathf.Clamp(ParseInt(parts[1]), 0, 255);
                b = Mathf.Clamp(ParseInt(parts[2]), 0, 255);
                if (parts.Length == 4) a = Mathf.Clamp(ParseInt(parts[3]), 0, 255);
            }
            return (parts.Length == 4)
                ? $"#{r:X2}{g:X2}{b:X2}{a:X2}"
                : $"#{r:X2}{g:X2}{b:X2}";
        }
        return null;
    }

    static int ParseInt(string s) { int.TryParse(s.Trim(), out int v); return v; }
    static float ParseFloat(string s)
    {
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float v);
        return v;
    }

    // ==== Helper: alternar estado de [check]/[checkx] cercano a un índice ====
    public static string ToggleChecklistAt(string raw, int hintIndex, bool? forceState = null)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        int n = raw.Length;
        hintIndex = Mathf.Clamp(hintIndex, 0, n - 1);

        // Buscar el '[' más cercano hacia atrás
        int lb = raw.LastIndexOf('[', hintIndex);
        if (lb < 0) return raw;

        int rb = raw.IndexOf(']', lb + 1);
        if (rb < 0) return raw;

        string token = raw.Substring(lb + 1, rb - (lb + 1)).Trim(); // "check" o "checkx" o "check=..."
        string key, param;
        ParseKeywordAndParam(token, out key, out param);
        string lowKey = key.ToLowerInvariant();
        if (lowKey != "check" && lowKey != "checkx") return raw;

        bool isChecked = (lowKey == "checkx");
        if (!string.IsNullOrEmpty(param))
        {
            string p = param.Trim().ToLowerInvariant();
            if (p == "1" || p == "true" || p == "x") isChecked = true;
            else if (p == "0" || p == "false") isChecked = false;
        }

        bool newState = forceState.HasValue ? forceState.Value : !isChecked;

        string newToken = newState ? "checkx" : "check";

        var sb = new StringBuilder(raw.Length + 4);
        sb.Append(raw, 0, lb + 1);
        sb.Append(newToken);
        sb.Append(raw, rb, raw.Length - rb);
        return sb.ToString();
    }

    static void AppendTextWithLinksOnly(string rawChunk, StringBuilder sb, List<LinkSpan> links, string linkColorOverride)
    {
        int last = 0;
        foreach (Match m in RX_LINK.Matches(rawChunk))
        {
            if (m.Index > last) sb.Append(rawChunk, last, m.Index - last);

            string rawName = m.Groups["name"].Value;
            string id = m.Groups["id"].Value.Trim();

            id = id == null ? string.Empty : id.Trim();

            // Saneado básico (duplicamos lógica aquí para no depender de NotesLinkActions desde LinkMarkup)
            static string _Sanitize(string s)
            {
                if (string.IsNullOrEmpty(s)) return string.Empty;
                var sb = new System.Text.StringBuilder(s.Length);
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    bool drop = c == '\u200B' || c == '\u200C' || c == '\u200D' || c == '\uFEFF' ||
                                c == '\u00A0' || c == '\u200E' || c == '\u200F' ||
                                c == '\u202A' || c == '\u202B' || c == '\u202C' || c == '\u202D' || c == '\u202E' ||
                                c == '\r' || c == '\n' || c == '\t';
                    if (!drop) sb.Append(c);
                }
                return sb.ToString();
            }
            id = _Sanitize(id);


            bool isExt = IsExternal(id);
            bool resolved = isExt || TryResolve(id, out _);

            string baseColor = isExt ? "#4EA3FF" : (resolved ? "#4EA3FF" : "#FF6A6A"); // rojo si roto
            string color = string.IsNullOrEmpty(linkColorOverride) ? baseColor : linkColorOverride;

            const string prefixA = "<color=";
            const string prefixB = "><b>";
            const string suffix = "</b></color>";

            int strStart = sb.Length + (prefixA.Length + color.Length + prefixB.Length);
            sb.Append(prefixA).Append(color).Append(prefixB).Append(rawName).Append(suffix);
            int strEnd = strStart + rawName.Length;

            links.Add(new LinkSpan
            {
                name = rawName,
                id = id,
                strStart = strStart,
                strEnd = strEnd,
                isExternal = isExt,
                isBroken = !resolved && !isExt
            });

            last = m.Index + m.Length;
        }
        if (last < rawChunk.Length) sb.Append(rawChunk, last, rawChunk.Length - last);
    }

    // Parseador inline con soporte de anidación para: bold, italics, color, size, tag + enlaces.
// No procesa img/check aquí a propósito (son de bloque). Si aparecen, se tratan como literal/enlace.
static void AppendTextWithMarkup(string rawChunk, StringBuilder sb, List<LinkSpan> links, string linkColorOverride)
{
    if (string.IsNullOrEmpty(rawChunk)) return;

    int i = 0, N = rawChunk.Length;
    while (i < N)
    {
        int b = rawChunk.IndexOf('[', i);
        if (b < 0) { AppendTextWithLinksOnly(rawChunk.Substring(i), sb, links, linkColorOverride); break; }

        if (b > i) AppendTextWithLinksOnly(rawChunk.Substring(i, b - i), sb, links, linkColorOverride);

        int rb = rawChunk.IndexOf(']', b + 1);
        if (rb < 0) { AppendTextWithLinksOnly(rawChunk.Substring(b), sb, links, linkColorOverride); break; }

        string tokenFull = rawChunk.Substring(b + 1, rb - (b + 1)).Trim();
        string keyword, param;
        ParseKeywordAndParam(tokenFull, out keyword, out param);

        int lpScan = rb + 1;
        while (lpScan < N && char.IsWhiteSpace(rawChunk[lpScan])) lpScan++;
        int lp = (lpScan < N && rawChunk[lpScan] == '(') ? lpScan : -1;
        if (lp < 0) { AppendTextWithLinksOnly(rawChunk.Substring(b, rb - b + 1), sb, links, linkColorOverride); i = rb + 1; continue; }

        int rp = FindClosingParen(rawChunk, lp);
        if (rp < 0) { AppendTextWithLinksOnly(rawChunk.Substring(b), sb, links, linkColorOverride); break; }

        string inner = rawChunk.Substring(lp + 1, rp - (lp + 1));

        if (IsKeyword(keyword))
        {
            string k = keyword.ToLowerInvariant();

            if (k == "bold")
            {
                sb.Append("<b>");
                AppendTextWithMarkup(inner, sb, links, linkColorOverride);
                sb.Append("</b>");
                i = rp + 1; continue;
            }
            if (k == "italics")
            {
                sb.Append("<i>");
                AppendTextWithMarkup(inner, sb, links, linkColorOverride);
                sb.Append("</i>");
                i = rp + 1; continue;
            }
            if (k == "color")
            {
                string col = NormalizeColorToken(param);
                if (string.IsNullOrEmpty(col))
                {
                    AppendTextWithMarkup(inner, sb, links, linkColorOverride);
                }
                else
                {
                    sb.Append("<color=").Append(col).Append(">");
                    AppendTextWithMarkup(inner, sb, links, linkColorOverride);
                    sb.Append("</color>");
                }
                i = rp + 1; continue;
            }
            if (k == "size")
            {
                int sizePx; if (!int.TryParse(param, out sizePx)) sizePx = 12;
                sizePx = Mathf.Clamp(sizePx, 6, 64);
                sb.Append("<size=").Append(sizePx).Append(">");
                AppendTextWithMarkup(inner, sb, links, linkColorOverride);
                sb.Append("</size>");
                i = rp + 1; continue;
            }
            if (k == "tag")
            {
                const string tagColor = "#FFD54A";
                sb.Append("<b><color=").Append(tagColor).Append(">@");
                // Dentro de tag, los enlaces heredan el color del tag
                AppendTextWithMarkup(inner, sb, links, tagColor);
                sb.Append("</color></b>");
                i = rp + 1; continue;
            }

            // Otros keywords (img/check/...) se consideran de bloque -> tratarlos como literal/enlace
        }

        // No es keyword conocida: deja que la regex de enlaces lo gestione o quede literal si no matchea
        AppendTextWithLinksOnly(rawChunk.Substring(b, rp - b + 1), sb, links, linkColorOverride);
        i = rp + 1;
    }
}


    // ====== Layout de zonas clicables (MÉTODO LEGACY MANTENIDO PARA COMPATIBILIDAD) ======
    public static void LayoutLinkHitRects(Rect bodyR, GUIStyle style, GUIContent content, VisibleIndexMap map, List<LinkSpan> links)
    {
        foreach (var l in links) l.hitRects.Clear();
        if (links == null || links.Count == 0 || string.IsNullOrEmpty(content?.text)) return;

        float lineH = style.lineHeight > 0 ? style.lineHeight : style.CalcSize(new GUIContent("Ay")).y;

        foreach (var li in links)
        {
            int vStart = Mathf.Clamp(li.vStart, 0, map.visibleLen);
            int vEnd = Mathf.Clamp(li.vEnd, 0, map.visibleLen);
            if (vEnd <= vStart) continue;

            bool hasCurrent = false;
            float curY = 0f, minX = 0f, maxX = 0f;

            for (int v = vStart; v < vEnd; v++)
            {
                Vector2 a = style.GetCursorPixelPosition(bodyR, content, v);
                Vector2 b = style.GetCursorPixelPosition(bodyR, content, v + 1);

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
                    minX = a.x; maxX = Mathf.Max(a.x, b.x);
                }
                else
                {
                    maxX = Mathf.Max(maxX, b.x);
                }
            }
            if (hasCurrent && maxX > minX) li.hitRects.Add(new Rect(minX, curY, maxX - minX, lineH));

            foreach (var r in li.hitRects) EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
        }
    }

    public static void LayoutChecklistHitRects(Rect bodyR, GUIStyle style, GUIContent content, VisibleIndexMap map, List<ChecklistSpan> checks)
    {
        if (checks == null || checks.Count == 0 || string.IsNullOrEmpty(content?.text)) return;

        float lineH = style.lineHeight > 0 ? style.lineHeight : style.CalcSize(new GUIContent("Ay")).y;

        // Gap = ~3.5 espacios (ajústalo a 3 o 4 si lo prefieres)
        float spaceW = style.CalcSize(new GUIContent(" ")).x;
        float gapPx = Mathf.Round(spaceW * 3.5f);

        foreach (var ck in checks)
        {
            ck.hitRects.Clear();

            int v = Mathf.Clamp(ck.vContentStart, 0, map.visibleLen);
            Vector2 a = style.GetCursorPixelPosition(bodyR, content, v);

            float size = lineH;

            // Caja inmediatamente a la izquierda del texto, dejando el hueco pedido
            float x = Mathf.Max(bodyR.x+00f, a.x - (size + gapPx));
            Rect box = new Rect(x, a.y, size, size);

            ck.hitRects.Add(box);
            EditorGUIUtility.AddCursorRect(box, MouseCursor.Link);
        }
    }



    // ====== Resolución ======
    public static bool IsExternal(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        id = id.Trim().ToLowerInvariant();
        return id.StartsWith("http://") || id.StartsWith("https://") || id.StartsWith("file://");
    }

    public static bool TryResolve(string id, out UnityEngine.Object obj)
    {
        obj = null; if (string.IsNullOrEmpty(id)) return false;

        if (GlobalObjectId.TryParse(id, out var gid))
        {
            obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(gid);
            if (obj != null) return true;
        }

        if (id.Length == 32 && IsHex(id))
        {
            string path = AssetDatabase.GUIDToAssetPath(id);
            if (!string.IsNullOrEmpty(path))
            {
                obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (obj != null) return true;
            }
        }

        if (id.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(id);
            if (obj != null) return true;
        }

        return false;
    }

    public static bool TryResolveTextureOrSprite(string src, out Texture2D tex, out Rect texCoords, out bool isExternal)
    {
        tex = null; texCoords = new Rect(0, 0, 1, 1); isExternal = IsExternal(src);
        if (isExternal) return false;

        if (TryResolve(src, out var obj) && obj != null)
        {
            if (obj is Texture2D t) { tex = t; return true; }
            if (obj is Sprite s && s.texture != null)
            {
                tex = s.texture;
                var r = s.textureRect;
                texCoords = new Rect(r.x / tex.width, r.y / tex.height, r.width / tex.width, r.height / tex.height);
                return true;
            }
        }
        return false;
    }

    static bool IsHex(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!hex) return false;
        }
        return true;
    }

    // ====== Mapeo visible/real para IMGUI ======
    public static VisibleIndexMap BuildVisibleIndexMapForIMGUI(string text)
    {
        var map = new VisibleIndexMap { text = text ?? string.Empty };
        int n = map.text.Length;
        map.str2vis = new int[n + 1];

        var vis2strList = new List<int>(n + 1);
        int i = 0, v = 0;
        vis2strList.Add(0);

        while (i < n)
        {
            if (TryGetValidIMGUIRichTag(map.text, i, out int tagLen))
            {
                for (int k = 0; k < tagLen; k++) map.str2vis[i + k] = v;
                i += tagLen;
                continue;
            }

            int j = NextGraphemeClusterBreak(map.text, i);

            for (int k = i; k < j; k++) map.str2vis[k] = v; // todo el “grapheme” comparte v
            i = j;
            v++;
            vis2strList.Add(i);
        }

        map.str2vis[n] = v;
        vis2strList.Add(n);
        map.vis2str = vis2strList.ToArray();
        map.visibleLen = v;
        return map;
    }

    static int NextGraphemeClusterBreak(string s, int i)
    {
        int n = s.Length;
        int j = i;

        // base (par sustituto o single)
        if (j + 1 < n && char.IsHighSurrogate(s[j]) && char.IsLowSurrogate(s[j + 1])) j += 2;
        else j += 1;

        // VSxx (incluye VS16)
        if (j < n && IsVariationSelector(s[j])) j++;

        // diacríticos combinantes
        while (j < n && IsCombiningMark(s[j])) j++;

        // keycap U+20E3
        if (j < n && s[j] == '\u20E3') j++;

        // secuencias ZWJ (?????, ???????????, etc.)
        while (j < n && s[j] == '\u200D')
        {
            j++; // consumir ZWJ

            // siguiente base
            if (j + 1 < n && char.IsHighSurrogate(s[j]) && char.IsLowSurrogate(s[j + 1])) j += 2;
            else if (j < n) j++;

            if (j < n && IsVariationSelector(s[j])) j++;
            while (j < n && IsCombiningMark(s[j])) j++;
            if (j < n && s[j] == '\u20E3') j++;
        }

        return j;
    }

    static bool IsVariationSelector(char c) => (c >= '\uFE00' && c <= '\uFE0F');
    static bool IsCombiningMark(char c)
    {
        var cat = CharUnicodeInfo.GetUnicodeCategory(c);
        return cat == UnicodeCategory.NonSpacingMark
            || cat == UnicodeCategory.SpacingCombiningMark
            || cat == UnicodeCategory.EnclosingMark;
    }


    static int SafeStrToVis(VisibleIndexMap map, int strIndex)
    {
        strIndex = Mathf.Clamp(strIndex, 0, map.str2vis.Length - 1);
        return map.str2vis[strIndex];
    }

    static bool TryGetValidIMGUIRichTag(string text, int i, out int tagLen)
    {
        tagLen = 0;
        if (i >= text.Length || text[i] != '<') return false;
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
        if (v[0] == '#')
        {
            int L = v.Length; return (L == 4 || L == 5 || L == 7 || L == 9);
        }
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
            case "gray": return true;
            default: return false;
        }
    }
}
#endif