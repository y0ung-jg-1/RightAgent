namespace RightAgent.App.ViewModels;

public sealed record OptionItem(string Key, string Label)
{
    // RadioButtons displays and announces the item itself, so keep ToString human-readable.
    public override string ToString() => Label;
}
