namespace DontStarveRuneScape.Camera;

using DontStarveRuneScape.Config;
using DontStarveRuneScape.Core;
using DontStarveRuneScape.Input;
using Silk.NET.Maths;

/// <summary>
/// Orbital camera with independent yaw (orbit), pitch (tilt), zoom, and pan.
/// </summary>
public sealed class Camera
{
    private float _yaw = 0f;           // Horizontal orbit (radians)
    private float _pitch = 0.5236f;    // Vertical tilt (radians) - default ~30°
    private World.TileMap? _world;     // For focus-elevation anchoring
    private float _focusElev = 0f;     // Ground elevation under the camera focus
    private float _zoom = Constants.CameraZoomDefault;
    private float _panX = 0f;
    private float _panY = 0f;

    private Player? _player;
    private int _screenWidth;
    private int _screenHeight;

    public float Yaw => _yaw;
    public float Pitch => _pitch;
    public float Zoom => _zoom;
    public float PanX => _panX;
    public float PanY => _panY;

    /// <summary>
    /// Vertical anchor of the camera focus as a fraction of screen height.
    /// At low pitch the world compresses near the horizon and spreads below,
    /// so the focus sits lower and the view covers both ahead and behind.
    /// Near top-down it centers.
    /// </summary>
    public float AnchorY
    {
        get
        {
            float pitchMin = Constants.CameraPitchMin * MathF.PI / 180f;
            float pitchMax = Constants.CameraPitchMax * MathF.PI / 180f;
            float t = (_pitch - pitchMin) / Math.Max(1e-5f, pitchMax - pitchMin);
            return 0.72f - 0.22f * Math.Clamp(t, 0f, 1f);
        }
    }

    public Camera(int screenWidth, int screenHeight)
    {
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;
    }

    /// <summary>
    /// Set the player to follow.
    /// </summary>
    public void SetPlayer(Player? player)
    {
        _player = player;
    }

    /// <summary>World reference used to read the ground elevation at the focus point.</summary>
    public void SetWorld(World.TileMap? world)
    {
        _world = world;
    }

    /// <summary>
    /// Update camera position and handle input.
    /// </summary>
    public void Update(float dt, InputState inputState)
    {
        // Orbit (yaw) - Left/Right arrows
        if (inputState.OrbitCCW) _yaw -= Constants.CameraOrbitSpeed * dt * MathF.PI / 180f;
        if (inputState.OrbitCW) _yaw += Constants.CameraOrbitSpeed * dt * MathF.PI / 180f;

        // Tilt (pitch) - Up/Down arrows
        if (inputState.OrbitTiltUp) _pitch += Constants.CameraTiltSpeed * dt * MathF.PI / 180f;
        if (inputState.OrbitTiltDown) _pitch -= Constants.CameraTiltSpeed * dt * MathF.PI / 180f;

        // Clamp pitch
        float pitchMin = Constants.CameraPitchMin * MathF.PI / 180f;
        float pitchMax = Constants.CameraPitchMax * MathF.PI / 180f;
        _pitch = Math.Clamp(_pitch, pitchMin, pitchMax);

        // Zoom - mouse wheel handled separately via InputState.ZoomDelta
        if (inputState.ZoomDelta != 0)
        {
            _zoom *= 1f - inputState.ZoomDelta * 0.1f;
            _zoom = Math.Clamp(_zoom, Constants.CameraZoomMin, Constants.CameraZoomMax);
        }

        // Pan - handled by arrow keys when not orbiting (or separate keys)
        // For now, camera centers on player with offset
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (_player == null) return;

        // Camera orbits around player
        // The pan offsets are in world space
        _panX = _player.WorldX;
        _panY = _player.WorldY;

        // Elevation anchoring: keep the player's actual ground point fixed at
        // the anchor instead of the elevation-0 plane. Without this, the
        // player's feet drift up-screen by elev*ZScale*sin(pitch)*zoom.
        if (_world != null)
        {
            float txf = _panX / Constants.TileSize;
            float tyf = _panY / Constants.TileSize;
            var tile = _world.GetTile((int)txf, (int)tyf);
            if (tile != null)
            {
                _focusElev = tile.Biome?.Id == "water"
                    ? tile.GetSurfaceElevation() - 0.8f // camera floats on the waterline
                    : tile.GetElevationAt(txf - (int)txf, tyf - (int)tyf);
            }
        }
    }

