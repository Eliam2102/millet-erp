using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain;

/// <summary>
/// Configuración del módulo Compras por empresa. Una fila por empresa,
/// con valores tipados explícitos (no key-value genérico).
///
/// <para>
/// <b><see cref="AutoGenerarOcAlAutorizar"/></b>: gobierna el flujo de
/// autorización de Requisiciones (RQ) cuando hay saldo de compra:
/// </para>
///
/// <list type="bullet">
///   <item><c>true</c> — el handler de Autorizar llama síncrono a
///         <c>IGenerarSolicitudCompraPort</c> para generar una OC
///         borrador. La RQ queda comprometida implícitamente. Es la
///         narrativa "automática" del diseño original (RQ §A3).</item>
///   <item><c>false</c> (default) — el handler de Autorizar deja la RQ
///         en estado <c>EnSurtido</c> con <c>ComprometidaEnOcId=null</c>.
///         El comprador convierte manualmente vía
///         <c>POST /api/v1/compras/ordenes/desde-requisicion</c> (1:1)
///         o vía el Sheet "Nueva OC" con consolidación N:1. Es la
///         narrativa del diseño OC §3.bis.1.</item>
/// </list>
///
/// <para>
/// Ver decisión arquitectónica en <c>docs/decisiones/0032-auto-generar-oc-al-autorizar.md</c>.
/// </para>
/// </summary>
public sealed class ComprasSettings : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public bool AutoGenerarOcAlAutorizar { get; private set; }

    /// <summary>Constructor de EF Core.</summary>
    private ComprasSettings() { }

    public ComprasSettings(Guid id, Guid empresaId, bool autoGenerarOcAlAutorizar)
        : base(id)
    {
        if (id == Guid.Empty)
            throw new BusinessRuleException("COMPRAS_SETTINGS_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("COMPRAS_SETTINGS_EMPRESA_INVALIDA", "EmpresaId es obligatorio.");

        EmpresaId = empresaId;
        AutoGenerarOcAlAutorizar = autoGenerarOcAlAutorizar;
    }

    /// <summary>
    /// Actualiza el flag de auto-generación. Idempotente — re-aplicar
    /// el mismo valor no produce side effects (el interceptor de audit
    /// igual lo registra; la concurrencia EF gestiona Version).
    /// </summary>
    public void EstablecerAutoGenerarOcAlAutorizar(bool valor)
    {
        AutoGenerarOcAlAutorizar = valor;
    }

    /// <summary>
    /// Factory para construir el row default cuando una empresa nueva
    /// se aprovisiona. El default es <c>AutoGenerarOcAlAutorizar=false</c>
    /// (narrativa manual; ver decisión de owner 2026-05-13).
    /// </summary>
    public static ComprasSettings CrearDefault(Guid empresaId)
    {
        return new ComprasSettings(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            autoGenerarOcAlAutorizar: false);
    }
}
