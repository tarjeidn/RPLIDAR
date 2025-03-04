using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RPLIDAR_Mapping.Utilities;

namespace RPLIDAR_Mapping.Features.Map.UI
{
  public class Camera
  {
    public float Zoom { get; private set; } = 1f;
    private int MapWidth;
    private int MapHeight;
    private int DestRectHeight;
    private int DestRectWidth;
    private int ScreenWidth;
    private int ScreenHeight;
    private Vector2 DestRectPos;
    public Vector2 Position { get; private set; } = Vector2.Zero;
    private GraphicsDevice _graphicsDevice;

    public Camera(GraphicsDevice graphicsDevice)
    {
      _graphicsDevice = graphicsDevice;
      ScreenWidth = _graphicsDevice.Viewport.Width;
      ScreenHeight = _graphicsDevice.Viewport.Height;
      DestRectHeight = (int)(ScreenHeight * 0.8);
      DestRectWidth = (int)(ScreenHeight * 0.8);
      DestRectPos = new Vector2((int)(ScreenHeight *0.05), (int)(ScreenHeight*0.05));
      MapWidth = AppSettings.Default.MapWindowWidth;
      MapHeight = AppSettings.Default.MapWindowHeight;
    }

    // 🔄 Update zoom from GUI slider
    public void SetZoom(float zoomFactor)
    {
      Zoom = MathHelper.Clamp(1f / zoomFactor, 0.2f, 5f); // Inverted zoom logic
    }

    // 🔄 Center camera on a specific point with aspect ratio correction
    public void CenterOn(Vector2 targetPosition)
    {
      Position = targetPosition;
    }
    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
      // Apply transformation: Move world coordinates relative to camera viewport and scale
      return (worldPosition - Position) * Zoom;
    }

    public Rectangle GetViewportBounds(int gridSize)
    {
      int viewportWidth = (int)(ScreenWidth / Zoom);
      int viewportHeight = (int)(ScreenHeight / Zoom);

      //  Center viewport properly, allowing negative coordinates
      int viewportX = (int)(Position.X - viewportWidth / 2);
      int viewportY = (int)(Position.Y - viewportHeight / 2);

      //  Expand viewport slightly to avoid missing edge grids
      return new Rectangle(viewportX - gridSize, viewportY - gridSize, viewportWidth + 2 * gridSize, viewportHeight + 2 * gridSize);
    }

    public Rectangle GetSourceRectangle()
    {
      //  Compute the visible area size based on zoom
      int sourceWidth = (int)(MapWidth / Zoom);
      int sourceHeight = (int)(MapHeight / Zoom);

      //  Prevent source rectangle from becoming larger than the map texture
      sourceWidth = Math.Clamp(sourceWidth, 1, MapWidth);
      sourceHeight = Math.Clamp(sourceHeight, 1, MapHeight);

      //  Center the view on the device position
      int sourceX = (int)(Position.X - sourceWidth / 2);
      int sourceY = (int)(Position.Y - sourceHeight / 2);

      //  Ensure sourceX and sourceY do not go out of bounds
      sourceX = Math.Clamp(sourceX, 0, MapWidth - sourceWidth);
      sourceY = Math.Clamp(sourceY, 0, MapHeight - sourceHeight);

      return new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);
    }



    public Rectangle GetDestinationRectangle(Vector2? targetPosition = null)
    {
      //  Use default position if no position is provided
      Vector2 position = targetPosition ?? DestRectPos;

      return new Rectangle(
          (int)position.X,
          (int)position.Y,
          DestRectWidth,
          DestRectHeight
      );
    }
  }




  }
