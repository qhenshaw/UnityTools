using UnityEngine;

namespace UnityTools.PlaytestFeedback.Internal
{
    internal static class ImageUtility
    {
        /// <summary>Copies a region of a readable texture into a new texture. Rect origin is bottom-left.</summary>
        public static Texture2D Crop(Texture2D source, RectInt rect)
        {
            var pixels = source.GetPixels(rect.x, rect.y, rect.width, rect.height);
            var result = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false)
            {
                name = "Feedback Screenshot",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            result.SetPixels(pixels);
            result.Apply(false);
            return result;
        }

        public static Texture2D Clone(Texture2D source)
        {
            var result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            {
                name = source.name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            result.SetPixels32(source.GetPixels32());
            result.Apply(false);
            return result;
        }

        /// <summary>
        /// Scales the texture down so its longest side is at most <paramref name="maxSize"/>.
        /// Returns the same texture if no scaling is needed; otherwise destroys the source and returns a new one.
        /// </summary>
        public static Texture2D FitWithin(Texture2D source, int maxSize)
        {
            int longest = Mathf.Max(source.width, source.height);
            if (maxSize <= 0 || longest <= maxSize) return source;

            float scale = (float)maxSize / longest;
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            source.filterMode = FilterMode.Bilinear;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            Graphics.Blit(source, rt);

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = source.name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            Object.Destroy(source);
            return result;
        }
    }
}
