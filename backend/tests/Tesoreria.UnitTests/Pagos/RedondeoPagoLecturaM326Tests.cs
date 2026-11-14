using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Application.Pasivos;
using Millet.Tesoreria.Domain.Pasivos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Pagos;

/// <summary>M-326 (b), lectura: lista de solicitudes de redondeo y su marca en la bandeja de pasivos.</summary>
public sealed class RedondeoPagoLecturaM326Tests
{
    private static readonly Guid EmpresaId = Guid.NewGuid(), Solicita = Guid.NewGuid(), Gerente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => EmpresaId; public bool IsBypassed => true; public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
    private sealed class Permisos : ICurrentUserPermissions
    { public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(false); }
    private sealed class SinProveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(null);
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(new Dictionary<Guid, ProveedorBancoDto>());
    }

    private sealed class ConProveedores : IProveedorBancoReadPort
    {
        public Task<ProveedorBancoDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ProveedorBancoDto?>(null);
        public Task<IReadOnlyDictionary<Guid, ProveedorBancoDto>> ObtenerVariosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorBancoDto>>(ids.ToDictionary(i => i, i => new ProveedorBancoDto(i, "PROV-DEMO", "Proveedor de prueba SA", [], true)));
    }

    private static TesoreriaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase($"m326f-{Guid.NewGuid():N}").Options, new Empresa());

    private static PasivoPendientePago Pasivo(string folio) =>
        new(EmpresaId, Guid.NewGuid(), Guid.NewGuid(), null, 1000m, 1000m, "MXN", null, new DateOnly(2026, 12, 1), null, folio, Ahora, "PUE");

    private static AutorizacionRedondeoPago Solicitud(Guid factura, decimal redondeo, string motivo = "Centavos del banco") =>
        new(EmpresaId, factura, redondeo, motivo, Solicita, Ahora);

    [Fact]
    public async Task Lista_las_solicitudes_con_los_campos_del_contrato_y_el_folio_de_la_factura()
    {
        await using var db = NewDb();
        var p = Pasivo("F-100"); db.PasivosPendientesPago.Add(p);
        var a = Solicitud(p.FacturaProveedorId, 0.40m); a.Autorizar(Gerente, Ahora.AddMinutes(5));
        db.AutorizacionesRedondeoPago.Add(a); await db.SaveChangesAsync();

        var r = await new ListarRedondeosPagoHandler(db, new Empresa(), new SinProveedores()).Handle(new ListarRedondeosPagoQuery(), default);

        var i = r.Items.Should().ContainSingle().Subject;
        i.Id.Should().Be(a.Id); i.FacturaProveedorId.Should().Be(p.FacturaProveedorId); i.FolioProveedor.Should().Be("F-100");
        i.Importe.Should().Be(0.40m); i.Motivo.Should().Be("Centavos del banco"); i.Estado.Should().Be("Autorizada");
        i.SolicitadoPor.Should().Be(Solicita); i.AutorizadoPor.Should().Be(Gerente);
        i.Fecha.Should().Be(Ahora); i.ResueltoEn.Should().Be(Ahora.AddMinutes(5)); i.Usable.Should().BeTrue();
    }

    [Fact]
    public async Task Cada_item_trae_folio_proveedor_y_los_alias_del_contrato_de_RedondeoPagoResponse()
    {
        await using var db = NewDb();
        var p = Pasivo("F-200"); db.PasivosPendientesPago.Add(p);
        var a = Solicitud(p.FacturaProveedorId, -0.30m); a.Autorizar(Gerente, Ahora);
        db.AutorizacionesRedondeoPago.Add(a); await db.SaveChangesAsync();

        var i = (await new ListarRedondeosPagoHandler(db, new Empresa(), new ConProveedores())
            .Handle(new ListarRedondeosPagoQuery(), default)).Items.Single();

        i.FolioProveedor.Should().Be("F-200"); i.ProveedorId.Should().Be(p.ProveedorId);
        i.ProveedorClave.Should().Be("PROV-DEMO"); i.ProveedorRazonSocial.Should().Be("Proveedor de prueba SA");
        i.Redondeo.Should().Be(i.Importe).And.Be(-0.30m);
        i.ResueltoPor.Should().Be(Gerente).And.Be(i.AutorizadoPor);
    }

    [Fact]
    public async Task Filtra_por_estado_y_por_factura_y_rechaza_un_estado_invalido()
    {
        await using var db = NewDb();
        var p1 = Pasivo("F-1"); var p2 = Pasivo("F-2"); db.PasivosPendientesPago.AddRange(p1, p2);
        var pendiente = Solicitud(p1.FacturaProveedorId, 0.10m);
        var autorizada = Solicitud(p2.FacturaProveedorId, -0.20m); autorizada.Autorizar(Gerente, Ahora);
        db.AutorizacionesRedondeoPago.AddRange(pendiente, autorizada); await db.SaveChangesAsync();
        var h = new ListarRedondeosPagoHandler(db, new Empresa(), new SinProveedores());

        (await h.Handle(new ListarRedondeosPagoQuery(Estado: "autorizada"), default)).Items.Single().Id.Should().Be(autorizada.Id);
        (await h.Handle(new ListarRedondeosPagoQuery(Estado: "Solicitada"), default)).Items.Single().AutorizadoPor.Should().BeNull();
        (await h.Handle(new ListarRedondeosPagoQuery(FacturaId: p1.FacturaProveedorId), default)).Items.Single().Id.Should().Be(pendiente.Id);
        var mal = () => h.Handle(new ListarRedondeosPagoQuery(Estado: "Otro"), default);
        (await mal.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("RPP_ESTADO_INVALIDO");
    }

    [Fact]
    public async Task El_alcance_por_sucursal_solo_deja_las_facturas_de_los_pasivos_permitidos()
    {
        await using var db = NewDb();
        var visible = Pasivo("F-V"); var oculto = Pasivo("F-O"); db.PasivosPendientesPago.AddRange(visible, oculto);
        db.AutorizacionesRedondeoPago.AddRange(Solicitud(visible.FacturaProveedorId, 0.10m), Solicitud(oculto.FacturaProveedorId, 0.20m));
        await db.SaveChangesAsync();
        var h = new ListarRedondeosPagoHandler(db, new Empresa(), new SinProveedores());

        (await h.Handle(new ListarRedondeosPagoQuery { DocumentosPermitidos = [visible.Id] }, default))
            .Items.Single().FolioProveedor.Should().Be("F-V");
        (await h.Handle(new ListarRedondeosPagoQuery { DocumentosPermitidos = [] }, default)).Items.Should().BeEmpty();
        (await h.Handle(new ListarRedondeosPagoQuery(), default)).Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task La_bandeja_marca_el_redondeo_autorizado_pendiente_de_usar_y_el_solicitado_no_lo_es()
    {
        await using var db = NewDb();
        var conAutorizado = Pasivo("F-A"); var conSolicitado = Pasivo("F-S"); var sin = Pasivo("F-N");
        db.PasivosPendientesPago.AddRange(conAutorizado, conSolicitado, sin);
        var a = Solicitud(conAutorizado.FacturaProveedorId, 0.40m); a.Autorizar(Gerente, Ahora);
        var s = Solicitud(conSolicitado.FacturaProveedorId, -0.10m);
        db.AutorizacionesRedondeoPago.AddRange(a, s); await db.SaveChangesAsync();

        var r = await new BandejaPasivosPendientesHandler(db, new SinProveedores(), new Permisos())
            .Handle(new BandejaPasivosPendientesQuery(), default);

        var autorizado = r.Items.Single(x => x.FolioProveedor == "F-A");
        autorizado.RedondeoAutorizadoPendiente.Should().BeTrue(); autorizado.RedondeoEstado.Should().Be("Autorizada");
        autorizado.RedondeoImporte.Should().Be(0.40m); autorizado.RedondeoSolicitudId.Should().Be(a.Id);
        var solicitado = r.Items.Single(x => x.FolioProveedor == "F-S");
        solicitado.RedondeoAutorizadoPendiente.Should().BeFalse(); solicitado.RedondeoEstado.Should().Be("Solicitada");
        solicitado.RedondeoImporte.Should().Be(-0.10m);
        var ninguno = r.Items.Single(x => x.FolioProveedor == "F-N");
        ninguno.RedondeoEstado.Should().BeNull(); ninguno.RedondeoImporte.Should().BeNull(); ninguno.RedondeoAutorizadoPendiente.Should().BeFalse();
    }

    [Fact]
    public async Task Una_autorizacion_ya_consumida_o_rechazada_no_aparece_como_pendiente_en_la_bandeja()
    {
        await using var db = NewDb();
        var p = Pasivo("F-C"); db.PasivosPendientesPago.Add(p);
        var a = Solicitud(p.FacturaProveedorId, 0.40m); a.Autorizar(Gerente, Ahora); a.Consumir(Guid.NewGuid());
        db.AutorizacionesRedondeoPago.Add(a); await db.SaveChangesAsync();

        var r = await new BandejaPasivosPendientesHandler(db, new SinProveedores(), new Permisos())
            .Handle(new BandejaPasivosPendientesQuery(), default);
        r.Items.Single().RedondeoAutorizadoPendiente.Should().BeFalse();
        r.Items.Single().RedondeoEstado.Should().BeNull();
        (await new ListarRedondeosPagoHandler(db, new Empresa(), new SinProveedores()).Handle(new ListarRedondeosPagoQuery(Estado: "Consumida"), default))
            .Items.Single().Usable.Should().BeFalse();
    }
}
