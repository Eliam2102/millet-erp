using MediatR;
using Millet.Compras.Domain;

namespace Millet.Compras.Application.Listar;

/// <summary>
/// Query de bandeja general. Aplica el global query filter de empresa
/// (ADR-0011) automáticamente. Filtros opcionales soportados por los
/// índices §10.1: estado, departamentoId, requisitanteId.
///
/// Paginación offset-based con tope de 200 para evitar cargas costosas
/// (default 50). Orden: <c>FechaSolicitud DESC</c> para coincidir con
/// el índice <c>(empresa_id, estado, fecha_solicitud DESC)</c>.
/// </summary>
public sealed record ListarRequisicionesQuery(
    EstadoRequisicion? Estado = null,
    Guid? DepartamentoId = null,
    Guid? RequisitanteId = null,
    int Offset = 0,
    int Limit = 50,
    string? Q = null) : IRequest<PagedResponse<RequisicionListItemResponse>>, Millet.SharedKernel.Application.ISucursalScopedQuery
{
    public string PermisoTodasSucursales => "compras.requisiciones.leer-todas-sucursales";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }

    /// <summary>
    /// Alcance por departamento (F-RQOC-01): lo fija el endpoint cuando el usuario no ve todos los
    /// departamentos. Null = sin restricción.
    /// </summary>
    public AlcanceDepartamentoRq? AlcanceDepartamento { get; init; }
}

/// <summary>
/// Quien no tiene <c>compras.requisiciones.ver-todos-departamentos</c> solo ve las RQ de su departamento
/// y las que pidió o capturó (doc 05 §10.4, ayuda de Compras).
/// </summary>
public sealed record AlcanceDepartamentoRq(Guid UsuarioId, Guid? DepartamentoId)
{
    public System.Linq.Expressions.Expression<Func<Requisicion, bool>> Visible()
    {
        var usuario = UsuarioId;
        var depto = DepartamentoId;
        return r => r.RequisitanteId == usuario || r.CreadorId == usuario
            || (depto != null && r.DepartamentoId == depto);
    }
}
