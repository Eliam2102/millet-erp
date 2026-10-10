namespace Millet.CuentasPorPagar.Domain.Ports.Contabilidad;

/// <summary>Consulta de periodos ordinarios por fecha. Inexistente o no abierto rechaza movimientos (ADR-0059).</summary>
public interface IPeriodoContablePort
{
    Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct);
}
