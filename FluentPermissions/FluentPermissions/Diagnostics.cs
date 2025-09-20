using Microsoft.CodeAnalysis;

namespace FluentPermissions;

internal static class Diagnostics
{
    public static readonly DiagnosticDescriptor MissingRegisterMethod = new(
        id: "FP0001",
        title: "Registrar missing Register method",
        messageFormat: "The registrar type must implement a Register method with a single parameter",
        category: "SourceGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InconsistentOptionsTypes = new(
        id: "FP0002",
        title: "Inconsistent options type arguments",
        messageFormat: "All registrars must use the same TGroupOptions and TPermissionOptions generic arguments",
        category: "SourceGen",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}