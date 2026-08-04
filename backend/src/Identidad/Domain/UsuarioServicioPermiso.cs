namespace Millet.Identidad.Domain;

/// <summary>
/// Asignación directa de un permiso canónico a un <see cref="UsuarioServicio"/>.
/// PK compuesta <c>(UsuarioServicioId, PermisoClave)</c> — semánticamente
/// es un set, no necesita un id propio.
///
/// <para>
/// La columna <c>PermisoClave</c> es el código string del permiso
/// (ej. <c>"integraciones.aw.cotizaciones.crear"</c>), NO un FK al id de
/// <c>identidad.permiso</c>. Razón: simetría con los claims que el handler
/// de SP emite directo desde aquí (string-based) y simplicidad operativa
/// (el bootstrap del SP recibe strings desde appsettings y persiste strings).
/// La validación de que la clave existe en el catálogo canónico la hace
/// el bootstrap antes de persistir.
/// </para>
/// </summary>
public sealed class UsuarioServicioPermiso
{
    public Guid UsuarioServicioId { get; private set; }
    public string PermisoClave { get; private set; } = string.Empty;

    private UsuarioServicioPermiso() { } // EF Core

    public UsuarioServicioPermiso(Guid usuarioServicioId, string permisoClave)
    {
        if (usuarioServicioId == Guid.Empty)
            throw new ArgumentException("UsuarioServicioId es requerido.", nameof(usuarioServicioId));
        if (string.IsNullOrWhiteSpace(permisoClave))
            throw new ArgumentException("PermisoClave es requerida.", nameof(permisoClave));

        UsuarioServicioId = usuarioServicioId;
        PermisoClave = permisoClave;
    }
}
