using Millet.Integraciones.Aw.Application.Pinturas.Catalogos;

namespace Millet.Integraciones.Aw.UnitTests.Pinturas;

public sealed class PinturasCatalogosModelosTests
{
    [Fact]
    public void FilaPinturasCliente_ConValoresPorDefecto_InicializaCorrectamente()
    {
        var cliente = new FilaPinturasCliente(1001, "Vidrios Millet S.A. de C.V.");

        cliente.Clave.Should().Be(1001);
        cliente.RazonSocial.Should().Be("Vidrios Millet S.A. de C.V.");
        cliente.NombreComercial.Should().BeNull();
        cliente.Contacto.Should().BeNull();
        cliente.Telefono.Should().BeNull();
        cliente.Celular.Should().BeNull();
        cliente.Email.Should().BeNull();
        cliente.Activo.Should().BeTrue();
    }

    [Fact]
    public void FilaPinturasCliente_ConTodosLosCampos_AsignaPropiedades()
    {
        var cliente = new FilaPinturasCliente(
            Clave: 2002,
            RazonSocial: "Aluminios y Fachadas SA",
            NombreComercial: "AluFachadas",
            Contacto: "Juan Perez",
            Telefono: "9991234567",
            Celular: "9999876543",
            Email: "juan@alufachadas.com",
            Activo: false);

        cliente.Clave.Should().Be(2002);
        cliente.RazonSocial.Should().Be("Aluminios y Fachadas SA");
        cliente.NombreComercial.Should().Be("AluFachadas");
        cliente.Contacto.Should().Be("Juan Perez");
        cliente.Telefono.Should().Be("9991234567");
        cliente.Celular.Should().Be("9999876543");
        cliente.Email.Should().Be("juan@alufachadas.com");
        cliente.Activo.Should().BeFalse();
    }

    [Fact]
    public void FilaPinturasServicio_ConValoresPorDefecto_InicializaActivoTrue()
    {
        var servicio = new FilaPinturasServicio("SER95224", "Pintura electrostática blanco mate");

        servicio.Clave.Should().Be("SER95224");
        servicio.Nombre.Should().Be("Pintura electrostática blanco mate");
        servicio.Activo.Should().BeTrue();
    }

    [Fact]
    public void FilaPinturasServicio_ConActivoFalso_AsignaCorrectamente()
    {
        var servicio = new FilaPinturasServicio("SER95225", "Pintura gris grafito", Activo: false);

        servicio.Clave.Should().Be("SER95225");
        servicio.Nombre.Should().Be("Pintura gris grafito");
        servicio.Activo.Should().BeFalse();
    }

    [Fact]
    public void FilaPinturasObra_ConValoresPorDefecto_AsignaOrigenPinturasYActivoTrue()
    {
        var obra = new FilaPinturasObra("OB-101", "Torre Sky Capital");

        obra.Clave.Should().Be("OB-101");
        obra.Nombre.Should().Be("Torre Sky Capital");
        obra.Origen.Should().Be(FilaPinturasObra.OrigenPinturas);
        obra.ReferenciaAw.Should().BeNull();
        obra.Activo.Should().BeTrue();
        obra.EsOrigenPinturas.Should().BeTrue();
        obra.EsOrigenAw.Should().BeFalse();
    }

    [Fact]
    public void FilaPinturasObra_ConOrigenAw_NormalizaMayusculasYAsignaReferencia()
    {
        var obra = new FilaPinturasObra("OB-AW-50", "Hospital Faro del Mayab", "aw", "KA-8890", true);

        obra.Clave.Should().Be("OB-AW-50");
        obra.Nombre.Should().Be("Hospital Faro del Mayab");
        obra.Origen.Should().Be(FilaPinturasObra.OrigenAw);
        obra.ReferenciaAw.Should().Be("KA-8890");
        obra.Activo.Should().BeTrue();
        obra.EsOrigenAw.Should().BeTrue();
        obra.EsOrigenPinturas.Should().BeFalse();
    }

    [Fact]
    public void FilaPinturasObra_ConOrigenInvalido_LanzaArgumentException()
    {
        var act = () => new FilaPinturasObra("OB-X", "Obra Desconocida", "OTRO");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Origen de obra no válido*");
    }

    [Fact]
    public void FilaPinturasEmpleado_ConValoresPorDefecto_InicializaActivoTrue()
    {
        var empleado = new FilaPinturasEmpleado("EMP-001", "Carlos Mendoza");

        empleado.Clave.Should().Be("EMP-001");
        empleado.Nombre.Should().Be("Carlos Mendoza");
        empleado.Activo.Should().BeTrue();
    }

