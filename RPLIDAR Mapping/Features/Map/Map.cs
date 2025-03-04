using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using MonoGame.Framework.Utilities.Deflate;
using RPLIDAR_Mapping.Core;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Features.Map.Statistics;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;




namespace RPLIDAR_Mapping.Features.Map
{
  public class Map
  {
    private int MaxPoints = AppSettings.Default.MaxPoints; // Maximum points to store
    private readonly List<MapPoint> _points; // List of LiDAR points
    private ScanMatcher _matcher;
    //private MapDictionary _mapDictionary;
    private Distributor _Distributor { get; set; }
    public MapStats _MapStats { get; set; }
    public Device _device { get; set; }
    private List<DataPoint> _pointsBuffer;
    private FPSCounter _fpsCounter;
    private TileTrustRegulator _tileTrustRegulator;
    public bool _pointsAdded {  get; private set; }
    //public List<Rectangle> _mergedObjects {  get; private set; }
     public List<(Rectangle, float, bool)> _mergedObjects {  get; private set; }
    //public List<(Rectangle, float)> _mergedObjects { get; private set; }
    private int _mergeTilesFrameCounter;
    public GridManager _gridManager { get; set; }

    private float _gridScaleFactor = 1.0f;  // 🔥 Centralized here!

    public float GridScaleFactor { get; set; }



    public Map()
    {
      MapScaleManager.Instance._map = this;
      _tileTrustRegulator = AlgorithmProvider.TileTrustRegulator;
      _mergeTilesFrameCounter = 0;
      _fpsCounter = UtilityProvider.FPSCounter;
      _MapStats = new MapStats();
      _points = new List<MapPoint>();
      _Distributor = new Distributor(this);
      _pointsBuffer = new List<DataPoint>();
      //_mergedObjects = new List<Rectangle>();
      _mergedObjects = new List<(Rectangle, float, bool)>();
      _matcher = new ScanMatcher(_Distributor._GridManager._globalTrustedMapPoints);
      _gridManager = _Distributor._GridManager;
      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;

    }

    public void Update(List<DataPoint> dplist, float deltaTime)
    {
      GridScaleFactor = AppSettings.Default.GridScaleFactor;
      _Distributor.Update();

      if (TileRegulatorSettings.Default.RegulatorEnabled)
      {
        _tileTrustRegulator.Update(deltaTime);
      }

      if (dplist.Count > 0)
      {
        StatisticsProvider.MapStats.AddPointUpdates++;
        _fpsCounter.IncrementLiDARUpdate();
        AddPoints(dplist);

        //_mergeTilesFrameCounter = 0; // Reset counter since we received new points
      }
      else if (_mergeTilesFrameCounter >= AppSettings.Default.MergeTilesFrequency)
      {
        Stopwatch stopwatch = Stopwatch.StartNew();
        MergeTilesGlobal(AppSettings.Default.TilemergeThreshold);
        stopwatch.Stop();
        // Debug.WriteLine($"MergeTilesGlobal Execution Time: {stopwatch.ElapsedMilliseconds} ms");

        _mergeTilesFrameCounter = 0; // Reset counter after merging
      }
      else
      {
        _mergeTilesFrameCounter++; // Increment counter if no new points
      }

      _MapStats.AddToPointHistory(dplist.Count);
    }
     // Clears all added tiles
    public void ResetAllTiles()
    {
      foreach (var grid in _Distributor._GridManager.Grids.Values)
      {
        grid.ResetTiles();
      }
      _mergedObjects.Clear();
    }

    public Distributor GetDistributor()
    {
      return _Distributor;
    }
    public void AddPoints(List<DataPoint> dplist)
    {
      List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);

      foreach (DataPoint dp in dplist)
      {
        if (dp.Quality > AppSettings.Default.MinPointQuality && dp.Distance >= 1.0f)
        {
          float radians = MathHelper.ToRadians(dp.Angle);
          float distance = dp.Distance;


          // 🔥 Adjust relative positions based on scaling
          float relativeX = distance * (float)Math.Cos(radians);
          float relativeY = distance * (float)Math.Sin(radians);

          // ✅ Convert global coordinates, now using the scaled distance
          float globalX = (_device._devicePosition.X) + relativeX;
          float globalY = (_device._devicePosition.Y) + relativeY;

          // ✅ Store only scaled values in MapPoint
          MapPoint mapPoint = new MapPoint(relativeX, relativeY, dp.Angle, distance, radians, dp.Quality, globalX, globalY);
          addedPoints.Add(mapPoint);
        }
      }

