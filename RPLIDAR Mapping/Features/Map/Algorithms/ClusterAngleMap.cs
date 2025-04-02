using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class ClusterAngleMap
  {
    private List<ClusterAngleRange> _angleRanges = new();
    private Vector2 _devicePosition;

    public ClusterAngleMap(Vector2 devicePosition, List<TileCluster> clusters)
    {
      _devicePosition = devicePosition;
      _angleRanges = ComputeClusterAngleRanges(devicePosition, clusters);
    }

    public void Update(Vector2 newDevicePosition, List<TileCluster> clusters)
    {
      _devicePosition = newDevicePosition;
      _angleRanges = ComputeClusterAngleRanges(newDevicePosition, clusters);
    }

    public TileCluster? GetExpectedCluster(float angle)
    {
      int angleInt = ((int)Math.Round(angle) + 360) % 360;
      foreach (var range in _angleRanges)
      {
        if (range.MinAngle <= range.MaxAngle)
        {
          if (angleInt >= range.MinAngle && angleInt <= range.MaxAngle)
            return range.Cluster;
        } else
        {
          // Handle wraparound
          if (angleInt >= range.MinAngle || angleInt <= range.MaxAngle)
            return range.Cluster;
        }
      }
      return null;
    }

    public Vector2 CompareExpectedToActual(List<DataPoint> newPoints, List<TileCluster> currentClusters)
    {
      const int MinValidMatches = 5;
      const float MaxOffsetLength = 50f;

      List<Vector2> offsets = new();
      int matchCount = 0;

      foreach (var point in newPoints)
      {
        int angleInt = ((int)Math.Round(point.Angle) + 360) % 360;
        TileCluster? expectedCluster = GetExpectedCluster(angleInt);

        // Skip if no cluster, tiny cluster, or 0-area
        if (expectedCluster == null ||
            expectedCluster.Tiles.Count < 2 ||
            expectedCluster.Bounds.Width < 5 || expectedCluster.Bounds.Height < 5)
        {
          continue;
        }

        // Find actual cluster that contains the point
        TileCluster? actualCluster = currentClusters
          .FirstOrDefault(c => c.Bounds.Contains(point.EqTileGlobalCenter.ToPoint()));

        if (actualCluster == null || actualCluster == expectedCluster)
          continue;

        matchCount++;
        Vector2 expected = expectedCluster.Center;
        Vector2 actual = actualCluster.Center;
        Vector2 offset = actual - expected;

        Debug.WriteLine($"→ Angle {angleInt}° | Expected: {expectedCluster.Bounds} | Actual: {actualCluster.Bounds} | Offset: {offset}");
        offsets.Add(offset);
      }

      if (offsets.Count < MinValidMatches)
      {
        Debug.WriteLine($"⚠ Not enough mismatch points ({offsets.Count}) to apply offset");
        return Vector2.Zero;
      }

      Vector2 avgOffset = offsets.Aggregate(Vector2.Zero, (a, b) => a + b) / offsets.Count;

      if (avgOffset.Length() > MaxOffsetLength)
      {
        Debug.WriteLine($"🚫 Offset too large ({avgOffset}), discarding");
        return Vector2.Zero;
      }

      Debug.WriteLine($"📌 Estimated Device Offset: {avgOffset} from {matchCount} mismatches");
      return avgOffset;
    }
  



    private List<ClusterAngleRange> ComputeClusterAngleRanges(Vector2 devicePos, List<TileCluster> clusters)
    {
      List<ClusterAngleRange> angleRanges = new();

      foreach (var cluster in clusters)
      {
        Rectangle bounds = cluster.Bounds;
        Vector2[] corners = new Vector2[]
        {
        new(bounds.Left, bounds.Top),
        new(bounds.Right, bounds.Top),
        new(bounds.Right, bounds.Bottom),
        new(bounds.Left, bounds.Bottom)
        };

        float min = 999f;
        float max = -999f;

        foreach (var corner in corners)
        {
          Vector2 dir = corner - devicePos;
          float angle = MathHelper.ToDegrees(MathF.Atan2(dir.Y, dir.X));
          angle = (angle + 360f) % 360f;

          if (angle < min) min = angle;
          if (angle > max) max = angle;
        }

        if (max - min > 180)
        {
          // Wraparound range
          angleRanges.Add(new ClusterAngleRange { Cluster = cluster, MinAngle = 0, MaxAngle = (int)min });
          angleRanges.Add(new ClusterAngleRange { Cluster = cluster, MinAngle = (int)max, MaxAngle = 359 });
        } else
        {
          angleRanges.Add(new ClusterAngleRange { Cluster = cluster, MinAngle = (int)min, MaxAngle = (int)max });
        }
      }

      return angleRanges;
    }
  }

  public struct ClusterAngleRange
  {
    public TileCluster Cluster;
    public int MinAngle;
    public int MaxAngle;
  }
}
