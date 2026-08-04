namespace Millet.Almacen.Domain.Ports;

/// <summary>
/// Puerto de lectura hacia Identidad para resolver el <b>usuario de servicio del
/// motor de reorden</b> (ADR-0047 PR5.C) — la identidad M2M que figura como creador
/// y requisitante de las RQ automáticas, y de la que se toma la empresa (mono-empresa).
///
/// <para>
/// Interfaz propiedad de Almacén; el adapter real vive en
/// <c>Millet.Identidad.Infrastructure.PublicAdapters.UsuarioServicioReadAdapter</c>
/// (Identidad es dueño del dato y ya referencia Almacén.Domain; Almacén no referencia
/// Identidad). Cableado en <c>Program.cs</c>.
/// </para>
/// </summary>
public interface IUsuarioServicioReadPort
{
    /// <summary>
    /// Resuelve el usuario de servicio "Reabastecimiento Automático" del reorden.
    /// <c>null</c> si aún no está sembrado (bootstrap no corrió / RFC no resolvió).
    /// </summary>
    Task<UsuarioServicioLectura?> ObtenerReordenAsync(CancellationToken cancellationToken);
}

/// <summary>Proyección read-only del usuario de servicio: su Id (=creador/requisitante) y su empresa.</summary>
public sealed record UsuarioServicioLectura(Guid Id, Guid EmpresaId, bool Activo);
