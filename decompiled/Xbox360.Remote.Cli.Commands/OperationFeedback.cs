using Spectre.Console;

namespace Xbox360.Remote.Cli.Commands;

internal static class OperationFeedback
{
	public static void WriteSuccess(string title, string detail)
	{
		AnsiConsole.MarkupLine("[bold springgreen3_1]SUCCESS[/] [grey]" + Markup.Escape(title) + "[/]");
		AnsiConsole.MarkupLine(detail);
	}

	public static void WriteFailure(string title, string detail)
	{
		AnsiConsole.MarkupLine("[bold red1]FAILED[/] [grey]" + Markup.Escape(title) + "[/]");
		AnsiConsole.MarkupLine("[red]" + Markup.Escape(detail) + "[/]");
	}

	public static void WriteWarning(string title, string detail)
	{
		AnsiConsole.MarkupLine("[bold gold1]NOTICE[/] [grey]" + Markup.Escape(title) + "[/]");
		AnsiConsole.MarkupLine(detail);
	}
}
