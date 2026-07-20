using System.ComponentModel;
using System.Text;
using Spectre.Console.Cli;

namespace Xbox360.Remote.Cli.Commands;

public sealed class CompletionCommand : Command<CompletionCommand.Settings> {
    public sealed class Settings : CommandSettings {
        [CommandArgument(0, "<SHELL>")]
        [LocalizedDescription("Shell to generate completion for: powershell, pwsh, ps, bash, or zsh.")]
        public string Shell { get; init; } = string.Empty;
    }

    public override int Execute(CommandContext context, Settings settings) {
        if (!CompletionScriptBuilder.TryBuild(settings.Shell, out string script, out string error)) {
            Console.Error.WriteLine(error);
            return 1;
        }

        Console.Write(script);
        return 0;
    }
}

internal static class CompletionScriptBuilder {
    private static readonly string[] CommandPaths = new[] {
        "status",
        "profiles",
        "users",
        "title",
        "titles",
        "report",
        "report-profile",
        "report-profile add",
        "report-profile list",
        "report-profile ls",
        "report-profile show",
        "report-profile get",
        "report-profile remove",
        "report-profile rm",
        "health",
        "script run",
        "script replay",
        "batch run",
        "batch replay",
        "mem-bookmarks add",
        "bookmarks add",
        "memmarks add",
        "mem-bookmarks rename",
        "bookmarks rename",
        "memmarks rename",
        "mem-bookmarks mv",
        "bookmarks mv",
        "memmarks mv",
        "mem-bookmarks import",
        "bookmarks import",
        "memmarks import",
        "mem-bookmarks export",
        "bookmarks export",
        "memmarks export",
        "mem-bookmarks list",
        "bookmarks list",
        "memmarks list",
        "mem-bookmarks ls",
        "bookmarks ls",
        "memmarks ls",
        "mem-bookmarks show",
        "bookmarks show",
        "memmarks show",
        "mem-bookmarks jump",
        "bookmarks jump",
        "memmarks jump",
        "mem-bookmarks remove",
        "bookmarks remove",
        "memmarks remove",
        "mem-bookmarks rm",
        "bookmarks rm",
        "memmarks rm",
        "mem-bookmarks del",
        "bookmarks del",
        "memmarks del",
        "log list",
        "log export",
        "diagnostics bundle",
        "diag bundle",
        "support bundle",
        "release-notes",
        "changelog",
        "release check",
        "target",
        "language",
        "ping",
        "reboot",
        "restart",
        "shutdown",
        "poweroff",
        "launch",
        "run",
        "install",
        "start",
        "s",
        "connect",
        "c",
        "scan",
        "discover",
        "screenshot",
        "shot",
        "terminal",
        "shell",
        "homebrew list",
        "homebrew ls",
        "hb list",
        "hb ls",
        "homebrew install",
        "homebrew stage",
        "hb install",
        "hb stage",
        "ogxbox list",
        "ogxbox ls",
        "xefu list",
        "xefu ls",
        "ogxbox install",
        "ogxbox stage",
        "xefu install",
        "xefu stage",
        "xtaf devices",
        "xtaf disks",
        "xtaf partitions",
        "xtaf parts",
        "xtaf scan",
        "xtaf probe",
        "xtaf info",
        "xtaf list",
        "xtaf ls",
        "xtaf find",
        "xtaf get",
        "xtaf cat",
        "xtaf mkdir",
        "xtaf put",
        "xtaf inject",
        "xtaf mv",
        "xtaf rename",
        "xtaf rm",
        "xtaf del",
        "xtaf format",
        "xtaf initialize",
        "xtaf check",
        "xtaf verify",
        "xtaf repair",
        "xtaf extract",
        "xtaf dump",
        "xtaf metadata backup",
        "xtaf metadata restore",
        "xtaf meta backup",
        "xtaf meta restore",
        "xtaf patch plan",
        "xtaf patch apply",
        "xtaf-cli config",
        "xtaf-cli status",
        "xtaf-cli install",
        "xtaf-cli run",
        "xbdm info",
        "xbdm raw",
        "xbdm screenshot",
        "xbdm modules list",
        "xbdm modules ls",
        "xbdm modules info",
        "xbdm modules address",
        "xbdm modules addr",
        "xbdm modules resolve",
        "xbdm modules dump",
        "xbdm modules load",
        "xbdm modules unload",
        "xbdm modules remove",
        "xbdm modules pending",
        "xbdm modules verify",
        "xbdm module list",
        "xbdm module ls",
        "xbdm module info",
        "xbdm module address",
        "xbdm module addr",
        "xbdm module resolve",
        "xbdm module dump",
        "xbdm module load",
        "xbdm module unload",
        "xbdm module remove",
        "xbdm module pending",
        "xbdm module verify",
        "xbdm mem dump",
        "xbdm mem hexdump",
        "xbdm mem regions",
        "xbdm mem map",
        "xbdm mem diff",
        "xbdm mem compare",
        "xbdm mem dump-diff",
        "xbdm mem dump-compare",
        "xbdm mem session create",
        "xbdm mem session list",
        "xbdm mem session ls",
        "xbdm mem session show",
        "xbdm mem session jump",
        "xbdm mem session remove",
        "xbdm mem session rm",
        "xbdm mem session del",
        "xbdm mem peek",
        "xbdm mem read",
        "xbdm mem poke",
        "xbdm mem write",
        "xbdm mem watch",
        "xbdm mem strings",
        "xbdm mem find",
        "xbdm mem search",
        "xbdm xex dump",
        "xbdm xex launch",
        "xbdm xex run",
        "xbdm xex strings",
        "xbdm xex info",
        "xbdm xex header",
        "xbdm xex map",
        "xbdm xex decompile",
        "xbdm xex decode",
        "xbdm xex ida-decompile",
        "xbdm fs list",
        "xbdm fs ls",
        "xbdm fs get",
        "xbdm fs put",
        "xbdm fs cat",
        "xbdm fs rm",
        "xbdm fs del",
        "xbdm fs mkdir",
        "xbdm fs mv",
        "xbdm fs move",
        "xbdm threads list",
        "xbdm threads ls",
        "xbdm threads context",
        "xbdm threads suspend",
        "xbdm threads resume",
        "xbdm debug address-check",
        "xbdm debug addr",
        "xbdm debug resolve",
        "xbdm debug stop",
        "xbdm debug go",
        "xbdm debug watch",
        "xbdm debug events",
        "xbdm debug break add",
        "xbdm debug break remove",
        "xbdm debug break del",
        "xbdm debug break clearall",
        "xbdm debug break clear",
        "xbdm debug databreak add",
        "xbdm debug databreak remove",
        "xbdm debug databreak del",
        "modules list",
        "modules ls",
        "modules info",
        "modules address",
        "modules addr",
        "modules resolve",
        "modules dump",
        "modules load",
        "modules unload",
        "modules remove",
        "modules pending",
        "modules verify",
        "module list",
        "module ls",
        "module info",
        "module address",
        "module addr",
        "module resolve",
        "module dump",
        "module load",
        "module unload",
        "module remove",
        "module pending",
        "module verify",
        "mem dump",
        "mem hexdump",
        "mem regions",
        "mem map",
        "mem diff",
        "mem compare",
        "mem dump-diff",
        "mem dump-compare",
        "mem session create",
        "mem session list",
        "mem session ls",
        "mem session show",
        "mem session jump",
        "mem session remove",
        "mem session rm",
        "mem session del",
        "mem peek",
        "mem read",
        "mem poke",
        "mem write",
        "mem watch",
        "mem strings",
        "mem find",
        "mem search",
        "trainer apply",
        "trainer run",
        "xex dump",
        "xex launch",
        "xex run",
        "xex strings",
        "xex info",
        "xex header",
        "xex map",
        "xex bundle",
        "xex decompile",
        "xex decode",
        "xex ida-decompile",
        "fs list",
        "fs ls",
        "fs get",
        "fs put",
        "fs cat",
        "fs rm",
        "fs del",
        "fs mkdir",
        "fs mv",
        "fs move",
        "threads list",
        "threads ls",
        "threads context",
        "threads suspend",
        "threads resume",
        "debug address-check",
        "debug addr",
        "debug resolve",
        "debug stop",
        "debug go",
        "debug watch",
        "debug events",
        "debug break add",
        "debug break remove",
        "debug break del",
        "debug break clearall",
        "debug break clear",
        "debug databreak add",
        "debug databreak remove",
        "debug databreak del",
        "jrpc2 cpu-key",
        "jrpc2 temps",
        "jrpc2 title-id",
        "jrpc2 dashboard",
        "jrpc2 motherboard",
        "jrpc2 resolve",
        "jrpc2 notify",
        "jrpc2 call",
        "notify",
        "xnotify",
        "notify-icons list",
        "notify-icons show",
        "notify-icons resolve",
        "notify-icons add",
        "notify-icons remove",
        "notify-icons del",
        "smc version",
        "smc ver",
        "smc temps",
        "smc temperature",
        "fan set",
        "fan speed",
        "fan show",
        "fan state",
        "led set",
        "led apply",
        "led state",
        "led show",
        "signin state",
        "signin status",
        "tray open",
        "tray close",
        "xell boot",
        "xell info",
        "xell kv export",
        "xell kv dump",
        "xell nand dump",
        "popup show",
        "popup open",
        "network",
        "network doctor",
        "network recent",
        "ftp target",
        "ftp check",
        "ftp doctor",
        "ftp list",
        "ftp ls",
        "ftp find",
        "ftp get",
        "ftp put",
        "ftp sync",
        "ftp cat",
        "ftp hash",
        "ftp diff",
        "ftp rm",
        "ftp del",
        "ftp mkdir",
        "ftp mv",
        "ftp move",
        "target-saved add",
        "target-saved set",
        "target-saved list",
        "target-saved ls",
        "target-saved show",
        "target-saved get",
        "target-saved validate",
        "target-saved remove",
        "target-saved rm",
        "target-saved use",
        "target-saved select",
        "target-saved current",
        "target-saved state",
        "save list",
        "save ls",
        "save extract",
        "save pull",
        "save inject",
        "save push",
        "save put",
        "content list",
        "content ls",
        "content delete",
        "content rm",
        "content browse",
        "con info",
        "con verify",
        "con rehash",
        "con resign",
        "con magic-name",
        "con fatx-path",
        "profile info",
        "profile extract",
        "profile account show",
        "profile account extract",
        "profile account set-gamertag",
        "profile account set",
        "profile gpd list",
        "profile gpd ls",
        "profile gpd extract",
        "profile titles list",
        "profile titles ls",
        "profile titles add",
        "profile achievements list",
        "profile achievements ls",
        "profile achievements unlock",
        "profile achievements lock",
        "profile settings list",
        "profile settings ls",
        "profile settings get",
        "profile settings set",
        "profile avatar-colors get",
        "profile avatar-colors set",
        "xdbf list",
        "xdbf ls",
        "xdbf get",
        "xdbf extract",
        "xdbf sync-status",
        "avatar library show",
        "avatar library set",
        "avatar games",
        "avatar items",
        "avatar choose",
        "avatar pick",
        "avatar browse",
        "avatar gui",
        "avatar cache status",
        "avatar cache refresh",
        "avatar install",
        "avatar apply",
        "plugin list",
        "plugin ls",
        "plugin enable",
        "plugin disable",
        "god info",
        "god build",
        "god make",
        "god convert",
        "god watch",
        "god watchdog",
        "ghidra config",
        "ghidra install-loader",
        "ghidra analyze",
        "ghidra export-symbols",
        "ghidra decompile",
        "ghidra verify",
        "ida config",
        "ida check",
        "ida doctor",
        "ida install-loader",
        "ida analyze",
        "ida export-symbols",
        "ida decompile",
        "ida verify",
        "completion",
    };

