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
using RPLIDAR_Mapping.Providers;

namespace RPLIDAR_Mapping.Utilities
{


  public static class DrawingHelperFunctions
  {
    private static GraphicsDevice GraphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
    public static Texture2D WhiteTexture = CreateWhitePixel(GraphicsDevice);
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

    public static bool ClipLineToRectangle(ref Vector2 start, ref Vector2 end, Rectangle rect)
    {
      const int INSIDE = 0, LEFT = 1, RIGHT = 2, BOTTOM = 4, TOP = 8;

      int ComputeOutCode(Vector2 p)
      {
        int code = INSIDE;
        if (p.X < rect.Left) code |= LEFT;
        else if (p.X > rect.Right) code |= RIGHT;
        if (p.Y < rect.Top) code |= TOP;
        else if (p.Y > rect.Bottom) code |= BOTTOM;
        return code;
      }

      int outcodeStart = ComputeOutCode(start);
      int outcodeEnd = ComputeOutCode(end);

      while (true)
      {
        if ((outcodeStart | outcodeEnd) == 0)
        {
          // Both points inside
          return true;
        } else if ((outcodeStart & outcodeEnd) != 0)
        {
          // Both points outside (completely outside)
          return false;
        } else
        {
          // One point is inside, the other is outside; clip the line
          int outcodeOut = outcodeStart != 0 ? outcodeStart : outcodeEnd;
          Vector2 newPoint = Vector2.Zero;

          if ((outcodeOut & TOP) != 0)
          {
            newPoint.X = start.X + (end.X - start.X) * (rect.Top - start.Y) / (end.Y - start.Y);
            newPoint.Y = rect.Top;
          } else if ((outcodeOut & BOTTOM) != 0)
          {
            newPoint.X = start.X + (end.X - start.X) * (rect.Bottom - start.Y) / (end.Y - start.Y);
            newPoint.Y = rect.Bottom;
          } else if ((outcodeOut & RIGHT) != 0)
          {
            newPoint.Y = start.Y + (end.Y - start.Y) * (rect.Right - start.X) / (end.X - start.X);
            newPoint.X = rect.Right;
          } else if ((outcodeOut & LEFT) != 0)
          {
            newPoint.Y = start.Y + (end.Y - start.Y) * (rect.Left - start.X) / (end.X - start.X);
            newPoint.X = rect.Left;
          }

          if (outcodeOut == outcodeStart)
          {
            start = newPoint;
            outcodeStart = ComputeOutCode(start);
          } else
          {
            end = newPoint;
            outcodeEnd = ComputeOutCode(end);
          }
        }
      }
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

