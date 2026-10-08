using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.PublicAdapters;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.Contabilidad.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>Calendario FIX propio con limpieza por ID. Nunca abre ni cierra ejercicios ajenos.</summary>
internal sealed class PeriodoContableFixture(string connectionString, Guid empresaId, int anio) : IAsyncDisposable
{
    private readonly ContabilidadDbContext _db = new(
        new DbContextOptionsBuilder<ContabilidadDbContext>().UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options,
        new EmpresaFixture(empresaId));
    private readonly Guid _ejercicioId = Guid.NewGuid();

    public IPeriodoContableReadPort Port => new PeriodoContableReadAdapter(new PeriodoContableConsultaAdapter(_db));

    public async Task SembrarAsync(int mesAbierto, bool cerrarAnteriores = false)
    {
        var ejercicio = new EjercicioContable(_ejercicioId, anio) { EmpresaId = empresaId };
        var periodos = ejercicio.GenerarPeriodos();
        foreach (var p in periodos)
        {
            p.EmpresaId = empresaId;
            if (p.Numero == mesAbierto || (cerrarAnteriores && p.Numero < mesAbierto))
                p.Abrir(null, "FIX C1.2 apertura", null, "FIX C1.2", DateTimeOffset.UtcNow);
            if (cerrarAnteriores && p.Numero < mesAbierto)
                p.Cerrar(periodos.Where(x => x.Numero < p.Numero), "FIX C1.2 cierre", null, "FIX C1.2", DateTimeOffset.UtcNow);
        }
        _db.Ejercicios.Add(ejercicio);
        _db.Periodos.AddRange(periodos);
        await _db.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _db.Periodos.Where(p => p.EjercicioId == _ejercicioId).ExecuteDeleteAsync();
            await _db.Ejercicios.Where(e => e.Id == _ejercicioId).ExecuteDeleteAsync();
        }
        finally { await _db.DisposeAsync(); }
    }

    private sealed class EmpresaFixture(Guid empresaId) : ICurrentEmpresaContext
    {
        public Guid? Current => empresaId;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException("El fixture mantiene el aislamiento por empresa.");
    }
}
