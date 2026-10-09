using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Parametros;
using Millet.Compartido.Infrastructure.Calendario;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Administracion;

public sealed class CalendarioHabilTests
{
    [Fact]
    public async Task Calendario_usa_festivos_editados_mediante_el_handler_de_parametros()
    {
        await using var db = CrearDb();
        await db.Database.EnsureCreatedAsync();
        var calendario = new CalendarioHabilService(db);
        var fecha = new DateOnly(2026, 11, 13); // Viernes; el lunes 16 es festivo.
        var original = await calendario.SumarHorasAsync(fecha, 48, default);

        await new ActualizarParametroHandler(db).Handle(new(CalendarioHabil.ClaveFestivos,
            "[\"2026-11-16\",\"2026-11-17\"]"), default);

        var actualizado = await calendario.SumarHorasAsync(fecha, 48, default);
        actualizado.Should().Be(original.AddDays(1));
    }

    [Fact]
    public async Task Festivos_invalidos_no_alteran_el_parametro()
    {
        await using var db = CrearDb();
        await db.Database.EnsureCreatedAsync();
        var parametro = await db.ParametrosGlobales.SingleAsync(p => p.Clave == CalendarioHabil.ClaveFestivos);
        var original = parametro.Valor;
        var action = () => new ActualizarParametroHandler(db).Handle(new(CalendarioHabil.ClaveFestivos,
            "[\"2026-02-30\"]"), default);
        (await action.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CALENDARIO_FESTIVOS_INVALIDOS");
        parametro.Valor.Should().Be(original);
        db.ChangeTracker.HasChanges().Should().BeFalse();
    }

    private static CompartidoDbContext CrearDb() => new(
        new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new EmpresaContext());

    private sealed class EmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
