using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// Tests del método <see cref="Requisicion.EditarCabecera"/> (B.4):
/// PATCH parcial sobre cabecera, solo en estado Borrador.
/// </summary>
public class RequisicionEditarCabeceraTests
{
    private static Requisicion CrearBorrador(string? descripcion = "original") =>
        new(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("MID2026-000001"),
            folioAnio: 2026,
            clasificacion: Clasificacion.MateriaPrima,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: descripcion);

    [Fact]
    public void EditarCabecera_AsignaCamposNoNullables_SiVienenConValor()
    {
        var rq = CrearBorrador();

        rq.EditarCabecera(
            descripcion: null,
            fechaEntregaDeseada: null,
            prioridad: Prioridad.Alta,
            proveedorSugeridoId: null,
            clasificacion: Clasificacion.Servicio);

        Assert.Equal(Prioridad.Alta, rq.Prioridad);
        Assert.Equal(Clasificacion.Servicio, rq.Clasificacion);
        // Descripción no tocada porque vino null y limpiar=false.
        Assert.Equal("original", rq.Descripcion);
    }

    [Fact]
    public void EditarCabecera_Descripcion_NullSinLimpiar_NoToca()
    {
        var rq = CrearBorrador("texto previo");

        rq.EditarCabecera(
            descripcion: null,
            fechaEntregaDeseada: null,
            prioridad: null,
            proveedorSugeridoId: null,
            clasificacion: null);

        Assert.Equal("texto previo", rq.Descripcion);
    }

    [Fact]
    public void EditarCabecera_Descripcion_LimpiarTrue_VacíaACampoNull()
    {
        var rq = CrearBorrador("a borrar");

        rq.EditarCabecera(
            descripcion: null,
            fechaEntregaDeseada: null,
            prioridad: null,
            proveedorSugeridoId: null,
            clasificacion: null,
            limpiarDescripcion: true);

        Assert.Null(rq.Descripcion);
    }

    [Fact]
    public void EditarCabecera_Descripcion_NuevoValor_AsignaSinImportarLimpiar()
    {
        var rq = CrearBorrador();

        rq.EditarCabecera(
            descripcion: "nuevo texto",
            fechaEntregaDeseada: null,
            prioridad: null,
            proveedorSugeridoId: null,
            clasificacion: null,
            limpiarDescripcion: true);   // Ignorado porque hay valor.

        Assert.Equal("nuevo texto", rq.Descripcion);
    }

    [Fact]
    public void EditarCabecera_FechaEntrega_AsignacionLimpieza_FuncionaIgualQueDescripcion()
    {
        var rq = CrearBorrador();
        var fecha = new DateOnly(2026, 12, 31);

        // Asignar
        rq.EditarCabecera(null, fecha, null, null, null);
        Assert.Equal(fecha, rq.FechaEntregaDeseada);

        // Limpiar
        rq.EditarCabecera(null, null, null, null, null, limpiarFechaEntregaDeseada: true);
        Assert.Null(rq.FechaEntregaDeseada);
    }

    [Fact]
    public void EditarCabecera_ProveedorSugerido_AsignacionLimpieza_FuncionaIgual()
    {
        var rq = CrearBorrador();
        var provId = Guid.CreateVersion7();

        rq.EditarCabecera(null, null, null, provId, null);
        Assert.Equal(provId, rq.ProveedorSugeridoId);

        rq.EditarCabecera(null, null, null, null, null, limpiarProveedorSugeridoId: true);
        Assert.Null(rq.ProveedorSugeridoId);
    }

    [Fact]
    public void EditarCabecera_NoEnBorrador_Lanza_BusinessRuleException()
    {
        var rq = CrearBorrador();
        // Forzar estado != Borrador via reflection no es necesario:
        // EnviarAAutorizacion lo hace pero requiere líneas. Patrón:
        // agregar línea + transmitir. Para test unit puro, validamos
        // el método de transmisión.
        rq.AgregarLinea(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 1m,
            unidadMedida: "PZA",
            precioEstimado: new SharedKernel.Domain.Money(10m, "MXN"));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.EditarCabecera(
                descripcion: "ya muy tarde",
                fechaEntregaDeseada: null,
                prioridad: null,
                proveedorSugeridoId: null,
                clasificacion: null));

        Assert.Equal("EDITAR_CABECERA_SOLO_EN_BORRADOR", ex.Code);
    }
}
