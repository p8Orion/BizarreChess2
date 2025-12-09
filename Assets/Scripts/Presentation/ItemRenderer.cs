using UnityEngine;
using BizarreChess.Core.Items;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Renders an item on the board as a small primitive shape in the corner of a tile.
    /// </summary>
    public class ItemRenderer : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _scale = 0.15f;
        [SerializeField] private float _cornerOffset = 0.35f;
        [SerializeField] private float _heightOffset = 0.1f;
        
        [Header("Animation")]
        [SerializeField] private float _rotationSpeed = 45f;
        [SerializeField] private float _bobSpeed = 2f;
        [SerializeField] private float _bobAmount = 0.05f;
        
        [Header("Highlight")]
        [SerializeField] private float _highlightScale = 1.3f;
        [SerializeField] private float _highlightPulseSpeed = 3f;

        public string ItemId { get; private set; }
        public System.Action OnClicked;

        private Item _item;
        private MeshRenderer _meshRenderer;
        private Material _material;
        private Vector3 _basePosition;
        private float _bobPhase;
        private bool _isHighlighted;
        private float _currentScale;
        private float _targetScale;

        /// <summary>
        /// Initialize the item renderer with an item and position.
        /// </summary>
        public void Initialize(Item item, Vector3 tilePosition)
        {
            _item = item;
            ItemId = item.Id;

            // Create primitive mesh
            CreateMesh(item.Shape);

            // Position in corner of tile
            _basePosition = tilePosition + new Vector3(_cornerOffset, _heightOffset, _cornerOffset);
            transform.position = _basePosition;
            
            // Set scale
            _currentScale = _scale;
            _targetScale = _scale;
            transform.localScale = Vector3.one * _scale;

            // Set color
            if (_meshRenderer != null)
            {
                _material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _material.color = item.ItemColor;
                _material.SetFloat("_Smoothness", 0.8f);
                _meshRenderer.material = _material;
            }

            // Random bob phase for variety
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);

            // Add collider for click detection (larger for easier clicking)
            var collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = 2f; // Larger radius relative to scale for easier clicking
        }

        private void CreateMesh(ItemShape shape)
        {
            GameObject primitive;
            
            switch (shape)
            {
                case ItemShape.Cube:
                    primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    break;
                case ItemShape.Capsule:
                    primitive = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    break;
                case ItemShape.Cylinder:
                    primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    break;
                case ItemShape.Sphere:
                default:
                    primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    break;
            }

            // Parent the primitive mesh to this object
            var meshFilter = primitive.GetComponent<MeshFilter>();
            var meshRenderer = primitive.GetComponent<MeshRenderer>();

            // Copy components
            var myMeshFilter = gameObject.AddComponent<MeshFilter>();
            myMeshFilter.mesh = meshFilter.mesh;
            
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.material = meshRenderer.material;

            // Destroy the temporary primitive
            Destroy(primitive);
        }

        private void Update()
        {
            // Rotation animation
            transform.Rotate(Vector3.up, _rotationSpeed * Time.deltaTime);

            // Bob animation
            _bobPhase += _bobSpeed * Time.deltaTime;
            float bobOffset = Mathf.Sin(_bobPhase) * _bobAmount;
            transform.position = _basePosition + Vector3.up * bobOffset;

            // Scale animation for highlight
            if (_isHighlighted)
            {
                float pulse = 1f + Mathf.Sin(Time.time * _highlightPulseSpeed) * 0.1f;
                _targetScale = _scale * _highlightScale * pulse;
            }
            else
            {
                _targetScale = _scale;
            }

            _currentScale = Mathf.Lerp(_currentScale, _targetScale, Time.deltaTime * 10f);
            transform.localScale = Vector3.one * _currentScale;
        }

        /// <summary>
        /// Set whether this item is highlighted (actionable).
        /// </summary>
        public void SetHighlighted(bool highlighted)
        {
            _isHighlighted = highlighted;

            if (_material != null)
            {
                if (highlighted)
                {
                    _material.EnableKeyword("_EMISSION");
                    _material.SetColor("_EmissionColor", _item.ItemColor * 0.5f);
                }
                else
                {
                    _material.DisableKeyword("_EMISSION");
                }
            }
        }

        /// <summary>
        /// Play pickup animation and destroy.
        /// </summary>
        public void PlayPickupAnimation()
        {
            StartCoroutine(PickupAnimationCoroutine());
        }

        private System.Collections.IEnumerator PickupAnimationCoroutine()
        {
            float duration = 0.3f;
            float elapsed = 0f;
            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + Vector3.up * 0.5f;
            Vector3 startScale = transform.localScale;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float easeOut = 1f - (1f - t) * (1f - t);
                
                transform.position = Vector3.Lerp(startPos, endPos, easeOut);
                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                
                if (_material != null)
                {
                    Color c = _material.color;
                    c.a = 1f - t;
                    _material.color = c;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Play spawn animation (for items dropped on death).
        /// </summary>
        public void PlaySpawnAnimation()
        {
            StartCoroutine(SpawnAnimationCoroutine());
        }

        private System.Collections.IEnumerator SpawnAnimationCoroutine()
        {
            float duration = 0.4f;
            float elapsed = 0f;
            Vector3 startPos = _basePosition + Vector3.up * 1f;
            Vector3 endScale = Vector3.one * _scale;
            
            transform.localScale = Vector3.zero;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                // Bounce easing
                float bounce = 1f - Mathf.Pow(1f - t, 2f) * Mathf.Cos(t * Mathf.PI * 2f) * (1f - t);
                
                transform.position = Vector3.Lerp(startPos, _basePosition, t);
                transform.localScale = Vector3.Lerp(Vector3.zero, endScale, bounce);

                elapsed += Time.deltaTime;
                yield return null;
            }

            transform.position = _basePosition;
            transform.localScale = endScale;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}

