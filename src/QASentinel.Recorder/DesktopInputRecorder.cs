using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using QASentinel.Automation.Abstractions;
using QASentinel.Automation.Models;

namespace QASentinel.Recorder;

public sealed class DesktopInputRecorder(
    IPointerInputSource pointerSource,
    IAutomationEngine automationEngine,
    ILogger<DesktopInputRecorder> logger) : IDesktopInputRecorder
{
    private readonly object _sync = new();
    private readonly List<RecordedStep> _steps = [];
    private readonly RecorderFieldPolicy _fieldPolicy = new();
    private Channel<RawPointerEvent>? _events;
    private CancellationTokenSource? _recordingCancellation;
    private Task? _processingTask;
    private Guid? _sessionId;
    private string _statusMessage = "Recorder inativo.";
    private bool _isRecording;

    public RecorderSnapshot Current
    {
        get
        {
            lock (_sync)
            {
                return new RecorderSnapshot(
                    _isRecording,
                    _sessionId,
                    _steps.ToArray(),
                    _statusMessage);
            }
        }
    }

    public async Task StartAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var session = automationEngine.GetSession(sessionId);
        lock (_sync)
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("Já existe uma gravação em andamento.");
            }

            _steps.Clear();
            _sessionId = sessionId;
            _statusMessage = "Recorder iniciando.";
            _events = Channel.CreateUnbounded<RawPointerEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
            _recordingCancellation = new CancellationTokenSource();
        }

        pointerSource.EventReceived += OnPointerEvent;
        try
        {
            await pointerSource.StartAsync(session.ProcessId, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                _isRecording = true;
                _statusMessage = "Gravando cliques e preenchimentos da aplicação conectada.";
                _processingTask = ProcessEventsAsync(
                    sessionId,
                    _events.Reader,
                    _recordingCancellation.Token);
            }

            logger.LogInformation(
                "Operation=StartRecorder SessionId={SessionId} ProcessId={ProcessId} Result=Success",
                sessionId,
                session.ProcessId);
        }
        catch
        {
            pointerSource.EventReceived -= OnPointerEvent;
            await ResetAfterStopAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? processingTask;
        lock (_sync)
        {
            if (!_isRecording && _processingTask is null)
            {
                _statusMessage = "Recorder inativo.";
                return;
            }

            _isRecording = false;
            _statusMessage = "Finalizando gravação.";
            processingTask = _processingTask;
        }

        pointerSource.EventReceived -= OnPointerEvent;
        await pointerSource.StopAsync(cancellationToken).ConfigureAwait(false);
        _events?.Writer.TryComplete();
        if (processingTask is not null)
        {
            await processingTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        lock (_sync)
        {
            _statusMessage = $"Gravação finalizada com {_steps.Count} passo(s).";
            _processingTask = null;
        }

        _recordingCancellation?.Dispose();
        _recordingCancellation = null;
        _events = null;
        logger.LogInformation(
            "Operation=StopRecorder SessionId={SessionId} StepCount={StepCount} Result=Success",
            _sessionId,
            Current.Steps.Count);
    }

    private async Task ProcessEventsAsync(
        Guid sessionId,
        ChannelReader<RawPointerEvent> reader,
        CancellationToken cancellationToken)
    {
        var normalizer = new PointerClickNormalizer();
        PendingFill? pendingFill = null;
        await foreach (var input in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            AutomationElementInfo? element = null;
            if (input.Kind == PointerEventKind.Down)
            {
                if (pendingFill is not null)
                {
                    await CompletePendingFillAsync(sessionId, pendingFill, cancellationToken).ConfigureAwait(false);
                    pendingFill = null;
                }

                try
                {
                    element = await automationEngine.InspectActionableAtAsync(
                        sessionId,
                        input.X,
                        input.Y,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "Operation=ResolveRecordedClick SessionId={SessionId} X={X} Y={Y} Result=Ignored",
                        sessionId,
                        input.X,
                        input.Y);
                }

                if (element is not null)
                {
                    pendingFill = await TryStartFillAsync(sessionId, element, cancellationToken).ConfigureAwait(false);
                    if (pendingFill is not null)
                    {
                        element = null;
                    }
                }
            }

            var normalized = normalizer.Process(input, element);
            if (normalized is not null)
            {
                AddStep(normalized);
            }
        }

        if (pendingFill is not null)
        {
            await CompletePendingFillAsync(sessionId, pendingFill, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<PendingFill?> TryStartFillAsync(
        Guid sessionId,
        AutomationElementInfo element,
        CancellationToken cancellationToken)
    {
        if (!_fieldPolicy.CanTrack(element))
        {
            if (_fieldPolicy.IsSensitiveMetadata(element))
            {
                LogSensitiveFieldSkipped(element);
            }

            return null;
        }

        try
        {
            var value = await automationEngine.GetValueAsync(
                sessionId,
                element.Locators,
                cancellationToken).ConfigureAwait(false);
            if (value.IsPassword)
            {
                LogSensitiveFieldSkipped(element);
                return null;
            }

            if (!value.IsSupported || value.IsReadOnly)
            {
                return null;
            }

            lock (_sync)
            {
                _statusMessage = $"Monitorando preenchimento de {GetDisplayName(element)}.";
            }

            return new PendingFill(element, value.Value ?? string.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(
                exception,
                "Operation=StartSemanticFill Element={Element} Result=Ignored",
                GetDisplayName(element));
            return null;
        }
    }

    private async Task CompletePendingFillAsync(
        Guid sessionId,
        PendingFill pending,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await automationEngine.GetValueAsync(
                sessionId,
                pending.Element.Locators,
                cancellationToken).ConfigureAwait(false);
            var finalValue = value.Value ?? string.Empty;
            if (!value.IsSupported ||
                value.IsReadOnly ||
                value.IsPassword ||
                string.Equals(finalValue, pending.InitialValue, StringComparison.Ordinal))
            {
                return;
            }

            if (!_fieldPolicy.CanStore(finalValue))
            {
                logger.LogWarning(
                    "Operation=CompleteSemanticFill Element={Element} Length={Length} Result=IgnoredTooLong",
                    GetDisplayName(pending.Element),
                    finalValue.Length);
                return;
            }

            AddStep(new NormalizedRecordedAction(
                RecordedActionType.Fill,
                pending.Element,
                finalValue,
                DateTimeOffset.UtcNow));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Operation=CompleteSemanticFill Element={Element} Result=Ignored",
                GetDisplayName(pending.Element));
        }
    }

    private void LogSensitiveFieldSkipped(AutomationElementInfo element) => logger.LogInformation(
        "Operation=SemanticFill Element={Element} Result=IgnoredSensitiveField",
        GetDisplayName(element));

    private void AddStep(NormalizedRecordedAction action)
    {
        lock (_sync)
        {
            var element = action.Element;
            _steps.Add(new RecordedStep(
                Guid.NewGuid(),
                _steps.Count + 1,
                action.Action,
                GetDisplayName(element),
                element.ClassName,
                element.ControlType,
                element.Locators,
                action.Input,
                action.CapturedAt));
            _statusMessage = $"{_steps.Count} passo(s) capturado(s).";
        }
    }

    private void OnPointerEvent(RawPointerEvent input)
    {
        ChannelWriter<RawPointerEvent>? writer;
        lock (_sync)
        {
            writer = _isRecording ? _events?.Writer : null;
        }

        _ = writer?.TryWrite(input);
    }

    private async Task ResetAfterStopAsync()
    {
        try
        {
            await pointerSource.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        lock (_sync)
        {
            _isRecording = false;
            _sessionId = null;
            _statusMessage = "Recorder inativo.";
            _events = null;
            _processingTask = null;
        }

        _recordingCancellation?.Dispose();
        _recordingCancellation = null;
    }

    private static string GetDisplayName(AutomationElementInfo element) =>
        FirstNonEmpty(element.Name, element.AutomationId, element.ClassName, element.ControlType) ?? "Elemento sem nome";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record PendingFill(AutomationElementInfo Element, string InitialValue);

    public async ValueTask DisposeAsync()
    {
        if (Current.IsRecording)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await StopAsync(timeout.Token).ConfigureAwait(false);
        }

        await pointerSource.DisposeAsync().ConfigureAwait(false);
    }
}
