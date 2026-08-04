using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Movimientos;

/// <summary>
/// Agregado raíz <b>MovimientoInventario</b> (01-diseno §4.1, §5.1).
/// Unidad atómica de cambio de inventario. Tabla polimórfica
/// <c>almacen.movimientos_inventario</c> con columna <c>tipo</c> como
/// discriminador.
///
/// <para>
/// <b>Invariantes</b>:
/// <list type="bullet">
///   <item>Estado <see cref="EstadoMovimiento.Registrado"/> es
///   <b>inmutable</b>. Corrección por contramovimiento.</item>
///   <item>Solo puede pasar a <see cref="EstadoMovimiento.Cancelado"/>
///   desde <see cref="EstadoMovimiento.Borrador"/> o
///   <see cref="EstadoMovimiento.Validado"/>.</item>
///   <item>Al pasar a Registrado, el trigger PostgreSQL
///   <c>tg_movimientos_actualizar_saldo</c> actualiza
///   <c>saldos_inventario</c> en la misma transacción
///   (cuidado §3.1 del 04-cuidados-infra).</item>
///   <item>Folio asignado al pasar a Registrado (no en Borrador) —
///   garantiza secuencias sin huecos.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>F2-PR1 trae el agregado base + saldo materializado.</b>
/// F2-PR2 agrega los flujos de recepción Variante A (con factura) y
/// el evento <c>OcRecepcionRegistradaEvent</c>. Los handlers que
/// realmente disparan los movimientos viven en cada PR (F2-PR2 hace
/// el primero).
/// </para>
/// </summary>
public sealed class MovimientoInventario : BaseEntity, IPerteneceAEmpresa
{
    public string? Folio { get; private set; }
    public TipoMovimiento Tipo { get; private set; }
    public EstadoMovimiento Estado { get; private set; }
    // Almacén-por-línea PR6a: el sub-almacén salió de la cabecera del movimiento.
    // Ahora se deriva de la ubicación (bin N4) de las líneas — todas comparten
    // sub-almacén, invariante que impone el trigger (MOVIMIENTO_MULTI_SUBALMACEN).
    // Los lectores lo resuelven vía la vista almacen.v_movimiento_sub_almacen.

    /// <summary>
    /// Almacén-por-línea PR4: ubicación (bin N4) que el almacenista eligió como
    /// <b>helper de cabecera</b> al capturar la recepción, para auto-aplicarla a
    /// las líneas sin ubicación. <c>null</c> = <b>no usó el helper</b> (capturó
    /// línea por línea). Es dato de reportería — la ubicación real de cada línea
    /// vive en <see cref="LineaMovimiento.UbicacionId"/> y es la que manda para
    /// saldos. No participa en ninguna invariante del movimiento.
    /// </summary>
    public Guid? UbicacionHelperId { get; private set; }

    public DateOnly FechaMovimiento { get; private set; }
    public DateTimeOffset FechaRegistro { get; private set; }
    public Guid EmpresaId { get; set; }

    // Vinculaciones según tipo (nullables; activadas en fases posteriores).
    public Guid? OcId { get; private set; }
    public Guid? OcLineaId { get; private set; }
    public Guid? FacturaId { get; private set; }
    public Guid? CfdiRecibidoId { get; private set; }
    // Variante A: folio fiscal (UUID SAT) capturado del impreso cuando el
    // CFDI aún no está en el repositorio de CxP (§5.4 del levantamiento).
    // Normalizado a mayúsculas (mismo formato que el VO UuidCfdi de CxP)
    // para que la conciliación diferida matchee por igualdad exacta.
    public string? CfdiUuidFiscal { get; private set; }
    public string? PackingListBlobRef { get; private set; }
    public Guid? RqId { get; private set; }
    public string? ValeBlobRef { get; private set; }
    public Guid? SalidaOrigenId { get; private set; }
    public Guid? RecepcionOrigenId { get; private set; }
    public Guid? ConteoId { get; private set; }
    public Guid? ProveedorId { get; private set; }

