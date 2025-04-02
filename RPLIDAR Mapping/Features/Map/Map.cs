using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
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
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;

using static RPLIDAR_Mapping.Features.Map.Algorithms.DevicePositionEstimator;




namespace RPLIDAR_Mapping.Features.Map
{
  public class Map
  {
    private int MaxPoints = AppSettings.Default.MaxPoints; // Maximum points to store
    private readonly List<MapPoint> _points; // List of LiDAR points

    //private MapDictionary _mapDictionary;
    private Distributor _Distributor { get; set; }
    public MapStats _MapStats { get; set; }
    public Device _device { get; set; }
    private List<DataPoint> _pointsBuffer;
    private FPSCounter _fpsCounter;
    public TileTrustRegulator _tileTrustRegulator;
    public TileMerge _tileMerge {  get; private set; }
    public InputManager InputManager { get; set; }
    //public List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians, bool IsPermanent)> PermanentLines = new();
    public List<LineSegment> PermanentLines = new();
    public HashSet<Tile> NewTiles { get; set; } = new();
    public List<(Vector2 from, Vector2 to)> MatchedPairs { get; set; } = new();
    private List<DataPoint> _lastScanPoints = new();

    public bool _pointsAdded {  get; private set; }
    public int MatchedTileCount = 0;
    public int MismatchedTileCount = 0;


    private int _mergeTilesFrameCounter;
    public GridManager _gridManager { get; set; }

    public float GridScaleFactor { get; set; }
    public bool MapUpdated = false;
    public int _minPointQuality = 5;
    public float _minPointDistance = 160;
    public int MaxAcceptableDistance = 3000;
    bool IsUpdated = false;
    private int IdleFrames = 0;
    public int MinTileBufferSizeToAdd { get; set; } = 50;
    public enum MapUpdateState
    {
      Idle,
      UpdateDevicePosition,
      AddingNewPoints,
      UpdatingTiles,
      RunningTrustRegulator,
      DistributingTiles,
      ComparingToPermanentLines,
      Complete
    }
    public static Dictionary<ObservationKey, Tile> ObservationLookup = new();
    public struct ObservationKey
    {
      public Vector2 DevicePosition;
      public float Angle;
      public float Distance;
      public float Orientation;

      public ObservationKey(Vector2 pos, float angle, float dist, float orientation)
      {
        DevicePosition = pos;
        Angle = angle;
        Distance = dist;
        Orientation = orientation;
    }

      public override int GetHashCode()
      {
        return HashCode.Combine(
            (int)(DevicePosition.X / 10),  // bucket by 10mm
            (int)(DevicePosition.Y / 10),
            (int)(Angle * 2),              // bucket by 0.5°
            (int)(Distance / 10),           // bucket by 10mm
            (int)(Orientation * 10)
        );
      }

      public override bool Equals(object obj)
      {
        if (obj is not ObservationKey other) return false;

        return Vector2.Distance(DevicePosition, other.DevicePosition) < 10f && // within 10mm (1cm)
               Math.Abs(Angle - other.Angle) < 0.5f &&                         // within 0.5 degrees
               Math.Abs(Distance - other.Distance) < 10f &&                     // within 10mm (1cm)
               Math.Abs(Orientation - other.Orientation) < 0.5f;
      }
    }


      public Map(Device device, InputManager IM)
    {
      _device = device;
      MapScaleManager.Instance._map = this;
      InputManager = IM;
      _tileTrustRegulator = AlgorithmProvider.TileTrustRegulator;
      _tileMerge = AlgorithmProvider.TileMerge;
      _tileMerge._map = this;
      _mergeTilesFrameCounter = 0;
      _fpsCounter = UtilityProvider.FPSCounter;
      _MapStats = new MapStats();
      _points = new List<MapPoint>();
      _Distributor = new Distributor(this);
      _pointsBuffer = new List<DataPoint>();
      _gridManager = _Distributor._GridManager;
      GridScaleFactor = MapScaleManager.Instance.ScaleFactor;

    }
    private MapUpdateState _currentState = MapUpdateState.Idle;

