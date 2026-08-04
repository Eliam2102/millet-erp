using Millet.Integraciones.Fiscal.Application.RfcsReceptores.ActualizarRfcReceptor;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.AgregarRfcReceptor;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.EliminarRfcReceptor;
using Millet.Integraciones.Fiscal.Application.RfcsReceptores.ListarRfcsReceptores;
using Millet.Integraciones.Fiscal.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Fiscal.UnitTests.Application;

public sealed class RfcReceptorHandlerTests
{
    private static readonly Guid EmpresaId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Agregar_persiste_normalizado_a_uppercase()
    {
        var db = InMemoryFiscalDb.Create();
        var handler = new AgregarRfcReceptorHandler(
            db, new InMemoryFiscalDb.FakeEmpresaContext(EmpresaId),
            new InMemoryFiscalDb.FakeClock(DateTimeOffset.UtcNow));

        var response = await handler.Handle(
            new AgregarRfcReceptorCommand(EmpresaId, "  mil010101aaa  "),
            CancellationToken.None);

        response.Rfc.Should().Be("MIL010101AAA");
        db.RfcsReceptores.Single().Rfc.Should().Be("MIL010101AAA");
    }

    [Fact]
    public async Task Agregar_duplicado_lanza_Conflict()
    {
        var db = InMemoryFiscalDb.Create();
        var handler = new AgregarRfcReceptorHandler(
            db, new InMemoryFiscalDb.FakeEmpresaContext(EmpresaId),
            new InMemoryFiscalDb.FakeClock(DateTimeOffset.UtcNow));

        await handler.Handle(new AgregarRfcReceptorCommand(EmpresaId, "MIL010101AAA"), CancellationToken.None);

        var act = () => handler.Handle(
            new AgregarRfcReceptorCommand(EmpresaId, "mil010101aaa"),
            CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ConflictException>()).Subject.First();
        ex.Code.Should().Be("RFC_RECEPTOR_DUPLICADO");
    }

    [Fact]
    public async Task Agregar_con_otra_empresa_lanza_CrossTenant()
    {
        var db = InMemoryFiscalDb.Create();
        var otraEmpresa = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new AgregarRfcReceptorHandler(
            db, new InMemoryFiscalDb.FakeEmpresaContext(otraEmpresa),
            new InMemoryFiscalDb.FakeClock(DateTimeOffset.UtcNow));

        var act = () => handler.Handle(
            new AgregarRfcReceptorCommand(EmpresaId, "MIL010101AAA"),
            CancellationToken.None);

        await act.Should().ThrowAsync<CrossTenantViolationException>();
    }

    [Fact]
    public async Task Listar_devuelve_solo_filas_de_la_empresa()
    {
        var db = InMemoryFiscalDb.Create();
        db.RfcsReceptores.Add(new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA"));
        db.RfcsReceptores.Add(new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL020202BBB"));
        var otra = Guid.NewGuid();
        db.RfcsReceptores.Add(new RfcReceptor(Guid.NewGuid(), otra, "OTR030303CCC"));
        await db.SaveChangesAsync();

        var handler = new ListarRfcsReceptoresHandler(db,
            new InMemoryFiscalDb.FakeClock(DateTimeOffset.UtcNow));
        var result = await handler.Handle(new ListarRfcsReceptoresQuery(EmpresaId), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(r => r.Rfc).Should().Contain(["MIL010101AAA", "MIL020202BBB"]);
    }

    [Fact]
    public async Task Actualizar_toggle_flags()
    {
        var db = InMemoryFiscalDb.Create();
        var entity = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        db.RfcsReceptores.Add(entity);
        await db.SaveChangesAsync();

        var handler = new ActualizarRfcReceptorHandler(db,
            new InMemoryFiscalDb.FakeClock(DateTimeOffset.UtcNow));
        var result = await handler.Handle(
            new ActualizarRfcReceptorCommand(entity.Id, DescargaHabilitada: false, RefreshHabilitada: true),
            CancellationToken.None);

        result.DescargaHabilitada.Should().BeFalse();
        result.RefreshHabilitada.Should().BeTrue();
    }

    [Fact]
    public async Task Actualizar_no_encontrado_lanza_NotFound()
    {
        var db = InMemoryFiscalDb.Create();
        var handler = new ActualizarRfcReceptorHandler(db,
            new InMemoryFiscalDb.FakeClock(DateTimeOffset.UtcNow));

        var act = () => handler.Handle(
            new ActualizarRfcReceptorCommand(Guid.NewGuid(), true, true),
            CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<EntityNotFoundException>()).Subject.First();
        ex.Code.Should().Be("RFC_RECEPTOR_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Eliminar_remueve_la_fila()
    {
        var db = InMemoryFiscalDb.Create();
        var entity = new RfcReceptor(Guid.NewGuid(), EmpresaId, "MIL010101AAA");
        db.RfcsReceptores.Add(entity);
        await db.SaveChangesAsync();

        var handler = new EliminarRfcReceptorHandler(db);
        await handler.Handle(new EliminarRfcReceptorCommand(entity.Id), CancellationToken.None);

        // InMemory provider no implementa soft-delete via interceptor — verifica
        // que el handler invocó Remove (la fila desaparece de la query).
        db.RfcsReceptores.Find(entity.Id).Should().BeNull();
    }

    [Fact]
    public async Task Eliminar_no_encontrado_lanza_NotFound()
    {
        var db = InMemoryFiscalDb.Create();
        var handler = new EliminarRfcReceptorHandler(db);

        var act = () => handler.Handle(new EliminarRfcReceptorCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<EntityNotFoundException>();
    }
}
