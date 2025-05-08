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
    public int mergeTileRadius = 2;
    public int mergeClusterRadius = 1;
    
    public int MinTileClusterSize = 10;
    public int MinLargeFeaturesLineLength = 100;
    public float TileMergeThreshold = 50;
    public bool ComputeMergedTiles = true;
    public bool ComputeMergedLines = true;
    public bool ComputeMergedRectangles = true;
    public bool DrawMergedTiles = true;
    public bool DrawMergedLines = true;
    private int MergeTilesFrameCounter = 0;
    public float PointClusterMergeDistance = 200f;
    private int _tileSize;
    public int MinPointQuality = 0;
    public float MinPointDistance = 0;
    public bool IsUpdated = false;
    private bool TilesMerged = false;
    public bool LinesDetected = false;
    private int SplitClusterFrequency = 50;
    private int framesSinceLastSplit = 0;
    private HashSet<Tile> _trustedTiles;
    private Dictionary<Vector2, HashSet<Tile>> clusterLookup = new();

    private Dictionary<(int, int), Tile> TileLookup = new();
    private List<Vector2> _previousClusterCenters = new();
    public Vector2 _estimatedPosition { get; private set; } = Vector2.Zero;
    private Dictionary<(int, int), List<Tile>> tileSpatialLookup = new();
    private const int SpatialCellSize = 100; // 100mm grid cells 

    public Device _device { get; set; }
    public MergeState CurrentState { get; set; } = MergeState.Idle;
    public enum MergeState
    {
      Idle,              // Waiting, nothing computed yet
      NewPointsAdded,
      LinkNeighbourTiles,
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
      bool ClustersChanged = false;
      _tileSize = 10;

      switch (CurrentState)
      {
        case MergeState.Idle:
          break;

        case MergeState.NewPointsAdded:
          _previousClusterCenters = TileClusters
              .Where(c => c.Tiles.Count >= 10)
              .Select(c => c.Center)
              .ToList();
          CurrentState = MergeState.LinkNeighbourTiles;
          break;

        case MergeState.LinkNeighbourTiles:
          LinkNearestNeighborsByProximity(_map.NewTiles.ToList());
          CurrentState = MergeState.ClusteringTiles;
          break;

        case MergeState.ClusteringTiles:
          if (_map.NewTiles.Count > 0)
          {
            ClusterTrustedTiles(_map.NewTiles);
            _map.NewTiles.Clear();
            ClustersChanged = true; // ✅ New points -> clusters changed
          }
          CurrentState = MergeState.MergingClusters;
          break;

        case MergeState.MergingClusters:
          if (ClustersChanged)
          {
            int maxSearchDistance = mergeClusterRadius * _tileSize;
            FastMergeOverlappingClusters(UpdatedClusters, maxSearchDistance);
            ClustersChanged = true; // ✅ Merging -> clusters changed
          }
          CurrentState = MergeState.SplittingClusters;
          break;

        case MergeState.SplittingClusters:
          if (ClustersChanged && framesSinceLastSplit >= SplitClusterFrequency)
          {
            SplitClusters();
            framesSinceLastSplit = 0;
            ClustersChanged = true; // ✅ Splitting -> clusters changed
          }
          else framesSinceLastSplit++;
          CurrentState = MergeState.ComputingRects;
          break;

        case MergeState.ComputingRects:
          foreach (TileCluster cluster in TileClusters)
          {
            cluster.Update();
            cluster.ComputeSightWindow(UtilityProvider.Device._devicePosition);
          }
          ClustersChanged = false; // ✅ Reset after updating
          CurrentState = MergeState.ComputeClusterShift;
          break;

        case MergeState.ComputeClusterShift:
          ComputeClusterShift();
          CurrentState = MergeState.ReadyToDraw;
          break;

        case MergeState.ReadyToDraw:
          CurrentState = MergeState.Idle;
          IsUpdated = true;
          break;
      }

      return IsUpdated;
    }

    public void LinkNearestNeighborsByProximity(List<Tile> allTiles)
    {
      float maxSearchDistance = 100f; // mm
      float maxSearchDistanceSquared = maxSearchDistance * maxSearchDistance;
      float coneHalfAngleDegrees = 45f;
      float cosHalfAngle = MathF.Cos(MathHelper.ToRadians(coneHalfAngleDegrees));
      BuildSpatialLookup(allTiles);

      foreach (Tile tile in allTiles)
      {
        Vector2 origin = tile.GlobalCenter;

        Tile leftBest = null, rightBest = null, topBest = null, bottomBest = null;
        float leftBestScore = float.MinValue;
        float rightBestScore = float.MinValue;
        float topBestScore = float.MinValue;
        float bottomBestScore = float.MinValue;

        foreach (Tile candidate in GetNearbyTiles(origin))
        {
          if (candidate == tile)
            continue;

          Vector2 toTarget = candidate.GlobalCenter - origin;
          float distSq = toTarget.LengthSquared();
          if (distSq > maxSearchDistanceSquared) continue;

          Vector2 dir = Vector2.Normalize(toTarget);

          float dotLeft = Vector2.Dot(dir, new Vector2(-1, 0));
          float dotRight = Vector2.Dot(dir, new Vector2(1, 0));
          float dotUp = Vector2.Dot(dir, new Vector2(0, -1));
          float dotDown = Vector2.Dot(dir, new Vector2(0, 1));

          float inverseDist = 1f / MathF.Sqrt(distSq); // Do sqrt *only* for scoring
          float scoreLeft = dotLeft * inverseDist;
          float scoreRight = dotRight * inverseDist;
          float scoreUp = dotUp * inverseDist;
          float scoreDown = dotDown * inverseDist;

          if (dotLeft > cosHalfAngle && scoreLeft > leftBestScore)
          {
            leftBest = candidate;
            leftBestScore = scoreLeft;
          }
          else if (dotRight > cosHalfAngle && scoreRight > rightBestScore)
          {
            rightBest = candidate;
            rightBestScore = scoreRight;
          }
          else if (dotUp > cosHalfAngle && scoreUp > topBestScore)
          {
            topBest = candidate;
            topBestScore = scoreUp;
          }
          else if (dotDown > cosHalfAngle && scoreDown > bottomBestScore)
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



    private IEnumerable<Tile> GetNearbyTiles(Vector2 position)
    {
      var cell = GetSpatialCell(position);

      for (int dx = -1; dx <= 1; dx++)
      {
        for (int dy = -1; dy <= 1; dy++)
        {
          var neighborCell = (cell.Item1 + dx, cell.Item2 + dy);
          if (tileSpatialLookup.TryGetValue(neighborCell, out var tilesInCell))
          {
            foreach (var tile in tilesInCell)
              yield return tile;
          }
        }
      }
    }


    public void BuildSpatialLookup(List<Tile> allTiles)
    {
      tileSpatialLookup.Clear();

      foreach (var tile in allTiles)
      {
        var cell = GetSpatialCell(tile.GlobalCenter);
        if (!tileSpatialLookup.ContainsKey(cell))
          tileSpatialLookup[cell] = new List<Tile>();
        tileSpatialLookup[cell].Add(tile);
      }
    }

    private (int, int) GetSpatialCell(Vector2 position)
    {
      int x = (int)MathF.Floor(position.X / SpatialCellSize);
      int y = (int)MathF.Floor(position.Y / SpatialCellSize);
      return (x, y);
    }
    public void MarkClusterEnds(List<Tile> allTiles)
    {
      foreach (var tile in allTiles)
      {
        int neighborCount = 0;
        if (tile.LeftAngularNeighbor != null) neighborCount++;
        if (tile.RightAngularNeighbor != null) neighborCount++;
        if (tile.TopAngularNeighbor != null) neighborCount++;
        if (tile.BottomAngularNeighbor != null) neighborCount++;

        tile.IsClusterEnd = (neighborCount == 1);
        tile.IsClusterIsolated = (neighborCount == 0);
        tile.IsClusterMiddle = (neighborCount == 2);
      }
    }

    private Tile GetNextNeighbor(Tile current, Tile previous)
    {
      if (current.LeftAngularNeighbor != null && current.LeftAngularNeighbor != previous)
        return current.LeftAngularNeighbor;
      if (current.RightAngularNeighbor != null && current.RightAngularNeighbor != previous)
        return current.RightAngularNeighbor;
      if (current.TopAngularNeighbor != null && current.TopAngularNeighbor != previous)
        return current.TopAngularNeighbor;
      if (current.BottomAngularNeighbor != null && current.BottomAngularNeighbor != previous)
        return current.BottomAngularNeighbor;
      return null;
    }

    /// ////////////////////////OLD

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

        //Debug.WriteLine("🔍 Cluster Shift Check:");
        //Debug.WriteLine($"• Cluster Center        : {cluster.Center}");
        //Debug.WriteLine($"• Device Position       : {devicePos}");
        //Debug.WriteLine($"• Expected Angle        : {MathHelper.ToDegrees(cluster.ExpectedAngle):0.00}°");
        //Debug.WriteLine($"• Current Angle         : {MathHelper.ToDegrees(angleNow):0.00}°");
        //Debug.WriteLine($"• Δ Angle               : {MathHelper.ToDegrees(angleDelta):0.00}°");
        //Debug.WriteLine($"• Expected Distance     : {cluster.ExpectedDistance:0.00}");
        //Debug.WriteLine($"• Current Distance      : {distNow:0.00}");
        //Debug.WriteLine($"• Δ Distance            : {distDelta:0.00}");
        //Debug.WriteLine($"• Allowed Angle Span    : {MathHelper.ToDegrees(cluster.ExpectedSpan / 2f):0.00}°");
        //Debug.WriteLine($"• Max Distance Drift    : {maxDistanceDrift}");

        //if (Math.Abs(angleDelta) > cluster.ExpectedSpan / 2f ||
        //    Math.Abs(distDelta) > maxDistanceDrift)
        //{
        //  Debug.WriteLine("⚠️  Cluster OUTSIDE sight cone — possible movement.");
        //}
        //else
        //{
        //  Debug.WriteLine("✅ Cluster within expected view.");
        //}

        //Debug.WriteLine(""); // Empty line for readability
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
        if (tile.TrustedScore < AlgorithmProvider.TileTrustRegulator.TrustThreshold) continue;
        if (!visited.Add(tile)) continue;

        // **Find the nearest cluster within search distance**
        TileCluster? nearestCluster = FindNearbyCluster(tile, maxSearchDistance);
        if (nearestCluster != null)
        {
          nearestCluster.AddTile(tile);
          tile.Cluster = nearestCluster;
          updatedClusters.Add(nearestCluster);
        }
        else
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
        foreach (var clusterTile in cluster.Tiles)
        {
          float distance = Vector2.Distance(tile.GlobalCenter, clusterTile.GlobalCenter);

          if (distance < bestDistance)
          {
            bestDistance = distance;
            bestCluster = cluster;
          }
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
          if (cluster == otherCluster || toRemove.Contains(otherCluster))
            continue;

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
        var split = cluster.SplitDisconnectedClusters();

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
      // 🔥 First quick check: if centers are way too far apart, skip
      float centerDistSq = Vector2.DistanceSquared(clusterA.Center, clusterB.Center);
      float maxMergeDistSq = (maxDistance * 2f) * (maxDistance * 2f); // extra margin
      if (centerDistSq > maxMergeDistSq)
        return false;

      // 🔥 Fast bounding box intersection check
      if (clusterA.Bounds.Intersects(clusterB.Bounds))
        return true;

      // 🔥 Slower closest-point check
      Vector2 closestPointA = GetClosestPointOnBounds(clusterA.Center, clusterB.Bounds);
      Vector2 closestPointB = GetClosestPointOnBounds(clusterB.Center, clusterA.Bounds);

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


  }
}
