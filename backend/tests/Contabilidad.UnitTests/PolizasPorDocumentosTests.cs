using FluentAssertions;
using Millet.Contabilidad.Application.Polizas;

namespace Millet.Contabilidad.UnitTests;

/// <summary>P19: la columna «Póliza» de las bandejas consulta las pólizas de una página en un solo lote.</summary>
public sealed class PolizasPorDocumentosTests
{
    private static readonly PolizasPorDocumentosValidator Validador = new();

    [Fact]
    public void Acepta_un_lote_de_facturas_de_venta()
    {
        var ids = Enumerable.Range(0, PolizasPorDocumentosValidator.Maximo).Select(_ => Guid.NewGuid()).ToList();
        Validador.Validate(new PolizasPorDocumentosQuery("facturacion", "comprobante", ids)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Rechaza_lote_vacio_demasiado_grande_o_tipo_no_admitido()
    {
        var demasiados = Enumerable.Range(0, PolizasPorDocumentosValidator.Maximo + 1).Select(_ => Guid.NewGuid()).ToList();
        Validador.Validate(new PolizasPorDocumentosQuery("facturacion", "comprobante", [])).IsValid.Should().BeFalse();
        Validador.Validate(new PolizasPorDocumentosQuery("facturacion", "comprobante", demasiados)).IsValid.Should().BeFalse();
        Validador.Validate(new PolizasPorDocumentosQuery("compras", "orden", [Guid.NewGuid()])).IsValid.Should().BeFalse();
    }
}
