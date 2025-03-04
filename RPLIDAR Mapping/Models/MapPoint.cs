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
    //  Update drawing coordinates when zoom changes (but NOT global coordinates)
    //  Update drawing coordinates when zoom OR GridScaleFactor changes
    public void UpdateDrawingPosition(Rectangle view, float zoom, float scaleFactor)
    {
      X = (GlobalX - view.X) * zoom * scaleFactor;
      Y = (GlobalY - view.Y) * zoom * scaleFactor;
    }

    //public void UpdateDrawingPosition(Rectangle view, float zoom)
    //{
    //  X = (GlobalX - view.X) * zoom;
    //  Y = (GlobalY - view.Y) * zoom;
    //}


  }

}
