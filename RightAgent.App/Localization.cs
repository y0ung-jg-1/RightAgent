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
        "MasterSwitch" => "启用 RightAgent",
        "MenuOff" => "（菜单已关闭）",
        "Save" => "保存设置",
        "Saved" => "设置已保存。下一次打开右键菜单时生效。",
        "SaveFailed" => "保存失败",
        "MenuSection" => "右键菜单",
        "MenuMode" => "菜单模式",
        "Grouped" => "使用 RightAgent 打开（分组）",
        "Direct" => "使用单个 Agent 打开（直达）",
        "DirectAgent" => "直达 Agent",
        "TerminalProfile" => "Terminal 配置文件",
        "TerminalProfileHint" => "留空时使用默认配置文件",
        "Preview" => "菜单预览",
        "AgentsSection" => "Agent",
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
        "ValidationTitle" => "请先解决以下问题再保存",
        "ErrorNameRequired" => "输入名称。",
        "ErrorActionRequired" => "已启用的 Agent 需要命令或 URL。",
        "ErrorUrlInvalid" => "URL 必须以 http:// 或 https:// 开头。",
        "UnnamedAgent" => "（未命名）",
        "OpenWithRightAgent" => "使用 RightAgent 打开",
        "OpenWithAgent" => "使用 {0} 打开",
        "PreviewEmpty" => "（没有已启用的 Agent，菜单将隐藏）",
        "EnableFor" => "启用 {0}",
        "MoveUpFor" => "上移 {0}",
        "MoveDownFor" => "下移 {0}",
        "DeleteFor" => "删除 {0}",
        "ChooseIconFor" => "为 {0} 选择图标",
        "IconFor" => "{0} 的图标",
        "IconFilter" => "图标文件",
        _ => key
    };

    private static string English(string key) => key switch
    {
        "WindowTitle" => "RightAgent Settings",
        "Title" => "RightAgent",
        "MasterSwitch" => "Enable RightAgent",
        "MenuOff" => "(Menu is off)",
        "Save" => "Save settings",
        "Saved" => "Settings saved. The next context menu will use them.",
        "SaveFailed" => "Could not save settings",
        "MenuSection" => "Context menu",
        "MenuMode" => "Menu mode",
        "Grouped" => "Open with RightAgent (grouped)",
        "Direct" => "Open with one agent (direct)",
        "DirectAgent" => "Direct agent",
        "TerminalProfile" => "Terminal profile",
        "TerminalProfileHint" => "Leave empty for the default profile",
        "Preview" => "Menu preview",
        "AgentsSection" => "Agents",
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
        "ValidationTitle" => "Fix the following before saving",
        "ErrorNameRequired" => "Enter a name.",
        "ErrorActionRequired" => "An enabled agent needs a command or URL.",
        "ErrorUrlInvalid" => "The URL must start with http:// or https://.",
        "UnnamedAgent" => "(Unnamed)",
        "OpenWithRightAgent" => "Open with RightAgent",
        "OpenWithAgent" => "Open with {0}",
        "PreviewEmpty" => "(No enabled agents — the menu is hidden)",
        "EnableFor" => "Enable {0}",
        "MoveUpFor" => "Move {0} up",
        "MoveDownFor" => "Move {0} down",
        "DeleteFor" => "Delete {0}",
        "ChooseIconFor" => "Choose icon for {0}",
        "IconFor" => "Icon for {0}",
        "IconFilter" => "Icon files",
        _ => key
    };
}
