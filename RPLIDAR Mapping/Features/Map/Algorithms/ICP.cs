using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Communications;
using RPLIDAR_Mapping.Features.Map;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;



public class ICP
{
  private readonly int _maxIterations = 10;
  private readonly float _convergenceThreshold = 0.01f;

  public (Vector2 offset, List<(Vector2 from, Vector2 to)> matches) Run(List<DataPoint> currentPoints, HashSet<Tile> trustedTiles)
  {
    if (trustedTiles.Count == 0 || currentPoints.Count == 0)
      return (Vector2.Zero, new());

    var transformedPoints = currentPoints.Select(p => p.GlobalPosition).ToList();
    List<(Vector2 from, Vector2 to)> matchedPairs = new();

    for (int iteration = 0; iteration < _maxIterations; iteration++)
    {
      var matches = MatchClosestPoints(transformedPoints, trustedTiles);
      if (matches.Count < 10)
        break;

      // Weighted centroid shift
      float totalWeight = 0f;
      Vector2 weightedFrom = Vector2.Zero;
      Vector2 weightedTo = Vector2.Zero;

      foreach (var (from, to) in matches)
      {
        float distSq = Vector2.DistanceSquared(from, to);
        float weight = 1f / (1f + distSq); // inverse square weighting
        totalWeight += weight;
        weightedFrom += from * weight;
        weightedTo += to * weight;
      }

      Vector2 centroidFrom = weightedFrom / totalWeight;
      Vector2 centroidTo = weightedTo / totalWeight;
      Vector2 iterationOffset = centroidTo - centroidFrom;

      float maxStep = 10f;
      if (iterationOffset.Length() > maxStep)
        iterationOffset = Vector2.Normalize(iterationOffset) * maxStep;

      if (iterationOffset.Length() < _convergenceThreshold)
        break;

      for (int i = 0; i < transformedPoints.Count; i++)
        transformedPoints[i] += iterationOffset;

      matchedPairs = matches;

      float avgError = matches.Select(m => Vector2.Distance(m.Item1, m.Item2)).Average();
      Debug.WriteLine($"📐 Iter {iteration} → Avg match error: {avgError:F1} from {matches.Count} matches");
    }

    // --- MAD-based Filtering ---
    Vector2 finalOffset = Vector2.Zero;

    if (matchedPairs.Count >= 5)
    {
      var deltas = matchedPairs.Select(m => m.Item2 - m.Item1).ToList();
      var lengths = deltas.Select(d => d.Length()).ToList();
      float median = GetMedian(lengths);
      float mad = GetMedian(lengths.Select(l => Math.Abs(l - median)).ToList());
      float threshold = median + 2f * mad;

      var filtered = matchedPairs
        .Where((m, i) => lengths[i] <= threshold)
        .ToList();

      if (filtered.Count >= 3)
      {
        matchedPairs = filtered;

        float weightSum = 0f;
        Vector2 weightedSum = Vector2.Zero;

        foreach (var (from, to) in matchedPairs)
        {
          Vector2 delta = to - from;
          float weight = 1f / (1f + delta.LengthSquared());
          weightedSum += delta * weight;
          weightSum += weight;

          Debug.WriteLine($"🔍 Match Δ: {delta} (from {from} to {to})");
        }

        finalOffset = (weightSum > 0f) ? weightedSum / weightSum : Vector2.Zero;
      }
    }

    // Clamp final offset
    float maxTotal = 50f;
    if (finalOffset.Length() > maxTotal)
      finalOffset = Vector2.Normalize(finalOffset) * maxTotal;

    Debug.WriteLine($"📌 ICP Estimated Offset: {finalOffset} from {matchedPairs.Count} matches");
    return (finalOffset, matchedPairs);
  }


  // --- Utility ---
  private float GetMedian(List<float> values)
  {
    var sorted = values.OrderBy(v => v).ToList();
    int count = sorted.Count;
    if (count == 0) return 0f;
    return (count % 2 == 0)
      ? (sorted[count / 2 - 1] + sorted[count / 2]) * 0.5f
      : sorted[count / 2];
  }




  private List<(Vector2 from, Vector2 to)> MatchClosestPoints(List<Vector2> points, HashSet<Tile> trustedTiles)
  {
    const float MaxStrictMatchDistSq = 200 * 200; // Tight, reliable matches
    const float MaxLooseMatchDistSq = 400 * 400; // Looser, less trusted matches

    List<(Vector2 from, Vector2 to)> matches = new();
    HashSet<Tile> unusedTiles = new(trustedTiles); // Enforce one-to-one matching

    foreach (var p in points)
    {
      float bestDistSq = float.MaxValue;
      Tile? bestTile = null;

      foreach (var tile in unusedTiles)
      {
        float distSq = Vector2.DistanceSquared(p, tile.GlobalCenter);

        if (distSq > MaxLooseMatchDistSq)
          continue;

        if (distSq < bestDistSq)
        {
          bestDistSq = distSq;
          bestTile = tile;
        }
      }

      if (bestTile != null)
      {
        // Accept match if within strict range or loose range with random chance
        if (bestDistSq < MaxStrictMatchDistSq || Random.Shared.NextDouble() < 0.25f)
        {
          matches.Add((p, bestTile.GlobalCenter));
          unusedTiles.Remove(bestTile); // Enforce one-to-one by removing matched tile
        }
      }
    }

    return matches;
  }





  private Vector2 Average(IEnumerable<Vector2> vectors)
  {
    Vector2 sum = Vector2.Zero;
    int count = 0;
    foreach (var v in vectors)
    {
      sum += v;
      count++;
    }
    return count > 0 ? sum / count : Vector2.Zero;
  }
}













