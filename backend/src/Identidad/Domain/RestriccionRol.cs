using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Identidad.Domain;

/// <summary>
/// Restricción de Segregación de Funciones (SoD): par de roles incompatibles
/// que un mismo usuario NO puede tener simultáneamente en la misma empresa.
/// Ejemplo: "facturador" + "auditor" en una empresa permitiría auditarse a
/// uno mismo.
///
/// Los pares se almacenan normalizados (RolAId &lt; RolBId vía
/// <see cref="Guid.CompareTo(Guid)"/>) para que no haya duplicados como
/// (A,B) y (B,A) en la tabla.
///
/// Solo aplica cuando <see cref="Habilitada"/> es <c>true</c> — permite
/// "apagar" temporalmente una restricción sin perder el registro histórico
/// (ADR-0008).
/// </summary>
public sealed class RestriccionRol : BaseEntity, IAuditable
{
    public Guid RolAId { get; private set; }
    public Guid RolBId { get; private set; }
    public string Razon { get; private set; } = string.Empty;
    public bool Habilitada { get; private set; } = true;

    private RestriccionRol() { } // EF Core

    public RestriccionRol(Guid id, Guid rolAId, Guid rolBId, string razon) : base(id)
    {
        if (rolAId == Guid.Empty)
            throw new ArgumentException("RolAId es requerido.", nameof(rolAId));
        if (rolBId == Guid.Empty)
            throw new ArgumentException("RolBId es requerido.", nameof(rolBId));
        if (rolAId == rolBId)
            throw new ArgumentException("RolA y RolB no pueden ser el mismo rol.", nameof(rolBId));
        if (string.IsNullOrWhiteSpace(razon))
            throw new ArgumentException("Razon es requerida.", nameof(razon));

        // Normalizar para que RolAId < RolBId siempre. Evita duplicados
        // (A,B) vs (B,A) y simplifica queries.
        if (rolAId.CompareTo(rolBId) > 0)
        {
            (rolAId, rolBId) = (rolBId, rolAId);
        }

        RolAId = rolAId;
        RolBId = rolBId;
        Razon = razon;
    }

    public void Habilitar() => Habilitada = true;
    public void Deshabilitar() => Habilitada = false;
}
