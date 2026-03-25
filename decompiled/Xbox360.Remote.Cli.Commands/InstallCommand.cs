using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using XeCli.Localization;
using Color = Spectre.Console.Color;
using Panel = Spectre.Console.Panel;

namespace Xbox360.Remote.Cli.Commands;

public sealed class InstallCommand : Command<InstallCommand.Settings>
{
	public sealed class Settings : CommandSettings
	{
		[CommandOption("--machine")]
		[LocalizedDescription("Install for all users (administrator approval required).")]
		public bool Machine { get; init; }

		[CommandOption("--uninstall")]
		[LocalizedDescription("Remove the command registration. With --machine, remove the all-users PATH entry.")]
		public bool Uninstall { get; init; }

		[CommandOption("--machine-path")]
		[LocalizedDescription("Legacy path-only install for the current executable directory (admin required).")]
		public bool MachinePath { get; init; }

		[CommandOption("--path <DIR>")]
		[LocalizedDescription("Install directory for XeCLI.")]
		public string? Path { get; init; }

		[CommandOption("--source <DIR>")]
		[LocalizedDescription("Source release directory containing rgh.exe and its runtime files.")]
		public string? Source { get; init; }

		[CommandOption("--no-path")]
		[LocalizedDescription("Do not add the install directory to PATH.")]
		public bool NoPath { get; init; }

		[CommandOption("--quiet")]
		[LocalizedDescription("Suppress non-error install output.")]
		public bool Quiet { get; init; }
	}

	private sealed record InstallPlan(bool AllUsers, string InstallDirectory, bool AddToPath);

