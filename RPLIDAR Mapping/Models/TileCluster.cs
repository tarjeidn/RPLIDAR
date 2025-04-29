using Microsoft.AspNetCore.Identity;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class TileCluster
  {
    public HashSet<Tile> Tiles { get; private set; }
    public HashSet<TileCluster> MergedFrom { get; private set; } = new();
    public Rectangle Bounds { get; private set; }
    public Vector2 Center { get; private set; }
    public LineSegment? FeatureLine { get; private set; }
    public Vector2 PreviousCenter { get; private set; } // 🔥 Track last position
    public bool HasMoved { get; private set; } // 🔥 Track movement status
    public int TrustedTiles { get; set; } = 0;
    public float TrustedTilesRatio { get; set; } = 0f;
    public float ExpectedAngle { get; private set; }
    public float ExpectedSpan { get; private set; }
    public float ExpectedDistance { get; private set; }
    public List<(float Angle, float Distance)> Observations = new();
    Dictionary<TileCluster, Vector2> clusterToDeviceVector = new();
    public Vector2 BoundsCenter { get; private set; }
    public Vector2 BoundsSize { get; private set; }
    public float BoundsRotation { get; private set; } // In radians

    private Vector2 SmoothedCenter;
    private float SmoothedRotation;
    private Vector2 SmoothedSize;

    private const int MinStableFrames = 3;
    private const int MinTilesToDraw = 5;
    private const float SmoothingFactor = 0.95f; // 0 = instant, 1 = no smoothing
    private Vector2 SmoothedVelocity = Vector2.Zero;
    private const float VelocitySmoothing = 0.1f;

    private const float StabilityThreshold = 100f;
    public struct ClusterAngleRange
    {
      public TileCluster Cluster;
      public int MinAngle;
      public int MaxAngle;
    }
    public TileCluster()
    {
      Tiles = new HashSet<Tile>();
      Bounds = Rectangle.Empty;
      Center = Vector2.Zero;
      SmoothedCenter = Vector2.Zero;
      SmoothedRotation = 0f;
      SmoothedSize = Vector2.Zero;
    }
    // ✅ Copy Constructor (Clones another cluster)
    public TileCluster(TileCluster other)
    {
      Tiles = new HashSet<Tile>(other.Tiles); // Copy tiles
      Bounds = other.Bounds; // Copy bounding box
      Center = other.Center; // Copy center position
      FeatureLine = other.FeatureLine;
      SmoothedCenter = other.SmoothedCenter;
      SmoothedRotation = other.SmoothedRotation;
      SmoothedSize = other.SmoothedSize;
    }
    public void Update()
    {
      if (Tiles.Count == 0)
        return;

      foreach (var tile in Tiles)
      {
        tile.FramesInCluster++; 
      }

      // Select stable tiles
      var stableTiles = Tiles.Where(t => t.FramesInCluster >= MinStableFrames).ToList();

      if (stableTiles.Count < MinTilesToDraw)
      {
        Debug.WriteLine($"⚠️ Cluster Unstable: {stableTiles.Count} stable tiles (needs {MinTilesToDraw})");
        return; // ❌ Skip center update if unstable
      }

      Debug.WriteLine($"✅ Cluster Stable: {stableTiles.Count} stable tiles");

      // ✅ Only now update the Center/FeatureLine/bounds
      UpdateCenter(stableTiles);
      UpdateFeatureLine(stableTiles);
      UpdateRotatedBoundingBox(stableTiles);
    }



    //public void UpdateBoundingBox()
    //{
    //  PreviousCenter = new Vector2(Center.X, Center.Y);
    //  //ComputeBoundingBox();
    //  UpdateRotatedBoundingBox();
    //  ComputeClusterCenter();
    //  ComputeSightCone();
    //  TrustedTilesRatio = TrustedTiles / Tiles.Count;
    //  float movementThreshold = 2.0f; // Ignore small movements
    //  HasMoved = Vector2.Distance(PreviousCenter, Center) > movementThreshold;
    //}
    //public void UpdateFeatureLine()
    //{
    //  ComputeFeatureLine();
    //}

    private void UpdateCenter(List<Tile> stableTiles)
    {
      int count = stableTiles.Count;
      if (count == 0)
        return;

      var sortedX = stableTiles.OrderBy(t => t.GlobalCenter.X).ToList();
      var sortedY = stableTiles.OrderBy(t => t.GlobalCenter.Y).ToList();

      float medianX = sortedX[count / 2].GlobalCenter.X;
      float medianY = sortedY[count / 2].GlobalCenter.Y;

      Vector2 computedCenter = new Vector2(medianX, medianY);

      if (SmoothedCenter == Vector2.Zero)
      {
        SmoothedCenter = computedCenter;
      }
      else
      {
        Vector2 delta = computedCenter - SmoothedCenter;
        float deltaLength = delta.Length();

        // 🔥 Adaptive smoothing:
        float dynamicSmoothingFactor = MathHelper.Clamp(1f - (deltaLength / 50f), 0.05f, 0.95f);

        SmoothedCenter = Vector2.Lerp(SmoothedCenter, computedCenter, dynamicSmoothingFactor);
      }

      Center = SmoothedCenter;
    }

    private void UpdateFeatureLine(List<Tile> stableTiles)
    {
      if (stableTiles.Count < 2)
        return;

      List<Vector2> points = stableTiles.Select(t => t.GlobalCenter).ToList();
      (Vector2 direction, Vector2 centroid) = ComputePCA(points);

      points.Sort((a, b) => Vector2.Dot(a - centroid, direction).CompareTo(Vector2.Dot(b - centroid, direction)));

      Vector2 start = points.First();
      Vector2 end = points.Last();

      float angleRadians = MathF.Atan2(end.Y - start.Y, end.X - start.X);

      FeatureLine = new LineSegment(start, end, MathHelper.ToDegrees(angleRadians), angleRadians, false, this);
    }

    private void UpdateRotatedBoundingBox(List<Tile> stableTiles)
    {
      if (FeatureLine == null || stableTiles.Count == 0)
        return;

      float targetRotation = FeatureLine.AngleRadians;
      Vector2 centroid = Center;

      List<Vector2> rotatedPoints = new();
      foreach (var tile in stableTiles)
      {
        Vector2 relative = tile.GlobalCenter - centroid;
        float rotatedX = relative.X * MathF.Cos(-targetRotation) - relative.Y * MathF.Sin(-targetRotation);
        float rotatedY = relative.X * MathF.Sin(-targetRotation) + relative.Y * MathF.Cos(-targetRotation);
        rotatedPoints.Add(new Vector2(rotatedX, rotatedY));
      }

      float minX = rotatedPoints.Min(p => p.X);
      float maxX = rotatedPoints.Max(p => p.X);
      float minY = rotatedPoints.Min(p => p.Y);
      float maxY = rotatedPoints.Max(p => p.Y);

      Vector2 computedSize = new Vector2(maxX - minX, maxY - minY);

      // --- 🔥 Anti-shrink protection ---
      if (SmoothedSize != Vector2.Zero)
      {
        if (computedSize.X < SmoothedSize.X * 0.8f)
          computedSize.X = SmoothedSize.X * 0.8f;
        if (computedSize.Y < SmoothedSize.Y * 0.8f)
          computedSize.Y = SmoothedSize.Y * 0.8f;
      }

      // --- 🔥 Adaptive smoothing ---
      if (SmoothedSize == Vector2.Zero || float.IsNaN(SmoothedSize.X) || float.IsNaN(SmoothedSize.Y))
        SmoothedSize = computedSize;
      else
      {
        float sizeDelta = Vector2.Distance(SmoothedSize, computedSize);
        float sizeSmoothingFactor = MathHelper.Clamp(1f - (sizeDelta / 50f), 0.05f, 0.95f);

        SmoothedSize = Vector2.Lerp(SmoothedSize, computedSize, sizeSmoothingFactor);
      }

      if (float.IsNaN(SmoothedRotation) || SmoothedRotation == 0f)
        SmoothedRotation = targetRotation;
      else
      {
        float angleDelta = MathF.Abs(MathHelper.WrapAngle(SmoothedRotation - targetRotation));
        float rotationSmoothingFactor = MathHelper.Clamp(1f - (angleDelta / MathHelper.PiOver4), 0.05f, 0.95f);

        SmoothedRotation = MathHelper.Lerp(SmoothedRotation, targetRotation, rotationSmoothingFactor);
      }

      BoundsCenter = SmoothedCenter;
      BoundsSize = SmoothedSize;
      BoundsRotation = SmoothedRotation;
    }



    private (Vector2 Direction, Vector2 Centroid) ComputePCA(List<Vector2> points)
    {
      if (points.Count < 2)
        return (Vector2.UnitX, points[0]);

      Vector2 centroid = new Vector2(points.Average(p => p.X), points.Average(p => p.Y));
      float sumXX = 0, sumXY = 0, sumYY = 0;

      foreach (var p in points)
      {
        float dx = p.X - centroid.X;
        float dy = p.Y - centroid.Y;
        sumXX += dx * dx;
        sumXY += dx * dy;
        sumYY += dy * dy;
      }

      float trace = sumXX + sumYY;
      float determinant = (sumXX * sumYY) - (sumXY * sumXY);
      float eigenvalue = (trace + MathF.Sqrt(trace * trace - 4 * determinant)) / 2;

      Vector2 direction = new Vector2(sumXY, eigenvalue - sumXX);
      direction.Normalize();

      return (direction, centroid);
    }

    /// ///////////////

    //public void UpdateRotatedBoundingBox()
    //{
    //  if (FeatureLine == null || stableTiles.Count < 3)
    //    return; // ❌ Not enough tiles to update bounds reliably
    //  float rotation = FeatureLine.AngleRadians; // Use the feature line orientation
    //  Vector2 centroid = Center; // Use cluster center (already computed)

    //  List<Vector2> rotatedPoints = new();

    //  foreach (var tile in Tiles)
    //  {
    //    Vector2 relative = tile.GlobalCenter - centroid;
    //    float rotatedX = relative.X * MathF.Cos(-rotation) - relative.Y * MathF.Sin(-rotation);
    //    float rotatedY = relative.X * MathF.Sin(-rotation) + relative.Y * MathF.Cos(-rotation);
    //    rotatedPoints.Add(new Vector2(rotatedX, rotatedY));
    //  }

    //  float minX = rotatedPoints.Min(p => p.X);
    //  float maxX = rotatedPoints.Max(p => p.X);
    //  float minY = rotatedPoints.Min(p => p.Y);
    //  float maxY = rotatedPoints.Max(p => p.Y);

    //  BoundsCenter = centroid;
    //  BoundsSize = new Vector2(maxX - minX, maxY - minY);
    //  BoundsRotation = rotation;

    //  // Optional: store corners too if you want for faster drawing
    //}
    //public List<ClusterAngleRange> ComputeClusterAngleRanges(Vector2 devicePos, List<TileCluster> clusters)
    //{
    //  List<ClusterAngleRange> angleRanges = new();

    //  foreach (var cluster in clusters)
    //  {
    //    Rectangle bounds = cluster.Bounds;

    //    // Get all 4 corners of the bounding rectangle
    //    Vector2[] corners = new Vector2[]
    //    {
    //  new(bounds.Left, bounds.Top),
    //  new(bounds.Right, bounds.Top),
    //  new(bounds.Right, bounds.Bottom),
    //  new(bounds.Left, bounds.Bottom)
    //    };

    //    float min = 999f;
    //    float max = -999f;

    //    foreach (var corner in corners)
    //    {
    //      Vector2 dir = corner - devicePos;
    //      float angle = MathHelper.ToDegrees(MathF.Atan2(dir.Y, dir.X));
    //      angle = (angle + 360f) % 360f;

    //      if (angle < min) min = angle;
    //      if (angle > max) max = angle;
    //    }

    //    // Handle wraparound (e.g., from 350° to 10°)
    //    if (max - min > 180)
    //    {
    //      // Invert range: e.g. 350°–10° becomes two ranges: 0–10 and 350–360
    //      angleRanges.Add(new ClusterAngleRange { Cluster = cluster, MinAngle = 0, MaxAngle = (int)min });
    //      angleRanges.Add(new ClusterAngleRange { Cluster = cluster, MinAngle = (int)max, MaxAngle = 359 });
    //    } else
    //    {
    //      angleRanges.Add(new ClusterAngleRange { Cluster = cluster, MinAngle = (int)min, MaxAngle = (int)max });
    //    }
    //  }

    //  return angleRanges;
    //}


    /// <summary>
    /// Adds a tile to the cluster and updates bounds & center.
    /// </summary>
    public void AddTile(Tile tile)
    {
      if (!Tiles.Add(tile)) return; // Avoid duplicate tiles
      tile.FramesInCluster = 0;
      // Update bounds
      if (Tiles.Count == 1)
      {
        Center = tile.GlobalCenter;
        Bounds = new Rectangle((int)tile.GlobalCenter.X, (int)tile.GlobalCenter.Y, 1, 1);
      }


    }
    private void ComputeSightCone()
    {
      Vector2 devicePos = UtilityProvider.Device._devicePosition;
      Vector2 toCenter = Center - devicePos;

      float angleToCenter = MathF.Atan2(toCenter.Y, toCenter.X);  // 🔥 central angle
      float distanceToCenter = toCenter.Length();
      Vector2 topLeft = new Vector2(Bounds.Left, Bounds.Top);
      Vector2 topRight = new Vector2(Bounds.Right, Bounds.Top);
      Vector2 bottomLeft = new Vector2(Bounds.Left, Bounds.Bottom);
      Vector2 bottomRight = new Vector2(Bounds.Right, Bounds.Bottom);

      // Pick corner with **maximum angle difference** from center
      float angleTL = MathF.Atan2(topLeft.Y - devicePos.Y, topLeft.X - devicePos.X);
      float angleTR = MathF.Atan2(topRight.Y - devicePos.Y, topRight.X - devicePos.X);
      float angleBL = MathF.Atan2(bottomLeft.Y - devicePos.Y, bottomLeft.X - devicePos.X);
      float angleBR = MathF.Atan2(bottomRight.Y - devicePos.Y, bottomRight.X - devicePos.X);

      float minAngle = new[] { angleTL, angleTR, angleBL, angleBR }.Min();
      float maxAngle = new[] { angleTL, angleTR, angleBL, angleBR }.Max();

      float angularSpan = Utility.NormalizeAngle(maxAngle - minAngle); // 🔥 how wide the cluster appears
    }


    public void ComputeSightWindow(Vector2 devicePosition)
    {
      Vector2 toCenter = Center - devicePosition;
      ExpectedAngle = MathF.Atan2(toCenter.Y, toCenter.X);
      ExpectedDistance = toCenter.Length();

      Vector2[] corners = {
        new Vector2(Bounds.Left, Bounds.Top),
        new Vector2(Bounds.Right, Bounds.Top),
        new Vector2(Bounds.Left, Bounds.Bottom),
        new Vector2(Bounds.Right, Bounds.Bottom),
    };

      float min = float.MaxValue, max = float.MinValue;
      foreach (var corner in corners)
      {
        float angle = MathF.Atan2(corner.Y - devicePosition.Y, corner.X - devicePosition.X);
        if (angle < min) min = angle;
        if (angle > max) max = angle;
      }

      ExpectedSpan = Utility.NormalizeAngle(max - min);
    }
    public void MergeWith(TileCluster other)
    {
      if (other == this) return;

      foreach (Tile tile in other.Tiles)
      {
        Tiles.Add(tile);
        tile.Cluster = this;
        tile.FramesInCluster = 0;
      }

      MergedFrom.Add(other);
      if (other.MergedFrom.Count > 0)
      {
        foreach (var origin in other.MergedFrom)
          MergedFrom.Add(origin);
      }
    }

    public TileCluster Clone()
    {
      TileCluster copy = new TileCluster(this);

      return copy;
    }

    public void AdjustForMovement(Vector2 movementOffset, float rotationOffset)
    {
      // Move the entire cluster by the offset
      Center += movementOffset;
      Bounds.Offset(movementOffset);

      // Rotate each tile around the new cluster center
      foreach (var tile in Tiles)
      {
        Vector2 relativePosition = tile.GlobalCenter - Center;

        // Apply rotation
        float rotatedX = (float)(relativePosition.X * Math.Cos(rotationOffset) - relativePosition.Y * Math.Sin(rotationOffset));
        float rotatedY = (float)(relativePosition.X * Math.Sin(rotationOffset) + relativePosition.Y * Math.Cos(rotationOffset));

        tile.GlobalCenter = new Vector2(Center.X + rotatedX, Center.Y + rotatedY);
      }
    }

    public Vector2 GetClosestPoint(Vector2 point)
    {
      // 🔥 If the point is inside the cluster bounds, return the nearest boundary point
      if (Bounds.Contains(point.ToPoint()))
      {
        return point;
      }

      Vector2 closestPoint = Vector2.Zero;
      float minDistance = float.MaxValue;

      foreach (Tile tile in Tiles)
      {
        float distance = Vector2.Distance(point, tile.GlobalCenter);
        if (distance < minDistance)
        {
          minDistance = distance;
          closestPoint = tile.GlobalCenter;
        }
      }

      return closestPoint;
    }


    /// <summary>
    /// Removes a tile from the cluster and updates bounds & center.
    /// </summary>
    public void RemoveTile(Tile tile)
    {
      Tiles.Remove(tile);
      tile.Cluster = null;
      if (Tiles.Count == 0)  // 🚨 If empty, remove from TileClusters list
      {
        if (AlgorithmProvider.TileMerge.TileClusters.Contains(this))
        {
          AlgorithmProvider.TileMerge.TileClusters.Remove(this); // ✅ Remove from global clusters
        }

        // ✅ Remove associated merged line
        AlgorithmProvider.TileMerge._mergedLines.RemoveAll(line => line.ParentCluster == this);
      }
      if (Tiles.Count == 0)
      {
        Bounds = Rectangle.Empty;
        Center = Vector2.Zero;
      }

    }

    /// <summary>
    /// Recomputes the bounding box of the cluster.
    /// </summary>
    private void ComputeBoundingBox()
    {
      if (Tiles.Count == 0)
      {
        Bounds = Rectangle.Empty;
        return;
      }
      int minX = (int)Tiles.Min(t => t.GlobalCenter.X);
      int minY = (int)Tiles.Min(t => t.GlobalCenter.Y);
      int maxX = (int)Tiles.Max(t => t.GlobalCenter.X);
      int maxY = (int)Tiles.Max(t => t.GlobalCenter.Y);

      Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Computes the center of the cluster based on tile positions.
    /// </summary>
    private void ComputeClusterCenter()
    {
      if (Tiles.Count == 0)
      {
        Center = Vector2.Zero;
        return;
      }
      float avgX = Tiles.Average(t => t.GlobalCenter.X);
      float avgY = Tiles.Average(t => t.GlobalCenter.Y);
      Center = new Vector2(avgX, avgY);
    }


    public void ComputeFeatureLine()
    {
      if (Tiles.Count < 10) return;

      List<Vector2> points = Tiles.Select(t => t.GlobalCenter).ToList();

      // ✅ Compute PCA for dominant axis
      (Vector2 direction, Vector2 centroid) = ComputePCA(points);

      // ✅ Sort points along dominant direction
      points.Sort((a, b) => Vector2.Dot(a - centroid, direction).CompareTo(Vector2.Dot(b - centroid, direction)));

      Vector2 start = points.First();
      Vector2 end = points.Last();

      float angleRadians = MathF.Atan2(end.Y - start.Y, end.X - start.X);
      float angleDegrees = MathHelper.ToDegrees(angleRadians);

      FeatureLine = new LineSegment(start, end, angleDegrees, angleRadians, false, this);
    }
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

    public List<TileCluster> SplitDisconnectedClusters()
    {
      List<TileCluster> newClusters = new();
      HashSet<Tile> unvisited = new(Tiles);

      while (unvisited.Count > 0)
      {
        Queue<Tile> queue = new();
        Tile start = unvisited.First();
        queue.Enqueue(start);

        TileCluster cluster = new TileCluster();
        cluster.Tiles.Add(start);
        unvisited.Remove(start);

        while (queue.Count > 0)
        {
          Tile current = queue.Dequeue();

          foreach (Tile neighbor in current.GetNeighbors())
          {
            if (unvisited.Contains(neighbor))
            {

              cluster.Tiles.Add(neighbor);
              queue.Enqueue(neighbor);
              unvisited.Remove(neighbor);
            }
          }
        }

        newClusters.Add(cluster);
      }

      return newClusters;
    }



  }
}
