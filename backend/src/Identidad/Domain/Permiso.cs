using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Acción atómica que el sistema sabe autorizar. Convención del código:
/// <c>{modulo}.{recurso}.{accion}</c> — por ejemplo <c>fiscal.cfdi.timbrar</c>,
/// <c>identidad.usuarios.crear</c>. El catálogo se carga desde
/// <see cref="PermisosCanonicos"/> en la migración (seed). Las
/// reasignaciones a roles viven en <see cref="RolPermiso"/>.
/// Ver ADR-0007.
/// </summary>
public sealed class Permiso : BaseEntity, IAuditable
{
    public string Codigo { get; private set; } = string.Empty;
    public string Modulo { get; private set; } = string.Empty;
    public string Recurso { get; private set; } = string.Empty;
    public string Accion { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;

    private Permiso() { } // EF Core

    public Permiso(Guid id, string codigo, string descripcion) : base(id)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("Codigo es requerido.", nameof(codigo));
        if (string.IsNullOrWhiteSpace(descripcion))
            throw new ArgumentException("Descripcion es requerida.", nameof(descripcion));

        var partes = codigo.Split('.');
        if (partes.Length != 3 || partes.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException(
                $"Codigo de permiso inválido: '{codigo}'. Formato esperado: 'modulo.recurso.accion'.",
                nameof(codigo));

        Codigo = codigo;
        Modulo = partes[0];
        Recurso = partes[1];
        Accion = partes[2];
        Descripcion = descripcion;
    }
}
