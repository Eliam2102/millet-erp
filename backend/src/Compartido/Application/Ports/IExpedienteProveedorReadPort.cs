namespace Millet.Compartido.Application.Ports;

/// <summary>
/// Resumen del expediente documental de un proveedor. <c>Completo</c> = todos los documentos obligatorios
/// aplicables (acta constitutiva solo si es persona moral) vigentes o por vencer. Listas de códigos de
/// tipo de documento (p. ej. <c>contrato</c>).
/// </summary>
public sealed record ExpedienteProveedorResumen(
    Guid ProveedorId,
    bool Completo,
    IReadOnlyList<string> Faltantes,
    IReadOnlyList<string> Vencidos,
    IReadOnlyList<string> PorVencer);

/// <summary>
/// Puerto de lectura del expediente del proveedor (F1-ADM-11 G1.2). Lo consume CxP para validar el
/// expediente antes de pasar un proveedor a Activo (G1.1/CA2.2) sin tocar tablas de Compartido.
/// Sin autorización por usuario: es lectura inter-módulo de confianza.
/// </summary>
public interface IExpedienteProveedorReadPort
{
    /// <summary>Resumen del expediente; <c>null</c> si el proveedor no existe.</summary>
    Task<ExpedienteProveedorResumen?> ObtenerAsync(Guid proveedorId, CancellationToken cancellationToken);
}
