using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using GameFramework;
using System.Numerics;
using System.Threading.Channels;
using Common;
using Daleks;
using GameFramework.Extensions;
using GameFramework.ImGui;
using GameFramework.Layers;
using GameFramework.PostProcessing;
using GameFramework.Renderer;
using GameFramework.Renderer.Batch;
using GameFramework.Scene;
using GameFramework.Utilities;
using GameFramework.Utilities.Extensions;
using ImGuiNET;
using Microsoft.Extensions.Logging;
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

internal sealed class RemoteGame : IDisposable
{
    private readonly ILogger<RemoteGame> _logger;
    public int Id { get; }
    public int AcidRounds { get; }
    public Task PollTask { get; }

    private readonly CancellationTokenSource _cts = new();

    private MatchInfo? _matchInfo;

    public MatchInfo Match => _matchInfo ?? throw new InvalidOperationException("Match not initialized");

    private readonly Channel<GameSnapshot> _gameChannel = Channel.CreateBounded<GameSnapshot>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = true,
        SingleReader = true
    });

    private readonly Channel<CommandState> _commandChannel = Channel.CreateBounded<CommandState>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = true,
        SingleReader = true
    });

    public RemoteGame(ILogger<RemoteGame> logger, int id, int acidRounds)
    {
        _logger = logger;
        Id = id;
        AcidRounds = acidRounds;
        PollTask = PollAndPostAsync();
    }

    private async Task PollAndPostAsync()
    {
        var manager = new GameManager(Id, AcidRounds);

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var state = await manager.ReadAsync(_cts.Token);

                if (state == null)
                {
                    await Task.Delay(5, _cts.Token);
                    continue;
                }

                _matchInfo ??= manager.MatchInfo;

                await _gameChannel.Writer.WriteAsync(state, _cts.Token);

                var command = await _commandChannel.Reader.ReadAsync(_cts.Token);

                await manager.SubmitAsync(command, _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignored
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Polling ex: {ex}", ex);
            Assert.Fail($"Unexpected polling exception: {ex}");
            // problem
        }
    }

    public bool TryRead([NotNullWhen(true)] out GameSnapshot? state) => _gameChannel.Reader.TryRead(out state);

    public void Submit(CommandState state)
    {
        if (!_commandChannel.Writer.TryWrite(state))
        {
            // Should complete (AI logic must post after a state was read, which happens after commands are submitted)

            throw new InvalidOperationException("Tried to post commands in an illegal fashion"); // nice message, i guess
        }        
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        PollTask.Wait();
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
    private readonly OrthographicCameraController2D _cameraController;

    private readonly QuadBatch _dynamicBatch;
    private readonly QuadBatch _unexploredTreeBatch;
    private UnexploredTreeRenderer? _unexploredTreeRenderer;

    private bool _renderUnexploredTree = true;
    private bool _renderUnexploredRegion = true;

    private readonly PostProcessor _postProcess;
    private bool _dragCamera;

    private readonly RemoteGame _game;
    private Bot? _controller;
    private GameSnapshot? _lastGameState;

    private Vector2di MouseGrid
    {
        get
        {
            if (_controller == null)
            {
                return Vector2di.Zero;
            }

            return _cameraController.Camera
                .MouseToWorld2D(_app.Input.MousePosition, _app.Window.Width, _app.Window.Height).Map(mouseWorld =>
                    new Vector2di(
                        Math.Clamp((int)Math.Round(mouseWorld.X), 0, _controller.TileWorld.Size.X - 1),
                        Math.Clamp(-(int)Math.Round(mouseWorld.Y), 0, _controller.TileWorld.Size.Y - 1)
                    )
                );
        }
    }

    private void FocusCenter()
    {
        if (_controller == null)
        {
            return;
        }

        var world = _controller.TileWorld;

        _cameraController.FuturePosition2 = GridPos(world.Size.X / 2, world.Size.Y / 2);
        _cameraController.FutureZoom = 35f;
    }

    public WorldLayer(App app, ImGuiLayer imGui, Textures textures, ILogger<RemoteGame> remoteGameLogger)
    {
        _app = app;
        _imGui = imGui;
        _textures = textures;

        _cameraController = new OrthographicCameraController2D(
            new OrthographicCamera(0, -1, 1),
            translationInterpolate: 25f,
            zoomInterpolate: 10f
        );

        _dynamicBatch = app.Resources.BatchPool.Get();
        _unexploredTreeBatch = app.Resources.BatchPool.Get();

        _postProcess = new PostProcessor(app)
        {
            BackgroundColor = RgbaFloat.Black
        };

        UpdatePipelines();
        FocusCenter();

        _game = new RemoteGame(remoteGameLogger, app.SelectedId, 150);

        _imGui.Submit += ImGuiOnSubmit;
    }

    private void ImGuiOnSubmit(ImGuiRenderer obj)
    {
        ImGui.DockSpaceOverViewport(ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode);

        try
        {
            if (ImGui.BeginMainMenuBar())
            {
                if (ImGui.Button("Exit"))
                {
                    _app.Layers.RemoveLayer(this);

                    Dispose();
                }
            }
        }
        finally
        {
            ImGui.EndMainMenuBar();
        }

        const string id = "WorldLayer";

        bool Begin(string title) => ImGuiExt.Begin(title, id);

        if (Begin("Display"))
        {
            ImGui.Checkbox("Show unexplored regions", ref _renderUnexploredTree);
            ImGui.Checkbox("Show unexplored target", ref _renderUnexploredRegion);
        }

        ImGui.End();

        var mouse = MouseGrid;

        if (Begin("Tiles"))
        {
            ImGui.Text($"Mouse: {mouse}");

            if (_controller != null)
            {
                var world = _controller.TileWorld;

                ImGui.Text($"Grid size: {world.Size}");
                ImGui.Text($"Selected: {world[mouse.X, mouse.Y]}");

                ImGui.Text($"Max overvisits: {_maxOverVisits}");
                ImGui.Text($"Average overvisits: {_averageOverVisits:F4}");

                var ores = new Histogram<TileType>();

                foreach (var position in _controller.PendingOres.Keys)
                {
                    ores[_controller.TileWorld.Tiles[position]]++;
                }

                foreach (var type in ores.Keys)
                {
                    ImGui.Text($"{type}: {ores[type]}");
                }

                if (_controller.UpgradeQueue.Count > 0)
                {
                    ImGui.Text("Upgrade queue:");
                    ImGui.Indent();
                    foreach (var abilityType in _controller.UpgradeQueue)
                    {
                        ImGui.Text(abilityType.ToString());
                    }
                    ImGui.Unindent();
                }
            }
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
        _cameraController.Camera.AspectRatio = _app.Window.Width / (float)_app.Window.Height;

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
                var delta = (_app.Input.MouseDelta / new Vector2(_app.Window.Width, _app.Window.Height)) * new Vector2(-1, 1) * _cameraController.Camera.Zoom * CamDragSpeed;
                _cameraController.FuturePosition2 += delta;
            }

            _cameraController.FutureZoom += _app.Input.ScrollDelta * CamZoomSpeed * frameInfo.DeltaTime;
            _cameraController.FutureZoom = Math.Clamp(_cameraController.FutureZoom, MinZoom, MaxZoom);
        }

        _cameraController.Update(frameInfo.DeltaTime);
    }

    private readonly Histogram<Vector2di> _overviewHistogram = new();

    private int _maxOverVisits;
    private double _averageOverVisits;

    private void UpdateGame()
    {
        if (!_game.TryRead(out var state))
        {
            return;
        }

        foreach (var tile in state.DiscoveredTiles)
        {
            _overviewHistogram[tile]++;
        }

        _maxOverVisits = 0;
        _averageOverVisits = 0;

        foreach (var tile in _overviewHistogram.Keys)
        {
            var visits = _overviewHistogram[tile];
            _maxOverVisits = Math.Max(_maxOverVisits, visits);
            _averageOverVisits += visits;
        }

        if (_overviewHistogram.Keys.Count > 0)
        {
            _averageOverVisits /= _overviewHistogram.Keys.Count;
        }

        _controller ??= new Bot(_game.Match, _game.AcidRounds, new BotConfig());

        var cl = new CommandState(state, _game.Match.BasePosition);

        _controller.Update(cl);

        _game.Submit(cl);

        _lastGameState = state;
    }

    protected override void Update(FrameInfo frameInfo)
    {
        base.Update(frameInfo);

        UpdateCamera(frameInfo);
        UpdateGame();
    }

    private static Vector2 GridPos(Vector2di pos) => new(pos.X, -pos.Y);
    private static Vector2 GridPos(Vector2 pos) => new(pos.X, -pos.Y);
    private static Vector2 GridPos(int x, int y) => new(x, -y);
    private static Vector2 GridPos(float x, float y) => new(x, -y);

    private void RenderHighlight()
    {
        _dynamicBatch.Quad(GridPos(MouseGrid), TileScale, HighlightColor);
    }

    private void RenderTerrain()
    {
        if (_controller == null)
        {
            return;
        }

        var world = _controller.TileWorld;
        var playerPos = Assert.NotNull(_lastGameState).Player.Position;

        var unknownColor = new RgbaFloat4(18 / 255f, 18 / 255f, 18 / 255f, 1f);

        var scale = TileScale;

        for (var y = 0; y < world.Size.Y; y++)
        {
            for (var x = 0; x < world.Size.X; x++)
            {
                var type = world[x, y];
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
                        TileType.Robot => (playerPos.X == x && playerPos.Y == y) ? _textures.PlayerRobotTile : _textures.EnemyRobotTile,
                        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unknown texture for {type}")
                    });
                }
            }
        }

        /*for (var index = 1; index < _controller.SearchGrid.C.Count; index++)
        {
            var g = _controller.SearchGrid.C[index];
            var rnd = new Random(g.GetHashCode());
            var colo = rnd.NextVector4(min: 0f) with { W = 0.3f };

            var a = _controller.SearchGrid.C[index - 1];
            var b = _controller.SearchGrid.C[index ];

            Vector2 p(SearchGrid.Node n) => GridPos(n.Rect.X + n.Rect.Width / 2f, n.Rect.Y + n.Rect.Height / 2f);

            _dynamicBatch.Line(p(a), p(b), colo, 0.1f);

        }*/
    }

    private void RenderView()
    {
        if (_lastGameState == null)
        {
            return;
        }

        foreach (var discoveredTile in _lastGameState.DiscoveredTiles)
        {
            _dynamicBatch.Quad(GridPos(discoveredTile), TileScale, new RgbaFloat4(0f, 0f, 1f, 0.2f));
        }
    }

    private void RenderPath()
    {
        if (_controller?.Path == null)
        {
            return;
        }

        for (var i = 1; i < _controller.Path.Count; i++)
        {
            var a = _controller.Path[i - 1];
            var b = _controller.Path[i];
            var color = new Random(HashCode.Combine(a, b)).NextVector4(min: 0.2f) with { W = 0.8f };
            _dynamicBatch.Line(GridPos(a), GridPos(b), color, 0.2f);
        }
    }

    private void RenderUnexploredTarget()
    {
        if (_controller is { NextMiningTile: not null })
        {
            _dynamicBatch.Quad(GridPos(_controller.NextMiningTile.Value), TileScale * 0.5f, new RgbaFloat4(0.1f, 1f, 0.2f, 0.3f));
        }
    }

    protected override void Render(FrameInfo frameInfo)
    {
        _dynamicBatch.Effects = QuadBatchEffects.Transformed(_cameraController.Camera.CameraMatrix);
        
        _postProcess.ClearColor();

        void RenderPass(Action body)
        {
            _dynamicBatch.Clear();
            body();
            _dynamicBatch.Submit(framebuffer: _postProcess.InputFramebuffer);
        }

        RenderPass(RenderTerrain);
        RenderPass(RenderView);

        if (_renderUnexploredTree && _controller != null)
        {
            _unexploredTreeRenderer ??= new UnexploredTreeRenderer(_controller.TileWorld, _unexploredTreeBatch);
            _unexploredTreeRenderer.Update();
            _unexploredTreeBatch.Effects = QuadBatchEffects.Transformed(_cameraController.Camera.CameraMatrix);
            _unexploredTreeBatch.Submit(framebuffer: _postProcess.InputFramebuffer);
        }

        RenderPass(RenderUnexploredTarget);
        RenderPass(RenderPath);

        RenderPass(RenderHighlight);

        _postProcess.Render();
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _imGui.Submit -= ImGuiOnSubmit;

        _disposed = true;

        _app.Resources.BatchPool.Return(_dynamicBatch);
        _app.Resources.BatchPool.Return(_unexploredTreeBatch);
        _postProcess.Dispose();
        _game.Dispose();
    }
}