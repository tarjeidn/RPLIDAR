using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System;
using RPLIDAR_Mapping.Utilities;
using System.Diagnostics;
using SharpDX.Direct2D1.Effects;

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

      _gridSize = MapScaleManager.Instance.ScaledGridSizePixels;
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



  
