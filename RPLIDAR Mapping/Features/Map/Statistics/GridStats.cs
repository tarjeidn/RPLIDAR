using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Statistics
{
  public class GridStats
  {
    // Stored statistics
    public int TotalGrids { get; private set; }
    public int TotalHitTiles { get; private set; }
    public float TotalPointsHandled { get; private set; }

    // Tile intensity statistics
    public int HighestTileHitCount { get; private set; }
    public float AverageTileHitCount { get; private set; }
    public float HighAverageTileHitCount { get; private set; }
    public int HighTotalHitTiles { get; private set; }
    public int HighTotalHitCount { get; private set; }

    // 🔥 New statistics for trust regulation
    public int HighTrustTileHits { get; private set; }  // Tiles (trust ≥ 90) hit this update
    public int TotalHighTrustTiles { get; private set; } // Number of tiles (trust ≥ 90)
    public float HighTrustTilePercentage { get; private set; } // % of hit tiles that are high-trust

    // Tracking points & unique tiles per batch
    public int TotalPointsAddedLastBatch { get; private set; }
    public int UniqueTilesHitLastBatch { get; private set; }  //  Unique tiles hit in last cycle

    private HashSet<(int, int)> _uniqueTileTracker;  // Tracks unique tiles hit per batch


    private GridManager _gridManager;

    public GridStats(GridManager gridManager)
    {
      _gridManager = gridManager;
      _uniqueTileTracker = new HashSet<(int, int)>();
      Reset();
    }

    // Reset all stats
    public void Reset()
    {
      TotalGrids = 0;
      TotalHitTiles = 0;
      TotalPointsHandled = 0;
      HighestTileHitCount = 0;
      AverageTileHitCount = 0;
      HighAverageTileHitCount = 0;
      HighTotalHitTiles = 0;
      HighTotalHitCount = 0;

      // 🔥 Reset new trust-based stats
      HighTrustTileHits = 0;
      TotalHighTrustTiles = 0;
      HighTrustTilePercentage = 0;

      // 🔥 Reset batch tracking
      TotalPointsAddedLastBatch = 0;
      UniqueTilesHitLastBatch = 0;
      _uniqueTileTracker.Clear();
    }
    // 🔥 Called when a point is added
    public void RegisterPointAdded(int x, int y)
    {
      TotalPointsAddedLastBatch++;  //  Increment total points added
      _uniqueTileTracker.Add((x, y));  //  Track unique tile hit
    }

    // 🔥 Called at the end of a batch cycle (e.g., before decay)
    public void FinalizeBatch()
    {
      UniqueTilesHitLastBatch = _uniqueTileTracker.Count;  //  Get total unique tiles hit
      _uniqueTileTracker.Clear();  //  Reset for the next batch
    }
    // Called every frame to update statistics
    public void Update()
    {
      if (_gridManager == null) return;

      TotalGrids = _gridManager.Grids.Count;
      TotalPointsHandled += StatisticsProvider.MapStats.PointsPerSecond;

      if (_gridManager.Grids.Count == 0)
      {
        Reset();
        return;
      }

      //  Aggregate statistics in a single-pass loop
      int totalHitPoints = 0, highTotalHitCount = 0, highTotalHitTiles = 0;
      int highestHitCount = 0;
      int totalTiles = 0;
      int highTrustTileHits = 0;
      int totalHighTrustTiles = 0;

      foreach (var grid in _gridManager.Grids.Values)
      {
        foreach (var tile in grid._drawnTiles.Values)
        {
          int hits = tile.pointsHitAtThisTile;
          totalHitPoints += hits;
          totalTiles++;

          if (hits > highestHitCount) highestHitCount = hits;

          // 🔥 Check if tile is high-trust (≥ 90)
          if (tile.TrustedScore >= 90)
          {
            totalHighTrustTiles++;
            highTrustTileHits += hits;
          }
        }
      }

      //  Avoid division by zero
      if (totalTiles == 0)
      {
        Reset();
        return;
      }

      //  Compute average hit count **once**
      float avgTileHitCount = (float)totalHitPoints / totalTiles;

      //  Second pass only for high-intensity calculations
      foreach (var grid in _gridManager.Grids.Values)
      {
        foreach (var tile in grid._drawnTiles.Values)
        {
          if (tile.pointsHitAtThisTile > avgTileHitCount)
          {
            highTotalHitTiles++;
            highTotalHitCount += tile.pointsHitAtThisTile;
          }
        }
      }

      //  Store results in instance variables
      TotalHitTiles = totalTiles;
      HighestTileHitCount = highestHitCount;
      AverageTileHitCount = avgTileHitCount;
      HighTotalHitTiles = highTotalHitTiles;
      HighTotalHitCount = highTotalHitCount;
      HighAverageTileHitCount = highTotalHitTiles > 0 ? (float)highTotalHitCount / highTotalHitTiles : 0;

      // 🔥 Update new trust-based statistics
      HighTrustTileHits = highTrustTileHits;
      TotalHighTrustTiles = totalHighTrustTiles;
      HighTrustTilePercentage = totalTiles > 0 ? (float)totalHighTrustTiles / totalTiles : 0;
    }
  }



}