      _Distributor.Distribute(addedPoints);
    }
    //public void AddPoints(List<DataPoint> dplist)
    //{
    //  List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);

    //  foreach (DataPoint dp in dplist)
    //  {
    //    if (dp.Quality > AppSettings.Default.MinPointQuality && dp.Distance >= 1.0f)
    //    {
    //      float radians = MathHelper.ToRadians(dp.Angle);
    //      float distanceCM = dp.Distance * 0.1f;

    //      // 🔥 Apply GridScaleFactor to distance scaling
    //      float scaledDistance = distanceCM * (1.0f / GridScaleFactor);

    //      // 🔥 Adjust relative positions based on scaling
    //      float relativeX = scaledDistance * (float)Math.Cos(radians);
    //      float relativeY = scaledDistance * (float)Math.Sin(radians);

    //      // ✅ Convert global coordinates, now using the scaled distance
    //      float globalX = (_device._devicePosition.X * 0.1f) + relativeX;
    //      float globalY = (_device._devicePosition.Y * 0.1f) + relativeY;

    //      // ✅ Store only scaled values in MapPoint
    //      MapPoint mapPoint = new MapPoint(relativeX, relativeY, dp.Angle, scaledDistance, radians, dp.Quality, globalX, globalY);
    //      addedPoints.Add(mapPoint);
    //    }
    //  }

    //  _Distributor.Distribute(addedPoints);
    //}
    //public void AddPoints(List<DataPoint> dplist)
    //{
    //  List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);

    //  foreach (DataPoint dp in dplist)
    //  {
    //    if (dp.Quality > AppSettings.Default.MinPointQuality && dp.Distance >= 1.0f)
    //    {
    //      float radians = MathHelper.ToRadians(dp.Angle);

    //      // ✅ Convert LiDAR distances from mm → cm
    //      float distanceCM = dp.Distance * 0.1f;

    //      // 🔥 Apply scaling factor (Zoom effect)
    //      float scaledDistance = distanceCM * (1.0f / GridScaleFactor);

    //      // ✅ Compute relative positions with the scale applied
    //      float relativeX = scaledDistance * (float)Math.Cos(radians);
    //      float relativeY = scaledDistance * (float)Math.Sin(radians);

    //      // ✅ Convert global coordinates, now using the scaled distance
    //      float globalX = (_device._devicePosition.X * 0.1f) + relativeX;
    //      float globalY = (_device._devicePosition.Y * 0.1f) + relativeY;

    //      // ✅ Store only scaled values in MapPoint
    //      MapPoint mapPoint = new MapPoint(relativeX, relativeY, dp.Angle, scaledDistance, radians, dp.Quality, globalX, globalY);
    //      addedPoints.Add(mapPoint);
    //    }
    //  }

    //  _Distributor.Distribute(addedPoints);
    //}


