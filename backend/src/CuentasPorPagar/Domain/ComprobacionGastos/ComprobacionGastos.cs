using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.ComprobacionGastos;

/// <summary>
/// Agregado raíz que agrupa N comprobantes (CFDIs y/o tickets) para un
/// gasto sin OC: Caja Chica, Viáticos, TC Empresarial u Otros (§4.10 y
/// §7.4 del 00-levantamiento; §A21 del 01-diseno). Cada línea hija
/// referencia una <c>FacturaProveedor</c> generada para que el CFDI
/// afecte gasto/IVA/DIOT independientemente.
///
/// <para>
/// <b>F7-PR1 alcance</b>: factory + variante Caja Chica completa +
/// autorización por responsable de sucursal + aplicación. Variantes
/// Aduanales, Viáticos y TC viven en F7-PR2/PR3/PR4-PR6.
/// </para>
///
/// <para>
/// Multi-tenant (<see cref="IPerteneceAEmpresa"/>): el query filter del
/// BaseDbContext aplica empresa + soft-delete automáticamente.
/// </para>
/// </summary>
public sealed class ComprobacionGastos : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public TipoComprobacionGastos Tipo { get; private set; }

    /// <summary>Sucursal o área operativa responsable. Para Caja Chica es la sucursal del gasto.</summary>
    public Guid SucursalId { get; private set; }

    /// <summary>Empleado responsable que captura y comprueba (caja chica responsable, empleado en viáticos, titular en TC).</summary>
    public Guid ResponsableId { get; private set; }

    /// <summary>Periodo cubierto por la comprobación.</summary>
    public DateOnly FechaInicio { get; private set; }
    public DateOnly FechaFin { get; private set; }

    public string Moneda { get; private set; } = "MXN";

    /// <summary>Total derivado de las líneas. Persistido para queries de bandeja sin necesidad de agregación.</summary>
    public decimal MontoTotal { get; private set; }

    public EstadoComprobacionGastos Estado { get; private set; }

    /// <summary>
    /// Número de pedimento aduanero (§7.2 caso Aduanales). Solo aplica
    /// para <see cref="TipoComprobacionGastos.GastosAduanales"/>; null
    /// para Caja Chica / otros.
    /// </summary>
    public string? NumeroPedimento { get; private set; }

    /// <summary>
    /// Destino de la reposición (doc 12 §D2/Q1) — solo caja chica: a la
    /// cuenta de la sucursal o al responsable. Null para otros tipos.
    /// </summary>
    public DestinoReposicionCaja? DestinoReposicion { get; private set; }

    /// <summary>
    /// Reposición agregada que cubrió esta comprobación (doc 12 §D2/Q4).
    /// Null = aplicada pero aún acumulando (o no es caja chica).
    /// </summary>
    public Guid? ReposicionId { get; private set; }

    /// <summary>Usuario que firmó Nivel 1 (Comercio Exterior) — solo Aduanales (F7-PR2).</summary>
    public Guid? AutorizadoPorNivel1 { get; private set; }
    public DateTimeOffset? FechaAutorizacionNivel1 { get; private set; }

    public Guid? AutorizadoPor { get; private set; }
    public Guid? AplicadoPor { get; private set; }
    public Guid? RechazadoPor { get; private set; }

    public DateTimeOffset FechaCreacion { get; private set; }
    public DateTimeOffset? FechaEnvioRevision { get; private set; }
    public DateTimeOffset? FechaAutorizacion { get; private set; }
    public DateTimeOffset? FechaAplicacion { get; private set; }
    public DateTimeOffset? FechaRechazo { get; private set; }

    public string? MotivoRechazo { get; private set; }

    /// <summary>Notas libres del Auxiliar de CxP (opcional).</summary>
    public string? Observaciones { get; private set; }

    private readonly List<LineaComprobacionGastos> _lineas = [];
    public IReadOnlyCollection<LineaComprobacionGastos> Lineas => _lineas.AsReadOnly();

    private ComprobacionGastos() { }

    public static ComprobacionGastos Crear(
        Guid empresaId,
        TipoComprobacionGastos tipo,
        Guid sucursalId,
        Guid responsableId,
        DateOnly fechaInicio,
        DateOnly fechaFin,
        string moneda,
        string? observaciones,
        DateTimeOffset ahora,
        string? numeroPedimento = null,
        DestinoReposicionCaja? destinoReposicion = null)
    {
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("COMP_SUCURSAL_VACIA",
                "La sucursal responsable es obligatoria.");
        if (responsableId == Guid.Empty)
            throw new BusinessRuleException("COMP_RESPONSABLE_VACIO",
                "El responsable de la comprobación es obligatorio.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("COMP_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");
        if (fechaFin < fechaInicio)
            throw new BusinessRuleException("COMP_PERIODO_INVALIDO",
                "La fecha fin no puede ser anterior a la fecha inicio.");

        if (tipo == TipoComprobacionGastos.GastosAduanales
            && string.IsNullOrWhiteSpace(numeroPedimento))
        {
            throw new BusinessRuleException(
                "COMP_PEDIMENTO_REQUERIDO",
                "Las comprobaciones de gastos aduanales requieren número de pedimento.");
        }
        if (tipo != TipoComprobacionGastos.GastosAduanales
            && !string.IsNullOrWhiteSpace(numeroPedimento))
        {
            throw new BusinessRuleException(
                "COMP_PEDIMENTO_NO_APLICA",
                $"Solo Aduanales puede llevar número de pedimento (tipo recibido: {tipo}).");
        }

        if (tipo == TipoComprobacionGastos.ReembolsoCajaChica && destinoReposicion is null)
        {
            throw new BusinessRuleException(
                "COMP_DESTINO_REPOSICION_REQUERIDO",
                "Las comprobaciones de caja chica requieren destino de reposición (cuenta de sucursal o responsable).");
        }
        if (tipo != TipoComprobacionGastos.ReembolsoCajaChica && destinoReposicion is not null)
        {
            throw new BusinessRuleException(
                "COMP_DESTINO_REPOSICION_NO_APLICA",
                $"Solo caja chica lleva destino de reposición (tipo recibido: {tipo}).");
        }

        return new ComprobacionGastos
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            Tipo = tipo,
            SucursalId = sucursalId,
            ResponsableId = responsableId,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin,
            Moneda = moneda.ToUpperInvariant(),
            MontoTotal = 0m,
            Estado = EstadoComprobacionGastos.Borrador,
            FechaCreacion = ahora,
            Observaciones = observaciones,
            NumeroPedimento = numeroPedimento?.Trim(),
            DestinoReposicion = destinoReposicion,
        };
    }

    /// <summary>
    /// Liga la comprobación a la reposición agregada que la cubre
    /// (doc 12 §D2/Q4). Solo aplica a caja chica Aplicada sin reposición
    /// previa.
    /// </summary>
    public void AsignarReposicion(Guid reposicionId)
    {
        if (Tipo != TipoComprobacionGastos.ReembolsoCajaChica)
            throw new BusinessRuleException(
                "COMP_REPOSICION_NO_APLICA",
                $"Solo caja chica participa en reposiciones (tipo: {Tipo}).");
        if (Estado != EstadoComprobacionGastos.Aplicada)
            throw new BusinessRuleException(
                "COMP_REPOSICION_NO_APLICADA",
                $"Solo comprobaciones Aplicadas entran a una reposición (actual: {Estado}).");
        if (ReposicionId is not null)
            throw new BusinessRuleException(
                "COMP_REPOSICION_YA_CUBIERTA",
                "La comprobación ya está cubierta por otra reposición.");

        ReposicionId = reposicionId;
    }

    /// <summary>
    /// Agrega una línea (CFDI ya generado en <c>FacturaProveedor</c>).
    /// Solo permitido en estado Borrador. Mantiene <see cref="MontoTotal"/>
    /// sumando.
    /// </summary>
    public LineaComprobacionGastos AgregarLinea(
        Guid facturaProveedorId,
        Guid? cfdiRecibidoId,
        string? uuidCfdi,
        Guid proveedorId,
        string? folioProveedor,
        DateTimeOffset fechaCfdi,
        decimal subtotal,
        decimal impuestosTrasladados,
        decimal retenciones,
        decimal total,
        string moneda,
        string? concepto)
    {
        AsegurarEditable("agregar línea");

        if (!string.Equals(moneda, Moneda, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("COMP_LINEA_MONEDA_MISMATCH",
                $"La moneda de la línea ({moneda}) no coincide con la moneda de la comprobación ({Moneda}).");

        var linea = new LineaComprobacionGastos(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaId,
            comprobacionGastosId: Id,
            facturaProveedorId: facturaProveedorId,
            cfdiRecibidoId: cfdiRecibidoId,
            uuidCfdi: uuidCfdi,
            proveedorId: proveedorId,
            folioProveedor: folioProveedor,
            fechaCfdi: fechaCfdi,
            subtotal: subtotal,
            impuestosTrasladados: impuestosTrasladados,
            retenciones: retenciones,
            total: total,
            moneda: moneda,
            concepto: concepto);

        _lineas.Add(linea);
        MontoTotal += total;
        return linea;
    }

    /// <summary>
    /// Marca la comprobación como enviada para revisión del responsable
    /// de sucursal. Requiere al menos una línea.
    /// </summary>
    public void EnviarARevision(DateTimeOffset ahora)
    {
        if (Estado != EstadoComprobacionGastos.Borrador)
            throw new BusinessRuleException(
                "COMP_NO_ENVIABLE",
                $"Solo comprobaciones en Borrador pueden enviarse a revisión (actual: {Estado}).");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("COMP_SIN_LINEAS",
                "No se puede enviar a revisión sin líneas.");

        Estado = EstadoComprobacionGastos.PorRevisar;
        FechaEnvioRevision = ahora;
    }

    /// <summary>
    /// Autoriza la comprobación con firma única (responsable de sucursal /
    /// área). Aplica para Caja Chica, Viáticos y Otros. <b>NO usar para
    /// Aduanales</b> — usar <see cref="AutorizarNivel1Aduanales"/> +
    /// <see cref="AutorizarNivel2Aduanales"/>.
    /// </summary>
    public void Autorizar(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Tipo == TipoComprobacionGastos.GastosAduanales)
            throw new BusinessRuleException(
                "COMP_ADUANALES_REQUIERE_DOBLE_FIRMA",
                "Aduanales requiere AutorizarNivel1 + AutorizarNivel2.");
        if (Estado is not (EstadoComprobacionGastos.Borrador or EstadoComprobacionGastos.PorRevisar))
            throw new BusinessRuleException(
                "COMP_NO_AUTORIZABLE",
                $"Solo se autoriza desde Borrador o PorRevisar (actual: {Estado}).");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("COMP_SIN_LINEAS",
                "No se puede autorizar una comprobación sin líneas.");

        Estado = EstadoComprobacionGastos.Autorizada;
        AutorizadoPor = usuarioId;
        FechaAutorizacion = ahora;
    }

    /// <summary>
    /// Firma Nivel 1 de Aduanales (Comercio Exterior, §7.2). Habilita
    /// el registro del pasivo pero el pago queda pendiente del Nivel 2.
    /// </summary>
    public void AutorizarNivel1Aduanales(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Tipo != TipoComprobacionGastos.GastosAduanales)
            throw new BusinessRuleException(
                "COMP_NIVEL1_SOLO_ADUANALES",
                $"AutorizarNivel1 solo aplica a Aduanales (tipo: {Tipo}).");
        if (Estado is not (EstadoComprobacionGastos.Borrador or EstadoComprobacionGastos.PorRevisar))
            throw new BusinessRuleException(
                "COMP_NIVEL1_NO_AUTORIZABLE",
                $"Solo se firma Nivel 1 desde Borrador/PorRevisar (actual: {Estado}).");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("COMP_SIN_LINEAS",
                "No se puede firmar Nivel 1 una comprobación sin líneas.");

        Estado = EstadoComprobacionGastos.AutorizadaNivel1;
        AutorizadoPorNivel1 = usuarioId;
        FechaAutorizacionNivel1 = ahora;
    }

    /// <summary>
    /// Firma Nivel 2 de Aduanales (Dirección de Finanzas, §7.2). Habilita
    /// el pago al proveedor. El handler debe transicionar las facturas
    /// ligadas a <c>EstadoPasivo.Autorizada</c> dentro de la misma TX.
    /// </summary>
    public void AutorizarNivel2Aduanales(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Tipo != TipoComprobacionGastos.GastosAduanales)
            throw new BusinessRuleException(
                "COMP_NIVEL2_SOLO_ADUANALES",
                $"AutorizarNivel2 solo aplica a Aduanales (tipo: {Tipo}).");
        if (Estado != EstadoComprobacionGastos.AutorizadaNivel1)
            throw new BusinessRuleException(
                "COMP_NIVEL2_NO_AUTORIZABLE",
                $"Solo se firma Nivel 2 desde AutorizadaNivel1 (actual: {Estado}).");
        if (AutorizadoPorNivel1 == usuarioId)
            throw new BusinessRuleException(
                "COMP_NIVEL2_MISMO_USUARIO",
                "El usuario de Nivel 2 debe ser distinto al de Nivel 1 (segregación de funciones).");

        Estado = EstadoComprobacionGastos.Autorizada;
        AutorizadoPor = usuarioId;
        FechaAutorizacion = ahora;
    }

    /// <summary>
    /// Aplica la comprobación al ledger: cierra el ciclo de captura y
    /// la pasa a Tesorería para reembolso.
    /// </summary>
    public void Aplicar(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoComprobacionGastos.Autorizada)
            throw new BusinessRuleException(
                "COMP_NO_APLICABLE",
                $"Solo comprobaciones Autorizadas pueden aplicarse (actual: {Estado}).");

        Estado = EstadoComprobacionGastos.Aplicada;
        AplicadoPor = usuarioId;
        FechaAplicacion = ahora;
    }

    public void Rechazar(Guid usuarioId, string motivo, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoComprobacionGastos.Borrador
                           or EstadoComprobacionGastos.PorRevisar
                           or EstadoComprobacionGastos.AutorizadaNivel1))
            throw new BusinessRuleException(
                "COMP_NO_RECHAZABLE",
                $"Solo se rechaza desde Borrador, PorRevisar o AutorizadaNivel1 (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("COMP_MOTIVO_VACIO",
                "El motivo de rechazo es obligatorio.");

        Estado = EstadoComprobacionGastos.Rechazada;
        RechazadoPor = usuarioId;
        FechaRechazo = ahora;
        MotivoRechazo = motivo.Trim();
    }

    private void AsegurarEditable(string operacion)
    {
        if (Estado != EstadoComprobacionGastos.Borrador)
            throw new BusinessRuleException(
                "COMP_NO_EDITABLE",
                $"No se puede {operacion} en estado {Estado}; solo Borrador es editable.");
    }
}
