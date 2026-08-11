using RightAgent.Core;

namespace RightAgent.App.ViewModels;

public sealed class AgentItemViewModel : BindableBase
{
    private readonly Localization localization;
    private string name;
    private bool enabled;
    private int sort;
    private string iconPath;
    private string actionType;
    private string actionValue;

    public AgentItemViewModel(AgentDefinition definition, Localization localization)
    {
        this.localization = localization;
        Id = definition.Id;
        name = definition.Name;
        enabled = definition.Enabled;
        sort = definition.Sort;
        iconPath = definition.IconPath;
        actionType = definition.Action.Type;
        actionValue = definition.Action.Value;
        RefreshLanguage();
    }

    public string Id { get; }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public bool Enabled
    {
        get => enabled;
        set => SetProperty(ref enabled, value);
    }

    public int Sort
    {
        get => sort;
        set => SetProperty(ref sort, value);
    }

    public string IconPath
    {
        get => iconPath;
        set
        {
            if (SetProperty(ref iconPath, value))
            {
                OnPropertyChanged(nameof(IconDisplayPath));
            }
        }
    }

    public string IconDisplayPath
    {
        get
        {
            if (IconPath.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                var relative = IconPath["local:".Length..].Replace('\\', '/');
                var escaped = string.Join('/', relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
                return "ms-appdata:///local/" + escaped;
            }

            var key = IconPath.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase)
                ? IconPath["builtin:".Length..].ToLowerInvariant()
                : "rightagent";
            if (key is not ("claude" or "codex" or "kimi" or "rightagent"))
            {
                key = "rightagent";
            }
            return $"ms-appx:///Assets/Agents/{key}.svg";
        }
    }

    public string ActionType
    {
        get => actionType;
        set => SetProperty(ref actionType, value == SettingsContract.Url ? SettingsContract.Url : SettingsContract.TerminalCommand);
    }

    public string ActionValue
    {
        get => actionValue;
        set => SetProperty(ref actionValue, value);
    }

    public IReadOnlyList<OptionItem> ActionTypeOptions { get; private set; } = [];

    public string NameLabel => localization["Name"];
    public string ActionTypeLabel => localization["ActionType"];
    public string ActionValueLabel => localization["ActionValue"];
    public string EnabledLabel => localization["Enabled"];
    public string DisabledLabel => localization["Disabled"];
    public string ChooseIconLabel => localization["ChooseIcon"];
    public string MoveUpLabel => localization["MoveUp"];
    public string MoveDownLabel => localization["MoveDown"];
    public string DeleteLabel => localization["Delete"];

    public AgentDefinition ToDefinition() => new()
    {
        Id = Id,
        Name = Name,
        Enabled = Enabled,
        Sort = Sort,
        IconPath = IconPath,
        Action = new AgentAction
        {
            Type = ActionType,
            Value = ActionValue
        }
    };

    public void RefreshLanguage()
    {
        ActionTypeOptions =
        [
            new OptionItem(SettingsContract.TerminalCommand, localization["TerminalCommand"]),
            new OptionItem(SettingsContract.Url, localization["Url"])
        ];
        OnPropertyChanged(nameof(ActionTypeOptions));
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(ActionTypeLabel));
        OnPropertyChanged(nameof(ActionValueLabel));
        OnPropertyChanged(nameof(EnabledLabel));
        OnPropertyChanged(nameof(DisabledLabel));
        OnPropertyChanged(nameof(ChooseIconLabel));
        OnPropertyChanged(nameof(MoveUpLabel));
        OnPropertyChanged(nameof(MoveDownLabel));
        OnPropertyChanged(nameof(DeleteLabel));
    }
}
