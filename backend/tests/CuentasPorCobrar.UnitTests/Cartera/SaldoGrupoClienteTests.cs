using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Cartera;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.CuentasPorCobrar.UnitTests.Cartera;

/// <summary>86e3x4g77: saldo por miembro y moneda del grupo de unificación; nunca se suma al principal.</summary>
public sealed class SaldoGrupoClienteTests
{
    private static readonly Guid Principal = Guid.NewGuid();
    private static readonly Guid Secundario = Guid.NewGuid();

    private static CuentasPorCobrarDbContext Db() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_grupo_{Guid.NewGuid()}").Options, new Empresa());

    private static void Factura(CuentasPorCobrarDbContext db, Guid cliente, decimal total, string moneda, decimal pagado = 0)
    {
        var f = FacturaCartera.Crear(Guid.NewGuid(), Guid.NewGuid(), cliente, "VGL860910IU4", "Cliente", Guid.NewGuid().ToString(), "FV-1",
            total, moneda, "PPD", fechaTimbrado: DateTimeOffset.UtcNow.AddDays(-10), fechaVencimiento: DateTimeOffset.UtcNow.AddDays(20));
        if (pagado > 0) f.AplicarPago(pagado);
        db.FacturasCartera.Add(f);
    }

    [Fact]
    public async Task Cada_miembro_conserva_su_saldo_por_moneda_sin_sumarse_al_principal()
    {
        using var db = Db();
        Factura(db, Principal, 1_000m, "MXN");
        Factura(db, Secundario, 500m, "USD", pagado: 100m);
        Factura(db, Secundario, 200m, "MXN");
        await db.SaveChangesAsync();
        var grupo = new Grupo([new(Principal, "1", "PRINCIPAL", true), new(Secundario, "2", "SECUNDARIO", false)]);

        var r = await new SaldoGrupoClienteHandler(db, grupo).Handle(new SaldoGrupoClienteQuery(Secundario), CancellationToken.None);

        r.Miembros.Select(m => m.ClienteId).Should().Equal(Principal, Secundario);
        r.Miembros[0].Monedas.Should().ContainSingle().Which.Should().Match<SaldoNetoMonedaResponse>(s => s.Moneda == "MXN" && s.SaldoNeto == 1_000m);
        r.Miembros[1].Monedas.Select(s => (s.Moneda, s.SaldoNeto)).Should().Equal(("MXN", 200m), ("USD", 400m));
    }

    [Fact]
    public async Task Sin_grupo_devuelve_miembros_vacio()
    {
        using var db = Db();
        Factura(db, Principal, 1_000m, "MXN");
        await db.SaveChangesAsync();

        var r = await new SaldoGrupoClienteHandler(db, new Grupo([])).Handle(new SaldoGrupoClienteQuery(Principal), CancellationToken.None);

        r.Miembros.Should().BeEmpty();
    }

    [Fact]
    public async Task Respeta_el_alcance_de_documentos_permitidos()
    {
        using var db = Db();
        Factura(db, Principal, 1_000m, "MXN");
        await db.SaveChangesAsync();
        var grupo = new Grupo([new(Principal, "1", "PRINCIPAL", true), new(Secundario, "2", "SECUNDARIO", false)]);

        var r = await new SaldoGrupoClienteHandler(db, grupo).Handle(
            new SaldoGrupoClienteQuery(Principal) { DocumentosPermitidos = [Guid.NewGuid()] }, CancellationToken.None);

        r.Miembros.Should().OnlyContain(m => m.Monedas.Count == 0);
    }

    private sealed class Grupo(IReadOnlyList<ClienteGrupoMiembroDto> miembros) : IClienteReadPort
    {
        public Task<ClienteRefDto?> ObtenerPorRfcAsync(string rfc, CancellationToken ct) => Task.FromResult<ClienteRefDto?>(null);
        public Task<ClienteRefDto?> ObtenerAsync(Guid clienteId, CancellationToken ct) => Task.FromResult<ClienteRefDto?>(null);
        public Task<IReadOnlyList<ClienteLookupCxcDto>> BuscarAsync(string? rfc, string? razonSocial, IReadOnlyCollection<Guid>? ids, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteLookupCxcDto>>([]);
        public Task<IReadOnlyList<ClienteGrupoMiembroDto>> ObtenerGrupoAsync(Guid clienteId, CancellationToken ct) => Task.FromResult(miembros);
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
