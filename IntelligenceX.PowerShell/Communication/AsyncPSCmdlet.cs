using System;
using System.Collections.Concurrent;
using System.Management.Automation;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.PowerShell;

/// <summary>
/// An abstract base class for asynchronous PowerShell cmdlets.
/// </summary>
public abstract class AsyncPSCmdlet : PSCmdlet, IDisposable {
    private enum PipelineType {
        Output,
        OutputEnumerate,
        Error,
        Warning,
        Verbose,
        Debug,
        Information,
        Progress,
        ShouldProcess,
    }

    private CancellationTokenSource _cancelSource = new();
    private BlockingCollection<(object?, PipelineType)>? _currentOutPipe;
    private BlockingCollection<object?>? _currentReplyPipe;

    /// <summary>
    /// Gets the cancellation token that is triggered when the cmdlet is stopped.
    /// </summary>
    protected internal CancellationToken CancelToken => _cancelSource.Token;

    /// <inheritdoc />
    protected override void BeginProcessing() => RunBlockInAsync(BeginProcessingAsync);

    /// <summary>
    /// Override this method to implement asynchronous begin processing logic.
    /// </summary>
    protected virtual Task BeginProcessingAsync() => Task.CompletedTask;

    /// <inheritdoc />
    protected override void ProcessRecord() => RunBlockInAsync(ProcessRecordAsync);

    /// <summary>
    /// Override this method to implement asynchronous record processing logic.
    /// </summary>
    protected virtual Task ProcessRecordAsync() => Task.CompletedTask;

    /// <inheritdoc />
    protected override void EndProcessing() => RunBlockInAsync(EndProcessingAsync);

    /// <summary>
    /// Override this method to implement asynchronous end processing logic.
    /// </summary>
    protected virtual Task EndProcessingAsync() => Task.CompletedTask;

    /// <inheritdoc />
    protected override void StopProcessing() => _cancelSource?.Cancel();

    private void RunBlockInAsync(Func<Task> task) {
        using BlockingCollection<(object?, PipelineType)> outPipe = new();
        using BlockingCollection<object?> replyPipe = new();
        Task blockTask = Task.Run(async () => {
            try {
                _currentOutPipe = outPipe;
                _currentReplyPipe = replyPipe;
                await task().ConfigureAwait(false);
            } finally {
                _currentOutPipe = null;
                _currentReplyPipe = null;
                outPipe.CompleteAdding();
                replyPipe.CompleteAdding();
            }
        });

        foreach ((object? data, PipelineType pipelineType) in outPipe.GetConsumingEnumerable()) {
            switch (pipelineType) {
                case PipelineType.Output:
                    base.WriteObject(data);
                    break;
                case PipelineType.OutputEnumerate:
                    base.WriteObject(data, true);
                    break;
                case PipelineType.Error:
                    base.WriteError((ErrorRecord)data!);
                    break;
                case PipelineType.Warning:
                    base.WriteWarning((string)data!);
                    break;
                case PipelineType.Verbose:
                    base.WriteVerbose((string)data!);
                    break;
                case PipelineType.Debug:
                    base.WriteDebug((string)data!);
                    break;
                case PipelineType.Information:
                    base.WriteInformation((InformationRecord)data!);
                    break;
                case PipelineType.Progress:
                    base.WriteProgress((ProgressRecord)data!);
                    break;
                case PipelineType.ShouldProcess:
                    (string target, string action) = (ValueTuple<string, string>)data!;
                    bool res = base.ShouldProcess(target, action);
                    replyPipe.Add(res);
                    break;
            }
        }

        blockTask.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Determines whether the cmdlet should continue processing.
    /// </summary>
    public new bool ShouldProcess(string target, string action) {
        ThrowIfStopped();
        _currentOutPipe?.Add(((target, action), PipelineType.ShouldProcess));
        return (bool)_currentReplyPipe?.Take(CancelToken)!;
    }

    /// <summary>
    /// Writes an object to the output pipeline.
    /// </summary>
    public new void WriteObject(object? sendToPipeline) => WriteObject(sendToPipeline, false);

    /// <summary>
    /// Writes an object to the output pipeline, optionally enumerating collections.
    /// </summary>
    public new void WriteObject(object? sendToPipeline, bool enumerateCollection) {
        ThrowIfStopped();
        _currentOutPipe?.Add((sendToPipeline, enumerateCollection ? PipelineType.OutputEnumerate : PipelineType.Output));
    }

    /// <summary>
    /// Writes an error record to the error pipeline.
    /// </summary>
    public new void WriteError(ErrorRecord errorRecord) {
        ThrowIfStopped();
        _currentOutPipe?.Add((errorRecord, PipelineType.Error));
    }

    /// <summary>
    /// Writes a warning message to the warning pipeline.
    /// </summary>
    public new void WriteWarning(string message) {
        ThrowIfStopped();
        _currentOutPipe?.Add((message, PipelineType.Warning));
    }

    /// <summary>
    /// Writes a verbose message to the verbose pipeline.
    /// </summary>
    public new void WriteVerbose(string message) {
        ThrowIfStopped();
        _currentOutPipe?.Add((message, PipelineType.Verbose));
    }

    /// <summary>
    /// Writes a debug message to the debug pipeline.
    /// </summary>
    public new void WriteDebug(string message) {
        ThrowIfStopped();
        _currentOutPipe?.Add((message, PipelineType.Debug));
    }

    /// <summary>
    /// Writes an information record to the information pipeline.
    /// </summary>
    public new void WriteInformation(InformationRecord informationRecord) {
        ThrowIfStopped();
        _currentOutPipe?.Add((informationRecord, PipelineType.Information));
    }

    /// <summary>
    /// Writes a progress record to the progress pipeline.
    /// </summary>
    public new void WriteProgress(ProgressRecord progressRecord) {
        ThrowIfStopped();
        _currentOutPipe?.Add((progressRecord, PipelineType.Progress));
    }

    /// <summary>
    /// Throws if the cmdlet has been stopped.
    /// </summary>
    protected void ThrowIfStopped() => CancelToken.ThrowIfCancellationRequested();

    /// <inheritdoc />
    public void Dispose() {
        _cancelSource.Cancel();
        _cancelSource.Dispose();
    }
}