    /// <summary>
    /// Convert world coordinates to screen coordinates.
    /// </summary>
    public Vector2D<float> WorldToScreen(float worldX, float worldY, float elevation = 0f)
    {
        if (_player == null) return new Vector2D<float>(worldX, worldY);

        // Translate to camera-relative
        float relX = worldX - _panX;
        float relY = worldY - _panY;

        // Apply elevation offset (height displacement), relative to the
        // ground elevation at the camera focus so the focus tile stays anchored.
        float elevOffset = (elevation - _focusElev) * Constants.ZScale * Constants.TerrainHeightScale;

        // Rotate by yaw
        float cosYaw = MathF.Cos(_yaw);
        float sinYaw = MathF.Sin(_yaw);
        float rotX = relX * cosYaw - relY * sinYaw;
        float rotY = relX * sinYaw + relY * cosYaw;

        // Apply pitch (tilt) - project Y with elevation
        float cosPitch = MathF.Cos(_pitch);
        float sinPitch = MathF.Sin(_pitch);
        float projectedY = rotY * cosPitch - elevOffset * sinPitch;

        // Apply zoom and center on screen (Y anchored lower at shallow pitch)
        float screenX = _screenWidth * 0.5f + rotX * _zoom;
        float screenY = _screenHeight * AnchorY + projectedY * _zoom;

        return new Vector2D<float>(screenX, screenY);
    }

    /// <summary>
    /// Convert screen coordinates to world coordinates (at elevation 0).
    /// </summary>
    public (float WorldX, float WorldY) ScreenToWorld(float screenX, float screenY)
    {
        if (_player == null) return (screenX, screenY);

        // Reverse the transform
        float relScreenX = (screenX - _screenWidth * 0.5f) / _zoom;
        float relScreenY = (screenY - _screenHeight * AnchorY) / _zoom;

        // Reverse pitch (approximate, ignoring elevation)
        float cosPitch = MathF.Cos(_pitch);
        float rotY = relScreenY / cosPitch;
        float rotX = relScreenX;

        // Reverse yaw
        float cosYaw = MathF.Cos(-_yaw);
        float sinYaw = MathF.Sin(-_yaw);
        float worldRelX = rotX * cosYaw - rotY * sinYaw;
        float worldRelY = rotX * sinYaw + rotY * cosYaw;

        return (_panX + worldRelX, _panY + worldRelY);
    }

    /// <summary>
    /// Get the view rectangle in world coordinates for culling.
    /// </summary>
    public (float Left, float Top, float Right, float Bottom) GetViewRect()
    {
        // Get screen corners in world space
        var tl = ScreenToWorld(0, 0);
        var tr = ScreenToWorld(_screenWidth, 0);
        var bl = ScreenToWorld(0, _screenHeight);
        var br = ScreenToWorld(_screenWidth, _screenHeight);

        float left = Math.Min(Math.Min(tl.WorldX, tr.WorldX), Math.Min(bl.WorldX, br.WorldX));
        float right = Math.Max(Math.Max(tl.WorldX, tr.WorldX), Math.Max(bl.WorldX, br.WorldX));
        float top = Math.Min(Math.Min(tl.WorldY, tr.WorldY), Math.Min(bl.WorldY, br.WorldY));
        float bottom = Math.Max(Math.Max(tl.WorldY, tr.WorldY), Math.Max(bl.WorldY, br.WorldY));

        return (left, top, right, bottom);
    }

    /// <summary>Level-of-detail tier derived from zoom (hysteresis to avoid popping).</summary>
    public enum LodTier { Nearest, Mid, Far }

    private LodTier _lodTier = LodTier.Nearest;

    public LodTier Tier
    {
        get
        {
            const float Hyst = 0.05f;
            float near = Constants.LodNearZoom;
            float far = Constants.LodFarZoom;
            _lodTier = _lodTier switch
            {
                LodTier.Nearest when _zoom < near * (1f - Hyst) =>
                    _zoom < far * (1f - Hyst) ? LodTier.Far : LodTier.Mid,
                LodTier.Mid when _zoom > near * (1f + Hyst) => LodTier.Nearest,
                LodTier.Mid when _zoom < far * (1f - Hyst) => LodTier.Far,
                LodTier.Far when _zoom > far * (1f + Hyst) =>
                    _zoom > near * (1f + Hyst) ? LodTier.Nearest : LodTier.Mid,
                _ => _lodTier,
            };
            return _lodTier;
        }
    }

    /// <summary>Override view angles/zoom directly (used by smoketest tooling).</summary>
    public void SetViewAngles(float? yawDeg = null, float? pitchDeg = null, float? zoom = null)
    {
        if (yawDeg.HasValue) _yaw = yawDeg.Value * MathF.PI / 180f;
        if (pitchDeg.HasValue)
        {
            float pitchMin = Constants.CameraPitchMin * MathF.PI / 180f;
            float pitchMax = Constants.CameraPitchMax * MathF.PI / 180f;
            _pitch = Math.Clamp(pitchDeg.Value * MathF.PI / 180f, pitchMin, pitchMax);
        }
        if (zoom.HasValue)
            _zoom = Math.Clamp(zoom.Value, Constants.CameraZoomMin, Constants.CameraZoomMax);
    }

    /// <summary>
    /// Set screen size (for window resize).
    /// </summary>
    public void SetScreenSize(int width, int height)
    {
        _screenWidth = width;
        _screenHeight = height;
    }
}