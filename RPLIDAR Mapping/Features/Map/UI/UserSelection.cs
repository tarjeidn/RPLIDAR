using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Models;
using RPLIDAR_Mapping.Providers;
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
    public void HighlightLinesInSelection()
    {
      Camera camera = UtilityProvider.Camera;
      highlightedLines.Clear();

      // Convert selection box from screen space to world space
      Rectangle worldSelectionBox = camera.ScreenToWorld(selectionBox);

      foreach (var line in _TileMerge._mergedLines)
      {
        if (LineIntersectsRectangle(line.Start, line.End, worldSelectionBox))
        {
          highlightedLines.Add(new LineSegment(line.Start, line.End, line.AngleDegrees, line.AngleRadians, line.IsPermanent, false, line));
        }
      }
      foreach (var line in _map.PermanentLines)
      {
        if (LineIntersectsRectangle(line.Start, line.End, worldSelectionBox))
        {
          highlightedLines.Add(new LineSegment(line.Start, line.End, line.AngleDegrees, line.AngleRadians, line.IsPermanent, true, line));
        }
      }
      //foreach (var line in _TileMerge._inferredLines)
      //{
      //  if (LineIntersectsRectangle(line.Start, line.End, worldSelectionBox))
      //  {
      //    highlightedLines.Add((line.Start, line.End, line.Angle, line.IsPermanent, true));
      //  }
      //}
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
    //// SIMPLE WORKING
    //public void InferLinesBetweenSelected()
    //{
    //  if (highlightedLines.Count < 2) return; // Need at least 2 lines to infer missing parts

    //  List<LineSegment> newInferredLines = new();

    //  // Group selected lines by approximate angle (tolerance: 5 degrees)
    //  var groupedLines = highlightedLines
    //      .GroupBy(line => MathF.Round(line.AngleDegrees / 45) * 45)
    //      .ToList();

    //  foreach (var group in groupedLines)
    //  {
    //    var sortedLines = group.OrderBy(line => Vector2.Dot(line.Start,
    //        new Vector2(MathF.Cos(line.AngleRadians), MathF.Sin(line.AngleRadians))))
    //        .ToList();

    //    for (int i = 0; i < sortedLines.Count - 1; i++)
    //    {
    //      var currentLine = sortedLines[i];
    //      var nextLine = sortedLines[i + 1];

    //      float gapDistance = Vector2.Distance(currentLine.End, nextLine.Start);

    //      // Infer a new line if the gap is reasonable
    //      if (gapDistance > 20 && gapDistance < 2000) // Threshold to avoid noise
    //      {
    //        LineSegment inferredLine = new LineSegment(
    //            currentLine.End, nextLine.Start,
    //            currentLine.AngleDegrees, currentLine.AngleRadians,
    //            false, true // Not permanent, inferred
    //        );

    //        newInferredLines.Add(inferredLine);
    //        InferredLines.Add(inferredLine); // ✅ Ensure it's stored
    //      }
    //    }
    //  }

    //  Debug.WriteLine($"Inferred {newInferredLines.Count} lines.");

    //  // Ensure inferred lines are added to the selection
    //  if (newInferredLines.Count > 0)
    //  {
    //    highlightedLines.AddRange(newInferredLines);
    //    Debug.WriteLine($"highlighted after Inferred {highlightedLines.Count} lines.");
    //  }
    //}
    public void InferLinesBetweenSelected()
    {
      if (highlightedLines.Count < 2) return; // Need at least 2 lines to infer missing parts

      List<LineSegment> newInferredLines = new();
      List<List<LineSegment>> lineGroups = new(); // Stores groups of nearby lines

      float maxGroupingDistance = 1000f; // 🔥 Adjust threshold for proximity

      // 🔹 Step 1: Sort by spatial position
      var sortedLines = highlightedLines
          .OrderBy(line => line.Start.X)
          .ThenBy(line => line.Start.Y)
          .ToList();

      // 🔹 Step 2: Group lines based on proximity
      foreach (var line in sortedLines)
      {
        bool addedToGroup = false;

        foreach (var group in lineGroups)
        {
          if (group.Any(other => Vector2.Distance(other.End, line.Start) < maxGroupingDistance))
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

      // 🔹 Step 3: Process each group
      foreach (var group in lineGroups)
      {
        for (int i = 0; i < group.Count - 1; i++)
        {
          var currentLine = group[i];
          var nextLine = group[i + 1];

          float gapDistance = Vector2.Distance(currentLine.End, nextLine.Start);
          float angleDifference = Math.Abs(currentLine.AngleDegrees - nextLine.AngleDegrees);

          // 🔥 Detect corners (angle difference > 30° but not a straight intersection)
          if (angleDifference > 30f && angleDifference < 150f)
          {
            Debug.WriteLine($"Corner Detected! Angle Diff: {angleDifference}°");

            // Connect the lines with a diagonal inferred corner
            newInferredLines.Add(new LineSegment(
                currentLine.End, nextLine.Start,
                (currentLine.AngleDegrees + nextLine.AngleDegrees) / 2, // Approximate angle
                MathHelper.ToRadians((currentLine.AngleDegrees + nextLine.AngleDegrees) / 2),
                false, true
            ));
          } else if (gapDistance > 20 && gapDistance < 2000) // 🔥 Standard inference
          {
            newInferredLines.Add(new LineSegment(
                currentLine.End, nextLine.Start,
                currentLine.AngleDegrees, currentLine.AngleRadians,
                false, true
            ));
          }
        }
      }

      Debug.WriteLine($"Inferred {newInferredLines.Count} lines.");

      // 🔹 Step 4: Add inferred lines to selection
      if (newInferredLines.Count > 0)
      {
        highlightedLines.AddRange(newInferredLines);
        Debug.WriteLine($"highlighted after Inferred {highlightedLines.Count} lines.");
      }
    }

    private Vector2 FindCornerPoint(LineSegment lineA, LineSegment lineB)
    {
      Vector2 dirA = new Vector2(MathF.Cos(lineA.AngleRadians), MathF.Sin(lineA.AngleRadians));
      Vector2 dirB = new Vector2(MathF.Cos(lineB.AngleRadians), MathF.Sin(lineB.AngleRadians));

      // Line equations: A1*x + B1*y = C1  and A2*x + B2*y = C2
      float A1 = dirA.Y, B1 = -dirA.X, C1 = A1 * lineA.End.X + B1 * lineA.End.Y;
      float A2 = dirB.Y, B2 = -dirB.X, C2 = A2 * lineB.Start.X + B2 * lineB.Start.Y;

      float det = A1 * B2 - A2 * B1;
      if (MathF.Abs(det) < 0.0001f)
        return Vector2.Zero; // Lines are parallel or too close to determine intersection

      float x = (B2 * C1 - B1 * C2) / det;
      float y = (A1 * C2 - A2 * C1) / det;

      return new Vector2(x, y);
    }


    private bool DetectCornerBetweenLines(LineSegment lineA, LineSegment lineB, out Vector2 cornerPoint)
    {
      // 🔥 Compute intersection point if extended
      if (FindIntersection(lineA, lineB, out Vector2 intersection))
      {
        // ✅ Only accept the corner if it's reasonably close to both
        float distA = Vector2.Distance(intersection, lineA.End);
        float distB = Vector2.Distance(intersection, lineB.Start);

        if (distA < 200f && distB < 200f) // ✅ Ensure it's a real corner
        {
          cornerPoint = intersection;
          return true;
        }
      }

      cornerPoint = Vector2.Zero;
      return false;
    }
    public void MergeSelectedLinesIntoWalls()
    {
      _map.PermanentLines.AddRange(highlightedLines);
      highlightedLines.Clear();
    }


    //public void MergeSelectedLinesIntoWalls()
    //{
    //  if (highlightedLines.Count < 2) return; // Need at least 2 lines

    //  List<LineSegment> mergedWallLines = new();
    //  HashSet<LineSegment> processed = new();
    //  float maxGapDistance = 500f; // Max distance to merge lines

    //  // Step 1: Sort lines by position
    //  var sortedLines = highlightedLines
    //      .OrderBy(line => Math.Min(line.Start.X, line.End.X))
    //      .ThenBy(line => Math.Min(line.Start.Y, line.End.Y))
    //      .ToList();

    //  while (sortedLines.Count > 1)
    //  {
    //    LineSegment current = sortedLines[0];
    //    sortedLines.RemoveAt(0);

    //    LineSegment closest = null;
    //    float minDistance = float.MaxValue;

    //    // Find the closest line that doesn't create an intersection
    //    foreach (var candidate in sortedLines)
    //    {
    //      float distStartToStart = Vector2.Distance(current.Start, candidate.Start);
    //      float distStartToEnd = Vector2.Distance(current.Start, candidate.End);
    //      float distEndToStart = Vector2.Distance(current.End, candidate.Start);
    //      float distEndToEnd = Vector2.Distance(current.End, candidate.End);

    //      float minCandidateDistance = Math.Min(Math.Min(distStartToStart, distStartToEnd),
    //                                            Math.Min(distEndToStart, distEndToEnd));

    //      if (minCandidateDistance < maxGapDistance && minCandidateDistance < minDistance &&
    //          !LinesIntersect(current, candidate)) // Prevent self-intersections
    //      {
    //        closest = candidate;
    //        minDistance = minCandidateDistance;
    //      }
    //    }

    //    if (closest != null)
    //    {
    //      sortedLines.Remove(closest);

    //      // Ensure correct connection of endpoints
    //      (Vector2 newStart, Vector2 newEnd) = ConnectClosestEndpoints(current, closest);

    //      mergedWallLines.Add(new LineSegment(newStart, newEnd, current.AngleDegrees, current.AngleRadians, true, false));
    //    } else
    //    {
    //      mergedWallLines.Add(current);
    //    }
    //  }

    //  if (sortedLines.Count == 1) mergedWallLines.Add(sortedLines[0]);

    //  // Step 3: Store merged walls in permanent lines
    //  _map.PermanentLines.AddRange(mergedWallLines);
    //  highlightedLines.Clear();
    //}
    private (Vector2 Start, Vector2 End) ConnectClosestEndpoints(LineSegment a, LineSegment b)
    {
      float distStartToStart = Vector2.Distance(a.Start, b.Start);
      float distStartToEnd = Vector2.Distance(a.Start, b.End);
      float distEndToStart = Vector2.Distance(a.End, b.Start);
      float distEndToEnd = Vector2.Distance(a.End, b.End);

      // Find the closest pairing
      float minDist = Math.Min(Math.Min(distStartToStart, distStartToEnd),
                               Math.Min(distEndToStart, distEndToEnd));

      if (minDist == distStartToStart) return (a.End, b.End);
      if (minDist == distStartToEnd) return (a.End, b.Start);
      if (minDist == distEndToStart) return (a.Start, b.End);
      return (a.Start, b.Start);
    }
    private bool LinesIntersect(LineSegment a, LineSegment b)
    {
      return Intersect(a.Start, a.End, b.Start, b.End);
    }

    private bool Intersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
    {
      float d1 = CrossProduct(q1 - p1, p2 - p1);
      float d2 = CrossProduct(q2 - p1, p2 - p1);
      float d3 = CrossProduct(p1 - q1, q2 - q1);
      float d4 = CrossProduct(p2 - q1, q2 - q1);

      return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
             ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private float CrossProduct(Vector2 a, Vector2 b)
    {
      return a.X * b.Y - a.Y * b.X;
    }




    private List<LineSegment> RetryMergeWithLooserThresholds(List<LineSegment> selectedLines)
    {
      List<LineSegment> retryLines = new List<LineSegment>(selectedLines);

      float looseAngleThreshold = 20f; // Allow a wider angle difference
      float looseMergeDistanceThreshold = 1500f; // Allow larger gaps

      // 🔥 Attempt merging again with looser conditions
      List<LineSegment> newMergedLines = new List<LineSegment>();
      HashSet<LineSegment> processed = new();

      for (int i = 0; i < retryLines.Count - 1; i++)
      {
        var currentLine = retryLines[i];
        var nextLine = retryLines[i + 1];

        if (processed.Contains(currentLine) || processed.Contains(nextLine)) continue;

        if (LinesAreAligned(currentLine, nextLine, looseAngleThreshold, looseMergeDistanceThreshold))
        {
          (Vector2 newStart, Vector2 newEnd) = MergeLineEndpoints(currentLine, nextLine);
          LineSegment mergedLine = new LineSegment(newStart, newEnd,
              currentLine.AngleDegrees, currentLine.AngleRadians, true, false, null);

          processed.Add(nextLine);
          newMergedLines.Add(mergedLine);
        } else
        {
          newMergedLines.Add(currentLine); // If no merge, at least keep the line
        }

        processed.Add(currentLine);
      }

      return newMergedLines;
    }
    private void IncludeNearbyExistingLines(List<LineSegment> selectedLines)
    {
      float maxSearchDistance = 250f; // 🔥 Prevent distant connections

      List<LineSegment> nearbyLines = new List<LineSegment>();

      foreach (var line in _TileMerge._mergedLines)
      {
        float closestDistance = selectedLines
            .Min(selected => Math.Min(Vector2.Distance(selected.Start, line.Start),
                                      Vector2.Distance(selected.End, line.End)));

        if (closestDistance < maxSearchDistance) // ✅ Only include close lines
        {
          nearbyLines.Add(line);
        }
      }

      selectedLines.AddRange(nearbyLines);
    }


    private LineSegment ForceMergeLines(List<LineSegment> group)
    {
      if (group.Count == 0) return null; // Fallback check

      // 🔥 Find the leftmost and rightmost points in the group
      Vector2 start = group.Select(l => l.Start).OrderBy(p => p.X).First();
      Vector2 end = group.Select(l => l.End).OrderBy(p => p.X).Last();

      // 🔥 Compute an approximate average angle
      float avgAngleDegrees = group.Select(l => l.AngleDegrees).Average();
      float avgAngleRadians = MathHelper.ToRadians(avgAngleDegrees);

      return new LineSegment(start, end, avgAngleDegrees, avgAngleRadians, true, false);
    }


    private bool FindIntersection(LineSegment lineA, LineSegment lineB, out Vector2 intersection)
    {
      intersection = Vector2.Zero;

      // Line equations: A1*x + B1*y = C1, A2*x + B2*y = C2
      float A1 = lineA.End.Y - lineA.Start.Y;
      float B1 = lineA.Start.X - lineA.End.X;
      float C1 = A1 * lineA.Start.X + B1 * lineA.Start.Y;

      float A2 = lineB.End.Y - lineB.Start.Y;
      float B2 = lineB.Start.X - lineB.End.X;
      float C2 = A2 * lineB.Start.X + B2 * lineB.Start.Y;

      float determinant = A1 * B2 - A2 * B1;

      if (MathF.Abs(determinant) < 0.0001f) return false; // Parallel lines

      intersection.X = (B2 * C1 - B1 * C2) / determinant;
      intersection.Y = (A1 * C2 - A2 * C1) / determinant;

      return true;
    }
    private List<LineSegment> FindNearbyRelevantPermanentLines(List<LineSegment> selectedLines)
    {
      List<LineSegment> relevantLines = new();
      float maxMergeDistance = 1000f;
      float angleThreshold = 10f;

      foreach (var permanentLine in _map.PermanentLines)
      {
        foreach (var selectedLine in selectedLines)
        {
          if (LinesAreAligned(selectedLine, permanentLine, angleThreshold, maxMergeDistance) ||
              LinesIntersect(selectedLine, permanentLine))
          {
            relevantLines.Add(permanentLine);
            break; // No need to check further, this line is already relevant
          }
        }
      }

      return relevantLines.Distinct().ToList();
    }

    private bool DoLineSegmentsIntersect(Vector2 p1, Vector2 q1, Vector2 p2, Vector2 q2)
    {
      // Check if two line segments (p1,q1) and (p2,q2) intersect
      float o1 = Orientation(p1, q1, p2);
      float o2 = Orientation(p1, q1, q2);
      float o3 = Orientation(p2, q2, p1);
      float o4 = Orientation(p2, q2, q1);

      // General case: If orientations are different, lines intersect
      if (o1 != o2 && o3 != o4) return true;

      // Special cases: Check if collinear points lie on the segment
      if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
      if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
      if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
      if (o4 == 0 && OnSegment(p2, q1, q2)) return true;

      return false; // No intersection
    }
    private float Orientation(Vector2 p, Vector2 q, Vector2 r)
    {
      // Compute the orientation of the triplet (p, q, r)
      float val = (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

      if (val == 0) return 0;  // Collinear
      return (val > 0) ? 1 : -1; // Clockwise or Counterclockwise
    }
    private bool OnSegment(Vector2 p, Vector2 q, Vector2 r)
    {
      return q.X <= MathF.Max(p.X, r.X) && q.X >= MathF.Min(p.X, r.X) &&
             q.Y <= MathF.Max(p.Y, r.Y) && q.Y >= MathF.Min(p.Y, r.Y);
    }
    private (Vector2 Start, Vector2 End) MergeLineEndpoints(LineSegment lineA, LineSegment lineB)
    {
      // Compute distances for all possible connections
      float distStartToStart = Vector2.Distance(lineA.Start, lineB.Start);
      float distStartToEnd = Vector2.Distance(lineA.Start, lineB.End);
      float distEndToStart = Vector2.Distance(lineA.End, lineB.Start);
      float distEndToEnd = Vector2.Distance(lineA.End, lineB.End);

      // Find the smallest distance
      float minDistance = MathF.Min(MathF.Min(distStartToStart, distStartToEnd),
                                    MathF.Min(distEndToStart, distEndToEnd));

      // Connect based on the closest points
      if (minDistance == distStartToStart) return (lineA.End, lineB.End);
      if (minDistance == distStartToEnd) return (lineA.End, lineB.Start);
      if (minDistance == distEndToStart) return (lineA.Start, lineB.End);
      return (lineA.Start, lineB.Start);
    }


    private void DetectAndMergeCorners(List<LineSegment> mergedGroup)
    {
      float cornerThresholdAngle = 30f; // If lines meet at <30° angle, form a corner
      float maxCornerDistance = 100f; // Max distance for corner detection

      for (int i = 0; i < mergedGroup.Count - 1; i++)
      {
        var lineA = mergedGroup[i];
        var lineB = mergedGroup[i + 1];

        if (Math.Abs(lineA.AngleDegrees - lineB.AngleDegrees) > cornerThresholdAngle &&
            Vector2.Distance(lineA.End, lineB.Start) < maxCornerDistance)
        {
          // Create a corner point
          Vector2 cornerPoint = (lineA.End + lineB.Start) / 2;
          mergedGroup[i] = new LineSegment(lineA.Start, cornerPoint, lineA.AngleDegrees, lineA.AngleRadians, true, false);
          mergedGroup[i + 1] = new LineSegment(cornerPoint, lineB.End, lineB.AngleDegrees, lineB.AngleRadians, true, false);
        }
      }
    }





    private bool LinesAreAligned(LineSegment lineA, LineSegment lineB, float angleThreshold, float mergeDistanceThreshold)
    {
      // ** Step 1: Check if the angles are similar **
      float angleDifference = Math.Abs(lineA.AngleDegrees - lineB.AngleDegrees);
      if (angleDifference > angleThreshold && angleDifference < (180 - angleThreshold))
        return false; // Not aligned

      // ** Step 2: Check if lines are close enough to be merged **
      float distanceStartToStart = Vector2.Distance(lineA.Start, lineB.Start);
      float distanceStartToEnd = Vector2.Distance(lineA.Start, lineB.End);
      float distanceEndToStart = Vector2.Distance(lineA.End, lineB.Start);
      float distanceEndToEnd = Vector2.Distance(lineA.End, lineB.End);

      float minDistance = Math.Min(Math.Min(distanceStartToStart, distanceStartToEnd),
                                   Math.Min(distanceEndToStart, distanceEndToEnd));

      return minDistance < mergeDistanceThreshold;
    }

    private LineSegment ComputeMergedLine(List<LineSegment> group)
    {
      // ** Step 1: Find the leftmost and rightmost points **
      Vector2 start = group.Select(l => l.Start).OrderBy(p => p.X).First();
      Vector2 end = group.Select(l => l.End).OrderBy(p => p.X).Last();

      // ** Step 2: Compute the average angle **
      float avgAngle = group.Select(l => l.AngleDegrees).Average();
      float avgAngleRadians = MathHelper.ToRadians(avgAngle);

      return new LineSegment(start, end, avgAngle, avgAngleRadians, false, false);
    }





  }
}
