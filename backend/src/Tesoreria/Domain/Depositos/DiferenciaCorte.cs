using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Depositos;

/// <summary>
/// Faltante o sobrante del efectivo de un corte de caja (Millet 23-oct, Fernando Alejos): lo autoriza
/// Tesorería y va a «deudores funcionarios y empleados». Nace al cerrar el corte con diferencia
/// distinta de cero (sin tolerancia). <see cref="Importe"/> lleva signo: negativo = faltante,
/// positivo = sobrante. Ciclo: PendienteAutorizar → Autorizada | PorAclarar (rechazo con motivo);
/// un faltante autorizado se salda con devoluciones del cajero (Saldada / SaldadaParcial).
/// </summary>
public sealed class DiferenciaCorte : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid CajaSesionId { get; private set; }
    public Guid CajaId { get; private set; }
    public Guid SucursalId { get; private set; }
    public Guid CajeroId { get; private set; }
    public DateOnly DiaCorte { get; private set; }

    /// <summary>Contado − esperado: negativo faltante, positivo sobrante.</summary>
    public decimal Importe { get; private set; }

    public EstadoDiferenciaCorte Estado { get; private set; } = EstadoDiferenciaCorte.PendienteAutorizar;
    public string? MotivoRechazo { get; private set; }

    /// <summary>Suma de devoluciones de faltante del cajero (en un corte posterior).</summary>
    public decimal ImporteDevuelto { get; private set; }

    public Guid? ResueltaPor { get; private set; }
    public DateTimeOffset? ResueltaEn { get; private set; }

    public bool EsFaltante => Importe < 0;
    public decimal PendientePorDevolver => EsFaltante ? -Importe - ImporteDevuelto : 0m;

    /// <summary>El corte solo puede conciliarse cuando Tesorería ya autorizó la diferencia.</summary>
    public bool Autorizada => Estado is EstadoDiferenciaCorte.Autorizada or EstadoDiferenciaCorte.Saldada
        or EstadoDiferenciaCorte.SaldadaParcial;

    private DiferenciaCorte() { }

    public static DiferenciaCorte Crear(Guid empresaId, Guid cajaSesionId, Guid cajaId, Guid sucursalId,
        Guid cajeroId, DateOnly diaCorte, decimal importe)
    {
        if (cajaSesionId == Guid.Empty)
            throw new BusinessRuleException("DIF_CAJA_SESION_VACIA", "La sesión de caja es obligatoria.");
        if (cajeroId == Guid.Empty)
            throw new BusinessRuleException("DIF_CAJERO_VACIO", "El cajero responsable es obligatorio.");
        if (importe == 0)
            throw new BusinessRuleException("DIF_IMPORTE_CERO", "Un corte sin diferencia no genera diferencia pendiente.");

        return new DiferenciaCorte
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            CajaSesionId = cajaSesionId,
            CajaId = cajaId,
            SucursalId = sucursalId,
            CajeroId = cajeroId,
            DiaCorte = diaCorte,
            Importe = importe,
        };
    }

    /// <summary>Autoriza (o re-autoriza una «Por aclarar»). Si el cajero ya devolvió algo, nace saldada.</summary>
    public void Autorizar(Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado is not (EstadoDiferenciaCorte.PendienteAutorizar or EstadoDiferenciaCorte.PorAclarar))
            throw new BusinessRuleException("DIF_YA_RESUELTA", $"La diferencia ya fue autorizada (estado actual: {Estado}).");
        AsegurarOtroUsuario(usuarioId);

        Estado = EstadoDiferenciaCorte.Autorizada;
        MotivoRechazo = null;
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
        RecalcularSaldo();
    }

    public void Rechazar(string motivo, Guid usuarioId, DateTimeOffset ahora)
    {
        if (Estado != EstadoDiferenciaCorte.PendienteAutorizar)
            throw new BusinessRuleException("DIF_YA_RESUELTA", $"Solo una diferencia pendiente puede rechazarse (estado actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("DIF_MOTIVO_VACIO", "El motivo de rechazo es obligatorio.");
        AsegurarOtroUsuario(usuarioId);

        Estado = EstadoDiferenciaCorte.PorAclarar;
        MotivoRechazo = motivo.Trim();
        ResueltaPor = usuarioId;
        ResueltaEn = ahora;
    }

    /// <summary>
    /// Abona una devolución de faltante. Siempre se acumula; el estado se recalcula solo si la
    /// diferencia ya está autorizada (si no, lo hace <see cref="Autorizar"/>).
    /// </summary>
    public void AplicarDevolucion(decimal importe)
    {
        if (!EsFaltante)
            throw new BusinessRuleException("DIF_NO_ES_FALTANTE", "Solo un faltante admite devoluciones.");
        if (importe <= 0)
            throw new BusinessRuleException("DIF_DEVOLUCION_INVALIDA", "La devolución debe ser mayor a cero.");
        if (importe > PendientePorDevolver)
            throw new BusinessRuleException("DIF_DEVOLUCION_EXCEDE",
                $"La devolución ({importe:0.00}) excede el faltante pendiente ({PendientePorDevolver:0.00}).");

        ImporteDevuelto += importe;
        if (Autorizada) RecalcularSaldo();
    }

    private void RecalcularSaldo()
    {
        if (ImporteDevuelto <= 0) return;
        Estado = PendientePorDevolver == 0 ? EstadoDiferenciaCorte.Saldada : EstadoDiferenciaCorte.SaldadaParcial;
    }

    // El cajero responsable no puede resolver su propia diferencia (se valida en el servidor).
    private void AsegurarOtroUsuario(Guid usuarioId)
    {
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("DIF_USUARIO_VACIO", "El usuario que resuelve es obligatorio.");
        if (usuarioId == CajeroId)
            throw new BusinessRuleException("DIF_CAJERO_MISMO_USUARIO",
                "El cajero responsable del corte no puede resolver su propia diferencia. Solicítalo a otra persona de Tesorería.");
    }
}

/// <summary>Estados de la diferencia de corte (1=Pendiente de autorizar … 5=Saldada parcialmente).</summary>
public enum EstadoDiferenciaCorte : short
{
    PendienteAutorizar = 1,
    Autorizada = 2,

    /// <summary>Tesorería la rechazó con motivo; queda por aclarar.</summary>
    PorAclarar = 3,
    Saldada = 4,
    SaldadaParcial = 5,
}
