using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.Algorithms
{
  public class VirtualTileScanMatcher
  {
    private const int TileSizeMm = 10;

    // Last known reference tile pattern
    private Dictionary<Point, Point> _previousTileEncodings = new();

    // Optional recent scan history (e.g. for smoothing/filtering)
    private readonly Queue<Dictionary<Point, Point>> _scanHistory = new();
    private const int MaxHistory = 5;

    /// <summary>
    /// Sets the reference tile pattern using a list of trusted tiles (e.g. from map).
    /// </summary>
    public void SetReferenceFromMap(IEnumerable<Tile> allTrustedTiles)
    {
      _previousTileEncodings.Clear();

      foreach (var tile in allTrustedTiles)
      {
        Point key = Quantize(tile.WorldGlobalPosition);
        Point encoded = new Point((int)tile.WorldGlobalPosition.X, (int)tile.WorldGlobalPosition.Y); // or tile.GlobalX/Y
        _previousTileEncodings[key] = encoded;
      }
    }

    /// <summary>
    /// Estimates device offset by comparing the current batch of points to the last reference layout.
    /// </summary>
    public Vector2 EstimateOffsetFromBatch(List<DataPoint> currentBatch, int searchRadiusTiles = 5)
    {
      var currentEncoding = new Dictionary<Point, Point>();

      foreach (var point in currentBatch)
      {
        if (MathF.Abs(point.Distance - 3500f) < 1f) // Skip ring points
          continue;

        Point key = Quantize(point.GlobalPosition);
        Point encoded = new Point((int)point.GlobalPosition.X, (int)point.GlobalPosition.Y);
        currentEncoding[key] = encoded;
      }

      if (currentEncoding.Count == 0 || _previousTileEncodings.Count == 0)
        return Vector2.Zero;

      // Match with best 2D offset
      Point bestOffset = Point.Zero;
      int bestMatchCount = 0;

      for (int dx = -searchRadiusTiles; dx <= searchRadiusTiles; dx++)
      {
        for (int dy = -searchRadiusTiles; dy <= searchRadiusTiles; dy++)
        {
          int matchCount = 0;
          Point offset = new(dx, dy);

          foreach (var kvp in currentEncoding)
          {
            Point shifted = new Point(kvp.Key.X + dx, kvp.Key.Y + dy);
            if (_previousTileEncodings.TryGetValue(shifted, out var prev))
            {
              if (prev == kvp.Value)
                matchCount++;
            }
          }

          if (matchCount > bestMatchCount)
          {
            bestMatchCount = matchCount;
            bestOffset = offset;
          }
        }
      }

      Vector2 offsetMm = new Vector2(bestOffset.X * TileSizeMm, bestOffset.Y * TileSizeMm);

      // Optionally store for smoothing or debugging
      _scanHistory.Enqueue(currentEncoding);
      if (_scanHistory.Count > MaxHistory)
        _scanHistory.Dequeue();

      return offsetMm;
    }

    /// <summary>
    /// Converts global position into a quantized tile key.
    /// </summary>
    private Point Quantize(Vector2 globalPosition)
    {
      int x = (int)MathF.Floor(globalPosition.X / TileSizeMm);
      int y = (int)MathF.Floor(globalPosition.Y / TileSizeMm);
      return new Point(x, y);
    }
  }

}
