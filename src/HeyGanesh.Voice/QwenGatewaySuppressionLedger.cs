namespace HeyGanesh.Voice;

/// <summary>
/// The one place that decides which Gateway frames HeyGanesh acts on, and the one place that counts what
/// the frontend generated and we refused to use.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The qwen-audio-agent frontend is a realtime <i>conversational</i> model: it
/// transcribes the user AND answers, in the same turn, whether or not HeyGanesh wants an answer. No
/// supported protocol switch stops it generating (DEC-013 records that investigation and the limitation).
/// So the guarantee that HeyGanesh's Tutor answers the user is enforced here: exactly one frame kind is
/// accepted as the user's words — <see cref="GatewayEventKind.UserFinal"/> — and everything
/// assistant-shaped is counted as suppressed and dropped.
/// </para>
/// <para>
/// <b>Why it is a separate type.</b> Because it is pure. Verification feeds it recorded frames and asserts
/// the policy offline (VO-5/VO-6), so the rule that keeps the Tutor authoritative is testable without a
/// Gateway, a microphone or a network — which on a developer machine is the only way it can be tested.
/// </para>
/// <para>
/// It also owns the two hard guards: a started assistant response is something the caller must cancel
/// (<see cref="GatewayEventKind.AssistantResponseStarted"/>), and a Backend Agent event is a protocol
/// violation outright (<see cref="SawBackendTask"/>).
/// </para>
/// </remarks>
public sealed class GatewaySuppressionLedger
{
    private readonly object _gate = new();

    private long _userPartials;
    private long _userFinals;
    private long _discards;
    private long _assistantPartials;
    private long _assistantFinals;
    private long _assistantAudioChunks;
    private long _toolCalls;
    private long _taskEvents;
    private long _unknownEvents;
    private long _errors;
    private long _cancelsIssued;

    private string? _lastUserFinalText;
    private string? _lastError;

    /// <summary>Interim user speech frames seen.</summary>
    public long UserPartials { get { lock (_gate) return _userPartials; } }

    /// <summary>Committed user transcripts seen — the only frame kind that can become a question.</summary>
    public long UserFinals { get { lock (_gate) return _userFinals; } }

    /// <summary>Transcripts the Gateway withdrew, either role.</summary>
    public long Discards { get { lock (_gate) return _discards; } }

    /// <summary>Assistant text fragments refused.</summary>
    public long AssistantPartials { get { lock (_gate) return _assistantPartials; } }

    /// <summary>Assistant answers refused — the count of times the frontend tried to be the teacher.</summary>
    public long AssistantFinals { get { lock (_gate) return _assistantFinals; } }

    /// <summary>Assistant audio frames refused. Not one of these was ever played.</summary>
    public long AssistantAudioChunks { get { lock (_gate) return _assistantAudioChunks; } }

    /// <summary>Model-facing tool calls (either surface) refused.</summary>
    public long ToolCalls { get { lock (_gate) return _toolCalls; } }

    /// <summary>Backend Agent task events seen. Must stay zero while <c>AGENT_PROTOCOL=none</c>.</summary>
    public long TaskEvents { get { lock (_gate) return _taskEvents; } }

    /// <summary>Frames whose type this module does not recognise.</summary>
    public long UnknownEvents { get { lock (_gate) return _unknownEvents; } }

    /// <summary>Structured Gateway errors seen.</summary>
    public long ErrorEvents { get { lock (_gate) return _errors; } }

    /// <summary>Cancel/interrupt messages this module issued.</summary>
    public long CancelsIssued { get { lock (_gate) return _cancelsIssued; } }

    /// <summary>The last committed user transcript observed. Diagnostics; the result carries the real one.</summary>
    public string? LastUserFinalText { get { lock (_gate) return _lastUserFinalText; } }

    /// <summary>The last Gateway error message observed, verbatim.</summary>
    public string? LastError { get { lock (_gate) return _lastError; } }

    /// <summary>Everything the frontend produced for this session that HeyGanesh discarded.</summary>
    public long SuppressedAssistantEvents
    {
        get { lock (_gate) return _assistantPartials + _assistantFinals + _assistantAudioChunks; }
    }

    /// <summary>True once a Backend Agent event has been seen — the condition under which Voice refuses to continue.</summary>
    public bool SawBackendTask { get { lock (_gate) return _taskEvents > 0; } }

    /// <summary>
    /// Classifies one frame and records it. This is the only way a frame should be read: the client calls
    /// it for every received frame, so counters cannot drift away from what was actually observed.
    /// </summary>
    public GatewayEventDecision Observe(string? frameJson)
    {
        var decision = QwenGatewayProtocol.Classify(frameJson);

        lock (_gate)
        {
            switch (decision.Kind)
            {
                case GatewayEventKind.UserPartial:
                    _userPartials++;
                    break;
                case GatewayEventKind.UserFinal:
                    _userFinals++;
                    _lastUserFinalText = decision.Text;
                    break;
                case GatewayEventKind.TranscriptDiscard:
                    _discards++;
                    break;
                case GatewayEventKind.AssistantPartial:
                    _assistantPartials++;
                    break;
                case GatewayEventKind.AssistantFinal:
                    _assistantFinals++;
                    break;
                case GatewayEventKind.AssistantAudio:
                    _assistantAudioChunks++;
                    break;
                case GatewayEventKind.ToolCall:
                    _toolCalls++;
                    break;
                case GatewayEventKind.TaskEvent:
                    _taskEvents++;
                    break;
                case GatewayEventKind.Unknown:
                    _unknownEvents++;
                    break;
                case GatewayEventKind.Error:
                    _errors++;
                    _lastError = decision.ErrorMessage;
                    break;
            }
        }

        return decision;
    }

    /// <summary>Counts an outgoing cancel/interrupt, because this counter is the evidence the design did its job.</summary>
    public void RecordCancelIssued()
    {
        lock (_gate)
        {
            _cancelsIssued++;
        }
    }

    /// <summary>One line describing the session's suppression, for a host log or a verification row.</summary>
    public string Describe() =>
        $"suppressed: assistantText={AssistantFinals}+{AssistantPartials} audio={AssistantAudioChunks} " +
        $"tools={ToolCalls} unknown={UnknownEvents} cancels={CancelsIssued} tasks={TaskEvents}";
}
