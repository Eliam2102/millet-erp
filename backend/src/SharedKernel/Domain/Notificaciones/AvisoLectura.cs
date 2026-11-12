using Millet.SharedKernel.Domain;

namespace Millet.SharedKernel.Domain.Notificaciones;

public sealed class AvisoLectura : IPerteneceAEmpresa, INotAudited
{
    public Guid UsuarioId { get; private set; }
    public Guid EmpresaId { get; set; }
    public string ClaveOcurrencia { get; private set; } = string.Empty;
    public long PrimeraDeteccionSec { get; private set; }
    public DateTimeOffset? LeidoAt { get; private set; }

    private AvisoLectura() { }

    public AvisoLectura(Guid usuarioId, Guid empresaId, string claveOcurrencia, DateTimeOffset? leidoAt = null)
    {
        if (usuarioId == Guid.Empty || empresaId == Guid.Empty)
            throw new ArgumentException("Usuario y empresa son obligatorios.");
        if (claveOcurrencia.Length != 64 || claveOcurrencia.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("La clave debe ser una huella SHA-256.", nameof(claveOcurrencia));
        UsuarioId = usuarioId;
        EmpresaId = empresaId;
        ClaveOcurrencia = claveOcurrencia;
        LeidoAt = leidoAt;
    }

    public void MarcarLeido(DateTimeOffset ahora) => LeidoAt ??= ahora;
}
