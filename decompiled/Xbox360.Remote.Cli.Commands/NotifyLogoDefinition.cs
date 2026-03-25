using System;
using System.Collections.Generic;

namespace Xbox360.Remote.Cli.Commands;

internal sealed class NotifyLogoDefinition
{
	public int Id { get; }

	public string Key { get; }

	public string Label { get; }

	public string? Notes { get; }

	public IReadOnlyList<string> Aliases { get; }

	public NotifyLogoDefinition(int id, string key, string label, string? notes = null, params string[] aliases)
	{
		Id = id;
		Key = key;
		Label = label;
		Notes = notes;
		Aliases = aliases ?? Array.Empty<string>();
	}
}
