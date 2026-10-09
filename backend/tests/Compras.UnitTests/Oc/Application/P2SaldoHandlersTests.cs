using Microsoft.EntityFrameworkCore;
using Millet.Compras.Application.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Oc.Application;

public sealed class P2SaldoHandlersTests
{
    [Fact]
    public async Task SaldoPersistido_CanceladaConCuatroRecibidasDejaSeis_CompromisoVivoConsumeSeis()
    {
        await using var f = await Fixture.Crear();
        f.CancelarConCuatroRecibidas();
        await f.Db.SaveChangesAsync();
        Assert.Equal(6m, (await SaldoCompraRq.ObtenerAsync(f.Db, [f.LineaRq.Id], default))[f.LineaRq.Id]);
        var nueva = f.NuevaOc("OC-MID2026-000902");
        var linea = nueva.AgregarLineaDesdeRequisicion(Guid.NewGuid(), f.LineaRq.ArticuloId, 6, "PZA", 100,
            f.Rq.DepartamentoId, f.Rq.Id, f.LineaRq.Id, centroCostoId: f.Ceco);
        f.Db.OrdenesCompra.Add(nueva);
        await f.Db.SaveChangesAsync();
        Assert.Equal(0m, (await SaldoCompraRq.ObtenerAsync(f.Db, [f.LineaRq.Id], default))[f.LineaRq.Id]);
        Assert.Equal(6m, (await SaldoCompraRq.ObtenerAsync(f.Db, [f.LineaRq.Id], default, linea.Id))[f.LineaRq.Id]);
    }

    [Fact]
    public void CantidadMayorAlSaldo_SeRechaza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => SaldoCompraRq.Validar(7, 6));
        Assert.Equal("OC_EXCEDE_SALDO_RQ", ex.Code);
        SaldoCompraRq.Validar(6, 6);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ComprasDbContext Db { get; init; }
        public required Requisicion Rq { get; init; }
        public required OrdenCompra Oc { get; init; }
        public LineaRequisicion LineaRq => Rq.Lineas.Single();
        public Guid Ceco { get; } = Guid.NewGuid();
        public static async Task<Fixture> Crear()
        {
            var empresa = Guid.NewGuid();
            var db = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
                .UseInMemoryDatabase($"P2-{Guid.NewGuid()}").Options, new Contexto(empresa));
            var rq = new Requisicion(Guid.NewGuid(), empresa, Millet.Compras.Domain.Folio.Parse("MID2026-000901"), 2026,
                Clasificacion.MateriaPrima, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Prioridad.Normal, DateTimeOffset.UtcNow, null);
            var oc = new OrdenCompra(Guid.NewGuid(), empresa, Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000901"), 2026,
                Guid.NewGuid(), rq.SucursalId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9));
            var f = new Fixture { Db = db, Rq = rq, Oc = oc };
            var l = rq.AgregarLinea(Guid.NewGuid(), Guid.NewGuid(), 10, "PZA", Money.Mxn(100), centroCostoId: f.Ceco);
            rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            rq.RegistrarAutorizacion(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1);
            rq.RegistrarCubrimiento([new CubrimientoLinea(l.Id, 0, 10)], DateTimeOffset.UtcNow);
            oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), l.ArticuloId, 10, "PZA", 100, rq.DepartamentoId, rq.Id, l.Id, centroCostoId: f.Ceco);
            rq.ComprometerEnOc(oc.Id);
            db.AddRange(rq, oc);
            await db.SaveChangesAsync();
            return f;
        }
        public OrdenCompra NuevaOc(string folio) => new(Guid.NewGuid(), Oc.EmpresaId, Millet.Compras.Domain.Oc.Folio.Parse(folio), 2026,
            Oc.ProveedorId, Rq.SucursalId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 9));
        public void CancelarConCuatroRecibidas()
        {
            Oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            Oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
            Oc.Autorizar(Guid.NewGuid(), NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
            Oc.RegistrarRecepcionLinea(Oc.Lineas.Single().Id, 4m, DateTimeOffset.UtcNow);
            Oc.SolicitarCancelacionConRecepciones(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), "Cancelar faltante");
            Oc.ConfirmarCancelacionConRecepciones(Guid.NewGuid(), DateTimeOffset.UtcNow, "Confirmar faltante");
            Rq.LiberarDeOc();
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
    private sealed class Contexto(Guid empresa) : ICurrentEmpresaContext, ICurrentUserContext, IClock
    {
        public Guid? Current => empresa;
        public bool IsBypassed => false;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? UserName => "P2";
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
