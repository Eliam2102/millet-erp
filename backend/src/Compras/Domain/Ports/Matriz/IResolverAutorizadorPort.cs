namespace Millet.Compras.Domain.Ports.Matriz;

/// <summary>
/// Resuelve qué usuario debe firmar cada nivel de una requisición según la
/// matriz vigente en <c>compras.aprobadores_departamento</c> (C3.1, F-09):
/// <list type="bullet">
///   <item>Nivel 1 → <c>JefeDpto</c> del departamento del solicitante.</item>
///   <item>Nivel 2 → <c>JefeAlmacen</c> de la sucursal de la requisición.</item>
/// </list>
///
/// <para>
/// Devuelve <c>null</c> si no hay aprobador vigente asignado. Política de
/// <c>AutorizarRequisicionHandler</c> (y por ende de la autorización masiva):
/// <b>fail-open</b> — sin aprobador configurado basta el permiso canónico del
/// nivel, para no bloquear la operación mientras Millet no entrega la matriz
/// real. Con aprobador configurado, solo él puede firmar
/// (<c>REQUISICION_AUTORIZADOR_NO_FACULTADO</c>, 403); el modelo no tiene
/// suplentes/delegados todavía y no hay bypass para super-admin.
/// </para>
/// </summary>
public interface IResolverAutorizadorPort
{
    Task<Guid?> ResolverN1Async(Requisicion requisicion, CancellationToken cancellationToken = default);

    Task<Guid?> ResolverN2Async(Requisicion requisicion, CancellationToken cancellationToken = default);
}
