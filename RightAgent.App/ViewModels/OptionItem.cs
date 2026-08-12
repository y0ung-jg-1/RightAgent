namespace RightAgent.App.ViewModels;

public sealed record OptionItem(string Key, string Label)
{
    // Some item hosts display and announce the item itself, so keep ToString human-readable.
    public override string ToString() => Label;
}
