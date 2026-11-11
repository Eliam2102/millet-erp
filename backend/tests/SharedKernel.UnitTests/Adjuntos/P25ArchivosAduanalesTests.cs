using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.SharedKernel.UnitTests.Adjuntos;
public sealed class P25ArchivosAduanalesTests
{
    [Theory]
    [InlineData("M1234567.232", "500|MUESTRA|\r\n501|DATOS|\r\n800|FIN|\r\n", true)]
    [InlineData("M1234567.232", "500|MUESTRA|\r\n501|SIN-CIERRE|", false)]
    [InlineData("M1234567.232", "500|texto\0binario\n800|FIN|", false)]
    [InlineData("archivo.exe", "500|MUESTRA|\n800|FIN|", false)]
    public async Task M_original_se_valida_solo_en_expediente_CE(string nombre, string contenido, bool valido)
    {
        var politica = new AdjuntosPoliticaOptions();
        using var stream = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(contenido));
        var cargar = () => politica.ParaEntidad("expediente_ce").ValidarContenidoAsync(nombre, "application/octet-stream", stream, default);
        if (valido) await cargar.Should().NotThrowAsync();
        else await cargar.Should().ThrowAsync<BusinessRuleException>();
        var otroModulo = () => politica.ParaEntidad("requisicion").ValidarContenidoAsync(nombre, "application/octet-stream", stream, default);
        await otroModulo.Should().ThrowAsync<BusinessRuleException>();
        stream.Position.Should().Be(0);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><muestra/>", true)]
    [InlineData("<muestra>", false)]
    [InlineData("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///no-existe'>]><x>&e;</x>", false)]
    public async Task Xml_se_parsea_completo_sin_resolver_entidades(string xml, bool valido)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var cargar = () => new AdjuntosPoliticaOptions().ParaEntidad("expediente_ce").ValidarContenidoAsync("muestra.xml", "application/xml", stream, default);
        if (valido) await cargar.Should().NotThrowAsync(); else await cargar.Should().ThrowAsync<BusinessRuleException>();
        stream.Position.Should().Be(0);
    }
}
