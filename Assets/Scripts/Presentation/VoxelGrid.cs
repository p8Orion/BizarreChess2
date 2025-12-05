using UnityEngine;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// 3D voxel grid for revolution mesh generation.
    /// Voxelizes a 2D profile PNG by projecting it radially around the Y axis.
    /// </summary>
    public class VoxelGrid
    {
        public readonly int Resolution;
        public readonly float Size;
        public readonly float VoxelSize;
        
        private readonly bool[,,] _data;
        private readonly float[,,] _density; // For smooth marching cubes interpolation

        /// <summary>
        /// Create a voxel grid from a profile texture.
        /// </summary>
        /// <param name="profile">Profile texture (Y=height, X=radius from center)</param>
        /// <param name="resolution">Grid resolution (e.g., 32 for 32³)</param>
        /// <param name="size">World size of the grid</param>
        /// <param name="alphaThreshold">Minimum alpha to consider solid</param>
        public VoxelGrid(Texture2D profile, int resolution, float size, float alphaThreshold = 0.1f)
        {
            Resolution = resolution;
            Size = size;
            VoxelSize = size / resolution;
            
            _data = new bool[resolution, resolution, resolution];
            _density = new float[resolution, resolution, resolution];

            if (profile != null)
            {
                Texture2D readable = GetReadableTexture(profile);
                Voxelize(readable, alphaThreshold);
                if (readable != profile)
                {
                    Object.Destroy(readable);
                }
            }
        }

        /// <summary>
        /// Check if a voxel is solid.
        /// </summary>
        public bool IsSolid(int x, int y, int z)
        {
            if (x < 0 || x >= Resolution || y < 0 || y >= Resolution || z < 0 || z >= Resolution)
                return false;
            return _data[x, y, z];
        }

        /// <summary>
        /// Get density value at voxel (for smooth interpolation).
        /// Returns 1.0 for solid, 0.0 for empty, with gradient at boundaries.
        /// </summary>
        public float GetDensity(int x, int y, int z)
        {
            if (x < 0 || x >= Resolution || y < 0 || y >= Resolution || z < 0 || z >= Resolution)
                return 0f;
            return _density[x, y, z];
        }

        /// <summary>
        /// Convert voxel coordinates to world position (center of voxel).
        /// </summary>
        public Vector3 VoxelToWorld(int x, int y, int z)
        {
            float halfSize = Size * 0.5f;
            return new Vector3(
                (x + 0.5f) * VoxelSize - halfSize,
                (y + 0.5f) * VoxelSize - halfSize,
                (z + 0.5f) * VoxelSize - halfSize
            );
        }

        /// <summary>
        /// Convert voxel coordinates to world position (corner of voxel).
        /// </summary>
        public Vector3 VoxelCornerToWorld(int x, int y, int z)
        {
            float halfSize = Size * 0.5f;
            return new Vector3(
                x * VoxelSize - halfSize,
                y * VoxelSize - halfSize,
                z * VoxelSize - halfSize
            );
        }

        private void Voxelize(Texture2D profile, float alphaThreshold)
        {
            int texWidth = profile.width;
            int texHeight = profile.height;
            float halfRes = Resolution * 0.5f;
            float maxRadius = halfRes; // Maximum radius in voxel units

            for (int y = 0; y < Resolution; y++)
            {
                // Map Y voxel to texture Y coordinate
                float texY = (y / (float)(Resolution - 1)) * (texHeight - 1);
                int texYInt = Mathf.Clamp(Mathf.RoundToInt(texY), 0, texHeight - 1);

                for (int x = 0; x < Resolution; x++)
                {
                    for (int z = 0; z < Resolution; z++)
                    {
                        // Calculate distance from center axis (Y axis)
                        float dx = x - halfRes + 0.5f;
                        float dz = z - halfRes + 0.5f;
                        float radius = Mathf.Sqrt(dx * dx + dz * dz);

                        // Map radius to texture X coordinate
                        // radius 0 = center (texX = 0), radius maxRadius = edge (texX = width-1)
                        float normalizedRadius = radius / maxRadius;
                        float texX = normalizedRadius * (texWidth - 1);
                        int texXInt = Mathf.Clamp(Mathf.RoundToInt(texX), 0, texWidth - 1);

                        // Sample the profile texture at this radius/height
                        Color pixel = profile.GetPixel(texXInt, texYInt);
                        bool isSolid = pixel.a >= alphaThreshold;
                        
                        // Use alpha directly as density for smooth marching cubes interpolation
                        _density[x, y, z] = pixel.a;
                        _data[x, y, z] = isSolid;
                    }
                }
            }
        }

        private static Texture2D GetReadableTexture(Texture2D source)
        {
            try
            {
                source.GetPixel(0, 0);
                return source;
            }
            catch (UnityException) { }

            RenderTexture rt = RenderTexture.GetTemporary(
                source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return readable;
        }
    }
}

