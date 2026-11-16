using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityTools.PlaytestFeedback.Internal
{
    /// <summary>
    /// Simple brush that paints into a CPU-side copy of a texture, with undo and clear.
    /// Changes are uploaded to the GPU at most once per frame via <see cref="Flush"/>.
    /// </summary>
    internal sealed class AnnotationTool : IDisposable
    {
        const int MaxUndoSteps = 12;

        readonly Texture2D _texture;
        readonly Color32[] _original;
        readonly List<Color32[]> _undo = new List<Color32[]>();
        readonly int _width;
        readonly int _height;

        Color32[] _pixels;
        Color32 _color;
        float _radius;
        Vector2 _last;
        bool _stroking;
        bool _dirty;
        bool _ownsTexture = true;

        /// <param name="workingTexture">A readable RGBA32 texture the tool takes ownership of and paints into.</param>
        public AnnotationTool(Texture2D workingTexture)
        {
            _texture = workingTexture;
            _width = workingTexture.width;
            _height = workingTexture.height;
            _pixels = workingTexture.GetPixels32();
            _original = (Color32[])_pixels.Clone();
        }

        public Texture2D Texture => _texture;
        public bool CanUndo => _undo.Count > 0;

        public void BeginStroke(Vector2 position, float radius, Color32 color)
        {
            PushUndo();
            _radius = Mathf.Max(1f, radius);
            _color = color;
            _last = position;
            _stroking = true;
            Stamp(position);
        }

        public void StrokeTo(Vector2 position)
        {
            if (!_stroking) return;

            float distance = Vector2.Distance(_last, position);
            float spacing = Mathf.Max(1f, _radius * 0.35f);
            int steps = Mathf.CeilToInt(distance / spacing);
            for (int i = 1; i <= steps; i++)
            {
                Stamp(Vector2.Lerp(_last, position, i / (float)steps));
            }
            _last = position;
        }

        public void EndStroke() => _stroking = false;

        public void Undo()
        {
            if (_undo.Count == 0) return;
            _pixels = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _dirty = true;
        }

        public void Clear()
        {
            PushUndo();
            _pixels = (Color32[])_original.Clone();
            _dirty = true;
        }

        /// <summary>Uploads pending changes. Returns true if anything changed.</summary>
        public bool Flush()
        {
            if (!_dirty) return false;
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            _dirty = false;
            return true;
        }

        /// <summary>Returns the finished texture. The caller now owns it.</summary>
        public Texture2D TakeResult()
        {
            Flush();
            _ownsTexture = false;
            return _texture;
        }

        public void Dispose()
        {
            if (_ownsTexture && _texture != null) Object.Destroy(_texture);
            _ownsTexture = false;
        }

        void PushUndo()
        {
            _undo.Add((Color32[])_pixels.Clone());
            if (_undo.Count > MaxUndoSteps) _undo.RemoveAt(0);
        }

        void Stamp(Vector2 center)
        {
            int xMin = Mathf.Max(0, Mathf.FloorToInt(center.x - _radius - 1));
            int xMax = Mathf.Min(_width - 1, Mathf.CeilToInt(center.x + _radius + 1));
            int yMin = Mathf.Max(0, Mathf.FloorToInt(center.y - _radius - 1));
            int yMax = Mathf.Min(_height - 1, Mathf.CeilToInt(center.y + _radius + 1));

            for (int y = yMin; y <= yMax; y++)
            {
                float dy = y + 0.5f - center.y;
                int row = y * _width;
                for (int x = xMin; x <= xMax; x++)
                {
                    float dx = x + 0.5f - center.x;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float coverage = Mathf.Clamp01(_radius - distance + 0.5f);
                    if (coverage <= 0f) continue;

                    int index = row + x;
                    _pixels[index] = coverage >= 1f ? _color : Color32.Lerp(_pixels[index], _color, coverage);
                }
            }
            _dirty = true;
        }
    }
}
