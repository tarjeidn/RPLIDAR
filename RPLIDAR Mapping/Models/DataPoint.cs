using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Models
{
  public class DataPoint
  {
    public float Angle {  get; set; }
    public float Distance { get; set; }
    public byte Quality { get; set; }
    public Vector2 OriginalDevicePosition {  get; set; }
    public Vector2 GlobalPosition { get; set; }
    public Vector2 EqTilePosition   { get; set; }
    public Vector2 EqTileGlobalCenter { get; set; }
    public Vector2 ComputedDevicePositionOffset { get; set; } = Vector2.Zero;
    public Vector2 VelocityAtHit { get; set; }
    public Vector2 DevicePositionAtHit { get; set; }
    public float Yaw { get; set; }

    public float Vx { get; set; }  // Acceleration x (optional)
    public float Vy { get; set; }  // Acceleration y
    public uint TimeStamp { get; set; }
    public bool InTrustedTile { get; set; } = false;
    public DataPoint(float angle, float distance, byte quality, Vector2 globalpos, Vector2 eqTilePosition, Vector2 eqTileGlobalCenter, float yaw
      , float vx = 0, float vy = 0, uint timeStamp = 0, bool inTrustedTile = false)
    {
      Angle = angle;
      Distance = distance;
      Quality = quality;
      GlobalPosition = globalpos;
      EqTilePosition = eqTilePosition;
      EqTileGlobalCenter = eqTileGlobalCenter;
      Yaw = yaw;
      Vx = vx;
      Vy = vy;
      VelocityAtHit = new Vector2(vy, vx); // vx and vy are inverted due to physical orientation
      TimeStamp = timeStamp;
      InTrustedTile = inTrustedTile;
    }
  }
}
