using Millet.SharedKernel.Domain;

namespace Millet.Almacen.Domain.Consumo;

/// <summary>
/// Consumo de un componente por una pieza que A+W marcó terminada (A4.8, D2). Tabla
/// <c>almacen.consumos_componente_aw</c>: llave única (empresa, origen, etiqueta, operación, componente), así un
/// componente se descuenta una sola vez aunque el escaneo se vuelva a leer. También es la bandeja: estado
/// <see cref="EnRevision"/> (sin norma, sin factor, sin sucursal, sin artículo o sin existencia; se reintenta) o
/// <see cref="Excepcion"/> (pieza sin componentes legibles: solo se descuenta el vidrio, A4.8-a).
/// </summary>
public sealed class ConsumoComponenteAw : IPerteneceAEmpresa
{
    public const string Recibido = "RECIBIDO";
    public const string EnRevision = "EN_REVISION";
    public const string Excepcion = "EXCEPCION";
    public const string Tipo = "CONSUMO_COMPONENTE";

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; set; }
    public string Origen { get; private set; } = string.Empty;
    public string Etiqueta { get; private set; } = string.Empty;
    public int Operacion { get; private set; }
    /// <summary>Artículo A+W del componente; vacío = la pieza no trae componentes legibles.</summary>
    public string ComponenteRef { get; private set; } = string.Empty;
    /// <summary>Capas del mismo componente en la pieza (p. ej. dos PVB iguales en un laminado); multiplica el consumo.</summary>
    public int Capas { get; private set; } = 1;
    public string Auftnr { get; private set; } = string.Empty;
    public int Pos { get; private set; }
    public decimal? AnchoMm { get; private set; }
    public decimal? AltoMm { get; private set; }
    public string? ClaveSucursal { get; private set; }
    public DateTime BearbZeit { get; private set; }
    public string Estado { get; private set; } = string.Empty;
    public decimal? Cantidad { get; private set; }
    public string? Folio { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset RegistradoAt { get; private set; }
    public DateTimeOffset IntentadoAt { get; private set; }

    private ConsumoComponenteAw() { }

    public ConsumoComponenteAw(Guid empresaId, string origen, string etiqueta, int operacion, string componenteRef, int capas,
        string auftnr, int pos, decimal? anchoMm, decimal? altoMm, string? claveSucursal, DateTime bearbZeit)
    {
        Id = Guid.CreateVersion7();
        EmpresaId = empresaId; Origen = origen; Etiqueta = etiqueta; Operacion = operacion; ComponenteRef = componenteRef; Capas = capas;
        Auftnr = auftnr; Pos = pos; AnchoMm = anchoMm; AltoMm = altoMm; ClaveSucursal = claveSucursal;
        BearbZeit = DateTime.SpecifyKind(bearbZeit, DateTimeKind.Unspecified); // hora local de A+W, sin zona
        RegistradoAt = DateTimeOffset.UtcNow;
    }

    public void Resolver(string estado, decimal? cantidad, string? folio, string? error)
    {
        Estado = estado; Cantidad = cantidad; Folio = folio;
        Error = error?.Length > 1000 ? error[..1000] : error;
        IntentadoAt = DateTimeOffset.UtcNow;
    }
}
