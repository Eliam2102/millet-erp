using System.Collections.Immutable;

namespace Millet.Documentos;

public sealed record EmpresaEncabezado
{
    private readonly byte[]? _logo;

    public EmpresaEncabezado(string razonSocial, string? nombreComercial, string rfc,
        string regimenFiscal, IEnumerable<string> domicilioLineas, byte[]? logo = null)
    {
        RazonSocial = razonSocial;
        NombreComercial = nombreComercial;
        Rfc = rfc;
        RegimenFiscal = regimenFiscal;
        DomicilioLineas = domicilioLineas.ToImmutableArray();
        _logo = logo?.ToArray();
    }

    public string RazonSocial { get; }
    public string? NombreComercial { get; }
    public string Rfc { get; }
    public string RegimenFiscal { get; }
    public ImmutableArray<string> DomicilioLineas { get; }
    public byte[]? Logo => _logo?.ToArray();
}

public sealed record DocumentoTitulo(string TituloEs, string? TituloEn, string SerieFolio,
    DateTime Fecha, string? Marca = null);

public sealed record RenglonTotal(string Etiqueta, decimal Importe, string Moneda, bool Resaltado = false);

public sealed record Totales
{
    public Totales(IEnumerable<RenglonTotal> renglones) => Renglones = renglones.ToImmutableArray();
    public ImmutableArray<RenglonTotal> Renglones { get; }
}
