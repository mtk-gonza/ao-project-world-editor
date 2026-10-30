using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AoProject.WorldEditor.Application;
using AoProject.WorldEditor.Domain;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using DrawingColor = System.Drawing.Color;

namespace AoProject.WorldEditor.Engine.Rendering;

/// <summary>
/// Initial D3D11 backend. It owns only the device, swap chain and back buffer;
/// map geometry and sprites will be added in later parity slices.
/// </summary>
internal sealed class D3D11WorldRenderer : IWorldRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public float X;
        public float Y;
        public float U;
        public float V;
        public float Slice;
        public float R;
        public float G;
        public float B;
        public float A;
    }

    private const int InitialVertexCapacity = 1_000_000;
    private const string ShaderSource = """
        cbuffer Constants : register(b0)
        {
            float2 Viewport;
            float2 Camera;
            float Zoom;
            float Padding;
        };
        struct VSInput { float2 Position : POSITION; float2 TexCoord : TEXCOORD0; float Slice : TEXCOORD1; float4 Color : COLOR0; };
        struct PSInput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; float Slice : TEXCOORD1; float4 Color : COLOR0; };
        PSInput VSMain(VSInput input)
        {
            PSInput output;
            float2 screen = (input.Position - Camera) * Zoom;
            output.Position = float4(screen.x / Viewport.x * 2.0 - 1.0,
                1.0 - screen.y / Viewport.y * 2.0, 0.0, 1.0);
            output.Color = input.Color;
            output.TexCoord = input.TexCoord;
            output.Slice = input.Slice;
            return output;
        }
        float4 PSMain(PSInput input) : SV_TARGET { return input.Color; }
        Texture2DArray Atlas : register(t0);
        Texture2D FontTexture : register(t0);
        SamplerState PointSampler : register(s0);
        float4 PSTextured(PSInput input) : SV_TARGET
        {
            float4 sample = Atlas.Sample(PointSampler, float3(input.TexCoord, input.Slice));
            // Legacy PNGs use transparent pixels with arbitrary RGB values
            // (often white). Do not let those RGB values leak through when a
            // driver or blend state treats zero-alpha samples as writable.
            if (sample.a < 0.01) discard;
            return sample * input.Color;
        }
        float4 PSText(PSInput input) : SV_TARGET
        {
            float4 sample = FontTexture.Sample(PointSampler, input.TexCoord);
            if (sample.a < 0.3) discard;
            return float4(input.Color.rgb, input.Color.a);
        }
        """;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _pixelShader;
    private readonly ID3D11PixelShader _texturedPixelShader;
    private readonly ID3D11PixelShader _textPixelShader;
    private readonly ID3D11InputLayout _inputLayout;
    private readonly ID3D11InputLayout _texturedInputLayout;
    private ID3D11Buffer _vertexBuffer;
    private readonly ID3D11Buffer _constantBuffer;
    private Vertex[] _vertices = new Vertex[InitialVertexCapacity];
    private int _vertexCapacity = InitialVertexCapacity;
    private D3D11AtlasResource? _atlasResource;
    private ID3D11ShaderResourceView? _fontView;
    private ID3D11Texture2D? _fontTexture;
    private FontAtlas? _fontAtlas;
    private IReadOnlyList<TextQuad> _hudQuads = [];
    private IReadOnlyList<RenderSprite> _sprites = [];
    private IReadOnlyDictionary<int, AnimationInfo> _animations = new Dictionary<int, AnimationInfo>();
    private int[,,]? _lights;
    private int _frameCounter;
    private double _time;
    private double _lastRoofTime;
    private int _roofFocusTrigger;
    private bool _roofFadeEnabled = true;
    private bool _alphaRoofs;
    private readonly Dictionary<int, float> _roofAlpha = [];
    public string? CapturePath { get; set; }

    /// <summary>
    /// Frame at which AO_CAPTURE writes the back buffer. 30 by default: a world
    /// needs seconds to warm its caches, so diagnosing the global view wants a
    /// later frame (AO_CAPTURE_FRAME=1200 ~ 20 s).
    /// </summary>
    private static readonly int CaptureFrame =
        int.TryParse(Environment.GetEnvironmentVariable("AO_CAPTURE_FRAME"), out var captureFrame) &&
        captureFrame > 0 ? captureFrame : 30;
    private ID3D11SamplerState? _sampler;
    private ID3D11RasterizerState? _rasterizer;
    private ID3D11BlendState? _alphaBlend;
    private ID3D11BlendState? _additiveBlend;
    private readonly WorldCamera _camera = new();
    private ID3D11RenderTargetView? _renderTarget;
    private int _width;
    private int _height;
    private bool _disposed;

    public D3D11WorldRenderer(nint windowHandle, int width, int height)
    {
        _width = Math.Max(1, width);
        _height = Math.Max(1, height);

        var featureLevels = new[]
        {
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        };

        D3D11.D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            featureLevels,
            out _device,
            out _context).CheckError();

        using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        using var factory = adapter.GetParent<IDXGIFactory2>();

        var description = new SwapChainDescription1
        {
            Width = (uint)_width,
            Height = (uint)_height,
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 1,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.Discard,
            AlphaMode = AlphaMode.Ignore
        };

        _swapChain = factory.CreateSwapChainForHwnd(_device, windowHandle, description);
        using var vsBlob = CompileShader("VSMain", "vs_5_0");
        using var psBlob = CompileShader("PSMain", "ps_5_0");
        unsafe
        {
            _vertexShader = _device.CreateVertexShader((void*)vsBlob.BufferPointer, (nuint)vsBlob.BufferSize, null);
            _pixelShader = _device.CreatePixelShader((void*)psBlob.BufferPointer, (nuint)psBlob.BufferSize, null);
            using var texturedBlob = CompileShader("PSTextured", "ps_5_0");
            _texturedPixelShader = _device.CreatePixelShader((void*)texturedBlob.BufferPointer, (nuint)texturedBlob.BufferSize, null);
            _texturedInputLayout = _device.CreateInputLayout(
                [
                    new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                    new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                    new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 16, 0),
                    new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 20, 0)
                ], vsBlob);
            var textBlob = CompileShader("PSText", "ps_5_0");
            _textPixelShader = _device.CreatePixelShader((void*)textBlob.BufferPointer, (nuint)textBlob.BufferSize, null);
        }
        _inputLayout = _device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
                new InputElementDescription("TEXCOORD", 1, Format.R32_Float, 16, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 20, 0)
            ], vsBlob);
        _vertexBuffer = CreateVertexBuffer(_vertexCapacity);
        _constantBuffer = _device.CreateBuffer(new BufferDescription(
            32, BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write));
        // Keep the legacy renderer's pixel-exact atlas sampling for detail view.
        // The global overview uses the pre-rendered floor PNG instead of
        // minifying atlas graphics through this sampler.
        _sampler = _device.CreateSamplerState(SamplerDescription.PointClamp);
        _rasterizer = _device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            DepthClipEnable = false
        });
        _alphaBlend = _device.CreateBlendState(BlendDescription.AlphaBlend);
        // VB6 AlphaTecho: Device_Box_Textured_Render uses SRCBLEND=ONE and
        // DESTBLEND=ONE (clsDX8Engine.cls:1961-1966) for the roof layer.
        _additiveBlend = _device.CreateBlendState(new BlendDescription(Blend.One, Blend.One));
        CreateRenderTarget();
    }

    public bool IsReady => !_disposed && _renderTarget is not null;

    public void SetHud(IReadOnlyList<TextQuad> quads, FontAtlas atlas)
    {
        _hudQuads = quads ?? [];
        if (!ReferenceEquals(_fontAtlas, atlas))
        {
            _fontView?.Dispose();
            _fontTexture?.Dispose();
            _fontAtlas = atlas;
            _fontTexture = CreateFontTexture(atlas);
            _fontView = _device.CreateShaderResourceView(_fontTexture);
        }
    }

    public void SetSprites(IReadOnlyList<RenderSprite> sprites) => _sprites = sprites ?? [];

    public void SetAnimations(IReadOnlyDictionary<int, AnimationInfo> animations) =>
        _animations = animations ?? new Dictionary<int, AnimationInfo>();

    public void SetLightMap(int[,,]? lights) => _lights = lights;

    public void SetTime(double seconds) => _time = seconds;

    public void SetRoofFocus(int trigger) => _roofFocusTrigger = MapRenderGeometry.IsRoofTrigger(trigger) ? trigger : 0;
    public void SetRoofFadeEnabled(bool enabled) => _roofFadeEnabled = enabled;

    /// <summary>VB6 "Techos transparentes" (AlphaTecho): additive roof layer.</summary>
    public void SetAlphaRoofsEnabled(bool enabled) => _alphaRoofs = enabled;

    /// <summary>Milliseconds spent building sprite rows in the last frame.</summary>
    public double LastSpriteRowsMs { get; private set; }

    /// <summary>Milliseconds spent emitting and drawing vertices in the last frame.</summary>
    public double LastDrawMs { get; private set; }

    /// <summary>Vertices emitted in the last frame.</summary>
    public int LastVertexCount { get; private set; }

    /// <summary>Restarts the AO_CAPTURE frame count after a map is loaded.</summary>
    public void ResetCaptureCounter() => _frameCounter = 0;

    /// <summary>Copies the current back buffer to a PNG for visual verification.</summary>
    public unsafe void CaptureBackBuffer(string path)
    {
        if (!IsReady) return;
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        var description = new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None
        };
        using var staging = _device.CreateTexture2D(description);
        _context.CopyResource(staging, backBuffer);
        var mapped = _context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            using var bitmap = new Bitmap(_width, _height, PixelFormat.Format32bppRgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, _width, _height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (var row = 0; row < _height; row++)
                    Buffer.MemoryCopy((byte*)mapped.DataPointer + row * mapped.RowPitch,
                        (byte*)data.Scan0 + row * data.Stride, data.Stride, Math.Min(data.Stride, mapped.RowPitch));
            }
            finally { bitmap.UnlockBits(data); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            bitmap.Save(path, ImageFormat.Png);
        }
        finally { _context.Unmap(staging, 0); }
    }


    private unsafe ID3D11Texture2D CreateFontTexture(FontAtlas atlas)
    {
        var description = new Texture2DDescription
        {
            Width = (uint)atlas.Width,
            Height = (uint)atlas.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.R8G8B8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None
        };
        fixed (byte* pointer = atlas.Pixels)
        {
            var data = new SubresourceData((nint)pointer, (uint)(atlas.Width * 4));
            return _device.CreateTexture2D(description, new[] { data });
        }
    }


    public void SetAtlas(GraphicAtlas atlas)
    {
        _atlasResource?.Dispose();
        _atlasResource = atlas.Pages.Count == 0 ? null : D3D11AtlasResource.Create(_device, atlas.Pages);
    }

    public void SetCamera(WorldCamera camera)
    {
        _camera.SetZoom(camera.Zoom, 0, 0);
        _camera.Pan(camera.OffsetX - _camera.OffsetX, camera.OffsetY - _camera.OffsetY);
    }

    public void Resize(int width, int height)
    {
        if (!IsReady || width <= 0 || height <= 0 || (width == _width && height == _height))
            return;

        _width = width;
        _height = height;
        _context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>());
        _renderTarget?.Dispose();
        _renderTarget = null;
        _swapChain.ResizeBuffers(0, (uint)_width, (uint)_height, Format.Unknown, SwapChainFlags.None).CheckError();
        CreateRenderTarget();
    }

    public void Render(DrawingColor clearColor)
    {
        if (!IsReady)
            return;

        _context.OMSetRenderTargets(_renderTarget!);
        _context.OMSetBlendState(_alphaBlend);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height));
        _context.ClearRenderTargetView(_renderTarget!, new Color4(
            clearColor.R / 255f,
            clearColor.G / 255f,
            clearColor.B / 255f,
            clearColor.A / 255f));
        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    public void Render(DrawingColor clearColor, MapRenderGeometry geometry)
    {
        if (!IsReady)
            return;
        _context.OMSetRenderTargets(_renderTarget!);
        _context.OMSetBlendState(_alphaBlend);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height));
        _context.ClearRenderTargetView(_renderTarget!, new Color4(
            clearColor.R / 255f, clearColor.G / 255f, clearColor.B / 255f, 1f));

        var count = 0;
        foreach (var tile in geometry.Tiles)
        {
            if (!IsVisible(tile.PixelX, tile.PixelY, tile.PixelX + MapRenderGeometry.TilePixels,
                tile.PixelY + MapRenderGeometry.TilePixels))
                continue;
            var color = tile.Layer switch
            {
                0 => new[] { 0.22f, 0.42f, 0.22f, 1f },
                1 => new[] { 0.32f, 0.52f, 0.28f, 1f },
                2 => new[] { 0.48f, 0.36f, 0.20f, 1f },
                _ => new[] { 0.62f, 0.62f, 0.62f, 0.75f }
            };
            var left = tile.PixelX;
            var top = tile.PixelY;
            var right = left + MapRenderGeometry.TilePixels;
            var bottom = top + MapRenderGeometry.TilePixels;
            AddVertex(left, top, color, ref count);
            AddVertex(right, top, color, ref count);
            AddVertex(right, bottom, color, ref count);
            AddVertex(left, top, color, ref count);
            AddVertex(right, bottom, color, ref count);
            AddVertex(left, bottom, color, ref count);
        }

        if (count > 0)
        {
            var mapped = _context.Map(_vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
            try
            {
                unsafe
                {
                    var target = new Span<byte>((void*)mapped.DataPointer, count * Marshal.SizeOf<Vertex>());
                    MemoryMarshal.AsBytes(_vertices.AsSpan(0, count)).CopyTo(target);
                }
            }
            finally
            {
                _context.Unmap(_vertexBuffer, 0);
            }
            var viewport = new[] { (float)_width, (float)_height, _camera.OffsetX, _camera.OffsetY, _camera.Zoom, 0f, 0f, 0f };
            _context.UpdateSubresource(viewport, _constantBuffer);
            _context.IASetInputLayout(_inputLayout);
            _context.IASetVertexBuffer(0, _vertexBuffer, (uint)Marshal.SizeOf<Vertex>());
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _context.VSSetShader(_vertexShader);
            _context.PSSetShader(_pixelShader);
            _context.VSSetConstantBuffer(0, _constantBuffer);
            _context.Draw((uint)count, 0);
        }
        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    public void RenderTextured(DrawingColor clearColor, MapRenderGeometry geometry,
        IReadOnlyDictionary<int, AtlasRegion> regions, IReadOnlyList<MapOverlay>? overlays = null)
    {
        if (!IsReady || _atlasResource is null)
        {
            Render(clearColor, geometry);
            return;
        }
        _context.OMSetRenderTargets(_renderTarget!);
        _context.OMSetBlendState(_alphaBlend);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height));
        _context.ClearRenderTargetView(_renderTarget!, new Color4(clearColor.R / 255f, clearColor.G / 255f, clearColor.B / 255f, 1f));
        UpdateRoofFade();
        var rowsWatch = System.Diagnostics.Stopwatch.StartNew();
        var count = 0;
        var objectRows = BuildSpriteRows(SpriteKind.Object);
        var characterRows = BuildSpriteRows(SpriteKind.Character);
        var particleRows = BuildSpriteRows(SpriteKind.Particle);
        LastSpriteRowsMs = rowsWatch.Elapsed.TotalMilliseconds;
        var drawWatch = System.Diagnostics.Stopwatch.StartNew();

        AppendLayer(geometry.Tiles, 0, regions, ref count);
        // Exact VB6 WorldEditor order (Graphic() is one-based there):
        // layer 1, layer 5, layer 2, layer 6, objects, chars, layer 3,
        // particles, layer 4.
        AppendLayer(geometry.Tiles, 4, regions, ref count);
        AppendLayer(geometry.Tiles, 1, regions, ref count);
        AppendLayer(geometry.Tiles, 5, regions, ref count);
        // VB6 completes the lower walkable layers before objects and chars.

        // Graphic(2) must be completely drawn before the row's objects and
        // characters. The previous implementation appended sprites before the
        // first tile of each row, which let the layer overwrite every NPC.
        foreach (var row in objectRows.Keys.Concat(characterRows.Keys).Distinct().OrderBy(row => row))
        {
            AppendRow(objectRows, row, regions, ref count);
            AppendRow(characterRows, row, regions, ref count);
        }

        AppendLayer(geometry.Tiles, 2, regions, ref count);
        foreach (var particleRow in particleRows.Keys.OrderBy(row => row))
            AppendRow(particleRows, particleRow, regions, ref count);

        // Capa 4 (techos) in its own draw so AlphaTecho can switch the blend
        // state. The main batch draws with the regular alpha blend.
        var roofCount = 0;
        AppendLayer(geometry.Tiles, 3, regions, ref roofCount);

        DrawVertices(count, _texturedInputLayout, _texturedPixelShader, true);
        if (roofCount > 0)
        {
            _context.OMSetBlendState(_alphaRoofs ? _additiveBlend : _alphaBlend);
            DrawVertices(roofCount, _texturedInputLayout, _texturedPixelShader, true);
            _context.OMSetBlendState(_alphaBlend);
        }
        if (overlays is not null)
            DrawOverlays(overlays, regions);
        DrawHud();
        LastDrawMs = drawWatch.Elapsed.TotalMilliseconds;
        LastVertexCount = count + roofCount;
        _frameCounter++;
        if (CapturePath is { } capture && _frameCounter >= CaptureFrame)
        {
            CapturePath = null;
            try { CaptureBackBuffer(capture); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError($"WorldEditor capture failed: {error}"); }
        }
        _swapChain.Present(1, PresentFlags.None).CheckError();
    }

    private void AppendLayer(IReadOnlyList<MapTileQuad> tiles, int layer,
        IReadOnlyDictionary<int, AtlasRegion> regions, ref int count)
    {
        foreach (var tile in tiles)
            if (tile.Layer == layer) AppendTile(tile, regions, ref count);
    }

    private void AppendTile(MapTileQuad tile, IReadOnlyDictionary<int, AtlasRegion> regions, ref int count)
    {
        var graphicIndex = ResolveFrame(tile.GraphicIndex);
        if (!regions.TryGetValue(graphicIndex, out var region)) return;
        var left = tile.PixelX;
        var top = tile.PixelY;
        // The official editor calls Draw_Grh with center=1 for Graphic(2),
        // Graphic(3) and Graphic(4). Graphic(1), Graphic(5) and Graphic(6)
        // use the tile origin (center=0).
            if (tile.Layer is 1 or 2 or 3)
            {
            left += MapRenderGeometry.TilePixels / 2 - (int)MathF.Floor(region.TileWidth * (MapRenderGeometry.TilePixels / 2f));
            top += MapRenderGeometry.TilePixels - (int)MathF.Floor(region.TileHeight * MapRenderGeometry.TilePixels);
        }
        var right = left + region.Width;
        var bottom = top + region.Height;
        if (!IsVisible(left, top, right, bottom)) return;
        var u0 = region.U0; var v0 = region.V0; var u1 = region.U1; var v1 = region.V1;
        if (tile.ClipLeft >= 0)
        {
            var originalWidth = right - left;
            var originalHeight = bottom - top;
            var clippedLeft = Math.Max(left, tile.ClipLeft);
            var clippedTop = Math.Max(top, tile.ClipTop);
            var clippedRight = Math.Min(right, tile.ClipRight);
            var clippedBottom = Math.Min(bottom, tile.ClipBottom);
            if (clippedLeft >= clippedRight || clippedTop >= clippedBottom) return;
            var uSpan = u1 - u0;
            var vSpan = v1 - v0;
            u0 += uSpan * (clippedLeft - left) / originalWidth;
            u1 -= uSpan * (right - clippedRight) / Math.Max(1, originalWidth);
            v0 += vSpan * (clippedTop - top) / originalHeight;
            v1 -= vSpan * (bottom - clippedBottom) / Math.Max(1, originalHeight);
            left = clippedLeft; top = clippedTop; right = clippedRight; bottom = clippedBottom;
        }
        AddTexturedVertex(left, top, u0, v0, region.Page, RoofLight(tile, 1), ref count);
        AddTexturedVertex(right, top, u1, v0, region.Page, RoofLight(tile, 3), ref count);
        AddTexturedVertex(right, bottom, u1, v1, region.Page, RoofLight(tile, 2), ref count);
        AddTexturedVertex(left, top, u0, v0, region.Page, RoofLight(tile, 1), ref count);
        AddTexturedVertex(right, bottom, u1, v1, region.Page, RoofLight(tile, 2), ref count);
        AddTexturedVertex(left, bottom, u0, v1, region.Page, RoofLight(tile, 0), ref count);
    }

    private readonly record struct ColorF(float R, float G, float B, float A);

    private readonly record struct CornerLights(ColorF C0, ColorF C1, ColorF C2, ColorF C3)
    {
        public ColorF this[int corner] => corner switch { 0 => C0, 1 => C1, 2 => C2, _ => C3 };
    }

    private ColorF RoofLight(MapTileQuad tile, int corner)
    {
        var light = Light(tile.X, tile.Y, corner);
        if (_roofFadeEnabled && tile.Layer == 3 && tile.RoofTrigger > 0 && _roofAlpha.TryGetValue(tile.RoofTrigger, out var alpha))
            light = light with { A = light.A * (alpha / 255f) };
        return light;
    }

    private void UpdateRoofFade()
    {
        var deltaMs = _lastRoofTime <= 0 ? 16d : Math.Clamp((_time - _lastRoofTime) * 1000d, 0d, 100d);
        _lastRoofTime = _time;
        foreach (var trigger in _roofAlpha.Keys.ToArray())
        {
            var target = trigger == _roofFocusTrigger ? 0f : 255f;
            var step = (float)(deltaMs * 48d / 16d);
            var value = _roofAlpha[trigger];
            _roofAlpha[trigger] = target < value ? Math.Max(target, value - step) : Math.Min(target, value + step);
        }
        if (_roofFocusTrigger > 0 && !_roofAlpha.ContainsKey(_roofFocusTrigger))
            _roofAlpha[_roofFocusTrigger] = 255f;
    }

    private ColorF Light(int x, int y, int corner)
    {
        var color = LightColorAt(_lights, x, y, corner);
        return new ColorF(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            1f);
    }

    /// <summary>
    /// Defensive light lookup. The global world view can ask for tiles outside
    /// the current map's light map (up to 19x22 chunks); white keeps that mode
    /// renderable instead of throwing IndexOutOfRangeException.
    /// </summary>
    internal static int LightColorAt(int[,,]? lights, int x, int y, int corner)
    {
        const int White = unchecked((int)0xFFFFFFFF);
        if (lights is null || corner is < 0 or > 3) return White;
        if (x < 0 || y < 0 || x >= lights.GetLength(0) || y >= lights.GetLength(1))
            return White;
        return lights[x, y, corner];
    }

    private CornerLights SpriteLights(int pixelX, int pixelY)
    {
        if (_lights is null)
            return new CornerLights(new ColorF(1,1,1,1), new ColorF(1,1,1,1), new ColorF(1,1,1,1), new ColorF(1,1,1,1));
        var x = Math.Clamp(pixelX / MapRenderGeometry.TilePixels + 1, 1, WorldMap.Width);
        var y = Math.Clamp(pixelY / MapRenderGeometry.TilePixels + 1, 1, WorldMap.Height);
        return new CornerLights(Light(x, y, 1), Light(x, y, 3), Light(x, y, 2), Light(x, y, 0));
    }

    private Dictionary<int, List<RenderSprite>> BuildSpriteRows(SpriteKind kind)
    {
        var rows = new Dictionary<int, List<RenderSprite>>();
        foreach (var sprite in _sprites)
        {
            if (sprite.Kind != kind) continue;
            var row = SpriteRowForPixel(sprite.PixelY);
            if (!rows.TryGetValue(row, out var list)) { list = []; rows[row] = list; }
            list.Add(sprite);
        }
        return rows;
    }

    internal static int SpriteRowForPixel(int pixelY) =>
        (int)MathF.Floor(pixelY / (float)MapRenderGeometry.TilePixels) + 1;

    private int ResolveFrame(int graphicIndex, bool animate = true)
    {
        if (!animate || !_animations.TryGetValue(graphicIndex, out var animation)) return graphicIndex;
        var frame = animation.FrameAt(_time);
        return frame > 0 ? frame : graphicIndex;
    }

    private bool IsVisible(float left, float top, float right, float bottom)
    {
        const float tileBuffer = 9f * MapRenderGeometry.TilePixels;
        var viewLeft = _camera.OffsetX - tileBuffer;
        var viewTop = _camera.OffsetY - tileBuffer;
        var viewRight = _camera.OffsetX + _width / Math.Max(_camera.Zoom, 0.01f) + tileBuffer;
        var viewBottom = _camera.OffsetY + _height / Math.Max(_camera.Zoom, 0.01f) + tileBuffer;
        return right >= viewLeft && left <= viewRight && bottom >= viewTop && top <= viewBottom;
    }

    private void AppendRow(Dictionary<int, List<RenderSprite>> rows, int row,
        IReadOnlyDictionary<int, AtlasRegion> regions, ref int count)
    {
        if (!rows.TryGetValue(row, out var sprites)) return;
        foreach (var sprite in sprites)
        {
            var graphicIndex = ResolveFrame(sprite.GraphicIndex, sprite.Animate);
            if (!regions.TryGetValue(graphicIndex, out var region)) continue;
            var tint = new ColorF(
                ((sprite.Color >> 16) & 0xFF) / 255f,
                ((sprite.Color >> 8) & 0xFF) / 255f,
                (sprite.Color & 0xFF) / 255f,
                sprite.Alpha / 255f);
            var lights = SpriteLights(sprite.PixelX, sprite.PixelY);
            ColorF ColorFor(int corner)
            {
                var light = lights[corner];
                return new ColorF(light.R * tint.R, light.G * tint.G, light.B * tint.B, tint.A);
            }
            var left = sprite.PixelX;
            var top = sprite.PixelY;
            if (sprite.Kind is SpriteKind.Character or SpriteKind.Object)
            {
                // engine.bas:474-479 Draw_Grh(center=1): x/y are shifted by the
                // GRH's tile size (Int of the fractional product, i.e. its pixel
                // size) only when it is not exactly one tile.
                if (Math.Abs(region.TileWidth - 1f) > 0.001f)
                    left += MapRenderGeometry.TilePixels / 2 -
                        (int)MathF.Floor(region.TileWidth * (MapRenderGeometry.TilePixels / 2f));
                if (Math.Abs(region.TileHeight - 1f) > 0.001f)
                    top += MapRenderGeometry.TilePixels -
                        (int)MathF.Floor(region.TileHeight * MapRenderGeometry.TilePixels);
            }
            var drawWidth = sprite.Width > 0 ? sprite.Width : region.Width;
            var drawHeight = sprite.Height > 0 ? sprite.Height : region.Height;
            var right = left + drawWidth;
            var bottom = top + drawHeight;
            if (!IsVisible(left, top, right, bottom)) continue;
            if (MathF.Abs(sprite.Angle) > 0.001f)
            {
                var centerX = (left + right) / 2f;
                var centerY = (top + bottom) / 2f;
                var radians = sprite.Angle * (MathF.PI / 180f);
                var cosine = MathF.Cos(radians);
                var sine = MathF.Sin(radians);
                (float X, float Y) Rotate(float x, float y)
                {
                    var dx = x - centerX;
                    var dy = y - centerY;
                    return (centerX + dx * cosine - dy * sine, centerY + dx * sine + dy * cosine);
                }
                var rotatedTopLeft = Rotate(left, top);
                var rotatedTopRight = Rotate(right, top);
                var rotatedBottomRight = Rotate(right, bottom);
                var rotatedBottomLeft = Rotate(left, bottom);
                AddTexturedVertex(rotatedTopLeft.X, rotatedTopLeft.Y, region.U0, region.V0, region.Page, ColorFor(1), ref count);
                AddTexturedVertex(rotatedTopRight.X, rotatedTopRight.Y, region.U1, region.V0, region.Page, ColorFor(3), ref count);
                AddTexturedVertex(rotatedBottomRight.X, rotatedBottomRight.Y, region.U1, region.V1, region.Page, ColorFor(2), ref count);
                AddTexturedVertex(rotatedTopLeft.X, rotatedTopLeft.Y, region.U0, region.V0, region.Page, ColorFor(1), ref count);
                AddTexturedVertex(rotatedBottomRight.X, rotatedBottomRight.Y, region.U1, region.V1, region.Page, ColorFor(2), ref count);
                AddTexturedVertex(rotatedBottomLeft.X, rotatedBottomLeft.Y, region.U0, region.V1, region.Page, ColorFor(0), ref count);
                continue;
            }
            AddTexturedVertex(left, top, region.U0, region.V0, region.Page, ColorFor(1), ref count);
            AddTexturedVertex(right, top, region.U1, region.V0, region.Page, ColorFor(3), ref count);
            AddTexturedVertex(right, bottom, region.U1, region.V1, region.Page, ColorFor(2), ref count);
            AddTexturedVertex(left, top, region.U0, region.V0, region.Page, ColorFor(1), ref count);
            AddTexturedVertex(right, bottom, region.U1, region.V1, region.Page, ColorFor(2), ref count);
            AddTexturedVertex(left, bottom, region.U0, region.V1, region.Page, ColorFor(0), ref count);
        }
    }

    private void DrawHud()
    {
        if (_hudQuads.Count == 0 || _fontView is null) return;
        var count = 0;
        foreach (var quad in _hudQuads)
        {
            var color = new[]
            {
                ((quad.Color >> 16) & 0xFF) / 255f,
                ((quad.Color >> 8) & 0xFF) / 255f,
                (quad.Color & 0xFF) / 255f,
                1f
            };
            var left = quad.X;
            var top = quad.Y;
            var right = left + quad.Width;
            var bottom = top + quad.Height;
            AddTexturedVertex(left, top, quad.U0, quad.V0, 0, color, ref count);
            AddTexturedVertex(right, top, quad.U1, quad.V0, 0, color, ref count);
            AddTexturedVertex(right, bottom, quad.U1, quad.V1, 0, color, ref count);
            AddTexturedVertex(left, top, quad.U0, quad.V0, 0, color, ref count);
            AddTexturedVertex(right, bottom, quad.U1, quad.V1, 0, color, ref count);
            AddTexturedVertex(left, bottom, quad.U0, quad.V1, 0, color, ref count);
        }
        DrawVertices(count, _texturedInputLayout, _textPixelShader, true, _fontView, true);
    }

    private void DrawOverlays(IReadOnlyList<MapOverlay> overlays, IReadOnlyDictionary<int, AtlasRegion> regions)
    {
        // Pass A: GRH markers (blocked sides, transfers, trigger digits).
        var markers = 0;
        foreach (var overlay in overlays)
        {
            if (overlay.Kind == OverlayKind.Blocked) continue;
            if (overlay.GraphicIndex <= 0 || !regions.TryGetValue(overlay.GraphicIndex, out var region)) continue;
            if (markers + 6 > _vertices.Length) break;
            var left = (overlay.X - 1) * MapRenderGeometry.TilePixels + overlay.OffsetX;
            var top = (overlay.Y - 1) * MapRenderGeometry.TilePixels + overlay.OffsetY;
            if (overlay.GraphicIndex != MapOverlayGeometry.GridGraphic)
            {
                // Blocked sides, transfers and trigger digits use Draw_Grh
                // with center=1 in VB6. The grid uses Draw_GrhIndex and stays
                // anchored at the tile origin.
                left += MapRenderGeometry.TilePixels / 2 - (int)MathF.Floor(region.TileWidth * (MapRenderGeometry.TilePixels / 2f));
                top += MapRenderGeometry.TilePixels - (int)MathF.Floor(region.TileHeight * MapRenderGeometry.TilePixels);
            }
            var right = left + region.Width;
            var bottom = top + region.Height;
            var white = new[] { 1f, 1f, 1f, 1f };
            AddTexturedVertex(left, top, region.U0, region.V0, region.Page, white, ref markers);
            AddTexturedVertex(right, top, region.U1, region.V0, region.Page, white, ref markers);
            AddTexturedVertex(right, bottom, region.U1, region.V1, region.Page, white, ref markers);
            AddTexturedVertex(left, top, region.U0, region.V0, region.Page, white, ref markers);
            AddTexturedVertex(right, bottom, region.U1, region.V1, region.Page, white, ref markers);
            AddTexturedVertex(left, bottom, region.U0, region.V1, region.Page, white, ref markers);
        }
        DrawVertices(markers, _texturedInputLayout, _texturedPixelShader, true);

        // Pass B: editor-only annotations (grid, particles, lights, selection).
        var count = 0;
        foreach (var overlay in overlays)
        {
            if (overlay.GraphicIndex > 0 && overlay.Kind != OverlayKind.Blocked) continue;
            var color = overlay.Kind switch
            {
                OverlayKind.Grid => new[] { 0.35f, 0.35f, 0.35f, 10f / 255f },
                OverlayKind.Blocked => new[] { 1f, 0.08f, 0.08f, 0.95f },
                OverlayKind.Particle => new[] { 0.20f, 1f, 0.55f, 0.65f },
                OverlayKind.Light => new[] { 1f, 1f, 0.55f, 0.35f },
                OverlayKind.Selection => new[] { 0f, 200f / 255f, 100f / 255f, 75f / 255f },
                _ => new[] { 1f, 1f, 1f, 0.85f }
            };
            var tile = MapRenderGeometry.TilePixels;
            var left = (overlay.X - 1) * tile;
            var top = (overlay.Y - 1) * tile;
            var width = overlay.Kind is OverlayKind.Grid or OverlayKind.Selection ? 30 : overlay.Width * tile;
            var height = overlay.Kind is OverlayKind.Grid or OverlayKind.Selection ? 30 : overlay.Height * tile;
            var right = left + width;
            var bottom = top + height;
            if (overlay.Kind == OverlayKind.Blocked)
            {
                var side = overlay.GraphicIndex - MapOverlayGeometry.BlockedBaseGraphic - 1;
                AppendBlockedSide(left, top, side, color, ref count);
                continue;
            }
            if (overlay.Kind == OverlayKind.Selection)
            {
                AppendFrame(left, top, right, bottom, color, ref count);
                continue;
            }
            if (overlay.Kind == OverlayKind.Grid)
            {
                // VB6 draws the grid as one-pixel lines. A filled quad makes
                // the map unreadable and is not equivalent at high zoom.
                AppendFrame(left, top, right, bottom, color, ref count);
                continue;
            }
            const int marker = 8;
            left = (int)((left + right) / 2f - marker / 2f);
            top = (int)((top + bottom) / 2f - marker / 2f);
            right = left + marker;
            bottom = top + marker;
            AppendRect(left, top, right, bottom, color, ref count);
        }
        DrawVertices(count, _inputLayout, _pixelShader, false);
    }

    private void AppendFrame(float left, float top, float right, float bottom, float[] color, ref int count)
    {
        const float thickness = 2f;
        AppendRect(left, top, right, top + thickness, color, ref count);
        AppendRect(left, bottom - thickness, right, bottom, color, ref count);
        AppendRect(left, top, left + thickness, bottom, color, ref count);
        AppendRect(right - thickness, top, right, bottom, color, ref count);
    }

    private void AppendBlockedSide(float left, float top, int side, float[] color, ref int count)
    {
        const float tile = MapRenderGeometry.TilePixels;
        const float thickness = 4f;
        switch (side)
        {
            case 0: AppendRect(left, top, left + tile, top + thickness, color, ref count); break;
            case 1: AppendRect(left + tile - thickness, top, left + tile, top + tile, color, ref count); break;
            case 2: AppendRect(left, top + tile - thickness, left + tile, top + tile, color, ref count); break;
            case 3: AppendRect(left, top, left + thickness, top + tile, color, ref count); break;
        }
    }

    private void AppendRect(float left, float top, float right, float bottom, float[] color, ref int count)
    {
        AddVertex(left, top, color, ref count);
        AddVertex(right, top, color, ref count);
        AddVertex(right, bottom, color, ref count);
        AddVertex(left, top, color, ref count);
        AddVertex(right, bottom, color, ref count);
        AddVertex(left, bottom, color, ref count);
    }

    private void AddTexturedVertex(float x, float y, float u, float v, int slice, float[] color, ref int count) =>
        AddTexturedVertexCore(x, y, u, v, slice, color[0], color[1], color[2], color[3], ref count);

    private void AddTexturedVertex(float x, float y, float u, float v, int slice, ColorF color, ref int count) =>
        AddTexturedVertexCore(x, y, u, v, slice, color.R, color.G, color.B, color.A, ref count);

    private void AddTexturedVertexCore(float x, float y, float u, float v, int slice, float r, float g, float b, float a, ref int count)
    {
        EnsureVertexCapacity(count + 1);
        _vertices[count++] = new Vertex { X = x, Y = y, U = u, V = v, Slice = slice, R = r, G = g, B = b, A = a };
    }

    private void DrawVertices(int count, ID3D11InputLayout layout, ID3D11PixelShader shader, bool textured,
        ID3D11ShaderResourceView? resource = null, bool screenSpace = false)
    {
        if (count <= 0) return;
        var mapped = _context.Map(_vertexBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                var target = new Span<byte>((void*)mapped.DataPointer, count * Marshal.SizeOf<Vertex>());
                MemoryMarshal.AsBytes(_vertices.AsSpan(0, count)).CopyTo(target);
            }
        }
        finally { _context.Unmap(_vertexBuffer, 0); }
        var viewport = new[]
        {
            (float)_width, (float)_height,
            screenSpace ? 0f : _camera.OffsetX,
            screenSpace ? 0f : _camera.OffsetY,
            screenSpace ? 1f : _camera.Zoom,
            0f, 0f, 0f
        };
        var mappedConstant = _context.Map(_constantBuffer, 0, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        try
        {
            unsafe
            {
                fixed (float* source = viewport)
                    Buffer.MemoryCopy(source, (void*)mappedConstant.DataPointer, 32, 32);
            }
        }
        finally { _context.Unmap(_constantBuffer, 0); }
        _context.IASetInputLayout(layout);        _context.IASetVertexBuffer(0, _vertexBuffer, (uint)Marshal.SizeOf<Vertex>());
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.RSSetState(_rasterizer);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(shader);
        _context.VSSetConstantBuffer(0, _constantBuffer);
        if (textured)
        {
            _context.PSSetSampler(0, _sampler!);
            _context.PSSetShaderResource(0, resource ?? _atlasResource!.View);
        }
        _context.Draw((uint)count, 0);
    }

    private void AddVertex(float x, float y, float[] color, ref int count)
    {
        EnsureVertexCapacity(count + 1);
        _vertices[count++] = new Vertex { X = x, Y = y, R = color[0], G = color[1], B = color[2], A = color[3] };
    }

    private ID3D11Buffer CreateVertexBuffer(int capacity) => _device.CreateBuffer(new BufferDescription(
        (uint)(Marshal.SizeOf<Vertex>() * capacity), BindFlags.VertexBuffer,
        ResourceUsage.Dynamic, CpuAccessFlags.Write));

    private void EnsureVertexCapacity(int required)
    {
        if (required <= _vertexCapacity) return;
        var capacity = _vertexCapacity;
        while (capacity < required) capacity = checked(capacity * 2);
        var replacement = CreateVertexBuffer(capacity);
        _vertexBuffer.Dispose();
        _vertexBuffer = replacement;
        var previous = _vertices;
        _vertices = new Vertex[capacity];
        Array.Copy(previous, _vertices, previous.Length);
        _vertexCapacity = capacity;
    }

    private static unsafe Blob CompileShader(string entryPoint, string profile)
    {
        var source = System.Text.Encoding.ASCII.GetBytes(ShaderSource);
        fixed (byte* pointer = source)
        {
            var result = Compiler.Compile(pointer, (nuint)source.Length, "world-editor-shader", null, null,
                entryPoint, profile, ShaderFlags.OptimizationLevel3, EffectFlags.None, out var code, out var errors);
            if (result.Failure)
            {
                var message = errors is null ? result.Description : Marshal.PtrToStringAnsi(errors.BufferPointer);
                errors?.Dispose();
                code?.Dispose();
                throw new InvalidOperationException($"Shader {entryPoint}: {message}");
            }
            errors?.Dispose();
            return code;
        }
    }

    private void CreateRenderTarget()
    {
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTarget = _device.CreateRenderTargetView(backBuffer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _context.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>());
        _renderTarget?.Dispose();
        _vertexBuffer.Dispose();
        _constantBuffer.Dispose();
        _inputLayout.Dispose();
        _vertexShader.Dispose();
        _pixelShader.Dispose();
        _texturedPixelShader.Dispose();
        _textPixelShader.Dispose();
        _texturedInputLayout.Dispose();
        _fontView?.Dispose();
        _fontTexture?.Dispose();
        _sampler?.Dispose();
        _rasterizer?.Dispose();
        _alphaBlend?.Dispose();
        _additiveBlend?.Dispose();
        _atlasResource?.Dispose();
        _swapChain.Dispose();
        _context.Dispose();
        _device.Dispose();
    }
}
