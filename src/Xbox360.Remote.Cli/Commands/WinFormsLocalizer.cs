using System.Windows.Forms;
using XeCli.Localization;

namespace Xbox360.Remote.Cli.Commands;

internal static class WinFormsLocalizer {
    public static void Apply(Control control) {
        if (!LocalizedText.IsSpanish)
            return;

        ApplyRecursive(control);
    }

    private static void ApplyRecursive(Control control) {
        if (!string.IsNullOrWhiteSpace(control.Text))
            control.Text = LocalizedText.Translate(control.Text);

        if (control is ListView listView) {
            foreach (ColumnHeader column in listView.Columns) {
                column.Text = LocalizedText.Translate(column.Text);
            }
        }

        foreach (Control child in control.Controls) {
            ApplyRecursive(child);
        }
    }
}
