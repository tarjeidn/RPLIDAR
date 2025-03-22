using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.GridModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class LineSegment
  {
    public Vector2 Start { get; set; }
    public Vector2 End { get; set; }
    public LineSegment ParentLine { get; set; }
    //public HashSet<Tile> ParentCluster { get; set; }
    public TileCluster ParentCluster { get; set; }
    public float AngleDegrees { get; set; }
    public float AngleRadians { get; set; }
    public bool IsPermanent { get; set; } = false;
    public bool IsInferred { get; set; } = false; // For inferred lines

    public LineSegment(Vector2 start, Vector2 end, float angleDegrees, float angleRadians, bool isPermanent)
    {
      Start = start;
      End = end;
      AngleDegrees = angleDegrees;
      AngleRadians = angleRadians;
      IsPermanent = isPermanent;


    }
    public LineSegment(Vector2 start, Vector2 end, float angleDegrees, float angleRadians, bool isPermanent, bool isInferred)
    {
      Start = start;
      End = end;
      AngleDegrees = angleDegrees;
      AngleRadians = angleRadians;
      IsPermanent = isPermanent;
      IsInferred = isInferred;


    }

    public LineSegment(Vector2 start, Vector2 end, float angleDegrees, float angleRadians, bool isPermanent, bool isInferred, LineSegment parentLine)
    {
      Start = start;
      End = end;
      AngleDegrees = angleDegrees;
      AngleRadians = angleRadians;
      IsPermanent = isPermanent;
      IsInferred = isInferred;
      ParentLine = parentLine;
    }

    public LineSegment(Vector2 start, Vector2 end, float angleDegrees, float angleRadians, bool isPermanent, TileCluster parentCluster)
    {
      Start = start;
      End = end;
      AngleDegrees = angleDegrees;
      AngleRadians = angleRadians;
      IsPermanent = isPermanent;
      ParentCluster = parentCluster;
    }
  }
}
