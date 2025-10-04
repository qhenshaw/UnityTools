using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ArtPipeline
{
    [DisallowMultipleComponent, HideMonoScript]
    public class VertexPainter : MonoBehaviour
    {
        [field: SerializeField, PropertyOrder(0)] public bool EnablePainting { get; private set; } = true;
        [field: BoxGroup("Settings")]
        [field: ShowIf("EnablePainting")]
        [field: SerializeField, HideLabel, PropertyOrder(2)] public PaintSettings Settings { get; private set; }

        [BoxGroup("Debug"), ShowIf("EnablePainting"), PropertyOrder(3), SerializeField] private Mesh _mesh;
        [BoxGroup("Debug"), ShowIf("EnablePainting"), PropertyOrder(4), SerializeField] private Mesh _sourceMesh;

        private MeshFilter _meshFilter;
        public MeshFilter MeshFilter
        {
            get
            {
                if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
                return _meshFilter;
            }
        }

        private Renderer _renderer;
        public Renderer Renderer
        {
            get
            {
                if (_renderer == null) _renderer = GetComponent<Renderer>();
                return _renderer;
            }
        }

        public Mesh Mesh
        {
            get
            {
                if (_sourceMesh == null) _sourceMesh = MeshFilter.sharedMesh;

                if (_mesh == null || !_mesh.name.Contains(gameObject.GetInstanceID().ToString()))
                {
                    _mesh = Instantiate(MeshFilter.sharedMesh);
                    string name = _sourceMesh.name + "-" + gameObject.GetInstanceID().ToString();
                    _mesh.name = name;
                }
                MeshFilter.mesh = _mesh;
                return _mesh;
            }
            set
            {
                _mesh = value;
                MeshFilter.mesh = _mesh;
            }
        }

        [Button, PropertyOrder(1), ShowIf("EnablePainting")]
        public void ResetColors()
        {
            List<Color> colors = new List<Color>();
            Mesh.GetColors(colors);
            if (colors.Count == 0)
            {
                for (int i = 0; i < Mesh.vertexCount; i++)
                {
                    colors.Add(Color.clear);
                }
            }
            for (int i = 0; i < colors.Count; i++)
            {
                colors[i] = new Color(0f, 0f, 0f, 0f);
            }
            Mesh.SetColors(colors);
        }
    }

    [System.Serializable]
    public class PaintSettings
    {
        [field: SerializeField]
        [field: InlineButton("SetA", "A"), InlineButton("SetB", "B"), InlineButton("SetG", "G"), InlineButton("SetR", "R"), InlineButton("SetC", "Base")]
        public Color Color { get; private set; } = new Color(1f, 0f, 0f, 0f);
        [field: SerializeField] public float Radius { get; private set; } = 2f;
        [field: SerializeField, Range(0f, 1f)] public float Strength { get; private set; } = 1f;
        [field: SerializeField, InlineButton("ResetFalloff", "Reset")]
        public AnimationCurve Falloff { get; private set; }
            = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
        [field: SerializeField] public float DebugSize { get; private set; } = 0.1f;

        private void SetC()
        {
            Color = new Color(0f, 0f, 0f, 0f);
        }

        private void SetR()
        {
            Color = new Color(1f, 0f, 0f, 0f);
        }

        private void SetG()
        {
            Color = new Color(0f, 1f, 0f, 0f);
        }

        private void SetB()
        {
            Color = new Color(0f, 0f, 1f, 0f);
        }

        private void SetA()
        {
            Color = new Color(0f, 0f, 0f, 1f);
        }

        private void ResetFalloff()
        {
            Falloff = new AnimationCurve(new Keyframe[] { new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -3f, 3f) });
        }
    }
}