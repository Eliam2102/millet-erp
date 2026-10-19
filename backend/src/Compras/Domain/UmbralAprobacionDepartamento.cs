using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Domain;

/// <summary>
/// Configuración de umbrales monetarios por departamento para el motor de
/// autorización (§3.bis.2). Dos niveles:
/// <list type="bullet">
///   <item><b>N1 (<see cref="UmbralMenorCuantia"/>)</b>: si el monto total
///         de la requisición es menor o igual a este valor, es compra
///         menor (aprobación rápida). <c>null</c> = sin umbral N1.</item>
///   <item><b>N2 (<see cref="UmbralMonto"/>)</b>: si el monto total supera
///         este valor, se requiere N1+N2; sino, solo N1.</item>
/// </list>
///
/// Vigencia con <c>VigenteDesde</c> y <c>VigenteHasta</c> (NULL = vigente).
/// PK compuesta para permitir histórico por cambios.
///
/// Sin fila vigente del departamento se usa la fila general
/// (<c>DepartamentoId = Guid.Empty</c>); sin ninguna, la RQ y la OC exigen N2
/// (fail-safe).
///
/// <see cref="ModoNivel2"/>: modo de nivel 2 de la requisición para este
/// departamento (default <see cref="Domain.ModoNivel2.SoloDisponibilidad"/>: quien pide no conoce el precio; el monto se evalúa en la OC).
/// <see cref="MontoMinimoDisponibilidad"/>: importe mínimo a comprar por
/// disponibilidad para exigir N2 en modo SoloDisponibilidad/Ambos; <c>null</c>
/// = sin mínimo.
///
/// Implementa <see cref="Millet.SharedKernel.Domain.IAuditable"/> (marcador):
/// cada alta o cambio queda en <c>core.audit_log</c>. No implementa
/// <see cref="Millet.SharedKernel.Domain.IPerteneceAEmpresa"/>: la EmpresaId
/// es parte de la PK explícita (no necesita query filter).
/// </summary>
public sealed class UmbralAprobacionDepartamento : Millet.SharedKernel.Domain.IAuditable
{
    public Guid EmpresaId { get; private set; }
    public Guid DepartamentoId { get; private set; }
    public DateOnly VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }
    public decimal? UmbralMenorCuantia { get; private set; }
    public decimal UmbralMonto { get; private set; }
    public string Moneda { get; private set; } = "MXN";
    public ModoNivel2 ModoNivel2 { get; private set; } = ModoNivel2.SoloDisponibilidad;
    public decimal? MontoMinimoDisponibilidad { get; private set; }

    private UmbralAprobacionDepartamento() { }

    public UmbralAprobacionDepartamento(
        Guid empresaId,
        Guid departamentoId,
        DateOnly vigenteDesde,
        decimal umbralMonto,
        string moneda = "MXN",
        DateOnly? vigenteHasta = null,
        decimal? umbralMenorCuantia = null,
        ModoNivel2 modoNivel2 = ModoNivel2.SoloDisponibilidad,
        decimal? montoMinimoDisponibilidad = null)
    {
        Validar(umbralMenorCuantia, umbralMonto, modoNivel2, montoMinimoDisponibilidad);

        EmpresaId = empresaId;
        DepartamentoId = departamentoId;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        UmbralMenorCuantia = umbralMenorCuantia;
        UmbralMonto = umbralMonto;
        Moneda = moneda;
        ModoNivel2 = modoNivel2;
        MontoMinimoDisponibilidad = montoMinimoDisponibilidad;
    }

    /// <summary>
    /// Reemplaza los montos de esta fila (mismo <c>VigenteDesde</c>). Usar
    /// solo sobre la fila que se creó hoy; cambios de días previos
    /// versionan con una fila nueva.
    /// </summary>
    public void ActualizarMontos(decimal? umbralMenorCuantia, decimal umbralMonto, string moneda,
        ModoNivel2 modoNivel2 = ModoNivel2.SoloDisponibilidad, decimal? montoMinimoDisponibilidad = null)
    {
        Validar(umbralMenorCuantia, umbralMonto, modoNivel2, montoMinimoDisponibilidad);

        UmbralMenorCuantia = umbralMenorCuantia;
        UmbralMonto = umbralMonto;
        Moneda = moneda;
        ModoNivel2 = modoNivel2;
        MontoMinimoDisponibilidad = montoMinimoDisponibilidad;
    }

    /// <summary>Cierra la vigencia. <paramref name="hasta"/> no puede ser anterior a <c>VigenteDesde</c>.</summary>
    public void CerrarVigencia(DateOnly hasta)
    {
        if (VigenteHasta is not null) return;
        if (hasta < VigenteDesde)
            throw new BusinessRuleException(
                "UMBRAL_CIERRE_INVALIDO",
                "VigenteHasta no puede ser anterior a VigenteDesde.");

        VigenteHasta = hasta;
    }

    private static void Validar(
        decimal? umbralMenorCuantia, decimal umbralMonto,
        ModoNivel2 modoNivel2, decimal? montoMinimoDisponibilidad)
    {
        if (!Enum.IsDefined(modoNivel2))
            throw new ValidationException([new ValidationError(
                nameof(ModoNivel2), "UMBRAL_MODO_NIVEL2_INVALIDO", "El modo de nivel 2 no es válido.")]);

        if (montoMinimoDisponibilidad < 0)
            throw new ValidationException([new ValidationError(
                nameof(MontoMinimoDisponibilidad), "UMBRAL_MONTO_MINIMO_NEGATIVO",
                "El monto mínimo por disponibilidad no puede ser negativo.")]);

        if (umbralMonto < 0)
            throw new ValidationException([new ValidationError(
                nameof(UmbralMonto), "UMBRAL_MONTO_NEGATIVO", "El umbral N2 no puede ser negativo.")]);

        if (umbralMenorCuantia is decimal n1)
        {
            if (n1 < 0)
                throw new ValidationException([new ValidationError(
                    nameof(UmbralMenorCuantia), "UMBRAL_MENOR_CUANTIA_NEGATIVO", "El umbral N1 no puede ser negativo.")]);

            if (n1 >= umbralMonto)
                throw new ValidationException([new ValidationError(
                    nameof(UmbralMenorCuantia), "UMBRAL_MENOR_CUANTIA_NO_MENOR",
                    "El umbral N1 (menor cuantía) debe ser menor que el umbral N2.")]);
        }
    }
}
