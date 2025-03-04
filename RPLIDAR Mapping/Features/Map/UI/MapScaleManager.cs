using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.UI
{
  public class MapScaleManager
  {
    private static MapScaleManager _instance;
    public static MapScaleManager Instance => _instance ??= new MapScaleManager();
    public Map _map {  get; set; }

    // 🔥 Adjustable real-world area each grid represents (in meters)
    public float GridAreaMeters { get; private set; } = 1.0f;  // Default: Each grid represents 1x1m area

    // 🔥 Base grid and tile sizes in pixels
    public int BaseGridSizePixels { get; private set; } = 1000; // Default grid size in pixels
    private int _baseTileCount = 100;  // 🔥 Default: 100 tiles per grid (can be adjusted)

    public float ScaleFactor { get; private set; } = 1.0f; // Default scale

    // 🔥 Dynamically calculate the tile size based on grid area
    public int ScaledGridSizePixels => (int)(BaseGridSizePixels * ScaleFactor);
    public int ScaledTileSizePixels => (int)(ScaledGridSizePixels / _baseTileCount);

    private MapScaleManager() { }

    // 🔄 Change scale factor (Zoom-like behavior)
    public void SetScaleFactor(float newScale)
    {
      ScaleFactor = MathHelper.Clamp(newScale, 0.1f, 10f); // Prevent extreme values
      _map.ResetAllTiles();
    }

    // 🔄 Change the real-world area each grid represents
    public void SetGridAreaMeters(float newAreaMeters)
    {
      GridAreaMeters = MathHelper.Clamp(newAreaMeters, 0.1f, 10f); // Keep it reasonable
      UpdateTileSize();
    }

    private void UpdateTileSize()
    {
      // 🔥 Adjust tile size based on grid area (Ensures tiles remain proportional)
      _baseTileCount = (int)(GridAreaMeters * 100);  // 🔄 Example: 1m² = 100 tiles
    }
  }



}
