using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using RPLIDAR_Mapping.Utilities;
using System.Diagnostics;
using SharpDX.Direct2D1.Effects;
using RPLIDAR_Mapping.Features.Communications;

namespace RPLIDAR_Mapping.Features.Map.UI
{
  public class Camera
  {
    public float Zoom { get; private set; } = 1f;
    private int MapWidth;
    private int MapHeight;
    private Vector2 _lastZoomFocus;
    private int ScreenWidth;
    private int ScreenHeight;
    private Vector2 DestRectPos;
    public Vector2 Position { get; private set; } = Vector2.Zero;
    public Vector2 ZoomFocusPoint { get; private set; }
    public Device _device {  get; set; }
    private GraphicsDevice _graphicsDevice;
    private int _gridSize;

    public Camera(GraphicsDevice graphicsDevice)
    {
      _graphicsDevice = graphicsDevice;
      ScreenWidth = _graphicsDevice.Viewport.Width;
      ScreenHeight = _graphicsDevice.Viewport.Height;
      ZoomFocusPoint = Vector2.Zero;
      DestRectPos = new Vector2((int)(ScreenHeight * 0.05), (int)(ScreenHeight * 0.05));
      MapWidth = (int)(ScreenWidth * 0.6);
      MapHeight = (int)(ScreenHeight * 0.8);

      //_gridSize = MapScaleManager.Instance.ScaledGridSizePixels;
      _gridSize = 1000;
    }

    ////  Update zoom from GUI slider
    public void SetZoom(float zoomFactor)
    {
      Zoom = MathHelper.Clamp(zoomFactor, 0.05f, 1f);
    }

    //  Add a getter for debugging
    public Vector2 GetLastZoomFocus()
    {
      return _lastZoomFocus;
    }


    //  Center camera on a specific point with aspect ratio correction
    public void CenterOn(Vector2 targetPosition)
    {
      Position = targetPosition;
    }

    public Vector2 WorldToScreen(Vector2 worldPosition)
    {
      Vector2 screenCenter = GetDestinationRectangle().Center.ToVector2();
      //return screenCenter + (worldPosition - Position) * Zoom;
      float scale = MapScaleManager.Instance.ScaleFactor; // 🔥 Use scale factor
      Vector2 baseOffset = (worldPosition - Position); // Distance from camera position
      Vector2 zoomedOffset = baseOffset * (1f / Zoom); // Apply zoom before scaling
      Vector2 scaledOffset = zoomedOffset / scale; // Scale after zoom
      return screenCenter + scaledOffset;
    }

    public Rectangle WorldToScreen(Rectangle worldRect)
    {
      Vector2 screenPos = WorldToScreen(new Vector2(worldRect.X, worldRect.Y)); // Convert position

      float scale = MapScaleManager.Instance.ScaleFactor;
      float scaledZoom = (1f / Zoom) / scale; // Apply zoom and scaling factor

      int screenWidth = (int)(worldRect.Width * scaledZoom);
      int screenHeight = (int)(worldRect.Height * scaledZoom);

      return new Rectangle((int)screenPos.X, (int)screenPos.Y, screenWidth, screenHeight);
    }
    public Vector2 ScreenToWorld(Vector2 screenPosition)
    {
      Vector2 screenCenter = GetDestinationRectangle().Center.ToVector2();

      float scale = MapScaleManager.Instance.ScaleFactor; // 🔥 Correct scale factor
      float adjustedZoom = Zoom * scale; // 🔥 Ensure zoom applies correctly

      // Reverse the transformation process
      Vector2 scaledOffset = (screenPosition - screenCenter) * scale; // Apply scale
      Vector2 zoomedOffset = scaledOffset * Zoom; // Apply zoom
      Vector2 worldPosition = Position + zoomedOffset; // Convert to world position

      return worldPosition;
    }

    public Rectangle ScreenToWorld(Rectangle screenRect)
    {
      Vector2 worldTopLeft = ScreenToWorld(new Vector2(screenRect.Left, screenRect.Top));
      Vector2 worldBottomRight = ScreenToWorld(new Vector2(screenRect.Right, screenRect.Bottom));

      return new Rectangle(
          (int)worldTopLeft.X,
          (int)worldTopLeft.Y,
          (int)(worldBottomRight.X - worldTopLeft.X),
          (int)(worldBottomRight.Y - worldTopLeft.Y)
      );
    }
    private Vector2 RotatePoint(Vector2 point, float radians)
    {
      float cos = MathF.Cos(radians);
      float sin = MathF.Sin(radians);

      return new Vector2(
          cos * point.X - sin * point.Y,
          sin * point.X + cos * point.Y
      );
    }


    //public Rectangle GetViewportBounds()
    //{
    //  int viewportWidth = (int)(MapWidth * Zoom) + MapScaleManager.Instance.BaseGridSizePixels;
    //  int viewportHeight = (int)(MapHeight * Zoom) + MapScaleManager.Instance.BaseGridSizePixels;
    //  int viewportX = (int)(Position.X - viewportWidth / 2);
    //  int viewportY = (int)(Position.Y - viewportHeight / 2);

    //  return new Rectangle(viewportX, viewportY, viewportWidth, viewportHeight);
    //}

    public Rectangle GetSourceRectangle()
    {
      float scale = MapScaleManager.Instance.ScaleFactor;
      int sourceWidth = (int)((MapWidth * scale) / Zoom);
      int sourceHeight = (int)((MapHeight * scale) / Zoom );
      int sourceX = (int)(Position.X - (sourceWidth / 2));
      int sourceY = (int)(Position.Y - (sourceHeight / 2));

      return new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight);
    }

    public Rectangle GetDestinationRectangle()
    {
      // Do not scale this with zoom, this is a constant size
      return new Rectangle(
          (int)DestRectPos.X,
          (int)DestRectPos.Y,
          (int)(MapWidth), 
          (int)(MapHeight) 
      );
    }
  }
}



  
