using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Map.UI;
using Microsoft.Xna.Framework;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RPLIDAR_Mapping.Utilities
{


  public static class DrawingHelperFunctions
  {
    private static GraphicsDevice GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
    private static Texture2D WhiteTexture = CreateWhitePixel(GraphicsDevice);
    public static void DrawGridPattern(SpriteBatch spriteBatch, RenderTarget2D screen, int lineWidth)
    {
      Camera camera = UtilityProvider.Camera;
      float scaleFactor = MapScaleManager.Instance.ScaleFactor;
      int gridSize = MapScaleManager.Instance.ScaledGridSizePixels;

      // Get _mapCanvas dimensions
      int canvasWidth = screen.Width;
      int canvasHeight = screen.Height;



      //  Find the world origin (0,0) relative to the screen center
      Vector2 worldOrigin = new Vector2(canvasWidth / 2, canvasHeight / 2);

      //  Find the first grid line positions aligned to the grid scale
      float firstVertical = worldOrigin.X % gridSize;
      float firstHorizontal = worldOrigin.Y % gridSize;



      //  Draw vertical grid lines (extra +1 to include rightmost line)
      for (float x = firstVertical; x <= canvasWidth + gridSize; x += gridSize)
      {

        DrawLine(spriteBatch, new Vector2(x, 0), new Vector2(x, canvasHeight), Color.White, lineWidth);
      }

      //  Draw horizontal grid lines (extra +1 to include bottom line)
      for (float y = firstHorizontal; y <= canvasHeight + gridSize; y += gridSize)
      {

        DrawLine(spriteBatch, new Vector2(0, y), new Vector2(canvasWidth, y), Color.White, lineWidth);
      }
    }

    //  Optimized line drawing function
    public static void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int lineWidth)
    {
      float length = Vector2.Distance(start, end);
      float angle = (float)Math.Atan2(end.Y - start.Y, end.X - start.X);

      spriteBatch.Draw(
          WhiteTexture,
          start,
          null,
          color,
          angle,
          Vector2.Zero,
          new Vector2(length, lineWidth),
          SpriteEffects.None,
          0
      );
    }
    private static Texture2D CreateWhitePixel(GraphicsDevice graphicsDevice)
    {
      Texture2D texture = new Texture2D(graphicsDevice, 1, 1);
      texture.SetData(new[] { Color.White });
      return texture;
    }
  }
}

