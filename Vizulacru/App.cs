using System.Text.Json;
using Common;
using Daleks;
using GameFramework;
using GameFramework.Assets;
using GameFramework.ImGui;
using GameFramework.Utilities.Extensions;
using ImGuiNET;
using Microsoft.Extensions.DependencyInjection;
using Veldrid;

namespace Vizulacru;

internal sealed class ConfigStore
{
    private const string Path = "botConfigs.json";

    public Dictionary<string, BotConfig> Map { get; set; } = new();

    public void Save()
    {
        File.WriteAllText(Path, JsonSerializer.Serialize(this));
    }

    public static ConfigStore Load()
    {
        if (!File.Exists(Path))
        {
            return new ConfigStore();
        }

        return JsonSerializer.Deserialize<ConfigStore>(File.ReadAllText(Path)) ?? throw new Exception($"Failed to deserialize {Path}");
    }
}

internal sealed class App : GameApplication
{
    private readonly IServiceProvider _serviceProvider;
    private int _selectedId;

    public int SelectedId => _selectedId;

    private readonly ConfigStore _configs;
    private string _selectedConfig;

    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        Device.SyncToVerticalBlank = true;
        Window.Title = "hublou";
        ClearColor = RgbaFloat.Black;

        _configs = ConfigStore.Load();

        if (_configs.Map.Count == 0)
        {
            _selectedConfig = "default";
            _configs.Map.Add(_selectedConfig, new BotConfig());
        }
        else
        {
            _selectedConfig = _configs.Map.Keys.First();
        }

