using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using RPLIDAR_Mapping.Features.Map.GridModel;
using RPLIDAR_Mapping.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RPLIDAR_Mapping.Features.Map.UI.Overlays
{
  public class DistanceOverlay
  {
    private readonly SpriteFont _font;
    private readonly Color _axisColor = Color.White;
    private readonly Color _textColor = Color.Yellow;
    private readonly MapRenderer _mapRenderer;
    private readonly GridManager _gridManager;
    private const int LineThickness = 2;
    private bool _isEnabled = true; // Toggle for showing/hiding overlay
    private SpriteBatch _spriteBatch;
    private Camera _camera;

    public DistanceOverlay(MapRenderer mr)
    {
      _mapRenderer = mr;
      _gridManager = mr._GridManager;
      _camera = UtilityProvider.Camera;
      _spriteBatch = GraphicsDeviceProvider.SpriteBatch;
      _font = ContentManagerProvider.GetFont("DebugFont"); // Load the font
    }

    public void ToggleOverlay() => _isEnabled = !_isEnabled;

    public void Draw()
    {
      if (!_isEnabled) return; // Skip drawing if disabled

      Rectangle viewport = _camera.GetViewportBounds(_gridManager._gridSizePixels);
      float scaleFactor = _gridManager.GridScaleFactor;
      float markerSpacing = 100 * scaleFactor;

      Vector2 zeroScreenPos = _camera.WorldToScreen(Vector2.Zero);

      // Draw X-axis
      _spriteBatch.DrawLine(
          new Vector2(viewport.Left, zeroScreenPos.Y),
          new Vector2(viewport.Right, zeroScreenPos.Y),
          _axisColor, LineThickness
      );

      // Draw Y-axis
      _spriteBatch.DrawLine(
          new Vector2(zeroScreenPos.X, viewport.Top),
          new Vector2(zeroScreenPos.X, viewport.Bottom),
          _axisColor, LineThickness
      );

      // Draw distance markers
      for (float x = viewport.Left; x < viewport.Right; x += markerSpacing)
      {
        Vector2 worldPos = new Vector2(x, 0);
        Vector2 screenPos = _camera.WorldToScreen(worldPos);
        _spriteBatch.DrawLine(screenPos, screenPos + new Vector2(0, 5), _axisColor, 1);
        _spriteBatch.DrawString(_font, $"{x / scaleFactor}m", screenPos + new Vector2(2, 5), _textColor);
      }

      for (float y = viewport.Top; y < viewport.Bottom; y += markerSpacing)
      {
        Vector2 worldPos = new Vector2(0, y);
        Vector2 screenPos = _camera.WorldToScreen(worldPos);
        _spriteBatch.DrawLine(screenPos, screenPos + new Vector2(5, 0), _axisColor, 1);
        _spriteBatch.DrawString(_font, $"{y / scaleFactor}m", screenPos + new Vector2(5, -10), _textColor);
      }
    }

  }

}
