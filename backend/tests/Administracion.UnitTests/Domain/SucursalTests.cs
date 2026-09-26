using Millet.Administracion.Domain;
using Millet.Catalogos.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Administracion.UnitTests.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="Sucursal"/> (F-Admin-PR2.1).
/// Cubren constructor, invariantes, ActualizarDatos y transiciones
/// Activar/Desactivar.
/// </summary>
public class SucursalTests
{
    private static readonly Guid EmpresaId = Guid.CreateVersion7();

    private static Sucursal Crear(
        string clave = "CDMX",
        string nombre = "Ciudad de México",
        EstatusCatalogo estatus = EstatusCatalogo.Activo) =>
        new(
            id: Guid.CreateVersion7(),
            empresaId: EmpresaId,
            clave: clave,
            nombre: nombre,
            tipo: TipoSucursal.Taller,
            calle: "Calle Ficticia 123",
            numeroExterior: "123",
            colonia: "Colonia de Prueba",
            ciudad: "Mérida",
            municipio: "Mérida",
            estado: "Yucatán",
            codigoPostal: "97000",
            pais: "México",
            estatus: estatus);

    [Fact]
    public void Should_Create_WithDefaultActivo()
    {
        var sucursal = Crear();
        sucursal.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("CLAVE-MUY-LARGA-QUE-EXCEDE-LIMITE")]    // > 20
    public void Should_Reject_InvalidClave(string clave)
    {
        var act = () => Crear(clave: clave);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_CLAVE_INVALIDA");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Should_Reject_Nombre_Empty(string nombre)
    {
        var act = () => Crear(nombre: nombre);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Should_Reject_Nombre_TooLong()
    {
        var act = () => Crear(nombre: new string('x', 255));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_NOMBRE_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyId()
    {
        var act = () => CrearConId(Guid.Empty);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_ID_INVALIDO");
    }

    [Fact]
    public void Constructor_Should_Reject_EmptyEmpresaId()
    {
        var act = () => new Sucursal(
            Guid.CreateVersion7(), Guid.Empty, "CDMX", "Ciudad de México", TipoSucursal.Taller,
            calle: "Calle Ficticia 123", numeroExterior: "123", colonia: "Colonia de Prueba",
            ciudad: "Mérida", municipio: "Mérida", estado: "Yucatán", codigoPostal: "97000", pais: "México");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_EMPRESA_INVALIDA");
    }

    private static Sucursal CrearConId(Guid id) => new(
        id, EmpresaId, "CDMX", "Ciudad de México", TipoSucursal.Taller,
        calle: "Calle Ficticia 123", numeroExterior: "123", colonia: "Colonia de Prueba",
        ciudad: "Mérida", municipio: "Mérida", estado: "Yucatán", codigoPostal: "97000", pais: "México");

    [Fact]
    public void ActualizarDatos_Should_UpdateOnly_NonNull()
    {
        var sucursal = Crear(nombre: "Original");
        sucursal.ActualizarDatos(nombre: "Nueva");
        sucursal.Nombre.Should().Be("Nueva");
    }

    [Fact]
    public void ActualizarDatos_With_NullNombre_Should_NotChange()
    {
        var sucursal = Crear(nombre: "Original");
        sucursal.ActualizarDatos(nombre: null);
        sucursal.Nombre.Should().Be("Original");
    }

    [Fact]
    public void Constructor_Should_Trim_ClaveAw()
    {
        var sucursal = CrearConClaveAw(" CONKAL ");
        sucursal.ClaveAw.Should().Be("CONKAL");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_Should_Reject_ClaveAw_Whitespace(string claveAw)
    {
        var act = () => CrearConClaveAw(claveAw);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_CLAVE_AW_INVALIDA");
    }

    [Fact]
    public void Constructor_Should_Reject_ClaveAw_TooLong()
    {
        var act = () => CrearConClaveAw(new string('x', 41));
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_CLAVE_AW_INVALIDA");
    }

    private static Sucursal CrearConClaveAw(string claveAw) => new(
        Guid.CreateVersion7(), EmpresaId, "CON", "CONKAL", TipoSucursal.Taller,
        calle: "Calle Ficticia 123", numeroExterior: "123", colonia: "Colonia de Prueba",
        ciudad: "Mérida", municipio: "Mérida", estado: "Yucatán", codigoPostal: "97000", pais: "México",
        claveAw: claveAw);

    [Fact]
    public void ActualizarDatos_Should_Set_And_Clear_ClaveAw()
    {
        var sucursal = Crear();
        sucursal.ClaveAw.Should().BeNull();

        sucursal.ActualizarDatos(claveAw: "CONKAL");
        sucursal.ClaveAw.Should().Be("CONKAL");

        // null = no tocar
        sucursal.ActualizarDatos(nombre: "Otro nombre");
        sucursal.ClaveAw.Should().Be("CONKAL");

        sucursal.ActualizarDatos(limpiarClaveAw: true);
        sucursal.ClaveAw.Should().BeNull();
    }

    [Fact]
    public void Desactivar_Then_Activar_Should_BeIdempotent()
    {
        var sucursal = Crear();

        sucursal.Desactivar();
        sucursal.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        sucursal.Desactivar();   // idempotent
        sucursal.Estatus.Should().Be(EstatusCatalogo.Inactivo);

        sucursal.Activar();
        sucursal.Estatus.Should().Be(EstatusCatalogo.Activo);

        sucursal.Activar();      // idempotent
        sucursal.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void CambiarEstatus_Should_AcceptEnRevision()
    {
        var sucursal = Crear();
        sucursal.CambiarEstatus(EstatusCatalogo.EnRevision);
        sucursal.Estatus.Should().Be(EstatusCatalogo.EnRevision);
    }

    [Fact]
    public void Constructor_Should_Accept_Planta_And_Taller()
    {
        var taller = new Sucursal(
            Guid.CreateVersion7(), EmpresaId, "TAL", "Taller Central", TipoSucursal.Taller,
            "Calle 1", "100", "Col", "Mérida", "Mérida", "Yucatán", "97000", "México");
        taller.Tipo.Should().Be(TipoSucursal.Taller);

        var planta = new Sucursal(
            Guid.CreateVersion7(), EmpresaId, "PLN", "Planta Conkal", TipoSucursal.Planta,
            "Calle 2", "200", "Col", "Conkal", "Conkal", "Yucatán", "97345", "México");
        planta.Tipo.Should().Be(TipoSucursal.Planta);
    }

    [Theory]
    [InlineData((TipoSucursal)0)]
    [InlineData((TipoSucursal)3)]
    [InlineData((TipoSucursal)99)]
    public void Constructor_Should_Reject_InvalidTipo(TipoSucursal invalidTipo)
    {
        var act = () => new Sucursal(
            Guid.CreateVersion7(), EmpresaId, "INV", "Invalida", invalidTipo,
            "Calle 1", "100", "Col", "Mérida", "Mérida", "Yucatán", "97000", "México");
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_TIPO_INVALIDO");
    }

    [Fact]
    public void ActualizarDatos_Should_UpdateTipo()
    {
        var sucursal = Crear();
        sucursal.Tipo.Should().Be(TipoSucursal.Taller);

        sucursal.ActualizarDatos(tipo: TipoSucursal.Planta);
        sucursal.Tipo.Should().Be(TipoSucursal.Planta);
    }

    [Fact]
    public void ActualizarDatos_Should_Reject_InvalidTipo()
    {
        var sucursal = Crear();
        var act = () => sucursal.ActualizarDatos(tipo: (TipoSucursal)99);
        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "SUCURSAL_TIPO_INVALIDO");
    }
}
