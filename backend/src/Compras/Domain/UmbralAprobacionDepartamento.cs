namespace Millet.Compras.Domain;

/// <summary>
/// Configuración de umbral monetario por departamento para el motor de
/// autorización (§3.bis.2). Si el monto total de la requisición supera
/// este umbral, se requiere N1+N2; sino, solo N1.
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
    public decimal UmbralMonto { get; private set; }
    public string Moneda { get; private set; } = "MXN";

    private UmbralAprobacionDepartamento() { }

    public UmbralAprobacionDepartamento(
        Guid empresaId,
        Guid departamentoId,
        DateOnly vigenteDesde,
        decimal umbralMonto,
        string moneda = "MXN",
        DateOnly? vigenteHasta = null)
    {
        EmpresaId = empresaId;
        DepartamentoId = departamentoId;
        VigenteDesde = vigenteDesde;
        VigenteHasta = vigenteHasta;
        UmbralMonto = umbralMonto;
        Moneda = moneda;
    }
}
