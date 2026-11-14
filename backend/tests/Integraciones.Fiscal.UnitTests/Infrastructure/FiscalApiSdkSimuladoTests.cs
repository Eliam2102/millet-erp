using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Infrastructure.SdkAdapter;

namespace Millet.Integraciones.Fiscal.UnitTests.Infrastructure;

/// <summary>M-293b: el SDK simulado completa el flujo de la descarga masiva y solo se acepta en Development. Datos ficticios.</summary>
public sealed class FiscalApiSdkSimuladoTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private const string Rfc = "ABC010101AB1";

    private sealed class Reloj(DateTimeOffset ahora) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = ahora;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private static async Task<(FiscalApiSdkSimulado Sdk, Reloj Reloj, string RequestId)> CrearSolicitud(SatQueryType q, DownloadType d)
    {
        var reloj = new Reloj(new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero));
        var sdk = new FiscalApiSdkSimulado(NullLogger<FiscalApiSdkSimulado>.Instance, reloj);
        var person = await sdk.AsegurarPersonAsync(Empresa, Rfc, "EMPRESA SIMULADA", "64000", "601", "G03", "a@b.c", default);
        var rule = await sdk.AsegurarDownloadRuleAsync(Empresa, person.IdExterno, q, d, SatInvoiceStatusFilter.Todos, "sim", default);
        var req = await sdk.CrearSolicitudAsync(Empresa, rule.IdExterno, new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 11, 23, 59, 59, TimeSpan.Zero), default);
        return (sdk, reloj, req.IdExterno);
    }

    [Fact]
    public async Task La_solicitud_pasa_de_en_proceso_a_completada_con_tres_cfdi()
    {
        var (sdk, reloj, id) = await CrearSolicitud(SatQueryType.Cfdi, DownloadType.Recibidos);

        var antes = await sdk.ConsultarSolicitudAsync(Empresa, id, default);
        antes.DownloadRequestStatusId.Should().Be(1);

        reloj.Ahora = reloj.Ahora.AddSeconds(FiscalApiSdkSimulado.SegundosParaCompletar + 1);
        var despues = await sdk.ConsultarSolicitudAsync(Empresa, id, default);
        despues.DownloadRequestStatusId.Should().Be(3);
        despues.InvoiceCount.Should().Be(3);
    }

    [Fact]
    public async Task Los_meta_items_y_los_xml_coinciden_por_uuid_con_dos_vigentes_y_un_cancelado()
    {
        var (sdk, _, id) = await CrearSolicitud(SatQueryType.Cfdi, DownloadType.Recibidos);

        var metas = await ToList(sdk.ListarMetaItemsAsync(Empresa, id, default));
        var xmls = await ToList(sdk.ListarXmlsAsync(Empresa, id, default));

        metas.Should().HaveCount(3);
        metas.Count(m => m.EstatusSat == "Vigente").Should().Be(2);
        metas.Count(m => m.EstatusSat == "Cancelado").Should().Be(1);
        metas.Select(m => m.Uuid).Should().BeEquivalentTo(xmls.Select(x => x.Uuid));
        metas.Should().OnlyContain(m => m.RfcReceptor == Rfc && m.RfcEmisor == FiscalApiSdkSimulado.RfcContraparte);
        System.Text.Encoding.UTF8.GetString(xmls[0].XmlBytes).Should().Contain($"UUID=\"{xmls[0].Uuid}\"").And.Contain("Version=\"4.0\"");
    }

    [Fact]
    public async Task Es_determinista_y_con_rule_de_metadata_no_hay_xml()
    {
        var (sdk, _, id) = await CrearSolicitud(SatQueryType.Metadata, DownloadType.Recibidos);
        var a = await ToList(sdk.ListarMetaItemsAsync(Empresa, id, default));
        var b = await ToList(sdk.ListarMetaItemsAsync(Empresa, id, default));
        a.Select(m => m.Uuid).Should().Equal(b.Select(m => m.Uuid));
        (await ToList(sdk.ListarXmlsAsync(Empresa, id, default))).Should().BeEmpty();
    }

    [Fact]
    public async Task El_estatus_por_uuid_es_consistente_con_el_meta_item()
    {
        var (sdk, _, id) = await CrearSolicitud(SatQueryType.Cfdi, DownloadType.Recibidos);
        foreach (var m in await ToList(sdk.ListarMetaItemsAsync(Empresa, id, default)))
        {
            var e = await sdk.ConsultarEstatusUuidAsync(Empresa, m.Uuid, m.RfcEmisor, m.RfcReceptor ?? "", m.Total, default);
            e.EstatusSat.Should().Be(m.EstatusSat);
        }
    }

    [Theory]
    [InlineData("Development", "Simulado", true)]
    [InlineData("Production", "Simulado", false)]
    [InlineData("QA", "Simulado", false)]
    [InlineData("Production", "Real", true)]
    [InlineData("Development", "Otro", false)]
    public void El_modo_simulado_solo_se_acepta_en_Development(string ambiente, string modo, bool valido)
    {
        var entorno = new Entorno(ambiente);
        var r = new SdkSimuladoSoloDevelopmentValidator(entorno).Validate(null, new FiscalApiSdkAdapterOptions { Modo = modo });
        r.Succeeded.Should().Be(valido);
    }

    private static async Task<List<T>> ToList<T>(IAsyncEnumerable<T> fuente)
    {
        var l = new List<T>();
        await foreach (var x in fuente) l.Add(x);
        return l;
    }

    private sealed class Entorno(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "/";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