        LoadConfig(BotConfig.Default);
    }

    protected override IServiceProvider BuildLayerServiceProvider(ServiceCollection registeredServices)
    {
        return _serviceProvider;
    }

    protected override void Initialize()
    {
        Layers.ConstructLayer<ImGuiLayer>(imGui =>
        {
            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            ImGuiStyles.Dark();
            ImGui.LoadIniSettingsFromDisk("imgui_vizulacru.ini");
            imGui.Submit += ImGuiOnSubmit;
        });
    }

    private string _typedConfigName = "";

    private float _exploreClosestP;
    private float _exploreClosestB;
    private float _exploreClosestBaseP;
    private float _exploreClosestBaseB;
    private Dictionary<TileType, float> _costs = new();
    private float _diagonalPenalty;
    private List<UpgradeType> _upgrades = new(0);
    private float _playerOverrideCost;
    private int _reserveOsmium;
    private int _roundsMargin;

    private void LoadConfig(IBotConfig cfg)
    {
        _exploreClosestP = cfg.ExploreCostMultipliers[Bot.ExploreMode.Closest].Player;
        _exploreClosestB = cfg.ExploreCostMultipliers[Bot.ExploreMode.Closest].Base;
        _exploreClosestBaseP = cfg.ExploreCostMultipliers[Bot.ExploreMode.ClosestBase].Player;
        _exploreClosestBaseB = cfg.ExploreCostMultipliers[Bot.ExploreMode.ClosestBase].Base;
        _costs = new Dictionary<TileType, float>(cfg.CostMap);
        _diagonalPenalty = cfg.DiagonalPenalty;
        _upgrades = cfg.UpgradeList.ToList();
        _playerOverrideCost = cfg.PlayerOverrideCost;
        _reserveOsmium = cfg.ReserveOsmium;
        _roundsMargin = cfg.RoundsMargin;
    }

    private int _gameRounds = 150;

    private void ImGuiOnSubmit(ImGuiRenderer obj)
    {
        if (Layers.FrontToBack.Any(t => t is WorldLayer))
        {
            return;
        }

        const string id = "App";

        if (ImGuiExt.Begin("Config Editor", id))
        {
            ImGui.Text("Config Editor");
            ImGui.InputText("Name", ref _typedConfigName, 64);

            ImGui.Text("Explore costs");
            {
                ImGui.Indent();

                ImGui.InputFloat("Closest - Player", ref _exploreClosestP);
                ImGui.InputFloat("Closest - Base", ref _exploreClosestB);
                ImGui.InputFloat("Closest Base - Player", ref _exploreClosestBaseP);
                ImGui.InputFloat("Closest Base - Base", ref _exploreClosestBaseB);

                ImGui.Unindent();
            }

            ImGui.Text("Tile costs");
            {
                ImGui.Indent();

                var keys = _costs.Keys.ToArray();

                Array.Sort(keys);

                foreach (var tileType in keys)
                {
                    var v = _costs[tileType];
                    ImGui.InputFloat(tileType.ToString(), ref v);
                    _costs[tileType] = v;
                }

                ImGui.Unindent();
            }

            ImGui.InputFloat("Diagonal penalty", ref _diagonalPenalty);

            ImGui.Text("Upgrade list");
            {
                ImGui.Indent();

                foreach (var t in _upgrades)
                {
                    ImGui.Text($"{t}");
                }

                if (_upgrades.Count > 0)
                {
                    if (ImGui.Button("Pop"))
                    {
                        _upgrades.RemoveAt(_upgrades.Count - 1);
                    }
                }


                void PushButton(UpgradeType type)
                {
                    if (_upgrades.Count(t => t == type) < 2)
                    {
                        if (ImGui.Button(type.ToString()))
                        {
                            _upgrades.Add(type);
                        }
                    }
                }

                ImGui.Indent();

                if (!_upgrades.Contains(UpgradeType.Antenna))
                {
                    if (ImGui.Button("Antenna"))
                    {
                        _upgrades.Add(UpgradeType.Antenna);
                    }
                }

                PushButton(UpgradeType.Sight);
                PushButton(UpgradeType.Drill);
                PushButton(UpgradeType.Movement);
                PushButton(UpgradeType.Attack);

                ImGui.Unindent();
                ImGui.Unindent();
            }

            ImGui.InputFloat("Player override cost", ref _playerOverrideCost);
            ImGui.InputInt("Reserve osmium", ref _reserveOsmium);
            ImGui.InputInt("Rounds margin", ref _roundsMargin);

            if (!string.IsNullOrEmpty(_typedConfigName))
            {
                if (ImGui.Button("Save"))
                {
                    _configs.Map.AddOrUpdate(_typedConfigName, new BotConfig
                    {
                        ExploreCostMultipliers = new Dictionary<Bot.ExploreMode, (float Player, float Base)>
                        {
                            { Bot.ExploreMode.Closest, (_exploreClosestP, _exploreClosestB) },
                            { Bot.ExploreMode.ClosestBase, (_exploreClosestBaseP, _exploreClosestBaseB) }
                        },
                        CostMap = new Dictionary<TileType, float>(_costs),
                        DiagonalPenalty = _diagonalPenalty,
                        UpgradeList = _upgrades.ToArray(),
                        PlayerOverrideCost = _playerOverrideCost,
                        ReserveOsmium = _reserveOsmium,
                        RoundsMargin = _roundsMargin
                    });

                    _configs.Save();
                }
            }
        }

        ImGui.End();

        if (ImGuiExt.Begin("Session", id))
        {
            ImGuiExt.StringComboBox(_configs.Map.Keys.ToArray(), ref _selectedConfig, "Config");

            if (ImGui.Button("Edit"))
            {
                LoadConfig(_configs.Map[_selectedConfig]);
            }

            ImGui.Separator();

            ImGui.Text("Start new game");
            {
                ImGui.Indent();
                ImGui.SliderInt("ID", ref _selectedId, 0, 5);

                ImGui.InputInt("Rounds", ref _gameRounds);

                if (ImGui.Button($"Start {_selectedConfig}"))
                {
                    Layers.AddLayer(
                        ActivatorUtilities.CreateInstance<WorldLayer>(
                            _serviceProvider,
                            _configs.Map[_selectedConfig],
                            new GameOptions
                            {
                                Rounds = _gameRounds
                            }
                        )
                    );
                }

                ImGui.Unindent();
            }
           
        }

        ImGui.End();
    }

    public static EmbeddedResourceKey Asset(string name)
    {
        return new EmbeddedResourceKey(typeof(App).Assembly, $"Vizulacru.Assets.{name}");
    }

    protected override void Destroy()
    {
        ImGui.SaveIniSettingsToDisk("imgui_vizulacru.ini");

        base.Destroy();
    }
}