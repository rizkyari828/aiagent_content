namespace AIStudio.Application.Creative;

/// <summary>
/// Raised when an approved idea cannot be directed, or when the model response is
/// not valid structured creative data. The <see cref="Code"/> is stable and safe
/// to surface to future callers; model output is never executed.
/// </summary>
public sealed class CreativeDirectionException : Exception
{
    public CreativeDirectionException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
