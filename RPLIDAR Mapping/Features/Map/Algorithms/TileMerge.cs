using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RPLIDAR_Mapping.Features;
using RPLIDAR_Mapping.Features.Map.GridModel;
using System.Diagnostics;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Utilities;
using static RPLIDAR_Mapping.Models.TileCluster;


namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class TileMerge
  {

    public List<(Rectangle, float, bool)> _mergedRectangles { get; private set; } = new();
    public List<LineSegment> _mergedLines { get; set; } = new();
    //public List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians,  bool IsPermanent)> _mergedLines { get; set; } = new();
    public List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians, bool IsPermanent)> _inferredLines { get; private set; } = new();
    public List<(Vector2 Start, Vector2 End, float AngleDegrees, float AngleRadians, bool IsPermanent)> _previousMergedLines { get; private set; } = new();
    public List<Rectangle> ClusterBoundingRectangles { get; private set; } = new();
    private List<TileCluster> UpdatedClusters = new();
    public List<TileCluster> TileClusters = new();
    public List<TileCluster> PreviousTileClusters = new();
    public Map _map { get; set; }
    public int MergeFrequency = 20;
    public int MinMergedTileSize = 50;
    public int mergeTileRadius = 3;
    public int MinTileClusterSize = 10;
    public int MinLargeFeaturesLineLength = 100;
    public float TileMergeThreshold = 50;
    public bool ComputeMergedTiles = true;
    public bool ComputeMergedLines = true;
    public bool ComputeMergedRectangles = false;
    public bool DrawMergedTiles = false;
    public bool DrawMergedLines = true;
    private int MergeTilesFrameCounter = 0;
    public float PointClusterMergeDistance = 200f;
    private int _tileSize;
    public int MinPointQuality = 0;
    public float MinPointDistance = 0;
    public bool IsUpdated = false;
    private bool TilesMerged = false;
    public bool LinesDetected = false;

    private HashSet<Tile> _trustedTiles;
    private Dictionary<Vector2, HashSet<Tile>> clusterLookup = new();

    private Dictionary<(int, int), Tile> TileLookup = new();
    private List<Vector2> _previousClusterCenters = new();
    public Vector2 _estimatedPosition { get; private set; } = Vector2.Zero;

    public Device _device { get; set; }
    public MergeState CurrentState { get; set; } = MergeState.Idle;
    public enum MergeState
    {
      Idle,              // Waiting, nothing computed yet
      NewPointsAdded,
      ClusteringTiles,    // Merging tiles
      MergingClusters,
      UpdatingClusters,
      ComputingLines,    // Detecting lines from merged tiles
      ComputingRects,    // Merging rectangles from lines
      SplittingClusters,
      ComputeClusterShift,
      ReadyToDraw        // All computations complete, ready to render
    }

    public TileMerge() 
    {


    }

    public bool Update()
    {
      IsUpdated = false;
      _tileSize = 10;
      switch (CurrentState)
      {
        case MergeState.Idle:

          break;
        case MergeState.NewPointsAdded:
          _previousClusterCenters = TileClusters
          //.Where(c => c.Tiles.Count >= 10) // ignore tiny clusters
          .Select(c => c.Center)
          .ToList();
          CurrentState = ComputeMergedLines ? MergeState.ClusteringTiles : MergeState.ReadyToDraw;
          break;

        case MergeState.ClusteringTiles:
          // Merge trusted tiles into clusters
          ClusterTrustedTiles(_map.NewTiles);
          CurrentState = ComputeMergedLines ? MergeState.MergingClusters : MergeState.ReadyToDraw;
          break;

        case MergeState.MergingClusters:
          // Merge clusters
          int maxSearchDistance = mergeTileRadius * _tileSize;
          FastMergeOverlappingClusters(UpdatedClusters, maxSearchDistance);
          CurrentState = ComputeMergedLines ? MergeState.ComputingRects : MergeState.ReadyToDraw;
          break;
        case MergeState.ComputingRects:
          // Update clusters
          foreach (TileCluster cluster in TileClusters)
          {
            cluster.UpdateBoundingBox();
          }

          CurrentState = ComputeMergedLines ? MergeState.ComputingLines : MergeState.ReadyToDraw;
          break;

        case MergeState.ComputingLines:
          // Process line detection from merged tiles
          foreach (TileCluster cluster in TileClusters)
          {
            cluster.UpdateFeatureLine();
          }
          CurrentState = ComputeMergedRectangles ? MergeState.ReadyToDraw : MergeState.ReadyToDraw;
          break;

        case MergeState.SplittingClusters:
          {

            SplitClusters();
            CurrentState = MergeState.ComputeClusterShift;
            break;
          }
        case MergeState.ComputeClusterShift:
          {

            ComputeClusterShift();
            CurrentState = MergeState.ReadyToDraw;
            break;
          }


        case MergeState.ReadyToDraw:
          // Computation is complete; tiles and lines are ready for rendering.
          // Reset state when ComputeMergedTiles is disabled
          CurrentState = MergeState.Idle;
          IsUpdated = true;          
          break;
      }

      return IsUpdated;
    }


    private void ComputeClusterShift()
    {
      int maxDistanceDrift = 50;
      Vector2 devicePos = UtilityProvider.Device._devicePosition;

      foreach (var cluster in TileClusters)
      {
        Vector2 toCenterNow = cluster.Center - devicePos;
        float angleNow = MathF.Atan2(toCenterNow.Y, toCenterNow.X);
        float distNow = toCenterNow.Length();

        float angleDelta = Utility.NormalizeAngle(angleNow - cluster.ExpectedAngle);
        float distDelta = distNow - cluster.ExpectedDistance;

        Debug.WriteLine("🔍 Cluster Shift Check:");
        Debug.WriteLine($"• Cluster Center        : {cluster.Center}");
        Debug.WriteLine($"• Device Position       : {devicePos}");
        Debug.WriteLine($"• Expected Angle        : {MathHelper.ToDegrees(cluster.ExpectedAngle):0.00}°");
        Debug.WriteLine($"• Current Angle         : {MathHelper.ToDegrees(angleNow):0.00}°");
        Debug.WriteLine($"• Δ Angle               : {MathHelper.ToDegrees(angleDelta):0.00}°");
        Debug.WriteLine($"• Expected Distance     : {cluster.ExpectedDistance:0.00}");
        Debug.WriteLine($"• Current Distance      : {distNow:0.00}");
        Debug.WriteLine($"• Δ Distance            : {distDelta:0.00}");
        Debug.WriteLine($"• Allowed Angle Span    : {MathHelper.ToDegrees(cluster.ExpectedSpan / 2f):0.00}°");
        Debug.WriteLine($"• Max Distance Drift    : {maxDistanceDrift}");

        if (Math.Abs(angleDelta) > cluster.ExpectedSpan / 2f ||
            Math.Abs(distDelta) > maxDistanceDrift)
        {
          Debug.WriteLine("⚠️  Cluster OUTSIDE sight cone — possible movement.");
        } else
        {
          Debug.WriteLine("✅ Cluster within expected view.");
        }

        Debug.WriteLine(""); // Empty line for readability
      }
    }

    public void ClearMergedObjects()
    {
      _mergedRectangles.Clear();
    }

    private void ClusterTrustedTiles(HashSet<Tile> newTiles)
    {
      PreviousTileClusters = TileClusters.Select(cluster => cluster.Clone()).ToList();
      List<TileCluster> updatedClusters = new();
      HashSet<Tile> visited = new();
      int maxSearchDistance = mergeTileRadius * _tileSize;

      // **Step 1: Remove outdated clusters**
      // **✅ Step 1: Remove Empty Clusters & Their Lines**
      TileClusters.RemoveAll(cluster =>
      {
        if (cluster.Tiles.Count == 0)
        {
          // ✅ Find and remove any merged line associated with this cluster
          _mergedLines.RemoveAll(line => line.ParentCluster == cluster);
          return true; // ✅ Remove the cluster
        }
        return false;
      });

      // **Step 2: Assign each tile to an existing cluster before creating a new one**
      foreach (Tile tile in newTiles)
      {
        if (!visited.Add(tile)) continue;

        // **Find the nearest cluster within search distance**
        TileCluster? nearestCluster = FindNearbyCluster(tile, maxSearchDistance);
        if (nearestCluster != null)
        {
          nearestCluster.AddTile(tile);
          tile.Cluster = nearestCluster;
          updatedClusters.Add(nearestCluster);
        } else
        {
          // **Create a new cluster only if no nearby cluster is found**
          TileCluster newCluster = new TileCluster();
          newCluster.AddTile(tile);
          tile.Cluster = newCluster;
          TileClusters.Add(newCluster);
          updatedClusters.Add(newCluster);
        }
      }
      UpdatedClusters = updatedClusters;
      // **Step 3: Merge overlapping clusters**

    }

    private TileCluster? FindNearbyCluster(Tile tile, float maxDistance)
    {
      TileCluster? bestCluster = null;
      float bestDistance = maxDistance;

      foreach (var cluster in TileClusters)
      {
        // Find the closest point on the cluster's bounding box
        Vector2 closestPoint = GetClosestPointOnBounds(tile.GlobalCenter, cluster.Bounds);
        float distance = Vector2.Distance(tile.GlobalCenter, closestPoint);

        if (distance < bestDistance)
        {
          bestCluster = cluster;
          bestDistance = distance;
        }
      }

      return bestCluster;
    }
    private Vector2 GetClosestPointOnBounds(Vector2 point, Rectangle bounds)
    {
      float closestX = Math.Clamp(point.X, bounds.Left, bounds.Right);
      float closestY = Math.Clamp(point.Y, bounds.Top, bounds.Bottom);
      return new Vector2(closestX, closestY);
    }


    private void FastMergeOverlappingClusters(List<TileCluster> updatedClusters, float maxDistance)
    {
      HashSet<TileCluster> toRemove = new();

      foreach (var cluster in updatedClusters)
      {
        foreach (var otherCluster in TileClusters)
        {
          if (cluster == otherCluster || toRemove.Contains(otherCluster)) continue;

          if (ClustersAreClose(cluster, otherCluster, maxDistance))
          {
            cluster.MergeWith(otherCluster);
            toRemove.Add(otherCluster);
          }
        }
      }

      TileClusters.RemoveAll(c => toRemove.Contains(c));
    }
    private void SplitClusters()
    {
      int maxSearchDistance = mergeTileRadius * _tileSize;
      List<TileCluster> toRemove = new();
      List<TileCluster> toAdd = new();

      foreach (var cluster in TileClusters)
      {
        var split = cluster.SplitDisconnectedClusters(maxSearchDistance);

        if (split.Count > 1)
        {
          toRemove.Add(cluster);
          toAdd.AddRange(split);

          foreach (var newCluster in split)
          {
            foreach (var tile in newCluster.Tiles)
              tile.Cluster = newCluster;
          }
        }
      }

      TileClusters.RemoveAll(c => toRemove.Contains(c));
      TileClusters.AddRange(toAdd);
    }


    private bool ClustersAreClose(TileCluster clusterA, TileCluster clusterB, float maxDistance)
    {
      // 🔥 First quick check: If bounding boxes are already overlapping
      if (clusterA.Bounds.Intersects(clusterB.Bounds))
        return true; // Already touching

      // 🔥 Find closest points on each cluster's bounding box
      Vector2 closestPointA = GetClosestPointOnBounds(clusterA.Center, clusterB.Bounds);
      Vector2 closestPointB = GetClosestPointOnBounds(clusterB.Center, clusterA.Bounds);

      // 🔥 Measure the actual distance
      float actualDistance = Vector2.Distance(closestPointA, closestPointB);

      return actualDistance <= maxDistance;
    }
    public List<Tile> GetConnectedNeighbors(Tile tile, float connectionDistance = 15f)
    {
      List<Tile> neighbors = new();

      if (tile.Cluster == null)
        return neighbors;

      foreach (var other in tile.Cluster.Tiles)
      {
        if (other == tile)
          continue;

        float dist = Vector2.Distance(tile.GlobalCenter, other.GlobalCenter);
        if (dist <= connectionDistance)
        {
          neighbors.Add(other);
        }
      }

      return neighbors;
    }




    // 🔹 Finds nearby clusters using **spatial lookup**

    public void DetectLargeFeatures()
    {
      _mergedLines.Clear();

      foreach (var cluster in TileClusters)
      {
        cluster.ComputeFeatureLine();
        if (cluster.FeatureLine != null)
        {
          _mergedLines.Add(cluster.FeatureLine);
        }
      }
    }
    public void AdjustClustersForMovement(Vector2 movementOffset, float rotationOffset)
    {
      foreach (var cluster in TileClusters)
      {
        cluster.AdjustForMovement(movementOffset, rotationOffset);
      }
    }


    private bool LineIntersectsCluster(LineSegment line, TileCluster cluster)
    {
      foreach (var tile in cluster.Tiles)
      {
        if (Vector2.Distance(line.Start, tile.GlobalCenter) < 50f ||
            Vector2.Distance(line.End, tile.GlobalCenter) < 50f)
        {
          return true; // ✅ Line already exists for this cluster
        }
      }
      return false;
    }


    //private void ComputeDeviceMovement(List<(Vector2 Start, Vector2 End, float Angle, bool IsPermanent)> oldLines,
    //                               List<(Vector2 Start, Vector2 End, float Angle, bool IsPermanent)> newLines)
    //{
    //  if (oldLines.Count == 0 || newLines.Count == 0)
    //  {
    //    Debug.WriteLine("ComputeDeviceMovement: No lines to compare.");
    //    return;
    //  }

    //  List<Vector2> shifts = new();
    //  List<float> angleChanges = new();

    //  int comparisonCount = Math.Min(oldLines.Count, newLines.Count);
    //  Debug.WriteLine($"Comparing {comparisonCount} old lines with {newLines.Count} new lines.");

    //  for (int i = 0; i < comparisonCount; i++)
    //  {
    //    var oldLine = oldLines[i];
    //    var newLine = newLines[i];

    //    Vector2 oldMidpoint = (oldLine.Start + oldLine.End) / 2;
    //    Vector2 newMidpoint = (newLine.Start + newLine.End) / 2;

    //    Vector2 shift = newMidpoint - oldMidpoint;
    //    shifts.Add(shift);

    //    float angleChange = newLine.Angle - oldLine.Angle;
    //    angleChanges.Add(angleChange);

    //    Debug.WriteLine($"Line {i}: Old Midpoint: {oldMidpoint}, New Midpoint: {newMidpoint}, Shift: {shift}, Angle Change: {angleChange}");
    //  }

    //  // Compute the average shift (translation)
    //  Vector2 avgShift = shifts.Aggregate(Vector2.Zero, (sum, v) => sum + v) / shifts.Count;
    //  float avgRotation = angleChanges.Sum() / angleChanges.Count;

    //  Debug.WriteLine($"Computed Shift: {avgShift}, Computed Rotation: {avgRotation}");

    //  if (avgShift.Length() > 0.1f) // Ignore tiny shifts to avoid noise
    //  {
    //    _device.SetDevicePosition(_device._devicePosition + avgShift);
    //    Debug.WriteLine($"New Device Position: {_device._devicePosition}");
    //  } else
    //  {
    //    Debug.WriteLine("No significant shift detected.");
    //  }

    //  //if (Math.Abs(avgRotation) > 0.01f)
    //  //{
    //  //  _device.SetEstimatedRotation(_device._deviceRotation + avgRotation);
    //  //  Debug.WriteLine($"New Device Rotation: {_device._deviceRotation}");
    //  //}
    //}


    //private bool IsClusterStraight(List<Vector2> points, Vector2 direction, Vector2 centroid, float baseDeviation)
    //{
    //  foreach (var point in points)
    //  {
        
    //    float distanceFromLidar = Vector2.Distance(_device._devicePosition, point); // Assuming LiDAR is at (0,0)
    //    float allowedDeviation = baseDeviation + (distanceFromLidar * 0.1f); // Increase tolerance for farther points

    //    float projectedDistance = Math.Abs(Vector2.Dot(point - centroid, new Vector2(-direction.Y, direction.X))); // Perpendicular distance
    //    if (projectedDistance > allowedDeviation)
    //    {
    //      return false; // Too much deviation, not a straight cluster
    //    }
    //  }
    //  return true;
    //}


    //private float ComputeDominantLidarAngle(List<float> lidarAngles)
    //{
    //  if (lidarAngles.Count < 3) return 0f; // Avoid unstable calculations

    //  float sumSin = 0, sumCos = 0, sumAngles = 0;

    //  foreach (float angle in lidarAngles)
    //  {
    //    float normalizedAngle = angle % 360;
    //    float radians = MathHelper.ToRadians(normalizedAngle);

    //    sumSin += MathF.Sin(radians);
    //    sumCos += MathF.Cos(radians);

    //    //  Handle angle wrapping inside this loop
    //    if (normalizedAngle < 90 && sumAngles / lidarAngles.Count > 270)
    //      normalizedAngle += 360;

    //    sumAngles += normalizedAngle;
    //  }

    //  if (sumSin == 0 && sumCos == 0) return 0f; // Prevent division errors

    //  float avgRadians = MathF.Atan2(sumSin, sumCos);
    //  float avgDegrees = MathHelper.ToDegrees(avgRadians) % 360; // Normalize to 0-360

    //  //  Use the weighted average of angles to stabilize results
    //  avgDegrees = sumAngles / lidarAngles.Count;
    //  avgDegrees = avgDegrees % 360; // Normalize again

    //  //  Round to the nearest 5 degrees for stability
    //  avgDegrees = MathF.Round(avgDegrees / 5) * 5;

    //  return MathHelper.ToRadians(avgDegrees);
    //}


    //private (Vector2 Direction, Vector2 Centroid) ComputePCA(List<Vector2> points)
    //{
    //  if (points.Count < 2) return (Vector2.UnitX, points[0]); // Default if not enough points

    //  // Compute centroid
    //  Vector2 centroid = new Vector2(points.Average(p => p.X), points.Average(p => p.Y));

    //  // Compute covariance matrix
    //  float sumXX = 0, sumXY = 0, sumYY = 0;
    //  foreach (var p in points)
    //  {
    //    float dx = p.X - centroid.X;
    //    float dy = p.Y - centroid.Y;
    //    sumXX += dx * dx;
    //    sumXY += dx * dy;
    //    sumYY += dy * dy;
    //  }

    //  // Solve for the eigenvector of the dominant eigenvalue
    //  float trace = sumXX + sumYY;
    //  float determinant = (sumXX * sumYY) - (sumXY * sumXY);
    //  float eigenvalue = (trace + MathF.Sqrt(trace * trace - 4 * determinant)) / 2;

    //  Vector2 direction = new Vector2(sumXY, eigenvalue - sumXX);
    //  direction.Normalize();

    //  return (direction, centroid);
    //}

  }
}
