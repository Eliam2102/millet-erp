using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Excepción de permiso por <c>(usuario, empresa)</c> sobre la base de su rol
/// (ADR-0053). Tabla <c>identidad.usuario_permiso_overrides</c>. Un permiso
/// tiene un solo <see cref="Efecto"/> por usuario/empresa (índice único).
/// Multi-tenant vía <see cref="IPerteneceAEmpresa"/> (ADR-0011).
/// </summary>
public sealed class UsuarioPermisoOverride : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public const int MotivoMaxLength = 500;

    public Guid UsuarioId { get; private set; }
    public Guid EmpresaId { get; set; } // public set requerido por IPerteneceAEmpresa
    public Guid PermisoId { get; private set; }
    public EfectoPermiso Efecto { get; private set; }
    public string? Motivo { get; private set; }

    /// <summary>Quién configuró la excepción (<c>null</c> si fue el sistema).</summary>
    public Guid? AsignadoPorUsuarioId { get; private set; }

    private UsuarioPermisoOverride() { } // EF Core

    public UsuarioPermisoOverride(
        Guid id,
        Guid usuarioId,
        Guid empresaId,
        Guid permisoId,
        EfectoPermiso efecto,
        string? motivo = null,
        Guid? asignadoPorUsuarioId = null) : base(id)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("UsuarioId es requerido.", nameof(usuarioId));
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId es requerido.", nameof(empresaId));
        if (permisoId == Guid.Empty)
            throw new ArgumentException("PermisoId es requerido.", nameof(permisoId));
        if (!Enum.IsDefined(efecto))
            throw new ArgumentException("Efecto inválido.", nameof(efecto));
        if (motivo is { Length: > MotivoMaxLength })
            throw new ArgumentException(
                $"Motivo excede {MotivoMaxLength} caracteres.", nameof(motivo));

        UsuarioId = usuarioId;
        EmpresaId = empresaId;
        PermisoId = permisoId;
        Efecto = efecto;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        AsignadoPorUsuarioId = asignadoPorUsuarioId;
    }

    /// <summary>Cambia efecto/motivo de una excepción existente.</summary>
    public void Actualizar(EfectoPermiso efecto, string? motivo, Guid? asignadoPorUsuarioId)
    {
        if (!Enum.IsDefined(efecto))
            throw new ArgumentException("Efecto inválido.", nameof(efecto));
        if (motivo is { Length: > MotivoMaxLength })
            throw new ArgumentException(
                $"Motivo excede {MotivoMaxLength} caracteres.", nameof(motivo));

        Efecto = efecto;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        AsignadoPorUsuarioId = asignadoPorUsuarioId;
    }
}
