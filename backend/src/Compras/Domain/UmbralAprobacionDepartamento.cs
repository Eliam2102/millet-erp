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
/// El motor v0 (<c>EvaluadorMontoVsUmbral</c>) lee la fila vigente. Si no
/// existe, asume umbral = decimal.MaxValue (fail-open: solo N1 hasta que
/// se configure).
///
/// No implementa <see cref="Millet.SharedKernel.Domain.IAuditable"/> ni
/// <see cref="Millet.SharedKernel.Domain.IPerteneceAEmpresa"/>: los
/// cambios al catálogo se versionan vía la PK compuesta y la EmpresaId
/// es parte de la PK explícita (no necesita query filter).
/// </summary>
public sealed class UmbralAprobacionDepartamento
{
    public Guid EmpresaId { get; private set; }
    public Guid DepartamentoId { get; private set; }
    public DateOnly VigenteDesde { get; private set; }
    public DateOnly? VigenteHasta { get; private set; }
    public decimal? UmbralMenorCuantia { get; private set; }
    public decimal UmbralMonto { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    private UmbralAprobacionDepartamento() { }

    public UmbralAprobacionDepartamento(
        Guid empresaId,
        Guid departamentoId,
        DateOnly vigenteDesde,
        decimal umbralMonto,
        string moneda = "MXN",
        DateOnly? vigenteHasta = null,
        decimal? umbralMenorCuantia = null)
    {
        Validar(umbralMenorCuantia, umbralMonto);

        EmpresaId = empresaId;
        DepartamentoId = departamentoId;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        UmbralMenorCuantia = umbralMenorCuantia;
        UmbralMonto = umbralMonto;
        Moneda = moneda;
    }

    /// <summary>
    /// Reemplaza los montos de esta fila (mismo <c>VigenteDesde</c>). Usar
    /// solo sobre la fila que se creó hoy; cambios de días previos
    /// versionan con una fila nueva.
    /// </summary>
    public void ActualizarMontos(decimal? umbralMenorCuantia, decimal umbralMonto, string moneda)
    {
        Validar(umbralMenorCuantia, umbralMonto);

        UmbralMenorCuantia = umbralMenorCuantia;
        UmbralMonto = umbralMonto;
        Moneda = moneda;
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

    private static void Validar(decimal? umbralMenorCuantia, decimal umbralMonto)
    {
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
