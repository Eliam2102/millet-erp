using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.Domain.Ports.Matriz;

/// <summary>
/// Resuelve qué usuario(s) deben autorizar una requisición específica
/// según las reglas de §3.bis.2 + la matriz vigente en
/// <c>compras.aprobadores_departamento</c> (F9-PR1, F9-PR2).
///
/// <para>
/// Implementación productiva en <c>ResolverAutorizadorService</c>; lee
/// la fila vigente correspondiente al <c>(rol, scope)</c> donde el
/// <c>scope</c> depende del rol:
/// <list type="bullet">
///   <item><c>JefeDpto</c>: scope = <c>requisicion.DepartamentoId</c></item>
///   <item><c>JefeAlmacen</c>: scope = <c>requisicion.AlmacenDestinoId</c></item>
///   <item><c>AutorizadorN2</c>: scope = <c>requisicion.SucursalId</c></item>
/// </list>
/// </para>
///
/// <para>
/// Devuelve <c>null</c> si no hay aprobador vigente asignado para esa
/// combinación. Política del caller (handler de Autorizar, bandeja de
/// "mis pendientes", notificador): decidir si es fail-open (basta el
/// permiso canónico) o fail-closed (rechazar la operación). En F9-PR2
/// el handler sigue siendo permission-only — el port queda disponible
/// para consumidores que requieran filtrar por aprobador específico.
/// </para>
/// </summary>
public interface IResolverAutorizadorPort
{
    /// <summary>
    /// Resuelve el usuario que debe firmar el Nivel 1 según la
    /// naturaleza más restrictiva de las líneas de la requisición:
    /// <list type="bullet">
    ///   <item><c>Estandar</c> → <c>JefeAlmacen</c> del almacén destino.</item>
    ///   <item><c>Servicio</c> / <c>Critico</c> / <c>Riesgo</c> → <c>JefeDpto</c> del departamento del solicitante.</item>
    /// </list>
    /// </summary>
    Task<Guid?> ResolverN1Async(
        Requisicion requisicion,
        Naturaleza naturaleza,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resuelve el usuario que debe firmar el Nivel 2 (siempre por
    /// sucursal en este MVP). En el futuro puede especializarse por
    /// naturaleza (ej. <c>Riesgo químico</c> → gerente de seguridad).
    /// </summary>
    Task<Guid?> ResolverN2Async(
        Requisicion requisicion,
        Naturaleza naturaleza,
        CancellationToken cancellationToken = default);
}
