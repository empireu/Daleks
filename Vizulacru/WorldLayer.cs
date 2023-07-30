using System.Drawing;
using GameFramework;
using System.Numerics;
using GameFramework.Extensions;
using GameFramework.ImGui;
using GameFramework.Layers;
using GameFramework.PostProcessing;
using GameFramework.Renderer.Batch;
using GameFramework.Scene;
using Veldrid;
using Vizulacru.Assets;

namespace Vizulacru;

internal sealed class WorldLayer : Layer, IDisposable
{
    private const float MinZoom = 2.0f;
    private const float MaxZoom = 10f;
    private const float CamDragSpeed = 5f;
    private const float CamZoomSpeed = 5f;

    private readonly App _app;
    private readonly ImGuiLayer _imGui;
    private readonly Textures _textures;
    private readonly OrthographicCameraController2D _controller;

    private readonly QuadBatch _terrainBatch;
    private readonly PostProcessor _postProcess;
    private bool _dragCamera;

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

        _terrainBatch = app.Resources.BatchPool.Get();

        _postProcess = new PostProcessor(app)
        {
            BackgroundColor = RgbaFloat.Black
        };

        UpdatePipelines();
    }

    protected override void OnAdded()
    {
        RegisterHandler<MouseEvent>(OnMouseEvent);
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

    private void UpdatePipelines()
    {
        _controller.Camera.AspectRatio = _app.Window.Width / (float)_app.Window.Height;

        _postProcess.ResizeInputs(_app.Window.Size() * 2);
        _postProcess.SetOutput(_app.Device.SwapchainFramebuffer);
        _terrainBatch.UpdatePipelines(outputDescription: _postProcess.InputFramebuffer.OutputDescription);
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

    protected override void Render(FrameInfo frameInfo)
    {
        _terrainBatch.Effects = QuadBatchEffects.Transformed(_controller.Camera.CameraMatrix);
        _postProcess.ClearColor();

        _terrainBatch.TexturedQuad(Vector2.Zero, _textures.DirtTile);
        _terrainBatch.Submit(framebuffer: _postProcess.InputFramebuffer);

        _postProcess.Render();
    }

    public void Dispose()
    {
        _app.Resources.BatchPool.Return(_terrainBatch);
        _postProcess.Dispose();
    }
}