namespace Xbox360.Remote;

public sealed record XbdmScreenshotInfo(
    uint Pitch,
    uint Height,
    uint Width,
    uint Format,
    uint OffsetX,
    uint OffsetY,
    uint FramebufferSize
);

public sealed record XbdmScreenshot(XbdmScreenshotInfo Info, byte[] Data);
