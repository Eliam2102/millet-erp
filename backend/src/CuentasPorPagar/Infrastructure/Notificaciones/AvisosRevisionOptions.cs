using System.Net.Mail;
using Millet.CuentasPorPagar.Domain.Notificaciones;

namespace Millet.CuentasPorPagar.Infrastructure.Notificaciones;

/// <summary>
/// Destinatarios de los avisos de SLA de facturas en revisión (SRC-093, parte 2).
/// Globales (no por empresa). Los correos reales van por variable de entorno:
/// <c>CuentasPorPagar__AvisosRevision__ContadorGeneral__0</c>.
/// </summary>
public sealed class AvisosRevisionOptions
{
    public const string SectionName = "CuentasPorPagar:AvisosRevision";

    /// <summary>"NoOp" (solo log) o "Graph" (correo desde el buzón del ERP).</summary>
    public string Proveedor { get; init; } = "NoOp";

    public string[] ContadorGeneral { get; init; } = [];
    public string[] Compras { get; init; } = [];

    /// <summary>
    /// Contador General siempre; Compras si el motivo la involucra. Sin repetidos.
    /// Un correo mal escrito se descarta: con Graph haría fallar el envío para todos.
    /// </summary>
    public IReadOnlyList<string> Destinatarios(SlaRevisionNotificacion n) =>
        (n.AvisaCompras ? ContadorGeneral.Concat(Compras) : ContadorGeneral)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Where(c => MailAddress.TryCreate(c, out var m) && m.Address == c)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
