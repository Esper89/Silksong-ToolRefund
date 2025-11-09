using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace ToolRefund;

[BepInAutoPlugin]
sealed partial class Mod : BaseUnityPlugin
{
    void Awake()
    {
        Mod._instance = this;
        this._config = new(base.Config);
        new Harmony(Mod.Id).PatchAll();
    }

    static Mod? _instance = null;
    static Mod Instance => Mod._instance ? Mod._instance :
        throw new NullReferenceException($"{nameof(Mod)} accessed before {nameof(Awake)}");

    ConfigEntries? _config = null;
    new ConfigEntries Config => this._config!;

    sealed class ConfigEntries(ConfigFile file)
    {
        internal bool Enabled => this._enabled.Value;
        ConfigEntry<bool> _enabled = file.Bind(
            "General", "Enabled", true,
            "Enable tool refund on death"
        );
    }

    void LogDebug(object msg) => this.Logger.LogDebug(msg);
    void LogInfo(object msg) => this.Logger.LogInfo(msg);
    void LogMessage(object msg) => this.Logger.LogMessage(msg);
    void LogWarning(object msg) => this.Logger.LogWarning(msg);
    void LogError(object msg) => this.Logger.LogError(msg);
    void LogFatal(object msg) => this.Logger.LogFatal(msg);

    bool playerDying = false;
    ToolRestoreState restoreState = new();
    ToolResourcesTotal resourcesTotal = new();

    void CaptureRestoreState()
    {
        if (!this.playerDying)
        {
            this.restoreState.Capture();
            this.LogDebug($"captured restore state: {this.restoreState}");
            this.resourcesTotal.Clear();
        }
    }

    void CaptureResourcesSpent(ToolResourcesTotal spent)
    {
        this.resourcesTotal.Add(spent);
        this.LogDebug($"captured resources spent: {spent}");
    }

    void RestoreRosariesEarly()
    {
        if (this.Config.Enabled)
        {
            this.resourcesTotal.RestoreRosaries();
            this.LogDebug($"restored tool rosaries: {this.resourcesTotal.Rosaries}");
            this.resourcesTotal.ClearRosaries();
        }
    }

    void RestoreTools()
    {
        if (this.Config.Enabled)
        {
            this.restoreState.Restore();
            this.LogDebug($"restored tool state: {this.restoreState}");

            this.resourcesTotal.Restore();
            this.LogDebug($"restored tool resources: {this.resourcesTotal}");
            this.resourcesTotal.Clear();
        }
    }

    [HarmonyPatch(
        typeof(GameManager), nameof(GameManager.SaveGame),
        [typeof(int), typeof(Action<bool>), typeof(bool), typeof(AutoSaveName)]
    )]
    static class OnSave
    {
        static void Postfix() => Mod.Instance!.CaptureRestoreState();
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.LoadGame))]
    static class OnLoad
    {
        static void Postfix() => Mod.Instance!.CaptureRestoreState();
    }

    [HarmonyPatch(typeof(HeroController), nameof(HeroController.Die))]
    static class OnDeath
    {
        static void Prefix()
        {
            Mod.Instance!.playerDying = true;
            Mod.Instance!.RestoreRosariesEarly();
        }
    }

    [HarmonyPatch(typeof(HeroController), nameof(HeroController.Respawn))]
    static class OnRespawn
    {
        static void Postfix()
        {
            Mod.Instance!.RestoreTools();
            Mod.Instance!.playerDying = false;
        }
    }

    [HarmonyPatch(typeof(ToolItemManager), nameof(ToolItemManager.TryReplenishTools))]
    static class OnToolReplenish
    {
        static void Prefix(ToolItemManager.ReplenishMethod method, ref ToolResourcesTotal __state)
        {
            if (method == ToolItemManager.ReplenishMethod.QuickCraft)
            {
                __state = new();
                __state.Capture();
            }
        }

        static void Postfix(ToolItemManager.ReplenishMethod method, ToolResourcesTotal __state)
        {
            switch (method)
            {
                case ToolItemManager.ReplenishMethod.Bench:
                case ToolItemManager.ReplenishMethod.BenchSilent:
                {
                    Mod.Instance!.CaptureRestoreState();
                    break;
                }

                case ToolItemManager.ReplenishMethod.QuickCraft:
                {
                    var laterState = new ToolResourcesTotal();
                    laterState.Capture();
                    __state.Subtract(laterState);
                    Mod.Instance!.CaptureResourcesSpent(__state);
                    break;
                }
            }
        }
    }
}

class ToolRestoreState
{
    Dictionary<ToolItem, int> toolAmounts = new();

    public void Capture()
    {
        this.Clear();

        if (!PlayerData.HasInstance) return;
        var pd = PlayerData.instance;

        foreach (var tool in ToolItemManager.GetAllTools())
        {
            if (tool == null) continue;
            var amount = pd.GetToolData(tool.name).AmountLeft;
            if (amount != 0) this.toolAmounts[tool] = amount;
        }
    }

