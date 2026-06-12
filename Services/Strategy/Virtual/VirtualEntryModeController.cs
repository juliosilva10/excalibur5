namespace Excalibur5.Services.Strategy.Virtual;

public interface IVirtualEntryModeController
{
    bool IsVirtualMode { get; }
    int CurrentRealCycleId { get; }
    void Start(VirtualEntrySettings settings);
    bool TryReserveVirtualEntry();
    void CancelVirtualEntry();
    bool RecordVirtualResult(bool won);
    bool RecordRealResult(int cycleId, bool won);
}

public sealed class VirtualEntryModeController : IVirtualEntryModeController
{
    private readonly object _sync = new();
    private VirtualEntrySettings _settings = VirtualEntrySettings.Disabled;
    private string _virtualResults = string.Empty;
    private int _consecutiveRealLosses;
    private bool _isVirtualMode;
    private bool _virtualEntryInProgress;
    private int _currentRealCycleId;

    public bool IsVirtualMode
    {
        get
        {
            lock (_sync)
                return _isVirtualMode;
        }
    }

    public int CurrentRealCycleId
    {
        get
        {
            lock (_sync)
                return _currentRealCycleId;
        }
    }

    public void Start(VirtualEntrySettings settings)
    {
        lock (_sync)
        {
            _settings = Normalize(settings);
            _virtualResults = string.Empty;
            _consecutiveRealLosses = 0;
            _currentRealCycleId = 0;
            _isVirtualMode = _settings.Enabled;
            _virtualEntryInProgress = false;
        }
    }

    public bool TryReserveVirtualEntry()
    {
        lock (_sync)
        {
            if (!_isVirtualMode || _virtualEntryInProgress) return false;

            _virtualEntryInProgress = true;
            return true;
        }
    }

    public void CancelVirtualEntry()
    {
        lock (_sync)
            _virtualEntryInProgress = false;
    }

    public bool RecordVirtualResult(bool won)
    {
        lock (_sync)
        {
            if (!_isVirtualMode || !_settings.Enabled || !_virtualEntryInProgress)
                return false;

            _virtualEntryInProgress = false;
            _virtualResults += won ? 'W' : 'L';
            TrimVirtualResults();
            if (!_virtualResults.EndsWith(_settings.TargetSequence, StringComparison.Ordinal))
                return false;

            _isVirtualMode = false;
            _consecutiveRealLosses = 0;
            _currentRealCycleId++;
            return true;
        }
    }

    public bool RecordRealResult(int cycleId, bool won)
    {
        lock (_sync)
        {
            if (!_settings.Enabled || _settings.Tolerance <= 0 ||
                _isVirtualMode || cycleId != _currentRealCycleId)
                return false;

            _consecutiveRealLosses = won ? 0 : _consecutiveRealLosses + 1;
            if (_consecutiveRealLosses < _settings.Tolerance) return false;

            _isVirtualMode = true;
            _virtualResults = string.Empty;
            _consecutiveRealLosses = 0;
            return true;
        }
    }

    private void TrimVirtualResults()
    {
        var maxLength = _settings.TargetSequence.Length;
        if (_virtualResults.Length > maxLength)
            _virtualResults = _virtualResults[^maxLength..];
    }

    private static VirtualEntrySettings Normalize(VirtualEntrySettings settings)
    {
        var sequence = new string(settings.TargetSequence
            .Where(character => character is 'W' or 'L' or 'w' or 'l')
            .Select(char.ToUpperInvariant)
            .ToArray());

        return new VirtualEntrySettings(sequence, settings.Tolerance);
    }
}
