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

    public Vector2 _estimatedPosition { get; private set; } = Vector2.Zero;

    public Device _device { get; set; }
    public MergeState CurrentState { get; set; } = MergeState.Idle;
    public enum MergeState
    {
      Idle,              // Waiting, nothing computed yet
      NewPointsAdded,
      ComputingTiles,    // Merging tiles
      UpdatingClusters,
      ComputingLines,    // Detecting lines from merged tiles
      ComputingRects,    // Merging rectangles from lines
      RecalculatingPosition,
      ReadyToDraw        // All computations complete, ready to render
    }

    public TileMerge() 
    {


    }

    public bool Update()
    {
      IsUpdated = false;
      _tileSize = MapScaleManager.Instance.ScaledTileSizePixels;
      switch (CurrentState)
      {
        case MergeState.Idle:

          break;
        case MergeState.NewPointsAdded:
          CurrentState = ComputeMergedLines ? MergeState.ComputingTiles : MergeState.ReadyToDraw;
          break;

        case MergeState.ComputingTiles:
          // Perform tile merging logic
          // Merge trusted tiles into clusters
          //ClusterTrustedTiles(_map.GetDistributor()._GridManager.GetAllTrustedTiles());
          ClusterTrustedTiles(_map.NewTiles);
          //if (DrawMergedTiles) GetClusterBoundingBoxes();
          CurrentState = ComputeMergedLines ? MergeState.UpdatingClusters : MergeState.ReadyToDraw;
          break;
        case MergeState.UpdatingClusters:
          // Update clusters
          foreach (TileCluster cluster in TileClusters)
          {
            cluster.Update();
          }
          CurrentState = ComputeMergedLines ? MergeState.ComputingLines : MergeState.ReadyToDraw;
          break;

        case MergeState.ComputingLines:
          // Process line detection from merged tiles
          //DetectLinesFromUpdatedTiles();
          //DetectLargeFeatures();
          CurrentState = ComputeMergedRectangles ? MergeState.RecalculatingPosition : MergeState.ReadyToDraw;
          break;

        case MergeState.ComputingRects:
          // Process merging rectangles (if enabled)
          // MergeRectangles(_map.GetDistributor()._GridManager.GetAllTrustedTiles());
          MergeTilesFrameCounter = 0;
          CurrentState = MergeState.ReadyToDraw;
          break;

        case MergeState.RecalculatingPosition:
          // Process line detection from merged tiles
          //DetectLinesFromUpdatedTiles();
          //AlgorithmProvider.DevicePositionEstimator.Update(_mergedLines, _trustedTiles);
          //ComputeDeviceMovement(_previousMergedLines, _mergedLines);
          CurrentState = MergeState.ReadyToDraw;
          break;

        case MergeState.ReadyToDraw:
          // Computation is complete; tiles and lines are ready for rendering.
          // Reset state when ComputeMergedTiles is disabled
          CurrentState = MergeState.Idle;
          IsUpdated = true;          
          break;
      }

      return IsUpdated;
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

      // **Step 3: Merge overlapping clusters**
      FastMergeOverlappingClusters(updatedClusters, maxSearchDistance);
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
      List<TileCluster> mergedClusters = new();
      HashSet<TileCluster> processed = new();
      HashSet<TileCluster> toRemove = new();

      foreach (var cluster in updatedClusters)
      {
        if (processed.Contains(cluster)) continue; // Skip already merged clusters

        TileCluster mergedCluster = new TileCluster(cluster); // Clone the current cluster
        bool merged = false;

        foreach (var otherCluster in TileClusters)
        {
          if (cluster == otherCluster || processed.Contains(otherCluster)) continue;

          // 🔥 Check if clusters should be merged
          if (ClustersAreClose(cluster, otherCluster, maxDistance))
          {
            mergedCluster.MergeWith(otherCluster);
            toRemove.Add(otherCluster); // Mark for removal
            processed.Add(otherCluster);
            merged = true;
          }
        }

        if (merged) // Only add merged clusters
        {
          mergedClusters.Add(mergedCluster);
        } else // Keep unmerged clusters
        {
          mergedClusters.Add(cluster);
        }

        processed.Add(cluster);
      }

      // Remove clusters that have been merged
      TileClusters.RemoveAll(cluster => toRemove.Contains(cluster));
      // Add newly merged clusters back
      TileClusters.AddRange(mergedClusters);
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


    //public void DetectLargeFeatures()
    //{
    //  _mergedLines.Clear();

    //  foreach (var cluster in TileClusters)
    //  {
    //    if (cluster.Tiles.Count < 10) continue; // Skip small clusters

    //    // ✅ Skip if a permanent line already exists
    //    if (_map.PermanentLines.Any(line => LineIntersectsCluster(line, cluster)))
    //      continue;

    //    // Extract all tile centers
    //    List<Vector2> points = cluster.Tiles.Select(tile => tile.GlobalCenter).ToList();

    //    // ✅ Compute PCA to determine dominant axis
    //    (Vector2 direction, Vector2 centroid) = ComputePCA(points);

    //    // ✅ Sort points along the dominant axis
    //    points.Sort((a, b) => Vector2.Dot(a - centroid, direction).CompareTo(Vector2.Dot(b - centroid, direction)));

    //    // ✅ Find min/max bounds
    //    Vector2 minBound = new Vector2(points.Min(p => p.X), points.Min(p => p.Y));
    //    Vector2 maxBound = new Vector2(points.Max(p => p.X), points.Max(p => p.Y));

    //    // ✅ Filter out noise and ensure valid points
    //    List<Vector2> containedPoints = points
    //        .Where(p => minBound.X <= p.X && p.X <= maxBound.X && minBound.Y <= p.Y && p.Y <= maxBound.Y)
    //        .ToList();

    //    if (containedPoints.Count < 5) continue; // Skip clusters with too few valid points

    //    // ✅ Identify endpoints of the best-fit line
    //    Vector2 start = containedPoints.First();
    //    Vector2 end = containedPoints.Last();
    //    float lineLength = Vector2.Distance(start, end);

    //    // ✅ Adjust filtering thresholds dynamically
    //    float distanceFromLidar = Vector2.Distance(Vector2.Zero, centroid);
    //    float straightnessThreshold = MathHelper.Lerp(5f, 10f, MathF.Min(distanceFromLidar / 1000f, 1f));

    //    if (lineLength < MinLargeFeaturesLineLength) continue; // Skip short lines
    //    if (!IsClusterStraight(containedPoints, direction, centroid, straightnessThreshold)) continue;

    //    // ✅ Determine if the cluster should be marked as permanent
    //    bool isPermanent = cluster.Tiles.Count(t => t.IsPermanent) / (float)cluster.Tiles.Count >= 0.5f;

    //    // ✅ Calculate angle
    //    float angleRadians = MathF.Atan2(end.Y - start.Y, end.X - start.X);
    //    float angleDegrees = MathHelper.ToDegrees(angleRadians);

    //    // ✅ Store detected line
    //    _mergedLines.Add(new LineSegment(start, end, angleDegrees, angleRadians, isPermanent, cluster));
    //  }

    //  // ✅ Infer missing segments between detected features
    //  //InferMissingWallSegments();
    //}

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


    private void ComputeDeviceMovement(List<(Vector2 Start, Vector2 End, float Angle, bool IsPermanent)> oldLines,
                                   List<(Vector2 Start, Vector2 End, float Angle, bool IsPermanent)> newLines)
    {
      if (oldLines.Count == 0 || newLines.Count == 0)
      {
        Debug.WriteLine("ComputeDeviceMovement: No lines to compare.");
        return;
      }

      List<Vector2> shifts = new();
      List<float> angleChanges = new();

      int comparisonCount = Math.Min(oldLines.Count, newLines.Count);
      Debug.WriteLine($"Comparing {comparisonCount} old lines with {newLines.Count} new lines.");

      for (int i = 0; i < comparisonCount; i++)
      {
        var oldLine = oldLines[i];
        var newLine = newLines[i];

        Vector2 oldMidpoint = (oldLine.Start + oldLine.End) / 2;
        Vector2 newMidpoint = (newLine.Start + newLine.End) / 2;

        Vector2 shift = newMidpoint - oldMidpoint;
        shifts.Add(shift);

        float angleChange = newLine.Angle - oldLine.Angle;
        angleChanges.Add(angleChange);

        Debug.WriteLine($"Line {i}: Old Midpoint: {oldMidpoint}, New Midpoint: {newMidpoint}, Shift: {shift}, Angle Change: {angleChange}");
      }

      // Compute the average shift (translation)
      Vector2 avgShift = shifts.Aggregate(Vector2.Zero, (sum, v) => sum + v) / shifts.Count;
      float avgRotation = angleChanges.Sum() / angleChanges.Count;

      Debug.WriteLine($"Computed Shift: {avgShift}, Computed Rotation: {avgRotation}");

      if (avgShift.Length() > 0.1f) // Ignore tiny shifts to avoid noise
      {
        _device.SetDevicePosition(_device._devicePosition + avgShift);
        Debug.WriteLine($"New Device Position: {_device._devicePosition}");
      } else
      {
        Debug.WriteLine("No significant shift detected.");
      }

      //if (Math.Abs(avgRotation) > 0.01f)
      //{
      //  _device.SetEstimatedRotation(_device._deviceRotation + avgRotation);
      //  Debug.WriteLine($"New Device Rotation: {_device._deviceRotation}");
      //}
    }


    private bool IsClusterStraight(List<Vector2> points, Vector2 direction, Vector2 centroid, float baseDeviation)
    {
      foreach (var point in points)
      {
        
        float distanceFromLidar = Vector2.Distance(_device._devicePosition, point); // Assuming LiDAR is at (0,0)
        float allowedDeviation = baseDeviation + (distanceFromLidar * 0.1f); // Increase tolerance for farther points

        float projectedDistance = Math.Abs(Vector2.Dot(point - centroid, new Vector2(-direction.Y, direction.X))); // Perpendicular distance
        if (projectedDistance > allowedDeviation)
        {
          return false; // Too much deviation, not a straight cluster
        }
      }
      return true;
    }


    private float ComputeDominantLidarAngle(List<float> lidarAngles)
    {
      if (lidarAngles.Count < 3) return 0f; // Avoid unstable calculations

      float sumSin = 0, sumCos = 0, sumAngles = 0;

      foreach (float angle in lidarAngles)
      {
        float normalizedAngle = angle % 360;
        float radians = MathHelper.ToRadians(normalizedAngle);

        sumSin += MathF.Sin(radians);
        sumCos += MathF.Cos(radians);

        //  Handle angle wrapping inside this loop
        if (normalizedAngle < 90 && sumAngles / lidarAngles.Count > 270)
          normalizedAngle += 360;

        sumAngles += normalizedAngle;
      }

      if (sumSin == 0 && sumCos == 0) return 0f; // Prevent division errors

      float avgRadians = MathF.Atan2(sumSin, sumCos);
      float avgDegrees = MathHelper.ToDegrees(avgRadians) % 360; // Normalize to 0-360

      //  Use the weighted average of angles to stabilize results
      avgDegrees = sumAngles / lidarAngles.Count;
      avgDegrees = avgDegrees % 360; // Normalize again

      //  Round to the nearest 5 degrees for stability
      avgDegrees = MathF.Round(avgDegrees / 5) * 5;

      return MathHelper.ToRadians(avgDegrees);
    }


    private (Vector2 Direction, Vector2 Centroid) ComputePCA(List<Vector2> points)
    {
      if (points.Count < 2) return (Vector2.UnitX, points[0]); // Default if not enough points

      // Compute centroid
      Vector2 centroid = new Vector2(points.Average(p => p.X), points.Average(p => p.Y));

      // Compute covariance matrix
      float sumXX = 0, sumXY = 0, sumYY = 0;
      foreach (var p in points)
      {
        float dx = p.X - centroid.X;
        float dy = p.Y - centroid.Y;
        sumXX += dx * dx;
        sumXY += dx * dy;
        sumYY += dy * dy;
      }

      // Solve for the eigenvector of the dominant eigenvalue
      float trace = sumXX + sumYY;
      float determinant = (sumXX * sumYY) - (sumXY * sumXY);
      float eigenvalue = (trace + MathF.Sqrt(trace * trace - 4 * determinant)) / 2;

      Vector2 direction = new Vector2(sumXY, eigenvalue - sumXX);
      direction.Normalize();

      return (direction, centroid);
    }

  }
}
