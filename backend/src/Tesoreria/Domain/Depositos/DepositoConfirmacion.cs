using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Movimientos;

namespace Millet.Tesoreria.Domain.Depositos;

/// <summary>
/// Confirmación de depósito de cliente (§4.6, TES-9): liga un
/// <c>MovimientoBancario</c> de ingreso con la propuesta de aplicación de
/// CxC (o con la expectativa de depósito de una sesión de caja de
/// Facturación). Tesorería confirma el hecho bancario, no el fiscal — la
/// aplicación real a cartera ocurre cuando CxC consume el timbrado del
/// REPP (<see cref="ReppTimbrado"/> cierra ese ciclo).
///
/// <para>
/// TES-PR7: ciclo <c>Pendiente → Confirmada/Rechazada</c>. RN-6:
/// confirmar exige un movimiento de INGRESO identificado (registrado en
/// el libro, no contramovimiento, aún sin aplicar). El rechazo lleva
/// motivo y publica <c>propuesta-aplicacion.rechazada.v1</c> [T-G7].
/// Dos orígenes:
/// (a) propuesta de CxC (<see cref="PropuestaCxcId"/>) — al confirmar se
/// publica <c>pago-cliente.confirmado.v1</c> y Facturación emite el REPP;
/// (b) expectativa de Caja (<see cref="CajaSesionId"/>) — solo cierra el
/// ciclo Caja→Banco, sin evento (el hecho fiscal del mostrador ya lo
/// cubrió el cobro de caja).
/// </para>
/// </summary>
public sealed class DepositoConfirmacion : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid? PropuestoPor { get; private set; }
    public decimal SaldoAFavorPorIdentificar { get; private set; }

    /// <summary>
    /// Movimiento bancario ligado. Confirmada: el que confirmó el depósito.
    /// Pendiente (C7.4): el depósito sin identificar que CxC identificó desde su
    /// bandeja — solo es la sugerencia que la UI preselecciona
    /// (<see cref="MovimientoSugeridoId"/>); el rechazo la borra.
    /// </summary>
    public Guid? MovimientoId { get; private set; }

    /// <summary>C7.4: movimiento identificado por CxC mientras la propuesta espera confirmación.</summary>
    public Guid? MovimientoSugeridoId => Estado == EstadoDepositoConfirmacion.Pendiente ? MovimientoId : null;

    public Guid? PropuestaCxcId { get; private set; }

    /// <summary>Cliente de la propuesta CxC; null en expectativas de Caja.</summary>
    public Guid? ClienteId { get; private set; }

    public EstadoDepositoConfirmacion Estado { get; private set; } = EstadoDepositoConfirmacion.Pendiente;
    public string? MotivoRechazo { get; private set; }

    /// <summary>Expectativa de depósito de Caja (<c>facturacion.caja-sesion.cerrada.v1</c>).</summary>
    public Guid? CajaSesionId { get; private set; }

    /// <summary>
    /// Solicitud de viáticos origen (GI-PR4, doc 12 §D4/Q3): expectativa
    /// del depósito con que el empleado devuelve la diferencia negativa
    /// de su liquidación. Null para propuestas CxC y expectativas de Caja.
    /// </summary>
    public Guid? SolicitudViaticosId { get; private set; }

    /// <summary>Se marca al consumir <c>facturacion.recibo-pago.timbrado.v1</c> (ciclo fiscal cerrado).</summary>
    public bool ReppTimbrado { get; private set; }

    /// <summary>JSON de (FacturaVentaId, Folio, ImporteAplicado)[] de la propuesta.</summary>
    public string FacturasJson { get; private set; } = "[]";

    /// <summary>Referencia del depósito según el remittance de la propuesta CxC.</summary>
    public string? DepositoRef { get; private set; }

    /// <summary>
    /// Monto que debe aparecer en banco: exacto para propuestas
    /// (MontoDeposito ya trae el ajuste no fiscal descontado en CxC);
    /// aproximado para expectativas de Caja (efectivo declarado — el
    /// fondo del día siguiente y diferencias se resuelven en conciliación).
    /// </summary>
    public decimal? MontoEsperado { get; private set; }

    public string? Moneda { get; private set; }

    public Guid? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }

    private DepositoConfirmacion() { }

    /// <summary>Proyección de <c>cuentas_por_cobrar.propuesta-aplicacion.creada.v1</c> (listener PR-7).</summary>
    public static DepositoConfirmacion CrearDesdePropuesta(
        Guid empresaId,
        Guid propuestaCxcId,
        Guid clienteId,
        string depositoRef,
        decimal montoDeposito,
        string moneda,
        string facturasJson, Guid? propuestoPor = null, decimal saldoAFavorPorIdentificar = 0,
        Guid? movimientoSugeridoId = null)
    {
        if (propuestaCxcId == Guid.Empty)
            throw new BusinessRuleException("DEP_PROPUESTA_VACIA", "La propuesta de CxC es obligatoria.");
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("DEP_CLIENTE_VACIO", "El cliente de la propuesta es obligatorio.");
        if (montoDeposito <= 0)
            throw new BusinessRuleException("DEP_MONTO_INVALIDO", "El monto del depósito debe ser mayor a cero.");

        return new DepositoConfirmacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            PropuestaCxcId = propuestaCxcId,
            PropuestoPor = propuestoPor,
            SaldoAFavorPorIdentificar = saldoAFavorPorIdentificar,
            ClienteId = clienteId,
            DepositoRef = depositoRef?.Trim(),
            MontoEsperado = montoDeposito,
            Moneda = moneda,
            FacturasJson = string.IsNullOrWhiteSpace(facturasJson) ? "[]" : facturasJson,
            Estado = EstadoDepositoConfirmacion.Pendiente,
            MovimientoId = movimientoSugeridoId,
        };
    }

    /// <summary>
    /// Expectativa de depósito de Caja (§3.3 paso 5): sesión cerrada con
    /// efectivo X → debe aparecer un depósito ~X. Cierra
    /// PLATFORM-TODO(&lt;TesoreriaCajaSesion&gt;).
    /// </summary>
    public static DepositoConfirmacion CrearExpectativaCaja(
        Guid empresaId,
        Guid cajaSesionId,
        decimal efectivoDeclarado,
        DateOnly diaOperacion)
    {
        if (cajaSesionId == Guid.Empty)
            throw new BusinessRuleException("DEP_CAJA_SESION_VACIA", "La sesión de caja es obligatoria.");
        if (efectivoDeclarado <= 0)
            throw new BusinessRuleException("DEP_MONTO_INVALIDO",
                "Una sesión sin efectivo declarado no genera expectativa de depósito.");

        return new DepositoConfirmacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CajaSesionId = cajaSesionId,
            DepositoRef = $"CAJA {diaOperacion:yyyy-MM-dd}",
            MontoEsperado = efectivoDeclarado,
            // Las cajas operan efectivo MXN (12-cajas.md); si llega
            // multimoneda de caja algún día, vendrá en el evento.
            Moneda = "MXN",
            Estado = EstadoDepositoConfirmacion.Pendiente,
        };
    }

    /// <summary>
    /// Expectativa del depósito con que un empleado devuelve la
    /// diferencia negativa de su liquidación de viáticos (GI-PR4,
    /// doc 12 §D4/Q3). Conciliable como cualquier expectativa RN-6.
    /// </summary>
    public static DepositoConfirmacion CrearExpectativaViaticos(
        Guid empresaId,
        Guid solicitudViaticosId,
        decimal montoEsperado,
        string moneda)
    {
        if (solicitudViaticosId == Guid.Empty)
            throw new BusinessRuleException("DEP_SOLICITUD_VIATICOS_VACIA",
                "La solicitud de viáticos es obligatoria.");
        if (montoEsperado <= 0)
            throw new BusinessRuleException("DEP_MONTO_INVALIDO",
                "Una liquidación sin diferencia por devolver no genera expectativa de depósito.");

        return new DepositoConfirmacion
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            SolicitudViaticosId = solicitudViaticosId,
            DepositoRef = $"VIATICOS {solicitudViaticosId.ToString()[..8].ToUpperInvariant()}",
            MontoEsperado = montoEsperado,
            Moneda = moneda,
            Estado = EstadoDepositoConfirmacion.Pendiente,
        };
    }

    /// <summary>
    /// RN-6: confirmar = ligar un movimiento bancario de INGRESO
    /// identificado. Para propuestas de CxC el monto debe coincidir con lo
    /// DISPONIBLE del movimiento (C7.4: monto menos lo ya confirmado; en un
    /// movimiento sin aplicar es su monto completo). Si CxC identificó este
    /// mismo movimiento desde su bandeja, la propuesta puede cubrir solo una
    /// parte y el resto sigue por identificar. La discrepancia se resuelve
    /// rechazando para que CxC re-proponga. Las expectativas de Caja y
    /// viáticos ligan el movimiento completo (monto aproximado; solo se exige
    /// la moneda).
    /// </summary>
    /// <param name="disponible">
    /// Lo que falta por aplicar del movimiento según las confirmaciones previas
    /// (lo calcula el handler). <c>null</c> = el movimiento completo si aún no
    /// se ha aplicado.
    /// </param>
    public void Confirmar(MovimientoBancario movimiento, Guid usuarioId, DateTimeOffset ahora, decimal? disponible = null)
    {
        AsegurarPendiente();
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("DEP_USUARIO_VACIO", "El usuario que confirma es obligatorio.");
        if (PropuestaCxcId is not null && (PropuestoPor is null || PropuestoPor == Guid.Empty))
            throw new BusinessRuleException("DEP_PROPONENTE_NO_IDENTIFICADO", "La propuesta no identifica quién la creó. Recházala y solicita una nueva a CxC.");
        if (PropuestoPor == usuarioId)
            throw new BusinessRuleException("DEP_MISMO_USUARIO", "Quien propone el cobro no puede confirmarlo. Solicita la confirmación a otra persona de Tesorería.");
        if (movimiento.Sentido != SentidoMovimiento.Ingreso)
            throw new BusinessRuleException("DEP_MOVIMIENTO_NO_INGRESO",
                "Solo un movimiento bancario de ingreso puede confirmar un depósito (RN-6).");
        if (movimiento.ContramovimientoDe is not null)
            throw new BusinessRuleException("DEP_MOVIMIENTO_ES_REVERSA",
                "Un contramovimiento de reversa no puede confirmar un depósito.");
        // E8 (ficha 26-oct): la propuesta que CxC ligó a un ingreso se confirma contra ese
        // mismo movimiento. Sin liga sigue la regla anterior (cualquier ingreso disponible).
        if (PropuestaCxcId is not null && MovimientoId is Guid ligado && movimiento.Id != ligado)
            throw new BusinessRuleException("DEP_MOVIMIENTO_DISTINTO_AL_LIGADO",
                "Esta propuesta de CxC está ligada a otro ingreso bancario. Confírmala contra ese movimiento o recházala para que CxC re-proponga.");

        var porAplicar = movimiento.EstadoAplicacion switch
        {
            EstadoAplicacionMovimiento.Aplicado => 0m,
            EstadoAplicacionMovimiento.NoAplicado => disponible ?? movimiento.Monto,
            _ => disponible ?? 0m,
        };
        // Caja y viáticos ligan el movimiento completo: no se confirman contra un sobrante.
        if (porAplicar <= 0 || (PropuestaCxcId is null && movimiento.EstadoAplicacion != EstadoAplicacionMovimiento.NoAplicado))
            throw new BusinessRuleException("DEP_MOVIMIENTO_YA_APLICADO",
                "El movimiento ya está aplicado a otro documento.");
        if (Moneda is not null && movimiento.Moneda != Moneda)
            throw new BusinessRuleException("DEP_MONEDA_DISTINTA",
                $"El movimiento es {movimiento.Moneda} y el depósito esperado es {Moneda}.");
        if (PropuestaCxcId is not null && MontoEsperado is decimal esperado && esperado != porAplicar
            && !(MovimientoId == movimiento.Id && esperado < porAplicar))
            throw new BusinessRuleException("DEP_MONTO_NO_COINCIDE",
                porAplicar == movimiento.Monto
                    ? $"El movimiento ({movimiento.Monto:0.00}) no coincide con el depósito propuesto ({esperado:0.00}); " +
                      "si el banco recibió otro importe, rechaza la propuesta para que CxC re-proponga."
                    : $"Lo disponible del movimiento ({porAplicar:0.00} de {movimiento.Monto:0.00}) no coincide con el depósito propuesto ({esperado:0.00}); " +
                      "si el banco recibió otro importe, rechaza la propuesta para que CxC re-proponga.");

        MovimientoId = movimiento.Id;
        Estado = EstadoDepositoConfirmacion.Confirmada;
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    /// <summary>
    /// Importe del movimiento que esta confirmación aplica (C7.4). Propuesta de
    /// CxC: el depósito propuesto menos el saldo a favor por identificar (ese
    /// sobrante sigue disponible en el movimiento). Caja y viáticos: el
    /// movimiento completo.
    /// </summary>
    public decimal ImporteAplicadoDe(decimal montoMovimiento) =>
        PropuestaCxcId is not null && MontoEsperado is decimal esperado
            ? Math.Min(montoMovimiento, Math.Max(0m, esperado - SaldoAFavorPorIdentificar))
            : montoMovimiento;

    public void Rechazar(string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        AsegurarPendiente();
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("DEP_USUARIO_VACIO", "El usuario que rechaza es obligatorio.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DEP_MOTIVO_VACIO", "El motivo de rechazo es obligatorio.");

        Estado = EstadoDepositoConfirmacion.Rechazada;
        MotivoRechazo = motivo.Trim();
        MovimientoId = null; // la sugerencia de CxC (C7.4) deja de aplicar
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    /// <summary>Ciclo fiscal cerrado: Facturación timbró el REPP del cobro confirmado.</summary>
    public void MarcarReppTimbrado()
    {
        if (Estado != EstadoDepositoConfirmacion.Confirmada)
            throw new BusinessRuleException("DEP_NO_CONFIRMADA",
                "Solo una confirmación puede marcarse con REPP timbrado.");
        ReppTimbrado = true;
    }

    private void AsegurarPendiente()
    {
        if (Estado != EstadoDepositoConfirmacion.Pendiente)
            throw new BusinessRuleException("DEP_YA_RESUELTA",
                $"Solo un depósito pendiente puede resolverse (estado actual: {Estado}).");
    }
}

/// <summary>Estados de la confirmación de depósito (§5 DDL: 1=Pendiente 2=Confirmada 3=Rechazada).</summary>
public enum EstadoDepositoConfirmacion : short
{
    Pendiente = 1,
    Confirmada = 2,
    Rechazada = 3,
}
