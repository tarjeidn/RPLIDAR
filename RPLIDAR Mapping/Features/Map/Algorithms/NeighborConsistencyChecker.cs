using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class NeighborConsistencyChecker
  {
    public struct NeighborComparisonResult
    {
      public int TotalTilesChecked;
      public int TotalMismatches;
      public float ConsistencyScore; // 0.0 = no match, 1.0 = perfect match
      public List<(Tile tile, string direction, Tile original, Tile simulated)> Mismatches;
    }

    public static NeighborComparisonResult CompareNeighborLinks(
        Dictionary<Tile, (Tile Left, Tile Right, Tile Top, Tile Bottom)> originalLinks,
        Dictionary<Tile, (Tile Left, Tile Right, Tile Top, Tile Bottom)> simulatedLinks)
    {
      int mismatches = 0;
      int comparisons = 0;
      List<(Tile tile, string dir, Tile original, Tile simulated)> mismatchList = new();

      foreach (var kvp in originalLinks)
      {
        Tile tile = kvp.Key;
        var original = kvp.Value;

        if (!simulatedLinks.TryGetValue(tile, out var simulated))
          continue;

        void Check(string dir, Tile orig, Tile sim)
        {
          comparisons++;
          if (orig != sim)
          {
            mismatches++;
            mismatchList.Add((tile, dir, orig, sim));
          }
        }

        Check("Left", original.Left, simulated.Left);
        Check("Right", original.Right, simulated.Right);
        Check("Top", original.Top, simulated.Top);
        Check("Bottom", original.Bottom, simulated.Bottom);
      }

      return new NeighborComparisonResult
      {
        TotalTilesChecked = originalLinks.Count,
        TotalMismatches = mismatches,
        ConsistencyScore = comparisons > 0 ? 1f - ((float)mismatches / comparisons) : 1f,
        Mismatches = mismatchList
      };
    }

    public static List<Tile> SimulateNewTilePlacement(List<DataPoint> dplist, Vector2 newDevicePosition)
    {
      var simulatedTiles = new List<Tile>();

      foreach (var dp in dplist)
      {
        if (dp.Distance >= 3500) continue;

        float yawRad = MathHelper.ToRadians(dp.Yaw);
        float adjustedAngle = MathHelper.ToRadians(dp.Angle) + yawRad;

        float x = newDevicePosition.X + dp.Distance * MathF.Cos(adjustedAngle);
        float y = newDevicePosition.Y + dp.Distance * MathF.Sin(adjustedAngle);

        Tile tile = UtilityProvider.Map._gridManager.GetTileAtGlobalCoordinates(x, y);
        if (tile != null && tile._lastLIDARpoint != null)
          simulatedTiles.Add(tile);
      }

      // Only simulate links for the affected tiles
      AlgorithmProvider.TileMerge.LinkNearestNeighborsByProximity(simulatedTiles);
      //UtilityProvider.Map.LinkNearestNeighborsByProximity(simulatedTiles);
      return simulatedTiles;
    }

    public static Dictionary<Tile, (Tile Left, Tile Right, Tile Top, Tile Bottom)> BuildNeighborLinkSnapshot(List<Tile> tiles)
    {
      var snapshot = new Dictionary<Tile, (Tile, Tile, Tile, Tile)>();

      foreach (var tile in tiles)
      {
        snapshot[tile] = (
            tile.LeftAngularNeighbor,
            tile.RightAngularNeighbor,
            tile.TopAngularNeighbor,
            tile.BottomAngularNeighbor
        );
      }

      return snapshot;
    }
  }


}
