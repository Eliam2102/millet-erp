namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura síncrono hacia Activos Fijos: estado de los activos creados desde una
/// línea de recepción de activos. Lo implementa ActivosFijos.Infrastructure; sin adapter el
/// NoOp falla cerrado (no se puede anular lo que no se puede verificar).
/// </summary>
public interface IActivoRecepcionReadPort
{
    Task<EstadoActivosRecepcion> ConsultarAsync(Guid empresaId, Guid recepcionId, Guid lineaOcId, CancellationToken cancellationToken);
}

/// <summary>Capitalizados = activos ya fuera de «por capitalizar» (o con póliza); Facturados = conciliados con factura de proveedor.</summary>
public sealed record EstadoActivosRecepcion(int Capitalizados, int Facturados);
