using UnityEngine;

namespace BizarreChess.Presentation.UnitRenderer
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
        /// Y starts at 0 and goes up, X/Z are centered.
        /// </summary>
        public Vector3 VoxelToWorld(int x, int y, int z)
        {
            float halfSize = Size * 0.5f;
            return new Vector3(
                (x + 0.5f) * VoxelSize - halfSize,  // X centered
                (y + 0.5f) * VoxelSize,              // Y from 0 upward
                (z + 0.5f) * VoxelSize - halfSize   // Z centered
            );
        }

        /// <summary>
        /// Convert voxel coordinates to world position (corner of voxel).
        /// Y starts at 0 and goes up, X/Z are centered.
        /// </summary>
        public Vector3 VoxelCornerToWorld(int x, int y, int z)
        {
            float halfSize = Size * 0.5f;
            return new Vector3(
                x * VoxelSize - halfSize,  // X centered
                y * VoxelSize,              // Y from 0 upward
                z * VoxelSize - halfSize   // Z centered
            );
        }

        /// <summary>
        /// Get radial frequency (number of spokes) from pixel color.
        /// Returns: positive = spokes at 0°, negative = spokes at 180°
        /// </summary>
        private static int GetRadialFrequency(Color pixel)
        {
            // Convert to hex color code (0x00, 0x80, or 0xFF per channel)
            // Thresholds: <0.25 = 0x00, 0.25-0.75 = 0x80, >0.75 = 0xFF
            int r = pixel.r > 0.75f ? 0xFF : (pixel.r > 0.25f ? 0x80 : 0x00);
            int g = pixel.g > 0.75f ? 0xFF : (pixel.g > 0.25f ? 0x80 : 0x00);
            int b = pixel.b > 0.75f ? 0xFF : (pixel.b > 0.25f ? 0x80 : 0x00);
            int hex = (r << 16) | (g << 8) | b;
            
            return hex switch
            {
                0xFFFFFF => 0,   // White = cylinder
                0x808080 => -1,  // Gray = 1 spoke at 180° (back)
                0x000000 => 1,   // Black = 1 spoke at 0° (front)
                0xFFFF00 => 2,   // Yellow = 2 spokes
                0x00FFFF => 3,   // Cyan = 3 spokes
                0xFF0000 => 4,   // Red = 4 spokes
                0xFF00FF => 5,   // Magenta = 5 spokes
                0x0000FF => 6,   // Blue = 6 spokes
                0x00FF00 => 8,   // Green = 8 spokes
                _ => 0           // Other = cylinder
            };
        }

        /// <summary>
        /// Check if an angle falls within one of N evenly-spaced solid sectors.
        /// Uses 16 fixed sectors; N determines how many are "on".
        /// Negative spokes = same count but offset by 180° (8 sectors).
        /// Special case: 1 or -1 spokes = 3 contiguous sectors (wider blade).
        /// </summary>
        private static bool IsInSolidSector(float angle, int spokes)
        {
            if (spokes == 0) return true;  // Cylinder: all solid
            
            // Handle 180° offset for negative spokes
            int offset = 0;
            if (spokes < 0)
            {
                spokes = -spokes;
                offset = 8;  // 180° = 8 sectors of 16
            }
            
            // Normalize angle to [0, 2π)
            const float TWO_PI = 2f * Mathf.PI;
            float normalizedAngle = angle;
            while (normalizedAngle < 0) normalizedAngle += TWO_PI;
            while (normalizedAngle >= TWO_PI) normalizedAngle -= TWO_PI;
            
            // 16 sectors, each 22.5° (π/8 radians)
            const int TOTAL_SECTORS = 16;
            const float SECTOR_SIZE = TWO_PI / TOTAL_SECTORS;  // π/8
            
            // Which sector is this angle in? (0-15), with offset applied
            int sector = Mathf.Clamp(Mathf.FloorToInt(normalizedAngle / SECTOR_SIZE), 0, TOTAL_SECTORS - 1);
            sector = (sector + offset) % TOTAL_SECTORS;
            
            // Special case: 1 spoke = 3 contiguous sectors (sectors 15, 0, 1)
            if (spokes == 1)
            {
                return sector <= 1 || sector == 15;  // 3 sectors centered on 0
            }
            
            // For N spokes, sectors are solid at indices: 0, 16/N, 32/N, ...
            // Sector is solid if: (sector * N) % 16 < N
            // This distributes N solid sectors evenly among 16
            return (sector * spokes) % TOTAL_SECTORS < spokes;
        }

        private void Voxelize(Texture2D profile, float alphaThreshold)
        {
            int texWidth = profile.width;
            int texHeight = profile.height;
            float halfRes = Resolution * 0.5f;
            
            // Respect texture aspect ratio: if texture is taller than wide,
            // the cylinder should be taller than wide
            float texAspect = (float)texHeight / texWidth;  // e.g. 2.0 for 32x64
            
            // maxRadius determines how wide the cylinder is relative to height
            // For aspect > 1 (taller than wide), reduce maxRadius so cylinder is narrower
            float maxRadius = halfRes / Mathf.Max(1f, texAspect);

            for (int y = 0; y < Resolution; y++)
            {
                // Map Y voxel to texture Y coordinate
                float texY = (y / (float)(Resolution - 1)) * (texHeight - 1);
                int texYInt = Mathf.Clamp(Mathf.RoundToInt(texY), 0, texHeight - 1);

                for (int x = 0; x < Resolution; x++)
                {
                    for (int z = 0; z < Resolution; z++)
                    {
                        // Calculate distance and angle from center axis (Y axis)
                        float dx = x - halfRes + 0.5f;
                        float dz = z - halfRes + 0.5f;
                        float radius = Mathf.Sqrt(dx * dx + dz * dz);
                        // Angle from +Z axis (front), so 0° = front, 180° = back
                        float angle = Mathf.Atan2(dx, dz);

                        // Map radius to texture X coordinate
                        // radius 0 = center (texX = 0), radius maxRadius = edge (texX = width-1)
                        float normalizedRadius = radius / maxRadius;
                        
                        // If radius exceeds texture bounds, voxel is empty
                        if (normalizedRadius > 1.0f)
                        {
                            _density[x, y, z] = 0.0f;
                            _data[x, y, z] = false;
                            continue;
                        }
                        
                        int texXInt = Mathf.RoundToInt(normalizedRadius * (texWidth - 1));

                        // Sample the profile texture at this radius/height
                        Color pixel = profile.GetPixel(texXInt, texYInt);
                        
                        // Check if pixel has enough alpha to be solid
                        if (pixel.a < alphaThreshold)
                        {
                            _density[x, y, z] = 0.0f;
                            _data[x, y, z] = false;
                            continue;
                        }
                        
                        // Get radial frequency (number of spokes) from color
                        int spokes = GetRadialFrequency(pixel);
                        
                        // Check if this angle falls in a solid sector
                        bool isSolid = IsInSolidSector(angle, spokes);
                        
                        // Density: 1.0 for solid voxels, 0.0 for empty
                        _density[x, y, z] = isSolid ? 1.0f : 0.0f;
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

