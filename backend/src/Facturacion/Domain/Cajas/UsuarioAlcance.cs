using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>
/// Concesión de alcance para usuarios de perfil administrativo SIN caja
/// (`[Decisión 12-6]`, 12-cajas.md §4.1). Cada fila otorga una combinación
/// (sucursal?, canal?): <c>SucursalId</c> null = todas las sucursales;
/// <c>CanalVentaId</c> null = todos los canales. El alcance efectivo del
/// usuario es la unión de sus cajas activas y sus filas de esta tabla; con
/// <c>facturacion.caja.leer-todas</c> el alcance es total y esta tabla no
/// aplica.
///
/// <para>
/// Tabla <c>facturacion.usuario_alcance</c>, UNIQUE (empresa_id, usuario_id,
/// sucursal_id, canal_venta_id) con NULLS NOT DISTINCT — dos concesiones
/// idénticas (incluyendo comodines) son la misma fila.
/// </para>
/// </summary>
public sealed class UsuarioAlcance : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid UsuarioId { get; private set; }

    /// <summary>Sucursal concedida; null = todas las sucursales.</summary>
    public Guid? SucursalId { get; private set; }

    /// <summary>Canal de venta concedido; null = todos los canales.</summary>
    public short? CanalVentaId { get; private set; }

    private UsuarioAlcance() { }

    private UsuarioAlcance(Guid id, Guid empresaId, Guid usuarioId, Guid? sucursalId, short? canalVentaId)
        : base(id)
    {
        EmpresaId = empresaId;
        UsuarioId = usuarioId;
        SucursalId = sucursalId;
        CanalVentaId = canalVentaId;
    }

    public static UsuarioAlcance Crear(Guid empresaId, Guid usuarioId, Guid? sucursalId, short? canalVentaId)
    {
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("USUARIO_ALCANCE_USUARIO_INVALIDO", "UsuarioId es obligatorio.");
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("USUARIO_ALCANCE_SUCURSAL_INVALIDA",
                "SucursalId debe ser un id válido u omitirse (null = todas).");
        if (canalVentaId is <= 0)
            throw new BusinessRuleException("USUARIO_ALCANCE_CANAL_INVALIDO",
                "CanalVentaId debe ser positivo u omitirse (null = todos).");
        if (sucursalId is null && canalVentaId is null)
            throw new BusinessRuleException("USUARIO_ALCANCE_COMODIN_TOTAL",
                "Una concesión sin sucursal ni canal equivale a alcance total; usa el permiso facturacion.caja.leer-todas.");

        return new UsuarioAlcance(Guid.CreateVersion7(), empresaId, usuarioId, sucursalId, canalVentaId);
    }
}
