using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Conteos;

/// <summary>
/// Agregado raíz <b>ConteoInventario</b> (F7-PR1, 01-diseno §4.1 + §7).
/// Tabla <c>almacen.conteos_inventario</c>.
///
/// <para>
/// <b>Invariantes</b>:
/// <list type="bullet">
///   <item>El snapshot se toma <b>una sola vez</b> al pasar a EnCurso —
///   no se re-actualiza durante el conteo (cuidado §4.1).</item>
///   <item>Aplicar requiere estado Aprobado.</item>
///   <item>Aplicado es terminal (los movimientos de ajuste son inmutables).</item>
/// </list>
/// </para>
/// </summary>
public sealed class ConteoInventario : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public TipoConteo Tipo { get; private set; }
    public EstadoConteo Estado { get; private set; }
    public Guid? SubAlmacenId { get; private set; }
    public string? FiltroFamilia { get; private set; }
    public DateOnly FechaPlanificada { get; private set; }
    public DateTimeOffset? FechaInicio { get; private set; }
    public DateTimeOffset? FechaCierre { get; private set; }
    public Guid ResponsableId { get; private set; }
    public DateTimeOffset? SnapshotCapturadoAt { get; private set; }
    public Guid? AprobadorId { get; private set; }
    public DateTimeOffset? FechaAprobacion { get; private set; }
    public string? MotivoRechazo { get; private set; }

    private readonly List<LineaConteo> _lineas = new();
    public IReadOnlyCollection<LineaConteo> Lineas => _lineas.AsReadOnly();

    private ConteoInventario() { }

    public ConteoInventario(
        Guid id,
        Guid empresaId,
        TipoConteo tipo,
        DateOnly fechaPlanificada,
        Guid responsableId,
        Guid? subAlmacenId = null,
        string? filtroFamilia = null) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("CONTEO_SIN_EMPRESA",
                "El conteo requiere empresa.");
        if (responsableId == Guid.Empty)
            throw new BusinessRuleException("CONTEO_SIN_RESPONSABLE",
                "El conteo requiere responsable.");
        if (!string.IsNullOrWhiteSpace(filtroFamilia) && filtroFamilia.Length > 50)
            throw new BusinessRuleException("CONTEO_FILTRO_INVALIDO",
                "El filtro de familia no puede exceder 50 caracteres.");

        EmpresaId = empresaId;
        Tipo = tipo;
        Estado = EstadoConteo.Planificado;
        FechaPlanificada = fechaPlanificada;
        ResponsableId = responsableId;
        SubAlmacenId = subAlmacenId;
        FiltroFamilia = filtroFamilia;
    }

    /// <summary>
    /// Pasa el conteo a EnCurso y registra el snapshot. El handler
    /// debe pre-poblar las líneas con la cantidad teórica y costo
    /// promedio vigente del saldo ANTES de llamar este método; ese es
    /// el snapshot inmutable.
    /// </summary>
    public void Iniciar()
    {
        if (Estado != EstadoConteo.Planificado)
            throw new BusinessRuleException("CONTEO_NO_PLANIFICADO",
                $"Solo se puede iniciar desde Planificado (estado: {Estado}).");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("CONTEO_SIN_LINEAS",
                "El conteo no tiene líneas; el handler debe pre-poblarlas con el snapshot del saldo.");

        Estado = EstadoConteo.EnCurso;
        FechaInicio = DateTimeOffset.UtcNow;
        SnapshotCapturadoAt = DateTimeOffset.UtcNow;
    }

    public void AgregarLinea(LineaConteo linea)
    {
        if (Estado != EstadoConteo.Planificado)
            throw new BusinessRuleException("CONTEO_NO_PLANIFICADO",
                "Solo se pueden agregar líneas en Planificado (antes del snapshot).");
        _lineas.Add(linea);
    }

    /// <summary>
    /// Pasa de EnCurso a EnConciliacion (captura completa). Validación
    /// en el handler: todas las líneas deben tener
    /// <c>cantidad_real_capturada</c>.
    /// </summary>
    public void EnviarAConciliacion()
    {
        if (Estado != EstadoConteo.EnCurso)
            throw new BusinessRuleException("CONTEO_NO_EN_CURSO",
                $"Solo se puede enviar a conciliación desde EnCurso (estado: {Estado}).");
        if (_lineas.Any(l => l.CantidadRealCapturada is null))
            throw new BusinessRuleException("CONTEO_CAPTURA_INCOMPLETA",
                "Todas las líneas deben tener cantidad real capturada.");

        Estado = EstadoConteo.EnConciliacion;
    }

    /// <summary>
    /// F7-PR2: aprobador firma. Validación: líneas con
    /// <c>requiere_recuento=true</c> deben tener al menos un recuento
    /// O estar aprobadas individualmente.
    /// </summary>
    public void Aprobar(Guid aprobadorId)
    {
        if (Estado != EstadoConteo.EnConciliacion)
            throw new BusinessRuleException("CONTEO_NO_EN_CONCILIACION",
                $"Solo se puede aprobar desde EnConciliacion (estado: {Estado}).");
        if (aprobadorId == Guid.Empty)
            throw new BusinessRuleException("CONTEO_SIN_APROBADOR",
                "Se requiere aprobador.");

        Estado = EstadoConteo.Aprobado;
        AprobadorId = aprobadorId;
        FechaAprobacion = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// F7-PR2: aplica el conteo (genera movimientos de ajuste). El
    /// handler ejecuta la generación de
    /// <c>AjustePositivo</c>/<c>AjusteNegativo</c> en batch
    /// transaccional. Terminal — no se reabre (cuidado §6.2).
    /// </summary>
    public void MarcarAplicado()
    {
        if (Estado != EstadoConteo.Aprobado)
            throw new BusinessRuleException("CONTEO_NO_APROBADO",
                $"Solo se puede aplicar desde Aprobado (estado: {Estado}).");
        Estado = EstadoConteo.Aplicado;
        FechaCierre = DateTimeOffset.UtcNow;
    }

    public void Rechazar(string motivo)
    {
        if (Estado is EstadoConteo.Aplicado or EstadoConteo.Rechazado)
            throw new BusinessRuleException("CONTEO_NO_RECHAZABLE",
                $"No se puede rechazar desde {Estado}.");
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length > 500)
            throw new BusinessRuleException("CONTEO_RECHAZO_INVALIDO",
                "El motivo es requerido (≤500 chars).");
        Estado = EstadoConteo.Rechazado;
        MotivoRechazo = motivo;
        FechaCierre = DateTimeOffset.UtcNow;
    }
}