    public bool Update(List<DataPoint> dplist, GameTime gameTime)
    {
      float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
      IsUpdated = false;
      InputManager.Update(gameTime);
      // Process new LiDAR points
      if (dplist.Count > 0)
      {
        _pointsBuffer.AddRange(dplist);
      }

      switch (_currentState)
      {
        case MapUpdateState.Idle:
          if (_pointsBuffer.Count >= MinTileBufferSizeToAdd)
          {
            StatisticsProvider.MapStats.AddPointUpdates++;
            StatisticsProvider.MapStats.UpdatesSincePointsAdded = 0;
            MatchedTileCount = 0;
            MismatchedTileCount = 0;
            _fpsCounter.IncrementLiDARUpdate();
            _currentState = MapUpdateState.UpdateDevicePosition;
          }
          break;
        case MapUpdateState.UpdateDevicePosition:
          {


            // Estimate offset ONLY when we have enough angle coverage
            Vector2 estimatedOffset = Vector2.Zero;
            (estimatedOffset, List<(Vector2 from, Vector2 to)> matchedPairs) = AlgorithmProvider.ICP.Run(_pointsBuffer, GetPermanentTiles());
            //(estimatedOffset, List<(Vector2 from, Vector2 to)> matchedPairs) = AlgorithmProvider.ICP.Run(_pointsBuffer, _gridManager.GetAllTrustedTiles());



            _device._devicePosition += estimatedOffset;
            MatchedPairs = matchedPairs;
            Debug.WriteLine($"🧭 Device Pos: {_device._devicePosition}");

            /// TILEMATCHING
            //DevicePositionEstimator.Update(_pointsBuffer, _gridManager);
            /// ICP
            //AlgorithmProvider.ICP.Update(_pointsBuffer, _tileMerge.TileClusters); 
            _currentState = MapUpdateState.AddingNewPoints;
          }
          break;
          

        case MapUpdateState.AddingNewPoints:
          NewTiles.Clear();
          AddPoints(_pointsBuffer);
          _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;
          _pointsBuffer.Clear();
          _currentState = MapUpdateState.UpdatingTiles;
          break;

        case MapUpdateState.ComparingToPermanentLines:
          _currentState = MapUpdateState.UpdatingTiles;
          break;

        case MapUpdateState.UpdatingTiles:
          _tileMerge.Update();
          //_currentState = MapUpdateState.RunningTrustRegulator;
          // 🔥 Wait until TileMerge is fully processed before continuing
          if (_tileMerge.CurrentState == TileMerge.MergeState.ReadyToDraw)
          {
            _currentState = MapUpdateState.RunningTrustRegulator;
          }
          break;
        case MapUpdateState.RunningTrustRegulator:
          if (_tileTrustRegulator.RegulatorEnabled)
          {
            _tileTrustRegulator.Update(deltaTime);
          }
          _currentState = MapUpdateState.DistributingTiles;
          break;

        case MapUpdateState.DistributingTiles:
          _Distributor.Update();
          _currentState = MapUpdateState.Complete;
          break;

        case MapUpdateState.Complete:
          IsUpdated = true;
          _currentState = MapUpdateState.Idle;
          break;
      }
      return IsUpdated;
    }
    public (Vector2 offset, List<(Vector2 from, Vector2 to)> matches) EstimateOffsetFromTrustedTiles(List<DataPoint> currentPoints, HashSet<Tile> trustedTiles)
    {
      List<(Vector2 scanPoint, Vector2 tileCenter)> matchedPairs = new();
      float matchRadius = 10f;

      foreach (var point in currentPoints)
      {
        // Get the global position where this beam thinks it hits
        float angleRad = MathHelper.ToRadians(point.Angle + point.Yaw);
        Vector2 estimatedHit = _device._devicePosition + new Vector2(
            MathF.Cos(angleRad) * point.Distance,
            MathF.Sin(angleRad) * point.Distance
        );

        // Try to find the closest trusted tile within matchRadius
        Tile? match = trustedTiles
            .FirstOrDefault(t => Vector2.DistanceSquared(t.GlobalCenter, estimatedHit) <= matchRadius * matchRadius);

        if (match != null)
        {
          matchedPairs.Add((estimatedHit, match.GlobalCenter));
          Debug.WriteLine($"🔍 Match → Beam @ {point.Angle:F1}° → Δ: {match.GlobalCenter - estimatedHit}");
        }
      }

      if (matchedPairs.Count < 5)
      {
        Debug.WriteLine($"⚠ Not enough matched tiles ({matchedPairs.Count}) to estimate offset.");
        return (Vector2.Zero, matchedPairs);
      }

      // Calculate centroids
      Vector2 scanSum = Vector2.Zero;
      Vector2 tileSum = Vector2.Zero;
      foreach (var (scan, tile) in matchedPairs)
      {
        scanSum += scan;
        tileSum += tile;
      }

      Vector2 scanCenter = scanSum / matchedPairs.Count;
      Vector2 tileCenter = tileSum / matchedPairs.Count;

      Vector2 offset = tileCenter - scanCenter;
      Debug.WriteLine($"📌 Estimated Offset from Tiles: {offset} from {matchedPairs.Count} matches");
      return (offset, matchedPairs);
    }

    public HashSet<Tile> GetPermanentTiles()
    {
      HashSet<Tile> allPermanentTiles = new();
        foreach (LineSegment line in PermanentLines)      
        {
        if (line == null) continue;
          allPermanentTiles.UnionWith(line.ParentCluster.Tiles);
        }
        return allPermanentTiles;
    }






    public void resetAllGrids()
    {
      _gridManager.Grids.Clear();
      IsUpdated = true;
    }

    public Distributor GetDistributor()
    {
      return _Distributor;
    }
    public void AddPoints(List<DataPoint> dplist)
    {
      Rectangle sourceRect = UtilityProvider.Camera.GetSourceRectangle();
      List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);
      var sortedList = dplist.OrderBy(dp => dp.TimeStamp).ToList();

      foreach (DataPoint dp in sortedList)
      {
        if (dp.Quality >= _minPointQuality && dp.Distance >= _minPointDistance && dp.Distance <= MaxAcceptableDistance)
        {
          float yawRadians = MathHelper.ToRadians(dp.Yaw);
          float rawRadians = MathHelper.ToRadians(dp.Angle);

          // Use IMU yaw as device orientation
          _device._deviceOrientation = yawRadians;

          float adjustedAngle = rawRadians + yawRadians;

          // Apply current (updated) device position to this point
          Vector2 devicePosAtHit = _device._devicePosition;

          float relativeX = dp.Distance * MathF.Cos(adjustedAngle);
          float relativeY = dp.Distance * MathF.Sin(adjustedAngle);
          float globalX = devicePosAtHit.X + relativeX;
          float globalY = devicePosAtHit.Y + relativeY;

          Vector2 screenpos = UtilityProvider.Camera.WorldToScreen(new Vector2(globalX, globalY));
          if (!sourceRect.Contains(screenpos))
            continue;

          MapPoint mapPoint = new MapPoint(
              relativeX, relativeY,
              dp.Angle, dp.Distance,
              adjustedAngle, rawRadians,
              dp.Quality,
              globalX, globalY,
              devicePosAtHit,
              yawRadians
          );

          addedPoints.Add(mapPoint);
        }
      }

      _Distributor.Distribute(addedPoints);
    }



  }
}