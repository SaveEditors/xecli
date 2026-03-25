using System;
using System.Collections.Generic;
using System.Linq;

namespace Xbox360.Remote.Cli.God;

internal sealed class WindowsPath
{
	public IReadOnlyList<string> Components { get; }

	public WindowsPath(string path)
	{
		Components = (from p in path.Split('\\', StringSplitOptions.RemoveEmptyEntries)
			select p.Trim() into p
			where !string.IsNullOrEmpty(p)
			select p).ToList();
	}
}
