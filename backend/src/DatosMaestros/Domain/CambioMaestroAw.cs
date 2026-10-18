using System.Security.Cryptography;
using System.Text;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.DatosMaestros.Domain;

public enum TipoCambioMaestroAw : short { Aplicado = 0, EditadoEnErp = 1, RfcCompartido = 2, Baja = 3, Reactivacion = 4, Diferencia = 5 }

public enum EstadoCambioMaestroAw : short { Informativo = 0, Pendiente = 1, Resuelto = 2 }

/// <summary>
/// Bitácora y conflictos de la sincronización de maestros desde A+W (ADM-06):
/// una fila por campo cambiado, con antes/después, versión del evento (hash de origen) y ejecución.
/// </summary>
public sealed class CambioMaestroAw : BaseEntity, INotAudited
{
    private const int MaxValor = 400;

    public string TipoEntidad { get; private set; } = string.Empty;
    public Guid? EntidadId { get; private set; }
    public string ReferenciaExterna { get; private set; } = string.Empty;
    public string Campo { get; private set; } = string.Empty;
    public string? ValorAntes { get; private set; }
    public string? ValorDespues { get; private set; }
    public string Dueno { get; private set; } = string.Empty;
    public TipoCambioMaestroAw Tipo { get; private set; }
    public EstadoCambioMaestroAw Estado { get; private set; }
    public string Causa { get; private set; } = string.Empty;
    public string VersionEvento { get; private set; } = string.Empty;
    /// <summary>
    /// SHA-256 del valor anterior completo (sin recortar) más el hash de origen previo: distingue una misma transición
    /// repetida en otro momento (bloqueo→desbloqueo→bloqueo) de un reenvío idéntico del mismo evento.
    /// </summary>
    public string HuellaPrevia { get; private set; } = string.Empty;
    public string VersionMatriz { get; private set; } = string.Empty;
    public Guid? EjecucionId { get; private set; }
    public DateTime OcurridoEnUtc { get; private set; }
    public Guid? ResueltoPor { get; private set; }
    public DateTimeOffset? ResueltoEnUtc { get; private set; }
    public string? Resolucion { get; private set; }

    private CambioMaestroAw() { }

    public CambioMaestroAw(Guid id, string tipoEntidad, Guid? entidadId, string referenciaExterna, string campo,
        string? valorAntes, string? valorDespues, string dueno, TipoCambioMaestroAw tipo, EstadoCambioMaestroAw estado,
        string causa, string versionEvento, string versionMatriz, Guid? ejecucionId, DateTime ocurridoEnUtc,
        string? hashAnterior = null) : base(id)
    {
        if (tipoEntidad is not ("Cliente" or "ProductoAw")) throw Invalido(nameof(tipoEntidad));
        if (dueno is not ("AW" or "ERP")) throw Invalido(nameof(dueno));
        TipoEntidad = tipoEntidad;
        ReferenciaExterna = Requerido(referenciaExterna, 50, nameof(referenciaExterna));
        Campo = Requerido(campo, 60, nameof(campo));
        Causa = Requerido(causa, 500, nameof(causa));
        VersionEvento = Requerido(versionEvento, 64, nameof(versionEvento));
        VersionMatriz = Requerido(versionMatriz, 20, nameof(versionMatriz));
        HuellaPrevia = Huella(valorAntes, hashAnterior);
        EntidadId = entidadId; ValorAntes = Recortar(valorAntes); ValorDespues = Recortar(valorDespues);
        Dueno = dueno; Tipo = tipo; Estado = estado; EjecucionId = ejecucionId; OcurridoEnUtc = ocurridoEnUtc;
    }

    public static string Huella(string? valorAntes, string? hashAnterior) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{valorAntes}\u0001{hashAnterior}"))).ToLowerInvariant();

    public void Resolver(string resolucion, Guid usuario, DateTimeOffset ahora)
    {
        if (Estado != EstadoCambioMaestroAw.Pendiente)
            throw new BusinessRuleException("CONFLICTO_AW_YA_RESUELTO", "Este conflicto ya fue resuelto.");
        if (string.IsNullOrWhiteSpace(resolucion) || resolucion.Length > 40)
            throw new BusinessRuleException("CONFLICTO_AW_RESOLUCION_INVALIDA", "La resolución no es válida.");
        Estado = EstadoCambioMaestroAw.Resuelto; Resolucion = resolucion; ResueltoPor = usuario; ResueltoEnUtc = ahora;
    }

    /// <summary>Recorte que se aplica a los valores guardados; sirve para comparar contra lo ya registrado.</summary>
    public static string? Truncar(string? v) => v is { Length: > MaxValor } ? v[..MaxValor] : v;
    private static string? Recortar(string? v) => Truncar(v);
    private static string Requerido(string? v, int max, string nombre) =>
        string.IsNullOrWhiteSpace(v) || v.Length > max ? throw Invalido(nombre) : v;
    private static BusinessRuleException Invalido(string nombre) =>
        new("CAMBIO_AW_CAMPO_INVALIDO", $"{nombre} es requerido o excede su longitud.");
}
