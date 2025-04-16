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
    private Vector2 _previousOffset = Vector2.Zero;
    public Dictionary<int, (Vector2 inferred, Vector2 origin)> PreviousRingVectors = new();
    private List<MapPoint> AddedPoints = new();
    private readonly Dictionary<int, float> _previousInferredDistances = new();


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
      EstimateDevicePosition,
      SimulateDevicePosition,
      UpdateDevicePosition,
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
            _currentState = MapUpdateState.EstimateDevicePosition;
          }
          break;



        case MapUpdateState.EstimateDevicePosition:
          {
            var allRingTiles = _gridManager.GetAllRingTiles();
            if (allRingTiles.Count == 0)
            {
              Debug.WriteLine("🕒 Skipping offset estimation — no established ring tiles yet.");
              _currentState = MapUpdateState.UpdateDevicePosition;
              break;
            }

            //var newInferredPoints = AddedPoints.Where(p => p.IsInferredRingPoint).ToList();
            //Vector2 offset = EstimateOffsetFromRingTiles(_pointsBuffer, EstablishedRingTilesByAngle);
            // Initial estimation
            //Vector2 initialOffset = EstimateOffsetFromRingTiles(_pointsBuffer, EstablishedRingTilesByAngle);
            //ApplyIndividualRingOffsets(_pointsBuffer, EstablishedRingTilesByAngle);
            //Vector2 initialOffset = EstimateOffsetFromRingTilesRawAngleMatch(_pointsBuffer, _establishedOriginalPointsByRawAngle);
            Vector2 offset = MotionEstimator.EstimateDeviceOffsetFromAngleHistory(_pointsBuffer);
            _device._devicePosition += offset;
            //if (initialOffset != Vector2.Zero)
            //{
            //  _device._devicePosition += initialOffset;
            //  _currentState = MapUpdateState.UpdateDevicePosition;
            //  _gridManager.ResetAllRingTiles();
            //  break;
            //}


            _currentState = MapUpdateState.AddingNewPoints;
            break;
          }
        case MapUpdateState.UpdateDevicePosition:
         // // Simulate adding points at estimated position
         // List<MapPoint> simulatedRingPoints = SimulateAddingPoints(_pointsBuffer, _device._devicePosition);

         // //Step 3: calculate corrective offset to center ring back to origin
         //Vector2 correctiveOffset = CalculateAngleLockedRingCorrection(_pointsBuffer, EstablishedRingTilesByAngle, _device._devicePosition);

         // //Apply corrective offset 

         // if (correctiveOffset != Vector2.Zero)
         // {
         //   _device._devicePosition += correctiveOffset;
         //   //UtilityProvider.Camera.CenterOn(_device._devicePosition);
         //   _gridManager.ResetAllRingTiles(); // Clear outdated ones after motion
         // }
          _currentState = MapUpdateState.AddingNewPoints;
          break;

        case MapUpdateState.AddingNewPoints:
          NewTiles.Clear();
          AddPoints(_pointsBuffer);
          _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;
          _pointsBuffer.Clear();
          _currentState = MapUpdateState.DistributePoints;
          break;



        case MapUpdateState.DistributePoints:
          {
            _Distributor.Distribute(AddedPoints);
            _currentState = MapUpdateState.UpdatingTiles;
          }
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
    public void ApplyIndividualRingOffsets(
    List<DataPoint> currentBatch,
    Dictionary<int, List<Tile>> angleToTilesDict)
    {
      const float maxMatchingDistance = 50f;

      foreach (var dp in currentBatch)
      {
        if (dp.Distance >= 3500) continue;

        Vector2 origin = dp.OriginalDevicePosition;
        float angleRad = MathHelper.ToRadians(dp.Angle + dp.Yaw);

        Vector2 inferred = origin + new Vector2(
            MathF.Cos(angleRad) * 3500,
            MathF.Sin(angleRad) * 3500
        );

        int angleKey = (int)(MathHelper.ToDegrees(angleRad) * 10) % 3600;
        if (angleKey < 0) angleKey += 3600;

        if (!angleToTilesDict.TryGetValue(angleKey, out var tileList))
          continue;

        Tile bestTile = null;
        float bestDist = float.MaxValue;

        foreach (var tile in tileList)
        {
          if (tile.InferredBy == null) continue;

          float dist = Vector2.Distance(inferred, tile.GlobalCenter);
          if (dist < bestDist && dist <= maxMatchingDistance)
          {
            bestDist = dist;
            bestTile = tile;
          }
        }

        if (bestTile == null) continue;

        Vector2 oldRing = bestTile.GlobalCenter;
        Vector2 oldOrigin = bestTile.InferredBy.EqTileGlobalCenter;

        float oldDistance = Vector2.Distance(oldRing, oldOrigin);
        float newDistance = Vector2.Distance(inferred, dp.EqTileGlobalCenter);

        float deltaDistance = newDistance - oldDistance;
        Vector2 direction = Vector2.Normalize(inferred - dp.EqTileGlobalCenter);
        Vector2 offset = direction * deltaDistance;

        if (offset.Length() > maxMatchingDistance)
          continue;

        // 👇 Apply correction directly
        dp.OriginalDevicePosition += offset;
      }
    }

    public Vector2 CalculateAngleLockedRingCorrection(
        List<DataPoint> currentBatch,
        Dictionary<int, List<Tile>> angleToTilesDict,
        Vector2 currentDevicePosition)
    {
      const float maxMatchingDistance = 50f;
      const int minRequiredMatches = 5;

      List<Vector2> offsets = new();

      foreach (var dp in currentBatch)
      {
        if (dp.Distance != 3500) continue; // Skip clamped ones

        float yaw = MathHelper.ToRadians(dp.Yaw);
        float raw = MathHelper.ToRadians(dp.Angle);
        float adjustedAngle = raw + yaw;

        Vector2 inferred = currentDevicePosition + new Vector2(
            MathF.Cos(adjustedAngle) * 3500,
            MathF.Sin(adjustedAngle) * 3500
        );

        int angleKey = (int)(MathHelper.ToDegrees(adjustedAngle) * 10) % 3600;
        if (angleKey < 0) angleKey += 3600;

        if (!angleToTilesDict.TryGetValue(angleKey, out var tileList) || tileList.Count == 0)
          continue;

        // Find closest tile for this angle
        Tile closest = tileList
            .OrderBy(t => Vector2.Distance(t.GlobalCenter, inferred))
            .FirstOrDefault();

        if (closest == null) continue;

        Vector2 offset = closest.GlobalCenter - inferred;

        if (offset.Length() > maxMatchingDistance)
          continue;

        offsets.Add(offset);
      }

      if (offsets.Count < minRequiredMatches)
      {
        Debug.WriteLine("🚫 Not enough matched angles for ring correction.");
        return Vector2.Zero;
      }

      return ComputeStableOffsetWithMAD(offsets, maxMatchingDistance);
    }
    public Vector2 EstimateOffsetFromRingTilesRawAngleMatch(
    List<DataPoint> currentBatch,
    Dictionary<int, List<Tile>> angleToTilesByRawAngle)
    {
      const float maxMatchingDistance = 50f;       // mm
      const float maxOffsetPerUpdate = 100f;       // mm
      const int minMatchesRequired = 10;
      const float smoothingFactor = 0.05f;
      const float minOffsetThreshold = 3f;

      List<Vector2> offsets = new();

      foreach (var dp in currentBatch)
      {
        if (dp.Distance >= 3500) continue; // Skip inferred ring points

        int rawAngleKey = (int)(dp.Angle * 10f) % 3600;
        if (rawAngleKey < 0) rawAngleKey += 3600;

        if (!angleToTilesByRawAngle.TryGetValue(rawAngleKey, out var tileList))
          continue;

        // Get the original hit location
        Vector2 newMeasuredPos = dp.EqTileGlobalCenter;

        // Try to find a tile whose InferredBy matches this angle
        Tile bestMatch = null;
        float bestDist = float.MaxValue;

        foreach (var tile in tileList)
        {
          if (tile.InferredBy == null) continue;

          float dist = Vector2.Distance(newMeasuredPos, tile.InferredBy.EqTileGlobalCenter);
          if (dist < bestDist && dist < maxMatchingDistance)
          {
            bestDist = dist;
            bestMatch = tile;
          }
        }

        if (bestMatch == null) continue;

        // Compute expected offset from where this tile was originally inferred
        Vector2 establishedMeasured = bestMatch.InferredBy.EqTileGlobalCenter;
        Vector2 delta = establishedMeasured - newMeasuredPos;

        if (delta.Length() > maxMatchingDistance)
          continue;

        offsets.Add(delta);
      }

      if (offsets.Count < minMatchesRequired)
      {
        //Debug.WriteLine($"❌ Not enough raw-angle matches ({offsets.Count}), skipping update.");
        return Vector2.Zero;
      }

      Vector2 stableOffset = ComputeStableOffsetWithMAD(offsets, maxOffsetPerUpdate);

      if (stableOffset.Length() > maxOffsetPerUpdate)
      {
        stableOffset = Vector2.Normalize(stableOffset) * maxOffsetPerUpdate;
        Debug.WriteLine($"⚠️ Clamped offset to {stableOffset}");
      }

      Vector2 smoothedOffset = Vector2.Lerp(_previousOffset, stableOffset, smoothingFactor);

      if (smoothedOffset.Length() < minOffsetThreshold)
      {
        //Debug.WriteLine($"🛑 Offset below threshold: {smoothedOffset.Length():F2}mm — skipping update.");
        return Vector2.Zero;
      }

      _previousOffset = smoothedOffset;
      return smoothedOffset;
    }


    public Vector2 EstimateOffsetFromRingTiles(
        List<DataPoint> currentBatch,
        Dictionary<int, List<Tile>> angleToTilesDict)
    {
      const float maxMatchingDistance = 50f;
      const float maxOffsetPerUpdate = 100f;
      const int minMatchesRequired = 10;
      const float smoothingFactor = 0.05f;
      const float minOffsetThreshold = 2f;

      var deltas = new List<Vector2>();

      foreach (var dp in currentBatch)
      {
        if (dp.Distance >= 3500) continue;

        Vector2 origin = dp.OriginalDevicePosition;
        float angleRad = MathHelper.ToRadians(dp.Angle + dp.Yaw);

        // Calculate inferred ring point at 3500mm along beam
        Vector2 inferred = origin + new Vector2(
            MathF.Cos(angleRad) * 3500,
            MathF.Sin(angleRad) * 3500
        );

        int adjustedAngleKey = (int)(MathHelper.ToDegrees(angleRad) * 10) % 3600;
        if (adjustedAngleKey < 0) adjustedAngleKey += 3600;

        if (!angleToTilesDict.TryGetValue(adjustedAngleKey, out var tileList))
          continue;

        // Find closest matching established ring tile at this angle
        Tile bestTile = null;
        float bestDist = float.MaxValue;

        foreach (var tile in tileList)
        {
          float dist = Vector2.Distance(inferred, tile.GlobalCenter);
          if (dist < bestDist && dist <= maxMatchingDistance && tile.InferredBy != null)
          {
            bestDist = dist;
            bestTile = tile;
          }
        }

        if (bestTile == null) continue;

        // Previously established positions (global coordinates)
        Vector2 oldRingGlobal = bestTile.GlobalCenter;
        Vector2 oldMeasuredGlobal = bestTile.InferredBy.EqTileGlobalCenter;

        // Previous distance between measured and ring tile
        float oldDistance = Vector2.Distance(oldRingGlobal, oldMeasuredGlobal);

        // New measured position (global), based on current estimate
        Vector2 newMeasuredGlobal = dp.EqTileGlobalCenter;

        // New inferred position (just calculated above as 'inferred')
        Vector2 newRingGlobal = inferred;

        // New distance between newly measured and inferred positions
        float newDistance = Vector2.Distance(newMeasuredGlobal, newRingGlobal);

        // The difference clearly indicates device moved closer or farther
        float distanceDelta = newDistance - oldDistance;

        // Direction clearly: from measured point to inferred ring point
        Vector2 direction = Vector2.Normalize(newRingGlobal - newMeasuredGlobal);

        // Offset vector clearly indicates exact required movement
        Vector2 offset = direction * distanceDelta;

        // CRITICAL: explicitly filter overly large offsets
        if (offset.Length() > maxMatchingDistance)
          continue;

        deltas.Add(offset);
      }

      if (deltas.Count < minMatchesRequired)
      {
        //Debug.WriteLine($"❌ Not enough matches ({deltas.Count}), skipping update.");
        return Vector2.Zero;
      }

      Vector2 stableOffset = ComputeStableOffsetWithMAD(deltas, maxOffsetPerUpdate);

      if (stableOffset.Length() > maxOffsetPerUpdate)
      {
        stableOffset = Vector2.Normalize(stableOffset) * maxOffsetPerUpdate;
        Debug.WriteLine($"⚠️ Clamped offset to {stableOffset}");
      }

      Vector2 smoothedOffset = Vector2.Lerp(_previousOffset, stableOffset, smoothingFactor);

      if (smoothedOffset.Length() < minOffsetThreshold)
      {
        Debug.WriteLine($"🛑 Offset below threshold: {smoothedOffset.Length():F2}mm — skipping update.");
        return Vector2.Zero;
      }

      _previousOffset = smoothedOffset;
      //Debug.WriteLine($"📐 Smoothed offset = {smoothedOffset}");
      return smoothedOffset;
    }




    private Vector2 ComputeStableOffsetWithMAD(List<Vector2> deltas, float maxOffsetPerUpdate)
    {
      if (deltas.Count == 0) return Vector2.Zero;

      // Step 1: Median
      var sortedX = deltas.Select(d => d.X).OrderBy(x => x).ToList();
      var sortedY = deltas.Select(d => d.Y).OrderBy(y => y).ToList();
      float medianX = sortedX[deltas.Count / 2];
      float medianY = sortedY[deltas.Count / 2];
      var median = new Vector2(medianX, medianY);

      // Step 2: Compute MAD (Median Absolute Deviation)
      var deviations = deltas
          .Select(d => Vector2.Distance(d, median))
          .OrderBy(d => d)
          .ToList();
      float mad = deviations[deviations.Count / 2];

      const float madMultiplier = 2.5f;  // Sensitivity; lower = stricter
      float threshold = mad * madMultiplier;

      // Step 3: Filter out large deviations from the median
      var filtered = deltas
          .Where(d => Vector2.Distance(d, median) <= threshold)
          .ToList();

      if (filtered.Count == 0)
        return Vector2.Zero;

      // Step 4: Average of filtered values
      Vector2 average = filtered.Aggregate(Vector2.Zero, (acc, d) => acc + d) / filtered.Count;

      // Step 5: Clamp
      if (average.Length() > maxOffsetPerUpdate)
        average = Vector2.Normalize(average) * maxOffsetPerUpdate;

      return average;
    }

    private Vector2 ComputeMedianOffset(List<Vector2> deltas)
    {
      if (deltas.Count == 0) return Vector2.Zero;

      var sortedX = deltas.Select(d => d.X).OrderBy(x => x).ToList();
      var sortedY = deltas.Select(d => d.Y).OrderBy(y => y).ToList();

      float medianX = sortedX[deltas.Count / 2];
      float medianY = sortedY[deltas.Count / 2];

      return new Vector2(medianX, medianY);
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
    public List<MapPoint> SimulateAddingPoints(List<DataPoint> dplist, Vector2 devicePos)
    {
      List<MapPoint> simulatedPoints = new();
      foreach (DataPoint dp in dplist)
      {
        if (dp.Distance >= 3500) continue;

        float yawRadians = MathHelper.ToRadians(dp.Yaw);
        float adjustedAngle = MathHelper.ToRadians(dp.Angle) + yawRadians;

        float inferredX = devicePos.X + 3500 * MathF.Cos(adjustedAngle);
        float inferredY = devicePos.Y + 3500 * MathF.Sin(adjustedAngle);
        Vector2 inferredGlobal = new(inferredX, inferredY);

        MapPoint inferredRingPoint = new MapPoint(
            3500 * MathF.Cos(adjustedAngle),
            3500 * MathF.Sin(adjustedAngle),
            dp.Angle, 3500,
            adjustedAngle, MathHelper.ToRadians(dp.Angle),
            dp.Quality,
            inferredGlobal.X, inferredGlobal.Y,
            devicePos,
            yawRadians,
            true
        );
        inferredRingPoint.IsInferredRingPoint = true;

        simulatedPoints.Add(inferredRingPoint);
      }
      return simulatedPoints;
    }
    public Vector2 CalculateCorrectiveOffset(List<MapPoint> simulatedRingPoints, Dictionary<int, List<Tile>> establishedRingDict)
    {
      const float maxMatchDistance = 50f;
      List<Vector2> correctiveOffsets = new();

      foreach (var inferredPoint in simulatedRingPoints)
      {
        int angleKey = (int)(MathHelper.ToDegrees(inferredPoint.Radians) * 10) % 3600;
        if (angleKey < 0) angleKey += 3600;

        if (!establishedRingDict.TryGetValue(angleKey, out var establishedTiles))
          continue;

        Tile closestTile = establishedTiles
            .OrderBy(t => Vector2.Distance(inferredPoint.EqTileGlobalCenter, t.GlobalCenter))
            .FirstOrDefault();

        if (closestTile == null) continue;

        Vector2 offset = closestTile.GlobalCenter - inferredPoint.EqTileGlobalCenter;

        if (offset.Length() > maxMatchDistance) continue;

        correctiveOffsets.Add(offset);
      }

      if (correctiveOffsets.Count == 0)
        return Vector2.Zero;

      return ComputeStableOffsetWithMAD(correctiveOffsets, maxMatchDistance);
    }



  }
}