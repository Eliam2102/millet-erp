using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain;

/// <summary>
/// Asignación de un usuario como aprobador de un rol específico en un
/// departamento (F9-PR1). Slowly-changing dimension type 2: cuando se
/// designa un nuevo aprobador para un <c>(empresa, depto, rol)</c>, la
/// fila vigente actual cierra (<c>VigenteHasta = now()</c>) y se inserta
/// una fila nueva con <c>VigenteDesde = now()</c>. El histórico queda
/// intacto para auditoría.
///
/// <para>
/// Invariante: para cada <c>(empresa_id, departamento_id, rol)</c> hay
/// a lo más UNA fila con <c>VigenteHasta IS NULL</c>. Garantizado por
/// UNIQUE filtered index en infra; el handler de designar es responsable
/// de cerrar la previa antes de insertar la nueva (TX EF).
/// </para>
///
/// <para>
/// <b>Nota F9-PR2 — semántica polimórfica de <see cref="DepartamentoId"/>:</b>
/// el campo <c>departamento_id</c> actúa como <b>scope_id</b> y su
/// significado depende del <see cref="Rol"/>:
/// <list type="bullet">
///   <item><c>JefeDpto</c> → scope es el <c>departamento_id</c> del solicitante (literal).</item>
///   <item><c>JefeAlmacen</c> → scope es el <c>almacen_destino_id</c> de la requisición (overload).</item>
///   <item><c>AutorizadorN2</c> → scope es la <c>sucursal_id</c> de la requisición (overload).</item>
/// </list>
/// El nombre del campo se mantuvo (no rename físico) para no romper la
/// API expuesta en F9-PR1; la polimorfía la maneja
/// <c>ResolverAutorizadorService</c>. Si el cliente alguna vez requiere
/// semánticas más complejas (por ejemplo <c>JefeAlmacen</c> por turno),
/// considerar tabla específica + ADR.
/// </para>
/// </summary>
public sealed class AprobadorDepartamento
{
    public Guid Id { get; private set; }

    public Guid EmpresaId { get; private set; }

    public Guid DepartamentoId { get; private set; }

    public RolAprobador Rol { get; private set; }

    public Guid UsuarioId { get; private set; }

    public DateTimeOffset VigenteDesde { get; private set; }

    public DateTimeOffset? VigenteHasta { get; private set; }

    public Guid DesignadoPor { get; private set; }

    public string? Motivo { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private AprobadorDepartamento() { }

    public AprobadorDepartamento(
        Guid id,
        Guid empresaId,
        Guid departamentoId,
        RolAprobador rol,
        Guid usuarioId,
        DateTimeOffset vigenteDesde,
        Guid designadoPor,
        string? motivo,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new BusinessRuleException("APROBADOR_ID_INVALIDO", "El id es obligatorio.");
        if (empresaId == Guid.Empty) throw new BusinessRuleException("APROBADOR_EMPRESA_INVALIDA", "EmpresaId es obligatorio.");
        if (departamentoId == Guid.Empty) throw new BusinessRuleException("APROBADOR_DEPARTAMENTO_INVALIDO", "DepartamentoId es obligatorio.");
        if (usuarioId == Guid.Empty) throw new BusinessRuleException("APROBADOR_USUARIO_INVALIDO", "UsuarioId es obligatorio.");
        if (designadoPor == Guid.Empty) throw new BusinessRuleException("APROBADOR_DESIGNADO_POR_INVALIDO", "DesignadoPor es obligatorio.");
        if (motivo is not null && motivo.Length > 500)
            throw new BusinessRuleException("APROBADOR_MOTIVO_DEMASIADO_LARGO", "El motivo no puede exceder 500 caracteres.");

        Id = id;
        EmpresaId = empresaId;
        DepartamentoId = departamentoId;
        Rol = rol;
        UsuarioId = usuarioId;
        VigenteDesde = vigenteDesde;
        VigenteHasta = null;
        DesignadoPor = designadoPor;
        Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Cierra la vigencia (set <see cref="VigenteHasta"/>). Idempotente:
    /// si ya está cerrada, no-op.
    /// </summary>
    public void Cerrar(DateTimeOffset cerradoEn)
    {
        if (VigenteHasta is not null) return;
        if (cerradoEn < VigenteDesde)
            throw new BusinessRuleException(
                "APROBADOR_CIERRE_INVALIDO",
                "VigenteHasta no puede ser anterior a VigenteDesde.");

        VigenteHasta = cerradoEn;
    }
}