	public override int Execute(CommandContext context, Settings settings)
	{
		if (!OperatingSystem.IsWindows())
		{
			AnsiConsole.MarkupLine("[red]The install command is only supported on Windows.[/]");
			return 1;
		}
		bool flag = IsInteractiveInstall(settings);
		string text = InstallHelpers.NormalizeDirectory(settings.Source ?? AppContext.BaseDirectory);
		string text2 = Path.Combine(text, "rgh.exe");
		if (!File.Exists(text2))
		{
			AnsiConsole.MarkupLine("[red]rgh.exe not found at[/] " + Markup.Escape(text2));
			return 1;
		}
		if (flag && !PromptToReinstallIfNeeded(text))
		{
			return 0;
		}
		if (settings.Uninstall)
		{
			if (settings.MachinePath)
			{
				if (!InstallHelpers.IsAdministrator())
				{
					AnsiConsole.MarkupLine("[red]Machine PATH uninstall requires an elevated terminal.[/]");
					return 1;
				}
				if (!InstallHelpers.RemoveMachinePathEntry(text, out string message))
				{
					AnsiConsole.MarkupLine("[red]" + Markup.Escape(message) + "[/]");
					return 1;
				}
				if (!settings.Quiet)
				{
					RenderInstallSummary("PATH uninstall complete", ("Scope", "Machine PATH"), ("Directory", text), ("Next Step", "Open a new terminal so the PATH change is picked up."));
				}
				return 0;
			}
			string text3 = InstallHelpers.NormalizeDirectory(settings.Path ?? InstallHelpers.DefaultUserInstallDir);
			if (settings.Machine)
			{
				if (!InstallHelpers.IsAdministrator())
				{
					AnsiConsole.MarkupLine("[red]All-users uninstall requires an elevated terminal.[/]");
					return 1;
				}
				if (!InstallHelpers.RemoveMachinePathEntry(text3, out string message2))
				{
					AnsiConsole.MarkupLine("[red]" + Markup.Escape(message2) + "[/]");
					return 1;
				}
				if (!settings.Quiet)
				{
					RenderInstallSummary("Uninstall complete", ("Scope", "All users"), ("Install Directory", text3), ("Command Access", "Removed from machine PATH"), ("Next Step", "You can delete the install folder manually if you no longer need it."));
				}
				return 0;
			}
			InstallHelpers.UninstallUserShim();
			InstallHelpers.RemoveUserPathEntry(text3, out string _);
			if (!settings.Quiet)
			{
				RenderInstallSummary("Uninstall complete", ("Scope", "Current user"), ("Install Directory", text3), ("Command Access", "Removed from user registration"), ("Next Step", "You can delete the install folder manually if you no longer need it."));
			}
			return 0;
		}
		if (settings.MachinePath)
		{
			if (!InstallHelpers.IsAdministrator())
			{
				AnsiConsole.MarkupLine("[red]Machine PATH install requires an elevated terminal.[/]");
				return 1;
			}
			if (!InstallHelpers.AddMachinePathEntry(text, out string message4))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(message4) + "[/]");
				return 1;
			}
			if (!settings.Quiet)
			{
				RenderInstallSummary("PATH install complete", ("Scope", "Machine PATH"), ("Directory", text), ("Command", "rgh"), ("Next Step", "Open a new terminal and run `rgh --help`."));
			}
			return 0;
		}
		InstallPlan installPlan;
		try
		{
			installPlan = BuildInstallPlan(settings, text);
		}
		catch (OperationCanceledException)
		{
			AnsiConsole.MarkupLine("[yellow]Installation cancelled.[/]");
			return 1;
		}
		string item = Path.Combine(installPlan.InstallDirectory, "rgh.exe");
		if (installPlan.AllUsers && !InstallHelpers.IsAdministrator())
		{
			int num = InstallHelpers.RunElevatedInstall(text2, text, installPlan.InstallDirectory, installPlan.AddToPath);
			if (num != 0)
			{
				AnsiConsole.MarkupLine($"[red]Installer exited with code {num}.[/]");
				return num;
			}
			if (!settings.Quiet)
			{
				RenderInstallSummary("Install complete", ("Scope", "All users"), ("Install Directory", installPlan.InstallDirectory), ("Command", "rgh"), ("PATH", installPlan.AddToPath ? "Machine PATH updated" : "Not changed"), ("Next Step", GetInstallNextStep(installPlan.InstallDirectory, installPlan.AddToPath)));
			}
			if (flag)
			{
				PromptToConnectDetectedConsole(installPlan.InstallDirectory);
			}
			return 0;
		}
		InstallHelpers.MirrorDirectory(text, installPlan.InstallDirectory);
		if (installPlan.AllUsers)
		{
			if (installPlan.AddToPath && !InstallHelpers.AddMachinePathEntry(installPlan.InstallDirectory, out string message5))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(message5) + "[/]");
				return 1;
			}
		}
		else if (installPlan.AddToPath)
		{
			InstallHelpers.UninstallUserShim();
			if (!InstallHelpers.AddUserPathEntry(installPlan.InstallDirectory, out string message6))
			{
				AnsiConsole.MarkupLine("[red]" + Markup.Escape(message6) + "[/]");
				return 1;
			}
		}
		else
		{
			InstallHelpers.InstallUserShim(installPlan.InstallDirectory);
		}
		if (!settings.Quiet)
		{
			RenderInstallSummary("Install complete", ("Scope", installPlan.AllUsers ? "All users" : "Current user"), ("Install Directory", installPlan.InstallDirectory), ("Target EXE", item), ("Command", "rgh"), ("PATH", (!installPlan.AddToPath) ? "Not changed" : (installPlan.AllUsers ? "Machine PATH updated" : "User PATH updated")), ("Next Step", GetInstallNextStep(installPlan.InstallDirectory, installPlan.AddToPath)));
		}
		if (flag)
		{
			PromptToConnectDetectedConsole(installPlan.InstallDirectory);
		}
		return 0;
	}

	private static string GetInstallNextStep(string installDirectory, bool addToPath)
	{
		if (addToPath)
		{
			return "Open a new terminal and run `rgh --help`.";
		}
		string text = Path.Combine(InstallHelpers.NormalizeDirectory(installDirectory), "rgh.exe");
		return "Run `" + text + " --help` or reinstall with PATH enabled.";
	}

	private static InstallPlan BuildInstallPlan(Settings settings, string sourceDir)
	{
		if (!IsInteractiveInstall(settings))
		{
			return new InstallPlan(settings.Machine, InstallHelpers.NormalizeDirectory(settings.Path ?? (settings.Machine ? InstallHelpers.DefaultMachineInstallDir : InstallHelpers.DefaultUserInstallDir)), !settings.NoPath);
		}
		AnsiConsole.Write(new Panel("[bold white]XeCLI Installer[/]\n[grey]This setup copies the current XeCLI release to an install folder and registers the `rgh` command for terminal use.[/]\n\n[white]Source:[/] [deepskyblue1]" + Markup.Escape(sourceDir) + "[/]\n[white]Recommended user install:[/] [springgreen3_1]" + Markup.Escape(InstallHelpers.DefaultUserInstallDir) + "[/]\n[white]Recommended all-users install:[/] [gold1]" + Markup.Escape(InstallHelpers.DefaultMachineInstallDir) + "[/]\n\n[mediumpurple3]Created by Pew - Se7ensins[/]").BorderColor(Color.Silver).Header("[bold deepskyblue1]Setup[/]"));
		AnsiConsole.MarkupLine("[bold white]1.[/] Current user [grey](Recommended)[/]");
		AnsiConsole.MarkupLine("[bold white]2.[/] All users [grey](Administrator approval required)[/]");
		bool flag = ReadInstallerResponse("Choose an install scope [1]:", "1").Trim() == "2";
		string text = (flag ? InstallHelpers.DefaultMachineInstallDir : InstallHelpers.DefaultUserInstallDir);
		string text2 = ReadInstallerResponse("Install directory:", text).Trim();
		if (string.IsNullOrWhiteSpace(text2))
		{
			text2 = text;
		}
		bool flag2 = ReadInstallerYesNo("Add `rgh` to PATH for new terminals? [Y/n]:", defaultYes: true);
		AnsiConsole.Write(new Panel($"[white]Scope:[/] {(flag ? "[gold1]All users[/]" : "[springgreen3_1]Current user[/]")}\n[white]Install directory:[/] [deepskyblue1]{Markup.Escape(InstallHelpers.NormalizeDirectory(text2))}[/]\n[white]PATH update:[/] {(flag2 ? "[springgreen3_1]Yes[/]" : "[grey]No[/]")}").BorderColor(Color.Grey).Header("[bold deepskyblue1]Plan[/]"));
		if (!ReadInstallerYesNo("Continue with installation? [Y/n]:", defaultYes: true))
		{
			throw new OperationCanceledException("Installer cancelled by user.");
		}
		return new InstallPlan(flag, InstallHelpers.NormalizeDirectory(text2), flag2);
	}

	private static void RenderInstallSummary(string title, params (string Label, string Value)[] rows)
	{
		OperationFeedback.WriteSuccess(title, "[grey]Created by Pew - Se7ensins[/]");
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]Field[/]"));
		table.AddColumn(new TableColumn("[bold white]Value[/]"));
		for (int i = 0; i < rows.Length; i++)
		{
			var (text, text2) = rows[i];
			table.AddRow("[white]" + Markup.Escape(text) + "[/]", "[springgreen3_1]" + Markup.Escape(text2) + "[/]");
		}
		AnsiConsole.Write(table);
	}

	private static bool IsInteractiveInstall(Settings settings)
	{
		if (!settings.Quiet && !Console.IsInputRedirected && string.IsNullOrWhiteSpace(settings.Path) && !settings.Machine)
		{
			return !settings.NoPath;
		}
		return false;
	}

	private static bool PromptToReinstallIfNeeded(string sourceDir)
	{
		string text = InstallHelpers.TryResolveInstalledDirectory();
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}
		string value = (InstallHelpers.IsSameDirectory(text, sourceDir) ? "[grey]This copy is already the registered XeCLI install.[/]" : "[grey]A registered XeCLI install is already available on this system.[/]");
		AnsiConsole.Write(new Panel($"[bold white]XeCLI is already installed[/]\n{value}\n\n[white]Registered location:[/] [springgreen3_1]{Markup.Escape(text)}[/]\n[white]Current source:[/] [deepskyblue1]{Markup.Escape(sourceDir)}[/]").BorderColor(Color.Silver).Header("[bold deepskyblue1]Reinstall[/]"));
		return ReadInstallerYesNo("Would you like to reinstall or update it? [y/N]:", defaultYes: false);
	}

	private static string ReadInstallerResponse(string prompt, string? defaultValue = null)
	{
		AnsiConsole.Markup("[white]" + Markup.Escape(prompt) + "[/] ");
		string text = Console.ReadLine();
		if (string.IsNullOrWhiteSpace(text))
		{
			return defaultValue ?? string.Empty;
		}
		return text.Trim();
	}

	private static bool ReadInstallerYesNo(string prompt, bool defaultYes)
	{
		string defaultValue = (defaultYes ? "y" : "n");
		while (true)
		{
			string text = ReadInstallerResponse(prompt, defaultValue);
			if (string.IsNullOrWhiteSpace(text))
			{
				return defaultYes;
			}
			text = text.Trim().ToLowerInvariant();
			if (LocalizedText.IsAffirmative(text))
			{
				return true;
			}
			if (LocalizedText.IsNegative(text))
			{
				break;
			}
			AnsiConsole.MarkupLine("[red]Please enter Y or N.[/]");
		}
		return false;
	}

	private static void PromptToConnectDetectedConsole(string installDirectory)
	{
		IReadOnlyList<DiscoveredConsole> consoles = Array.Empty<DiscoveredConsole>();
		bool discoveryFailed = false;
		Status status = AnsiConsole.Status().Spinner(Spinner.Known.Dots);
		Color? foreground = Color.SpringGreen3_1;
		Decoration? decoration = Decoration.Bold;
		status.SpinnerStyle(new Style(foreground, null, decoration)).Start("Searching for consoles on the local network...", delegate
		{
			try
			{
				consoles = DiscoverInstallerConsoles().GetAwaiter().GetResult();
			}
			catch
			{
				discoveryFailed = true;
			}
		});
		if (discoveryFailed)
		{
			AnsiConsole.MarkupLine("[yellow]Console discovery did not complete during setup. You can run `rgh start` later.[/]");
			return;
		}
		if (consoles.Count == 0)
		{
			AnsiConsole.MarkupLine("[grey]No consoles were detected during setup. Run `rgh start` later if needed.[/]");
			return;
		}
		AnsiConsole.MarkupLine((consoles.Count == 1) ? "[green]1 console detected.[/]" : $"[green]{consoles.Count} consoles detected.[/]");
		DiscoveredConsole discoveredConsole = ((consoles.Count == 1) ? PromptForSingleConsole(consoles[0]) : PromptForMultipleConsoles(consoles));
		if (discoveredConsole == null)
		{
			return;
		}
		CliConfig cliConfig = CliConfig.Load();
		cliConfig.DefaultIp = discoveredConsole.Ip.ToString();
		cliConfig.DefaultPort = ((discoveredConsole.Port > 0) ? discoveredConsole.Port : 730);
		cliConfig.Save();
		string text = Path.Combine(InstallHelpers.NormalizeDirectory(installDirectory), "rgh.exe");
		if (!File.Exists(text))
		{
			AnsiConsole.MarkupLine("[yellow]Installed console command not found. Skipping automatic connect test.[/]");
			return;
		}
		AnsiConsole.MarkupLine("[green]Connecting now and running status...[/]");
		using Process process = Process.Start(new ProcessStartInfo(text)
		{
			UseShellExecute = false,
			WorkingDirectory = InstallHelpers.NormalizeDirectory(installDirectory),
			ArgumentList = 
			{
				"status",
				"--ip",
				discoveredConsole.Ip.ToString(),
				"--port",
				((discoveredConsole.Port > 0) ? discoveredConsole.Port : 730).ToString(CultureInfo.InvariantCulture)
			}
		});
		process?.WaitForExit();
	}

	private static DiscoveredConsole? PromptForSingleConsole(DiscoveredConsole console)
	{
		string consoleDisplayName = GetConsoleDisplayName(console);
		if (!ReadInstallerYesNo($"{consoleDisplayName} at {console.Ip} was detected, would you like to connect now? [y/N]:", defaultYes: false))
		{
			return null;
		}
		return console;
	}

	private static DiscoveredConsole? PromptForMultipleConsoles(IReadOnlyList<DiscoveredConsole> consoles)
	{
		AnsiConsole.MarkupLine("[bold white]Multiple consoles detected, please choose which one you'd like to connect to.[/]");
		Table table = CliOutput.CreateTable();
		table.AddColumn(new TableColumn("[bold white]#[/]"));
		table.AddColumn(new TableColumn("[bold springgreen3_1]Console[/]"));
		table.AddColumn(new TableColumn("[bold deepskyblue1]IP[/]"));
		table.AddColumn(new TableColumn("[bold gold1]Port[/]"));
		for (int i = 0; i < consoles.Count; i++)
		{
			DiscoveredConsole discoveredConsole = consoles[i];
			table.AddRow($"[white]{i + 1}[/]", "[springgreen3_1]" + Markup.Escape(GetConsoleDisplayName(discoveredConsole)) + "[/]", "[deepskyblue1]" + Markup.Escape(discoveredConsole.Ip.ToString()) + "[/]", "[gold1]" + ((discoveredConsole.Port > 0) ? discoveredConsole.Port : 730).ToString(CultureInfo.InvariantCulture) + "[/]");
		}
		AnsiConsole.Write(table);
		int result;
		while (true)
		{
			string text = ReadInstallerResponse("Choose a console number or press Enter to skip:", string.Empty);
			if (string.IsNullOrWhiteSpace(text))
			{
				return null;
			}
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 1 && result <= consoles.Count)
			{
				break;
			}
			AnsiConsole.MarkupLine("[red]Enter a valid console number or press Enter to skip.[/]");
		}
		return consoles[result - 1];
	}

	private static string GetConsoleDisplayName(DiscoveredConsole console)
	{
		if (!string.IsNullOrWhiteSpace(console.DebugName))
		{
			return console.DebugName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(console.ConsoleId))
		{
			return console.ConsoleId.Trim();
		}
		return "Xbox 360 console";
	}

	private static async Task<IReadOnlyList<DiscoveredConsole>> DiscoverInstallerConsoles()
	{
		IReadOnlyList<IPNetwork> installerNetworks = GetInstallerNetworks();
		DiscoveryOptions options = new DiscoveryOptions
		{
			Ports = new int[2] { 730, 731 },
			NapTimeoutMs = 900,
			TcpTimeoutMs = 175,
			MaxConcurrency = 96,
			UseNapDiscovery = true,
			UseTcpScan = true,
			Networks = installerNetworks
		};
		using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(6L));
		return await ConsoleDiscovery.DiscoverAsync(options, cts.Token);
	}

	private static IReadOnlyList<IPNetwork> GetInstallerNetworks()
	{
		List<IPNetwork> list = new List<IPNetwork>();
		foreach (NetworkInterface item in (from i in NetworkInterface.GetAllNetworkInterfaces()
			where i.OperationalStatus == OperationalStatus.Up
			select i).OrderBy(GetInstallerInterfacePriority).ThenBy<NetworkInterface, string>((NetworkInterface i) => i.Name, StringComparer.OrdinalIgnoreCase))
		{
			IPInterfaceProperties iPProperties;
			try
			{
				iPProperties = item.GetIPProperties();
			}
			catch
			{
				continue;
			}
			foreach (UnicastIPAddressInformation unicastAddress in iPProperties.UnicastAddresses)
			{
				if (unicastAddress.Address.AddressFamily != AddressFamily.InterNetwork || unicastAddress.IPv4Mask == null)
				{
					continue;
				}
				IPNetwork network;
				try
				{
					network = ComputeInstallerNetwork(unicastAddress.Address, unicastAddress.IPv4Mask);
					if (network.HostCount > 1024)
					{
						network = ComputeInstallerNetwork(unicastAddress.Address, IPAddress.Parse("255.255.255.0"));
					}
				}
				catch
				{
					continue;
				}
				if (!list.Any((IPNetwork existing) => existing.Network.Equals(network.Network) && existing.Mask.Equals(network.Mask)))
				{
					list.Add(network);
				}
			}
			if (list.Count >= 3)
			{
				break;
			}
		}
		return list;
	}

	private static int GetInstallerInterfacePriority(NetworkInterface iface)
	{
		return iface.NetworkInterfaceType switch
		{
			NetworkInterfaceType.Ethernet => 0, 
			NetworkInterfaceType.Wireless80211 => 1, 
			NetworkInterfaceType.GigabitEthernet => 2, 
			_ => 9, 
		};
	}

	private static IPNetwork ComputeInstallerNetwork(IPAddress address, IPAddress mask)
	{
		byte[] addressBytes = address.GetAddressBytes();
		byte[] addressBytes2 = mask.GetAddressBytes();
		byte[] array = new byte[4];
		for (int i = 0; i < 4; i++)
		{
			array[i] = (byte)(addressBytes[i] & addressBytes2[i]);
		}
		return new IPNetwork(new IPAddress(array), mask);
	}
}
