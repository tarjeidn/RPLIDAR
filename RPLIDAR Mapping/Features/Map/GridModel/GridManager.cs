using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  public class GridManager
  {
    public Map _map;
    public int tileSize;
    private readonly float _gridSizeM;

    public int _gridSizePixels;
    private int _framUpdates;
    public GridStats GridStats { get; set; }
    public List<MapPoint> _globalTrustedMapPoints {  get; set; }
    public Dictionary<(int, int), Grid> Grids { get; set; }
    public int TotalDrawnTiles => Grids.Values.Sum(grid => grid._drawnTiles.Count);
    public float GridScaleFactor {get; set;}

    public GridManager(Map map)
    {
      _framUpdates = 0;
      _map = map;
      _globalTrustedMapPoints = new List<MapPoint>();
      Grids = new Dictionary<(int, int), Grid>();
      GridStats = new GridStats(this);
      InitializeGrids();
      Grid template = Grids[(0, 0)];
      tileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      _gridSizeM = MapScaleManager.Instance.GridAreaMeters;
      _gridSizePixels = MapScaleManager.Instance.ScaledGridSizePixels;

      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;


    }
    private void InitializeGrids()
    {
      for (int dx = -1; dx <= 1; dx++)
      {
        for (int dy = -1; dy <= 1; dy++)
        {
          var key = (dx, dy);
          Grids[key] = new Grid(this, dx, dy);
        }
      }

    }
    //public void UpdateGridScale(float newScaleFactor)
    //{
    //  GridScaleFactor = newScaleFactor;

    //  // 🔥 Inform each grid about the new scale
    //  foreach (var grid in Grids.Values)
    //  {
    //    grid.UpdateGridScale(GridScaleFactor);
    //  }
    //}


    public void Update()
    {

      GridStats.Update();
      foreach (Grid grid in Grids.Values)
      {
        grid.Update();

      }

    }

    private Grid GetOrCreateGrid(int gridX, int gridY)
    {
      var key = (gridX, gridY);
      if (!Grids.ContainsKey(key))
      {
        Grids[key] = new Grid(this, gridX, gridY);
      }

      return Grids[key];
    }
    //public void MapPointToGrid(List<MapPoint> points)
    //{
    //  foreach (MapPoint point in points)
    //  {
    //    // ✅ Ensure we're using the correct grid size after scaling
    //    float gridSizeScaled = _gridSizePixels / GridScaleFactor;
    //    int gridSizeInt = (int)gridSizeScaled; // Convert to int for indexing

    //    // ✅ Compute correct grid position using consistent scaling
    //    int gridX = (int)Math.Floor(point.GlobalX / gridSizeScaled);
    //    int gridY = (int)Math.Floor(point.GlobalY / gridSizeScaled);

    //    // ✅ Ensure local tile position is correctly mapped (Fix for negative grids)
    //    int localX = Utility.Modulo((int)(point.GlobalX - gridX * gridSizeScaled), gridSizeInt) / tileSize;
    //    int localY = Utility.Modulo((int)(point.GlobalY - gridY * gridSizeScaled), gridSizeInt) / tileSize;

    //    // ✅ Prevent out-of-bounds errors
    //    if (localX < 0 || localX >= gridSizeInt / tileSize || localY < 0 || localY >= gridSizeInt / tileSize)
    //    {
    //      Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
    //      continue;
    //    }

    //    // ✅ Fetch or create the grid and add the point
    //    GetOrCreateGrid(gridX, gridY).AddPoint(localX, localY, point);
    //  }
    //}

    //public void MapPointToGrid(List<MapPoint> points)
    //{
    //  foreach (MapPoint point in points)
    //  {
    //    // ✅ Use unscaled grid size for correct grid placement
    //    float gridSize = _gridSizePixels;

    //    // 🔥 Adjust for scaling when determining grid indices
    //    int gridX = (int)Math.Floor((point.GlobalX * GridScaleFactor) / gridSize);
    //    int gridY = (int)Math.Floor((point.GlobalY * GridScaleFactor) / gridSize);

    //    // ✅ Compute local tile position correctly within the grid
    //    int localX = Utility.Modulo((int)((point.GlobalX * GridScaleFactor) - gridX * gridSize), (int)gridSize) / tileSize;
    //    int localY = Utility.Modulo((int)((point.GlobalY * GridScaleFactor) - gridY * gridSize), (int)gridSize) / tileSize;

    //    // ✅ Prevent out-of-bounds errors
    //    if (localX < 0 || localX >= (int)gridSize / tileSize || localY < 0 || localY >= (int)gridSize / tileSize)
    //    {
    //      Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
    //      continue;
    //    }

    //    // ✅ Fetch or create the grid and add the point
    //    GetOrCreateGrid(gridX, gridY).AddPoint(localX, localY, point);
    //  }
    //}
    public void MapPointToGrid(List<MapPoint> points)
    {
      foreach (MapPoint point in points)
      {
        // ✅ Use dynamically scaled grid size
        float scaledGridSize = MapScaleManager.Instance.ScaledGridSizePixels;
        float scaledTileSize = MapScaleManager.Instance.ScaledTileSizePixels;

        // ✅ Compute correct grid index based on global position
        int gridX = (int)Math.Floor(point.GlobalX / scaledGridSize);
        int gridY = (int)Math.Floor(point.GlobalY / scaledGridSize);

        // ✅ Compute correct local tile position within the grid
        int localX = Utility.Modulo((int)(point.GlobalX - gridX * scaledGridSize), (int)scaledGridSize) / (int)scaledTileSize;
        int localY = Utility.Modulo((int)(point.GlobalY - gridY * scaledGridSize), (int)scaledGridSize) / (int)scaledTileSize;

        // ✅ Prevent out-of-bounds errors
        if (localX < 0 || localX >= (int)scaledGridSize / (int)scaledTileSize ||
            localY < 0 || localY >= (int)scaledGridSize / (int)scaledTileSize)
        {
          Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
          continue;
        }

        // ✅ Fetch or create the grid and add the point
        GetOrCreateGrid(gridX, gridY).AddPoint(localX, localY, point);
      }
    }













    public IEnumerable<Grid> GetAllGrids()
    {
      return Grids.Values;
    }
    public List<Tile> GetAllDrawnTiles()
    {
      List<Tile> tiles = new List<Tile>();
      foreach(Grid grid in Grids.Values)
      {
        tiles.AddRange(grid.GetDrawnTiles());
      }
      return tiles;
    }


  }
}
