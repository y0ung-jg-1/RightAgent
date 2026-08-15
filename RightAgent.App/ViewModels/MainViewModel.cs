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
    private string terminalProfile = string.Empty;
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
    private CancellationTokenSource? autoSaveCts;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    // Captured on the UI thread that constructed the view model; persist-side
    // callbacks marshal their binding cascade back through it.
    private readonly SynchronizationContext? uiContext = SynchronizationContext.Current;

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

    public ObservableCollection<OptionItem> TerminalProfileOptions { get; } = [];

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
                ScheduleAutoSave();
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
                NotifyMenuModePresentation();
                RefreshPreview();
                RefreshValidation();
                ScheduleAutoSave();
            }
        }
    }

    public bool IsDirectMode => MenuMode == SettingsContract.DirectMenu;

    public bool ShowAgentList => !IsDirectMode && !IsEmpty;

    public ObservableCollection<OptionItem> EnabledAgentOptions { get; } = [];

    public AgentItemViewModel? SelectedDirectAgent => IsDirectMode ? FindAgent(DirectAgentId) : null;

    public bool ShowDirectAgentEditor => SelectedDirectAgent is not null;

    public bool ShowNoAgentEditor => !ShowAgentList && !ShowDirectAgentEditor;

    public bool MenuEnabled
    {
        get => menuEnabled;
        set
        {
            if (SetProperty(ref menuEnabled, value))
            {
                RefreshPreview();
                RefreshValidation();
                ScheduleAutoSave();
            }
        }
    }

    public string? DirectAgentId
    {
        get => directAgentId;
        set
        {
            // ComboBox pushes null while ItemsSource is swapped; only RemoveAgent
            // and Normalize may clear the selection through ApplyDirectAgentId.
            if (value is null)
            {
                return;
            }
            ApplyDirectAgentId(value, allowClear: false, scheduleSave: true);
        }
    }

    private void ApplyDirectAgentId(string? value, bool allowClear, bool scheduleSave)
    {
        if (value is null && !allowClear)
        {
            return;
        }

        if (!SetProperty(ref directAgentId, value, nameof(DirectAgentId)))
        {
            return;
        }

        if (scheduleSave)
        {
            NotifyDirectAgentChanged();
            ScheduleAutoSave();
            return;
        }

        // Persist-side repair runs on a worker after ConfigureAwait(false):
        // marshal the binding cascade back to the UI thread and never
        // re-enter the save machinery, which would cancel the in-flight
        // save's own token and schedule a redundant write of the same state.
        if (uiContext is { } context)
        {
            context.Post(_ => NotifyDirectAgentChanged(), null);
        }
        else
        {
            NotifyDirectAgentChanged();
        }
    }

    private void NotifyDirectAgentChanged()
    {
        NotifyMenuModePresentation();
        RefreshPreview();
        RefreshValidation();
    }

    public string TerminalProfile
    {
        get => terminalProfile;
        set
        {
            if (value is null)
            {
                return;
            }
            if (SetProperty(ref terminalProfile, value))
            {
                ScheduleAutoSave();
            }
        }
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

    public bool PreviewShowsMultipleRoots
    {
        get => previewShowsMultipleRoots;
        private set => SetProperty(ref previewShowsMultipleRoots, value);
    }

    public event EventHandler<string>? PersistFailed;

    public string WindowTitle => localization["WindowTitle"];
    public string MasterSwitchLabel => localization["MasterSwitch"];
    public string SaveFailedLabel => localization["SaveFailed"];
    public string MenuSectionLabel => localization["MenuSection"];
    public string MenuModeLabel => localization["MenuMode"];
    public string DirectAgentLabel => localization["DirectAgent"];
    public string TerminalProfileLabel => localization["TerminalProfile"];
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
    public string VersionText => string.Format(CultureInfo.CurrentCulture, localization["VersionFormat"], ReadDisplayVersion());

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await store.LoadAsync(cancellationToken);
        language = settings.Language;
        localization.ConfiguredLanguage = language;
        menuMode = settings.MenuMode;
        directAgentId = settings.DirectAgentId;
        terminalProfile = settings.TerminalProfile ?? string.Empty;
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

        RefreshSort();
        RefreshLocalization();
        IsLoaded = true;
        NotifyState();
        _ = SynchronizeOccupancyInBackgroundAsync(settings);
    }

    /// <summary>Returns the settings snapshot that reached the disk, or null when the save was skipped.</summary>
    public async Task<RightAgentSettings?> SaveAsync(
        CancellationToken cancellationToken = default,
        bool synchronizeOccupancy = true)
    {
        if (!IsLoaded || HasValidationErrors)
        {
            return null;
        }

        RightAgentSettings normalized;
        await saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            normalized = await PersistAsync(cancellationToken).ConfigureAwait(false);
            if (!synchronizeOccupancy)
            {
                return normalized;
            }
        }
        finally
        {
            saveLock.Release();
        }

        try
        {
            await CommandPackageSynchronizer.SynchronizeAsync(
                normalized,
                store.LocalStateDirectory,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // JSON is already on disk. Occupancy is repaired on the next successful load.
        }
        return normalized;
    }

    public async Task FlushAutoSaveAsync()
    {
        autoSaveCts?.Cancel();
        // Persist first so a mode/occupancy change is not lost if the user
        // closes before the debounce timer. Occupancy follows only a real
        // persist: with invalid fields the disk keeps the last valid settings
        // and the installed slots must keep matching those, not the broken UI.
        var persisted = await SaveAsync(synchronizeOccupancy: false).ConfigureAwait(false);
        if (persisted is not null)
        {
            _ = SynchronizeOccupancyInBackgroundAsync(persisted);
        }
    }

    private void ScheduleAutoSave()
    {
        if (!IsLoaded || HasValidationErrors)
        {
            return;
        }

        // Cancel only, never Dispose: the replaced source is still held by the
        // in-flight DebouncedSaveAsync, where a disposed source makes
        // SemaphoreSlim.WaitAsync(token) throw ObjectDisposedException and
        // surface as a false PersistFailed.
        autoSaveCts?.Cancel();
        autoSaveCts = new CancellationTokenSource();
        var token = autoSaveCts.Token;
        _ = DebouncedSaveAsync(token);
    }

    private async Task DebouncedSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token);
            await SaveAsync(token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            PersistFailed?.Invoke(this, exception.Message);
        }
    }

    private async Task<RightAgentSettings> PersistAsync(CancellationToken cancellationToken)
    {
        var normalized = SettingsValidator.Normalize(BuildCurrentSettings());
        await store.SaveAsync(normalized, cancellationToken);
        ApplyDirectAgentId(normalized.DirectAgentId, allowClear: true, scheduleSave: false);
        return normalized;
    }

    private RightAgentSettings BuildCurrentSettings()
    {
        RefreshSort();
        return new RightAgentSettings
        {
            Language = Language,
            MenuMode = MenuMode,
            DirectAgentId = DirectAgentId,
            TerminalProfile = string.IsNullOrWhiteSpace(TerminalProfile) ? null : TerminalProfile,
            MenuEnabled = MenuEnabled,
            Agents = Agents.Select(agent => agent.ToDefinition()).ToList()
        };
    }

    private async Task SynchronizeOccupancyInBackgroundAsync(RightAgentSettings settings)
    {
        try
        {
            await CommandPackageSynchronizer.SynchronizeAsync(settings, store.LocalStateDirectory);
        }
        catch (Exception)
        {
            // Occupancy is repaired on the next successful sync. Never fail the UI for it.
        }
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
        ScheduleAutoSave();
    }

    public AgentItemViewModel? FindAgent(string? id) =>
        id is null ? null : Agents.FirstOrDefault(agent => agent.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public void RemoveAgent(AgentItemViewModel agent)
    {
        agent.PropertyChanged -= AgentPropertyChanged;
        Agents.Remove(agent);
        if (agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyDirectAgentId(
                Agents.FirstOrDefault(candidate => candidate.Enabled)?.Id,
                allowClear: true,
                scheduleSave: true);
        }
        RefreshSort();
        NotifyState();
        ScheduleAutoSave();
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
        ScheduleAutoSave();
    }

    public void SetAgentIcon(AgentItemViewModel agent, string relativePath)
    {
        agent.IconPath = "local:" + relativePath.Replace('\\', '/');
    }

    public void ResetAgentIcon(AgentItemViewModel agent)
    {
        agent.IconPath = "builtin:rightagent";
    }

    private void Attach(AgentItemViewModel agent)
    {
        agent.PropertyChanged += AgentPropertyChanged;
        Agents.Add(agent);
    }

    private void AgentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        var property = args.PropertyName;
        var affectsList = property is nameof(AgentItemViewModel.Name) or nameof(AgentItemViewModel.Enabled);
        if (affectsList)
        {
            OnPropertyChanged(nameof(Agents));
            RefreshEnabledAgentOptions();
        }

        if (affectsList
            || property is nameof(AgentItemViewModel.IconPath)
                or nameof(AgentItemViewModel.ActionType)
                or nameof(AgentItemViewModel.ActionValue)
                or nameof(AgentItemViewModel.HasNameError)
                or nameof(AgentItemViewModel.HasActionError))
        {
            RefreshPreview();
            RefreshValidation();
        }

        if (affectsList
            || property is nameof(AgentItemViewModel.IconPath)
                or nameof(AgentItemViewModel.ActionType)
                or nameof(AgentItemViewModel.ActionValue))
        {
            ScheduleAutoSave();
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
        var lines = new List<string>();
        foreach (var agent in Agents)
        {
            var error = agent.NameError ?? (MenuEnabled && agent.Enabled ? agent.ActionError : null);
            if (error is not null)
            {
                lines.Add($"· {agent.DisplayName}: {error}");
            }
        }

        if (MenuEnabled
            && MenuMode == SettingsContract.DirectMenu
            && !Agents.Any(agent => agent.Enabled && agent.Id.Equals(DirectAgentId, StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add("· " + localization["ValidationDirect"]);
        }
        if (MenuEnabled
            && MenuMode == SettingsContract.MultiDirectMenu
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
        RefreshTerminalProfileOptions();
        foreach (var agent in Agents)
        {
            agent.RefreshLanguage();
        }
        NotifyLocalizedProperties();
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(MenuMode));
        OnPropertyChanged(nameof(DirectAgentId));
        OnPropertyChanged(nameof(TerminalProfile));
        RefreshPreview();
        RefreshValidation();
    }

    private void RefreshTerminalProfileOptions()
    {
        var catalog = WindowsTerminalProfileCatalog.Load();
        var selected = catalog.NormalizeSelection(terminalProfile) ?? string.Empty;
        if (!string.Equals(terminalProfile, selected, StringComparison.Ordinal))
        {
            terminalProfile = selected;
            OnPropertyChanged(nameof(TerminalProfile));
        }

        var items = new List<OptionItem>
        {
            new(string.Empty, FormatDefaultProfileLabel(catalog.DefaultProfileName))
        };
        foreach (var profile in catalog.VisibleProfiles)
        {
            items.Add(new OptionItem(profile.Id, profile.Name));
        }

        if (selected.Length > 0
            && items.TrueForAll(item => !item.Key.Equals(selected, StringComparison.OrdinalIgnoreCase)))
        {
            var leftover = catalog.Find(selected);
            items.Add(new OptionItem(selected, leftover?.Name ?? selected));
        }

        TerminalProfileOptions.Clear();
        foreach (var item in items)
        {
            TerminalProfileOptions.Add(item);
        }
    }

    private string FormatDefaultProfileLabel(string? defaultProfileName) =>
        string.IsNullOrWhiteSpace(defaultProfileName)
            ? localization["TerminalProfileDefault"]
            : string.Format(CultureInfo.CurrentCulture, localization["TerminalProfileDefaultNamed"], defaultProfileName);

    private void NotifyLocalizedProperties()
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(MasterSwitchLabel));
        OnPropertyChanged(nameof(SaveFailedLabel));
        OnPropertyChanged(nameof(MenuSectionLabel));
        OnPropertyChanged(nameof(MenuModeLabel));
        OnPropertyChanged(nameof(DirectAgentLabel));
        OnPropertyChanged(nameof(TerminalProfileLabel));
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
        OnPropertyChanged(nameof(VersionText));
    }

    private static string ReadDisplayVersion()
    {
        try
        {
            var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}";
        }
        catch (Exception)
        {
            var assemblyVersion = typeof(App).Assembly.GetName().Version;
            return assemblyVersion is null
                ? "1.3.0"
                : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
        }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(Agents));
        RefreshEnabledAgentOptions();
        OnPropertyChanged(nameof(IsEmpty));
        RefreshPreview();
        RefreshValidation();
    }

    private void RefreshEnabledAgentOptions()
    {
        var enabled = Agents.Where(agent => agent.Enabled).ToList();
        for (var index = EnabledAgentOptions.Count - 1; index >= 0; --index)
        {
            if (!enabled.Any(agent => agent.Id == EnabledAgentOptions[index].Key))
            {
                EnabledAgentOptions.RemoveAt(index);
            }
        }

        for (var index = 0; index < enabled.Count; ++index)
        {
            var agent = enabled[index];
            var existing = EnabledAgentOptions.FirstOrDefault(option => option.Key == agent.Id);
            if (existing is null)
            {
                EnabledAgentOptions.Insert(index, new OptionItem(agent.Id, agent.Name));
                continue;
            }

            existing.UpdateLabel(agent.Name);
            var currentIndex = EnabledAgentOptions.IndexOf(existing);
            if (currentIndex != index)
            {
                EnabledAgentOptions.Move(currentIndex, index);
            }
        }

        EnsureDirectAgentId();
        OnPropertyChanged(nameof(DirectAgentId));
        NotifyMenuModePresentation();
    }

    private void EnsureDirectAgentId()
    {
        if (directAgentId is not null
            && Agents.Any(agent => agent.Enabled
                && agent.Id.Equals(directAgentId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var next = Agents.FirstOrDefault(agent => agent.Enabled)?.Id;
        if (!string.Equals(directAgentId, next, StringComparison.Ordinal))
        {
            directAgentId = next;
        }
    }

    private void NotifyMenuModePresentation()
    {
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(ShowAgentList));
        OnPropertyChanged(nameof(SelectedDirectAgent));
        OnPropertyChanged(nameof(ShowDirectAgentEditor));
        OnPropertyChanged(nameof(ShowNoAgentEditor));
        if (SelectedDirectAgent is { } agent)
        {
            agent.IsExpanded = true;
        }
    }
}
