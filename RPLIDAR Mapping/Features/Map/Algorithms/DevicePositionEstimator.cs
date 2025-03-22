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
using static RPLIDAR_Mapping.Features.Map.Map;


namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class DevicePositionEstimator
  {
    public static Vector2? EstimateOffset(
        List<DataPoint> newPoints,
        Dictionary<ObservationKey, Tile> lookup,
        Vector2 currentDevicePosition,
        float tileSize,
        GridManager gridManager,
        out int matched, out int mismatched)
    {
      List<Vector2> offsets = new();
      matched = 0;
      mismatched = 0;

      foreach (var point in newPoints)
      {
        var key = new ObservationKey(currentDevicePosition, point.Angle, point.Distance);
        if (Map.ObservationLookup.TryGetValue(key, out Tile oldTile))
        {
          matched++;

          Vector2 oldCenter = oldTile.GlobalCenter;

          // Recalculate where this point would land from current device position
          Vector2 offset = Utility.PolarToCartesian(point.Angle, point.Distance);
          Vector2 newGlobal = currentDevicePosition + offset;

          int newX = (int)(newGlobal.X / tileSize);
          int newY = (int)(newGlobal.Y / tileSize);

          Tile newTile = gridManager.GetTileAtGlobalCoordinates(newGlobal.X, newGlobal.Y);
          Vector2 newCenter = newTile.GlobalCenter;

          if ((int)(oldCenter.X / tileSize) != newX || (int)(oldCenter.Y / tileSize) != newY)
          {
            mismatched++;
            Vector2 movementOffset = oldCenter - newCenter;
            offsets.Add(movementOffset);
          }
        }
      }

      if (offsets.Count >= 10)
      {
        Vector2 sum = Vector2.Zero;
        foreach (var o in offsets) sum += o;
        return sum / offsets.Count;
      }

      return null; // Not enough evidence
    }
  }

  //public class DevicePositionEstimator
  //{
  //public Device _device { get; set; }
  //public Map _map { get; set; }
  //public Vector2 UpdatedPosition { get; set; } = Vector2.Zero;
  //public float UpdatedRotation { get; set; } = 0f;




  //private List<(Vector2 newCenter, Vector2 matchedCenter)> MatchClustersToStructures()
  //{
  //  List<(Vector2 newCenter, Vector2 matchedCenter)> matchedPairs = new();
  //  List<float> rotationDifferences = new();
  //  bool hasPermanentMatch = false; // ✅ Ensure at least one stable match

  //  foreach (var cluster in _map._tileMerge.TileClusters)
  //  {
  //    Vector2 clusterCenter = cluster.Center;
  //    Vector2 bestMatch = Vector2.Zero;
  //    float bestDistance = float.MaxValue;
  //    bool matchedToPermanentLine = false;

  //    // 🔹 Check against permanent lines first (higher priority)
  //    foreach (var line in _map.PermanentLines)
  //    {
  //      Vector2 closestLinePoint = GetClosestPointOnLineSegment(line.Start, line.End, clusterCenter);
  //      float distance = Vector2.Distance(clusterCenter, closestLinePoint);

  //      if (distance < bestDistance)
  //      {
  //        bestDistance = distance;
  //        bestMatch = closestLinePoint;
  //        matchedToPermanentLine = true;
  //        hasPermanentMatch = true; // ✅ Found a stable reference
  //      }
  //    }

  //    // 🔹 Only check previous clusters if no permanent line match is found
  //    if (!matchedToPermanentLine)
  //    {
  //      foreach (var previousCluster in AlgorithmProvider.TileMerge.PreviousTileClusters)
  //      {
  //        float distance = Vector2.Distance(clusterCenter, previousCluster.Center);

  //        if (distance < bestDistance && previousCluster.Bounds.Intersects(cluster.Bounds))
  //        {
  //          bestDistance = distance;
  //          bestMatch = previousCluster.Center;
  //        }
  //      }
  //    }

  //    // ✅ Apply stricter matching threshold (reduce noise)
  //    if (bestDistance < 250)
  //    {
  //      matchedPairs.Add((clusterCenter, bestMatch));

  //      // 🔹 Compute angle difference for rotation estimation
  //      float angleBefore = MathF.Atan2(clusterCenter.Y - _device._devicePosition.Y, clusterCenter.X - _device._devicePosition.X);
  //      float angleAfter = MathF.Atan2(bestMatch.Y - _device._devicePosition.Y, bestMatch.X - _device._devicePosition.X);
  //      float angleDifference = angleAfter - angleBefore;

  //      // Normalize angle difference to -π to π range
  //      angleDifference = MathF.Atan2(MathF.Sin(angleDifference), MathF.Cos(angleDifference));

  //      rotationDifferences.Add(angleDifference);
  //    }
  //  }

  //  // ✅ If no stable matches exist, **skip position update**
  //  if (!hasPermanentMatch && matchedPairs.Count < 3) // Require at least 3 cluster matches
  //  {
  //    return new List<(Vector2 newCenter, Vector2 matchedCenter)>();
  //  }

  //  // ✅ Use median rotation change (avoids noise)
  //  if (rotationDifferences.Count > 0)
  //  {
  //    rotationDifferences.Sort();
  //    float medianRotation = rotationDifferences[rotationDifferences.Count / 2];

  //    // Apply median rotation change
  //    _device._deviceOrientation += medianRotation;
  //  }

  //  return matchedPairs;
  //}


  //public Vector2 GetClosestPointOnLineSegment(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
  //{
  //  Vector2 lineDirection = lineEnd - lineStart;
  //  float lineLengthSquared = lineDirection.LengthSquared();

  //  // 🔥 If the line segment is just a single point
  //  if (lineLengthSquared == 0f)
  //  {
  //    return lineStart;
  //  }

  //  // 🔥 Compute the projection factor (t) of 'point' onto the infinite line
  //  float t = Vector2.Dot(point - lineStart, lineDirection) / lineLengthSquared;

  //  // 🔥 Clamp 't' to stay within the line segment bounds [0,1]
  //  t = Math.Clamp(t, 0f, 1f);

  //  // 🔥 Compute the closest point using interpolation
  //  return lineStart + t * lineDirection;
  //}

  //private (Vector2 translation, float rotation) ComputeTransformation(List<(Vector2 newPoint, Vector2 matchedPoint)> matchedPairs)
  //{
  //  if (matchedPairs.Count < 3) return (Vector2.Zero, 0f); // Not enough data to compute a meaningful transformation

  //  Vector2 centroidNew = Vector2.Zero;
  //  Vector2 centroidMatched = Vector2.Zero;

  //  foreach (var pair in matchedPairs)
  //  {
  //    centroidNew += pair.newPoint;
  //    centroidMatched += pair.matchedPoint;
  //  }

  //  centroidNew /= matchedPairs.Count;
  //  centroidMatched /= matchedPairs.Count;

  //  // 🔹 Compute rotation using least squares method
  //  float sumNum = 0, sumDenom = 0;
  //  foreach (var pair in matchedPairs)
  //  {
  //    Vector2 newRelative = pair.newPoint - centroidNew;
  //    Vector2 matchedRelative = pair.matchedPoint - centroidMatched;

  //    sumNum += newRelative.X * matchedRelative.Y - newRelative.Y * matchedRelative.X;
  //    sumDenom += newRelative.X * matchedRelative.X + newRelative.Y * matchedRelative.Y;
  //  }

  //  float rotation = MathF.Atan2(sumNum, sumDenom); // Rotation angle in radians
  //  Vector2 translation = centroidMatched - centroidNew;

  //  return (translation, rotation);
  //}
  //private List<(Vector2 previousClusterCenter, Vector2 currentClusterCenter)> MatchClustersToPreviousFrame()
  //{
  //  List<(Vector2 previousClusterCenter, Vector2 currentClusterCenter)> matchedPairs = new();

  //  foreach (var cluster in _map._tileMerge.TileClusters)
  //  {
  //    Vector2 clusterCenter = cluster.Center;
  //    Vector2 bestMatch = Vector2.Zero;
  //    float bestDistance = float.MaxValue;

  //    // 🔥 Find the best-matching previous cluster
  //    foreach (var previousCluster in _map._tileMerge.PreviousTileClusters)
  //    {
  //      float distance = Vector2.Distance(clusterCenter, previousCluster.Center);

  //      if (distance < bestDistance && previousCluster.Bounds.Intersects(cluster.Bounds))
  //      {
  //        bestDistance = distance;
  //        bestMatch = previousCluster.Center;
  //      }
  //    }

  //    if (bestDistance < 300) // Set a reasonable max distance threshold
  //    {
  //      matchedPairs.Add((bestMatch, clusterCenter));
  //    }
  //  }

  //  return matchedPairs;
  //}


  //public void UpdateDevicePosition()
  //{
  //  List<(Vector2 previousCenter, Vector2 newCenter)> matchedPairs = new();

  //  foreach (var cluster in _map._tileMerge.TileClusters)
  //  {
  //    if (!cluster.HasMoved) continue; // 🔥 Skip clusters that didn't move

  //    matchedPairs.Add((cluster.PreviousCenter, cluster.Center));
  //  }

  //  if (matchedPairs.Count < 2) return; // 🔥 Need at least 2 matches to determine movement

  //  var (translation, rotation) = ComputeTransformation(matchedPairs);

  //  if (translation.Length() > 0.1f || MathF.Abs(rotation) > 0.01f)
  //  {
  //    UpdatedPosition += translation;
  //    UpdatedRotation += rotation;
  //    _device._devicePosition += translation;
  //    _device._deviceOrientation += rotation;
  //  }
  //}






  //}
}
