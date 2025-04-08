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
    public HashSet<int> observedAngles = new();
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
            _currentState = MapUpdateState.AddingNewPoints;
          }
          break;

        case MapUpdateState.AddingNewPoints:
          NewTiles.Clear();
          AddPoints(_pointsBuffer);
          _tileMerge.CurrentState = TileMerge.MergeState.NewPointsAdded;

          _currentState = MapUpdateState.UpdateDevicePosition;
          break;

        case MapUpdateState.UpdateDevicePosition:
          {
            //var allRingTiles = _gridManager.GetAllRingTiles();
            //if (allRingTiles.Count == 0)
            //{
            //  Debug.WriteLine("🕒 Skipping offset estimation — no established ring tiles yet.");
            //  _currentState = MapUpdateState.DistributePoints;
            //  break;
            //}

            //var newInferredPoints = AddedPoints.Where(p => p.IsInferredRingPoint).ToList();
            //Vector2 offset = EstimateOffsetFromRingTiles(_pointsBuffer, EstablishedRingTilesByAngle);

            //if (offset != Vector2.Zero)
            //{
            //  _device._devicePosition += offset;
            //  UtilityProvider.Camera.CenterOn(_device._devicePosition);
            //  _gridManager.ResetAllRingTiles(); // Clear outdated ones after motion
            //}

            _pointsBuffer.Clear();
            _currentState = MapUpdateState.DistributePoints;
            break;
          }





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
    private bool IsValidRingMatch(Vector2 oldInferred, Vector2 oldOrigin, Vector2 newInferred, Vector2 newOrigin,
                               float maxDelta, float maxDistanceChange)
    {
      Vector2 oldVector = oldInferred - oldOrigin;
      Vector2 newVector = newInferred - newOrigin;
      Vector2 delta = oldVector - newVector;

      float distanceChange = MathF.Abs(oldVector.Length() - newVector.Length());
      float individualDelta = delta.Length();

      if (individualDelta > maxDelta)
        return false;

      if (distanceChange > maxDistanceChange)
        return false;

      return true;
    }
    public Vector2 EstimateOffsetFromRingTiles(
      List<DataPoint> currentBatch,
      Dictionary<int, List<Tile>> angleToTilesDict)
    {
      const float maxMatchingDistance = 50f;       // mm
      const float maxOffsetPerUpdate = 100f;       // mm
      const int minMatchesRequired = 10;
      const float maxIndividualDelta = 5f;         // mm
      const float maxDistanceDifference = 10f;     // mm — reject if device→tile distance changes too much
      const float maxAllowedDistanceDelta = 20f;   // mm — tweak as needed
      const float smoothingFactor = 0.1f;          // reduced to lessen long-term drift
      const float minOffsetThreshold = 0.35f;      // mm – ignore sub-millimeter drift
      const float maxInferredExtensionError = 20f; // mm
      var deltas = new List<Vector2>();
      //Debug.WriteLine($"Offset estimate based on: {_device._devicePosition}");
      foreach (var dp in currentBatch)
      {
        if (dp.Distance >= 3500) continue; // Only use inferred ring points

        float angleRad = MathHelper.ToRadians(dp.Angle + dp.Yaw);
        Vector2 origin = dp.OriginalDevicePosition;

        // Convert origin to the tile center to match tile system
        int tileSize = 10;
        Vector2 originTile = new(
          MathF.Floor(origin.X / tileSize) * tileSize + tileSize / 2f,
          MathF.Floor(origin.Y / tileSize) * tileSize + tileSize / 2f
        );

        // Compute the expected ring hit location (3500mm along adjusted beam direction)
        Vector2 inferred = originTile + new Vector2(
          MathF.Cos(angleRad) * 3500,
          MathF.Sin(angleRad) * 3500
        );

        // Adjusted angle key for tile lookup
        int adjustedAngleKey = (int)(MathHelper.ToDegrees(angleRad) * 10) % 3600;
        if (adjustedAngleKey < 0) adjustedAngleKey += 3600;

        // Raw angle key for previous distance match
        int rawAngleKey = (int)(dp.Angle * 10) % 3600;
        if (rawAngleKey < 0) rawAngleKey += 3600;

        if (!angleToTilesDict.TryGetValue(adjustedAngleKey, out var tileList) || tileList.Count == 0)
          continue;

        // Distance consistency check
        float inferredDistance = 3500f;
        if (_previousInferredDistances.TryGetValue(rawAngleKey, out float previousDistance))
        {
          float deltaDistance = Math.Abs(inferredDistance - previousDistance);
          if (deltaDistance > maxAllowedDistanceDelta)
          {
            Debug.WriteLine($"🚫 Δdistance too large at angle {rawAngleKey / 10f:F1}°: {deltaDistance:F1}mm");
            continue;
          }
        }
        // Expected distance from hit to inferred point
        float expectedExtension = 3500f - dp.Distance;
        Vector2 actualHitPoint = origin + new Vector2(
            MathF.Cos(angleRad) * dp.Distance,
            MathF.Sin(angleRad) * dp.Distance
        );
        float actualExtension = Vector2.Distance(actualHitPoint, inferred);

        float extensionError = Math.Abs(actualExtension - expectedExtension);
        if (extensionError > maxInferredExtensionError)  // e.g., 5mm
        {
          Debug.WriteLine($"🚫 Inferred ring extension mismatch at angle {dp.Angle:F1}°: Δextension = {extensionError:F1}mm");
          continue;
        }

        // Find best matching tile near the inferred ring point
        Tile bestTile = null;
        float bestDist = float.MaxValue;
        foreach (var tile in tileList)
        {
          float dist = Vector2.Distance(inferred, tile.GlobalCenter);
          if (dist < bestDist && dist <= maxMatchingDistance)
          {
            bestDist = dist;
            bestTile = tile;
          }
        }

        if (bestTile == null) continue;

        Vector2 delta = bestTile.GlobalCenter - inferred;
        //Vector2 rawDelta = bestTile.GlobalCenter - inferred;
        //Vector2 delta = new Vector2(rawDelta.X + 1.5f, rawDelta.Y + 1.5f);
        // Check if the distance to tile matches the ring length
        float distFromDeviceToTile = Vector2.Distance(originTile, bestTile.GlobalCenter);
        float distDiff = Math.Abs(distFromDeviceToTile - 3500f);
        if (distDiff > maxDistanceDifference)
        {
          // Debug.WriteLine($"🚫 Distance check failed at angle {adjustedAngleKey / 10f:F1}°: Δdistance = {distDiff:F1}mm");
          continue;
        }

        if (delta.Length() > maxIndividualDelta)
        {
          // Debug.WriteLine($"🚫 Delta too large at angle {adjustedAngleKey / 10f:F1}°: Δoffset = {delta.Length():F1}mm");
          continue;
        }

        deltas.Add(delta);
        // Debug.WriteLine($"✅ Match at angle {adjustedAngleKey / 10f:F1}°, Δoffset = {delta}");
        _previousInferredDistances[rawAngleKey] = inferredDistance; // update last seen distance
      }

      //Debug.WriteLine($"🧮 Matched {deltas.Count} points");

      if (deltas.Count < minMatchesRequired)
      {
        Debug.WriteLine($"❌ Not enough matches ({deltas.Count}), skipping update.");
        return Vector2.Zero;
      }

      // Median/MAD filter
      Vector2 medianOffset = ComputeStableOffsetWithMAD(deltas, maxOffsetPerUpdate);

      if (medianOffset.Length() > maxOffsetPerUpdate)
      {
        medianOffset = Vector2.Normalize(medianOffset) * maxOffsetPerUpdate;
        Debug.WriteLine($"⚠️ Clamped offset to {medianOffset}");
      }

      Vector2 smoothedOffset = Vector2.Lerp(_previousOffset, medianOffset, smoothingFactor);

      if (smoothedOffset.Length() < minOffsetThreshold)
      {
        Debug.WriteLine($"🛑 Offset below threshold: {smoothedOffset.Length():F2}mm — skipping update.");
        return Vector2.Zero;
      }

      _previousOffset = smoothedOffset;
      Debug.WriteLine($"📐 Smoothed offset = {smoothedOffset}");
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
    Dictionary<int, List<(Tile realTile, Tile inferredTile)>> EstimateCurrentRingVectorsFromMapPoints(List<MapPoint> points)
    {
      var result = new Dictionary<int, List<(Tile, Tile)>>();

      foreach (var p in points)
      {
        if (!p.IsInferredRingPoint || p.InferredBy == null) continue;

        int angleKey = (int)(p.Angle * 10); // Use first decimal (0–3599)

        // Get tiles from global position
        Tile inferredTile = _gridManager.GetTileAtGlobalCoordinates(p.GlobalX, p.GlobalY);
        Tile realTile = _gridManager.GetTileAtGlobalCoordinates(p.InferredBy.GlobalX, p.InferredBy.GlobalY);

        if (inferredTile != null && realTile != null)
        {
          if (!result.TryGetValue(angleKey, out var pairList))
          {
            pairList = new List<(Tile, Tile)>();
            result[angleKey] = pairList;
          }

          pairList.Add((realTile, inferredTile));
        }
      }

      return result;
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
          Vector2 devicePosAtHit = _device._devicePosition;

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
          if (!isRingPoint)
          {
            float inferredRingX = 3500 * MathF.Cos(adjustedAngle);
            float inferredRingY = 3500 * MathF.Sin(adjustedAngle);
            float inferredGlobalX = devicePosAtHit.X + inferredRingX;
            float inferredGlobalY = devicePosAtHit.Y + inferredRingY;

            MapPoint inferredRingPoint = new MapPoint(
                inferredRingX, inferredRingY,
                dp.Angle, 3500,
                adjustedAngle, rawRadians,
                dp.Quality,
                inferredGlobalX, inferredGlobalY,
                devicePosAtHit,
                yawRadians,
                true
            );
            int tileSize = 10;
            Vector2 tilePos = new(MathF.Floor(inferredGlobalX / tileSize) * tileSize, MathF.Floor(inferredGlobalX / tileSize) * tileSize);
            Vector2 tileCenter = tilePos + new Vector2(tileSize / 2f, tileSize / 2f);
            inferredRingPoint.IsInferredRingPoint = true;
            inferredRingPoint.InferredBy = mapPoint;
            inferredRingPoint.EqTileGlobalCenter = tileCenter;
            addedPoints.Add(inferredRingPoint);
          }
        }
      }
      AddedPoints = addedPoints;
    }




  }
}