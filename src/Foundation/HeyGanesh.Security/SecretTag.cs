namespace HeyGanesh.Security;

/// <summary>
/// Marks a field, property, or parameter as containing secret material. The redacting
/// logger uses this to suppress plaintext emission in diagnostics. Tagged values are
/// never logged; only their presence (redacted) is acknowledged.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class SecretTagAttribute : Attribute
{
}