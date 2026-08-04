using Millet.SharedKernel.Application.Integration;

namespace Millet.Administracion.Application.Events;

/// <summary>
/// Integration event v1: sucursal creada (F-Admin-PR2.3). EventType:
/// <c>admin.sucursal.creada.v1</c>.
///
/// <para>
/// En MVP single-tenant, EmpresaId del evento se rellena con
/// <see cref="Guid.Empty"/> porque la Sucursal no tiene EmpresaId todavía
/// (decisión: no agregar columna hasta multi-empresa real).
/// </para>
/// <para>
/// PLATFORM-TODO(&lt;AdminOutbox&gt;): ver nota en
/// <see cref="EmpresaCreadaEvent"/>.
/// </para>
/// </summary>
public sealed record SucursalCreadaEvent(
    Guid SucursalId,
    string Clave,
    string Nombre,
    DateTimeOffset OcurridoEn)
    : IntegrationEvent("admin.sucursal.creada.v1", Guid.Empty, OcurridoEn);
