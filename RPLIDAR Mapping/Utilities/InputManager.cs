using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RPLIDAR_Mapping.Interfaces;
using RPLIDAR_Mapping.Features.Communications;

using RPLIDAR_Mapping.Features.Map.UI;
using RPLIDAR_Mapping.Providers;
using ImGuiNET;

namespace RPLIDAR_Mapping.Utilities
{
  public class InputManager
  {
    private Dictionary<string, bool> _keyStates = new Dictionary<string, bool>();
    private Dictionary<string, double> _keyCooldowns = new Dictionary<string, double>();
    private const double CooldownTime = 500; // 500ms cooldown for each key
    private Device _device;
    private UserSelection _UserSelection;
    private MouseState _previousMouseState;
    private bool _isPanning = false;
    private Vector2 _lastMousePosition;

    public InputManager(Device device)
    {
      _device = device;
      _UserSelection = GUIProvider.UserSelection;
    }

    public void Update(GameTime gameTime)
    {
      double currentTime = gameTime.TotalGameTime.TotalMilliseconds;

      HandleKeyBoard(currentTime);
      HandleMouse();

      
    }
    private void HandleMouse()
    {
      MouseState mouse = Mouse.GetState();
      KeyboardState keyboard = Keyboard.GetState();

      bool ctrlHeld = keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl);


      if (mouse.LeftButton == ButtonState.Pressed && _previousMouseState.LeftButton == ButtonState.Released)
      {
        if (ImGui.IsAnyItemHovered() || ImGui.IsAnyItemActive())
          return; // Prevent clicking through UI

        Vector2 mouseWorldPos = UtilityProvider.Camera.ScreenToWorld(new Vector2(mouse.X, mouse.Y));

        _UserSelection.SelectClusterAtPosition(mouseWorldPos, ctrlHeld);
      }
      if (mouse.LeftButton == ButtonState.Pressed)
      {
        if (!_UserSelection.isSelecting)
        {
          if (ImGui.IsAnyItemHovered() || ImGui.IsAnyItemActive()) return; // 🔥 Prevent clearing when clicking UI

          // Start selection
          _UserSelection.isSelecting = true;
          _UserSelection.selectionStart = new Vector2(mouse.X, mouse.Y);
        }

        // Update the selection box while dragging
        _UserSelection.selectionEnd = new Vector2(mouse.X, mouse.Y);
        _UserSelection.selectionBox = _UserSelection.CreateRectangle(_UserSelection.selectionStart, _UserSelection.selectionEnd);

      } else if (mouse.LeftButton == ButtonState.Released && _UserSelection.isSelecting)
      {
        // Finish selection
        _UserSelection.isSelecting = false;
        _UserSelection.HighlightClustersInSelection(ctrlHeld);
      }
      // Right mouse button panning
      if (mouse.RightButton == ButtonState.Pressed)
      {
        if (!_isPanning)
        {
          _isPanning = true;
          _lastMousePosition = new Vector2(mouse.X, mouse.Y);
        }
        else
        {
          Vector2 currentMousePosition = new Vector2(mouse.X, mouse.Y);
          Vector2 mouseDelta = currentMousePosition - _lastMousePosition;

          // Convert pixel delta to world-space delta
          Vector2 worldDelta = UtilityProvider.Camera.ScreenToWorld(_lastMousePosition) -
                               UtilityProvider.Camera.ScreenToWorld(currentMousePosition);

          UtilityProvider.Camera.Position += worldDelta;
          _lastMousePosition = currentMousePosition;
        }
      }
      else
      {
        _isPanning = false;
      }
      _previousMouseState = mouse;
    }

    private void HandleKeyBoard(double currentTime)
    {
      var pressedKeys = getPressedKeys();
      foreach (string key in pressedKeys)
      {
        // Check if the key is on cooldown
        if (_keyCooldowns.TryGetValue(key, out double lastPressedTime) && currentTime - lastPressedTime < CooldownTime)
        {
          continue; // Skip if still in cooldown
        }

        // Key is pressed and cooldown has passed, trigger action
        if (!_keyStates.ContainsKey(key) || !_keyStates[key])
        {
          // Mark the key as pressed and not in cooldown
          _keyStates[key] = true;
          HandleKeyPress(key);
        }

        // Update cooldown
        _keyCooldowns[key] = currentTime;
      }

      // Reset keys that are no longer being pressed
      foreach (var key in _keyStates.Keys.ToList())
      {
        if (!pressedKeys.Contains(key))
        {
          _keyStates[key] = false;
        }
      }
    }



private void HandleKeyPress(string key)
    {
      if (_device.IsConnected)
      {

        var response = "";
        switch (key)
        {
          case "SPACE":
            Log("pressed space");
            _device.Send("s"); // Start LiDAR
            //response = _device.Receive();
            //Log(response);
            break;
          case "LEFT":
            Log("pressed left");
            break;
          case "RIGHT":
            _device.Send("p"); // stop LiDAR
            //response = _device.Receive();
            //Log(response);
            break;
          case "DOWN":
            Log("pressed down");
            break;
          case "UP":
            Log("pressed up");
            break;
          default:
            Log("pressed no key");
            break;
        }
      }
    }

    public List<string> getPressedKeys()
    {
      List<string> keysPressed = new List<string>();
      KeyboardState kbs = Keyboard.GetState();

      if (kbs.IsKeyDown(Keys.Space))
        keysPressed.Add("SPACE");
      if (kbs.IsKeyDown(Keys.Left))
        keysPressed.Add("LEFT");
      if (kbs.IsKeyDown(Keys.Right))
        keysPressed.Add("RIGHT");
      if (kbs.IsKeyDown(Keys.Down))
        keysPressed.Add("DOWN");
      if (kbs.IsKeyDown(Keys.Up))
        keysPressed.Add("UP");

      return keysPressed;
    }
  }
}
