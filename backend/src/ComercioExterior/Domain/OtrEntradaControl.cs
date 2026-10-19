using Millet.SharedKernel.Domain;
namespace Millet.ComercioExterior.Domain;
public sealed class OtrEntradaControl : BaseEntity, IAuditable, IPerteneceAEmpresa
{
    public Guid EmpresaId { get; set; }
    public string IdOrigen { get; private set; } = "";
    public long VersionOrigen { get; private set; }
    public int Intentos { get; private set; }
    public bool Rechazada { get; private set; }
    public string? UltimoError { get; private set; }
    private OtrEntradaControl() { }
    public OtrEntradaControl(Guid empresa, string origen, long version) : base(Guid.CreateVersion7())
    { EmpresaId = empresa; IdOrigen = origen; VersionOrigen = version; }
    public void Intentar() => Intentos++;
    public void Fallar(string motivo, bool definitivo, int maxIntentos)
    { UltimoError = motivo[..Math.Min(motivo.Length, 500)]; Rechazada = definitivo || Intentos >= maxIntentos; }
}
