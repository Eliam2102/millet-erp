using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Compras.Infrastructure;
using Millet.Compras.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Npgsql;

namespace Millet.Compras.UnitTests.P7;

/// <summary>Prueba el ciclo de vida real de EF/Npgsql sin conectar a PostgreSQL.
/// La atomicidad de los datos se verifica en P7RestoComprasAlmacenTests.</summary>
public sealed class TransaccionApartadosRqTests
{
    private const string ConexionDemo = "Host=localhost;Database=DEMO-P7;Username=DEMO";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Union_restaura_conexion_utilizable_y_no_libera_la_de_compras(bool externa)
    {
        await using var prestada = new NpgsqlConnection(ConexionDemo);
        var contexto = new Empresa();
        var interceptor = new SinRed();
        var opciones = new DbContextOptionsBuilder<AlmacenDbContext>().AddInterceptors(interceptor);
        if (externa) opciones.UseNpgsql(prestada);
        else opciones.UseNpgsql(ConexionDemo);
        await using var almacen = new AlmacenDbContext(opciones.Options, contexto);
        await using var compras = new ComprasDbContext(new DbContextOptionsBuilder<ComprasDbContext>()
            .UseNpgsql(ConexionDemo).AddInterceptors(interceptor).Options, contexto);
        var anterior = almacen.Database.GetDbConnection();
        var principal = compras.Database.GetDbConnection();
        await using var tx = new TransaccionDemo(principal);
        await compras.Database.UseTransactionAsync(tx);
        var servicio = new TransaccionApartadosRq(compras, almacen,
            new ApartadosRequisicionService(almacen, new AlmacenSaldoQueryAdapter(almacen)));
        for (var i = 0; i < 2; i++)
        {
            await using (await servicio.UnirAsync(default))
            {
                almacen.Database.GetDbConnection().Should().BeSameAs(principal);
                almacen.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(tx);
            }
            almacen.Database.CurrentTransaction.Should().BeNull();
            var restaurada = almacen.Database.GetDbConnection();
            // El setter lanza ObjectDisposedException si se reinstaló la
            // conexión liberada. No abre socket ni ejecuta SQL.
            restaurada.ConnectionString = ConexionDemo;
            principal.ConnectionString = ConexionDemo;
            if (externa) restaurada.Should().BeSameAs(anterior);
            else restaurada.Should().NotBeSameAs(anterior);
            compras.Database.CurrentTransaction!.GetDbTransaction().Should().BeSameAs(tx);
        }
    }

    private sealed class SinRed : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class TransaccionDemo(DbConnection conexion) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => conexion;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new Scope();
        private sealed class Scope : IDisposable { public void Dispose() { } }
    }
}
