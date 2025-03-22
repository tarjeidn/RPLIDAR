using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using RPLIDAR_Mapping.Features.Map.UI;


namespace RPLIDAR_Mapping.Providers
{
  public static class ContentManagerProvider
  {
    private static Dictionary<string, SpriteFont> fonts = new Dictionary<string, SpriteFont>(); //  Store fonts globally

    private static ContentManager content;
    private static Dictionary<string, Dictionary<string, Dictionary<string, Texture2D>>> textures = new Dictionary<string, Dictionary<string, Dictionary<string, Texture2D>>>();
    private static Texture2D tileTexture;
    private static Texture2D WhiteTexture;

    private static BasicEffect _lineEffect;
    private static GraphicsDevice _graphicsDevice;
    //  Buffer to store lines before drawing
    private static List<LineVertex> _lineVertices = new List<LineVertex>();

    public static void Initialize(ContentManager contentManager)
    {
      _graphicsDevice = GraphicsDeviceProvider.GraphicsDevice;
      content = contentManager;
      textures["Utility"] = new Dictionary<string, Dictionary<string, Texture2D>>();
      WhiteTexture = CreateWhitePixel(_graphicsDevice);
      _lineEffect = new BasicEffect(_graphicsDevice)
      {
        VertexColorEnabled = true,
        Projection = Matrix.CreateOrthographicOffCenter(0, _graphicsDevice.Viewport.Width,
                                                    _graphicsDevice.Viewport.Height, 0,
                                                    0, 1)
      };

    }

    private static Texture2D LoadTexture(string path)
    {
      string[] parts = path.Split('/');
      if (parts.Length != 3)
      {
        throw new ArgumentException($"Invalid path format: {path}. Expected format: mainFolder/subFolder/fileName");
      }

      string mainFolder = parts[0];
      string subFolder = parts[1];
      string fileName = parts[2];

      // Initialize the mainFolder dictionary if it doesn't exist
      if (!textures.ContainsKey(mainFolder))
      {
        textures[mainFolder] = new Dictionary<string, Dictionary<string, Texture2D>>();
      }

      // Initialize the subFolder dictionary if it doesn't exist
      if (!textures[mainFolder].ContainsKey(subFolder))
      {
        textures[mainFolder][subFolder] = new Dictionary<string, Texture2D>();
      }

      // Load the texture if it doesn't exist
      if (!textures[mainFolder][subFolder].ContainsKey(fileName))
      {
        textures[mainFolder][subFolder][fileName] = content.Load<Texture2D>(path);
      }

      return textures[mainFolder][subFolder][fileName];
    }


    public static Texture2D GetTexture(string path)
    {
      if (path == "tiletexture")
      {
        if (tileTexture == null)
        {
          tileTexture = new Texture2D(GraphicsDeviceProvider.GraphicsDevice, 1, 1);
          tileTexture.SetData(new[] { Color.White });
        }
        return tileTexture;
      }
      string[] parts = path.Split('/');
      if (parts.Length != 3)
      {
        throw new ArgumentException($"Invalid path format: {path}. Expected format: mainFolder/subFolder/fileName");
      }

      string mainFolder = parts[0];
      string subFolder = parts[1];
      string fileName = parts[2];

      // Check if the path exists in the dictionary
      if (textures.ContainsKey(mainFolder) &&
          textures[mainFolder].ContainsKey(subFolder) &&
          textures[mainFolder][subFolder].ContainsKey(fileName))
      {
        return textures[mainFolder][subFolder][fileName];
      } else
      {
        return LoadTexture(path);
      }

      // If the texture is not found, return null or throw an exception
      throw new KeyNotFoundException($"Texture not found for path: {path}");
    }
    public static void LoadFont(string fontName, string path)
    {
      try
      {
        if (content == null)
        {
          throw new Exception("ContentManager is NULL! Did you call ContentManagerProvider.Initialize(content)?");
        }

        if (fonts == null)
        {
          throw new Exception("Fonts dictionary is NULL! It should have been initialized.");
        }

        if (!fonts.ContainsKey(fontName))
        {
          fonts[fontName] = content.Load<SpriteFont>(path);
          Console.WriteLine($"Successfully loaded font: {fontName} from {path}");
        }
      } catch (Exception ex)
      {
        Console.WriteLine($"Error loading font '{fontName}' from '{path}': {ex.Message}");
      }
    }

    //  Retrieve a font globally
    public static SpriteFont GetFont(string fontName)
    {
      if (fonts.ContainsKey(fontName))
      {
        return fonts[fontName];
      }

      throw new KeyNotFoundException($"Font '{fontName}' not found. Make sure it is loaded.");
    }
    public static void DrawRenderTargetBorder(SpriteBatch spriteBatch, Rectangle target, int thickness, Color borderColor)
    {

      int width = target.Width;
      int height = target.Height;



      // Top border
      spriteBatch.Draw(WhiteTexture, new Rectangle(0, 0, width, thickness), borderColor);
      // Bottom border
      spriteBatch.Draw(WhiteTexture, new Rectangle(0, height - thickness, width, thickness), borderColor);
      // Left border
      spriteBatch.Draw(WhiteTexture, new Rectangle(0, 0, thickness, height), borderColor);
      // Right border
      spriteBatch.Draw(WhiteTexture, new Rectangle(width - thickness, 0, thickness, height), borderColor);


    }


    //public static void DrawRectangleBorder(SpriteBatch spriteBatch, Rectangle rect, int thickness, Color borderColor)
    //{
    //  Camera camera = UtilityProvider.Camera;
    //  Rectangle viewportBounds = camera.GetViewportBounds(rect.Width); // Get current viewport bounds