    private void MergeTilesGlobal(float mergeThreshold)
    {
      HashSet<Tile> visited = new();
      List<(Rectangle, float, bool)> mergedRegions = new();
      List<Tile> drawnTiles = _Distributor._GridManager.GetAllDrawnTiles();
      if (drawnTiles.Count == 0) return;

      int tileSize = (int)(drawnTiles[0]._tileSize); // ✅ Correct tile size

      // ✅ Spatial lookup for fast merging
      Dictionary<(int, int), Tile> tileMap = new();
      foreach (Tile tile in drawnTiles)
      {
        var key = ((int)(tile.GlobalCenter.X / tileSize),
                   (int)(tile.GlobalCenter.Y / tileSize));
        tileMap[key] = tile;
      }

      foreach (Tile tile in drawnTiles)
      {
        if (visited.Contains(tile)) continue;

        // ✅ Start a new merged region
        List<Tile> mergedTiles = new() { tile };
        float minX = tile.GlobalCenter.X;
        float minY = tile.GlobalCenter.Y;
        float maxX = minX;
        float maxY = minY;
        visited.Add(tile);

        Queue<Tile> queue = new();
        queue.Enqueue(tile);

        List<float> lidarAngles = new();
        int permanentTileCount = 0;

        if (tile._lastLIDARpoint != null)
          lidarAngles.Add(tile._lastLIDARpoint.Angle);

        if (tile.IsPermanent)
          permanentTileCount++;

        while (queue.Count > 0)
        {
          Tile currentTile = queue.Dequeue();

          for (int dx = -1; dx <= 1; dx++)
          {
            for (int dy = -1; dy <= 1; dy++)
            {
              var neighborKey = ((int)(currentTile.GlobalCenter.X / tileSize) + dx,
                                 (int)(currentTile.GlobalCenter.Y / tileSize) + dy);

              if (tileMap.TryGetValue(neighborKey, out Tile neighbor) && !visited.Contains(neighbor))
              {
                float distance = Vector2.Distance(currentTile.GlobalCenter, neighbor.GlobalCenter);

                if (distance <= (mergeThreshold * tileSize))
                {
                  visited.Add(neighbor);
                  queue.Enqueue(neighbor);
                  mergedTiles.Add(neighbor);

                  // ✅ Update min/max boundaries
                  minX = Math.Min(minX, neighbor.GlobalCenter.X);
                  minY = Math.Min(minY, neighbor.GlobalCenter.Y);
                  maxX = Math.Max(maxX, neighbor.GlobalCenter.X);
                  maxY = Math.Max(maxY, neighbor.GlobalCenter.Y);

                  if (neighbor._lastLIDARpoint != null)
                    lidarAngles.Add(neighbor._lastLIDARpoint.Angle);

                  if (neighbor.IsPermanent)
                    permanentTileCount++;
                }
              }
            }
          }
        }

        // ✅ Compute dominant angle
        float angle = ComputeDominantLidarAngle(lidarAngles);

        // ✅ Determine permanence
        bool isPermanentRegion = (float)permanentTileCount / mergedTiles.Count >= 0.2f;

        // ✅ Corrected Rectangle Creation
        int rectX = (int)(minX - tileSize / 2); // Move to top-left origin
        int rectY = (int)(minY - tileSize / 2);
        int rectWidth = (int)(maxX - minX + tileSize);
        int rectHeight = (int)(maxY - minY + tileSize);

        mergedRegions.Add((
            new Rectangle(rectX, rectY, rectWidth, rectHeight),
            angle,
            isPermanentRegion
        ));
      }

      _mergedObjects = mergedRegions;
    }


    private float ComputeDominantLidarAngle(List<float> lidarAngles)
    {
      if (lidarAngles.Count < 3) return 0f; // Avoid unstable calculations

      float sumSin = 0, sumCos = 0, sumAngles = 0;

      foreach (float angle in lidarAngles)
      {
        float normalizedAngle = angle % 360;
        float radians = MathHelper.ToRadians(normalizedAngle);

        sumSin += MathF.Sin(radians);
        sumCos += MathF.Cos(radians);

        //  Handle angle wrapping inside this loop
        if (normalizedAngle < 90 && sumAngles / lidarAngles.Count > 270)
          normalizedAngle += 360;

        sumAngles += normalizedAngle;
      }

      if (sumSin == 0 && sumCos == 0) return 0f; // Prevent division errors

      float avgRadians = MathF.Atan2(sumSin, sumCos);
      float avgDegrees = MathHelper.ToDegrees(avgRadians) % 360; // Normalize to 0-360

      //  Use the weighted average of angles to stabilize results
      avgDegrees = sumAngles / lidarAngles.Count;
      avgDegrees = avgDegrees % 360; // Normalize again

      //  Round to the nearest 5 degrees for stability
      avgDegrees = MathF.Round(avgDegrees / 5) * 5;

      return MathHelper.ToRadians(avgDegrees);
    }

    private float ComputeSurfaceAngle(List<Tile> tiles, int tileSize)
    {
      float sumDx = 0;
      float sumDy = 0;
      int count = 0;

      foreach (var tile in tiles)
      {
        foreach (var neighbor in tiles)
        {
          if (tile == neighbor) continue;

          float dx = neighbor.GlobalCenter.X - tile.GlobalCenter.X;
          float dy = neighbor.GlobalCenter.Y - tile.GlobalCenter.Y;
          float distance = Vector2.Distance(tile.GlobalCenter, neighbor.GlobalCenter);

          if (distance <= tileSize * AppSettings.Default.TilemergeThreshold) // Only consider close neighbors
          {
            sumDx += dx;
            sumDy += dy;
            count++;
          }
        }
      }

      if (count == 0) return 0f; // No rotation if no neighbors are found

      return (float)Math.Atan2(sumDy, sumDx); // Returns the rotation angle in radians
    }

    public List<MapPoint> GetPoints()
    {
      // Return a copy of the points list to avoid modification outside
      return new List<MapPoint>(_points);
    }

    public void Clear()
    {
      _points.Clear();
    }
  }
}