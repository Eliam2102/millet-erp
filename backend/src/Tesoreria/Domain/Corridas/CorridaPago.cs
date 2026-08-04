using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Corrida de pagos (§4.5, TES-8): lote de pasivos seleccionados para
/// desembolso, autorizable como unidad vía la matriz de autorización. El
/// oficio de cartera es artefacto de la corrida autorizada (ADR-0036).
///
/// <para>
/// TES-PR1 define solo la forma persistida; la máquina de estados
/// (RN-5: <c>Borrador → EnAutorizacion → Autorizada → Ejecutada →
/// Cerrada</c>; <c>Rechazada</c>/<c>Cancelada</c> solo desde los dos
/// primeros; ejecutar requiere <c>Autorizada</c>, parcial permitido) y
/// los comandos llegan en PR-5 [gate T-G4].
/// </para>
/// </summary>
public sealed class CorridaPago : BaseEntity, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }

    public Guid CuentaBancariaId { get; private set; }
    public EstadoCorridaPago Estado { get; private set; } = EstadoCorridaPago.Borrador;
    public decimal Total { get; private set; }
    public string Moneda { get; private set; } = default!;
    public Guid SolicitadaPor { get; private set; }
    public Guid? AutorizadaPor { get; private set; }
    public DateTimeOffset? OficioGeneradoEn { get; private set; }
    public DateTimeOffset CreadaEn { get; private set; }

    private readonly List<CorridaPagoLinea> _lineas = [];
    public IReadOnlyCollection<CorridaPagoLinea> Lineas => _lineas.AsReadOnly();

    private CorridaPago() { }
}

/// <summary>Estados de la corrida (§4.2 del 01-diseño; RN-5).</summary>
public enum EstadoCorridaPago : short
{
    Borrador = 1,
    EnAutorizacion = 2,
    Autorizada = 3,
    Ejecutada = 4,
    Cerrada = 5,
    Rechazada = 6,
    Cancelada = 7,
}
