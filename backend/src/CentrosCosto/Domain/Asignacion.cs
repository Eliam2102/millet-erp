using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CentrosCosto.Domain;

/// <summary>
/// Fila de alcance usuario → máquina (CECO-PR6, 01-diseno §7): el alcance
/// se CONGELA en Dim3 — una columna, sin nodo polimórfico ni CHECKs de
/// combinaciones. Tabla <c>centros_costo.asignaciones</c>,
/// UNIQUE (usuario_id, dim3_id).
///
/// <para>
/// Son hechos de pertenencia a un conjunto, no catálogo: sin estatus,
/// borrado FÍSICO (§7 "inserta/borra hojas") — la regla "nada se borra"
/// del §5 aplica al catálogo. Marcar un nodo superior es un atajo de
/// captura: <c>MarcarAlcanceCommand</c> expande a las Dim3 vivas y guarda
/// ESTAS filas; la regla se tira. Sin re-evaluación en vivo.
/// </para>
/// <para>
/// <see cref="UsuarioId"/> es Guid LÓGICO (molde
/// <c>facturacion.usuario_alcance</c>): sin FK a Identidad — todas las FKs
/// del módulo son internas al esquema (§4). Consecuencia aceptada
/// (§7): usuario borrado en Identidad deja filas huérfanas que nada
/// limpia. <see cref="Dim3Id"/> sí es FK física <c>Restrict</c>.
/// </para>
/// </summary>
public sealed class Asignacion : BaseEntity, IAuditable
{
    public Guid UsuarioId { get; private set; }
    public Guid Dim3Id { get; private set; }

    private Asignacion() { }

    private Asignacion(Guid id, Guid usuarioId, Guid dim3Id) : base(id)
    {
        UsuarioId = usuarioId;
        Dim3Id = dim3Id;
    }

    public static Asignacion Crear(Guid usuarioId, Guid dim3Id)
    {
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("CECO_ASIGNACION_USUARIO_INVALIDO",
                "UsuarioId es obligatorio.");
        if (dim3Id == Guid.Empty)
            throw new BusinessRuleException("CECO_ASIGNACION_DIM3_INVALIDA",
                "Dim3Id es obligatorio.");

        return new Asignacion(Guid.CreateVersion7(), usuarioId, dim3Id);
    }
}
