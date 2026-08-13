using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using RightAgent.App.Services;
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
    private string terminalShell = SettingsContract.AutomaticTerminalShell;
    private string? terminalProfile;
    private bool menuEnabled = true;
    private bool isLoaded;
    private string previewRootTitle = string.Empty;
    private string previewRootIconPath = AppIconPath;
    private bool previewIsGrouped = true;
    private bool previewHasEntries;
    private bool previewShowRootTitle = true;
    private bool previewShowRootHint;
    private bool previewShowsMultipleRoots;
    private string validationSummary = string.Empty;
    private bool hasValidationErrors;

    public MainViewModel(string localStateDirectory)
    {
        store = new SettingsStore(localStateDirectory);
        RefreshLocalization();
    }

    public ObservableCollection<AgentItemViewModel> Agents { get; } = [];

    public ObservableCollection<AgentItemViewModel> PreviewEntries { get; } = [];

    public IReadOnlyList<OptionItem> LanguageOptions { get; } =
    [
        new OptionItem(SettingsContract.SystemLanguage, string.Empty),
        new OptionItem(SettingsContract.ChineseLanguage, string.Empty),
        new OptionItem(SettingsContract.EnglishLanguage, string.Empty)
    ];

    public IReadOnlyList<OptionItem> MenuModeOptions { get; } =
    [
        new OptionItem(SettingsContract.GroupedMenu, string.Empty),
        new OptionItem(SettingsContract.DirectMenu, string.Empty),
        new OptionItem(SettingsContract.MultiDirectMenu, string.Empty)
    ];

    public IReadOnlyList<OptionItem> TerminalShellOptions { get; } =
    [
        new OptionItem(SettingsContract.AutomaticTerminalShell, string.Empty),
        new OptionItem(SettingsContract.PowerShell7TerminalShell, string.Empty),
        new OptionItem(SettingsContract.WindowsPowerShellTerminalShell, string.Empty),
        new OptionItem(SettingsContract.CommandPromptTerminalShell, string.Empty)
    ];

    public bool IsLoaded
    {
        get => isLoaded;
        private set
        {
            if (SetProperty(ref isLoaded, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
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
            var normalized = value is SettingsContract.DirectMenu or SettingsContract.MultiDirectMenu
                ? value
                : SettingsContract.GroupedMenu;
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

    public string TerminalShell
    {
        get => terminalShell;
        set
        {
            if (value is null)
            {
                return;
            }
            var normalized = value is SettingsContract.PowerShell7TerminalShell
                or SettingsContract.WindowsPowerShellTerminalShell
                or SettingsContract.CommandPromptTerminalShell
                ? value
                : SettingsContract.AutomaticTerminalShell;
            SetProperty(ref terminalShell, normalized);
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
        private set
        {
            if (SetProperty(ref hasValidationErrors, value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        }
    }

    public bool PreviewShowsMultipleRoots
    {
        get => previewShowsMultipleRoots;
        private set => SetProperty(ref previewShowsMultipleRoots, value);
    }

    public bool CanSave => IsLoaded && !HasValidationErrors;

    public string WindowTitle => localization["WindowTitle"];
    public string HeaderTitle => localization["Title"];
    public string MasterSwitchLabel => localization["MasterSwitch"];
    public string SaveLabel => localization["Save"];
    public string SavedMessage => localization["Saved"];
    public string SaveFailedLabel => localization["SaveFailed"];
    public string MenuSectionLabel => localization["MenuSection"];
    public string MenuModeLabel => localization["MenuMode"];
    public string DirectAgentLabel => localization["DirectAgent"];
    public string TerminalShellLabel => localization["TerminalShell"];
    public string TerminalShellHint => localization["TerminalShellHint"];
    public string TerminalProfileLabel => localization["TerminalProfile"];
    public string TerminalProfileHint => localization["TerminalProfileHint"];
    public string TerminalProfileHelp => localization["TerminalProfileHelp"];
    public string TerminalRequiredTitle => localization["TerminalRequiredTitle"];
    public string TerminalRequiredBody => localization["TerminalRequiredBody"];
    public string InstallFromStoreLabel => localization["InstallFromStore"];
    public string InstallLaterLabel => localization["InstallLater"];
    public string TerminalStoreOpenFailedMessage => localization["TerminalStoreOpenFailed"];
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
        terminalShell = settings.TerminalShell;
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
        var addedBuiltIn = false;
        foreach (var builtIn in SettingsDefaults.Create().Agents)
        {
            if (FindAgent(builtIn.Id) is null)
            {
                Attach(new AgentItemViewModel(builtIn, localization));
                addedBuiltIn = true;
            }
        }

        RefreshSort();
        var occupancy = settings;
        if (addedBuiltIn)
        {
            occupancy = await PersistAsync(cancellationToken);
        }
        try
        {
            await SynchronizeCommandPackagesAsync(occupancy, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Settings still load if Explorer command occupancy cannot be updated.
        }
        RefreshLocalization();
        IsLoaded = true;
        NotifyState();
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        var normalized = await PersistAsync(cancellationToken);
        try
        {
            await SynchronizeCommandPackagesAsync(normalized, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Settings were already written. Occupancy is retried on the next launch or save.
        }
    }

    private async Task<RightAgentSettings> PersistAsync(CancellationToken cancellationToken)
    {
        RefreshSort();
        var settings = new RightAgentSettings
        {
            Language = Language,
            MenuMode = MenuMode,
            DirectAgentId = DirectAgentId,
            TerminalShell = TerminalShell,
            TerminalProfile = TerminalProfile,
            MenuEnabled = MenuEnabled,
            Agents = Agents.Select(agent => agent.ToDefinition()).ToList()
        };
        var normalized = SettingsValidator.Normalize(settings);
        await store.SaveAsync(normalized, cancellationToken);
        DirectAgentId = normalized.DirectAgentId;
        return normalized;
    }

    private Task SynchronizeCommandPackagesAsync(
        RightAgentSettings settings,
        CancellationToken cancellationToken)
    {
        return CommandPackageSynchronizer.SynchronizeAsync(settings, store.LocalStateDirectory, cancellationToken);
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
        PreviewShowsMultipleRoots = MenuEnabled
                                    && MenuMode == SettingsContract.MultiDirectMenu
                                    && enabled.Count > 0;
        PreviewShowRootTitle = MenuEnabled
                               && MenuMode != SettingsContract.MultiDirectMenu
                               && (MenuMode == SettingsContract.GroupedMenu || enabled.Count > 0);
        PreviewShowRootHint = !PreviewShowsMultipleRoots && !PreviewShowRootTitle;
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
        if (MenuMode == SettingsContract.MultiDirectMenu
            && Agents.Count(agent => agent.Enabled) > SettingsContract.MaxMultiDirectAgents)
        {
            lines.Add("· " + localization["ValidationMultiDirectLimit"]);
        }

        ValidationSummary = string.Join(Environment.NewLine, lines);
        HasValidationErrors = lines.Count > 0;
    }

    private void RefreshLocalization()
    {
        localization.ConfiguredLanguage = language;
        LanguageOptions[0].UpdateLabel(localization["SystemLanguage"]);
        LanguageOptions[1].UpdateLabel(localization["Chinese"]);
        LanguageOptions[2].UpdateLabel(localization["English"]);
        MenuModeOptions[0].UpdateLabel(localization["Grouped"]);
        MenuModeOptions[1].UpdateLabel(localization["Direct"]);
        MenuModeOptions[2].UpdateLabel(localization["MultiDirect"]);
        TerminalShellOptions[0].UpdateLabel(localization["TerminalShellAuto"]);
        TerminalShellOptions[1].UpdateLabel(localization["PowerShell7"]);
        TerminalShellOptions[2].UpdateLabel(localization["WindowsPowerShell"]);
        TerminalShellOptions[3].UpdateLabel(localization["CommandPrompt"]);
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
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(MasterSwitchLabel));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(SavedMessage));
        OnPropertyChanged(nameof(SaveFailedLabel));
        OnPropertyChanged(nameof(MenuSectionLabel));
        OnPropertyChanged(nameof(MenuModeLabel));
        OnPropertyChanged(nameof(DirectAgentLabel));
        OnPropertyChanged(nameof(TerminalShellLabel));
        OnPropertyChanged(nameof(TerminalShellHint));
        OnPropertyChanged(nameof(TerminalProfileLabel));
        OnPropertyChanged(nameof(TerminalProfileHint));
        OnPropertyChanged(nameof(TerminalProfileHelp));
        OnPropertyChanged(nameof(TerminalRequiredTitle));
        OnPropertyChanged(nameof(TerminalRequiredBody));
        OnPropertyChanged(nameof(InstallFromStoreLabel));
        OnPropertyChanged(nameof(InstallLaterLabel));
        OnPropertyChanged(nameof(TerminalStoreOpenFailedMessage));
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
