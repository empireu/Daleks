using GameFramework;
using GameFramework.Renderer;
using Veldrid;

namespace Vizulacru.Assets;

internal sealed class Textures
{
    public readonly TextureSampler DirtTile;
    public readonly TextureSampler StoneTile;
    public readonly TextureSampler CobblestoneTile;
    public readonly TextureSampler IronTile;
    public readonly TextureSampler OsmiumTile;
    public readonly TextureSampler BedrockTile;
    public readonly TextureSampler AcidTile;
    public readonly TextureSampler BaseTile;
    public readonly TextureSampler EnemyRobotTile;
    public readonly TextureSampler PlayerRobotTile;

    public Textures(GameApplication app)
    {
        TextureSampler Get(string path) => new(app.Resources.AssetManager.GetView(App.Asset(path)), app.Device.LinearSampler);

        DirtTile = Get("Images.Tiles.Dirt.png");
        StoneTile = Get("Images.Tiles.Stone.png");
        CobblestoneTile = Get("Images.Tiles.Cobblestone.png");
        IronTile = Get("Images.Tiles.Iron.png");
        OsmiumTile = Get("Images.Tiles.Osmium.png");
        BedrockTile = Get("Images.Tiles.Bedrock.png");
        AcidTile = Get("Images.Tiles.Acid.png");
        BaseTile = Get("Images.Tiles.Base.png");
        EnemyRobotTile = Get("Images.Tiles.Enemy.png");
        PlayerRobotTile = Get("Images.Tiles.Player.png");
    }
}