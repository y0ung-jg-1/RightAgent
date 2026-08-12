using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using RightAgent.Core;

namespace RightAgent.App.ViewModels;

public sealed class MainViewModel : BindableBase
{
    private const string AppIconPath = "ms-appx:///Assets/Agents/rightagent.svg";

    private readonly SettingsStore store;
    private readonly Localization localization = new();
    private string language = SettingsContract.SystemLanguage;
    private string menuMode = SettingsContract.GroupedMenu;
    private string? directAgentId;
    private string? terminalProfile;
    private bool menuEnabled = true;
    private bool isLoaded;
    private string previewRootTitle = string.Empty;
    private string previewRootIconPath = AppIconPath;
    private bool previewIsGrouped = true;
    private bool previewHasEntries;
    private bool previewShowRootTitle = true;
    private bool previewShowRootHint;
    private string validationSummary = string.Empty;
    private bool hasValidationErrors;

    public MainViewModel(string localStateDirectory)
    {
        store = new SettingsStore(localStateDirectory);
        RefreshLocalization();
    }

    public ObservableCollection<AgentItemViewModel> Agents { get; } = [];

    public ObservableCollection<AgentItemViewModel> PreviewEntries { get; } = [];

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
            // The ComboBox pushes null while its ItemsSource is being swapped during a
            // language change; ignore it so the swap cannot re-enter RefreshLocalization.
            if (value is null)
            {
                return;
            }
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
            // The ComboBox pushes null while its ItemsSource is being swapped during a
            // language change; ignore it so the swap cannot reset the mode.
            if (value is null)
            {
                return;
            }
            var normalized = value == SettingsContract.DirectMenu ? SettingsContract.DirectMenu : SettingsContract.GroupedMenu;
            if (SetProperty(ref menuMode, normalized))
            {
                OnPropertyChanged(nameof(IsDirectMode));
                RefreshPreview();
                RefreshValidation();
            }
        }
    }

    public bool IsDirectMode => MenuMode == SettingsContract.DirectMenu;

    public bool MenuEnabled
    {
        get => menuEnabled;
        set
        {
            if (SetProperty(ref menuEnabled, value))
            {
                RefreshPreview();
                RefreshValidation();
            }
        }
    }

    public string? DirectAgentId
    {
        get => directAgentId;
        set
        {
            if (SetProperty(ref directAgentId, value))
            {
                RefreshPreview();
                RefreshValidation();
            }
        }
    }

    public string? TerminalProfile
    {
        get => terminalProfile;
        set => SetProperty(ref terminalProfile, value);
    }

    public bool IsEmpty => Agents.Count == 0;

    public string PreviewRootTitle
    {
        get => previewRootTitle;
        private set => SetProperty(ref previewRootTitle, value);
    }

    public string PreviewRootIconPath
    {
        get => previewRootIconPath;
        private set => SetProperty(ref previewRootIconPath, value);
    }

    public bool PreviewIsGrouped
    {
        get => previewIsGrouped;
        private set => SetProperty(ref previewIsGrouped, value);
    }

    public bool PreviewHasEntries
    {
        get => previewHasEntries;
        private set => SetProperty(ref previewHasEntries, value);
    }

    public bool PreviewShowRootTitle
    {
        get => previewShowRootTitle;
        private set => SetProperty(ref previewShowRootTitle, value);
    }

    public bool PreviewShowRootHint
    {
        get => previewShowRootHint;
        private set => SetProperty(ref previewShowRootHint, value);
    }

    public string PreviewEmptyLabel => localization["PreviewEmpty"];

    public string PreviewHintText => MenuEnabled ? localization["PreviewEmpty"] : localization["MenuOff"];

    public string ValidationSummary
    {
        get => validationSummary;
        private set => SetProperty(ref validationSummary, value);
    }

    public bool HasValidationErrors
    {
        get => hasValidationErrors;
        private set => SetProperty(ref hasValidationErrors, value);
    }

    public string WindowTitle => localization["WindowTitle"];
    public string HeaderTitle => localization["Title"];
    public string MasterSwitchLabel => localization["MasterSwitch"];
    public string SaveLabel => localization["Save"];
    public string SavedMessage => localization["Saved"];
    public string SaveFailedLabel => localization["SaveFailed"];
    public string MenuSectionLabel => localization["MenuSection"];
    public string MenuModeLabel => localization["MenuMode"];
    public string DirectAgentLabel => localization["DirectAgent"];
    public string TerminalProfileLabel => localization["TerminalProfile"];
    public string TerminalProfileHint => localization["TerminalProfileHint"];
    public string PreviewLabel => localization["Preview"];
    public string AgentsSectionLabel => localization["AgentsSection"];
    public string AddAgentLabel => localization["AddAgent"];
    public string NoAgentsLabel => localization["NoAgents"];
    public string GeneralSectionLabel => localization["GeneralSection"];
    public string LanguageLabel => localization["Language"];
    public string DeleteTitle => localization["DeleteTitle"];
    public string DeleteBody => localization["DeleteBody"];
    public string DeleteLabel => localization["Delete"];
    public string CancelLabel => localization["Cancel"];
    public string ValidationTitle => localization["ValidationTitle"];

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await store.LoadAsync(cancellationToken);
        language = settings.Language;
        localization.ConfiguredLanguage = language;
        menuMode = settings.MenuMode;
        directAgentId = settings.DirectAgentId;
        terminalProfile = settings.TerminalProfile;
        menuEnabled = settings.MenuEnabled;

        foreach (var existing in Agents)
        {
            existing.PropertyChanged -= AgentPropertyChanged;
        }
        Agents.Clear();
        foreach (var definition in settings.Agents.OrderBy(agent => agent.Sort))
        {
            Attach(new AgentItemViewModel(definition, localization));
        }

        // Built-ins introduced after the user's settings file was written are merged in,
        // enabled only when their command is detected on this machine.
        foreach (var builtIn in SettingsDefaults.Create().Agents)
        {
            if (FindAgent(builtIn.Id) is null)
            {
                Attach(new AgentItemViewModel(builtIn, localization));
            }
        }

        RefreshSort();
        RefreshLocalization();
        IsLoaded = true;
        NotifyState();
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
            MenuEnabled = MenuEnabled,
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
        }, localization)
        {
            IsExpanded = true
        });
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
        RefreshPreview();
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
            OnPropertyChanged(nameof(Agents));
        }

        if (args.PropertyName is nameof(AgentItemViewModel.Name)
            or nameof(AgentItemViewModel.Enabled)
            or nameof(AgentItemViewModel.IconPath)
            or nameof(AgentItemViewModel.ActionType)
            or nameof(AgentItemViewModel.ActionValue)
            or nameof(AgentItemViewModel.HasNameError)
            or nameof(AgentItemViewModel.HasActionError))
        {
            RefreshPreview();
            RefreshValidation();
        }
    }

    private void RefreshSort()
    {
        for (var index = 0; index < Agents.Count; ++index)
        {
            Agents[index].Sort = index;
        }
    }

    private void RefreshPreview()
    {
        var enabled = Agents.Where(agent => agent.Enabled).OrderBy(agent => agent.Sort).ToList();
        PreviewEntries.Clear();
        foreach (var agent in enabled)
        {
            PreviewEntries.Add(agent);
        }

        PreviewHasEntries = enabled.Count > 0;
        PreviewIsGrouped = MenuEnabled && MenuMode == SettingsContract.GroupedMenu;
        PreviewShowRootTitle = MenuEnabled && (MenuMode == SettingsContract.GroupedMenu || enabled.Count > 0);
        PreviewShowRootHint = !PreviewShowRootTitle;
        if (!MenuEnabled)
        {
            PreviewRootTitle = string.Empty;
            PreviewRootIconPath = AppIconPath;
        }
        else if (PreviewIsGrouped)
        {
            PreviewRootTitle = localization["OpenWithRightAgent"];
            PreviewRootIconPath = AppIconPath;
        }
        else
        {
            var selected = enabled.FirstOrDefault(agent => agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase))
                           ?? enabled.FirstOrDefault();
            PreviewRootTitle = selected is null
                ? string.Empty
                : string.Format(CultureInfo.CurrentCulture, localization["OpenWithAgent"], selected.Name);
            PreviewRootIconPath = selected?.IconDisplayPath ?? AppIconPath;
        }
        OnPropertyChanged(nameof(PreviewHintText));
    }

    private void RefreshValidation()
    {
        if (!MenuEnabled)
        {
            // With the master switch off the menu never appears, so pending edits must not block saving.
            ValidationSummary = string.Empty;
            HasValidationErrors = false;
            return;
        }

        var lines = new List<string>();
        foreach (var agent in Agents)
        {
            var error = agent.NameError ?? (agent.Enabled ? agent.ActionError : null);
            if (error is not null)
            {
                lines.Add($"· {agent.DisplayName}: {error}");
            }
        }

        if (MenuMode == SettingsContract.DirectMenu
            && !Agents.Any(agent => agent.Enabled && agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add("· " + localization["ValidationDirect"]);
        }

        ValidationSummary = string.Join(Environment.NewLine, lines);
        HasValidationErrors = lines.Count > 0;
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
        RefreshPreview();
        RefreshValidation();
    }

    private void NotifyLocalizedProperties()
    {
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(MenuModeOptions));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(MasterSwitchLabel));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(SavedMessage));
        OnPropertyChanged(nameof(SaveFailedLabel));
        OnPropertyChanged(nameof(MenuSectionLabel));
        OnPropertyChanged(nameof(MenuModeLabel));
        OnPropertyChanged(nameof(DirectAgentLabel));
        OnPropertyChanged(nameof(TerminalProfileLabel));
        OnPropertyChanged(nameof(TerminalProfileHint));
        OnPropertyChanged(nameof(PreviewLabel));
        OnPropertyChanged(nameof(PreviewEmptyLabel));
        OnPropertyChanged(nameof(PreviewHintText));
        OnPropertyChanged(nameof(AgentsSectionLabel));
        OnPropertyChanged(nameof(AddAgentLabel));
        OnPropertyChanged(nameof(NoAgentsLabel));
        OnPropertyChanged(nameof(GeneralSectionLabel));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(DeleteTitle));
        OnPropertyChanged(nameof(DeleteBody));
        OnPropertyChanged(nameof(DeleteLabel));
        OnPropertyChanged(nameof(CancelLabel));
        OnPropertyChanged(nameof(ValidationTitle));
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Agents));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsDirectMode));
        RefreshPreview();
        RefreshValidation();
    }
}