    // Vale: regularización (F5).
    public bool PendienteRegularizacion { get; private set; }
    public DateTimeOffset? FechaLimiteRegularizacion { get; private set; }
    public Guid? RqRegularizadoraId { get; private set; }

    // Solicitante / destinatario (Salida).
    public Guid? PersonaDestinatariaId { get; private set; }
    public string? ComentarioLibre { get; private set; }

    // Devolución.
    public string? Motivo { get; private set; }
    public string? EstadoMaterial { get; private set; }

    // F3-PR1: ajuste de precio variante B.
    public Guid? FacturaIdOrigenDiff { get; private set; }

    // Quién y cuándo firmó el movimiento.
    public Guid? RegistradoPor { get; private set; }
    public DateTimeOffset? RegistradoAt { get; private set; }

    private readonly List<LineaMovimiento> _lineas = new();
    public IReadOnlyCollection<LineaMovimiento> Lineas => _lineas.AsReadOnly();

    private MovimientoInventario() { }

    public MovimientoInventario(
        Guid id,
        TipoMovimiento tipo,
        Guid empresaId,
        DateOnly fechaMovimiento,
        Guid? ubicacionHelperId = null) : base(id)
    {
        if (empresaId == Guid.Empty)
            throw new BusinessRuleException("MOV_SIN_EMPRESA", "El movimiento requiere empresa.");

        Tipo = tipo;
        Estado = EstadoMovimiento.Borrador;
        EmpresaId = empresaId;
        // PR4: opcional, sin invariante. Guid.Empty se normaliza a null para no
        // guardar un FK inválido si el caller manda un default sin querer.
        UbicacionHelperId = ubicacionHelperId == Guid.Empty ? null : ubicacionHelperId;
        FechaMovimiento = fechaMovimiento;
        FechaRegistro = DateTimeOffset.UtcNow;
    }

    public void AgregarLinea(LineaMovimiento linea)
    {
        if (Estado != EstadoMovimiento.Borrador)
            throw new BusinessRuleException("MOV_NO_BORRADOR",
                "Solo se pueden agregar líneas en estado Borrador.");
        _lineas.Add(linea);
    }

    public void Validar()
    {
        if (Estado != EstadoMovimiento.Borrador)
            throw new BusinessRuleException("MOV_NO_BORRADOR",
                $"Solo se puede validar desde Borrador (estado actual: {Estado}).");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("MOV_SIN_LINEAS",
                "El movimiento no tiene líneas.");
        Estado = EstadoMovimiento.Validado;
    }

    /// <summary>
    /// Asigna folio y firma el movimiento. Es responsabilidad del
    /// handler reservar el siguiente número de
    /// <see cref="FolioSecuenciaMovimiento"/> dentro de la misma
    /// transacción que actualiza <c>saldos_inventario</c>.
    /// </summary>
    public void Registrar(FolioMovimiento folio, Guid registradoPor)
    {
        if (Estado is not EstadoMovimiento.Borrador and not EstadoMovimiento.Validado)
            throw new BusinessRuleException("MOV_NO_REGISTRABLE",
                $"Solo se puede registrar desde Borrador o Validado (estado actual: {Estado}).");
        if (_lineas.Count == 0)
            throw new BusinessRuleException("MOV_SIN_LINEAS",
                "El movimiento no tiene líneas.");
        if (registradoPor == Guid.Empty)
            throw new BusinessRuleException("MOV_SIN_REGISTRADO_POR",
                "El movimiento requiere usuario que registra.");

        Folio = folio.Valor;
        Estado = EstadoMovimiento.Registrado;
        RegistradoPor = registradoPor;
        RegistradoAt = DateTimeOffset.UtcNow;
    }

