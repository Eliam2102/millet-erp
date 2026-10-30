using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Corridas;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Corridas;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Corridas;

/// <summary>T9.1-B (86e3jvvhf): oficio de la corrida autorizada y registro de la ejecución; CA1 (pagos y oficio), CA3 y CA6 de la ficha.</summary>
public sealed class CorridasEjecucionTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Armador = Guid.NewGuid();
    private static readonly Guid Jefe = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 30);

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    /// <summary>Corrida AUTORIZADA con un pasivo por (proveedor, monto).</summary>
    private static async Task<(TesoreriaDbContext Db, CorridaResponse Corrida, List<PasivoPendientePago> Pasivos)> Autorizada(params (Guid Proveedor, decimal Monto)[] items)
    {
        var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var pasivos = items.Select(x => CorridaPagoDominioTests.Pasivo(x.Proveedor, x.Monto, x.Monto, 0m, 0m, empresa: Empresa)).ToList();
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.AddRange(pasivos); await db.SaveChangesAsync();
        var c = await new CrearCorridaHandler(db, new EmpresaContext(), new User(Armador), new Proveedores(), new Clock())
            .Handle(new(cuenta.Id, pasivos.Select(p => p.FacturaProveedorId).ToList()), default);
        c = await new EnviarCorridaAAutorizacionHandler(db, new Proveedores()).Handle(new(c.Id, c.Version), default);
        c = await new AutorizarCorridaHandler(db, new EmpresaContext(), new User(Jefe), new Roles(), new Proveedores(), new Clock()).Handle(new(c.Id, c.Version), default);
        c.Estado.Should().Be("Autorizada");
        return (db, c, pasivos);
    }

    private static RegistrarEjecucionCorridaHandler Ejecutar(TesoreriaDbContext db) =>
        new(db, new PagoSender(db), new Proveedores());

    private static PagoCorridaItem Pago(string referencia, params PasivoPendientePago[] p) => new(p.Select(x => x.FacturaProveedorId).ToList(), referencia, Fecha);

    [Fact]
    public async Task CA3_ejecutar_600_de_1000_deja_400_pendientes_y_repetir_la_referencia_no_suma()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var (db, c, p) = await Autorizada((a, 600m), (b, 400m));
        await using var _ = db;

        var r = await Ejecutar(db).Handle(new(c.Id, [Pago("REF-A", p[0])], c.Version), default);
        (r.Estado, r.Ejecutado, r.Pendiente).Should().Be(("Ejecutada", 600m, 400m));
        r.Lineas.Single(l => l.ReferenciaBancaria == "REF-A").Neto.Should().Be(600m);

        r = await Ejecutar(db).Handle(new(c.Id, [Pago("REF-A", p[0])], r.Version), default);   // mismo pago otra vez
        (r.Ejecutado, r.Pendiente).Should().Be((600m, 400m));
        db.MovimientosBancarios.Should().ContainSingle();
        db.AplicacionesPagoProveedor.Should().ContainSingle(x => x.CorridaId == c.Id && x.ImporteAplicado == 600m);
        db.PasivosPendientesPago.Single(x => x.FacturaProveedorId == p[0].FacturaProveedorId).SaldoPendiente.Should().Be(0m);
    }

    [Fact]
    public async Task CA1_cada_pago_cubre_un_solo_proveedor_aunque_vaya_en_la_misma_peticion()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c3 = Guid.NewGuid();
        var (db, c, p) = await Autorizada((a, 1_000m), (a, 2_000m), (b, 3_000m), (c3, 4_000m));
        await using var _ = db;

        var r = await Ejecutar(db).Handle(new(c.Id, [Pago("R1", p[0], p[1]), Pago("R2", p[2]), Pago("R3", p[3])], c.Version), default);

        db.MovimientosBancarios.Should().HaveCount(3);
        (await db.MovimientosBancarios.Select(m => m.BeneficiarioRef).Distinct().CountAsync()).Should().Be(3);
        r.Pendiente.Should().Be(0m);

        var mezcla = () => Ejecutar(db).Handle(new(c.Id, [Pago("R9", p[0], p[2])], r.Version), default);
        await mezcla.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Misma_referencia_en_otros_pasivos_se_rechaza()
    {
        var (db, c, p) = await Autorizada((Guid.NewGuid(), 600m), (Guid.NewGuid(), 400m));
        await using var _ = db;
        var r = await Ejecutar(db).Handle(new(c.Id, [Pago("REF-A", p[0])], c.Version), default);

        var act = () => Ejecutar(db).Handle(new(c.Id, [Pago("ref-a", p[1])], r.Version), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_REFERENCIA_REPETIDA");
    }

    [Fact]
    public async Task Pasivo_modificado_o_retirado_despues_de_autorizar_no_se_paga()
    {
        var (db, c, p) = await Autorizada((Guid.NewGuid(), 600m), (Guid.NewGuid(), 400m));
        await using var _ = db;
        var modificado = await db.PasivosPendientesPago.FirstAsync(x => x.FacturaProveedorId == p[0].FacturaProveedorId);
        modificado.ActualizarDesdeEvento(600m, 300m, "MXN", null, new DateOnly(2026, 11, 15), null, "F-1", Ahora);
        var retirado = await db.PasivosPendientesPago.FirstAsync(x => x.FacturaProveedorId == p[1].FacturaProveedorId);
        retirado.RetirarDePago("revisión", Ahora.AddMinutes(1));
        await db.SaveChangesAsync();

        var a = () => Ejecutar(db).Handle(new(c.Id, [Pago("X1", p[0])], c.Version), default);
        await a.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_PASIVO_MODIFICADO");
        var b = () => Ejecutar(db).Handle(new(c.Id, [Pago("X2", p[1])], c.Version), default);
        await b.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_PASIVO_RETIRADO");

        db.MovimientosBancarios.Should().BeEmpty();
    }

    [Fact]
    public async Task No_se_ejecuta_una_corrida_que_no_esta_autorizada()
    {
        await using var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var p = CorridaPagoDominioTests.Pasivo(Guid.NewGuid(), 100m, 100m, 0m, 0m, empresa: Empresa);
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.Add(p); await db.SaveChangesAsync();
        var c = await new CrearCorridaHandler(db, new EmpresaContext(), new User(Armador), new Proveedores(), new Clock()).Handle(new(cuenta.Id, [p.FacturaProveedorId]), default);

        var act = () => Ejecutar(db).Handle(new(c.Id, [Pago("R", p)], c.Version), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_ESTADO_INVALIDO");
    }

    [Fact]
    public async Task CA6_revertir_un_pago_de_la_corrida_deja_contramovimiento_con_motivo_y_el_pasivo_pendiente()
    {
        var (db, c, p) = await Autorizada((Guid.NewGuid(), 600m));
        await using var _ = db;
        var r = await Ejecutar(db).Handle(new(c.Id, [Pago("REF-A", p[0])], c.Version), default);
        var aplicacion = await db.AplicacionesPagoProveedor.SingleAsync();

        await new RevertirPagoProveedorHandler(db, new EmpresaContext(), new User(Jefe), new Periodo(), new Publicador(), new Clock())
            .Handle(new(aplicacion.Id, "Pago registrado por error"), default);

        (await db.MovimientosBancarios.SingleAsync(m => m.ContramovimientoDe != null)).MotivoReversa.Should().Be("Pago registrado por error");
        aplicacion.Revertida.Should().BeTrue();
        db.PasivosPendientesPago.Single(x => x.FacturaProveedorId == p[0].FacturaProveedorId).SaldoPendiente.Should().Be(600m);
        var despues = await new ObtenerCorridaHandler(db, new Proveedores()).Handle(new(c.Id), default);
        (despues.Estado, despues.Ejecutado, despues.Pendiente).Should().Be(("Autorizada", 0m, 600m));
        despues.Lineas.Single().ReferenciaBancaria.Should().BeNull();
    }

    [Fact]
    public async Task CA1_oficio_muestra_proveedores_con_bruto_retenciones_y_neto_solo_de_la_autorizada()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c3 = Guid.NewGuid();
        var (db, c, _) = await Autorizada((a, 1_000m), (b, 2_000m), (c3, 3_000m));
        await using var _ = db;

        var oficio = await new GenerarOficioCorridaHandler(db, new Proveedores(), new Clock()).Handle(new(c.Id), default);

        oficio.Filas.Should().HaveCount(3);
        oficio.Columnas.Select(x => x.Key).Should().Contain(["bruto", "retenciones", "neto"]);
        oficio.Totales!["neto"].Should().Be(6_000m);
        (await db.CorridasPago.SingleAsync()).OficioGeneradoEn.Should().Be(Ahora);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task El_oficio_no_se_genera_de_una_corrida_en_borrador_o_en_autorizacion(bool enviada)
    {
        await using var db = Db();
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var p = CorridaPagoDominioTests.Pasivo(Guid.NewGuid(), 100m, 100m, 0m, 0m, empresa: Empresa);
        db.CuentasBancarias.Add(cuenta); db.PasivosPendientesPago.Add(p); await db.SaveChangesAsync();
        var c = await new CrearCorridaHandler(db, new EmpresaContext(), new User(Armador), new Proveedores(), new Clock()).Handle(new(cuenta.Id, [p.FacturaProveedorId]), default);
        if (enviada) await new EnviarCorridaAAutorizacionHandler(db, new Proveedores()).Handle(new(c.Id, c.Version), default);

        var act = () => new GenerarOficioCorridaHandler(db, new Proveedores(), new Clock()).Handle(new(c.Id), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CORRIDA_OFICIO_NO_AUTORIZADA");
    }

    [Fact]
    public async Task Cerrar_libera_lo_pendiente_para_otra_corrida()
    {
        var (db, c, p) = await Autorizada((Guid.NewGuid(), 600m), (Guid.NewGuid(), 400m));
        await using var _ = db;
        var r = await Ejecutar(db).Handle(new(c.Id, [Pago("REF-A", p[0])], c.Version), default);

        r = await new CerrarCorridaHandler(db, new Proveedores()).Handle(new(c.Id, r.Version), default);

        r.Estado.Should().Be("Cerrada");
        var libres = await new PasivosParaCorridaHandler(db, new Proveedores()).Handle(new("MXN"), default);
        libres.Items.Select(x => x.FacturaProveedorId).Should().BeEquivalentTo([p[1].FacturaProveedorId]);
    }

    /// <summary>Despacha el pago real (RegistrarPagoProveedorHandler) como lo haría MediatR.</summary>
    private sealed class PagoSender(TesoreriaDbContext db) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var cmd = (RegistrarPagoProveedorCommand)(object)request;
            var h = new RegistrarPagoProveedorHandler(db, new EmpresaContext(), new User(Jefe), new Periodo(), new Proveedores(), new Elegible(), new Publicador(), new Clock());
            return (TResponse)(object)await h.Handle(cmd, cancellationToken);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Periodo : IPeriodoContablePort { public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true); }
    private sealed class Elegible : IElegibleFacturaReadPort { public Task<decimal> ObtenerLimiteAcumuladoAsync(Guid id, CancellationToken ct) => Task.FromResult(decimal.MaxValue); }
    private sealed class Publicador : IIntegrationEventPublisher { public Task PublishAsync(object e, CancellationToken ct) => Task.CompletedTask; }
    private sealed class Roles : IFirmanteRolesReadPort
    {
        public Task<IReadOnlyCollection<string>> CodigosRolAsync(Guid u, Guid e, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<string>>(["jefe-tesoreria"]);
    }
    private sealed class Proveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(Dto(id));
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(ids.ToDictionary(i => i, Dto));
        private static ProveedorBancoDto Dto(Guid id) => new(id, "P", $"Proveedor {id.ToString()[..4]}",
            [new CuentaProveedorDto(Guid.NewGuid(), "BBVA", "012180001234567897", true, null, "MXN", "Titular", true, "Activa")]);
    }
}
