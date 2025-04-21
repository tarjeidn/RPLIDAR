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
    public readonly float RawAngleDegrees;
    public readonly float RawAngleRadians;
    public readonly float DeviceOrientation;
    public readonly Vector2 GlobalPosition;
    public readonly Vector2 RelativePosition;
    public readonly Vector2 DevicePosition;
    public Vector2 EqTileGlobalCenter;
    public Vector2 EqTilePosition;
    public byte Quality;
    public bool IsRingPoint;
    public bool IsInferredRingPoint = false;
    public MapPoint InferredBy = null;
    public (int, int) InferredByGridIndex;

    public MapPoint(float x, float y, float angle, float distance, float radians, float rawRadians, 
      byte quality, float globalX, float globalY, Vector2 devicePosition, float deviceOrientation, bool isRingPoint)
    {
      X = x;
      Y = y;
      Angle = angle;
      Distance = distance;
      Radians = radians;
      Quality = quality;
      GlobalX = globalX;
      GlobalY = globalY;
      RawAngleRadians = rawRadians;
      DeviceOrientation = deviceOrientation;
      this.DevicePosition = new Vector2(devicePosition.X, devicePosition.Y);
      IsRingPoint = isRingPoint;
      GlobalPosition = new Vector2(globalX, globalY);
      RelativePosition = new Vector2(x, y);
    }
  }

}
