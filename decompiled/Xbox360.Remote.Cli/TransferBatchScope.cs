using System;
using System.IO;

namespace Xbox360.Remote.Cli;

internal sealed class TransferBatchScope
{
	private readonly IProgress<CliOutput.TransferProgressUpdate> _progress;

	private readonly int _totalFiles;

	private readonly long _totalBytes;

	private long _completedBytes;

	private long _currentFileSize;

	private string _currentLabel = string.Empty;

	public int CompletedFiles { get; private set; }

	public TransferBatchScope(IProgress<CliOutput.TransferProgressUpdate> progress, int totalFiles, long totalBytes)
	{
		_progress = progress;
		_totalFiles = Math.Max(0, totalFiles);
		_totalBytes = Math.Max(0L, totalBytes);
	}

	public void StartFile(string label, long fileSize)
	{
		_currentLabel = label;
		_currentFileSize = Math.Max(0L, fileSize);
		ReportFileProgress(0L, $"file {CompletedFiles + 1}/{Math.Max(1, _totalFiles)}");
	}

	public void ReportFileProgress(long fileBytes, string? status = null)
	{
		long num = Math.Max(0L, Math.Min(fileBytes, _currentFileSize));
		long value = _completedBytes + num;
		string text = (string.IsNullOrWhiteSpace(status) ? $"file {CompletedFiles + 1}/{Math.Max(1, _totalFiles)}" : status);
		_progress.Report(new CliOutput.TransferProgressUpdate(value, text + " | " + Path.GetFileName(_currentLabel)));
	}

	public void CompleteFile()
	{
		_completedBytes = Math.Min(_completedBytes + _currentFileSize, (_totalBytes > 0) ? _totalBytes : (_completedBytes + _currentFileSize));
		CompletedFiles++;
		_progress.Report(new CliOutput.TransferProgressUpdate(_completedBytes, $"completed {CompletedFiles}/{Math.Max(1, _totalFiles)} | {Path.GetFileName(_currentLabel)}"));
		_currentFileSize = 0L;
		_currentLabel = string.Empty;
	}

	public void Finish()
	{
		_progress.Report(new CliOutput.TransferProgressUpdate(_totalBytes, $"completed {CompletedFiles}/{Math.Max(1, _totalFiles)}"));
	}
}
