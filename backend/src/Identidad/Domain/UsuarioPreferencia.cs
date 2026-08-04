using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Preferencias de UI y sesión de un <see cref="Usuario"/>. Relación 1:1
/// (unique index en UsuarioId). Marcada como <see cref="INotAudited"/>:
/// cambios cosméticos (tema, idioma) no aportan valor al audit_log y
/// generarían ruido. La empresa actual del usuario tampoco se audita aquí
/// — el audit_log ya registra la empresa de cada operación.
/// </summary>
public sealed class UsuarioPreferencia : BaseEntity, INotAudited
{
    public Guid UsuarioId { get; private set; }

    /// <summary>
    /// Última empresa que el usuario seleccionó. Usada al login para
    /// auto-seleccionar el contexto de empresa cuando tiene acceso a
    /// varias (ADR-0007).
    /// </summary>
    public Guid? UltimaEmpresaId { get; private set; }

    public string IdiomaUI { get; private set; } = "es-MX";
    public string? ZonaHoraria { get; private set; } // null → usa default America/Mexico_City (ADR-0013)
    public string TemaUI { get; private set; } = "system"; // "light" | "dark" | "system"

    private UsuarioPreferencia() { } // EF Core

    public UsuarioPreferencia(Guid id, Guid usuarioId) : base(id)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("UsuarioId es requerido.", nameof(usuarioId));

        UsuarioId = usuarioId;
    }

    public void RegistrarUltimaEmpresa(Guid empresaId)
    {
        if (empresaId == Guid.Empty)
            throw new ArgumentException("EmpresaId es requerido.", nameof(empresaId));

        UltimaEmpresaId = empresaId;
    }

    public void CambiarIdioma(string idioma)
    {
        if (string.IsNullOrWhiteSpace(idioma))
            throw new ArgumentException("Idioma es requerido.", nameof(idioma));

        IdiomaUI = idioma;
    }

    public void CambiarTema(string tema)
    {
        if (tema is not ("light" or "dark" or "system"))
            throw new ArgumentException(
                $"Tema inválido: '{tema}'. Valores aceptados: 'light', 'dark', 'system'.",
                nameof(tema));

        TemaUI = tema;
    }
}
