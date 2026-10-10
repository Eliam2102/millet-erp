namespace Millet.SharedKernel.Application.Adjuntos;

/// <summary>Operación sobre adjuntos; decide qué permiso del propietario aplica.</summary>
public enum AdjuntoOperacion
{
    Ver = 1,
    Subir = 2,
    Baja = 3,
}

/// <summary>
/// Datos de la entidad dueña de los adjuntos que el servicio genérico necesita
/// para autorizar. <see cref="EmpresaId"/> nulo = entidad cross-empresa
/// (p. ej. Proveedor); <see cref="SucursalId"/> nulo = sin alcance por sucursal.
/// </summary>
/// <param name="PuedeSubir">False si la entidad no admite nuevos adjuntos (p. ej. proveedor inactivo).</param>
/// <param name="EsPersonaMoral">Solo aplica a entidades con tipo de persona; nulo si no aplica.</param>
/// <param name="Etiqueta">Texto legible para auditoría (p. ej. "P001 · Vidrios SA").</param>
public sealed record AdjuntoPropietarioInfo(
    Guid EntidadId,
    Guid? EmpresaId,
    Guid? SucursalId,
    bool PuedeSubir,
    bool? EsPersonaMoral,
    string Etiqueta);

/// <summary>
/// Propietario de adjuntos (F1-ADM-11 G1.2): cada módulo que adjunta archivos a
/// una entidad registra una implementación por DI. Hereda la autorización del
/// padre: permisos por operación + existencia + alcance (contrato adm-11 §3).
/// Orden aplicado por el servicio: permiso de rol, existencia del padre,
/// alcance, operación.
/// </summary>
public interface IAdjuntoPropietario
{
    /// <summary>Entidad dueña en snake_case (<c>proveedor</c>); coincide con <c>Adjunto.TipoEntidad</c>.</summary>
    string TipoEntidad { get; }

    /// <summary>Código de error 404 cuando el padre no existe (p. ej. <c>PROVEEDOR_NO_ENCONTRADO</c>).</summary>
    string CodigoNoEncontrado { get; }

    string PermisoVer { get; }

    string PermisoSubir { get; }

    string PermisoBaja { get; }

    /// <summary>Resuelve el padre; <c>null</c> si no existe (o no es visible para la empresa actual).</summary>
    Task<AdjuntoPropietarioInfo?> ResolverAsync(Guid entidadId, CancellationToken cancellationToken);

    /// <summary>
    /// Alcance territorial sobre el padre (p. ej. <c>SucursalScopeGuard</c> si tiene sucursal).
    /// Lanza <c>ForbiddenException</c> si el usuario no tiene alcance; sin sucursal no hace nada.
    /// </summary>
    Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, CancellationToken cancellationToken);
    Task VerificarAlcanceAsync(AdjuntoPropietarioInfo info, AdjuntoOperacion operacion, CancellationToken cancellationToken)
        => VerificarAlcanceAsync(info, cancellationToken);
}
