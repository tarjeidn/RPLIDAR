using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using RPLIDAR_Mapping.Features;
using RPLIDAR_Mapping.Features.Map.GridModel;
using SharpDX.Direct3D9;

namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class TileMerge
  {

    public List<(Rectangle, float, bool)> _mergedObjects {  get; private set; }
    public int MergeFrequency = 20;
    public int MinMergedTileSize = 50;
    public float TileMergeThreshold = 50;
    public bool DrawMergedTiles = false;

    public int MinPointQuality = 0;
    public float MinPointDistance = 0;

    public TileMerge() 
    { 

    }
    public void ClearMergedObjects()
    {
      _mergedObjects.Clear();
    }
    public void MergeTilesGlobal(float mergeThreshold, List<Tile> drawnTiles)
    {
      HashSet<Tile> visited = new();
      List<(Rectangle, float, bool)> mergedRegions = new();
      //List<Tile> drawnTiles = Map.GetDistributor()._GridManager.GetAllDrawnTiles();
      if (drawnTiles.Count == 0) return;

      int tileSize = (int)(drawnTiles[0]._tileSize); //  Correct tile size

      //  Spatial lookup for fast merging
      Dictionary<(int, int), Tile> tileMap = new();
      foreach (Tile tile in drawnTiles)
      {
        var key = ((int)(tile.GlobalCenter.X / tileSize),
                   (int)(tile.GlobalCenter.Y / tileSize));
        tileMap[key] = tile;
      }

      foreach (Tile tile in drawnTiles)
      {
        if (visited.Contains(tile)) continue;

        //  Start a new merged region
        List<Tile> mergedTiles = new() { tile };
        float minX = tile.GlobalCenter.X;
        float minY = tile.GlobalCenter.Y;
        float maxX = minX;
        float maxY = minY;
        visited.Add(tile);

        Queue<Tile> queue = new();
        queue.Enqueue(tile);

        List<float> lidarAngles = new();
        int permanentTileCount = 0;

        if (tile._lastLIDARpoint != null)
          lidarAngles.Add(tile._lastLIDARpoint.Angle);

        if (tile.IsPermanent)
          permanentTileCount++;

        while (queue.Count > 0)
        {
          Tile currentTile = queue.Dequeue();

          for (int dx = -1; dx <= 1; dx++)
          {
            for (int dy = -1; dy <= 1; dy++)
            {
              var neighborKey = ((int)(currentTile.GlobalCenter.X / tileSize) + dx,
                                 (int)(currentTile.GlobalCenter.Y / tileSize) + dy);

              if (tileMap.TryGetValue(neighborKey, out Tile neighbor) && !visited.Contains(neighbor))
              {
                float distance = Vector2.Distance(currentTile.GlobalCenter, neighbor.GlobalCenter);

                if (distance <= (mergeThreshold * tileSize))
                {
                  visited.Add(neighbor);
                  queue.Enqueue(neighbor);
                  mergedTiles.Add(neighbor);

                  //  Update min/max boundaries
                  minX = Math.Min(minX, neighbor.GlobalCenter.X);
                  minY = Math.Min(minY, neighbor.GlobalCenter.Y);
                  maxX = Math.Max(maxX, neighbor.GlobalCenter.X);
                  maxY = Math.Max(maxY, neighbor.GlobalCenter.Y);

                  if (neighbor._lastLIDARpoint != null)
                    lidarAngles.Add(neighbor._lastLIDARpoint.Angle);

                  if (neighbor.IsPermanent)
                    permanentTileCount++;
                }
              }
            }
          }
        }

        //  Compute dominant angle
        float angle = ComputeDominantLidarAngle(lidarAngles);

        //  Determine permanence
        bool isPermanentRegion = (float)permanentTileCount / mergedTiles.Count >= 0.2f;

        //  Corrected Rectangle Creation
        int rectX = (int)(minX - tileSize / 2); // Move to top-left origin
        int rectY = (int)(minY - tileSize / 2);
        int rectWidth = (int)(maxX - minX + tileSize);
        int rectHeight = (int)(maxY - minY + tileSize);

        mergedRegions.Add((
            new Rectangle(rectX, rectY, rectWidth, rectHeight),
            angle,
            isPermanentRegion
        ));
      }

      _mergedObjects = mergedRegions;
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

    private float ComputeSurfaceAngle(List<Tile> tiles, int tileSize)
    {
      float sumDx = 0;
      float sumDy = 0;
      int count = 0;

      foreach (var tile in tiles)
      {
        foreach (var neighbor in tiles)
        {
          if (tile == neighbor) continue;

          float dx = neighbor.GlobalCenter.X - tile.GlobalCenter.X;
          float dy = neighbor.GlobalCenter.Y - tile.GlobalCenter.Y;
          float distance = Vector2.Distance(tile.GlobalCenter, neighbor.GlobalCenter);

          if (distance <= tileSize * AppSettings.Default.TilemergeThreshold) // Only consider close neighbors
          {
            sumDx += dx;
            sumDy += dy;
            count++;
          }
        }
      }

      if (count == 0) return 0f; // No rotation if no neighbors are found

      return (float)Math.Atan2(sumDy, sumDx); // Returns the rotation angle in radians
    }
  }
}
