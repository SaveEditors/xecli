using System.IO;

namespace Xbox360.Remote.Cli.God;

internal sealed class GodFileLayout
{
	private readonly string basePath;

	private readonly TitleExecutionInfo exeInfo;

	private readonly ContentType contentType;

	private string TitleIdString => exeInfo.TitleId.ToString("X8");

	private string ContentTypeString
	{
		get
		{
			uint num = (uint)contentType;
			return num.ToString("X8");
		}
	}

	private string MediaIdString
	{
		get
		{
			if (contentType != ContentType.GamesOnDemand)
			{
				return exeInfo.TitleId.ToString("X8");
			}
			return exeInfo.MediaId.ToString("X8");
		}
	}

	public GodFileLayout(string basePath, TitleExecutionInfo exeInfo, ContentType contentType)
	{
		this.basePath = basePath;
		this.exeInfo = exeInfo;
		this.contentType = contentType;
	}

	public string DataDirPath()
	{
		return Path.Combine(basePath, TitleIdString, ContentTypeString, MediaIdString + ".data");
	}

	public string PartFilePath(long partIndex)
	{
		return Path.Combine(DataDirPath(), $"Data{partIndex:0000}");
	}

	public string ConHeaderFilePath()
	{
		return Path.Combine(basePath, TitleIdString, ContentTypeString, MediaIdString);
	}
}
