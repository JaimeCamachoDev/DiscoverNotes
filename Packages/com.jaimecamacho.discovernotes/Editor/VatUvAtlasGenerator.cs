#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

public static class VatUvAtlasGenerator
{
    public struct Result
    {
        public Texture2D atlasTexture;
        public int usedTextureCount;
        public int rows;
        public int columns;
        public int cellWidth;
        public int cellHeight;
    }

    public static bool TryBuildAtlas(IReadOnlyList<Texture2D> textures,
                                     int desiredCount,
                                     int columns,
                                     out Result result,
                                     out string error)
    {
        result = default;
        error = null;

        if (textures == null)
        {
            error = "No se proporcionaron texturas de entrada.";
            return false;
        }

        var validTextures = new List<Texture2D>();
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i] != null)
            {
                validTextures.Add(textures[i]);
            }
        }

        if (validTextures.Count == 0)
        {
            error = "Añade al menos una textura de referencia.";
            return false;
        }

        desiredCount = desiredCount <= 0 ? validTextures.Count : Mathf.Min(desiredCount, validTextures.Count);
        desiredCount = Mathf.Clamp(desiredCount, 1, validTextures.Count);

        columns = columns <= 0 ? desiredCount : Mathf.Clamp(columns, 1, desiredCount);
        int rows = Mathf.CeilToInt(desiredCount / (float)columns);

        int cellWidth = 0;
        int cellHeight = 0;
        for (int i = 0; i < desiredCount; i++)
        {
            var tex = validTextures[i];
            if (tex == null) continue;
            cellWidth = Mathf.Max(cellWidth, tex.width);
            cellHeight = Mathf.Max(cellHeight, tex.height);
        }

        if (cellWidth <= 0 || cellHeight <= 0)
        {
            error = "Las texturas deben tener dimensiones mayores que cero.";
            return false;
        }

        int atlasWidth = Mathf.Max(1, cellWidth * columns);
        int atlasHeight = Mathf.Max(1, cellHeight * rows);

        var rt = RenderTexture.GetTemporary(atlasWidth, atlasHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        GL.PushMatrix();
        try
        {
            GL.LoadPixelMatrix(0, atlasWidth, 0, atlasHeight);
            GL.Clear(true, true, Color.clear);

            for (int i = 0; i < desiredCount; i++)
            {
                var tex = validTextures[i];
                if (tex == null) continue;

                int col = i % columns;
                int row = i / columns;
                float x = col * cellWidth + (cellWidth - tex.width) * 0.5f;
                float y = (rows - 1 - row) * cellHeight + (cellHeight - tex.height) * 0.5f;

                var destRect = new Rect(x, y, tex.width, tex.height);
                Graphics.DrawTexture(destRect, tex, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0);
            }

            var atlas = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false, false)
            {
                name = $"VATAtlas_{desiredCount}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                alphaIsTransparency = true
            };

            atlas.ReadPixels(new Rect(0, 0, atlasWidth, atlasHeight), 0, 0);
            atlas.Apply(false, true);

            result = new Result
            {
                atlasTexture = atlas,
                usedTextureCount = desiredCount,
                rows = rows,
                columns = columns,
                cellWidth = cellWidth,
                cellHeight = cellHeight
            };
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            GL.PopMatrix();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
#endif
