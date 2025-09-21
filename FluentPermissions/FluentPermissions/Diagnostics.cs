using Microsoft.CodeAnalysis;

namespace FluentPermissions;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor MissingRegisterMethod = new(
        "FP0001",
        "Registrar missing Register method",
        "The registrar type must implement a Register method with a single parameter",
        "SourceGen",
        DiagnosticSeverity.Warning,
        true);

    public static readonly DiagnosticDescriptor InconsistentOptionsTypes = new(
        "FP0002",
        "Inconsistent options type arguments",
        "All registrars must use the same TGroupOptions and TPermissionOptions generic arguments",
        "SourceGen",
        DiagnosticSeverity.Error,
        true);
}