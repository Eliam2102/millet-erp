using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.CrearRequisicion;

/// <summary>
/// Comando para crear una requisición en estado <see cref="EstadoRequisicion.Borrador"/>.
///
/// El cliente NO envía <c>EmpresaId</c> ni <c>CreadorId</c>: ambos los
/// resuelve el handler desde el JWT (<c>ICurrentEmpresaContext.Current</c>
/// y <c>ICurrentUserContext.UserId</c>). Esto evita inyección cross-tenant
/// y suplantación del creador.
///
/// <see cref="RequisitanteId"/> es opcional. Si <c>null</c>, el handler usa
/// el current user como requisitante. Si el cliente lo envía distinto al
/// current user, debe tener el permiso
/// <c>compras.requisiciones.seleccionar-requisitante</c> (delegación) — el
/// handler valida y lanza HTTP 403 en caso contrario.
///
/// El folio se genera atómicamente en el handler combinando
/// <see cref="SucursalCodigo"/> + <see cref="FolioAnio"/> + secuencia de
/// <c>compras.folio_secuencias</c>.
/// </summary>
public sealed record CrearRequisicionCommand(
    Guid SucursalId,
    string SucursalCodigo,
    short FolioAnio,
    Guid DepartamentoId,
    Clasificacion Clasificacion,
    Prioridad Prioridad,
    DateTimeOffset FechaSolicitud,
    DateOnly? FechaEntregaDeseada,
    Guid? ProveedorSugeridoId,
    string? Descripcion,
    Guid? RequisitanteId = null) : IRequest<CrearRequisicionResponse>;
