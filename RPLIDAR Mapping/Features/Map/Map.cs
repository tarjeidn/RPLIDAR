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
          Vector2 simOffset = EstimateOffsetAdaptive(_pointsBuffer, _device._devicePosition);
          _device._devicePosition += simOffset;

          _currentState = MapUpdateState.EstimateDevicePosition;
          break;

        case MapUpdateState.EstimateDevicePosition:
          {
            Vector2 offset = MotionEstimator.EstimateDeviceOffsetFromAngleHistory(_pointsBuffer);
            _device._devicePosition += offset;
            _currentState = MapUpdateState.AddingNewPoints;
            break;
          }


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
            LinkNearestNeighborsByProximity(NewTiles.ToList());
            _currentState = MapUpdateState.UpdatingTiles;
            NewTiles.Clear(); 
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



    public Vector2 EstimateOffsetAdaptive(List<DataPoint> dplist, Vector2 guessedDevicePosition)
    {
      float tileSize = 10f;
      int initialSearchRadius = 4;     // or 5
      int extendedSearchRadius = 7;    // or 8
      float maxOffsetLength = 20f;
      float allowedNeighborError = 0.3f; // tighter
      float minMatchRatio = 0.3f;        // higher confidence
      float minOffsetMagnitude = 0.1f;
      float maxAcceptableError = 0.7f;   // more picky
      var originalNeighbors = new Dictionary<Tile, (Tile Left, Tile Right, Tile Top, Tile Bottom)>();
      foreach (var tile in UtilityProvider.Map._gridManager.GetAllTrustedTiles())
      {
        originalNeighbors[tile] = (
            tile.LeftAngularNeighbor,
            tile.RightAngularNeighbor,
            tile.TopAngularNeighbor,
            tile.BottomAngularNeighbor
        );
      }
      // Step 1: Run initial sweep
      var (offset, matchCount, neighborError) = RunOffsetSweep(
          dplist, guessedDevicePosition, initialSearchRadius,
          tileSize, maxOffsetLength, allowedNeighborError, minMatchRatio, originalNeighbors
      );

      //Debug.WriteLine($"🔍 Initial offset = {offset}, matchCount = {matchCount}, error = {neighborError:F2}");

      if (matchCount == 0 || offset.Length() < minOffsetMagnitude || neighborError > maxAcceptableError)
      {
        //Debug.WriteLine($"⚠️ Initial sweep too noisy. Triggering fallback sweep.");

        // Step 2: Fallback extended sweep
        var (fallbackOffset, fallbackCount, fallbackError) = RunOffsetSweep(
            dplist, guessedDevicePosition, extendedSearchRadius,
            tileSize, maxOffsetLength, allowedNeighborError, minMatchRatio * 0.8f, // slightly relaxed
            originalNeighbors
        );

        if (fallbackCount > 0 && fallbackOffset.Length() >= minOffsetMagnitude && fallbackError <= maxAcceptableError * 2)
        {
          //Debug.WriteLine($"✅ Fallback successful: offset = {fallbackOffset}, error = {fallbackError:F2}");
          return fallbackOffset;
        }

        //Debug.WriteLine("🛑 Fallback failed — returning zero offset.");
        return Vector2.Zero;
      }
      //Debug.WriteLine($"✅ Final offset applied: {offset}");
      return offset;
    }
    private (Vector2 offset, int matchCount, float avgNeighborError) RunOffsetSweep(
      List<DataPoint> dplist,
      Vector2 guessedDevicePosition,
      int searchRadius,
      float tileSize,
      float maxOffsetLength,
      float allowedNeighborError,
      float minMatchRatio,
      Dictionary<Tile, (Tile Left, Tile Right, Tile Top, Tile Bottom)> originalLinks)
    {
      float stepSize = tileSize / 2f; // 5mm if tileSize = 10
      List<Vector2> candidateOffsets = new();
      float range = searchRadius * tileSize;

      for (float dx = -range; dx <= range; dx += stepSize)
      {
        for (float dy = -range; dy <= range; dy += stepSize)
        {
          candidateOffsets.Add(new Vector2(dx, dy));
        }
      }

      Vector2 bestOffset = Vector2.Zero;
      int bestMatchCount = 0;
      List<Vector2> bestMatchedDeltas = new();
      float bestAvgNeighborError = float.MaxValue;
      float bestAvgAlignmentScore = float.MaxValue;

      foreach (Vector2 candidate in candidateOffsets)
      {
        int matchCount = 0;
        List<Vector2> deltas = new();
        float totalNeighborError = 0f;
        int neighborComparisons = 0;
        float alignmentScoreSum = 0f;
        int alignmentSamples = 0;

        foreach (DataPoint dp in dplist)
        {
          if (dp.Distance >= 3500) continue;

          float yawRadians = MathHelper.ToRadians(dp.Yaw);
          float adjustedAngle = MathHelper.ToRadians(dp.Angle) + yawRadians;

          float hitX = guessedDevicePosition.X + dp.Distance * MathF.Cos(adjustedAngle);
          float hitY = guessedDevicePosition.Y + dp.Distance * MathF.Sin(adjustedAngle);
          Vector2 inferredGlobal = new(hitX, hitY);
          Vector2 shiftedGlobal = inferredGlobal + candidate;

          Tile tile = UtilityProvider.Map._gridManager.GetTileAtGlobalCoordinates(shiftedGlobal.X, shiftedGlobal.Y);
          if (tile != null && tile._lastLIDARpoint != null)
          {
            var oldPoint = tile._lastLIDARpoint;

            Vector2 newDir = new(MathF.Cos(adjustedAngle), MathF.Sin(adjustedAngle));
            Vector2 estimatedNewDevicePos = oldPoint.EqTileGlobalCenter - dp.Distance * newDir;
            Vector2 delta = estimatedNewDevicePos - guessedDevicePosition;

            if (delta.Length() < maxOffsetLength)
            {
              float alignment = ComputeAlignmentScore(candidate, tile);
              alignmentScoreSum += alignment;
              alignmentSamples++;

              deltas.Add(delta);
              matchCount++;
              if (originalLinks.TryGetValue(tile, out var oldLinks))
              {
                int comparisons = 0;
                float mismatchScore = 0;

                if (oldLinks.Left != null)
                {
                  comparisons++;
                  if (tile.LeftAngularNeighbor != oldLinks.Left) mismatchScore += 1f;
                }
                if (oldLinks.Right != null)
                {
                  comparisons++;
                  if (tile.RightAngularNeighbor != oldLinks.Right) mismatchScore += 1f;
                }
                if (oldLinks.Top != null)
                {
                  comparisons++;
                  if (tile.TopAngularNeighbor != oldLinks.Top) mismatchScore += 1f;
                }
                if (oldLinks.Bottom != null)
                {
                  comparisons++;
                  if (tile.BottomAngularNeighbor != oldLinks.Bottom) mismatchScore += 1f;
                }

                totalNeighborError += mismatchScore;
                neighborComparisons += comparisons;
              }

            }
          }
        }

        int totalPoints = dplist.Count(dp => dp.Distance < 3500);
        int minValidMatches = Math.Max(10, (int)(totalPoints * minMatchRatio));
        if (deltas.Count < minValidMatches)
          continue;

        float avgAlignmentScore = alignmentSamples > 0 ? alignmentScoreSum / alignmentSamples : 1f;

        var simulatedTiles = NeighborConsistencyChecker.SimulateNewTilePlacement(dplist, guessedDevicePosition + candidate);
        var simulatedLinks = NeighborConsistencyChecker.BuildNeighborLinkSnapshot(simulatedTiles);
        var result = NeighborConsistencyChecker.CompareNeighborLinks(originalLinks, simulatedLinks);
        float consistencyScore = result.ConsistencyScore;

        // 📊 Adjust thresholds based on match strength
        bool reject = false;

        // 🚫 Reject if both are bad
        if (consistencyScore < 0.4f && avgAlignmentScore > 0.8f)
        {
          reject = true;
        } else if (consistencyScore < 0.5f && avgAlignmentScore > 0.9f)
        {
          reject = true;
        }
          // ✅ Allow high alignment if structure is strong
          else if (avgAlignmentScore >= 0.98f && consistencyScore >= 0.85f)
        {
          reject = false;
        }
          // ✅ Allow low consistency if alignment is orthogonal (indicates structure break)
          else if (consistencyScore >= 0.3f && avgAlignmentScore < 0.65f)
        {
          reject = false;
        }
        // Accept perfect alignment if consistency is strong and enough points match
        else if (avgAlignmentScore >= 0.99f)
        {
          if (consistencyScore >= 0.8f && matchCount >= 40)
            reject = false;
          else
            reject = true;
        }

        //if (reject)
        //{
        //  Debug.WriteLine($"❌ Offset {candidate} rejected.");
        //  Debug.WriteLine($"⚠️ matchCount={matchCount}, deltas={deltas.Count}, align={avgAlignmentScore:F2}, consistency={consistencyScore:F2}");
        //} else
        //{
          //Debug.WriteLine($"✅ Offset {candidate} accepted.");
        //}




        float avgNeighborError = neighborComparisons > 0 ? totalNeighborError / neighborComparisons : float.MaxValue;

        if (matchCount > bestMatchCount || (matchCount == bestMatchCount && avgAlignmentScore < bestAvgAlignmentScore))
        {
          bestOffset = candidate;
          bestMatchCount = matchCount;
          bestMatchedDeltas = deltas;
          bestAvgNeighborError = avgNeighborError;
          bestAvgAlignmentScore = avgAlignmentScore;
        }
      }

      if (bestMatchCount == 0 || bestMatchedDeltas.Count == 0)
        return (Vector2.Zero, 0, float.MaxValue);
      //Debug.WriteLine($"🏁 Final chosen offset = {bestOffset}, matches = {bestMatchCount}, neighborErr = {bestAvgNeighborError:F2}");

      return (bestOffset, bestMatchCount, bestAvgNeighborError);
    }



    private float ComputeAlignmentScore(Vector2 offsetDirection, Tile tile)
    {
      if (offsetDirection.LengthSquared() < 0.0001f)
        return 0f; // No movement = no alignment

      Vector2 normalizedOffset = Vector2.Normalize(offsetDirection);
      List<Vector2> neighborDirections = new();

      if (tile.LeftAngularNeighbor != null)
        neighborDirections.Add(Vector2.Normalize(tile.LeftAngularNeighbor.GlobalCenter - tile.GlobalCenter));
      if (tile.RightAngularNeighbor != null)
        neighborDirections.Add(Vector2.Normalize(tile.RightAngularNeighbor.GlobalCenter - tile.GlobalCenter));
      if (tile.TopAngularNeighbor != null)
        neighborDirections.Add(Vector2.Normalize(tile.TopAngularNeighbor.GlobalCenter - tile.GlobalCenter));
      if (tile.BottomAngularNeighbor != null)
        neighborDirections.Add(Vector2.Normalize(tile.BottomAngularNeighbor.GlobalCenter - tile.GlobalCenter));

      if (neighborDirections.Count == 0)
        return 1f; // No neighbors = assume bad alignment (max penalty)

      float totalDot = 0f;
      foreach (var dir in neighborDirections)
        totalDot += MathF.Abs(Vector2.Dot(normalizedOffset, dir)); // Use abs to account for both directions

      float averageDot = totalDot / neighborDirections.Count;
      return averageDot; // Closer to 1 = high alignment, 0 = orthogonal
    }


    public void LinkNearestNeighborsByProximity(List<Tile> allTiles)
    {
      float maxSearchDistance = 100f; // mm
      float coneHalfAngleDegrees = 45f;

      foreach (Tile tile in allTiles)
      {
        Vector2 origin = tile.GlobalCenter;

        Tile leftBest = null, rightBest = null, topBest = null, bottomBest = null;
        float leftBestScore = float.MinValue;
        float rightBestScore = float.MinValue;
        float topBestScore = float.MinValue;
        float bottomBestScore = float.MinValue;

        foreach (Tile candidate in allTiles)
        {
          if (candidate == tile)
            continue;

          Vector2 toTarget = candidate.GlobalCenter - origin;
          float dist = toTarget.Length();
          if (dist > maxSearchDistance) continue;

          Vector2 dir = Vector2.Normalize(toTarget);

          float dotLeft = Vector2.Dot(dir, new Vector2(-1, 0));
          float dotRight = Vector2.Dot(dir, new Vector2(1, 0));
          float dotUp = Vector2.Dot(dir, new Vector2(0, -1));
          float dotDown = Vector2.Dot(dir, new Vector2(0, 1));

          // We favor closer + better aligned in direction
          float scoreLeft = dotLeft * (1f / dist);
          float scoreRight = dotRight * (1f / dist);
          float scoreUp = dotUp * (1f / dist);
          float scoreDown = dotDown * (1f / dist);

          if (dotLeft > MathF.Cos(MathHelper.ToRadians(coneHalfAngleDegrees)) && scoreLeft > leftBestScore)
          {
            leftBest = candidate;
            leftBestScore = scoreLeft;
          } else if (dotRight > MathF.Cos(MathHelper.ToRadians(coneHalfAngleDegrees)) && scoreRight > rightBestScore)
          {
            rightBest = candidate;
            rightBestScore = scoreRight;
          } else if (dotUp > MathF.Cos(MathHelper.ToRadians(coneHalfAngleDegrees)) && scoreUp > topBestScore)
          {
            topBest = candidate;
            topBestScore = scoreUp;
          } else if (dotDown > MathF.Cos(MathHelper.ToRadians(coneHalfAngleDegrees)) && scoreDown > bottomBestScore)
          {
            bottomBest = candidate;
            bottomBestScore = scoreDown;
          }
        }

        tile.LeftAngularNeighbor = leftBest;
        tile.RightAngularNeighbor = rightBest;
        tile.TopAngularNeighbor = topBest;
        tile.BottomAngularNeighbor = bottomBest;
      }
    }


    private bool IsInDirectionCone(Vector2 toTarget, Vector2 baseDir, float coneHalfAngleDegrees)
    {
      float dot = Vector2.Dot(Vector2.Normalize(toTarget), Vector2.Normalize(baseDir));
      float angle = MathF.Acos(Math.Clamp(dot, -1f, 1f)) * (180f / MathF.PI);
      return angle <= coneHalfAngleDegrees;
    }



    private void LinkAngularNeighbors()
    {
      var angleMap = UtilityProvider.Map.AngularTileMap;
      const int maxAngleKey = 3599;
      const int maxSearchSteps = 1800; // search up to half the circle

      foreach (var kvp in angleMap)
      {
        int key = kvp.Key;
        Tile tile = kvp.Value;

        tile.LeftAngularNeighbor = FindClosestAngleNeighbor(angleMap, key, direction: -1, maxSearchSteps);
        tile.RightAngularNeighbor = FindClosestAngleNeighbor(angleMap, key, direction: 1, maxSearchSteps);
      }
    }

    private Tile FindClosestAngleNeighbor(Dictionary<int, Tile> angleMap, int startKey, int direction, int maxSteps)
    {
      for (int step = 1; step <= maxSteps; step++)
      {
        int searchKey = (startKey + direction * step + 3600) % 3600;
        if (angleMap.TryGetValue(searchKey, out var neighbor))
          return neighbor;
      }
      return null;
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