using Microsoft.AspNetCore.Identity;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class TileCluster
  {
    public HashSet<Tile> Tiles { get; private set; }
    public Rectangle Bounds { get; private set; }
    public Vector2 Center { get; private set; }
    public LineSegment? FeatureLine { get; private set; }
    public Vector2 PreviousCenter { get; private set; } // 🔥 Track last position
    public bool HasMoved { get; private set; } // 🔥 Track movement status

    public TileCluster()
    {
      Tiles = new HashSet<Tile>();
      Bounds = Rectangle.Empty;
      Center = Vector2.Zero;
    }
    // ✅ Copy Constructor (Clones another cluster)
    public TileCluster(TileCluster other)
    {
      Tiles = new HashSet<Tile>(other.Tiles); // Copy tiles
      Bounds = other.Bounds; // Copy bounding box
      Center = other.Center; // Copy center position
      FeatureLine = other.FeatureLine;
    }
    public void Update()
    {
      PreviousCenter = new Vector2(Center.X, Center.Y);
      ComputeBoundingBox();
      ComputeClusterCenter();
      //ComputeFeatureLine();
      float movementThreshold = 2.0f; // Ignore small movements
      HasMoved = Vector2.Distance(PreviousCenter, Center) > movementThreshold;
    }

    /// <summary>
    /// Adds a tile to the cluster and updates bounds & center.
    /// </summary>
    public void AddTile(Tile tile)
    {
      if (!Tiles.Add(tile)) return; // Avoid duplicate tiles

      // Update bounds
      if (Tiles.Count == 1)
      {
        Bounds = new Rectangle((int)tile.GlobalCenter.X, (int)tile.GlobalCenter.Y, 1, 1);
      } 

    }
    public void MergeWith(TileCluster other)
    {
      foreach (Tile tile in other.Tiles)
      {
        AddTile(tile);  // 🔥 Efficiently adds tile
        tile.Cluster = this;
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
