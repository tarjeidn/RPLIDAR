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
    public TileMerge _tileMerge {  get; private set; }
    public InputManager InputManager { get; set; }
    //public List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians, bool IsPermanent)> PermanentLines = new();
    public List<LineSegment> PermanentLines = new();
    public HashSet<Tile> NewTiles { get; set; } = new();
    public bool _pointsAdded {  get; private set; }
    public int MatchedTileCount = 0;
    public int MismatchedTileCount = 0;

    private int _mergeTilesFrameCounter;
    public GridManager _gridManager { get; set; }


    public float GridScaleFactor { get; set; }
    public bool MapUpdated = false;
    public int _minPointQuality;
    public float _minPointDistance;
    bool IsUpdated = false;
    private int IdleFrames = 0;
    public int MinTileBufferSizeToAdd { get; set; } = 0;
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

      public ObservationKey(Vector2 pos, float angle, float dist)
      {
        DevicePosition = pos;
        Angle = angle;
        Distance = dist;
      }

      public override int GetHashCode()
      {
        return HashCode.Combine(
            (int)(DevicePosition.X / 10),  // bucket by 10mm
            (int)(DevicePosition.Y / 10),
            (int)(Angle * 2),              // bucket by 0.5°
            (int)(Distance / 10)           // bucket by 10mm
        );
      }

      public override bool Equals(object obj)
      {
        if (obj is not ObservationKey other) return false;

        return Vector2.Distance(DevicePosition, other.DevicePosition) < 10f && // within 10mm (1cm)
               Math.Abs(Angle - other.Angle) < 0.5f &&                         // within 0.5 degrees
               Math.Abs(Distance - other.Distance) < 10f;                     // within 10mm (1cm)
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
      _minPointDistance = _tileMerge.MinPointDistance;
      _minPointQuality = _tileMerge.MinPointQuality;
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

      // ✅ Process new LiDAR points
      if (dplist.Count > 0)
      {
        _pointsBuffer.AddRange(dplist);
      }

      switch (_currentState)
      {
        case MapUpdateState.Idle:
          if (_pointsBuffer.Count > MinTileBufferSizeToAdd)
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
            int matched, mismatched;
            Vector2 currentDevicePos = _device._devicePosition;
            Stopwatch sw = Stopwatch.StartNew();
            Vector2? estimatedOffset = DevicePositionEstimator.EstimateOffset(
                _pointsBuffer,
                Map.ObservationLookup,
                currentDevicePos,
                MapScaleManager.Instance.ScaledTileSizePixels,
                _gridManager,
                out matched,
                out mismatched
            );
            sw.Stop();
            if (sw.ElapsedMilliseconds >  1) Debug.WriteLine($"⏱ Motion estimation took: {sw.ElapsedMilliseconds}ms");
            float mismatchRatio = (matched == 0) ? 0 : (float)mismatched / matched;
            //Debug.WriteLine($"[MotionEstimator] Matched: {matched}, Mismatched: {mismatched}, Ratio: {mismatchRatio}");

            if (estimatedOffset.HasValue)
            {
              float smoothingFactor = 0.25f; // You can tweak this
              Vector2 targetPosition = _device._devicePosition + estimatedOffset.Value;

              _device._devicePosition = Vector2.Lerp(_device._devicePosition, targetPosition, smoothingFactor);

              //_device._devicePosition += estimatedOffset.Value;
              //Debug.WriteLine($"📍 Adjusted device position by {estimatedOffset.Value}");
              //Debug.WriteLine($"📍 Adjusted device position  {_device._devicePosition}");
              Map.ObservationLookup.Clear(); // start fresh from new position
            }

            _currentState = MapUpdateState.AddingNewPoints;
            break;
          }

        case MapUpdateState.AddingNewPoints:
          NewTiles.Clear();
          AddPoints(_pointsBuffer);
          _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;
          _pointsBuffer.Clear();
          _currentState = MapUpdateState.ComparingToPermanentLines;
          break;

        case MapUpdateState.ComparingToPermanentLines:

          //if(PermanentLines.Count > 0)AlgorithmProvider.DevicePositionEstimator.UpdateDevicePosition(); // ✅ Adjust device position based on matched tiles
          _currentState = MapUpdateState.UpdatingTiles;
          break;

        case MapUpdateState.UpdatingTiles:
          //_tileMerge.Update();
          _currentState = MapUpdateState.RunningTrustRegulator;
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
      //if (GUIProvider.UserSelection.isSelecting) IsUpdated = true;
      return IsUpdated;
    }


    //public bool Update(List<DataPoint> dplist, GameTime gameTime)
    //{
    //  float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
    //  IsUpdated = false;

    //  InputManager.Update(gameTime);
    //  _tileMerge.Update();
    //  if (dplist.Count > 0)
    //  {
    //    _pointsBuffer.AddRange(dplist);
    //  }

    //  if (_pointsBuffer.Count() > MinTileBufferSizeToAdd)
    //  {
    //    StatisticsProvider.MapStats.AddPointUpdates++;
    //    StatisticsProvider.MapStats.UpdatesSincePointsAdded = 0;
    //    _fpsCounter.IncrementLiDARUpdate();
    //    AddPoints(dplist);
    //    _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;
    //    _Distributor.Update();

    //    if (_tileTrustRegulator.RegulatorEnabled)
    //    {
    //      _tileTrustRegulator.Update(deltaTime);
    //    }
    //    IsUpdated = true;
    //    _pointsBuffer.Clear();
    //    //_mergeTilesFrameCounter = 0; // Reset counter since we received new points
    //  }
    //  _MapStats.AddToPointHistory(dplist.Count);
    //  if (!IsUpdated) {
    //    IdleFrames++;
    //    if (IdleFrames == 60)
    //    {
    //      IsUpdated = true;
    //      IdleFrames = 0;
    //    }
    //  }
    //  return IsUpdated;
    //}
    // Clears all added tiles
    public void ResetAllTiles()
    {
      foreach (var grid in _Distributor._GridManager.Grids.Values)
      {
        grid.ResetTiles();
      }
      _tileMerge.ClearMergedObjects();
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
      List<MapPoint> addedPoints = new List<MapPoint>(dplist.Count);

      //float deviceRotationRadians = MathHelper.ToRadians(_device._deviceOrientation); // 🔥 Convert device rotation to radians
      float deviceRotationRadians = _device._deviceOrientation;

      foreach (DataPoint dp in dplist)
      {
        if (dp.Quality >= _minPointQuality && dp.Distance >= _minPointDistance)
        {
          float pointAngleRadians = MathHelper.ToRadians(dp.Angle);

          // **🔥 Apply device rotation to LiDAR point**
          float adjustedAngle = pointAngleRadians - deviceRotationRadians;

          float distance = dp.Distance;

          // 🔥 Rotate the relative position based on the device's rotation
          float relativeX = distance * (float)Math.Cos(adjustedAngle);
          float relativeY = distance * (float)Math.Sin(adjustedAngle);

          // Convert to global coordinates
          float globalX = _device._devicePosition.X + relativeX;
          float globalY = _device._devicePosition.Y + relativeY;

          // Store the transformed values in MapPoint
          MapPoint mapPoint = new MapPoint(relativeX, relativeY, dp.Angle, distance, adjustedAngle, dp.Quality, globalX, globalY);
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
    //    if (dp.Quality >= _minPointQuality && dp.Distance >= _minPointDistance)
    //    {
    //      float radians = MathHelper.ToRadians(dp.Angle);
    //      float distance = dp.Distance;


    //      // Relative positions, from the viewpoint of the device. 
    //      float relativeX = distance * (float)Math.Cos(radians);
    //      float relativeY = distance * (float)Math.Sin(radians);

    //      // Global coordinates 
    //      float globalX = (_device._devicePosition.X) + relativeX;
    //      float globalY = (_device._devicePosition.Y) + relativeY;

    //      //  Store only scaled values in MapPoint
    //      MapPoint mapPoint = new MapPoint(relativeX, relativeY, dp.Angle, distance, radians, dp.Quality, globalX, globalY);
    //      addedPoints.Add(mapPoint);
    //    }
    //  }

    //  _Distributor.Distribute(addedPoints);
    //}





    public List<MapPoint> GetPoints()
    {
      // Return a copy of the points list to avoid modification outside
      return new List<MapPoint>(_points);
    }


  }
}