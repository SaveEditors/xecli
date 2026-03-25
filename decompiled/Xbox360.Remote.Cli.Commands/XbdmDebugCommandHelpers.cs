namespace Xbox360.Remote.Cli.Commands;

internal static class XbdmDebugCommandHelpers
{
	internal static string NormalizeDataBreakType(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "write";
		}
		return value.Trim().ToLowerInvariant() switch
		{
			"read" => "read", 
			"rw" => "read", 
			"execute" => "execute", 
			"exec" => "execute", 
			"write" => "write", 
			_ => "write", 
		};
	}
}
