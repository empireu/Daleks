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
using GameFramework.Utilities.Extensions;
using ImGuiNET;
using Veldrid;
using Vizulacru.Assets;

namespace Vizulacru;

internal sealed class UnexploredTreeRenderer
{
    private readonly TileWorld _world;
    private readonly QuadBatch _batch;
    private int _lastVersion = -1;

    public UnexploredTreeRenderer(TileWorld world, QuadBatch batch)
    {
        _world = world;
        _batch = batch;
    }

    private readonly Dictionary<Vector2di, Vector4> _nodeColors = new();

    public bool Update()
    {
        if (_lastVersion == _world.UnexploredTreeVersion)
        {
            return false;
        }

        _batch.Clear();

        var worldRectangle = new System.Drawing.Rectangle(0, 0, _world.Size.X, _world.Size.Y);

        void Visit(IQuadTreeView node)
        {
            if (!node.NodeRectangle.IntersectsWith(worldRectangle))
            {
                return;
            }

            var tl = new Vector2(node.Position.X, -node.Position.Y) - new Vector2(0.5f, -0.5f);
            var sz = node.Size;

            if (node.IsFilled || node.Size == 1)
            {
                _batch.Quad(tl + new Vector2(sz / 2f, -sz / 2f), new Vector2(0.1f), RgbaFloat4.Red);
            }
            else
            {
                var color = _nodeColors.GetOrAdd(node.Position, _ =>
                {
                    var random = new Random(node.Position.GetHashCode());

                    return new Vector4(
                        random.NextFloat(min: 0.5f),
                        random.NextFloat(min: 0.5f),
                        random.NextFloat(min: 0.5f),
                        0.4f
                    );
                });

                var dx = new Vector2(sz, 0);
                var dy = new Vector2(0, sz);
                var dx2 = dx / 2f;
                var dy2 = dy / 2f;

                for (byte i = 0; i < 4; i++)
                {
                    node.GetChildView((BitQuadTree.Quadrant)i)?.Also(Visit);
                }

                _batch.Line(tl, tl + dx, color, 0.1f);
                _batch.Line(tl - dy, tl - dy + dx, color, 0.1f);
                _batch.Line(tl, tl - dy, color, 0.1f);
                _batch.Line(tl + dx, tl + dx - dy, color, 0.1f);
                _batch.Line(tl - dy2, tl + dx - dy2, color, 0.1f);
                _batch.Line(tl + dx2, tl + dx2 - dy, color, 0.1f);
            }
        }

        Visit(_world.UnexploredTree);

        _lastVersion = _world.UnexploredTreeVersion;

        return true;
    }
}

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

    private readonly QuadBatch _dynamicBatch;
    private readonly QuadBatch _unexploredTreeBatch;
    private readonly UnexploredTreeRenderer _unexploredTreeRenderer;

    private bool _renderUnexploredTree = true;

    private readonly PostProcessor _postProcess;
    private bool _dragCamera;

    private readonly TileWorld _tileWorld = new TileWorld(new Vector2di(64, 64)).Also(w =>
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
                Math.Clamp((int)Math.Round(mouseWorld.X), 0, _tileWorld.Size.X - 1),
                Math.Clamp(-(int)Math.Round(mouseWorld.Y), 0, _tileWorld.Size.Y - 1)
            )
        );

    private void FocusCenter()
    {
        _controller.FuturePosition2 = GridPos(_tileWorld.Size.X / 2, _tileWorld.Size.Y / 2);
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

        _dynamicBatch = app.Resources.BatchPool.Get();
        _unexploredTreeBatch = app.Resources.BatchPool.Get();
        _unexploredTreeRenderer = new UnexploredTreeRenderer(_tileWorld, _unexploredTreeBatch);

        _postProcess = new PostProcessor(app)
        {
            BackgroundColor = RgbaFloat.Black
        };

        UpdatePipelines();

        FocusCenter();

        _imGui.Submit += ImGuiOnSubmit;
    }

    private void ImGuiOnSubmit(ImGuiRenderer obj)
    {
        if (ImGui.Begin("Display"))
        {
            ImGui.Checkbox("Show unexplored regions", ref _renderUnexploredTree);
        }

        ImGui.End();
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
            _tileWorld.SetExplored(MouseGrid);
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
        _dynamicBatch.UpdatePipelines(outputDescription: _postProcess.InputFramebuffer.OutputDescription);
        _unexploredTreeBatch.UpdatePipelines(outputDescription: _postProcess.InputFramebuffer.OutputDescription);
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

    private void RenderHighlight()
    {
        _dynamicBatch.Quad(GridPos(MouseGrid), TileScale, HighlightColor);
    }

    private void RenderTerrain()
    {
        var unknownColor = new RgbaFloat4(18 / 255f, 18 / 255f, 18 / 255f, 1f);

        var scale = TileScale;

        for (var y = 0; y < _tileWorld.Size.Y; y++)
        {
            for (var x = 0; x < _tileWorld.Size.X; x++)
            {
                var type = _tileWorld[x, y];
                var pos = GridPos(x, y);

                if (type == TileType.Unknown)
                {
                    _dynamicBatch.Quad(pos, scale, unknownColor);
                }
                else
                {
                    _dynamicBatch.TexturedQuad(pos, scale, type switch
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

    private readonly Dictionary<Vector2di, Vector4> _nodeColors = new();

    protected override void Render(FrameInfo frameInfo)
    {
        _dynamicBatch.Effects = QuadBatchEffects.Transformed(_controller.Camera.CameraMatrix);
        
        _postProcess.ClearColor();

        void RenderPass(Action body)
        {
            _dynamicBatch.Clear();
            body();
            _dynamicBatch.Submit(framebuffer: _postProcess.InputFramebuffer);
        }

        RenderPass(RenderTerrain);

        if (_renderUnexploredTree)
        {
            _unexploredTreeRenderer.Update();
            _unexploredTreeBatch.Effects = QuadBatchEffects.Transformed(_controller.Camera.CameraMatrix);
            _unexploredTreeBatch.Submit(framebuffer: _postProcess.InputFramebuffer);
        }

        RenderPass(RenderHighlight);

        _postProcess.Render();
    }

    public void Dispose()
    {
        _app.Resources.BatchPool.Return(_dynamicBatch);
        _app.Resources.BatchPool.Return(_unexploredTreeBatch);
        _postProcess.Dispose();
    }
}