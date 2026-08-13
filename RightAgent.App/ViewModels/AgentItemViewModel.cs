using System.Globalization;
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
    private bool isExpanded;
    private string? nameError;
    private string? actionError;

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
        set
        {
            if (SetProperty(ref name, value))
            {
                RefreshValidation();
                NotifyAutomationNames();
            }
        }
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            if (SetProperty(ref enabled, value))
            {
                RefreshValidation();
            }
        }
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
            if (key is not ("claude" or "codex" or "kimi" or "grok" or "opencode" or "rightagent"))
            {
                key = "rightagent";
            }
            return $"ms-appx:///Assets/Agents/{key}.svg";
        }
    }

    public string ActionType
    {
        get => actionType;
        set
        {
            if (SetProperty(ref actionType, value == SettingsContract.Url ? SettingsContract.Url : SettingsContract.TerminalCommand))
            {
                RefreshValidation();
            }
        }
    }

    public string ActionValue
    {
        get => actionValue;
        set
        {
            if (SetProperty(ref actionValue, value))
            {
                RefreshValidation();
            }
        }
    }

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public string? NameError
    {
        get => nameError;
        private set
        {
            if (SetProperty(ref nameError, value))
            {
                OnPropertyChanged(nameof(HasNameError));
            }
        }
    }

    public bool HasNameError => NameError is not null;

    public string? ActionError
    {
        get => actionError;
        private set
        {
            if (SetProperty(ref actionError, value))
            {
                OnPropertyChanged(nameof(HasActionError));
            }
        }
    }

    public bool HasActionError => ActionError is not null;

    public IReadOnlyList<OptionItem> ActionTypeOptions { get; } =
    [
        new OptionItem(SettingsContract.TerminalCommand, string.Empty),
        new OptionItem(SettingsContract.Url, string.Empty)
    ];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? localization["UnnamedAgent"] : Name;
    public string MenuTitle => Format("OpenWithAgent");

    public string NameLabel => localization["Name"];
    public string ActionTypeLabel => localization["ActionType"];
    public string ActionValueLabel => localization["ActionValue"];
    public string ChooseIconLabel => localization["ChooseIcon"];
    public string MoveUpLabel => localization["MoveUp"];
    public string MoveDownLabel => localization["MoveDown"];
    public string DeleteLabel => localization["Delete"];

    public string EnabledAutomationName => Format("EnableFor");
    public string MoveUpAutomationName => Format("MoveUpFor");
    public string MoveDownAutomationName => Format("MoveDownFor");
    public string DeleteAutomationName => Format("DeleteFor");
    public string ChooseIconAutomationName => Format("ChooseIconFor");
    public string IconPreviewAutomationName => Format("IconFor");

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
        ActionTypeOptions[0].UpdateLabel(localization["TerminalCommand"]);
        ActionTypeOptions[1].UpdateLabel(localization["Url"]);
        OnPropertyChanged(nameof(NameLabel));
        OnPropertyChanged(nameof(ActionTypeLabel));
        OnPropertyChanged(nameof(ActionValueLabel));
        OnPropertyChanged(nameof(ChooseIconLabel));
        OnPropertyChanged(nameof(MoveUpLabel));
        OnPropertyChanged(nameof(MoveDownLabel));
        OnPropertyChanged(nameof(DeleteLabel));
        OnPropertyChanged(nameof(MenuTitle));
        RefreshValidation();
        NotifyAutomationNames();
    }

    private void RefreshValidation()
    {
        NameError = string.IsNullOrWhiteSpace(Name) ? localization["ErrorNameRequired"] : null;
        if (!Enabled)
        {
            ActionError = null;
        }
        else if (string.IsNullOrWhiteSpace(ActionValue))
        {
            ActionError = localization["ErrorActionRequired"];
        }
        else
        {
            ActionError = SettingsValidator.IsActionValid(ActionType, ActionValue) ? null : localization["ErrorUrlInvalid"];
        }
    }

    private void NotifyAutomationNames()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(MenuTitle));
        OnPropertyChanged(nameof(EnabledAutomationName));
        OnPropertyChanged(nameof(MoveUpAutomationName));
        OnPropertyChanged(nameof(MoveDownAutomationName));
        OnPropertyChanged(nameof(DeleteAutomationName));
        OnPropertyChanged(nameof(ChooseIconAutomationName));
        OnPropertyChanged(nameof(IconPreviewAutomationName));
    }

    private string Format(string key) => string.Format(CultureInfo.CurrentCulture, localization[key], DisplayName);
}
