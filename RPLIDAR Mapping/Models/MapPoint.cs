using Microsoft.Xna.Framework;
using SharpDX.DirectWrite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class MapPoint
  {
    public float X;
    public float Y;
    public readonly float Angle;
    public readonly float Distance;
    public readonly float Radians;
    public readonly float GlobalX;
    public readonly float GlobalY;
    public byte Quality;

    public MapPoint(float x, float y, float angle, float distance, float radians, byte quality, float globalX, float globalY)
    {
      X = x;
      Y = y;
      Angle = angle;
      Distance = distance;
      Radians = radians;
      Quality = quality;
      GlobalX = globalX;
      GlobalY = globalY;
    }
  }

}
