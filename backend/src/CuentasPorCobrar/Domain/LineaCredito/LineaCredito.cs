using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorCobrar.Domain.LineaCredito;

/// <summary>
/// Línea de crédito por cliente (§3.1 del 00-levantamiento, §4.1-4.2 del
/// 01-diseño, CXC-PR1). Master data del módulo: define límite, moneda,
/// origen (SOLUNION / interno) y plazo con los que se evalúa el crédito
/// disponible y la liberación de pedidos.
///
/// <para>
/// Invariantes (§4.2): una sola línea <c>Activa</c> por
/// (<c>cliente_id</c>, <c>moneda</c>) — respaldada por índice único
/// parcial; <c>limite &gt; 0</c>; bloquear exige <c>motivo_bloqueo</c>.
/// El cliente es el master de DatosMaestros (ADR-0048 D6) — FK lógica
/// vía read port, nunca join físico.
/// </para>
/// </summary>
public sealed class LineaCredito : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public const string MonedaMxn = "MXN";
    public const string MonedaUsd = "USD";

    public Guid EmpresaId { get; set; }

    public Guid ClienteId { get; private set; }
    public string Moneda { get; private set; } = MonedaMxn;
    public decimal Limite { get; private set; }
    public OrigenLineaCredito Origen { get; private set; }
    public int PlazoDias { get; private set; }

    /// <summary>
    /// Clasificación del cliente por tipo de crédito — A/B/C/E
    /// (levantamiento §1.2, CXC-PR6): atributo capturado, no fórmula
    /// manual como en el Excel legacy. Nullable hasta que Crédito y
    /// Cobranza clasifique al cliente.
    /// </summary>
    public string? Clasificacion { get; private set; }

    public EstadoLineaCredito Estado { get; private set; }
    public string? MotivoBloqueo { get; private set; }

    private LineaCredito() { }

    public static LineaCredito Crear(
        Guid empresaId,
        Guid clienteId,
        string moneda,
        decimal limite,
        OrigenLineaCredito origen,
        int plazoDias,
        string? clasificacion = null)
    {
        if (clienteId == Guid.Empty)
            throw new BusinessRuleException("LC_CLIENTE_VACIO", "El cliente es obligatorio.");
        if (moneda is not (MonedaMxn or MonedaUsd))
            throw new BusinessRuleException("LC_MONEDA_INVALIDA",
                $"La moneda debe ser {MonedaMxn} o {MonedaUsd}.");
        if (limite <= 0)
            throw new BusinessRuleException("LC_LIMITE_INVALIDO",
                "El límite de crédito debe ser > 0.");
        if (plazoDias <= 0)
            throw new BusinessRuleException("LC_PLAZO_INVALIDO",
                "El plazo en días debe ser > 0.");
        ValidarClasificacion(clasificacion);

        return new LineaCredito
        {
            Id = Guid.CreateVersion7(),
            EmpresaId = empresaId,
            ClienteId = clienteId,
            Moneda = moneda,
            Limite = limite,
            Origen = origen,
            PlazoDias = plazoDias,
            Clasificacion = clasificacion,
            Estado = EstadoLineaCredito.Activa,
        };
    }

    /// <summary>
    /// Edición acotada del levantamiento §3.1: solo límite y plazo (sin
    /// CRUD prematuro — cliente, moneda y origen son inmutables; para
    /// cambiarlos se bloquea esta línea y se crea otra).
    /// </summary>
    public void ActualizarDatos(decimal limite, int plazoDias, string? clasificacion = null)
    {
        if (Estado == EstadoLineaCredito.Suspendida)
            throw new BusinessRuleException("LC_SUSPENDIDA_NO_EDITABLE",
                "Una línea Suspendida no se puede editar.");
        if (limite <= 0)
            throw new BusinessRuleException("LC_LIMITE_INVALIDO",
                "El límite de crédito debe ser > 0.");
        if (plazoDias <= 0)
            throw new BusinessRuleException("LC_PLAZO_INVALIDO",
                "El plazo en días debe ser > 0.");
        ValidarClasificacion(clasificacion);

        Limite = limite;
        PlazoDias = plazoDias;
        Clasificacion = clasificacion;
    }

    private static void ValidarClasificacion(string? clasificacion)
    {
        if (clasificacion is not null && clasificacion is not ("A" or "B" or "C" or "E"))
            throw new BusinessRuleException("LC_CLASIFICACION_INVALIDA",
                "La clasificación debe ser A, B, C o E.");
    }

    public void Bloquear(string motivo)
    {
        if (Estado != EstadoLineaCredito.Activa)
            throw new BusinessRuleException("LC_NO_BLOQUEABLE",
                $"Solo se bloquea desde Activa (actual: {Estado}).");
        if (string.IsNullOrWhiteSpace(motivo))
            throw new BusinessRuleException("LC_MOTIVO_BLOQUEO_VACIO",
                "El motivo de bloqueo es obligatorio.");

        Estado = EstadoLineaCredito.Bloqueada;
        MotivoBloqueo = motivo.Trim();
    }

    public void Desbloquear()
    {
        if (Estado != EstadoLineaCredito.Bloqueada)
            throw new BusinessRuleException("LC_NO_DESBLOQUEABLE",
                $"Solo se desbloquea desde Bloqueada (actual: {Estado}).");

        Estado = EstadoLineaCredito.Activa;
        MotivoBloqueo = null;
    }
}
