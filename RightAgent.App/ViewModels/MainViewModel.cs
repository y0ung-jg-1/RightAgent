using System.Collections.ObjectModel;
using System.ComponentModel;
using RightAgent.Core;

namespace RightAgent.App.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private readonly SettingsStore store;
    private readonly Localization localization = new();
    private string language = SettingsContract.SystemLanguage;
    private string menuMode = SettingsContract.GroupedMenu;
    private string? directAgentId;
    private string? terminalProfile;
    private bool isLoaded;

    public MainViewModel(string localStateDirectory)
    {
        store = new SettingsStore(localStateDirectory);
        RefreshLocalization();
    }

    public ObservableCollection<AgentItemViewModel> Agents { get; } = [];

    public IReadOnlyList<OptionItem> LanguageOptions { get; private set; } = [];

    public IReadOnlyList<OptionItem> MenuModeOptions { get; private set; } = [];

    public bool IsLoaded
    {
        get => isLoaded;
        private set => SetProperty(ref isLoaded, value);
    }

    public string Language
    {
        get => language;
        set
        {
            var normalized = value is SettingsContract.ChineseLanguage or SettingsContract.EnglishLanguage
                ? value
                : SettingsContract.SystemLanguage;
            if (SetProperty(ref language, normalized))
            {
                localization.ConfiguredLanguage = normalized;
                RefreshLocalization();
            }
        }
    }

    public string MenuMode
    {
        get => menuMode;
        set
        {
            var normalized = value == SettingsContract.DirectMenu ? SettingsContract.DirectMenu : SettingsContract.GroupedMenu;
            if (SetProperty(ref menuMode, normalized))
            {
                OnPropertyChanged(nameof(IsDirectMode));
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    public bool IsDirectMode => MenuMode == SettingsContract.DirectMenu;

    public string? DirectAgentId
    {
        get => directAgentId;
        set
        {
            if (SetProperty(ref directAgentId, value))
            {
                OnPropertyChanged(nameof(Preview));
            }
        }
    }

    public string? TerminalProfile
    {
        get => terminalProfile;
        set => SetProperty(ref terminalProfile, value);
    }

    public bool IsEmpty => Agents.Count == 0;

    public string Preview
    {
        get
        {
            var enabled = Agents.Where(agent => agent.Enabled).OrderBy(agent => agent.Sort).ToList();
            if (MenuMode == SettingsContract.DirectMenu)
            {
                var selected = enabled.FirstOrDefault(agent => agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase))
                               ?? enabled.FirstOrDefault();
                return selected is null
                    ? "—"
                    : localization.IsChinese ? $"使用 {selected.Name} 打开" : $"Open with {selected.Name}";
            }

            if (enabled.Count == 0)
            {
                return localization.IsChinese ? "使用 RightAgent 打开  >\n    （没有已启用的 Agent）" : "Open with RightAgent  >\n    (No enabled agents)";
            }
            var header = localization.IsChinese ? "使用 RightAgent 打开  >" : "Open with RightAgent  >";
            return header + "\n" + string.Join("\n", enabled.Select(agent => "    " + agent.Name));
        }
    }

    public string WindowTitle => localization["WindowTitle"];
    public string HeaderTitle => localization["Title"];
    public string Subtitle => localization["Subtitle"];
    public string SaveLabel => localization["Save"];
    public string SavedMessage => localization["Saved"];
    public string SaveFailedLabel => localization["SaveFailed"];
    public string MenuSectionLabel => localization["MenuSection"];
    public string MenuDescription => localization["MenuDescription"];
    public string MenuModeLabel => localization["MenuMode"];
    public string DirectAgentLabel => localization["DirectAgent"];
    public string TerminalProfileLabel => localization["TerminalProfile"];
    public string TerminalProfileHint => localization["TerminalProfileHint"];
    public string PreviewLabel => localization["Preview"];
    public string AgentsSectionLabel => localization["AgentsSection"];
    public string AgentsDescription => localization["AgentsDescription"];
    public string AddAgentLabel => localization["AddAgent"];
    public string NoAgentsLabel => localization["NoAgents"];
    public string GeneralSectionLabel => localization["GeneralSection"];
    public string LanguageLabel => localization["Language"];
    public string DeleteTitle => localization["DeleteTitle"];
    public string DeleteBody => localization["DeleteBody"];
    public string DeleteLabel => localization["Delete"];
    public string CancelLabel => localization["Cancel"];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await store.LoadAsync(cancellationToken);
        language = settings.Language;
        localization.ConfiguredLanguage = language;
        menuMode = settings.MenuMode;
        directAgentId = settings.DirectAgentId;
        terminalProfile = settings.TerminalProfile;

        foreach (var existing in Agents)
        {
            existing.PropertyChanged -= AgentPropertyChanged;
        }
        Agents.Clear();
        foreach (var definition in settings.Agents.OrderBy(agent => agent.Sort))
        {
            Attach(new AgentItemViewModel(definition, localization));
        }

        RefreshSort();
        RefreshLocalization();
        IsLoaded = true;
        NotifyState();
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Agents.Any(agent => string.IsNullOrWhiteSpace(agent.Name)))
        {
            errors.Add(localization["ValidationName"]);
        }
        if (Agents.Any(agent => agent.Enabled && !SettingsValidator.IsActionValid(agent.ActionType, agent.ActionValue)))
        {
            errors.Add(localization["ValidationAction"]);
        }
        if (MenuMode == SettingsContract.DirectMenu
            && !Agents.Any(agent => agent.Enabled && agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(localization["ValidationDirect"]);
        }
        return errors;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        RefreshSort();
        var settings = new RightAgentSettings
        {
            Language = Language,
            MenuMode = MenuMode,
            DirectAgentId = DirectAgentId,
            TerminalProfile = TerminalProfile,
            Agents = Agents.Select(agent => agent.ToDefinition()).ToList()
        };
        var normalized = SettingsValidator.Normalize(settings);
        await store.SaveAsync(normalized, cancellationToken);
        DirectAgentId = normalized.DirectAgentId;
    }

    public void AddAgent()
    {
        var id = "agent-" + Guid.NewGuid().ToString("N")[..8];
        Attach(new AgentItemViewModel(new AgentDefinition
        {
            Id = id,
            Name = localization["NewAgent"],
            Enabled = false,
            Sort = Agents.Count,
            IconPath = "builtin:rightagent",
            Action = new AgentAction { Type = SettingsContract.TerminalCommand, Value = string.Empty }
        }, localization));
        NotifyState();
    }

    public AgentItemViewModel? FindAgent(string? id) =>
        id is null ? null : Agents.FirstOrDefault(agent => agent.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void RemoveAgent(AgentItemViewModel agent)
    {
        agent.PropertyChanged -= AgentPropertyChanged;
        Agents.Remove(agent);
        if (agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase))
        {
            DirectAgentId = Agents.FirstOrDefault(candidate => candidate.Enabled)?.Id;
        }
        RefreshSort();
        NotifyState();
    }

    public void MoveAgent(AgentItemViewModel agent, int offset)
    {
        var current = Agents.IndexOf(agent);
        var target = current + offset;
        if (current < 0 || target < 0 || target >= Agents.Count)
        {
            return;
        }
        Agents.Move(current, target);
        RefreshSort();
        OnPropertyChanged(nameof(Preview));
    }

    public void SetAgentIcon(AgentItemViewModel agent, string relativePath)
    {
        agent.IconPath = "local:" + relativePath.Replace('\\', '/');
    }

    private void Attach(AgentItemViewModel agent)
    {
        agent.PropertyChanged += AgentPropertyChanged;
        Agents.Add(agent);
    }

    private void AgentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(AgentItemViewModel.Name) or nameof(AgentItemViewModel.Enabled))
        {
            OnPropertyChanged(nameof(Preview));
            OnPropertyChanged(nameof(Agents));
        }
    }

    private void RefreshSort()
    {
        for (var index = 0; index < Agents.Count; ++index)
        {
            Agents[index].Sort = index;
        }
    }

    private void RefreshLocalization()
    {
        localization.ConfiguredLanguage = language;
        LanguageOptions =
        [
            new OptionItem(SettingsContract.SystemLanguage, localization["SystemLanguage"]),
            new OptionItem(SettingsContract.ChineseLanguage, localization["Chinese"]),
            new OptionItem(SettingsContract.EnglishLanguage, localization["English"])
        ];
        MenuModeOptions =
        [
            new OptionItem(SettingsContract.GroupedMenu, localization["Grouped"]),
            new OptionItem(SettingsContract.DirectMenu, localization["Direct"])
        ];
        foreach (var agent in Agents)
        {
            agent.RefreshLanguage();
        }
        NotifyLocalizedProperties();
    }

    private void NotifyLocalizedProperties()
    {
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(MenuModeOptions));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(SavedMessage));
        OnPropertyChanged(nameof(SaveFailedLabel));
        OnPropertyChanged(nameof(MenuSectionLabel));
        OnPropertyChanged(nameof(MenuDescription));
        OnPropertyChanged(nameof(MenuModeLabel));
        OnPropertyChanged(nameof(DirectAgentLabel));
        OnPropertyChanged(nameof(TerminalProfileLabel));
        OnPropertyChanged(nameof(TerminalProfileHint));
        OnPropertyChanged(nameof(PreviewLabel));
        OnPropertyChanged(nameof(AgentsSectionLabel));
        OnPropertyChanged(nameof(AgentsDescription));
        OnPropertyChanged(nameof(AddAgentLabel));
        OnPropertyChanged(nameof(NoAgentsLabel));
        OnPropertyChanged(nameof(GeneralSectionLabel));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(DeleteTitle));
        OnPropertyChanged(nameof(DeleteBody));
        OnPropertyChanged(nameof(DeleteLabel));
        OnPropertyChanged(nameof(CancelLabel));
        OnPropertyChanged(nameof(Preview));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Agents));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(IsDirectMode));
    }
}