    //  //  If no part of the grid is visible, skip drawing
    //  if (!viewportBounds.Intersects(rect)) return;

    //  int x = rect.X;
    //  int y = rect.Y;
    //  int width = rect.Width;
    //  int height = rect.Height;

    //  //  Adjust clipping to make sure partially visible borders are drawn correctly

    //  // Top border (even if rect.Y is above the viewport, adjust it down)
    //  int topY = Math.Max(y, viewportBounds.Y);
    //  int topStartX = Math.Max(x, viewportBounds.X);
    //  int topEndX = Math.Min(x + width, viewportBounds.X + viewportBounds.Width);
    //  if (topEndX > topStartX)
    //    spriteBatch.Draw(WhiteTexture, new Rectangle(topStartX, topY, topEndX - topStartX, thickness), borderColor);

    //  // Bottom border
    //  int bottomY = y + height - thickness; // Always stays at rect's bottom
    //  int bottomStartX = Math.Max(x, viewportBounds.X);
    //  int bottomEndX = Math.Min(x + width, viewportBounds.X + viewportBounds.Width);
    //  if (bottomEndX > bottomStartX)
    //    spriteBatch.Draw(WhiteTexture, new Rectangle(bottomStartX, bottomY, bottomEndX - bottomStartX, thickness), borderColor);

    //  // Left border (even if rect.X is left of the viewport, adjust it right)
    //  int leftX = Math.Max(x, viewportBounds.X);
    //  int leftStartY = Math.Max(y, viewportBounds.Y);
    //  int leftEndY = Math.Min(y + height, viewportBounds.Y + viewportBounds.Height);
    //  if (leftEndY > leftStartY)
    //    spriteBatch.Draw(WhiteTexture, new Rectangle(leftX, leftStartY, thickness, leftEndY - leftStartY), borderColor);

    //  // Right border
    //  int rightX = x + width - thickness; // Always stays at rect's right
    //  int rightStartY = Math.Max(y, viewportBounds.Y);
    //  int rightEndY = Math.Min(y + height, viewportBounds.Y + viewportBounds.Height);
    //  if (rightEndY > rightStartY)
    //    spriteBatch.Draw(WhiteTexture, new Rectangle(rightX, rightStartY, thickness, rightEndY - rightStartY), borderColor);
    //}



    struct LineVertex
    {
      public Vector2 Position;
      public Color Color;
    }

    //  **Optimized Rotation Function** (No Matrix, just fast math)
    private static Vector2 RotatePointFast(float x, float y, float cos, float sin)
    {
      return new Vector2(x * cos - y * sin, x * sin + y * cos);
    }
    public static void QueueRotatedRectangleBorder(Vector2 center, int width, int height, float angle, Color color)
    {
      float cos = (float)Math.Cos(-angle);
      float sin = (float)Math.Sin(-angle);

      Vector2 topLeft = RotatePointFast(-width / 2, -height / 2, cos, sin) + center;
      Vector2 topRight = RotatePointFast(width / 2, -height / 2, cos, sin) + center;
      Vector2 bottomLeft = RotatePointFast(-width / 2, height / 2, cos, sin) + center;
      Vector2 bottomRight = RotatePointFast(width / 2, height / 2, cos, sin) + center;

      //  Instead of calling DrawLine, we queue it
      QueueLine(topLeft, topRight, color);
      QueueLine(topRight, bottomRight, color);
      QueueLine(bottomRight, bottomLeft, color);
      QueueLine(bottomLeft, topLeft, color);
    }






    public static void QueueLine(Vector2 start, Vector2 end, Color color)
    {
      _lineVertices.Add(new LineVertex { Position = start, Color = color });
      _lineVertices.Add(new LineVertex { Position = end, Color = color });
    }

    public static void DrawQueuedLines(SpriteBatch spriteBatch)
    {
      if (_lineVertices.Count == 0) return;

      Texture2D whitePixel = WhiteTexture;

      for (int i = 0; i < _lineVertices.Count; i += 2)
      {
        Vector2 start = _lineVertices[i].Position;
        Vector2 end = _lineVertices[i + 1].Position;
        float distance = Vector2.Distance(start, end);
        float angle = (float)Math.Atan2(end.Y - start.Y, end.X - start.X);
        Vector2 lineCenter = (start + end) / 2;

        spriteBatch.Draw(
            whitePixel,
            lineCenter,
            null,
            _lineVertices[i].Color,
            angle,
            new Vector2(0.5f, 0.5f),
            new Vector2(distance, 2),
            SpriteEffects.None,
            0f
        );
      }

      _lineVertices.Clear(); //  Remove all lines AFTER drawing them
    }
    public static void DrawLine(this SpriteBatch spriteBatch, Vector2 start, Vector2 end, Color color, int thickness = 2)
    {
      Texture2D whitePixel = WhiteTexture;

      Vector2 edge = end - start;
      float angle = (float)Math.Atan2(edge.Y, edge.X);
      float length = edge.Length();

      spriteBatch.Draw(whitePixel, start, null, color, angle, Vector2.Zero, new Vector2(length, thickness), SpriteEffects.None, 0);
    }




    private static Texture2D CreateWhitePixel(GraphicsDevice graphicsDevice)
    {
      Texture2D texture = new Texture2D(graphicsDevice, 1, 1);
      texture.SetData(new[] { Color.White });
      return texture;
    }
  }
}
