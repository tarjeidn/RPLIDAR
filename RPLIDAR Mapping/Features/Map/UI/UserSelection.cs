using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.UI
{
  public class UserSelection
  {
    public Rectangle selectionBox { get; set; }
    private TileMerge _TileMerge;
    public Map _map { get; set; }
    public bool isSelecting { get; set; } = false;
    public List<LineSegment> highlightedLines { get; set; } = new();
    public List<LineSegment> InferredLines { get; set; } = new();
    public Vector2 selectionStart { get; set; } // Where the user starts dragging
    public Vector2 selectionEnd { get; set; }   // Where the user stops dragging
    public UserSelection()
    {
      _TileMerge = AlgorithmProvider.TileMerge;
    }

    public Rectangle CreateRectangle(Vector2 start, Vector2 end)
    {
      int x = (int)Math.Min(start.X, end.X);
      int y = (int)Math.Min(start.Y, end.Y);
      int width = (int)Math.Abs(start.X - end.X);
      int height = (int)Math.Abs(start.Y - end.Y);

      return new Rectangle(x, y, width, height);
    }
    public void SelectClusterAtPosition(Vector2 worldPos, bool ctrlHeld)
    {
      const float maxDistance = 20f;

      TileCluster? closestCluster = null;
      float closestDistance = float.MaxValue;

      foreach (var cluster in _TileMerge.TileClusters)
      {
        if (cluster.FeatureLine == null) continue;

        float distance = DistancePointToLine(worldPos, cluster.FeatureLine.Start, cluster.FeatureLine.End);
        if (distance < maxDistance && distance < closestDistance)
        {
          closestDistance = distance;
          closestCluster = cluster;
        }
      }

      if (closestCluster != null)
      {
        if (!ctrlHeld)
          highlightedLines.Clear();

        var newLine = new LineSegment(
            closestCluster.FeatureLine.Start,
            closestCluster.FeatureLine.End,
            closestCluster.FeatureLine.AngleDegrees,
            closestCluster.FeatureLine.AngleRadians,
            closestCluster.FeatureLine.IsPermanent,
            
            closestCluster.FeatureLine.ParentCluster
        );

        if (!highlightedLines.Any(l => l.Start == newLine.Start && l.End == newLine.End))
          highlightedLines.Add(newLine);
      }
    }

    public void HighlightClustersInSelection(bool ctrlHeld)
    {
      Camera camera = UtilityProvider.Camera;

      if (!ctrlHeld)
        highlightedLines.Clear();

      Rectangle worldSelectionBox = camera.ScreenToWorld(selectionBox);

      foreach (var cluster in _TileMerge.TileClusters)
      {
        if (cluster.Bounds.Intersects(worldSelectionBox) && cluster.FeatureLine != null)
        {
          var newLine = new LineSegment(
              cluster.FeatureLine.Start,
              cluster.FeatureLine.End,
              cluster.FeatureLine.AngleDegrees,
              cluster.FeatureLine.AngleRadians,
              cluster.FeatureLine.IsPermanent,
              
              cluster.FeatureLine.ParentCluster
          );

          if (!highlightedLines.Any(l => l.Start == newLine.Start && l.End == newLine.End))
            highlightedLines.Add(newLine);
        }
      }

      foreach (var line in _map.PermanentLines)
      {
        if (LineIntersectsRectangle(line.Start, line.End, worldSelectionBox))
        {
          var newPermLine = new LineSegment(
              line.Start, line.End, line.AngleDegrees, line.AngleRadians,
              line.IsPermanent, line.ParentCluster);

          if (!highlightedLines.Any(l => l.Start == newPermLine.Start && l.End == newPermLine.End))
            highlightedLines.Add(newPermLine);
        }
      }
    }

    public float DistancePointToLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
      float lineLength = Vector2.Distance(lineStart, lineEnd);
      if (lineLength == 0f) return Vector2.Distance(point, lineStart);

      float t = Vector2.Dot(point - lineStart, lineEnd - lineStart) / (lineLength * lineLength);
      t = Math.Clamp(t, 0f, 1f);

      Vector2 projection = lineStart + t * (lineEnd - lineStart);
      return Vector2.Distance(point, projection);
    }


    public void DeleteSelectedLines()
    {
      if (highlightedLines.Count == 0) return;

      // Create a list of all lines that should be deleted (including parents)
      HashSet<LineSegment> linesToDelete = new HashSet<LineSegment>(highlightedLines);

      // Include parents if they exist
      foreach (var line in highlightedLines)
      {
        if (line.ParentLine != null)
        {
          linesToDelete.Add(line.ParentLine);
        }
      }

      // Remove from both collections
      _TileMerge._mergedLines.RemoveAll(line => linesToDelete.Contains(line));
      _map.PermanentLines.RemoveAll(line => linesToDelete.Contains(line));

      // Clear selection
      highlightedLines.Clear();
    }

    private bool LineIntersectsRectangle(Vector2 start, Vector2 end, Rectangle rect)
    {
      // Check if both endpoints are inside the rectangle (full containment)
      return rect.Contains(start) && rect.Contains(end);
    }

    public void InferLinesBetweenSelected()
    {
      if (highlightedLines.Count < 2) return;

      List<LineSegment> newInferredLines = new();
      List<List<LineSegment>> lineGroups = new();

      float maxGroupingDistance = 5000f;

      // Step 1: Sort by spatial position (e.g., average of endpoints)
      var sortedLines = highlightedLines
          .OrderBy(line => (line.Start.X + line.End.X) / 2)
          .ThenBy(line => (line.Start.Y + line.End.Y) / 2)
          .ToList();

      // Step 2: Group lines based on proximity of any endpoints
      foreach (var line in sortedLines)
      {
        bool addedToGroup = false;

        foreach (var group in lineGroups)
        {
          if (group.Any(other =>
              Vector2.Distance(other.Start, line.Start) < maxGroupingDistance ||
              Vector2.Distance(other.Start, line.End) < maxGroupingDistance ||
              Vector2.Distance(other.End, line.Start) < maxGroupingDistance ||
              Vector2.Distance(other.End, line.End) < maxGroupingDistance))
          {
            group.Add(line);
            addedToGroup = true;
            break;
          }
        }

        if (!addedToGroup)
        {
          lineGroups.Add(new List<LineSegment> { line });
        }
      }

      // Step 3: Infer lines using closest endpoints
      foreach (var group in lineGroups)
      {
        for (int i = 0; i < group.Count - 1; i++)
        {
          var currentLine = group[i];
          var nextLine = group[i + 1];

          // Find closest endpoints
          (Vector2 p1, Vector2 p2) = FindClosestEndpoints(currentLine, nextLine);
          float gapDistance = Vector2.Distance(p1, p2);
          float angleDifference = Math.Abs(currentLine.AngleDegrees - nextLine.AngleDegrees);

          // Infer corner if angle difference significant
          if (angleDifference > 30f && angleDifference < 150f)
          {
            float inferredAngleDegrees = (currentLine.AngleDegrees + nextLine.AngleDegrees) / 2f;
            newInferredLines.Add(new LineSegment(
                p1, p2,
                inferredAngleDegrees,
                MathHelper.ToRadians(inferredAngleDegrees),
                false, true
            ));
          } else if (gapDistance > 20f && gapDistance < 2000f)
          {
            float inferredAngleDegrees = MathHelper.ToDegrees(MathF.Atan2(p2.Y - p1.Y, p2.X - p1.X));
            newInferredLines.Add(new LineSegment(
                p1, p2,
                inferredAngleDegrees,
                MathHelper.ToRadians(inferredAngleDegrees),
                false, true
            ));
          }
        }
      }

      Debug.WriteLine($"Inferred {newInferredLines.Count} lines.");

      // Step 4: Add inferred lines
      if (newInferredLines.Count > 0)
      {
        highlightedLines.AddRange(newInferredLines);
        Debug.WriteLine($"highlighted after Inferred {highlightedLines.Count} lines.");
      }
    }

    // Helper method to find closest endpoints
    private (Vector2, Vector2) FindClosestEndpoints(LineSegment lineA, LineSegment lineB)
    {
      var pointsA = new[] { lineA.Start, lineA.End };
      var pointsB = new[] { lineB.Start, lineB.End };

      float minDistance = float.MaxValue;
      Vector2 closestA = pointsA[0];
      Vector2 closestB = pointsB[0];

      foreach (var pA in pointsA)
      {
        foreach (var pB in pointsB)
        {
          float distance = Vector2.Distance(pA, pB);
          if (distance < minDistance)
          {
            minDistance = distance;
            closestA = pA;
            closestB = pB;
          }
        }
      }

      return (closestA, closestB);
    }

    public void SmoothPermanentLines()
    {
      float intersectionTolerance = 10f;
      float angleToleranceDegrees = 10f;
      float minLineLength = 20f;

      // Step 1: Remove intersecting lines
      RemoveIntersectingLines(intersectionTolerance);

      // Step 2: Merge collinear lines
      MergeCollinearLines(angleToleranceDegrees, intersectionTolerance);

      // Step 3: Remove short or isolated lines
      RemoveShortLines(minLineLength);
    }

    private void RemoveIntersectingLines(float tolerance)
    {
      var linesToRemove = new HashSet<LineSegment>();

      for (int i = 0; i < highlightedLines.Count; i++)
      {
        var lineA = highlightedLines[i];
        for (int j = i + 1; j < highlightedLines.Count; j++)
        {
          var lineB = highlightedLines[j];

          if (LinesIntersect(lineA, lineB, out Vector2 intersection))
          {
            // If intersection is not near endpoints, mark shorter line for removal
            bool isNearEndpoints = (Vector2.Distance(intersection, lineA.Start) < tolerance ||
                                    Vector2.Distance(intersection, lineA.End) < tolerance ||
                                    Vector2.Distance(intersection, lineB.Start) < tolerance ||
                                    Vector2.Distance(intersection, lineB.End) < tolerance);

            if (!isNearEndpoints)
            {
              var shorterLine = (lineA.Length < lineB.Length) ? lineA : lineB;
              linesToRemove.Add(shorterLine);
            }
          }
        }
      }

      highlightedLines.RemoveAll(l => linesToRemove.Contains(l));
    }

    private void MergeCollinearLines(float angleTolerance, float distanceTolerance)
    {
      var mergedLines = new List<LineSegment>();
      var processed = new HashSet<LineSegment>();

      foreach (var line in highlightedLines)
      {
        if (processed.Contains(line)) continue;

        List<LineSegment> similarLines = highlightedLines
            .Where(other => !processed.Contains(other)
                         && Math.Abs(line.AngleDegrees - other.AngleDegrees) < angleTolerance
                         && LinesAreClose(line, other, distanceTolerance))
            .ToList();

        if (similarLines.Count > 1)
        {
          var mergedLine = MergeLines(similarLines);
          mergedLines.Add(mergedLine);
          similarLines.ForEach(l => processed.Add(l));
        } else
        {
          mergedLines.Add(line);
          processed.Add(line);
        }
      }

      highlightedLines = mergedLines;
    }

    private void RemoveShortLines(float minLength)
    {
      highlightedLines.RemoveAll(l => l.Length < minLength);
    }

    // Utility methods

    private bool LinesIntersect(LineSegment lineA, LineSegment lineB, out Vector2 intersection)
    {
      intersection = Vector2.Zero;
      return Utility.LineSegmentsIntersect(lineA.Start, lineA.End, lineB.Start, lineB.End, out intersection);
    }

    private bool LinesAreClose(LineSegment lineA, LineSegment lineB, float tolerance)
    {
      return (Vector2.Distance(lineA.Start, lineB.Start) < tolerance ||
              Vector2.Distance(lineA.Start, lineB.End) < tolerance ||
              Vector2.Distance(lineA.End, lineB.Start) < tolerance ||
              Vector2.Distance(lineA.End, lineB.End) < tolerance);
    }

    private LineSegment MergeLines(List<LineSegment> lines)
    {
      var allPoints = lines.SelectMany(l => new[] { l.Start, l.End }).ToList();
      var centroid = new Vector2(allPoints.Average(p => p.X), allPoints.Average(p => p.Y));
      var direction = (lines.First().End - lines.First().Start);
      direction.Normalize();

      var projectedPoints = allPoints
          .OrderBy(p => Vector2.Dot(p - centroid, direction))
          .ToList();

      var mergedLine = new LineSegment(projectedPoints.First(), projectedPoints.Last(),
                                       lines.First().AngleDegrees,
                                       lines.First().AngleRadians,
                                       true, false);
      return mergedLine;
    }


    public void MergeSelectedLinesIntoWalls()
    {
      _map.PermanentLines.AddRange(highlightedLines);
      highlightedLines.Clear();
    }




  }
}
