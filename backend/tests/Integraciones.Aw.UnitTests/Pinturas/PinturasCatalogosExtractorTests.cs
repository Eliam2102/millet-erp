using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;
using Millet.Integraciones.Aw.Infrastructure.Pinturas;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

public class PinturasCatalogosExtractorTests
{
    private static CompartidoDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<CompartidoDbContext>()
            .UseInMemoryDatabase($"pinturas-extractor-{Guid.NewGuid():N}")
            .Options;
        return new CompartidoDbContext(options, new BypassedEmpresaContext());
    }

    [Fact]
    public void Constructor_DbNulo_LanzaArgumentNullException()
    {
        var act = () => new PinturasCatalogosExtractor(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("db");
    }

    [Fact]
    public async Task ExtraerAsync_RespetaCancellationToken()
    {
        using var db = CreateInMemoryDb();
        var extractor = new PinturasCatalogosExtractor(db);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => extractor.ExtraerAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExtraerAsync_Clientes_FiltraYExtraeCorrectamente()
    {
        using var db = CreateInMemoryDb();

        // 1. Cliente con ReferenciaExterna numérica (debe tomar ReferenciaExterna = 2001)
        var cliente1 = new Cliente(
            id: Guid.CreateVersion7(),
            clave: "CLI-01",
            razonSocial: "Vidrios y Aluminios del Norte S.A.",
            referenciaExterna: "2001",
            estatus: EstatusCatalogo.Activo,
            telefono: "9991234567",
            email: "contacto@norte.com");

        // 2. Cliente sin ReferenciaExterna pero con Clave numérica (debe tomar Clave = 3001)
        var cliente2 = new Cliente(
            id: Guid.CreateVersion7(),
            clave: "3001",
            razonSocial: "Cristales del Sureste S.A. de C.V.",
            referenciaExterna: null,
            estatus: EstatusCatalogo.Inactivo,
            telefono: "9997654321",
            email: "info@sureste.com");

        // 3. Cliente con clave y referencia alfanuméricas (debe ser ignorado)
        var clienteIgnorado1 = new Cliente(
            id: Guid.CreateVersion7(),
            clave: "ALFA-99",
            razonSocial: "Cliente No Numérico",
            referenciaExterna: "AW-REF-X",
            estatus: EstatusCatalogo.Activo);

        // 4. Cliente sin referencia y con clave alfanumérica (debe ser ignorado)
        var clienteIgnorado2 = new Cliente(
            id: Guid.CreateVersion7(),
            clave: "X100",
            razonSocial: "Cliente Alfanumerico",
            referenciaExterna: null,
            estatus: EstatusCatalogo.Activo);

        db.Clientes.AddRange(cliente1, cliente2, clienteIgnorado1, clienteIgnorado2);
        await db.SaveChangesAsync();

        var extractor = new PinturasCatalogosExtractor(db);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Clientes.Should().HaveCount(2);

        var c1 = batch.Clientes.Single(c => c.Clave == 2001);
        c1.RazonSocial.Should().Be("Vidrios y Aluminios del Norte S.A.");
        c1.Telefono.Should().Be("9991234567");
        c1.Email.Should().Be("contacto@norte.com");
        c1.NombreComercial.Should().BeNull();
        c1.Contacto.Should().BeNull();
        c1.Celular.Should().BeNull();
        c1.Activo.Should().BeTrue();

        var c2 = batch.Clientes.Single(c => c.Clave == 3001);
        c2.RazonSocial.Should().Be("Cristales del Sureste S.A. de C.V.");
        c2.Telefono.Should().Be("9997654321");
        c2.Email.Should().Be("info@sureste.com");
        c2.Activo.Should().BeFalse();
    }

    [Fact]
    public async Task ExtraerAsync_Clientes_DeduplicaPorClaveNumerica_PrefiereActivo()
    {
        using var db = CreateInMemoryDb();

        // Dos clientes que resuelven a la misma clave 5001: uno inactivo y uno activo
        var inactivo = new Cliente(
            id: Guid.CreateVersion7(),
            clave: "OLD-5001",
            razonSocial: "Cliente Inactivo Antiguo",
            referenciaExterna: "5001",
            estatus: EstatusCatalogo.Inactivo);

        var activo = new Cliente(
            id: Guid.CreateVersion7(),
            clave: "5001",
            razonSocial: "Cliente Activo Actual",
            referenciaExterna: "5001",
            estatus: EstatusCatalogo.Activo);

        db.Clientes.AddRange(inactivo, activo);
        await db.SaveChangesAsync();

        var extractor = new PinturasCatalogosExtractor(db);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Clientes.Should().ContainSingle();
        var c = batch.Clientes[0];
        c.Clave.Should().Be(5001);
        c.Activo.Should().BeTrue();
        c.RazonSocial.Should().Be("Cliente Activo Actual");
    }

    [Fact]
    public async Task ExtraerAsync_Servicios_FiltraSoloRecubrimientoYNoVacios()
    {
        using var db = CreateInMemoryDb();

        // 1. Servicio de recubrimiento activo
        var servicioActivo = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: "SER95224",
            descripcion: "Pintura Horneada Blanca 7035",
            unidadMedida: "M2",
            esServicioRecubrimiento: true,
            estatus: EstatusCatalogo.Activo);

        // 2. Servicio de recubrimiento inactivo
        var servicioInactivo = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: "SER95225",
            descripcion: "Pintura Horneada Negra 9005",
            unidadMedida: "M2",
            esServicioRecubrimiento: true,
            estatus: EstatusCatalogo.Inactivo);

        // 3. Producto común de manufactura (NO es servicio de recubrimiento) -> debe ignorarse
        var vidrio = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: "VID6MM-CLR",
            descripcion: "Vidrio Claro 6mm",
            unidadMedida: "M2",
            esServicioRecubrimiento: false,
            estatus: EstatusCatalogo.Activo);

        db.ProductosAw.AddRange(servicioActivo, servicioInactivo, vidrio);
        await db.SaveChangesAsync();

        var extractor = new PinturasCatalogosExtractor(db);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Servicios.Should().HaveCount(2);

        var s1 = batch.Servicios.Single(s => s.Clave == "SER95224");
        s1.Nombre.Should().Be("Pintura Horneada Blanca 7035");
        s1.Activo.Should().BeTrue();

        var s2 = batch.Servicios.Single(s => s.Clave == "SER95225");
        s2.Nombre.Should().Be("Pintura Horneada Negra 9005");
        s2.Activo.Should().BeFalse();
    }

    [Fact]
    public async Task ExtraerAsync_Servicios_DeduplicaPorReferencia_PrefiereActivo()
    {
        using var db = CreateInMemoryDb();

        var inactivo = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: "SER888",
            descripcion: "Recubrimiento Viejo",
            unidadMedida: "M2",
            esServicioRecubrimiento: true,
            estatus: EstatusCatalogo.Inactivo);

        var activo = new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: "ser888", // diferente casing
            descripcion: "Recubrimiento Nuevo",
            unidadMedida: "M2",
            esServicioRecubrimiento: true,
            estatus: EstatusCatalogo.Activo);

        db.ProductosAw.AddRange(inactivo, activo);
        await db.SaveChangesAsync();

        var extractor = new PinturasCatalogosExtractor(db);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Servicios.Should().ContainSingle();
        var s = batch.Servicios[0];
        s.Clave.Should().Be("ser888");
        s.Activo.Should().BeTrue();
    }

    [Fact]
    public async Task ExtraerAsync_Empleados_FiltraYExtraeCorrectamente()
    {
        using var db = CreateInMemoryDb();
        var empresaId = Guid.NewGuid();

        var empActivo = new Empleado(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            clave: "EMP001",
            nombre: "Juan Perez",
            estatus: EstatusCatalogo.Activo);

        var empInactivo = new Empleado(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            clave: "EMP002",
            nombre: "Maria Lopez",
            estatus: EstatusCatalogo.Inactivo);

        db.Empleados.AddRange(empActivo, empInactivo);
        await db.SaveChangesAsync();

        var extractor = new PinturasCatalogosExtractor(db);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Empleados.Should().HaveCount(2);

        var e1 = batch.Empleados.Single(e => e.Clave == "EMP001");
        e1.Nombre.Should().Be("Juan Perez");
        e1.Activo.Should().BeTrue();

        var e2 = batch.Empleados.Single(e => e.Clave == "EMP002");
        e2.Nombre.Should().Be("Maria Lopez");
        e2.Activo.Should().BeFalse();
    }

    [Fact]
    public async Task ExtraerAsync_Empleados_DeduplicaPorClave_PrefiereActivo()
    {
        using var db = CreateInMemoryDb();
        var empresaId1 = Guid.NewGuid();
        var empresaId2 = Guid.NewGuid();

        var empInactivo = new Empleado(
            id: Guid.CreateVersion7(),
            empresaId: empresaId1,
            clave: "EMP77",
            nombre: "Colaborador de Baja",
            estatus: EstatusCatalogo.Inactivo);

        var empActivo = new Empleado(
            id: Guid.CreateVersion7(),
            empresaId: empresaId2,
            clave: "EMP77",
            nombre: "Colaborador Recontratado",
            estatus: EstatusCatalogo.Activo);

        db.Empleados.AddRange(empInactivo, empActivo);
        await db.SaveChangesAsync();

        var extractor = new PinturasCatalogosExtractor(db);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Empleados.Should().ContainSingle();
        var e = batch.Empleados[0];
        e.Clave.Should().Be("EMP77");
        e.Activo.Should().BeTrue();
        e.Nombre.Should().Be("Colaborador Recontratado");
    }

    [Fact]
    public async Task ExtraerAsync_Obras_ConObrasSource_DevuelveObras()
    {
        using var db = CreateInMemoryDb();
        var sourceMock = new StubPinturasObrasSource([
            new FilaPinturasObra("OBRA-100", "Torre Uno", FilaPinturasObra.OrigenPinturas, null, true),
            new FilaPinturasObra("OBRA-200", "Torre Dos", FilaPinturasObra.OrigenAw, "AW-OBJ-200", false)
        ]);

        var extractor = new PinturasCatalogosExtractor(db, sourceMock, NullLogger<PinturasCatalogosExtractor>.Instance);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Obras.Should().HaveCount(2);

        var o1 = batch.Obras.Single(o => o.Clave == "OBRA-100");
        o1.Nombre.Should().Be("Torre Uno");
        o1.Origen.Should().Be(FilaPinturasObra.OrigenPinturas);
        o1.ReferenciaAw.Should().BeNull();
        o1.Activo.Should().BeTrue();

        var o2 = batch.Obras.Single(o => o.Clave == "OBRA-200");
        o2.Nombre.Should().Be("Torre Dos");
        o2.Origen.Should().Be(FilaPinturasObra.OrigenAw);
        o2.ReferenciaAw.Should().Be("AW-OBJ-200");
        o2.Activo.Should().BeFalse();
    }

    [Fact]
    public async Task ExtraerAsync_Obras_SinObrasSource_DevuelveVacio()
    {
        using var db = CreateInMemoryDb();
        var extractor = new PinturasCatalogosExtractor(db, obrasSource: null, logger: null);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.Obras.Should().BeEmpty();
    }

    [Fact]
    public async Task NoOpPinturasObrasSource_DevuelveListaVacia()
    {
        var source = new NoOpPinturasObrasSource();
        var obras = await source.ObtenerObrasAsync(CancellationToken.None);

        obras.Should().BeEmpty();
        NoOpPinturasObrasSource.Instance.Should().NotBeNull();
    }

    [Fact]
    public async Task ExtraerAsync_BatchCompleto_IntegraTodosLosCatalogos()
    {
        using var db = CreateInMemoryDb();

        db.Clientes.Add(new Cliente(
            id: Guid.CreateVersion7(),
            clave: "100",
            razonSocial: "Cliente Completo",
            referenciaExterna: "100"));

        db.ProductosAw.Add(new ProductoAw(
            id: Guid.CreateVersion7(),
            referenciaExterna: "SER001",
            descripcion: "Servicio Completo",
            unidadMedida: "M2",
            esServicioRecubrimiento: true));

        db.Empleados.Add(new Empleado(
            id: Guid.CreateVersion7(),
            empresaId: Guid.NewGuid(),
            clave: "EMP01",
            nombre: "Empleado Completo"));

        await db.SaveChangesAsync();

        var source = new StubPinturasObrasSource([
            new FilaPinturasObra("OBR01", "Obra Completa", FilaPinturasObra.OrigenPinturas)
        ]);

        var extractor = new PinturasCatalogosExtractor(db, source, NullLogger<PinturasCatalogosExtractor>.Instance);
        var batch = await extractor.ExtraerAsync(CancellationToken.None);

        batch.EstaVacio.Should().BeFalse();
        batch.TotalElementos.Should().Be(4);
        batch.Clientes.Should().HaveCount(1);
        batch.Servicios.Should().HaveCount(1);
        batch.Obras.Should().HaveCount(1);
        batch.Empleados.Should().HaveCount(1);
    }

    private sealed class StubPinturasObrasSource(IReadOnlyList<FilaPinturasObra> obras) : IPinturasObrasSource
    {
        public Task<IReadOnlyList<FilaPinturasObra>> ObtenerObrasAsync(CancellationToken cancellationToken) =>
            Task.FromResult(obras);
    }

    private sealed class BypassedEmpresaContext : ICurrentEmpresaContext
    {
        public Guid? Current => null;
        public bool IsBypassed => true;
        public IDisposable Bypass() => new NoOpScope();
        private sealed class NoOpScope : IDisposable { public void Dispose() { } }
    }
}
