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
    }
}