    [Fact]
    public void PinturasCatalogosBatch_Vacio_ColeccionesNoNulasYTotalCero()
    {
        var lote1 = PinturasCatalogosBatch.Empty;
        var lote2 = new PinturasCatalogosBatch();

        lote1.Clientes.Should().BeEmpty();
        lote1.Servicios.Should().BeEmpty();
        lote1.Obras.Should().BeEmpty();
        lote1.Empleados.Should().BeEmpty();
        lote1.TotalElementos.Should().Be(0);
        lote1.EstaVacio.Should().BeTrue();

        lote2.Clientes.Should().BeEmpty();
        lote2.TotalElementos.Should().Be(0);
        lote2.EstaVacio.Should().BeTrue();
    }

    [Fact]
    public void PinturasCatalogosBatch_ConColeccionesNulas_InicializaVacio()
    {
        var batch = new PinturasCatalogosBatch(null!, null!, null!, null!);

        batch.Clientes.Should().NotBeNull().And.BeEmpty();
        batch.Servicios.Should().NotBeNull().And.BeEmpty();
        batch.Obras.Should().NotBeNull().And.BeEmpty();
        batch.Empleados.Should().NotBeNull().And.BeEmpty();
        batch.TotalElementos.Should().Be(0);
        batch.EstaVacio.Should().BeTrue();
    }

    [Fact]
    public void PinturasCatalogosBatch_ConElementos_CalculaTotalCorrectamente()
    {
        var batch = new PinturasCatalogosBatch(
            Clientes: [new FilaPinturasCliente(1, "Cliente 1")],
            Servicios: [new FilaPinturasServicio("S1", "Servicio 1"), new FilaPinturasServicio("S2", "Servicio 2")],
            Obras: [new FilaPinturasObra("O1", "Obra 1")],
            Empleados: [new FilaPinturasEmpleado("E1", "Empleado 1"), new FilaPinturasEmpleado("E2", "Empleado 2"), new FilaPinturasEmpleado("E3", "Empleado 3")]);

        batch.Clientes.Should().HaveCount(1);
        batch.Servicios.Should().HaveCount(2);
        batch.Obras.Should().HaveCount(1);
        batch.Empleados.Should().HaveCount(3);
        batch.TotalElementos.Should().Be(7);
        batch.EstaVacio.Should().BeFalse();
    }

    [Fact]
    public void PinturasPublicacionResultado_CalculaTotalCorrectamente()
    {
        var ahora = DateTimeOffset.UtcNow;
        var resultado = new PinturasPublicacionResultado(
            ClientesPublicados: 10,
            ServiciosPublicados: 5,
            ObrasPublicadas: 2,
            EmpleadosPublicados: 8,
            PublicadoAt: ahora);

        resultado.ClientesPublicados.Should().Be(10);
        resultado.ServiciosPublicados.Should().Be(5);
        resultado.ObrasPublicadas.Should().Be(2);
        resultado.EmpleadosPublicados.Should().Be(8);
        resultado.PublicadoAt.Should().Be(ahora);
        resultado.TotalPublicados.Should().Be(25);
    }

    [Fact]
    public void PinturasPublicacionResultado_Vacio_InicializaEnCeros()
    {
        var res = PinturasPublicacionResultado.Vacio();

        res.ClientesPublicados.Should().Be(0);
        res.ServiciosPublicados.Should().Be(0);
        res.ObrasPublicadas.Should().Be(0);
        res.EmpleadosPublicados.Should().Be(0);
        res.TotalPublicados.Should().Be(0);
        res.PublicadoAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PinturasCatalogosOptions_ValoresPorDefecto_SonValidos()
    {
        var options = new PinturasCatalogosOptions();

        PinturasCatalogosOptions.SectionName.Should().Be("IntegracionesAw:Pinturas:Catalogos");
        options.Habilitado.Should().BeTrue();
        options.IntervaloMinutos.Should().Be(30);
        options.BatchSize.Should().Be(500);
        options.SqlQueryTimeoutSeconds.Should().Be(30);

        var act = () => options.Validar();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void PinturasCatalogosOptions_Validar_LanzaExcepcionSiIntervaloInvalido(int intervalo)
    {
        var options = new PinturasCatalogosOptions { IntervaloMinutos = intervalo };

        var act = () => options.Validar();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IntervaloMinutos debe ser mayor a 0*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-500)]
    public void PinturasCatalogosOptions_Validar_LanzaExcepcionSiBatchSizeInvalido(int batchSize)
    {
        var options = new PinturasCatalogosOptions { BatchSize = batchSize };

        var act = () => options.Validar();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*BatchSize debe ser mayor a 0*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-30)]
    public void PinturasCatalogosOptions_Validar_LanzaExcepcionSiTimeoutInvalido(int timeout)
    {
        var options = new PinturasCatalogosOptions { SqlQueryTimeoutSeconds = timeout };

        var act = () => options.Validar();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SqlQueryTimeoutSeconds debe ser mayor a 0*");
    }
}
