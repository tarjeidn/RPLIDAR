using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;


namespace RPLIDAR_Mapping.Utilities
{
  public static class ContentManagerProvider
  {
    private static Dictionary<string, SpriteFont> fonts = new Dictionary<string, SpriteFont>(); //  Store fonts globally

    private static ContentManager content;
    private static Dictionary<string, Dictionary<string,Dictionary<string, Texture2D>>> textures = new Dictionary<string, Dictionary<string, Dictionary<string, Texture2D>>>();
    private static Texture2D tileTexture;
  

    public static void Initialize(ContentManager contentManager)
    {
      content = contentManager;
      textures["Utility"] = new Dictionary<string,Dictionary<string, Texture2D>>();



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
      }
      else
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
      }
      catch (Exception ex)
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
  }
}
