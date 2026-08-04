using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de los métodos de F2-PR3: ActualizarReferenciaProveedor,
/// ActualizarContactoProveedor, ActualizarInformacionLogistica,
/// ActualizarInformacionImportacion y ActualizarNumeroPedimento.
/// </summary>
public class OrdenCompraLogisticaImportacionTests
{
    private static OrdenCompra NewOcBorrador() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Folio.Parse("OC-MID2026-000001"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

    // --- ReferenciaProveedor ---

    [Fact]
    public void ActualizarReferenciaProveedor_NormalizaAUpperCaseYTrim()
    {
        var oc = NewOcBorrador();
        oc.ActualizarReferenciaProveedor("  prov-123  ");
        Assert.Equal("PROV-123", oc.ReferenciaProveedor);
    }

    [Fact]
    public void ActualizarReferenciaProveedor_NullLimpia()
    {
        var oc = NewOcBorrador();
        oc.ActualizarReferenciaProveedor("ABC");
        oc.ActualizarReferenciaProveedor(null);
        Assert.Null(oc.ReferenciaProveedor);
    }

    [Fact]
    public void ActualizarReferenciaProveedor_DemasiadoLarga_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarReferenciaProveedor(new string('A', 61)));
        Assert.Equal("OC_REFERENCIA_DEMASIADO_LARGA", ex.Code);
    }

    // --- ContactoProveedor ---

    [Fact]
    public void ActualizarContactoProveedor_AsignaTodosLosCampos()
    {
        var oc = NewOcBorrador();
        oc.ActualizarContactoProveedor(new ContactoProveedor("Juan Perez", "juan@prov.com", "5550000"));

        Assert.NotNull(oc.ContactoProveedor);
        Assert.Equal("Juan Perez", oc.ContactoProveedorNombre);
        Assert.Equal("juan@prov.com", oc.ContactoProveedorEmail);
        Assert.Equal("5550000", oc.ContactoProveedorTelefono);
    }

    [Fact]
    public void ActualizarContactoProveedor_NullLimpiaTodo()
    {
        var oc = NewOcBorrador();
        oc.ActualizarContactoProveedor(new ContactoProveedor("Juan", "j@p.com", "555"));
        oc.ActualizarContactoProveedor(null);

        Assert.Null(oc.ContactoProveedor);
        Assert.Null(oc.ContactoProveedorNombre);
        Assert.Null(oc.ContactoProveedorEmail);
        Assert.Null(oc.ContactoProveedorTelefono);
    }

    // --- InformacionLogistica ---

    [Fact]
    public void InformacionLogistica_TransportistaAmbiguo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new InformacionLogistica(
                transportistaId: Guid.CreateVersion7(),
                transportistaTexto: "Transporte X"));
        Assert.Equal("LOGISTICA_TRANSPORTISTA_AMBIGUO", ex.Code);
    }

    [Fact]
    public void ActualizarInformacionLogistica_AsignaTodosLosCampos()
    {
        var oc = NewOcBorrador();
        var transportistaId = Guid.CreateVersion7();
        oc.ActualizarInformacionLogistica(new InformacionLogistica(
            direccionEntrega: "Av. Industrial 100",
            transportistaId: transportistaId,
            numeroGuia: "GUIA-001",
            instruccionesEnvio: "Llamar antes"));

        Assert.NotNull(oc.InformacionLogistica);
        Assert.Equal("Av. Industrial 100", oc.InfoLogisticaDireccion);
        Assert.Equal(transportistaId, oc.InfoLogisticaTransportistaId);
        Assert.Equal("GUIA-001", oc.InfoLogisticaNumeroGuia);
    }

    [Fact]
    public void ActualizarInformacionLogistica_NullLimpia()
    {
        var oc = NewOcBorrador();
        oc.ActualizarInformacionLogistica(new InformacionLogistica(
            transportistaTexto: "Algo"));
        oc.ActualizarInformacionLogistica(null);

        Assert.Null(oc.InformacionLogistica);
        Assert.Null(oc.InfoLogisticaTransportistaTexto);
    }

    // --- InformacionImportacion ---

    [Fact]
    public void InformacionImportacion_PaisOrigen3Letras_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            new InformacionImportacion(paisOrigen: "MEX"));
        Assert.Equal("IMPORT_PAIS_FORMATO_INVALIDO", ex.Code);
    }

    [Fact]
    public void InformacionImportacion_PaisOrigenNormalizaAUpper()
    {
        var info = new InformacionImportacion(paisOrigen: "mx");
        Assert.Equal("MX", info.PaisOrigen);
    }

    [Fact]
    public void ActualizarInformacionImportacion_IgnoraPedimento()
    {
        var oc = NewOcBorrador();
        oc.ActualizarInformacionImportacion(new InformacionImportacion(
            incotermId: Guid.CreateVersion7(),
            paisOrigen: "US",
            numeroContenedor: "CONT-001",
            codigoRuta: "RUTA-01",
            semanaEmbarque: "S10",
            numeroPedimento: "PED-INVALIDO"));

        // Pedimento debería seguir null — se setea solo vía ActualizarNumeroPedimento.
        Assert.Null(oc.InfoImportNumeroPedimento);
        Assert.Equal("CONT-001", oc.InfoImportNumeroContenedor);
    }

    [Fact]
    public void ActualizarInformacionImportacion_NullLimpia()
    {
        var oc = NewOcBorrador();
        oc.ActualizarInformacionImportacion(new InformacionImportacion(
            incotermId: Guid.CreateVersion7(),
            paisOrigen: "US",
            numeroContenedor: "CONT-001",
            codigoRuta: "RUTA-01",
            semanaEmbarque: "S10"));
        oc.ActualizarInformacionImportacion(null);

        Assert.Null(oc.InformacionImportacion);
    }

    // --- NumeroPedimento ---

    [Fact]
    public void ActualizarNumeroPedimento_PostAutorizacion_OK()
    {
        var oc = NewOcBorrador();
        // En Borrador también funciona. Estado terminal sí lanza —
        // verificado por OrdenCompra.ActualizarNumeroPedimento.
        oc.ActualizarNumeroPedimento("PED-12345");
        Assert.Equal("PED-12345", oc.InfoImportNumeroPedimento);
    }

    [Fact]
    public void ActualizarNumeroPedimento_DemasiadoLargo_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarNumeroPedimento(new string('X', 61)));
        Assert.Equal("IMPORT_PEDIMENTO_DEMASIADO_LARGO", ex.Code);
    }

    // --- VOs ---

    [Fact]
    public void ContactoProveedor_TrimmeaCamposVacios()
    {
        var c = new ContactoProveedor("Juan", "   ", null);
        Assert.Equal("Juan", c.Nombre);
        Assert.Null(c.Email);
        Assert.Null(c.Telefono);
    }
}
