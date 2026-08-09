using UnityEngine;

namespace ArtPipeline
{
    [DisallowMultipleComponent]
    public class VertexPainter : MonoBehaviour
    {
        [SerializeField] private Mesh _mesh;
        [SerializeField] private Mesh _sourceMesh;

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

                EntityId id = gameObject.GetEntityId();
                string idString = id.ToString();
                if (_mesh == null || !_mesh.name.Contains(idString))
                {
                    _mesh = Instantiate(MeshFilter.sharedMesh);
                    string name = _sourceMesh.name + "-" + idString;
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
    }
}