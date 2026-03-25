namespace Xbox360.Remote.Cli.God;

internal sealed record GodConversionResult(TitleExecutionInfo ExecutionInfo, ContentType ContentType, ulong DataSize, ulong BlockCount, ulong PartCount, string OutputDir, string ConHeaderPath);
