using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Facturacion.Domain.Cajas;

/// <summary>Estados de una <see cref="CajaSesion"/> (12-cajas.md §5.1).</summary>
public enum EstadoCajaSesion : short
{
    /// <summary>Acepta movimientos (cobros automáticos + retiros/depósitos manuales).</summary>
    Abierta = 1,

    /// <summary>El sistema calculó el esperado por forma de pago; el cajero captura el contado físico.</summary>
    EnArqueo = 2,

    /// <summary>Inmutable — registro financiero. Correcciones = ajuste en la sesión siguiente ([Decisión 12-C]).</summary>
    Cerrada = 3,
}

/// <summary>
/// Sesión de efectivo de una caja — agregado de la Capa B (12-cajas.md §5,
/// CAJAS-PR3). Ciclo: apertura (fondo + sucursal de operación `[12-A]`, con
/// autorización consumible si la caja es ajena `[12-1]`) → ABIERTA →
/// EN_ARQUEO → CERRADA (snapshot inmutable). Una caja no admite dos sesiones
/// abiertas (índice único parcial → 409). El día de operación se calcula con
/// la zona horaria de la sucursal (`[12-8]`); una sesión de día anterior
/// bloquea cobros hasta cerrarse extemporáneamente (§5.2).
/// </summary>
public sealed class CajaSesion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    /// <summary>Forma de pago SAT del efectivo — la única que participa en la diferencia del arqueo ([Decisión 12-5]).</summary>
    public const string FormaPagoEfectivo = "01";

    public Guid EmpresaId { get; set; }
    public Guid CajaId { get; private set; }

    /// <summary>Sucursal de operación declarada al abrir (`[Decisión 12-A]`); su zona horaria define el día de operación.</summary>
    public Guid SucursalId { get; private set; }

    /// <summary>Quien abre responde el efectivo, aunque más usuarios estén relacionados a la caja (§5.1).</summary>
    public Guid ResponsableUsuarioId { get; private set; }

    public EstadoCajaSesion Estado { get; private set; }

    /// <summary>Día local de la sucursal al abrir; base del corte y del bloqueo de día anterior (§5.2).</summary>
    public DateOnly DiaOperacion { get; private set; }

    public DateTimeOffset FechaApertura { get; private set; }
    public DateTimeOffset? FechaCierre { get; private set; }

    public decimal FondoApertura { get; private set; }

    /// <summary>Autorización consumida al abrir caja ajena (`[Decisión 12-1]`); null si el cajero está relacionado.</summary>
    public Guid? AutorizacionAperturaId { get; private set; }

    // ---- Snapshot inmutable al cierre (solo efectivo participa en diferencia) ----
    public decimal? EfectivoDeclarado { get; private set; }
    public decimal? EfectivoTeorico { get; private set; }
    public decimal? Diferencia { get; private set; }

    /// <summary>True si el cierre ocurrió en un día local posterior al de operación (§5.2).</summary>
    public bool CierreExtemporaneo { get; private set; }

    public string? NotasCierre { get; private set; }

    private readonly List<CajaSesionCorte> _cortes = [];

    /// <summary>Esperado (y declarado al cerrar) por forma de pago; congelado en CERRADA.</summary>
    public IReadOnlyCollection<CajaSesionCorte> Cortes => _cortes.AsReadOnly();

    private CajaSesion() { }

    private CajaSesion(
        Guid id, Guid empresaId, Guid cajaId, Guid sucursalId, Guid responsableUsuarioId,
        decimal fondoApertura, DateOnly diaOperacion, DateTimeOffset fechaApertura,
        Guid? autorizacionAperturaId) : base(id)
    {
        EmpresaId = empresaId;
        CajaId = cajaId;
        SucursalId = sucursalId;
        ResponsableUsuarioId = responsableUsuarioId;
        FondoApertura = fondoApertura;
        DiaOperacion = diaOperacion;
        FechaApertura = fechaApertura;
        AutorizacionAperturaId = autorizacionAperturaId;
        Estado = EstadoCajaSesion.Abierta;
    }

    /// <summary>Abre la sesión (§5.1 paso 1). Las validaciones cross-entity (caja activa, sucursal en alcance, sesión previa) viven en el handler.</summary>
    public static CajaSesion Abrir(
        Guid empresaId,
        Guid cajaId,
        Guid sucursalId,
        Guid responsableUsuarioId,
        decimal fondoApertura,
        DateOnly diaOperacion,
        DateTimeOffset fechaApertura,
        Guid? autorizacionAperturaId)
    {
        if (cajaId == Guid.Empty)
            throw new BusinessRuleException("SESION_CAJA_INVALIDA", "La caja es obligatoria.");
        if (sucursalId == Guid.Empty)
            throw new BusinessRuleException("SESION_SUCURSAL_INVALIDA", "La sucursal de operación es obligatoria.");
        if (responsableUsuarioId == Guid.Empty)
            throw new BusinessRuleException("SESION_RESPONSABLE_INVALIDO", "El responsable es obligatorio.");
        if (fondoApertura < 0)
            throw new BusinessRuleException("SESION_FONDO_INVALIDO", "El fondo de apertura no puede ser negativo.");

        return new CajaSesion(
            Guid.CreateVersion7(), empresaId, cajaId, sucursalId, responsableUsuarioId,
            fondoApertura, diaOperacion, fechaApertura, autorizacionAperturaId);
    }

    /// <summary>
    /// Transición <c>Abierta → EnArqueo</c> (§5.1 paso 3): congela el
    /// <b>esperado por forma de pago</b> (fondo + ingresos − retiros ±
    /// ajustes), calculado por el handler desde los movimientos de la sesión.
    /// Re-iniciar el arqueo tras <see cref="Reabrir"/> recalcula los cortes.
    /// </summary>
    public void IniciarArqueo(IReadOnlyDictionary<string, decimal> esperadoPorFormaPago)
    {
        if (Estado != EstadoCajaSesion.Abierta)
            throw new BusinessRuleException(
                "SESION_NO_ABIERTA",
                $"Solo una sesión Abierta puede iniciar arqueo (actual: {Estado}).");

        _cortes.Clear();
        foreach (var (formaPago, monto) in esperadoPorFormaPago.OrderBy(k => k.Key, StringComparer.Ordinal))
            _cortes.Add(new CajaSesionCorte(Guid.CreateVersion7(), Id, formaPago, monto));

        // El efectivo siempre tiene corte (aunque solo exista el fondo).
        if (_cortes.All(c => c.FormaPago != FormaPagoEfectivo))
            _cortes.Add(new CajaSesionCorte(Guid.CreateVersion7(), Id, FormaPagoEfectivo, 0m));

        Estado = EstadoCajaSesion.EnArqueo;
    }

    /// <summary>
    /// Transición <c>EnArqueo → Abierta</c> (permiso <c>caja.supervisar</c>):
    /// falta registrar algo antes de cerrar. Los cortes se recalculan al
    /// volver a iniciar arqueo.
    /// </summary>
    public void Reabrir()
    {
        if (Estado != EstadoCajaSesion.EnArqueo)
            throw new BusinessRuleException(
                "SESION_NO_EN_ARQUEO",
                $"Solo una sesión EnArqueo puede reabrirse (actual: {Estado}).");

        Estado = EstadoCajaSesion.Abierta;
    }

    /// <summary>
    /// Transición <c>EnArqueo → Cerrada</c> (§5.1 paso 4): captura el contado
    /// físico de efectivo y congela montos, diferencia (solo efectivo,
    /// `[Decisión 12-5]`) y desglose por forma. Inmutable después.
    /// </summary>
    public void Cerrar(
        decimal efectivoDeclarado,
        string? notasCierre,
        bool cierreExtemporaneo,
        DateTimeOffset fechaCierre)
    {
        if (Estado != EstadoCajaSesion.EnArqueo)
            throw new BusinessRuleException(
                "SESION_NO_EN_ARQUEO",
                $"Solo una sesión EnArqueo puede cerrarse (actual: {Estado}).");
        if (efectivoDeclarado < 0)
            throw new BusinessRuleException("SESION_DECLARADO_INVALIDO", "El efectivo declarado no puede ser negativo.");

        var corteEfectivo = _cortes.Single(c => c.FormaPago == FormaPagoEfectivo);
        corteEfectivo.DeclararMonto(efectivoDeclarado);

        EfectivoDeclarado = efectivoDeclarado;
        EfectivoTeorico = corteEfectivo.MontoSistema;
        Diferencia = efectivoDeclarado - corteEfectivo.MontoSistema;
        NotasCierre = string.IsNullOrWhiteSpace(notasCierre) ? null : notasCierre.Trim();
        CierreExtemporaneo = cierreExtemporaneo;
        FechaCierre = fechaCierre;
        Estado = EstadoCajaSesion.Cerrada;
    }
}

/// <summary>
/// Desglose por forma de pago del corte de una sesión (12-cajas.md §7). Tabla
/// <c>facturacion.caja_sesion_corte</c>, UNIQUE (sesión, forma). Solo el
/// efectivo (<c>01</c>) lleva declarado/diferencia; el resto es informativo.
/// </summary>
public sealed class CajaSesionCorte : BaseEntity
{
    public Guid CajaSesionId { get; private set; }
    public string FormaPago { get; private set; } = string.Empty;
    public decimal MontoSistema { get; private set; }
    public decimal? MontoDeclarado { get; private set; }

    private CajaSesionCorte() { }

    internal CajaSesionCorte(Guid id, Guid cajaSesionId, string formaPago, decimal montoSistema) : base(id)
    {
        if (string.IsNullOrWhiteSpace(formaPago) || formaPago.Trim().Length > 2)
            throw new BusinessRuleException("CORTE_FORMA_PAGO_INVALIDA", "La forma de pago debe ser clave SAT c_FormaPago de 2 caracteres.");

        CajaSesionId = cajaSesionId;
        FormaPago = formaPago.Trim();
        MontoSistema = montoSistema;
    }

    internal void DeclararMonto(decimal monto) => MontoDeclarado = monto;
}
