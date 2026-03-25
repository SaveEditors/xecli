using System.IO;

namespace Xbox360.Remote.Cli.God;

internal sealed class TitleInfo
{
	public ContentType ContentType { get; }

	public TitleExecutionInfo ExecutionInfo { get; }

	private TitleInfo(ContentType contentType, TitleExecutionInfo info)
	{
		ContentType = contentType;
		ExecutionInfo = info;
	}

	public static TitleInfo FromImage(IsoReader iso)
	{
		Stream entry = iso.GetEntry(new WindowsPath("\\default.xex"));
		if (entry != null)
		{
			XexHeader xexHeader = XexHeader.Read(entry);
			if (xexHeader.ExecutionInfo == null)
			{
				throw new InvalidDataException("No execution info in default.xex.");
			}
			return new TitleInfo(ContentType.GamesOnDemand, xexHeader.ExecutionInfo);
		}
		entry = iso.GetEntry(new WindowsPath("\\default.xbe"));
		if (entry != null)
		{
			XbeHeader xbeHeader = XbeHeader.Read(entry);
			if (xbeHeader.ExecutionInfo == null)
			{
				throw new InvalidDataException("No execution info in default.xbe.");
			}
			return new TitleInfo(ContentType.XboxOriginal, xbeHeader.ExecutionInfo);
		}
		throw new InvalidDataException("No executable found in this image.");
	}
}
