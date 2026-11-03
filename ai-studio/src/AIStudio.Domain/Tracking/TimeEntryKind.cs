namespace AIStudio.Domain.Tracking;

/// <summary>
/// Who performed the recorded work. Human and machine time are never added together
/// into one number: they mean different things and are reported separately.
/// </summary>
public enum TimeEntryKind
{
    Human,
    Machine
}
