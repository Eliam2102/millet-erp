using Millet.Almacen.Domain.Ports.Notificaciones;
using Millet.SharedKernel.Application.Integration;

namespace Millet.Almacen.Application.Integration;

/// <summary>Aviso a Finanzas. Almacenes conserva todas las dimensiones de un conteo transversal.</summary>
public sealed record ConteoAjusteNivel3AprobadoEvent(
    Guid EmpresaId, DateTimeOffset OcurridoEn, Guid ConteoId,
    Guid? AlmacenId, Guid? SucursalId, decimal MontoNetoMxn, Guid AprobadorId,
    IReadOnlyList<ConteoAlmacenSucursal> Almacenes)
    : IntegrationEvent("almacen.conteo.ajuste_nivel3.aprobado.v1", EmpresaId, OcurridoEn);
