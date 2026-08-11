using System.Globalization;
using RightAgent.Core;

namespace RightAgent.App;

public sealed class Localization
{
    private string configuredLanguage = SettingsContract.SystemLanguage;

    public string ConfiguredLanguage
    {
        get => configuredLanguage;
        set => configuredLanguage = value is SettingsContract.ChineseLanguage or SettingsContract.EnglishLanguage
            ? value
            : SettingsContract.SystemLanguage;
    }

    public bool IsChinese => ConfiguredLanguage == SettingsContract.ChineseLanguage
                             || (ConfiguredLanguage == SettingsContract.SystemLanguage
                                 && CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));

    public string this[string key] => IsChinese ? Chinese(key) : English(key);

    private static string Chinese(string key) => key switch
    {
        "WindowTitle" => "RightAgent 设置",
        "Title" => "RightAgent",
        "Subtitle" => "在 Windows 11 右键菜单中，从当前文件夹直接打开编程 Agent。",
        "Save" => "保存设置",
        "Saved" => "设置已保存。下一次打开右键菜单时生效。",
        "SaveFailed" => "保存失败",
        "MenuSection" => "右键菜单",
        "MenuDescription" => "选择分组菜单，或让一个 Agent 直接显示在新版右键菜单中。",
        "MenuMode" => "菜单模式",
        "Grouped" => "使用 RightAgent 打开（分组）",
        "Direct" => "使用单个 Agent 打开（直达）",
        "DirectAgent" => "直达 Agent",
        "TerminalProfile" => "Windows Terminal 配置文件（可选）",
        "TerminalProfileHint" => "留空时使用 Windows Terminal 默认配置文件",
        "Preview" => "菜单预览",
        "AgentsSection" => "Agent",
        "AgentsDescription" => "启用、排序或配置终端命令与网页动作。命令由当前用户在 PowerShell 中执行。",
        "AddAgent" => "添加 Agent",
        "NoAgents" => "还没有 Agent。添加一个终端命令或网页动作。",
        "GeneralSection" => "常规",
        "Language" => "界面与菜单语言",
        "SystemLanguage" => "跟随系统",
        "Chinese" => "简体中文",
        "English" => "English",
        "Name" => "名称",
        "ActionType" => "动作类型",
        "ActionValue" => "命令或 URL",
        "TerminalCommand" => "终端命令",
        "Url" => "网页 URL",
        "Enabled" => "已启用",
        "Disabled" => "已关闭",
        "ChooseIcon" => "选择图标",
        "MoveUp" => "上移",
        "MoveDown" => "下移",
        "Delete" => "删除",
        "DeleteTitle" => "删除 Agent？",
        "DeleteBody" => "此操作会从 RightAgent 配置中删除该 Agent。",
        "Cancel" => "取消",
        "NewAgent" => "新 Agent",
        "ValidationName" => "每个 Agent 都必须有名称。",
        "ValidationAction" => "每个启用的 Agent 都必须有有效命令，或 http/https URL。",
        "ValidationDirect" => "直达模式需要选择一个已启用的 Agent。",
        "IconFilter" => "图标文件",
        _ => key
    };

    private static string English(string key) => key switch
    {
        "WindowTitle" => "RightAgent Settings",
        "Title" => "RightAgent",
        "Subtitle" => "Open a coding agent from the current folder in the Windows 11 context menu.",
        "Save" => "Save settings",
        "Saved" => "Settings saved. The next context menu will use them.",
        "SaveFailed" => "Could not save settings",
        "MenuSection" => "Context menu",
        "MenuDescription" => "Use a grouped menu, or place one agent directly in the modern context menu.",
        "MenuMode" => "Menu mode",
        "Grouped" => "Open with RightAgent (grouped)",
        "Direct" => "Open with one agent (direct)",
        "DirectAgent" => "Direct agent",
        "TerminalProfile" => "Windows Terminal profile (optional)",
        "TerminalProfileHint" => "Leave empty to use the default Windows Terminal profile",
        "Preview" => "Menu preview",
        "AgentsSection" => "Agents",
        "AgentsDescription" => "Enable, reorder, or configure terminal commands and web actions. Commands run as the current user in PowerShell.",
        "AddAgent" => "Add agent",
        "NoAgents" => "No agents yet. Add a terminal command or web action.",
        "GeneralSection" => "General",
        "Language" => "App and menu language",
        "SystemLanguage" => "Use system language",
        "Chinese" => "简体中文",
        "English" => "English",
        "Name" => "Name",
        "ActionType" => "Action type",
        "ActionValue" => "Command or URL",
        "TerminalCommand" => "Terminal command",
        "Url" => "Web URL",
        "Enabled" => "Enabled",
        "Disabled" => "Disabled",
        "ChooseIcon" => "Choose icon",
        "MoveUp" => "Move up",
        "MoveDown" => "Move down",
        "Delete" => "Delete",
        "DeleteTitle" => "Delete agent?",
        "DeleteBody" => "This removes the agent from the RightAgent configuration.",
        "Cancel" => "Cancel",
        "NewAgent" => "New agent",
        "ValidationName" => "Every agent needs a name.",
        "ValidationAction" => "Every enabled agent needs a valid command or an http/https URL.",
        "ValidationDirect" => "Direct mode requires an enabled agent.",
        "IconFilter" => "Icon files",
        _ => key
    };
}
