using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using Millet.Tesoreria.Domain.Cuentas;

namespace Millet.Tesoreria.Domain.Movimientos;

/// <summary>
/// Movimiento bancario — agregado central del módulo (§4.2): todo peso
/// que entra o sale de una cuenta de Millet pasa por aquí.
///
/// <para>
/// El comportamiento llega por fases: alta manual de ingreso (PR-2, este
/// archivo); egresos ligados a pasivo (RN-1, RN-4) y reversa por
/// contramovimiento (RN-10) en PR-4; pago a cuenta con gate RN-2 en PR-6.
/// Inmutable una vez conciliado; la reversa nunca borra — genera
/// contramovimiento ligado vía <see cref="ContramovimientoDe"/>.
/// </para>
///
/// <para>
/// Invariantes clave (§4.2): <c>Moneda == CuentaBancaria.Moneda</c>
/// (RN-3); RN-2 respaldada con índice parcial único
/// <c>ux_pago_cuenta_abierto</c> (§5.2) además de la validación del
/// command.
/// </para>
/// </summary>
public sealed class MovimientoBancario : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }

    public Guid CuentaBancariaId { get; private set; }
    public SentidoMovimiento Sentido { get; private set; }
    public decimal Monto { get; private set; }
    public string Moneda { get; private set; } = default!;
    public DateOnly FechaValor { get; private set; }
    public string? ReferenciaBancaria { get; private set; }
    public Guid? ConceptoId { get; private set; }

    public EstadoAplicacionMovimiento EstadoAplicacion { get; private set; } = EstadoAplicacionMovimiento.NoAplicado;
    public EstadoConciliacionMovimiento EstadoConciliacion { get; private set; } = EstadoConciliacionMovimiento.NoConciliado;

    public BeneficiarioTipo? BeneficiarioTipo { get; private set; }

    /// <summary>Ref al Proveedor/Cliente de DatosMaestros según <see cref="BeneficiarioTipo"/>; nombres vía read-port (ADR-0042), nunca JOIN.</summary>
    public Guid? BeneficiarioRef { get; private set; }

    /// <summary>Movimiento original que este contramovimiento revierte (RN-10). NULL en movimientos normales.</summary>
    public Guid? ContramovimientoDe { get; private set; }

    /// <summary>Pago a cuenta (§3.4): motivo obligatorio del egreso sin documento ligado.</summary>
    public string? MotivoNoAplicado { get; private set; }

    public Guid CreadoPor { get; private set; }
    public DateTimeOffset CreadoEn { get; private set; }

    private MovimientoBancario() { }

    /// <summary>
    /// Alta manual de un movimiento de ingreso (§3.3 paso 2 / endpoint
    /// <c>POST movimientos</c>, PR-2). Los egresos NUNCA entran por aquí:
    /// solo vía pagos (RN-1, PR-4) o pago a cuenta (RN-2, PR-6).
    /// </summary>
    public static MovimientoBancario RegistrarIngreso(
        Guid empresaId,
        CuentaBancaria cuenta,
        decimal monto,
        DateOnly fechaValor,
        string? referenciaBancaria,
        Guid? conceptoId,
        BeneficiarioTipo? beneficiarioTipo,
        Guid? beneficiarioRef,
        Guid creadoPor,
        DateTimeOffset ahora)
    {
        if (!cuenta.Activa)
            throw new BusinessRuleException("MOV_CUENTA_INACTIVA",
                $"La cuenta bancaria '{cuenta.Banco} {Cuentas.NumeroCuenta.Crear(cuenta.NumeroCuenta).Enmascarado}' está inactiva.");
        if (monto <= 0)
            throw new BusinessRuleException("MOV_MONTO_INVALIDO", "El monto debe ser mayor a cero.");
        if (beneficiarioRef is not null && beneficiarioTipo is null)
            throw new BusinessRuleException("MOV_BENEFICIARIO_SIN_TIPO",
                "Si se indica la referencia del beneficiario, el tipo es obligatorio.");
        if (creadoPor == Guid.Empty)
            throw new BusinessRuleException("MOV_USUARIO_VACIO", "El usuario que registra es obligatorio.");

        return new MovimientoBancario
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CuentaBancariaId = cuenta.Id,
            Sentido = SentidoMovimiento.Ingreso,
            Monto = monto,
            // RN-3: la moneda del movimiento ES la de la cuenta — no se
            // acepta del caller, se copia (bloquea cross-moneda en MVP).
            Moneda = cuenta.Moneda,
            FechaValor = fechaValor,
            ReferenciaBancaria = NormalizarReferencia(referenciaBancaria),
            ConceptoId = conceptoId,
            EstadoAplicacion = EstadoAplicacionMovimiento.NoAplicado,
            EstadoConciliacion = EstadoConciliacionMovimiento.NoConciliado,
            BeneficiarioTipo = beneficiarioTipo,
            BeneficiarioRef = beneficiarioRef,
            CreadoPor = creadoPor,
            CreadoEn = ahora,
        };
    }

    /// <summary>
    /// Egreso por pago a proveedor contra pasivos autorizados (TES-PR4,
    /// §3.1). Nace <c>Aplicado</c>: el command garantiza que la suma de
    /// aplicaciones cubre el monto completo del movimiento. RN-1: solo se
    /// invoca con pasivos presentes en la bandeja (proyección del evento
    /// de CxP). RN-3: la moneda se copia de la cuenta.
    /// </summary>
    public static MovimientoBancario RegistrarPagoProveedor(
        Guid empresaId,
        CuentaBancaria cuenta,
        Guid proveedorId,
        decimal monto,
        DateOnly fechaValor,
        string? referenciaBancaria,
        Guid? conceptoId,
        Guid creadoPor,
        DateTimeOffset ahora)
    {
        if (!cuenta.Activa)
            throw new BusinessRuleException("MOV_CUENTA_INACTIVA",
                $"La cuenta bancaria '{cuenta.Banco}' está inactiva.");
        if (monto <= 0)
            throw new BusinessRuleException("MOV_MONTO_INVALIDO", "El monto debe ser mayor a cero.");
        if (proveedorId == Guid.Empty)
            throw new BusinessRuleException("MOV_PROVEEDOR_VACIO", "El proveedor es obligatorio.");
        if (creadoPor == Guid.Empty)
            throw new BusinessRuleException("MOV_USUARIO_VACIO", "El usuario que registra es obligatorio.");

        return new MovimientoBancario
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CuentaBancariaId = cuenta.Id,
            Sentido = SentidoMovimiento.Egreso,
            Monto = monto,
            Moneda = cuenta.Moneda,
            FechaValor = fechaValor,
            ReferenciaBancaria = NormalizarReferencia(referenciaBancaria),
            ConceptoId = conceptoId,
            // Nace Aplicado: las N AplicacionPagoProveedor se crean en la
            // misma transacción cubriendo el monto completo. Esto además lo
            // deja fuera del índice parcial RN-2 (que solo captura egresos
            // NoAplicado = pagos a cuenta).
            EstadoAplicacion = EstadoAplicacionMovimiento.Aplicado,
            EstadoConciliacion = EstadoConciliacionMovimiento.NoConciliado,
            BeneficiarioTipo = Movimientos.BeneficiarioTipo.Proveedor,
            BeneficiarioRef = proveedorId,
            CreadoPor = creadoPor,
            CreadoEn = ahora,
        };
    }

    /// <summary>
    /// Contramovimiento de reversa (RN-10): el original NO se toca ni se
    /// borra — se compensa con un movimiento de sentido inverso ligado vía
    /// <see cref="ContramovimientoDe"/>. Permite reversa parcial (por
    /// aplicación). El original conserva su estado de aplicación: la
    /// compensación vive en la pareja movimiento↔contramovimiento y en la
    /// marca <c>Revertida</c> de la aplicación.
    /// </summary>
    public MovimientoBancario CrearContramovimiento(
        decimal importe,
        DateOnly fechaValor,
        Guid creadoPor,
        DateTimeOffset ahora)
    {
        if (importe <= 0 || importe > Monto)
            throw new BusinessRuleException("MOV_REVERSA_IMPORTE_INVALIDO",
                "El importe a revertir debe ser mayor a cero y no exceder el monto del movimiento original.");
        if (ContramovimientoDe is not null)
            throw new BusinessRuleException("MOV_REVERSA_DE_REVERSA",
                "No se puede revertir un contramovimiento.");
        if (creadoPor == Guid.Empty)
            throw new BusinessRuleException("MOV_USUARIO_VACIO", "El usuario que registra es obligatorio.");

        return new MovimientoBancario
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = EmpresaId,
            CuentaBancariaId = CuentaBancariaId,
            Sentido = Sentido == SentidoMovimiento.Egreso
                ? SentidoMovimiento.Ingreso
                : SentidoMovimiento.Egreso,
            Monto = importe,
            Moneda = Moneda,
            FechaValor = fechaValor,
            ReferenciaBancaria = ReferenciaBancaria is null ? "REVERSA" : $"REVERSA {ReferenciaBancaria}",
            ConceptoId = ConceptoId,
            EstadoAplicacion = EstadoAplicacionMovimiento.Aplicado,
            EstadoConciliacion = EstadoConciliacionMovimiento.NoConciliado,
            BeneficiarioTipo = BeneficiarioTipo,
            BeneficiarioRef = BeneficiarioRef,
            ContramovimientoDe = Id,
            CreadoPor = creadoPor,
            CreadoEn = ahora,
        };
    }

    /// <summary>
    /// Pago a cuenta (TES-PR6, §3.4 / TES-2): egreso ejecutado en banca SIN
    /// documento ligado — el limbo se registra, no se oculta. Nace
    /// <c>NoAplicado</c> con motivo obligatorio; si el proveedor se conoce,
    /// entra al gate RN-2 (máximo un pago no aplicado abierto por
    /// proveedor — validación en el command + índice parcial único de
    /// respaldo). La reconciliación tardía llega por
    /// <see cref="ActualizarEstadoAplicacion"/> sin re-desembolso.
    /// </summary>
    public static MovimientoBancario RegistrarPagoACuenta(
        Guid empresaId,
        CuentaBancaria cuenta,
        Guid? proveedorId,
        decimal monto,
        DateOnly fechaValor,
        string? referenciaBancaria,
        Guid? conceptoId,
        string motivo,
        Guid creadoPor,
        DateTimeOffset ahora)
    {
        if (!cuenta.Activa)
            throw new BusinessRuleException("MOV_CUENTA_INACTIVA",
                $"La cuenta bancaria '{cuenta.Banco}' está inactiva.");
        if (monto <= 0)
            throw new BusinessRuleException("MOV_MONTO_INVALIDO", "El monto debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("MOV_MOTIVO_OBLIGATORIO",
                "El motivo es obligatorio en un pago a cuenta (egreso sin documento).");
        if (creadoPor == Guid.Empty)
            throw new BusinessRuleException("MOV_USUARIO_VACIO", "El usuario que registra es obligatorio.");

        return new MovimientoBancario
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CuentaBancariaId = cuenta.Id,
            Sentido = SentidoMovimiento.Egreso,
            Monto = monto,
            Moneda = cuenta.Moneda,
            FechaValor = fechaValor,
            ReferenciaBancaria = NormalizarReferencia(referenciaBancaria),
            ConceptoId = conceptoId,
            EstadoAplicacion = EstadoAplicacionMovimiento.NoAplicado,
            EstadoConciliacion = EstadoConciliacionMovimiento.NoConciliado,
            BeneficiarioTipo = proveedorId is null
                ? Movimientos.BeneficiarioTipo.Otro
                : Movimientos.BeneficiarioTipo.Proveedor,
            BeneficiarioRef = proveedorId,
            MotivoNoAplicado = motivo.Trim(),
            CreadoPor = creadoPor,
            CreadoEn = ahora,
        };
    }

    /// <summary>
    /// Deriva el estado de aplicación desde la suma de aplicaciones no
    /// revertidas (§4.2): 0 = NoAplicado, parcial = AplicadoParcial,
    /// completa = Aplicado. Usado por la liga tardía (TES-PR6): pasar de
    /// <c>NoAplicado</c> a parcial/aplicado libera el slot del gate RN-2
    /// (el índice parcial solo captura estado 1).
    /// </summary>
    public void ActualizarEstadoAplicacion(decimal sumaAplicada)
    {
        if (sumaAplicada < 0 || sumaAplicada > Monto)
            throw new BusinessRuleException("MOV_APLICACION_EXCEDE_MONTO",
                $"La suma aplicada {sumaAplicada:0.00} debe estar entre 0 y el monto del movimiento {Monto:0.00}.");

        EstadoAplicacion = sumaAplicada == 0
            ? EstadoAplicacionMovimiento.NoAplicado
            : sumaAplicada < Monto
                ? EstadoAplicacionMovimiento.AplicadoParcial
                : EstadoAplicacionMovimiento.Aplicado;
    }

    /// <summary>VO ReferenciaBancaria (§4.3): texto corto normalizado (trim + upper).</summary>
    private static string? NormalizarReferencia(string? referencia)
    {
        var r = referencia?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(r) ? null : r;
    }
}
