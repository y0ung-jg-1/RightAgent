using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using RightAgent.Core;

namespace RightAgent.App;

public sealed class Localization
{
    private static readonly ResourceManager ResourceManager = CreateResourceManager();
    private static readonly ResourceMap ResourceMap =
        ResourceManager.MainResourceMap.TryGetSubtree("Resources") ?? ResourceManager.MainResourceMap;

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

    public string this[string key]
    {
        get
        {
            try
            {
                var context = ResourceManager.CreateResourceContext();
                context.QualifierValues["Language"] = IsChinese ? "zh-CN" : "en-US";
                var value = ResourceMap.TryGetValue(key, context)?.ValueAsString;
                return string.IsNullOrEmpty(value) ? key : value;
            }
            catch (Exception)
            {
                return key;
            }
        }
    }

    private static ResourceManager CreateResourceManager()
    {
        try
        {
            return new ResourceManager();
        }
        catch (Exception)
        {
            var pri = Path.Combine(AppContext.BaseDirectory, "RightAgent.App.pri");
            return File.Exists(pri) ? new ResourceManager(pri) : new ResourceManager();
        }
    }
}
