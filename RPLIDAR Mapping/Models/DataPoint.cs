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
    public Vector2 GlobalPosition { get; set; }
    public Vector2 EqTilePosition   { get; set; }
    public Vector2 EqTileGlobalCenter { get; set; }
    public DataPoint(float angle, float distance, byte quality, Vector2 globalpos, Vector2 eqTilePosition, Vector2 eqTileGlobalCenter)
    {
      Angle = angle;
      Distance = distance;
      Quality = quality;
      GlobalPosition = globalpos;
      EqTilePosition = eqTilePosition;
      EqTileGlobalCenter = eqTileGlobalCenter;
    }
  }
}
