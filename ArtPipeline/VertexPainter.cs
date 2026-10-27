using UnityEngine;
using UnityEngine.Serialization;

namespace ArtPipeline
{
    // Holds references to the original mesh and its painted copy.
    // The painted copy is a mesh asset saved in the project (see ArtPipeline.Editor.VertexPaintStorage),
    // so it can be referenced from prefabs and scenes without embedding mesh data.
    [DisallowMultipleComponent]
    public class VertexPainter : MonoBehaviour
    {
        [SerializeField] private Mesh _sourceMesh;
        [FormerlySerializedAs("_mesh")]
        [SerializeField] private Mesh _paintedMesh;

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

        public Mesh SourceMesh
        {
            get => _sourceMesh;
            set => _sourceMesh = value;
        }

        public Mesh PaintedMesh
        {
            get => _paintedMesh;
            set => _paintedMesh = value;
        }
    }
}