    public void Restore()
    {
        if (!PlayerData.HasInstance) return;
        var pd = PlayerData.instance;

        foreach (var (tool, restoreAmount) in this.toolAmounts)
        {
            if (tool == null) continue;
            var maxAmount = ToolItemManager.GetToolStorageAmount(tool);
            var data = pd.GetToolData(tool.name);
            var amount = data.AmountLeft;
            var newAmount = Math.Clamp(restoreAmount, amount, maxAmount);
            if (newAmount != amount)
            {
                data.AmountLeft = newAmount;
                pd.SetToolData(tool.name, data);
            }
        }

        ToolItemManager.ReportAllBoundAttackToolsUpdated();
        ToolItemManager.SendEquippedChangedEvent(true);
    }

    public void Clear() => this.toolAmounts.Clear();

    public override string ToString()
    {
        var values = this.toolAmounts.Select(kvp => kvp.Key.name + ": " + kvp.Value.ToString());
        return "{ " + string.Join(", ", values) + " }";
    }
}

class ToolResourcesTotal
{
    int rosaries = 0;
    int shellShards = 0;
    Dictionary<ToolItemStatesLiquid, int> liquidRefills = new();

    public void Capture()
    {
        this.Clear();

        if (!PlayerData.HasInstance) return;
        var pd = PlayerData.instance;

        var cq = CurrencyManager.currencyQueue;

        this.rosaries = pd.geo + cq[(int)CurrencyType.Money].amount;
        this.shellShards = pd.ShellShards + cq[(int)CurrencyType.Shard].amount;
        foreach (var tool in ToolItemManager.GetAllTools())
        {
            if (tool is ToolItemStatesLiquid liquidTool)
            {
                var refills = liquidTool.LiquidSavedData.RefillsLeft;
                this.liquidRefills[liquidTool] = refills;
            }
        }
    }

    public void Subtract(ToolResourcesTotal later)
    {
        this.rosaries -= later.rosaries;
        this.shellShards -= later.shellShards;
        foreach (var (tool, refills) in later.liquidRefills)
        {
            if (!this.liquidRefills.ContainsKey(tool)) this.liquidRefills[tool] = 0;
            this.liquidRefills[tool] -= refills;
        }
    }

    public void Add(ToolResourcesTotal other)
    {
        this.rosaries += other.rosaries;
        this.shellShards += other.shellShards;
        foreach (var (tool, refills) in other.liquidRefills)
        {
            if (!this.liquidRefills.ContainsKey(tool)) this.liquidRefills[tool] = 0;
            this.liquidRefills[tool] += refills;
        }
    }

    public int Rosaries => this.rosaries;

    public void RestoreRosaries()
    {
        if (!PlayerData.HasInstance) return;
        var pd = PlayerData.instance;

        if (this.rosaries > 0)
        {
            var rosaries = pd.geo + this.rosaries;
            var max = GlobalSettings.Gameplay.GetCurrencyCap(CurrencyType.Money);
            pd.geo = Math.Clamp(rosaries, 0, max);
        }
    }

    public void ClearRosaries() => this.rosaries = 0;

    public void Restore()
    {
        if (!PlayerData.HasInstance) return;
        var pd = PlayerData.instance;

        if (this.rosaries > 0)
        {
            var rosaries = pd.geo + this.rosaries;
            var max = GlobalSettings.Gameplay.GetCurrencyCap(CurrencyType.Money);
            pd.geo = Math.Clamp(rosaries, 0, max);
        }

        if (this.shellShards > 0)
        {
            var shards = pd.ShellShards + this.shellShards;
            var max = GlobalSettings.Gameplay.GetCurrencyCap(CurrencyType.Shard);
            pd.ShellShards = Math.Clamp(shards, 0, max);
        }

        foreach (var (tool, refills) in this.liquidRefills)
        {
            if (refills > 0)
            {
                var data = tool.LiquidSavedData;
                data.RefillsLeft = Math.Clamp(data.RefillsLeft + refills, 0, tool.RefillsMax);
                tool.LiquidSavedData = data;
            }
        }
    }

    public void Clear()
    {
        this.rosaries = 0;
        this.shellShards = 0;
        this.liquidRefills.Clear();
    }

    public override string ToString()
    {
        var values = this.liquidRefills.Select(kvp => kvp.Key.name + ": " + kvp.Value.ToString());

        if (this.shellShards > 0) values = values.Prepend($"Shell Shards: {this.shellShards}");
        if (this.rosaries > 0 ) values = values.Prepend($"Rosaries: {this.rosaries}");

        return "{ " + string.Join(", ", values) + " }";
    }
}
