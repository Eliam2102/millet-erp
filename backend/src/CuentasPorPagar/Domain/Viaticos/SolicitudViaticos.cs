using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.Viaticos;

/// <summary>
/// Agregado raíz que modela el ciclo completo de viáticos (§7.4.2 del
/// 00-levantamiento, F7-PR3). El anticipo de viáticos es un <b>pasivo de
/// préstamo al empleado</b> (concepto contable <c>PRESTAMO_EMPLEADO</c>),
/// no un anticipo a proveedor — no genera CFDI de anticipo.
///
/// <para>
/// <b>Flujo</b> (8 estados):
/// <list type="number">
///   <item>Empleado captura solicitud → <see cref="EstadoSolicitudViaticos.Solicitada"/></item>
///   <item>Jefe firma → <see cref="EstadoSolicitudViaticos.AutorizadaPorJefe"/> si dentro de política;
///         <see cref="EstadoSolicitudViaticos.RequiereDireccionFinanzas"/> si excede.</item>
///   <item>DF firma (solo si excede) → <see cref="EstadoSolicitudViaticos.AutorizadaCompleta"/></item>
///   <item>Tesorería paga préstamo → <see cref="EstadoSolicitudViaticos.Anticipada"/></item>
///   <item>Empleado captura comprobación al regreso → <see cref="EstadoSolicitudViaticos.ComprobacionCapturada"/></item>
///   <item>CxP libera → <see cref="EstadoSolicitudViaticos.Liquidada"/></item>
/// </list>
/// </para>
///
/// <para>
/// Multi-tenant (<see cref="IPerteneceAEmpresa"/>): el query filter del
/// BaseDbContext aplica empresa + soft-delete automáticamente.
/// </para>
/// </summary>
public sealed class SolicitudViaticos : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid EmpleadoId { get; private set; }
    public Guid PuestoId { get; private set; }
    public Guid JefeDirectoId { get; private set; }

    public string Destino { get; private set; } = default!;
    public Domain.Catalogos.TipoDestinoViatico TipoDestino { get; private set; }
    public DateOnly FechaSalida { get; private set; }
    public DateOnly FechaRegreso { get; private set; }
    public int DiasEstimados => FechaRegreso.DayNumber - FechaSalida.DayNumber + 1;

    public string Moneda { get; private set; } = "MXN";
    public decimal MontoSolicitado { get; private set; }

    /// <summary>Tope calculado a partir de <c>PoliticaViaticos</c>; snapshot al solicitar.</summary>
    public decimal TopePoliticaSnapshot { get; private set; }

    /// <summary>True si la solicitud excede política y requiere DF.</summary>
    public bool ExcedePolitica { get; private set; }

    /// <summary>Justificación obligatoria cuando excede política.</summary>
    public string? JustificacionExceso { get; private set; }

    public EstadoSolicitudViaticos Estado { get; private set; }

    public Guid? AutorizadoPorJefe { get; private set; }
    public DateTimeOffset? FechaAutorizacionJefe { get; private set; }
    public Guid? AutorizadoPorDf { get; private set; }
    public DateTimeOffset? FechaAutorizacionDf { get; private set; }
    public Guid? RechazadoPor { get; private set; }
    public DateTimeOffset? FechaRechazo { get; private set; }
    public string? MotivoRechazo { get; private set; }

    /// <summary>Comentario de quien devolvió la solicitud a corregir (visible mientras está Devuelta).</summary>
    public string? ComentarioDevolucion { get; private set; }
    public Guid? DevueltoPor { get; private set; }
    public DateTimeOffset? FechaDevolucion { get; private set; }

    public DateTimeOffset? FechaAnticipoPagado { get; private set; }
    public DateTimeOffset? FechaComprobacion { get; private set; }
    public DateTimeOffset? FechaLiquidacion { get; private set; }
    public DateTimeOffset FechaSolicitud { get; private set; }

    // ---- Liquidación ----
    public decimal? MontoComprobado { get; private set; }

    /// <summary>
    /// Diferencia = MontoComprobado - MontoSolicitado. Positivo: empleado gastó más
    /// (Tesorería le reembolsa). Negativo: empleado gastó menos (debe devolver).
    /// </summary>
    public decimal? DiferenciaLiquidacion { get; private set; }

    private readonly List<LineaComprobacionViaticos> _lineas = [];
    public IReadOnlyCollection<LineaComprobacionViaticos> Lineas => _lineas.AsReadOnly();

    private SolicitudViaticos() { }

    public static SolicitudViaticos Solicitar(
        Guid empresaId,
        Guid empleadoId,
        Guid puestoId,
        Guid jefeDirectoId,
        string destino,
        Domain.Catalogos.TipoDestinoViatico tipoDestino,
        DateOnly fechaSalida,
        DateOnly fechaRegreso,
        string moneda,
        decimal montoSolicitado,
        decimal topePolitica,
        int diasMaxPolitica,
        string? justificacionExceso,
        DateTimeOffset ahora)
    {
        if (empleadoId == Guid.Empty)
            throw new BusinessRuleException("VIA_EMPLEADO_VACIO",
                "El empleado solicitante es obligatorio.");
        if (jefeDirectoId == Guid.Empty)
            throw new BusinessRuleException("VIA_JEFE_VACIO",
                "El jefe directo es obligatorio.");
        if (empleadoId == jefeDirectoId)
            throw new BusinessRuleException("VIA_EMPLEADO_JEFE_MISMO",
                "El jefe directo debe ser una persona distinta al solicitante.");
        if (puestoId == Guid.Empty)
            throw new BusinessRuleException("VIA_PUESTO_VACIO",
                "El puesto del solicitante es obligatorio.");
        if (string.IsNullOrWhiteSpace(destino))
            throw new BusinessRuleException("VIA_DESTINO_VACIO",
                "El destino es obligatorio.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("VIA_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");
        if (montoSolicitado <= 0)
            throw new BusinessRuleException("VIA_MONTO_INVALIDO",
                "El monto solicitado debe ser > 0.");
        if (fechaRegreso < fechaSalida)
            throw new BusinessRuleException("VIA_FECHAS_INVALIDAS",
                "La fecha de regreso no puede ser anterior a la salida.");

        var dias = fechaRegreso.DayNumber - fechaSalida.DayNumber + 1;
        var excede = montoSolicitado > topePolitica || dias > diasMaxPolitica;

        if (excede && string.IsNullOrWhiteSpace(justificacionExceso))
        {
            throw new BusinessRuleException(
                "VIA_JUSTIFICACION_REQUERIDA",
                "La solicitud excede política — se requiere justificación.");
        }

        return new SolicitudViaticos
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            EmpleadoId = empleadoId,
            PuestoId = puestoId,
            JefeDirectoId = jefeDirectoId,
            Destino = destino.Trim(),
            TipoDestino = tipoDestino,
            FechaSalida = fechaSalida,
            FechaRegreso = fechaRegreso,
            Moneda = moneda.ToUpperInvariant(),
            MontoSolicitado = montoSolicitado,
            TopePoliticaSnapshot = topePolitica,
            ExcedePolitica = excede,
            JustificacionExceso = justificacionExceso?.Trim(),
            Estado = EstadoSolicitudViaticos.Solicitada,
            FechaSolicitud = ahora,
        };
    }

    public void AutorizarPorJefe(Guid jefeId, DateTimeOffset ahora)
    {
        if (Estado != EstadoSolicitudViaticos.Solicitada)
            throw new BusinessRuleException(
                "VIA_NO_AUTORIZABLE_JEFE",
                $"Solo se autoriza por jefe desde Solicitada (actual: {Estado}).");
        if (jefeId != JefeDirectoId)
            throw new BusinessRuleException(
                "VIA_JEFE_NO_AUTORIZADO",
                "Solo el jefe directo asignado puede firmar Nivel 1.");

        AutorizadoPorJefe = jefeId;
        FechaAutorizacionJefe = ahora;
        Estado = ExcedePolitica
            ? EstadoSolicitudViaticos.RequiereDireccionFinanzas
            : EstadoSolicitudViaticos.AutorizadaPorJefe;
    }

    public void AutorizarPorDireccionFinanzas(Guid dfUsuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoSolicitudViaticos.RequiereDireccionFinanzas)
            throw new BusinessRuleException(
                "VIA_NO_AUTORIZABLE_DF",
                $"Solo se firma DF desde RequiereDireccionFinanzas (actual: {Estado}).");
        if (dfUsuarioId == AutorizadoPorJefe)
            throw new BusinessRuleException(
                "VIA_DF_MISMO_QUE_JEFE",
                "DF debe ser usuario distinto al jefe directo (segregación).");

        AutorizadoPorDf = dfUsuarioId;
        FechaAutorizacionDf = ahora;
        Estado = EstadoSolicitudViaticos.AutorizadaCompleta;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoSolicitudViaticos.Solicitada
                           or EstadoSolicitudViaticos.RequiereDireccionFinanzas))
            throw new BusinessRuleException(
                "VIA_NO_RECHAZABLE",
                $"Solo se rechaza antes del anticipo (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("VIA_MOTIVO_VACIO",
                "El motivo de rechazo es obligatorio.");

        Estado = EstadoSolicitudViaticos.Rechazada;
        RechazadoPor = usuarioId;
        FechaRechazo = ahora;
        MotivoRechazo = motivo.Trim();
    }

    /// <summary>
    /// Devuelve la solicitud a corregir sin cerrarla (Rechazar sigue siendo el cierre
    /// definitivo). Las firmas previas se conservan; al reenviar se reanuda el paso
    /// observado. No genera anticipo.
    /// </summary>
    public void Devolver(Guid usuarioId, string comentario, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoSolicitudViaticos.Solicitada
                           or EstadoSolicitudViaticos.RequiereDireccionFinanzas))
            throw new BusinessRuleException(
                "VIA_NO_DEVOLVIBLE",
                $"Solo se devuelve a corregir antes del anticipo, mientras espera firma (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(comentario))
            throw new BusinessRuleException("VIA_COMENTARIO_VACIO",
                "El comentario de la devolución es obligatorio.");

        Estado = EstadoSolicitudViaticos.Devuelta;
        DevueltoPor = usuarioId;
        FechaDevolucion = ahora;
        ComentarioDevolucion = comentario.Trim();
    }

    /// <summary>
    /// El empleado corrige monto, fechas y justificación de una solicitud Devuelta y la
    /// reenvía. Vuelve al paso que la devolvió: sin firma del jefe → Solicitada; con firma
    /// del jefe (la devolvió DF) → RequiereDireccionFinanzas.
    /// </summary>
    public void Reenviar(
        decimal montoSolicitado,
        DateOnly fechaSalida,
        DateOnly fechaRegreso,
        decimal topePolitica,
        int diasMaxPolitica,
        string? justificacionExceso)
    {
        if (Estado != EstadoSolicitudViaticos.Devuelta)
            throw new BusinessRuleException(
                "VIA_NO_REENVIABLE",
                $"Solo se reenvía una solicitud devuelta (actual: {Estado}).");
        if (montoSolicitado <= 0)
            throw new BusinessRuleException("VIA_MONTO_INVALIDO",
                "El monto solicitado debe ser > 0.");
        if (fechaRegreso < fechaSalida)
            throw new BusinessRuleException("VIA_FECHAS_INVALIDAS",
                "La fecha de regreso no puede ser anterior a la salida.");

        var dias = fechaRegreso.DayNumber - fechaSalida.DayNumber + 1;
        var excede = montoSolicitado > topePolitica || dias > diasMaxPolitica;
        if (excede && string.IsNullOrWhiteSpace(justificacionExceso))
            throw new BusinessRuleException(
                "VIA_JUSTIFICACION_REQUERIDA",
                "La solicitud excede política — se requiere justificación.");

        MontoSolicitado = montoSolicitado;
        FechaSalida = fechaSalida;
        FechaRegreso = fechaRegreso;
        TopePoliticaSnapshot = topePolitica;
        ExcedePolitica = excede;
        JustificacionExceso = justificacionExceso?.Trim();
        Estado = AutorizadoPorJefe is null
            ? EstadoSolicitudViaticos.Solicitada
            : EstadoSolicitudViaticos.RequiereDireccionFinanzas;
        ComentarioDevolucion = null;
        DevueltoPor = null;
        FechaDevolucion = null;
    }

    /// <summary>
    /// Marca que Tesorería pagó el préstamo al empleado. Acepta
    /// AutorizadaPorJefe (no excedía política) o AutorizadaCompleta
    /// (excedía y DF firmó).
    /// </summary>
    public void MarcarAnticipoPagado(DateTimeOffset ahora)
    {
        if (Estado is not (EstadoSolicitudViaticos.AutorizadaPorJefe
                           or EstadoSolicitudViaticos.AutorizadaCompleta))
            throw new BusinessRuleException(
                "VIA_NO_ANTICIPABLE",
                $"Solo se anticipa desde una solicitud autorizada (actual: {Estado}).");

        Estado = EstadoSolicitudViaticos.Anticipada;
        FechaAnticipoPagado = ahora;
    }

    /// <summary>
    /// El empleado captura su comprobación al regresar. Pueden capturarse
    /// múltiples veces hasta liberar, así que cada nueva captura
    /// reemplaza las líneas previas.
    /// </summary>
    public void CapturarComprobacion(
        IEnumerable<LineaComprobacionViaticosInput> lineas,
        DateTimeOffset ahora)
    {
        if (Estado is not (EstadoSolicitudViaticos.Anticipada
                           or EstadoSolicitudViaticos.ComprobacionCapturada))
            throw new BusinessRuleException(
                "VIA_NO_COMPROBABLE",
                $"Solo se comprueba después del anticipo (actual: {Estado}).");

        _lineas.Clear();
        foreach (var input in lineas)
        {
            var linea = new LineaComprobacionViaticos(
                id: Guid.CreateVersion7(),
                empresaId: EmpresaId,
                solicitudViaticosId: Id,
                cfdiRecibidoId: input.CfdiRecibidoId,
                uuidCfdi: input.UuidCfdi,
                proveedorId: input.ProveedorId,
                folioProveedor: input.FolioProveedor,
                fechaGasto: input.FechaGasto,
                subtotal: input.Subtotal,
                impuestosTrasladados: input.ImpuestosTrasladados,
                retenciones: input.Retenciones,
                total: input.Total,
                moneda: input.Moneda,
                concepto: input.Concepto,
                esTicketNoFiscal: input.EsTicketNoFiscal,
                clasificacionNoDeducible: input.ClasificacionNoDeducible);
            linea.AsignarCentroCosto(input.CentroCostoId);
            _lineas.Add(linea);
        }

        if (_lineas.Count == 0)
            throw new BusinessRuleException("VIA_COMPROBACION_VACIA",
                "La comprobación debe tener al menos una línea.");

        Estado = EstadoSolicitudViaticos.ComprobacionCapturada;
        FechaComprobacion = ahora;
    }

    /// <summary>
    /// CxP libera la comprobación. El handler debe haber generado las
    /// <c>FacturaProveedor</c> por cada línea fiscal antes de llamar este
    /// método y haber vinculado vía <see cref="LineaComprobacionViaticos"/>.
    /// Calcula la diferencia y deja la solicitud en Liquidada.
    /// </summary>
    public void LiberarComprobacion(DateTimeOffset ahora)
    {
        if (Estado != EstadoSolicitudViaticos.ComprobacionCapturada)
            throw new BusinessRuleException(
                "VIA_NO_LIBERABLE",
                $"Solo se libera desde ComprobacionCapturada (actual: {Estado}).");

        MontoComprobado = _lineas.Sum(l => l.Total);
        DiferenciaLiquidacion = MontoComprobado - MontoSolicitado;
        Estado = EstadoSolicitudViaticos.Liquidada;
        FechaLiquidacion = ahora;
    }

}

/// <summary>Input para captura de comprobación (no es entidad — DTO de dominio).</summary>
public sealed record LineaComprobacionViaticosInput(
    Guid? CfdiRecibidoId,
    string? UuidCfdi,
    Guid? ProveedorId,
    string? FolioProveedor,
    DateTimeOffset FechaGasto,
    decimal Subtotal,
    decimal ImpuestosTrasladados,
    decimal Retenciones,
    decimal Total,
    string Moneda,
    string Concepto,
    bool EsTicketNoFiscal,
    string? ClasificacionNoDeducible = null,
    // H1 (86e3z5w25): área o equipo del gasto (ya validado contra la sucursal del empleado).
    Guid? CentroCostoId = null);
