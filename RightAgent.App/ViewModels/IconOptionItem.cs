namespace RightAgent.App.ViewModels;

public sealed class IconOptionItem : OptionItem
{
    public IconOptionItem(string key, string label, string previewPath)
        : base(key, label)
    {
        PreviewPath = previewPath;
    }

    public string PreviewPath { get; }
}
