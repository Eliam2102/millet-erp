using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.EstadoCuenta;
using Millet.CuentasPorCobrar.Domain.Ports.Correo;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Reportes;

/// <summary>
/// C7.8 (F1-CXC-09): estado de cuenta enviado al cliente, bloqueado
/// mientras haya pagos aplicados sin REP o REP emitidos sin reflejar en
/// cartera (CA7.14).
/// </summary>
public sealed class EstadoCuentaEnvioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid ClienteId = Guid.NewGuid();
    private static readonly byte[] Pdf = "%PDF-1.4 estado de cuenta"u8.ToArray();

    private static ReppPendienteResumen PagoSinRep(decimal monto = 1_160m) =>
        new(Guid.NewGuid(), "Pendiente", new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 5),
            monto, "MXN", "SPEI 123", null);

    [Fact]
    public async Task Pendientes_lista_los_pagos_sin_REP()
    {
        using var db = CrearDbContext();
        var pago = PagoSinRep();
        var handler = new PendientesEstadoCuentaHandler(db, new FakeReppPort(new(true, [], [pago])));

        var r = await handler.Handle(new PendientesEstadoCuentaQuery(ClienteId), default);

        r.Emitible.Should().BeFalse();
        r.Pendientes.Should().ContainSingle();
        r.Pendientes[0].Id.Should().Be(pago.Id);
        r.Pendientes[0].Monto.Should().Be(1_160m);
        r.Pendientes[0].Moneda.Should().Be("MXN");
        r.Pendientes[0].Referencia.Should().Be("SPEI 123");
        r.Pendientes[0].Motivo.Should().Be("SinRep");
        r.RecibosSinReflejar.Should().BeEmpty();
    }

    [Fact]
    public async Task Pendientes_sin_bloqueo_es_emitible_y_sugiere_correo_del_cliente()
    {
        using var db = CrearDbContext();
        var handler = new PendientesEstadoCuentaHandler(db, new FakeReppPort(new(false, [])), new FakeClientePort());

        var r = await handler.Handle(new PendientesEstadoCuentaQuery(ClienteId), default);

        r.Emitible.Should().BeTrue();
        r.Pendientes.Should().BeEmpty();
        r.EmailCliente.Should().Be("cobranza@vidriosdelgolfo.mx");
    }

    [Fact]
    public async Task Enviar_con_pendientes_lanza_ESTADO_CUENTA_REPP_PENDIENTE_y_no_registra()
    {
        using var db = CrearDbContext();
        var correo = new FakeCorreo();
        var handler = CrearHandler(db, new(true, [], [PagoSinRep()]), correo);

        var accion = () => handler.Handle(Comando(), default);

        (await accion.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ESTADO_CUENTA_REPP_PENDIENTE");
        (await db.EstadoCuentaEnvios.CountAsync()).Should().Be(0);
        correo.Llamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Recibo_emitido_no_reflejado_bloquea_envio()
    {
        using var db = CrearDbContext();
        var recibo = Guid.NewGuid();
        var handler = CrearHandler(db, new(false, [recibo]), new FakeCorreo());

        var accion = () => handler.Handle(Comando(), default);

        (await accion.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ESTADO_CUENTA_REPP_PENDIENTE");
        (await db.EstadoCuentaEnvios.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Enviar_sin_pendientes_registra_envio_y_llama_puerto_correo()
    {
        using var db = CrearDbContext();
        var factura = FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            Guid.NewGuid().ToString(), "FV-90", 10_000m, "MXN", "PPD", Ahora.AddDays(-30), Ahora.AddDays(15));
        db.FacturasCartera.Add(factura);
        await db.SaveChangesAsync();
        var correo = new FakeCorreo();
        var handler = CrearHandler(db, new(false, []), correo);

        var r = await handler.Handle(Comando(), default);

        r.Estado.Should().Be("Enviado");
        r.Destinatario.Should().Be("cobranza@cliente.mx");
        r.NumeroFilas.Should().Be(1);
        r.PdfSha256.Should().HaveLength(64);
        r.Totales.Should().ContainSingle(t => t.Moneda == "MXN" && t.Cargo == 10_000m && t.Saldo == 10_000m);
        var guardado = await db.EstadoCuentaEnvios.SingleAsync();
        guardado.Estado.Should().Be(EstadoEnvioEstadoCuenta.Enviado);
        guardado.UsuarioNombre.Should().Be("Uziel");
        correo.Llamadas.Should().ContainSingle();
        correo.Llamadas[0].RazonSocialCliente.Should().Be("Vidrios del Golfo");
        correo.Llamadas[0].Pdf.Should().Equal(Pdf);
    }

    [Fact]
    public async Task Registro_del_envio_usa_totales_del_reporte_sin_anticipos()
    {
        using var db = CrearDbContext();
        var factura = FacturaCartera.Crear(
            EmpresaId, Guid.NewGuid(), ClienteId, "VGL860910IU4", "Cliente",
            Guid.NewGuid().ToString(), "FV-92", 11_600m, "MXN", "PPD", Ahora.AddDays(-20), Ahora.AddDays(10));
        factura.AplicarPago(5_000m);
        db.FacturasCartera.Add(factura);
        db.MovimientosCartera.Add(new MovimientoCartera(
            EmpresaId, factura.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), 5_000m, Ahora.AddDays(-3)));
        await db.SaveChangesAsync();
        var anticipos = new[]
        {
            new AnticipoSaldoClienteDto(Guid.NewGuid(), ClienteId, "Abierto", 2_000m, 0m, 2_000m, 2_000m, "MXN", "PED-9"),
        };
        var handler = CrearHandler(db, new(false, []), new FakeCorreo(), anticipos);

        var r = await handler.Handle(Comando(), default);

        // El anticipo es informativo: no entra al abono, igual que en el reporte.
        var mxn = r.Totales.Should().ContainSingle(t => t.Moneda == "MXN").Subject;
        mxn.Cargo.Should().Be(11_600m);
        mxn.Abono.Should().Be(5_000m);
        mxn.Saldo.Should().Be(6_600m);
        (mxn.Cargo - mxn.Abono).Should().Be(mxn.Saldo);
        var guardado = await db.EstadoCuentaEnvios.SingleAsync();
        guardado.TotalesJson.Should().Contain("5000").And.NotContain("7000");
    }

    [Fact]
    public async Task Sin_transporte_de_correo_queda_Registrado()
    {
        using var db = CrearDbContext();
        var handler = CrearHandler(db, new(false, []), new FakeCorreo(enviar: false));

        var r = await handler.Handle(Comando(), default);

        r.Estado.Should().Be("Registrado");
        (await db.EstadoCuentaEnvios.SingleAsync()).Estado.Should().Be(EstadoEnvioEstadoCuenta.Registrado);
    }

    [Fact]
    public async Task Fallo_de_correo_deja_estado_Fallido_y_conserva_registro()
    {
        using var db = CrearDbContext();
        var handler = CrearHandler(db, new(false, []), new FakeCorreo(falla: true));

        var r = await handler.Handle(Comando(), default);

        r.Estado.Should().Be("Fallido");
        r.DetalleError.Should().Contain("SMTP caído");
        var guardado = await db.EstadoCuentaEnvios.SingleAsync();
        guardado.Estado.Should().Be(EstadoEnvioEstadoCuenta.Fallido);
    }

    [Fact]
    public async Task Pendientes_fuera_de_alcance_de_sucursal_es_403()
    {
        using var db = CrearDbContext();
        var handler = new PendientesEstadoCuentaHandler(db, new FakeReppPort(new(false, [])));

        var accion = () => handler.Handle(
            new PendientesEstadoCuentaQuery(ClienteId) { DocumentosPermitidos = [Guid.NewGuid()] }, default);

        (await accion.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("SUCURSAL_FUERA_DE_ALCANCE");
    }

    [Fact]
    public async Task Historial_lista_envios_mas_reciente_primero()
    {
        using var db = CrearDbContext();
        var handler = CrearHandler(db, new(false, []), new FakeCorreo());
        await handler.Handle(Comando("a@cliente.mx"), default);
        await handler.Handle(Comando("b@cliente.mx"), default);

        var lista = await new ListarEnviosEstadoCuentaHandler(db)
            .Handle(new ListarEnviosEstadoCuentaQuery(ClienteId), default);

        lista.Should().HaveCount(2);
        lista[0].Fecha.Should().BeOnOrAfter(lista[1].Fecha);
    }

    [Theory]
    [InlineData("no-es-correo", "application/pdf", 10, false)]
    [InlineData("cobranza@cliente.mx", "image/png", 10, false)]
    [InlineData("cobranza@cliente.mx", "application/pdf", 0, false)]
    [InlineData("cobranza@cliente.mx", "application/pdf", EnviarEstadoCuentaValidator.TamanoMaximoBytes + 1, false)]
    [InlineData("cobranza@cliente.mx", "application/pdf", 10, true)]
    public void Validador_exige_correo_valido_y_pdf_acotado(string destinatario, string contentType, int bytes, bool valido)
    {
        var pdf = new byte[bytes];
        if (bytes >= 5) "%PDF-"u8.ToArray().CopyTo(pdf, 0);
        var resultado = new EnviarEstadoCuentaValidator().Validate(
            new EnviarEstadoCuentaCommand(ClienteId, destinatario, null, "estado-cuenta.pdf", contentType, pdf));
        resultado.IsValid.Should().Be(valido);
    }

    private static EnviarEstadoCuentaCommand Comando(string destinatario = "cobranza@cliente.mx") =>
        new(ClienteId, destinatario, "Adjunto su estado de cuenta.", "estado-cuenta.pdf", "application/pdf", Pdf);

    private static EnviarEstadoCuentaHandler CrearHandler(
        CuentasPorCobrarDbContext db, ReppClienteEstado estado, FakeCorreo correo,
        IReadOnlyList<AnticipoSaldoClienteDto>? anticipos = null) =>
        new(db, new FakeAnticipos(anticipos), new FakeClientePort(), new FakeClock(), new FakeEmpresaContext(),
            new FakeUser(), correo, new FakeReppPort(estado));

    private static CuentasPorCobrarDbContext CrearDbContext()
    {
        var options = new DbContextOptionsBuilder<CuentasPorCobrarDbContext>()
            .UseInMemoryDatabase(databaseName: $"cxc_edocta_envio_{Guid.NewGuid()}")
            .Options;
        return new CuentasPorCobrarDbContext(options, new FakeEmpresaContext());
    }

    private sealed class FakeCorreo(bool enviar = true, bool falla = false) : IEstadoCuentaCorreoPort
    {
        public List<CorreoEstadoCuenta> Llamadas { get; } = [];

        public Task<bool> EnviarAsync(CorreoEstadoCuenta correo, CancellationToken cancellationToken)
        {
            Llamadas.Add(correo);
            if (falla) throw new InvalidOperationException("SMTP caído");
            return Task.FromResult(enviar);
        }
    }

    private sealed class FakeReppPort(ReppClienteEstado estado) : IReppPendientesReadPort
    {
        public Task<ReppClienteEstado> ConsultarAsync(Guid clienteId, CancellationToken cancellationToken) =>
            Task.FromResult(estado);
    }

    private sealed class FakeAnticipos(IReadOnlyList<AnticipoSaldoClienteDto>? anticipos = null) : IFacturacionAnticiposReadPort
    {
        public Task<IReadOnlyList<AnticipoSaldoClienteDto>> ListarPorClienteAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult(anticipos ?? []);
    }

    private sealed class FakeClientePort : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(null);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) =>
            Task.FromResult<ClienteRefDto?>(
                new ClienteRefDto(clienteId, "VGL860910IU4", "Vidrios del Golfo", EsGenerico: false,
                    Email: "cobranza@vidriosdelgolfo.mx"));
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(
            string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private sealed class FakeUser : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "Uziel";
    }

    private sealed class FakeEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }
}