    private static readonly string[] s_registeredCommandPaths = ExpandRegisteredCommandPaths(CommandPaths);

    internal static IReadOnlyList<string> RegisteredCommandPaths => s_registeredCommandPaths;

    private static string[] ExpandRegisteredCommandPaths(IEnumerable<string> commandPaths) {
        (string Prefix, string[] Aliases)[] branchAliases = [
            ("script", ["batch"]),
            ("mem-bookmarks", ["bookmarks", "memmarks"]),
            ("diagnostics", ["diag"]),
            ("homebrew", ["hb"]),
            ("ogxbox", ["xefu"]),
            ("xtaf", ["fatman", "fatx"]),
            ("xbdm modules", ["xbdm module"]),
            ("jrpc2", ["jrpc"]),
            ("target-saved", ["target-profile", "target profile", "tp"])
        ];

        HashSet<string> expanded = new(StringComparer.OrdinalIgnoreCase);
        foreach (string commandPath in commandPaths) {
            expanded.Add(commandPath);
            foreach ((string prefix, string[] aliases) in branchAliases) {
                if (!commandPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !commandPath.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                string suffix = commandPath[prefix.Length..];
                foreach (string alias in aliases)
                    expanded.Add(alias + suffix);
            }
        }

        return expanded.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool TryBuild(string shell, out string script, out string error) {
        string normalized = shell.Trim();
        if (normalized.Equals("powershell", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("pwsh", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("ps", StringComparison.OrdinalIgnoreCase)) {
            script = BuildPowerShell();
            error = string.Empty;
            return true;
        }

        if (normalized.Equals("bash", StringComparison.OrdinalIgnoreCase)) {
            script = BuildBash();
            error = string.Empty;
            return true;
        }

        if (normalized.Equals("zsh", StringComparison.OrdinalIgnoreCase)) {
            script = BuildZsh();
            error = string.Empty;
            return true;
        }

        script = string.Empty;
        error = $"Unsupported shell '{shell}'. Supported shells: powershell, pwsh, ps, bash, zsh. fish is not supported.";
        return false;
    }

    private static string BuildPowerShell() {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# XeCLI completion script for PowerShell.");
        builder.AppendLine("# Source this file in the current session or your profile.");
        builder.AppendLine("$script:XeCliCompletionPaths = @'");
        foreach (string path in RegisteredCommandPaths)
            builder.AppendLine(path);
        builder.AppendLine("'@ -split \"`r?`n\" | Where-Object { $_ }");
        builder.AppendLine();
        builder.AppendLine("function global:__XeCliGetCompletionCandidates {");
        builder.AppendLine("    param(");
        builder.AppendLine("        [string[]]$Tokens,");
        builder.AppendLine("        [string]$Partial");
        builder.AppendLine("    )");
        builder.AppendLine();
        builder.AppendLine("    $results = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)");
        builder.AppendLine("    foreach ($path in $script:XeCliCompletionPaths) {");
        builder.AppendLine("        $segments = $path.Split(' ')");
        builder.AppendLine("        if ($segments.Length -le $Tokens.Length) { continue }");
        builder.AppendLine("        $match = $true");
        builder.AppendLine("        for ($i = 0; $i -lt $Tokens.Length; $i++) {");
        builder.AppendLine("            if (-not $segments[$i].StartsWith($Tokens[$i], [System.StringComparison]::OrdinalIgnoreCase)) {");
        builder.AppendLine("                $match = $false");
        builder.AppendLine("                break");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if ($i -lt $Tokens.Length - 1 -and -not $segments[$i].Equals($Tokens[$i], [System.StringComparison]::OrdinalIgnoreCase)) {");
        builder.AppendLine("                $match = $false");
        builder.AppendLine("                break");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if (-not $match) { continue }");
        builder.AppendLine();
        builder.AppendLine("        $candidate = $segments[$Tokens.Length]");
        builder.AppendLine("        if ($candidate.StartsWith($Partial, [System.StringComparison]::OrdinalIgnoreCase)) {");
        builder.AppendLine("            [void]$results.Add($candidate)");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    return $results");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("Register-ArgumentCompleter -CommandName 'rgh' -Native -ScriptBlock {");
        builder.AppendLine("    param($wordToComplete, $commandAst, $cursorPosition)");
        builder.AppendLine();
        builder.AppendLine("    $typedText = (@($commandAst.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.Extent.Text }) -join ' ').TrimStart()");
        builder.AppendLine("    $hasTrailingSpace = $commandAst.Extent.Text.EndsWith(' ')");
        builder.AppendLine("    $parts = if ([string]::IsNullOrWhiteSpace($typedText)) { @() } else { $typedText -split '\\s+' }");
        builder.AppendLine("    $prefixTokens = @()");
        builder.AppendLine("    $partial = ''");
        builder.AppendLine("    if ($hasTrailingSpace) {");
        builder.AppendLine("        $prefixTokens = $parts");
        builder.AppendLine("    }");
        builder.AppendLine("    elseif ($parts.Count -gt 0) {");
        builder.AppendLine("        if ($parts.Count -gt 1) { $prefixTokens = $parts[0..($parts.Count - 2)] }");
        builder.AppendLine("        $partial = $parts[-1]");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    $results = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)");
        builder.AppendLine("    foreach ($candidate in __XeCliGetCompletionCandidates -Tokens $prefixTokens -Partial $partial) {");
        builder.AppendLine("        [void]$results.Add($candidate)");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    if (-not $hasTrailingSpace -and $parts.Count -gt 0) {");
        builder.AppendLine("        $exactPrefix = __XeCliGetCompletionCandidates -Tokens $parts -Partial ''");
        builder.AppendLine("        foreach ($candidate in $exactPrefix) {");
        builder.AppendLine("            [void]$results.Add($candidate)");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        if ($exactPrefix.Count -gt 0 -and $results.Contains($partial)) {");
        builder.AppendLine("            [void]$results.Remove($partial)");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    $results");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildBash() {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# XeCLI completion script for bash.");
        builder.AppendLine("# Source this file into the current shell session.");
        builder.AppendLine("XECLI_COMPLETION_PATHS=$(cat <<'EOF'");
        foreach (string path in RegisteredCommandPaths)
            builder.AppendLine(path);
        builder.AppendLine("EOF");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("_rgh_completion() {");
        builder.AppendLine("    local cur_word");
        builder.AppendLine("    local i");
        builder.AppendLine("    local -a prefix_parts current_parts");
        builder.AppendLine("    cur_word=${COMP_WORDS[COMP_CWORD]}");
        builder.AppendLine("    prefix_parts=()");
        builder.AppendLine("    for ((i=1; i<COMP_CWORD; i++)); do");
        builder.AppendLine("        prefix_parts+=(\"${COMP_WORDS[i]}\")");
        builder.AppendLine("    done");
        builder.AppendLine("    current_parts=()");
        builder.AppendLine("    if [[ -n $cur_word ]]; then");
        builder.AppendLine("        current_parts=(\"${prefix_parts[@]}\" \"$cur_word\")");
        builder.AppendLine("    fi");
        builder.AppendLine("    local -A seen=()");
        builder.AppendLine("    local path candidate path_parts ok prefix_len");
        builder.AppendLine("    while IFS= read -r path; do");
        builder.AppendLine("        [[ -z $path ]] && continue");
        builder.AppendLine("        IFS=' ' read -r -a path_parts <<< \"$path\"");
        builder.AppendLine("        if (( ${#path_parts[@]} <= ${#prefix_parts[@]} )); then");
        builder.AppendLine("            continue");
        builder.AppendLine("        fi");
        builder.AppendLine("        ok=1");
        builder.AppendLine("        for ((i=0; i<${#prefix_parts[@]}; i++)); do");
        builder.AppendLine("            if [[ ${path_parts[i],,} != ${prefix_parts[i],,}* ]]; then");
        builder.AppendLine("                ok=0");
        builder.AppendLine("                break");
        builder.AppendLine("            fi");
        builder.AppendLine("            if (( i < ${#prefix_parts[@]} - 1 )) && [[ ${path_parts[i],,} != ${prefix_parts[i],,} ]]; then");
        builder.AppendLine("                ok=0");
        builder.AppendLine("                break");
        builder.AppendLine("            fi");
        builder.AppendLine("        done");
        builder.AppendLine("        (( ok == 0 )) && continue");
        builder.AppendLine("        candidate=${path_parts[${#prefix_parts[@]}]}");
        builder.AppendLine("        if [[ -z $cur_word || ${candidate,,} == ${cur_word,,}* ]]; then");
        builder.AppendLine("            seen[\"$candidate\"]=1");
        builder.AppendLine("        fi");
        builder.AppendLine("    done <<< \"$XECLI_COMPLETION_PATHS\"");
        builder.AppendLine("    if [[ -n $cur_word ]]; then");
        builder.AppendLine("        while IFS= read -r path; do");
        builder.AppendLine("            [[ -z $path ]] && continue");
        builder.AppendLine("            IFS=' ' read -r -a path_parts <<< \"$path\"");
        builder.AppendLine("            if (( ${#path_parts[@]} <= ${#current_parts[@]} )); then");
        builder.AppendLine("                continue");
        builder.AppendLine("            fi");
        builder.AppendLine("            ok=1");
        builder.AppendLine("            for ((i=0; i<${#current_parts[@]}; i++)); do");
        builder.AppendLine("                if [[ ${path_parts[i],,} != ${current_parts[i],,}* ]]; then");
        builder.AppendLine("                    ok=0");
        builder.AppendLine("                    break");
        builder.AppendLine("                fi");
        builder.AppendLine("            done");
        builder.AppendLine("            (( ok == 0 )) && continue");
        builder.AppendLine("            candidate=${path_parts[${#current_parts[@]}]}");
        builder.AppendLine("            if [[ ${candidate,,} == ${cur_word,,}* ]]; then");
        builder.AppendLine("                seen[\"$candidate\"]=1");
        builder.AppendLine("            fi");
        builder.AppendLine("        done <<< \"$XECLI_COMPLETION_PATHS\"");
        builder.AppendLine("    fi");
        builder.AppendLine("    COMPREPLY=( $(compgen -W \"${!seen[*]}\" -- \"$cur_word\") )");
        builder.AppendLine("}");
        builder.AppendLine("complete -F _rgh_completion rgh");
        return builder.ToString();
    }

    private static string BuildZsh() {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("# XeCLI completion script for zsh.");
        builder.AppendLine("# Source this file into the current shell session.");
        builder.AppendLine("autoload -Uz bashcompinit");
        builder.AppendLine("bashcompinit");
        builder.AppendLine("autoload -Uz compinit");
        builder.AppendLine("compinit");
        builder.AppendLine("XECLI_COMPLETION_PATHS=$(cat <<'EOF'");
        foreach (string path in RegisteredCommandPaths)
            builder.AppendLine(path);
        builder.AppendLine("EOF");
        builder.AppendLine(")");
        builder.AppendLine();
        builder.AppendLine("_rgh_completion() {");
        builder.AppendLine("    local cur_word");
        builder.AppendLine("    local i");
        builder.AppendLine("    local -a prefix_parts current_parts");
        builder.AppendLine("    cur_word=${COMP_WORDS[COMP_CWORD]}");
        builder.AppendLine("    prefix_parts=()");
        builder.AppendLine("    for ((i=1; i<COMP_CWORD; i++)); do");
        builder.AppendLine("        prefix_parts+=(\"${COMP_WORDS[i]}\")");
        builder.AppendLine("    done");
        builder.AppendLine("    current_parts=()");
        builder.AppendLine("    if [[ -n $cur_word ]]; then");
        builder.AppendLine("        current_parts=(\"${prefix_parts[@]}\" \"$cur_word\")");
        builder.AppendLine("    fi");
        builder.AppendLine("    local -A seen=()");
        builder.AppendLine("    local path candidate path_parts ok");
        builder.AppendLine("    while IFS= read -r path; do");
        builder.AppendLine("        [[ -z $path ]] && continue");
        builder.AppendLine("        IFS=' ' read -r -a path_parts <<< \"$path\"");
        builder.AppendLine("        if (( ${#path_parts[@]} <= ${#prefix_parts[@]} )); then");
        builder.AppendLine("            continue");
        builder.AppendLine("        fi");
        builder.AppendLine("        ok=1");
        builder.AppendLine("        for ((i=0; i<${#prefix_parts[@]}; i++)); do");
        builder.AppendLine("            if [[ ${path_parts[i],,} != ${prefix_parts[i],,}* ]]; then");
        builder.AppendLine("                ok=0");
        builder.AppendLine("                break");
        builder.AppendLine("            fi");
        builder.AppendLine("            if (( i < ${#prefix_parts[@]} - 1 )) && [[ ${path_parts[i],,} != ${prefix_parts[i],,} ]]; then");
        builder.AppendLine("                ok=0");
        builder.AppendLine("                break");
        builder.AppendLine("            fi");
        builder.AppendLine("        done");
        builder.AppendLine("        (( ok == 0 )) && continue");
        builder.AppendLine("        candidate=${path_parts[${#prefix_parts[@]}]}");
        builder.AppendLine("        if [[ -z $cur_word || ${candidate,,} == ${cur_word,,}* ]]; then");
        builder.AppendLine("            seen[\"$candidate\"]=1");
        builder.AppendLine("        fi");
        builder.AppendLine("    done <<< \"$XECLI_COMPLETION_PATHS\"");
        builder.AppendLine("    if [[ -n $cur_word ]]; then");
        builder.AppendLine("        while IFS= read -r path; do");
        builder.AppendLine("            [[ -z $path ]] && continue");
        builder.AppendLine("            IFS=' ' read -r -a path_parts <<< \"$path\"");
        builder.AppendLine("            if (( ${#path_parts[@]} <= ${#current_parts[@]} )); then");
        builder.AppendLine("                continue");
        builder.AppendLine("            fi");
        builder.AppendLine("            ok=1");
        builder.AppendLine("            for ((i=0; i<${#current_parts[@]}; i++)); do");
        builder.AppendLine("                if [[ ${path_parts[i],,} != ${current_parts[i],,}* ]]; then");
        builder.AppendLine("                    ok=0");
        builder.AppendLine("                    break");
        builder.AppendLine("                fi");
        builder.AppendLine("            done");
        builder.AppendLine("            (( ok == 0 )) && continue");
        builder.AppendLine("            candidate=${path_parts[${#current_parts[@]}]}");
        builder.AppendLine("            if [[ ${candidate,,} == ${cur_word,,}* ]]; then");
        builder.AppendLine("                seen[\"$candidate\"]=1");
        builder.AppendLine("            fi");
        builder.AppendLine("        done <<< \"$XECLI_COMPLETION_PATHS\"");
        builder.AppendLine("    fi");
        builder.AppendLine("    COMPREPLY=( $(compgen -W \"${!seen[*]}\" -- \"$cur_word\") )");
        builder.AppendLine("}");
        builder.AppendLine("compdef _rgh_completion rgh");
        builder.AppendLine("complete -F _rgh_completion rgh");
        return builder.ToString();
    }
}
