using Millet.Integraciones.Fiscal.Application.Configuracion.TestConexionPac;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.Integraciones.Fiscal.Infrastructure.Cifrado;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

public sealed class TestConexionPacHandlerTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Ahora = new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Stub mínimo del <see cref="IFiscalApiSdkClient"/>. Solo implementa
    /// <see cref="IFiscalApiSdkClient.PingAsync"/>; el resto lanza
    /// <c>NotImplementedException</c> — los demás métodos no se usan
    /// desde el handler de test conexión.
    /// </summary>
    private sealed class StubSdk : IFiscalApiSdkClient
    {
        public Guid UltimaEmpresa { get; private set; }
        public PingResultDto Resultado { get; set; } = new(true, 200, "OK", 12, default);

        public Task<PingResultDto> PingAsync(Guid empresaId, CancellationToken cancellationToken)
        {
            UltimaEmpresa = empresaId;
            return Task.FromResult(Resultado);
        }

        public Task<PersonExterno> AsegurarPersonAsync(Guid empresaId, string rfc, string legalName,
            string zipCode, string satTaxRegimeCode, string? satCfdiUseCode, string email,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SincronizarPersonAsync(Guid empresaId, string personIdExterno, string legalName,
            string zipCode, string? satCfdiUseCode, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<TaxFilesSubidos> SubirTaxFilesAsync(Guid empresaId, string personIdExterno,
            string rfc, byte[] cerBytes, byte[] keyBytes, string password,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<DownloadRuleExternaDto> AsegurarDownloadRuleAsync(Guid empresaId,
            string personIdExterno, SatQueryType satQueryType, DownloadType downloadType,
            SatInvoiceStatusFilter satInvoiceStatus, string descripcion,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SolicitudDescargaExternaDto> CrearSolicitudAsync(Guid empresaId,
            string ruleIdExterno, DateTimeOffset startDate, DateTimeOffset endDate,
            CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<SolicitudDescargaExternaDto> ConsultarSolicitudAsync(Guid empresaId,
            string requestIdExterno, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<MetaItemDto> ListarMetaItemsAsync(Guid empresaId,
            string requestIdExterno, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public IAsyncEnumerable<XmlCfdiItemDto> ListarXmlsAsync(Guid empresaId,
            string requestIdExterno, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<EstatusUuidDto> ConsultarEstatusUuidAsync(Guid empresaId, string uuidCfdi,
            string rfcEmisor, string rfcReceptor, decimal total, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    [Fact]
    public async Task Ping_sin_config_persistida_no_actualiza_BD()
    {
        var db = InMemoryFiscalDb.Create();
        var sdk = new StubSdk();
        var clock = new InMemoryFiscalDb.FakeClock(Ahora);
        var handler = new TestConexionPacHandler(db, sdk, clock);

        var response = await handler.Handle(
            new TestConexionPacCommand(EmpresaId, ProveedorPac.FiscalApi,
                BaseUrl: "https://live.fiscalapi.com", ApiKey: "transient-key", TimeoutSegundos: 5),
            CancellationToken.None);

        response.Exitosa.Should().BeTrue();
        sdk.UltimaEmpresa.Should().Be(EmpresaId);
        db.ConfiguracionesPac.Count().Should().Be(0);
    }

    [Fact]
    public async Task Ping_con_config_persistida_actualiza_timestamp_y_flag()
    {
        var db = InMemoryFiscalDb.Create();
        var cipher = InMemoryFiscalDb.Cipher();
        var config = new ConfiguracionPac(
            id: Guid.NewGuid(),
            empresaId: EmpresaId,
            proveedor: ProveedorPac.FiscalApi,
            baseUrl: "https://live.fiscalapi.com",
            apiKeyCifrado: cipher.Encrypt("key-existente"),
            apiKeyHash: FiscalSecretCipher.HashForChangeDetection("key-existente"),
            ahora: Ahora.AddDays(-30));
        db.ConfiguracionesPac.Add(config);
        await db.SaveChangesAsync();

        var sdk = new StubSdk { Resultado = new(true, 200, "OK", 50, Ahora) };
        var clock = new InMemoryFiscalDb.FakeClock(Ahora);
        var handler = new TestConexionPacHandler(db, sdk, clock);

        await handler.Handle(
            new TestConexionPacCommand(EmpresaId, ProveedorPac.FiscalApi,
                BaseUrl: null, ApiKey: null, TimeoutSegundos: null),
            CancellationToken.None);

        var refreshed = db.ConfiguracionesPac.Single();
        refreshed.UltimaTestConexionAt.Should().Be(Ahora);
        refreshed.UltimaTestConexionExitosa.Should().BeTrue();
    }

    [Fact]
    public async Task Ping_fallido_persiste_flag_en_false()
    {
        var db = InMemoryFiscalDb.Create();
        var cipher = InMemoryFiscalDb.Cipher();
        db.ConfiguracionesPac.Add(new ConfiguracionPac(
            id: Guid.NewGuid(),
            empresaId: EmpresaId,
            proveedor: ProveedorPac.FiscalApi,
            baseUrl: "https://live.fiscalapi.com",
            apiKeyCifrado: cipher.Encrypt("key"),
            apiKeyHash: FiscalSecretCipher.HashForChangeDetection("key"),
            ahora: Ahora.AddDays(-1)));
        await db.SaveChangesAsync();

        var sdk = new StubSdk { Resultado = new(false, 401, "Unauthorized", 30, Ahora) };
        var handler = new TestConexionPacHandler(
            db, sdk, new InMemoryFiscalDb.FakeClock(Ahora));

        var response = await handler.Handle(
            new TestConexionPacCommand(EmpresaId, ProveedorPac.FiscalApi, null, null, null),
            CancellationToken.None);

        response.Exitosa.Should().BeFalse();
        response.StatusCode.Should().Be(401);
        db.ConfiguracionesPac.Single().UltimaTestConexionExitosa.Should().BeFalse();
    }
}
