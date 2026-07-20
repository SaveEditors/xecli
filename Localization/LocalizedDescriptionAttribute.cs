using System.ComponentModel;
using XeCli.Localization;

[AttributeUsage(AttributeTargets.All, AllowMultiple = false)]
internal sealed class LocalizedDescriptionAttribute : DescriptionAttribute
{
    public LocalizedDescriptionAttribute(string description)
        : base(description)
    {
    }

    public override string Description => LocalizedText.Translate(base.Description);
}
