using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.CuentasPorPagar.Domain.TarjetaCredito;

/// <summary>
/// Línea cruda extraída del archivo del banco al parsear el estado de
/// cuenta (§4.1, §6 anexo TC, F7-PR5). El algoritmo de conciliación
/// automática evalúa cada línea contra los <see cref="MovimientoTarjetaCredito"/>
/// pendientes y asigna un <see cref="ScoreMatch"/> + <see cref="EstadoMatch"/>.
/// </summary>
public sealed class LineaBancoTc : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public Guid EstadoCuentaTcId { get; private set; }

    /// <summary>Posición en el archivo original (fila Excel). Único por estado de cuenta.</summary>
    public int PosicionArchivo { get; private set; }

    public DateOnly FechaAplicacion { get; private set; }
    public decimal Monto { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    /// <summary>Monto en MXN según el TC del corte del banco.</summary>
    public decimal MontoMxn { get; private set; }

    public string MerchantRaw { get; private set; } = default!;
    public string MerchantNormalizado { get; private set; } = default!;

    public string? ReferenciaBanco { get; private set; }
    public string? TipoSegunBanco { get; private set; }    // 'Compra', 'Refund', 'Interes', 'Anualidad', 'Comision'

    public Guid? MovimientoTcId { get; private set; }
    public EstadoMatchLineaBanco EstadoMatch { get; private set; }
    public decimal? ScoreMatch { get; private set; }

    private LineaBancoTc() { }

    internal LineaBancoTc(
        Guid id,
        Guid empresaId,
        Guid estadoCuentaTcId,
        int posicionArchivo,
        DateOnly fechaAplicacion,
        decimal monto,
        string moneda,
        decimal montoMxn,
        string merchantRaw,
        string? referenciaBanco,
        string? tipoSegunBanco) : base(id)
    {
        if (string.IsNullOrWhiteSpace(merchantRaw))
            throw new BusinessRuleException("LIN_BANCO_MERCHANT_VACIO",
                "El nombre del comercio es obligatorio.");
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Length != 3)
            throw new BusinessRuleException("LIN_BANCO_MONEDA_INVALIDA",
                "La moneda debe ser código ISO 4217 de 3 letras.");
        if (posicionArchivo < 1)
            throw new BusinessRuleException("LIN_BANCO_POSICION_INVALIDA",
                "La posición en el archivo debe ser >= 1.");

        EmpresaId = empresaId;
        EstadoCuentaTcId = estadoCuentaTcId;
        PosicionArchivo = posicionArchivo;
        FechaAplicacion = fechaAplicacion;
        Monto = monto;
        Moneda = moneda.ToUpperInvariant();
        MontoMxn = montoMxn;
        MerchantRaw = merchantRaw.Trim();
        MerchantNormalizado = MovimientoTarjetaCredito.NormalizarMerchant(merchantRaw);
        ReferenciaBanco = referenciaBanco?.Trim();
        TipoSegunBanco = tipoSegunBanco?.Trim();
        EstadoMatch = EstadoMatchLineaBanco.Pendiente;
    }

    /// <summary>
    /// Marca la línea como matched contra un movimiento (auto o manual).
    /// Solo el handler de conciliación llama este método.
    /// </summary>
    internal void MarcarMatched(Guid movimientoTcId, decimal score)
    {
        if (movimientoTcId == Guid.Empty)
            throw new BusinessRuleException("LIN_BANCO_MOV_VACIO",
                "El movimiento a vincular es obligatorio.");
        if (score < 0m || score > 100m)
            throw new BusinessRuleException("LIN_BANCO_SCORE_INVALIDO",
                $"Score fuera de rango 0-100 (recibido: {score}).");

        MovimientoTcId = movimientoTcId;
        ScoreMatch = score;
        EstadoMatch = EstadoMatchLineaBanco.Matched;
    }

    /// <summary>
    /// Marca la línea como sugerencia pendiente (score 60-89).
    /// </summary>
    internal void MarcarSugerencia(Guid movimientoTcId, decimal score)
    {
        MovimientoTcId = movimientoTcId;
        ScoreMatch = score;
        EstadoMatch = EstadoMatchLineaBanco.Pendiente;
    }

    /// <summary>
    /// Marca la línea como sin sugerencia (score &lt; 60 o sin candidatos).
    /// </summary>
    internal void MarcarSinSugerencia(decimal? mejorScore)
    {
        MovimientoTcId = null;
        ScoreMatch = mejorScore;
        EstadoMatch = EstadoMatchLineaBanco.NoConciliado;
    }
}
