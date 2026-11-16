using UnityEngine;
using UnityEngine.Serialization;

namespace UnityTools.Art
{
    /// <summary>
    /// Holds references to the original mesh and its painted copy.
    /// The painted copy is a mesh asset saved in the project (see UnityTools.Art.Editor.VertexPaintStorage),
    /// so it can be referenced from prefabs and scenes without embedding mesh data.
    /// Needs a MeshFilter and MeshRenderer; paint it with the Vertex Painter tool in the Scene view.
    /// </summary>
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

        /// <summary>The original, unpainted mesh. Set on the first stroke and used by Rebuild from Source and Remove Painter.</summary>
        public Mesh SourceMesh
        {
            get => _sourceMesh;
            set => _sourceMesh = value;
        }

        /// <summary>The painted copy of the source mesh that the MeshFilter shows. Null until the first stroke.</summary>
        public Mesh PaintedMesh
        {
            get => _paintedMesh;
            set => _paintedMesh = value;
        }
    }
}
