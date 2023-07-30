using System.Drawing;
using GameFramework;
using System.Numerics;
using Common;
using Daleks;
using GameFramework.Extensions;
using GameFramework.ImGui;
using GameFramework.Layers;
using GameFramework.PostProcessing;
using GameFramework.Renderer;
using GameFramework.Renderer.Batch;
using GameFramework.Scene;
using Veldrid;
using Vizulacru.Assets;

namespace Vizulacru;

internal sealed class WorldLayer : Layer, IDisposable
{
    private const float MinZoom = 1.0f;
    private const float MaxZoom = 150f;
    private const float CamDragSpeed = 2.5f;
    private const float CamZoomSpeed = 15f;

    private static readonly Vector2 TileScale = Vector2.One;
    private static readonly RgbaFloat4 HighlightColor = new(0.1f, 0.8f, 0.05f, 0.5f);

    private readonly App _app;
    private readonly ImGuiLayer _imGui;
    private readonly Textures _textures;
    private readonly OrthographicCameraController2D _controller;

    private readonly QuadBatch _batch;
    private readonly PostProcessor _postProcess;
    private bool _dragCamera;

    private readonly World _world = new World(new Vector2di(21, 21)).Also(w =>
    {
        for (var i = 0; i < w.Size.X; i++)
        {
            for (var j = 0; j < w.Size.Y; j++)
            {
                if (Random.Shared.NextDouble() > 0.9)
                {
                    w.Tiles[i, j] = TileType.Bedrock;
                }
            }
        }
    });

    private Vector2di MouseGrid => _controller.Camera
        .MouseToWorld2D(_app.Input.MousePosition, _app.Window.Width, _app.Window.Height).Map(mouseWorld =>
            new Vector2di(
                Math.Clamp((int)Math.Round(mouseWorld.X), 0, _world.Size.X),
                Math.Clamp(-(int)Math.Round(mouseWorld.Y), 0, _world.Size.Y)
            )
        );

    private void FocusCenter()
    {
        _controller.FuturePosition2 = GridPos(_world.Size.X / 2, _world.Size.Y / 2);
        _controller.FutureZoom = 35f;
    }

    public WorldLayer(App app, ImGuiLayer imGui, Textures textures)
    {
        _app = app;
        _imGui = imGui;
        _textures = textures;

        _controller = new OrthographicCameraController2D(
            new OrthographicCamera(0, -1, 1),
            translationInterpolate: 25f,
            zoomInterpolate: 10f
        );

        _batch = app.Resources.BatchPool.Get();

        _postProcess = new PostProcessor(app)
        {
            BackgroundColor = RgbaFloat.Black
        };

        UpdatePipelines();

        FocusCenter();
    }

    protected override void OnAdded()
    {
        RegisterHandler<MouseEvent>(OnMouseEvent);
        RegisterHandler<KeyEvent>(OnKeyEvent);
    }

    private bool OnMouseEvent(MouseEvent @event)
    {
        if (@event is { MouseButton: MouseButton.Right, Down: true })
        {
            _dragCamera = true;
        }
        else if (@event is { MouseButton: MouseButton.Right, Down: false })
        {
            _dragCamera = false;
        }
        else if (@event is { MouseButton: MouseButton.Left, Down: false })
        {
            _test.Add(MouseGrid);
            while (_test.Count > 2)
            {
                _test.RemoveAt(0);
            }
        }
        
        return true;
    }
   
    private bool OnKeyEvent(KeyEvent arg)
    {
        if (arg is { Key: Key.F, Down: false })
        {
            FocusCenter();
        }

        return true;
    }

    private void UpdatePipelines()
    {
        _controller.Camera.AspectRatio = _app.Window.Width / (float)_app.Window.Height;

        _postProcess.ResizeInputs(_app.Window.Size() * 2);
        _postProcess.SetOutput(_app.Device.SwapchainFramebuffer);
        _batch.UpdatePipelines(outputDescription: _postProcess.InputFramebuffer.OutputDescription);
    }

    protected override void Resize(Size size)
    {
        base.Resize(size);

        UpdatePipelines();
    }

    private void UpdateCamera(FrameInfo frameInfo)
    {
        if (!_imGui.Captured)
        {
            if (_dragCamera)
            {
                var delta = (_app.Input.MouseDelta / new Vector2(_app.Window.Width, _app.Window.Height)) * new Vector2(-1, 1) * _controller.Camera.Zoom * CamDragSpeed;
                _controller.FuturePosition2 += delta;
            }

            _controller.FutureZoom += _app.Input.ScrollDelta * CamZoomSpeed * frameInfo.DeltaTime;
            _controller.FutureZoom = Math.Clamp(_controller.FutureZoom, MinZoom, MaxZoom);
        }

        _controller.Update(frameInfo.DeltaTime);
    }

    protected override void Update(FrameInfo frameInfo)
    {
        base.Update(frameInfo);

        UpdateCamera(frameInfo);
    }

    private static Vector2 GridPos(Vector2di pos) => new(pos.X, -pos.Y);
    private static Vector2 GridPos(int x, int y) => new(x, -y);

    private readonly List<Vector2di> _test = new();

    private void RenderHighlight()
    {
        _batch.Quad(GridPos(MouseGrid), TileScale, HighlightColor);
    }

    private void RenderTerrain()
    {
        var unknownColor = new RgbaFloat4(18 / 255f, 18 / 255f, 18 / 255f, 1f);

        var scale = TileScale;

        for (var y = 0; y < _world.Size.Y; y++)
        {
            for (var x = 0; x < _world.Size.X; x++)
            {
                var type = _world[x, y];
                var pos = GridPos(x, y);

                if (type == TileType.Unknown)
                {
                    _batch.Quad(pos, scale, unknownColor);
                }
                else
                {
                    _batch.TexturedQuad(pos, scale, type switch
                    {
                        TileType.Dirt => _textures.DirtTile,
                        TileType.Stone => _textures.StoneTile,
                        TileType.Cobblestone => _textures.CobblestoneTile,
                        TileType.Bedrock => _textures.BedrockTile,
                        TileType.Iron => _textures.IronTile,
                        TileType.Osmium => _textures.OsmiumTile,
                        TileType.Base => _textures.BaseTile,
                        TileType.Acid => _textures.AcidTile,
                        TileType.Robot => _textures.EnemyRobotTile,
                        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unknown texture for {type}")
                    });
                }
            }
        }
    }

    protected override void Render(FrameInfo frameInfo)
    {
        _batch.Effects = QuadBatchEffects.Transformed(_controller.Camera.CameraMatrix);
        
        _postProcess.ClearColor();

        void RenderPass(Action body)
        {
            _batch.Clear();
            body();
            _batch.Submit(framebuffer: _postProcess.InputFramebuffer);
        }

        RenderPass(RenderTerrain);

        if (_test.Count == 2)
        {
            if (_world.TryFindPath(_test[0], _test[1], out var path))
            {
                RenderPass(() =>
                {
                    foreach (var v in path)
                    {
                        _batch.Quad(GridPos(v), RgbaFloat4.Cyan);
                    }
                });
                
                Console.Title = $"{path.Count}";
            }
            else
            {
                Console.Title = "no path";
            }

            RenderPass(() =>
            {
                foreach (var vector2di in _test)
                {
                    _batch.Quad(GridPos(vector2di), RgbaFloat4.Red);
                }
            });

        }

        RenderPass(RenderHighlight);

        _postProcess.Render();
    }

    public void Dispose()
    {
        _app.Resources.BatchPool.Return(_batch);
        _postProcess.Dispose();
    }
}