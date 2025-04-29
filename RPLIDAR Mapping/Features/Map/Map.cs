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
    public HashSet<int> observedAngles = new();
    public MotionEstimator MotionEstimator = new();
    public TileMerge _tileMerge { get; private set; }
    public InputManager InputManager { get; set; }
    //public List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians, bool IsPermanent)> PermanentLines = new();
    public List<LineSegment> PermanentLines = new();
    public HashSet<Tile> NewTiles { get; set; } = new();
    public List<(Vector2 from, Vector2 to)> MatchedPairs { get; set; } = new();
    private List<DataPoint> _lastScanPoints = new();
    public Dictionary<int, List<(MapPoint, MapPoint)>> PreviousRingPointPairs = new();
    public Dictionary<int, List<(MapPoint, MapPoint)>> CurrentRingPairs = new();
    public Dictionary<int, List<Tile>> EstablishedRingTilesByAngle { get; private set; } = new();
    public Dictionary<int, List<Tile>> _establishedOriginalPointsByRawAngle = new();
    public Dictionary<int, Tile> AngularTileMap = new();
    public Dictionary<int, Tile> _tempAngularTileMap = new();
    public Queue<Vector2> _previousOffsets = new();
    private Vector2 _previousOffset = Vector2.Zero;
    public Dictionary<int, (Vector2 inferred, Vector2 origin)> PreviousRingVectors = new();
    private List<MapPoint> AddedPoints = new();
    private readonly Dictionary<int, float> _previousInferredDistances = new();
    private Vector2 _lastCoarseOffset = Vector2.Zero;


    public bool _pointsAdded { get; private set; }
    public int MatchedTileCount = 0;
    public int MismatchedTileCount = 0;


    private int _mergeTilesFrameCounter;
    public GridManager _gridManager { get; set; }

    public float GridScaleFactor { get; set; }
    public bool MapUpdated = false;
    public int _minPointQuality = 5;
    public float _minPointDistance = 0;
    public int MaxAcceptableDistance = 4000;
    bool IsUpdated = false;
    private int IdleFrames = 0;
    public int MinTileBufferSizeToAdd { get; set; } = 50;
    public enum MapUpdateState
    {
      Idle,
      AddingNewPoints,
      CreateVirtualReference,
      EstimateDevicePosition,
      SimulateDevicePosition,
      EstimateDevicePositionFromCluster,
      DistributePoints,
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
            //_currentState = MapUpdateState.AddingNewPoints;
            _currentState = MapUpdateState.SimulateDevicePosition;
                    }
          break;



        case MapUpdateState.SimulateDevicePosition:

          // Simulate adding points at estimated position
          Vector2 simOffset = MotionEstimator.EstimateOffsetAdaptive(_pointsBuffer, _device._devicePosition);
          _device._devicePosition += simOffset;
          MotionEstimator.LastEstimatedOffset += simOffset;
          _currentState = MapUpdateState.EstimateDevicePosition;
          break;

        case MapUpdateState.EstimateDevicePosition:
          {
            Vector2 offset = MotionEstimator.EstimateDeviceOffsetFromAngleHistory(_pointsBuffer);
            _device._devicePosition += offset;
            MotionEstimator.LastEstimatedOffset += offset;
            _currentState = MapUpdateState.AddingNewPoints;
            break;
          }
        case MapUpdateState.EstimateDevicePositionFromCluster:
          if (HasEnoughStableClusters())
          {
            Vector2 clusterCorrectionOffset = MotionEstimator.EstimateCorrectionFromClusters(_tileMerge.TileClusters, _device._devicePosition);
            _device._devicePosition += clusterCorrectionOffset;
          }
          _currentState = MapUpdateState.AddingNewPoints;
          break;
          
        case MapUpdateState.AddingNewPoints:

          AddPoints(_pointsBuffer);
          _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;
          _pointsBuffer.Clear();
          _currentState = MapUpdateState.DistributePoints;
          break;



        case MapUpdateState.DistributePoints:
          {
            _Distributor.Distribute(AddedPoints);
            //LinkAngularNeighbors();
            //LinkNearestNeighborsByProximity(NewTiles.ToList());

            _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;
            _currentState = MapUpdateState.UpdatingTiles;

            //NewTiles.Clear(); 
          }
          break;

        case MapUpdateState.ComparingToPermanentLines:
          _currentState = MapUpdateState.UpdatingTiles;
          break;

        case MapUpdateState.UpdatingTiles:
          _tileMerge.Update();
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
          MotionEstimator.LastEstimatedOffset = Vector2.Zero;
          _currentState = MapUpdateState.Idle;
          break;
      }
      return IsUpdated;
    }

    private bool HasEnoughStableClusters()
    {
      const int MinimumTilesInCluster = 100;
      const int MinimumStableClusters = 3;

      int stableClusterCount = _tileMerge.TileClusters.Count(c => c.Tiles.Count >= MinimumTilesInCluster);
      return stableClusterCount >= MinimumStableClusters;
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
      //Debug.WriteLine($"Device pos when adding: {_device._devicePosition}");
      Rectangle sourceRect = UtilityProvider.Camera.GetSourceRectangle();
      List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);
      var sortedList = dplist.OrderBy(dp => dp.TimeStamp).ToList();

      foreach (DataPoint dp in sortedList)
      {
        if (dp.Distance >= 3500) continue;
        int angleDeg = (int)Math.Round(dp.Angle); // 0–359
        if (!observedAngles.Contains(angleDeg))
        {
          observedAngles.Add(angleDeg);
        }
        if (dp.Quality >= _minPointQuality && dp.Distance >= _minPointDistance && dp.Distance <= MaxAcceptableDistance)
        {
          bool isRingPoint = dp.Distance >= 3500;

          float yawRadians = MathHelper.ToRadians(dp.Yaw);
          float rawRadians = MathHelper.ToRadians(dp.Angle);

          // Use IMU yaw as device orientation
          _device._deviceOrientation = yawRadians;

          float adjustedAngle = rawRadians + yawRadians;

          // Apply current (updated) device position to this point
          //Vector2 devicePosAtHit = _device._devicePosition;
          Vector2 devicePosAtHit = dp.DevicePositionAtHit;

          float relativeX = dp.Distance * MathF.Cos(adjustedAngle);
          float relativeY = dp.Distance * MathF.Sin(adjustedAngle);
          float globalX = devicePosAtHit.X + relativeX;
          float globalY = devicePosAtHit.Y + relativeY;

          //Vector2 screenpos = UtilityProvider.Camera.WorldToScreen(new Vector2(globalX, globalY));
          //if (!sourceRect.Contains(screenpos))
          //  continue;

          MapPoint mapPoint = new MapPoint(
              relativeX, relativeY,
              dp.Angle, dp.Distance,
              adjustedAngle, rawRadians,
              dp.Quality,
              globalX, globalY,
              devicePosAtHit,
              yawRadians,
              isRingPoint
          );
          mapPoint.EqTileGlobalCenter = dp.EqTileGlobalCenter;
          mapPoint.EqTilePosition = dp.EqTilePosition;

          addedPoints.Add(mapPoint);
          // Add an inferred ringpoint behind every normal point
          //if (!isRingPoint)
          //{
          //  float inferredRingX = 3500 * MathF.Cos(adjustedAngle);
          //  float inferredRingY = 3500 * MathF.Sin(adjustedAngle);
          //  float inferredGlobalX = devicePosAtHit.X + inferredRingX;
          //  float inferredGlobalY = devicePosAtHit.Y + inferredRingY;

          //  MapPoint inferredRingPoint = new MapPoint(
          //      inferredRingX, inferredRingY,
          //      dp.Angle, 3500,
          //      adjustedAngle, rawRadians,
          //      dp.Quality,
          //      inferredGlobalX, inferredGlobalY,
          //      devicePosAtHit,
          //      yawRadians,
          //      true
          //  );
          //  int tileSize = 10;
          //  Vector2 tilePos = new(MathF.Floor(inferredGlobalX / tileSize) * tileSize, MathF.Floor(inferredGlobalX / tileSize) * tileSize);
          //  Vector2 tileCenter = tilePos + new Vector2(tileSize / 2f, tileSize / 2f);
          //  inferredRingPoint.IsInferredRingPoint = true;
          //  inferredRingPoint.InferredBy = mapPoint;
          //  inferredRingPoint.EqTileGlobalCenter = tileCenter;
          //  addedPoints.Add(inferredRingPoint);

          //}
        }
      }
      AddedPoints = addedPoints;
    }

    private Vector2 ComputeMedian(List<Vector2> vectors)
    {
      if (vectors.Count == 0)
        return Vector2.Zero;

      var xs = vectors.Select(v => v.X).OrderBy(x => x).ToList();
      var ys = vectors.Select(v => v.Y).OrderBy(y => y).ToList();

      float medianX = xs[xs.Count / 2];
      float medianY = ys[ys.Count / 2];

      return new Vector2(medianX, medianY);
    }
    private List<Vector2> FilterOutliersMAD(List<Vector2> deltas, float madMultiplier = 2.5f)
    {
      if (deltas.Count == 0)
        return deltas;

      Vector2 median = ComputeMedian(deltas);
      List<float> distances = deltas.Select(d => Vector2.Distance(d, median)).ToList();

      float mad = ComputeMedian(distances);

      float threshold = mad * madMultiplier;

      return deltas
          .Where(d => Vector2.Distance(d, median) < threshold)
          .ToList();
    }

    private float ComputeMedian(List<float> values)
    {
      if (values.Count == 0)
        return 0f;

      var sorted = values.OrderBy(v => v).ToList();
      return sorted[sorted.Count / 2];
    }

  


  }
}