using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Providers
{
  public static class GraphicsDeviceProvider
  {
    public static GraphicsDevice GraphicsDevice { get; private set; }
    public static SpriteBatch SpriteBatch { get; private set; }
    public static FPSCounter FPSCounter { get; private set; }

    public static void Initialize(GraphicsDevice graphicsDevice, FPSCounter fpscounter)
    {
      FPSCounter = fpscounter;
      if (GraphicsDevice == null)
      {
        GraphicsDevice = graphicsDevice;
        SpriteBatch = new SpriteBatch(GraphicsDevice);
      }
    }
    public static void LogCurrentRenderTarget()
    {
      var targets = GraphicsDevice.GetRenderTargets();
      if (targets.Length > 0 && targets[0].RenderTarget != null)
      {
        Debug.WriteLine($"Current Render Target: {targets[0].RenderTarget.Name}");
      } else
      {
        Debug.WriteLine("Current Render Target: Default Screen (null)");
      }
    }

  }
}
