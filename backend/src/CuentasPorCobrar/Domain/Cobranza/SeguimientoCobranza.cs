using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.Cobranza;

/// <summary>
/// Canal de la gestión de cobranza (§3.1 del 00-levantamiento).
/// Persistido como <c>short</c> con check constraint (incidente
/// 2026-07-11); valor nuevo = constraint + migration + mirror FE.
/// </summary>
public enum CanalCobranza : short
{
    Llamada = 1,
    Correo = 2,
    Whatsapp = 3,
}

/// <summary>Resultado de la gestión (§3.1).</summary>
public enum ResultadoCobranza : short
{
    /// <summary>Cliente comprometió monto y fecha (exige ambos, §4.2).</summary>
    PromesaPago = 1,

    SinRespuesta = 2,
    Excusa = 3,
    Otro = 4,
}

/// <summary>
/// Gestión individual de cobranza — historial auditable append-only por
/// cliente (§3.1 del 00-levantamiento, §4.2 del 01-diseño, CXC-PR5).
/// Nunca se edita ni borra: cada contacto con el cliente deja su rastro
/// tal cual ocurrió (el módulo se optimiza por auditabilidad, §1.3).
/// </summary>
public sealed class SeguimientoCobranza : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; private set; }
    public DateTimeOffset Fecha { get; private set; }
    public Guid UsuarioId { get; private set; }

    public CanalCobranza Canal { get; private set; }
    public ResultadoCobranza Resultado { get; private set; }

    /// <summary>Solo en promesa de pago (obligatorio ahí, §4.2).</summary>
    public decimal? MontoComprometido { get; private set; }

    /// <summary>Solo en promesa de pago (obligatoria ahí, §4.2).</summary>
    public DateOnly? FechaComprometida { get; private set; }

    public string Nota { get; private set; } = default!;

    private SeguimientoCobranza() { }

    public static SeguimientoCobranza Registrar(
        Guid empresaId,
        Guid clienteId,
        Guid usuarioId,
        CanalCobranza canal,
        ResultadoCobranza resultado,
        decimal? montoComprometido,
        DateOnly? fechaComprometida,
        string nota,
        DateTimeOffset fecha)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("SC_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (usuarioId == Guid.Empty)
            throw new BusinessRuleException("SC_USUARIO_VACIO", "El usuario gestor es obligatorio.");
        if (string.IsNullOrWhiteSpace(nota))
            throw new BusinessRuleException("SC_NOTA_VACIA", "La nota de la gestión es obligatoria.");

        if (resultado == ResultadoCobranza.PromesaPago)
        {
            if (montoComprometido is not > 0)
                throw new BusinessRuleException("SC_PROMESA_SIN_MONTO",
                    "Una promesa de pago exige el monto comprometido (> 0).");
            if (fechaComprometida is null)
                throw new BusinessRuleException("SC_PROMESA_SIN_FECHA",
                    "Una promesa de pago exige la fecha comprometida.");
            if (fechaComprometida.Value < DateOnly.FromDateTime(fecha.UtcDateTime))
                throw new BusinessRuleException("SC_PROMESA_FECHA_PASADA",
                    "La fecha comprometida no puede ser anterior a la gestión.");
        }
        else if (montoComprometido is not null || fechaComprometida is not null)
        {
            throw new BusinessRuleException("SC_COMPROMISO_SOBRANTE",
                "Monto y fecha comprometidos solo aplican a promesa de pago.");
        }

        return new SeguimientoCobranza
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            Fecha = fecha,
            UsuarioId = usuarioId,
            Canal = canal,
            Resultado = resultado,
            MontoComprometido = montoComprometido,
            FechaComprometida = fechaComprometida,
            Nota = nota.Trim(),
        };
    }
}
