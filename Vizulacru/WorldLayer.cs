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

internal sealed class GameOptions
{
    public int Rounds { get; init; }
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
    private readonly IBotConfig _config;
    private readonly GameOptions _gameOptions;
    private readonly OrthographicCameraController2D _cameraController;

    private readonly QuadBatch _dynamicBatch;
    private readonly QuadBatch _unexploredTreeBatch;

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
                        Math.Clamp((int)Math.Round(mouseWorld.X), 0, _controller.Tiles.Size.X - 1),
                        Math.Clamp(-(int)Math.Round(mouseWorld.Y), 0, _controller.Tiles.Size.Y - 1)
                    )
                );
        }
    }

    private bool _follow = true;

    private void Focus()
    {
        if (_lastGameState == null)
        {
            return;
        }

        _cameraController.FuturePosition2 = GridPos(_lastGameState.Player.Position);
    }

    public WorldLayer(App app, ImGuiLayer imGui, Textures textures, ILogger<RemoteGame> remoteGameLogger, IBotConfig config, GameOptions gameOptions)
    {
        _app = app;
        _imGui = imGui;
        _textures = textures;
        _config = config;
        _gameOptions = gameOptions;

        _cameraController = new OrthographicCameraController2D(
            new OrthographicCamera(0, -1, 1),
            translationInterpolate: 25f,
            zoomInterpolate: 10f
        );

        _cameraController.FutureZoom = 35;

        _dynamicBatch = app.Resources.BatchPool.Get();
        _unexploredTreeBatch = app.Resources.BatchPool.Get();

        _postProcess = new PostProcessor(app)
        {
            BackgroundColor = RgbaFloat.Black
        };

        UpdatePipelines();
        Focus();

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

        var mouse = MouseGrid;

        if (_controller != null && _lastGameState != null)
        {
            if (Begin("Game"))
            {
                var world = _controller.Tiles;

                ImGui.Text($"Round: {_lastGameState.Round}/{_controller.AcidRounds}");

                ImGui.Checkbox("Follow", ref _follow);

                ImGui.Separator();
                ImGui.Text($"Mouse: {mouse}");

                ImGui.Text($"Grid size: {world.Size}");
                ImGui.Text($"Selected: {world[mouse.X, mouse.Y]}");
                ImGui.Text($"Discovery: {_controller.ExplorationMode}");

                var ores = new Histogram<TileType>();

                foreach (var position in _controller.PendingOres.Keys)
                {
                    ores[_controller.Tiles[position]]++;
                }

                ImGui.Indent();

                foreach (var type in ores.Keys)
                {
                    ImGui.Text($"{type}: {ores[type]}");
                }

                ImGui.Unindent();

                ImGui.Separator();

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

                var player = _lastGameState.Player;

                ImGui.Text("Inventory:");
                {
                    ImGui.Indent();
                    ImGui.Text($"Osmium x {player.OsmiumCount}");
                    ImGui.Text($"Iron   x {player.IronCount}");
                    ImGui.Text($"Cobble x {player.CobbleCount}");
                    ImGui.Unindent();
                }

                ImGui.TextColored(player.Hp < 10 ? new Vector4(1, 0, 0, 1) : new Vector4(0, 1, 0, 1), $"HP: {player.Hp}");

                ImGui.Text("Abilities:");
                {
                    ImGui.Indent();

                    ImGui.Text($"Battery: {(player.HasBattery ? "yes" : "no")}");
                    ImGui.Text($"Antenna: {(player.HasAntenna ? "yes" : "no")}");
                    ImGui.Separator();

                    ImGui.Text($"Attack: {player.Attack}");
                    ImGui.Text($"Sight : {player.Sight}");
                    ImGui.Text($"Wheel : {player.Movement}");
                    ImGui.Text($"Dig   : {player.Dig}");

                    ImGui.Unindent();
                }
            }

            ImGui.End();

            if (Begin("Attack Log"))
            {
                if (_controller.Attacks.Count > 0)
                {
                    foreach (var attack in _controller.Attacks)
                    {
                        ImGui.Text($"R: {attack.Round}, T: {attack.TargetPos}");
                        ImGui.Separator();
                    }
                }
                else
                {
                    ImGui.Text("N/A");
                }
            }

            ImGui.End();

            if (Begin("Damage Log"))
            {
                if (_controller.DamageTaken.Count > 0)
                {
                    foreach (var loss in _controller.DamageTaken)
                    {
                        ImGui.Text($"-{loss.Delta} HP @ {loss.Round}");
                        ImGui.Separator();
                    }
                }
                else
                {
                    ImGui.Text("N/A");
                }
            }

            ImGui.End();

            if (Begin("Logs"))
            {
                var prevDepth = 0;

                foreach (var log in _controller.Logs)
                {
                    while (log.Depth > prevDepth)
                    {
                        ImGui.Indent();
                        prevDepth++;
                    }

                    while (log.Depth < prevDepth)
                    {
                        ImGui.Unindent();
                        prevDepth--;
                    }

                    ImGui.TextColored(log.Type switch
                    {
                        LogType.Info => new Vector4(0, 1, 0, 1),
                        LogType.Warning => new Vector4(1, 1, 0, 1),
                        LogType.Peril => new Vector4(0.7f, 0, 0, 1),
                        LogType.FTL => new Vector4(1, 0, 0, 1),
                        _ => throw new ArgumentOutOfRangeException()
                    }, log.Text);
                }
            }

            ImGui.End();
        }
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
            Focus();
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

    private void UpdateGame()
    {
        if (!_game.TryRead(out var state))
        {
            return;
        }

        _controller ??= new Bot(_game.Match, _config, _gameOptions.Rounds);

        var cl = new CommandState(state, _game.Match.BasePosition);

        _controller.Update(cl);
        _game.Submit(cl);
        _lastGameState = state;
    }

    protected override void Update(FrameInfo frameInfo)
    {
        base.Update(frameInfo);
        
        if (_follow)
        {
            Focus();
        }

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

        var world = _controller.Tiles;
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
                    TextureSampler ts;

                    if (playerPos.X == x && playerPos.Y == y)
                    {
                        ts = _textures.PlayerRobotTile;
                    }
                    else
                    {
                        ts = type switch
                        {
                            TileType.Dirt => _textures.DirtTile,
                            TileType.Stone => _textures.StoneTile,
                            TileType.Cobble => _textures.CobblestoneTile,
                            TileType.Bedrock => _textures.BedrockTile,
                            TileType.Iron => _textures.IronTile,
                            TileType.Osmium => _textures.OsmiumTile,
                            TileType.Base => _textures.BaseTile,
                            TileType.Acid => _textures.AcidTile,
                            TileType.Robot0 => _textures.Enemy0Tile,
                            TileType.Robot1 => _textures.Enemy1Tile,
                            TileType.Robot2 => _textures.Enemy2Tile,
                            TileType.Robot3 => _textures.Enemy3Tile,
                            TileType.Robot4 => _textures.Enemy4Tile,
                            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unknown texture for {type}")
                        };
                    }

                    _dynamicBatch.TexturedQuad(pos, scale, ts);
                }
            }
        }
    }

    private void RenderAttackHistory()
    {
        if (_controller == null)
        {
            return;
        }

        var attackIntensities = new Histogram<Vector2di>();

        foreach (var attack in _controller.Attacks)
        {
            attackIntensities[attack.TargetPos]++;
        }

        var maxCount = 0;

        foreach (var position in attackIntensities.Keys)
        {
            var count = attackIntensities[position];
            maxCount = Math.Max(maxCount, count);
        }

        if (maxCount > 0)
        {
            foreach (var position in attackIntensities.Keys)
            {
                _dynamicBatch.Quad(
                    GridPos(position), 
                    new RgbaFloat4(1, 0, 0, attackIntensities[position] / (float)maxCount * 0.8f)
                );
            }
        }
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

    private void RenderSpottedPlayers()
    {
        if (_controller == null)
        {
            return;
        }

        _dynamicBatch.Effects = _dynamicBatch.Effects with { Tint = new RgbaFloat(1, 1, 1, 0.5f) };

        foreach (var tile in _controller.SpottedPlayers.Keys)
        {
            var type = _controller.SpottedPlayers[tile].Id;

            _dynamicBatch.TexturedQuad(GridPos(tile), TileScale, type switch
            {
                TileType.Robot0 => _textures.Enemy0Tile,
                TileType.Robot1 => _textures.Enemy1Tile,
                TileType.Robot2 => _textures.Enemy2Tile,
                TileType.Robot3 => _textures.Enemy3Tile,
                TileType.Robot4 => _textures.Enemy4Tile,
                _ => throw new ArgumentOutOfRangeException()
            });
        }
    }

    private void RenderMiningTarget()
    {
        if (_lastGameState == null || _controller is not { NextMiningTile: not null })
        {
            return;
        }

        _dynamicBatch.Quad(GridPos(_controller.NextMiningTile.Value), TileScale * 0.5f, new RgbaFloat4(0.1f, 1f, 0.2f, 0.3f));

        var player = _lastGameState.Player;

        var offsets = Player.SightOffsets[player.Sight];
        var contours = Player.SightContours[player.Sight].ToHashSet();

        foreach (var offset in offsets)
        {
            var scale = contours.Contains(offset) ? TileScale * 1f : TileScale * 0.8f;

            _dynamicBatch.Quad(GridPos(_controller.NextMiningTile.Value + offset), scale, new RgbaFloat4(1f, 0.1f, 0.1f, 0.4f));
        }
    }

    protected override void Render(FrameInfo frameInfo)
    {
        _postProcess.ClearColor();

        void RenderPass(Action body)
        {
            _dynamicBatch.Effects = QuadBatchEffects.Transformed(_cameraController.Camera.CameraMatrix);
            _dynamicBatch.Clear();
            body();
            _dynamicBatch.Submit(framebuffer: _postProcess.InputFramebuffer);
        }

        RenderPass(RenderTerrain);
        RenderPass(RenderAttackHistory);
        RenderPass(RenderView);
        RenderPass(RenderSpottedPlayers);
        RenderPass(RenderMiningTarget);
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