    public void Cancelar(string motivo)
    {
        if (Estado is not EstadoMovimiento.Borrador and not EstadoMovimiento.Validado)
            throw new BusinessRuleException("MOV_NO_CANCELABLE",
                $"Solo se puede cancelar desde Borrador o Validado (estado actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("MOV_CANCEL_SIN_MOTIVO",
                "Cancelar movimiento requiere motivo.");
        Estado = EstadoMovimiento.Cancelado;
        Motivo = motivo;
    }

    // ─── Setters internos para vinculaciones (los usan los flujos
    //     específicos de cada fase: F2-PR2 recepción, F4-PR1 salida, etc.) ───

    /// <summary>
    /// F2-PR2 (variante A): vincula a OC autorizada + CFDI. El vínculo
    /// fiscal es obligatorio en variante A: o el <paramref name="cfdiRecibidoId"/>
    /// del repositorio de CxP, o el <paramref name="cfdiUuidFiscal"/> capturado
    /// del impreso (CxP enlaza después al procesar el XML).
    /// </summary>
    internal void VincularRecepcionVarianteA(
        Guid ocId, Guid? ocLineaId, Guid? cfdiRecibidoId, string? cfdiUuidFiscal)
    {
        if (cfdiRecibidoId is null && string.IsNullOrWhiteSpace(cfdiUuidFiscal))
            throw new BusinessRuleException("RECEPCION_SIN_CFDI",
                "La recepción variante A requiere el CFDI vinculado o su folio fiscal (UUID).");
        OcId = ocId;
        OcLineaId = ocLineaId;
        CfdiRecibidoId = cfdiRecibidoId;
        CfdiUuidFiscal = string.IsNullOrWhiteSpace(cfdiUuidFiscal)
            ? null
            : cfdiUuidFiscal.Trim().ToUpperInvariant();
    }

    /// <summary>F3-PR1 (variante B): vincula a OC autorizada + packing list, factura pendiente.</summary>
    internal void VincularRecepcionVarianteB(Guid ocId, Guid? ocLineaId, string packingListBlobRef)
    {
        OcId = ocId;
        OcLineaId = ocLineaId;
        PackingListBlobRef = packingListBlobRef;
    }

    /// <summary>F3-PR1: al recibir FacturaProveedorRegistrada (CxP), vincula factura final.</summary>
    internal void ConciliarConFacturaProveedor(Guid facturaId)
    {
        FacturaId = facturaId;
    }

    /// <summary>
    /// Enlace diferido variante A (§5.4): la recepción se registró con
    /// folio fiscal capturado a mano y CxP procesó después el XML con ese
    /// UUID (evento <c>cuentas_por_pagar.cfdi.ingresado.v1</c>).
    /// </summary>
    internal void EnlazarCfdiRecibido(Guid cfdiRecibidoId)
    {
        CfdiRecibidoId = cfdiRecibidoId;
    }

    /// <summary>F4-PR1 / F5-PR1: vincula salida a RQ o vale.</summary>
    internal void VincularSalida(
        Guid? rqId,
        string? valeBlobRef,
        Guid? personaDestinatariaId)
    {
        RqId = rqId;
        ValeBlobRef = valeBlobRef;
        PersonaDestinatariaId = personaDestinatariaId;
        if (Tipo == TipoMovimiento.SalidaPorVale)
        {
            PendienteRegularizacion = true;
            FechaLimiteRegularizacion = DateTimeOffset.UtcNow.AddHours(48); // A14
        }
    }

    public void RegularizarVale(Guid rqRegularizadoraId)
    {
        if (Tipo != TipoMovimiento.SalidaPorVale)
            throw new BusinessRuleException("MOV_NO_VALE",
                "Solo aplica a salidas por vale.");
        if (!PendienteRegularizacion)
            throw new BusinessRuleException("VALE_YA_REGULARIZADO",
                "El vale ya fue regularizado.");
        RqRegularizadoraId = rqRegularizadoraId;
        PendienteRegularizacion = false;
    }
}
