using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static RPLIDAR_Mapping.Features.Map.GridModel.Tile;
using static RPLIDAR_Mapping.Features.Map.Map;


namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  /// <summary>
  /// Provides motion estimation logic for determining LiDAR device translation between scans using historical angle data and tile structures.
  /// </summary>
  public class MotionEstimator
  {
    private readonly Dictionary<int, DataPoint> _lastKnownPoints = new();
    private const float MaxDeltaThreshold = 20f;
    private const float MinValidMatches = 0.8f;
    private const float MinOffsetMagnitude = 2.0f;
    private Vector2 _lastOffset = Vector2.Zero;
    private const float LerpFactor = 0.2f;

    private readonly Queue<(Vector2 position, float time)> _positionHistory = new();
    private const int PositionHistoryLength = 10;


    public Vector2 LastEstimatedOffset = Vector2.Zero;
    /// <summary>
    /// Determines whether the device is currently moving slowly, based on the last estimated offset.
    /// </summary>
    public bool IsDeviceMovingSlowly()
    {
      const float MovementThreshold = 1.0f;
      return LastEstimatedOffset.Length() < MovementThreshold;
    }

    /// <summary>
    /// Estimates a gentle correction offset toward the average center of stable tile clusters.
    /// </summary>
    public static Vector2 EstimateCorrectionFromClusters(List<TileCluster> clusters, Vector2 devicePos)
    {
      var stableClusters = clusters.Where(c => c.Tiles.Count >= 10).ToList();
      if (stableClusters.Count == 0) return Vector2.Zero;

      Vector2 totalOffset = Vector2.Zero;
      foreach (var cluster in stableClusters)
        totalOffset += (cluster.Center - devicePos);

      Vector2 averageOffset = totalOffset / stableClusters.Count;
      const float CorrectionStrength = 0.05f;
      return averageOffset * CorrectionStrength;
    }

    /// <summary>
    /// Estimates the device offset by comparing angle-to-position history between current and previous LiDAR points.
    /// </summary>
    public Vector2 EstimateDeviceOffsetFromAngleHistory(List<DataPoint> currentBatch)
    {
      List<Vector2> deltas = new();
      float latestTimestamp = 0;

      foreach (var point in currentBatch)
      {
        float adjustedAngleDeg = point.Angle + point.Yaw;
        int angleKey = (int)(adjustedAngleDeg * 10f);

        if (angleKey == int.MinValue)
          angleKey = int.MinValue + 1;

        int angleKeyWindow = 2;
        for (int i = -angleKeyWindow; i <= angleKeyWindow; i++)
        {
          int testKey = angleKey + i;
          if (_lastKnownPoints.TryGetValue(testKey, out var previousPoint))
          {
            Vector2 oldRel = previousPoint.GlobalPosition - previousPoint.DevicePositionAtHit;
            Vector2 newRel = point.GlobalPosition - point.DevicePositionAtHit;
            Vector2 delta = oldRel - newRel;

            if (delta.Length() < MaxDeltaThreshold)
            {
              deltas.Add(delta);
              latestTimestamp = Math.Max(latestTimestamp, point.TimeStamp);
              break;
            }
          }
        }

        _lastKnownPoints[angleKey] = point;
      }

      if (deltas.Count < MinValidMatches) return Vector2.Zero;
      deltas = FilterOutliersMAD(deltas);
      if (deltas.Count < MinValidMatches) return Vector2.Zero;

      Vector2 offset = ComputeMedian(deltas);
      if (offset.Length() < MinOffsetMagnitude) return Vector2.Zero;

      _lastOffset = Vector2.Lerp(_lastOffset, offset, LerpFactor);
      return _lastOffset;
    }

    private List<Vector2> FilterOutliersMAD(List<Vector2> deltas, float madMultiplier = 2.5f)
    {
      if (deltas.Count == 0) return deltas;

      Vector2 median = ComputeMedian(deltas);
      var distances = deltas.Select(d => Vector2.Distance(d, median)).ToList();

      float mad = ComputeMedian(distances);
      float threshold = mad * madMultiplier;

      return deltas.Where(d => Vector2.Distance(d, median) < threshold).ToList();
    }

    private float ComputeMedian(List<float> values)
    {
      if (values == null || values.Count == 0) return 0f;

      var sorted = values.OrderBy(v => v).ToList();
      int mid = sorted.Count / 2;

      return sorted.Count % 2 == 0
          ? (sorted[mid - 1] + sorted[mid]) / 2f
          : sorted[mid];
    }

    private Vector2 ComputeMedian(List<Vector2> vectors)
    {
      if (vectors.Count == 0) return Vector2.Zero;

      var xs = vectors.Select(v => v.X).OrderBy(x => x).ToList();
      var ys = vectors.Select(v => v.Y).OrderBy(y => y).ToList();

      float medianX = xs[xs.Count / 2];
      float medianY = ys[ys.Count / 2];

      return new Vector2(medianX, medianY);
    }
    /// <summary>
    /// Runs an adaptive matching routine that sweeps candidate offsets and selects the best match.
    /// </summary>
    public Vector2 EstimateOffsetAdaptive(List<DataPoint> dplist, Vector2 guessedDevicePosition)
    {
      // Parameters
      float tileSize = 10f;
      int initialSearchRadius = 4;
      int extendedSearchRadius = 7;
      float maxOffsetLength = 20f;
      float allowedNeighborError = 0.3f;
      float minMatchRatio = 0.3f;
      float minOffsetMagnitude = 0.1f;
      float maxAcceptableError = 0.7f;

      var originalNeighbors = UtilityProvider.Map._gridManager.GetAllTrustedTiles()
          .ToDictionary(
              tile => tile,
              tile => (
                  tile.LeftAngularNeighbor,
                  tile.RightAngularNeighbor,
                  tile.TopAngularNeighbor,
                  tile.BottomAngularNeighbor
              ));

      var (offset, matchCount, neighborError) = RunOffsetSweep(
          dplist, guessedDevicePosition, initialSearchRadius,
          tileSize, maxOffsetLength, allowedNeighborError,
          minMatchRatio, originalNeighbors);

      if (matchCount == 0 || offset.Length() < minOffsetMagnitude || neighborError > maxAcceptableError)
      {
        var (fallbackOffset, fallbackCount, fallbackError) = RunOffsetSweep(
            dplist, guessedDevicePosition, extendedSearchRadius,
            tileSize, maxOffsetLength, allowedNeighborError,
            minMatchRatio * 0.8f, originalNeighbors);

        if (fallbackCount > 0 && fallbackOffset.Length() >= minOffsetMagnitude && fallbackError <= maxAcceptableError * 2)
          return fallbackOffset;

        return Vector2.Zero;
      }

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
                }
                else if (consistencyScore < 0.5f && avgAlignmentScore > 0.9f)
                {
                    reject = true;
                }
                //  Allow high alignment if structure is strong
                else if (avgAlignmentScore >= 0.98f && consistencyScore >= 0.85f)
                {
                    reject = false;
                }
                //  Allow low consistency if alignment is orthogonal (indicates structure break)
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
                //  Debug.WriteLine($" Offset {candidate} rejected.");
                //  Debug.WriteLine($" matchCount={matchCount}, deltas={deltas.Count}, align={avgAlignmentScore:F2}, consistency={consistencyScore:F2}");
                //} else
                //{
                //Debug.WriteLine($" Offset {candidate} accepted.");
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


    }

}
