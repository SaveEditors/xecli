namespace Xbox360.Remote.Cli.God;

internal sealed class GodFileLayout {
    private readonly string basePath;
    private readonly TitleExecutionInfo exeInfo;
    private readonly ContentType contentType;

    public GodFileLayout(string basePath, TitleExecutionInfo exeInfo, ContentType contentType) {
        this.basePath = basePath;
        this.exeInfo = exeInfo;
        this.contentType = contentType;
    }

    private string TitleIdString => exeInfo.TitleId.ToString("X8");

    private string ContentTypeString => ((uint) contentType).ToString("X8");

    private string MediaIdString {
        get {
            return contentType == ContentType.GamesOnDemand
                ? exeInfo.MediaId.ToString("X8")
                : exeInfo.TitleId.ToString("X8");
        }
    }

    public string DataDirPath() {
        return Path.Combine(basePath, TitleIdString, ContentTypeString, $"{MediaIdString}.data");
    }

    public string PartFilePath(long partIndex) {
        return Path.Combine(DataDirPath(), $"Data{partIndex:0000}");
    }

    public string ConHeaderFilePath() {
        return Path.Combine(basePath, TitleIdString, ContentTypeString, MediaIdString);
    }
}
