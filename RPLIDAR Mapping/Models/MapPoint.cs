using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class MapPoint
  {
    public readonly float X;
    public readonly float Y;
    public readonly float Angle;
    public readonly float Distance;
    public readonly float Radians;
    public byte Quality { get; set; }
    public MapPoint(float x, float y, float angle, float distance, float radians, byte quality)
    {
      X = x;
      Y = y;
      Angle = angle;
      Distance = distance;
      Radians = radians;
      Quality = quality;
    }
  }
}
