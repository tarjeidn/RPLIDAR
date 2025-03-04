using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Map.Algorithms;
using RPLIDAR_Mapping.Features.Map.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Utilities
{
  public static class UtilityProvider
  {
    public static FPSCounter FPSCounter { get; set; }
    public static Camera Camera { get; set; }
    public static void Initialize(GraphicsDevice gd)
    {
      FPSCounter = new FPSCounter();
      Camera = new Camera(gd);
    }
  }
}
