using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Tesoreria.Domain.Corridas;

/// <summary>
/// Parámetro de firmas de la corrida (T9.1-A): desde <see cref="MontoDesde"/> (inclusive, «al alcanzar»,
/// plano 7.2 paso 5) y por moneda, la corrida necesita <see cref="FirmasRequeridas"/> firmas y, si se
/// indica, al menos una de un usuario con el rol <see cref="RolRequerido"/> (código de rol).
/// Sin escalón aplicable basta una firma. «2 firmas, una del Gerente» es propuesta de VILO, no regla de Millet.
/// </summary>
public sealed class PoliticaFirmaCorrida : BaseEntity, IPerteneceAEmpresa, IAuditable
{
    public Guid EmpresaId { get; set; }
    public string Moneda { get; private set; } = default!;
    public decimal MontoDesde { get; private set; }
    public int FirmasRequeridas { get; private set; }
    public string? RolRequerido { get; private set; }

    private PoliticaFirmaCorrida() { }

    public PoliticaFirmaCorrida(Guid empresaId, string moneda, decimal montoDesde, int firmasRequeridas, string? rolRequerido) : base(Guid.CreateVersion7())
    {
        if (string.IsNullOrWhiteSpace(moneda) || moneda.Trim().Length != 3)
            throw new BusinessRuleException("CORRIDA_POLITICA_MONEDA", "La moneda debe ser un código de 3 letras.");
        if (montoDesde < 0 || firmasRequeridas is < 1 or > 10)
            throw new BusinessRuleException("CORRIDA_POLITICA_INVALIDA", "El monto no puede ser negativo y las firmas deben ser de 1 a 10.");
        EmpresaId = empresaId; Moneda = moneda.Trim().ToUpperInvariant(); MontoDesde = montoDesde;
        FirmasRequeridas = firmasRequeridas;
        RolRequerido = string.IsNullOrWhiteSpace(rolRequerido) ? null : rolRequerido.Trim().ToLowerInvariant();
    }

    /// <summary>Valores DEMO de la ficha 86e3ke5bt (a confirmar con Gerardo López).</summary>
    public static PoliticaFirmaCorrida Demo(Guid empresaId) => new(empresaId, "MXN", 500_000m, 2, "gerente");

    /// <summary>Requisito para <paramref name="total"/>: el escalón con mayor <see cref="MontoDesde"/> ≤ total.</summary>
    public static (int Firmas, string? Rol) Requisito(IEnumerable<PoliticaFirmaCorrida> escalones, decimal total)
    {
        var e = escalones.Where(x => total >= x.MontoDesde).OrderByDescending(x => x.MontoDesde).FirstOrDefault();
        return e is null ? (1, null) : (e.FirmasRequeridas, e.RolRequerido);
    }
}
