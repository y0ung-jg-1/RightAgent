namespace RightAgent.App.ViewModels;

public class OptionItem : BindableBase
{
    private string label;

    public OptionItem(string key, string label)
    {
        Key = key;
        this.label = label;
    }

    public string Key { get; }

    public string Label
    {
        get => label;
        private set => SetProperty(ref label, value);
    }

    public void UpdateLabel(string value) => Label = value;

    // Some item hosts display and announce the item itself, so keep ToString human-readable.
    public override string ToString() => Label;
}
