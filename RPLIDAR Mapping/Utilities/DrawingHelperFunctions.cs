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
    public static void DrawGridPattern(SpriteBatch spriteBatch, Rectangle destRect, Vector2 deviceScreenPos, int lineThickness, float alpha = 0.3f)
    {
      Camera camera = UtilityProvider.Camera;

      // Ensure alpha is within valid range (0 = transparent, 1 = fully visible)
      alpha = MathHelper.Clamp(alpha, 0.0f, 1.0f);

      // 🟢 Get the scaled grid size in screen space
      int screenGridSize = (int)(MapScaleManager.Instance.ScaledGridSizePixels / camera.Zoom);
      if (screenGridSize <= 0) return; // Prevent division by zero

      // 🟢 Align the grid **relative to the device position**
      float startX = destRect.Left + ((deviceScreenPos.X - destRect.Left) % screenGridSize);
      float startY = destRect.Top + ((deviceScreenPos.Y - destRect.Top) % screenGridSize);

      // 🟢 Ensure the grid stays within destRect bounds (fixes (0,0) issue)
      if (startX < destRect.Left) startX += screenGridSize;
      if (startY < destRect.Top) startY += screenGridSize;

      // Set the transparent grid color
      Color gridColor = Color.White * alpha;

      // 🟢 Draw vertical grid lines
      for (float x = startX; x < destRect.Right; x += screenGridSize)
      {
        Vector2 screenStart = new Vector2(x, destRect.Top);
        Vector2 screenEnd = new Vector2(x, destRect.Bottom);
        ContentManagerProvider.DrawLine(spriteBatch, screenStart, screenEnd, gridColor, lineThickness);
      }

      // 🟢 Draw horizontal grid lines
      for (float y = startY; y < destRect.Bottom; y += screenGridSize)
      {
        Vector2 screenStart = new Vector2(destRect.Left, y);
        Vector2 screenEnd = new Vector2(destRect.Right, y);
        ContentManagerProvider.DrawLine(spriteBatch, screenStart, screenEnd, gridColor, lineThickness);
      }
    }


    public static void DrawRectangleBorder(SpriteBatch spriteBatch, Rectangle rect, int thickness, Color borderColor)
    {


      //  Top border
      spriteBatch.Draw(WhiteTexture, new Rectangle(rect.X, rect.Y, rect.Width, thickness), borderColor);

      //  Bottom border
      spriteBatch.Draw(WhiteTexture, new Rectangle(rect.X, rect.Y + rect.Height - thickness, rect.Width, thickness), borderColor);

      //  Left border
      spriteBatch.Draw(WhiteTexture, new Rectangle(rect.X, rect.Y, thickness, rect.Height), borderColor);

      //  Right border
      spriteBatch.Draw(WhiteTexture, new Rectangle(rect.X + rect.Width - thickness, rect.Y, thickness, rect.Height), borderColor);
    }




    //  Optimized line drawing function
    public static void DrawLine(SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int lineWidth, float alpha=1f)
    {
      float length = Vector2.Distance(start, end);
      float angle = (float)Math.Atan2(end.Y - start.Y, end.X - start.X);
      alpha = MathHelper.Clamp(alpha, 0.0f, 1.0f);
      color = color * alpha;
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
    public static Vector2 GetScreenBorderIntersection(Vector2 tileScreenPos, Rectangle destRect, float lidarAngleRadians)
    {
      // 🔥 Compute direction from LiDAR angle
      Vector2 direction = new Vector2(
          (float)Math.Cos(lidarAngleRadians),
          (float)Math.Sin(lidarAngleRadians)
      );

      // 🔥 Get screen dimensions


      // 🔥 Compute intersection with screen borders
      float tX = (direction.X > 0) ? (destRect.Width - tileScreenPos.X) / direction.X :
                                     (0 - tileScreenPos.X) / direction.X;
      float tY = (direction.Y > 0) ? (destRect.Height - tileScreenPos.Y) / direction.Y :
                                     (0 - tileScreenPos.Y) / direction.Y;

      float t = Math.Min(tX, tY); // Choose the first intersection (closest)

      // 🔥 Compute the final point at the screen border
      return tileScreenPos + direction * t;
    }

    private static Texture2D CreateWhitePixel(GraphicsDevice graphicsDevice)
    {
      Texture2D texture = new Texture2D(graphicsDevice, 1, 1);
      texture.SetData(new[] { Color.White });
      return texture;
    }
  }
}

