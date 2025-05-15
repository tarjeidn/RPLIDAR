using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using SharpDX.MediaFoundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.GridModel
{
  /// <summary>
  /// Manages a collection of spatial grids used to store and update tiles based on LiDAR input.
  /// </summary>
  public class GridManager
  {
    public Map _map;
    public int tileSize;
    private readonly float _gridSizeM;
    public int _gridSizePixels;
    private int _framUpdates;
    public GridStats GridStats { get; set; }
    public List<MapPoint> _globalTrustedMapPoints { get; set; }
    public Dictionary<(int, int), Grid> Grids { get; set; }
    private TileTrustRegulator TTR = AlgorithmProvider.TileTrustRegulator;
    public int TotalDrawnTiles => Grids.Values.Sum(grid => grid._drawnTiles.Count);
    public float GridScaleFactor { get; set; }

    /// <summary>
    /// Initializes the grid manager and creates the initial 3x3 grid layout.
    /// </summary>
    /// <param name="map">The parent map that owns this manager.</param>
    public GridManager(Map map)
    {
      _framUpdates = 0;
      _map = map;
      _globalTrustedMapPoints = new List<MapPoint>();
      Grids = new Dictionary<(int, int), Grid>();
      GridStats = new GridStats(this);
      InitializeGrids();
      Grid template = Grids[(0, 0)];
      tileSize = 10;
      _gridSizeM = MapScaleManager.Instance.GridAreaMeters;
      _gridSizePixels = 1000;
      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;
    }

    /// <summary>
    /// Creates a 3x3 set of initial grids centered around (0,0).
    /// </summary>
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

    /// <summary>
    /// Updates all grids and optionally triggers tile decay.
    /// </summary>
    public bool Update()
    {
      bool doDecay = StatisticsProvider.MapStats.AddPointUpdates % TTR.DecayFrequency == 0;
      if (doDecay) GridStats.HighTrustTilesLostLastCycle = 0;
      bool gridupdated = false;
      GridStats.Update();

      foreach (Grid grid in Grids.Values)
      {
        if (doDecay)
          grid.DecayTiles();
        if (grid.Update())
          gridupdated = true;
      }

      StatisticsProvider.GridStats.FinalizeBatch();
      return gridupdated;
    }

    /// <summary>
    /// Clears all ring tiles from every grid.
    /// </summary>
    public void ResetAllRingTiles()
    {
      foreach (Grid grid in Grids.Values)
        grid.RingTiles.Clear();
    }

    /// <summary>
    /// Gathers all ring tiles across all grids.
    /// </summary>
    public HashSet<Tile> GetAllRingTiles()
    {
      HashSet<Tile> allRingTiles = new();
      foreach (Grid grid in Grids.Values)
        allRingTiles.UnionWith(grid.RingTiles);
      return allRingTiles;
    }

    /// <summary>
    /// Maps a list of global MapPoints into their corresponding grids and tiles.
    /// </summary>
    public void MapPointToGrid(List<MapPoint> points)
    {
      foreach (MapPoint point in points)
      {
        float scaledGridSize = 1000;
        float scaledTileSize = 10;

        int gridX = (int)Math.Floor(point.GlobalX / scaledGridSize);
        int gridY = (int)Math.Floor(point.GlobalY / scaledGridSize);

        int maxTilesPerGrid = (int)(scaledGridSize / scaledTileSize);
        int localX = Math.Clamp((int)((point.GlobalX - (gridX * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);
        int localY = Math.Clamp((int)((point.GlobalY - (gridY * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);

        if (localX < 0 || localX >= maxTilesPerGrid || localY < 0 || localY >= maxTilesPerGrid)
        {
          Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
          continue;
        }

        if (Math.Abs(gridX) > 50 || Math.Abs(gridY) > 50)
        {
          Debug.WriteLine($"🚨 Excessive grid index: ({gridX}, {gridY}) from point ({point.GlobalX}, {point.GlobalY})");
        }

        if (point.IsInferredRingPoint)
          point.InferredByGridIndex = (localX, localY);

        GetOrCreateGrid(gridX, gridY).AddPoint(localX, localY, point);
      }
    }

    /// <summary>
    /// Retrieves the tile at the given global coordinates.
    /// </summary>
    public Tile GetTileAtGlobalCoordinates(float globalX, float globalY)
    {
      float scaledGridSize = 1000;
      float scaledTileSize = 10;

      int gridX = (int)Math.Floor(globalX / scaledGridSize);
      int gridY = (int)Math.Floor(globalY / scaledGridSize);

      int maxTilesPerGrid = (int)(scaledGridSize / scaledTileSize);
      int localX = Math.Clamp((int)((globalX - (gridX * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);
      int localY = Math.Clamp((int)((globalY - (gridY * scaledGridSize)) / scaledTileSize), 0, maxTilesPerGrid - 1);

      if (localX < 0 || localX >= maxTilesPerGrid || localY < 0 || localY >= maxTilesPerGrid)
      {
        Debug.WriteLine($"⚠ WARNING: Tile ({localX}, {localY}) out of bounds! (Grid {gridX}, {gridY})");
        return null;
      }

      Grid grid = GetOrCreateGrid(gridX, gridY, createIfMissing: false);
      return grid?.TryGetTileAt(localX, localY);
    }

    /// <summary>
    /// Returns all grids currently managed.
    /// </summary>
    public IEnumerable<Grid> GetAllGrids()
    {
      return Grids.Values;
    }

    /// <summary>
    /// Gathers all trusted tiles across all grids.
    /// </summary>
    public HashSet<Tile> GetAllTrustedTiles()
    {
      HashSet<Tile> allTrustedTiles = new();
      foreach (Grid grid in Grids.Values)
        allTrustedTiles.UnionWith(grid.GetTrustedTiles());
      return allTrustedTiles;
    }

    /// <summary>
    /// Retrieves an existing grid or creates it if it doesn't exist.
    /// </summary>
    private Grid GetOrCreateGrid(int gridX, int gridY, bool createIfMissing = true)
    {
      var key = (gridX, gridY);
      if (!Grids.ContainsKey(key) && createIfMissing)
        Grids[key] = new Grid(this, gridX, gridY);
      return Grids.GetValueOrDefault(key);
    }
  }
}
