using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Player;
using BizarreChess.Presentation;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Instantiates a traditional authored piece mesh (Blender FBX/OBJ/GLB/prefab)
    /// and fits it to the board like revolution pieces.
    /// </summary>
    public static class ImportedMeshGenerator
    {
        private const float MaxFootprint = 0.78f;
        private const float KingHeight = 1.9f;
        private static float? _pawnSetScale;

        public static GameObject CreateImportedObject(
            GameObject modelPrefab,
            Mesh mesh,
            int playerId,
            float height,
            string pieceName = null)
        {
            float targetHeight = GetTargetHeight(pieceName, height);

            if (modelPrefab != null)
                return CreateFromPrefab(modelPrefab, playerId, targetHeight, pieceName);

            if (mesh != null)
                return CreateFromMesh(mesh, playerId, targetHeight, pieceName);

            Debug.LogWarning("[ImportedMeshGenerator] No model or mesh provided");
            return Token2DMeshGenerator.CreateTokenObject(null, playerId);
        }

        /// <summary>
        /// Chess-like heights so a matching GLB set is not stretched to one size.
        /// Pawns stay ~half a king; revolution PieceHeight is left alone.
        /// </summary>
        public static float GetTargetHeight(string pieceName, float fallback)
        {
            if (string.IsNullOrEmpty(pieceName))
                return fallback > 0f ? fallback : KingHeight;

            return pieceName switch
            {
                "King" => KingHeight,
                "Queen" => KingHeight * 0.92f,
                "Bishop" => KingHeight * 0.76f,
                "Knight" => KingHeight * 0.68f,
                "Rook" => KingHeight * 0.62f,
                "Pawn" => KingHeight * 0.50f,
                "Lancer" => KingHeight * 0.50f,
                "Defender" => KingHeight * 0.52f,
                "Camel" => KingHeight * 0.68f,
                "Cannon" => KingHeight * 0.62f,
                "Crossbowman" => KingHeight * 0.72f,
                _ => fallback > 0f ? fallback : KingHeight * 0.7f
            };
        }

        private static GameObject CreateFromPrefab(GameObject modelPrefab, int playerId, float height, string pieceName)
        {
            var go = new GameObject("ImportedPiece");
            var instance = Object.Instantiate(modelPrefab, go.transform);
            instance.name = modelPrefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            FitToBoard(instance.transform, height, LimitsFootprint(pieceName), UsesPawnScale(pieceName));
            DisableChildColliders(go);
            AddBoundsCollider(go);
            InstantiateMaterials(go);
            DisableImportedAutoPlay(go);

            if (playerId != 0)
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            return go;
        }

        private static GameObject CreateFromMesh(Mesh mesh, int playerId, float height, string pieceName)
        {
            var go = new GameObject("ImportedPiece");
            var visual = new GameObject("Model");
            visual.transform.SetParent(go.transform, false);

            var meshFilter = visual.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            var meshRenderer = visual.AddComponent<MeshRenderer>();
            meshRenderer.material = CreatePieceMaterial(playerId);

            FitToBoard(visual.transform, height, LimitsFootprint(pieceName), UsesPawnScale(pieceName));
            AddBoundsCollider(go);

            if (playerId != 0)
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            return go;
        }

        /// <summary>
        /// After the piece is parented, placed, and scaled, drop the visual so the real mesh sits on the tile.
        /// </summary>
        public static void SnapVisualToGround(Transform wrapper, float groundY)
        {
            if (wrapper == null)
                return;

            Transform visual = wrapper.childCount > 0 ? wrapper.GetChild(0) : wrapper;
            if (!TryGetVisualWorldBounds(visual, out Bounds bounds))
                return;

            float dy = groundY - bounds.min.y;
            if (Mathf.Abs(dy) < 0.0001f)
                return;

            visual.position += new Vector3(0f, dy, 0f);
        }

        private static bool LimitsFootprint(string pieceName)
        {
            return !IsLancer(pieceName);
        }

        private static bool UsesPawnScale(string pieceName)
        {
            return IsLancer(pieceName);
        }

        private static bool IsLancer(string pieceName)
        {
            return !string.IsNullOrEmpty(pieceName)
                && pieceName.Equals("Lancer", System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetPawnSetScale(out float scale)
        {
            if (_pawnSetScale.HasValue)
            {
                scale = _pawnSetScale.Value;
                return true;
            }

            scale = 0f;
            var pawn = Resources.Load<GameObject>("Pieces/Models/Pawn")
                ?? Resources.Load<GameObject>("Pieces/Models/pawn");
            if (pawn == null)
                return false;

            var temp = Object.Instantiate(pawn);
            temp.hideFlags = HideFlags.HideAndDontSave;
            temp.transform.SetPositionAndRotation(new Vector3(0f, -1000f, 0f), Quaternion.identity);
            temp.transform.localScale = Vector3.one;

            bool ok = TryGetVisualWorldBounds(temp.transform, out Bounds bounds) && bounds.size.y > 0.0001f;
            if (ok)
            {
                scale = GetTargetHeight("Pawn", KingHeight * 0.50f) / bounds.size.y;
                _pawnSetScale = scale;
            }

            if (Application.isPlaying)
                Object.Destroy(temp);
            else
                Object.DestroyImmediate(temp);

            return ok;
        }

        private static void FitToBoard(Transform target, float height, bool limitFootprint, bool matchPawnScale)
        {
            if (!TryGetVisualWorldBounds(target, out Bounds bounds) || height <= 0f)
                return;

            float sizeY = bounds.size.y;
            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            if (sizeY < 0.0001f)
                return;

            float scale = height / sizeY;
            if (matchPawnScale && TryGetPawnSetScale(out float pawnScale))
            {
                target.localScale = Vector3.one;
                scale = pawnScale;
            }
            else if (limitFootprint && footprint > 0.0001f)
            {
                scale = Mathf.Min(scale, MaxFootprint / footprint);
            }

            target.localScale *= scale;

            if (!TryGetVisualWorldBounds(target, out bounds))
                return;

            Vector3 parentOrigin = target.parent != null ? target.parent.position : Vector3.zero;
            target.position += new Vector3(
                parentOrigin.x - bounds.center.x,
                parentOrigin.y - bounds.min.y,
                parentOrigin.z - bounds.center.z
            );
        }

        public static bool TryGetVisualWorldBounds(Transform root, out Bounds bounds)
        {
            var candidates = new List<Bounds>();

            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var filter = filters[i];
                if (filter == null || filter.sharedMesh == null)
                    continue;
                if (TryBuildWorldBounds(filter.sharedMesh.bounds, filter.transform, out Bounds meshBounds))
                    candidates.Add(meshBounds);
            }

            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                if (skin == null)
                    continue;
                if (TryBuildWorldBounds(skin.localBounds, skin.transform, out Bounds skinBounds))
                    candidates.Add(skinBounds);
                else
                    candidates.Add(skin.bounds);
            }

            if (candidates.Count == 0)
            {
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null || renderers[i] is ParticleSystemRenderer)
                        continue;
                    candidates.Add(renderers[i].bounds);
                }
            }

            bounds = new Bounds();
            if (candidates.Count == 0)
                return false;

            float maxExtent = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 size = candidates[i].size;
                maxExtent = Mathf.Max(maxExtent, size.x, size.y, size.z);
            }

            float minKeep = maxExtent * 0.08f;
            bool started = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 size = candidates[i].size;
                float extent = Mathf.Max(size.x, size.y, size.z);
                if (extent < minKeep)
                    continue;

                if (!started)
                {
                    bounds = candidates[i];
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(candidates[i]);
                }
            }

            if (!started)
            {
                bounds = candidates[0];
                for (int i = 1; i < candidates.Count; i++)
                    bounds.Encapsulate(candidates[i]);
                started = true;
            }

            return started && bounds.size.y > 0.0001f;
        }

        private static bool TryBuildWorldBounds(Bounds localBounds, Transform meshTransform, out Bounds worldBounds)
        {
            Vector3 c = localBounds.center;
            Vector3 e = localBounds.extents;
            if (e.sqrMagnitude < 1e-12f)
            {
                worldBounds = default;
                return false;
            }

            bool started = false;
            worldBounds = new Bounds();
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 world = meshTransform.TransformPoint(c + new Vector3(e.x * x, e.y * y, e.z * z));
                if (!started)
                {
                    worldBounds = new Bounds(world, Vector3.zero);
                    started = true;
                }
                else
                {
                    worldBounds.Encapsulate(world);
                }
            }

            return started;
        }

        private static void DisableChildColliders(GameObject root)
        {
            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private static void AddBoundsCollider(GameObject root)
        {
            if (!TryGetVisualWorldBounds(root.transform, out Bounds worldBounds))
                return;

            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(worldBounds.center);
            Vector3 lossy = root.transform.lossyScale;
            box.size = new Vector3(
                SafeDivide(worldBounds.size.x, lossy.x),
                SafeDivide(worldBounds.size.y, lossy.y),
                SafeDivide(worldBounds.size.z, lossy.z)
            );
        }

        private static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
        }

        private static void InstantiateMaterials(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null || renderers[i] is ParticleSystemRenderer)
                    continue;
                var mats = renderers[i].materials;
                for (int j = 0; j < mats.Length; j++)
                    RuntimeShaders.EnsureUrpCompatible(mats[j]);
                renderers[i].materials = mats;
            }
        }

        private static void DisableImportedAutoPlay(GameObject root)
        {
            var animations = root.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++)
            {
                if (animations[i] == null)
                    continue;
                animations[i].playAutomatically = false;
                animations[i].Stop();
            }

            var animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                if (animators[i] == null)
                    continue;
                animators[i].enabled = false;
            }
        }

        private static Material CreatePieceMaterial(int playerId)
        {
            var colorScheme = PlayerColors.Get(playerId);
            var mat = RuntimeShaders.Create(colorScheme.PrimaryColor, colorScheme.PieceTexture, colorScheme.Smoothness);
            if (colorScheme.PieceTexture != null)
            {
                var tiling = new Vector2(colorScheme.TextureTiling, colorScheme.TextureTiling);
                mat.mainTextureScale = tiling;
                if (mat.HasProperty("_MainTex"))
                    mat.SetTextureScale("_MainTex", tiling);
            }
            return mat;
        }
    }
}
