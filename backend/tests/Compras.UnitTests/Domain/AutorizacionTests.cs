using Millet.Compras.Domain;
using Millet.Compras.Domain.Matriz;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Administracion.Domain;
using Millet.Almacen.Domain;
using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Domain;

namespace Millet.Compras.UnitTests.Domain;

/// <summary>
/// Tests del flujo de transmitir + autorizar (F2-PR3) sobre el agregado
/// <see cref="Requisicion"/>. Cubre invariantes de transición, secuencia
/// N1→N2, unicidad por nivel, y el cumplimiento de matriz.
/// </summary>
public class AutorizacionTests
{
    private static readonly Money PrecioDefault = Money.Mxn(15m);

    private static Requisicion CrearRqConLineas(int lineas = 1)
    {
        var rq = RequisicionTestsHelper.RequisicionEnBorrador();
        for (var i = 0; i < lineas; i++)
        {
            rq.AgregarLinea(
                lineaId: Guid.CreateVersion7(),
                articuloId: Guid.CreateVersion7(),
                cantidad: 10m,
                unidadMedida: "PZA",
                precioEstimado: PrecioDefault);
        }
        return rq;
    }

    // --- EnviarAAutorizacion ---

    [Fact]
    public void EnviarAAutorizacion_Should_Succeed_When_BorradorConLineas()
    {
        var rq = CrearRqConLineas();

        var evento = rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.EnAutorizacion, rq.Estado);
        Assert.Equal(rq.Id, evento.RequisicionId);
        Assert.Equal(rq.Folio.Valor, evento.Folio);
    }

    [Fact]
    public void EnviarAAutorizacion_Should_Throw_When_SinLineas()
    {
        var rq = RequisicionTestsHelper.RequisicionEnBorrador();

        var ex = Assert.Throws<BusinessRuleException>(() => rq.EnviarAAutorizacion(DateTimeOffset.UtcNow));
        Assert.Equal("TRANSMITIR_SIN_LINEAS", ex.Code);
    }

    [Fact]
    public void EnviarAAutorizacion_Should_Throw_When_NotInBorrador()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleException>(() => rq.EnviarAAutorizacion(DateTimeOffset.UtcNow));
        Assert.Equal("TRANSMITIR_SOLO_DESDE_BORRADOR", ex.Code);
    }

    // --- RegistrarAutorizacion ---

    [Fact]
    public void RegistrarAutorizacion_Nivel1_SoloN1_Should_Transition_To_Autorizada()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            requiereNivel: RequiereNivel.SoloN1);

        Assert.Equal(EstadoRequisicion.Autorizada, rq.Estado);
        Assert.Single(rq.Autorizaciones);
    }

    [Fact]
    public void RegistrarAutorizacion_Nivel1_RequiereN1YN2_Should_StayInEnAutorizacion()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            requiereNivel: RequiereNivel.N1YN2);

        Assert.Equal(EstadoRequisicion.EnAutorizacion, rq.Estado);
        Assert.Single(rq.Autorizaciones);
    }

    [Fact]
    public void RegistrarAutorizacion_Nivel2_AfterNivel1_Should_Transition_To_Autorizada()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel1,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2);
        rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel2,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2);

        Assert.Equal(EstadoRequisicion.Autorizada, rq.Estado);
        Assert.Equal(2, rq.Autorizaciones.Count);
    }

    [Fact]
    public void RegistrarAutorizacion_Nivel2_Without_Nivel1_Should_Throw()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel2,
                Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2));
        Assert.Equal("AUTORIZACION_NIVEL2_SIN_NIVEL1", ex.Code);
    }

    [Fact]
    public void RegistrarAutorizacion_Duplicate_Nivel1_Should_Throw()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel1,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel1,
                Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2));
        Assert.Equal("AUTORIZACION_NIVEL_DUPLICADO", ex.Code);
    }

    [Fact]
    public void RegistrarAutorizacion_Should_Throw_When_NotInEnAutorizacion()
    {
        var rq = CrearRqConLineas();
        // estado = Borrador

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarAutorizacion(Guid.CreateVersion7(), NivelAutorizacion.Nivel1,
                Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.SoloN1));
        Assert.Equal("AUTORIZAR_SOLO_EN_AUTORIZACION", ex.Code);
    }

    // --- F4-PR2: MatrizAprobacionSatisfechaEvent ---

    [Fact]
    public void RegistrarAutorizacion_MatrizCumplida_Should_ReturnEvent()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        var ahora = DateTimeOffset.UtcNow;

        var resultado = rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: ahora,
            requiereNivel: RequiereNivel.SoloN1);

        Assert.NotNull(resultado.MatrizSatisfecha);
        Assert.Equal(rq.Id, resultado.MatrizSatisfecha!.RequisicionId);
        Assert.Equal(rq.EmpresaId, resultado.MatrizSatisfecha.EmpresaId);
        Assert.Equal(ahora, resultado.MatrizSatisfecha.OcurridoEn);
    }

    [Fact]
    public void RegistrarAutorizacion_MatrizNoCumplida_Should_ReturnNullEvent()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        // N1 con RequiereN1YN2 → no cumple matriz, queda esperando N2.
        var resultado = rq.RegistrarAutorizacion(
            Guid.CreateVersion7(), NivelAutorizacion.Nivel1,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2);

        Assert.Null(resultado.MatrizSatisfecha);
        Assert.Equal(EstadoRequisicion.EnAutorizacion, rq.Estado);
    }

    [Fact]
    public void RegistrarAutorizacion_MatrizCumplida_PorN2TrasN1_Should_ReturnEvent()
    {
        var rq = CrearRqConLineas();
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);

        var primeraN1 = rq.RegistrarAutorizacion(
            Guid.CreateVersion7(), NivelAutorizacion.Nivel1,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2);
        Assert.Null(primeraN1.MatrizSatisfecha);

        var segundaN2 = rq.RegistrarAutorizacion(
            Guid.CreateVersion7(), NivelAutorizacion.Nivel2,
            Guid.CreateVersion7(), DateTimeOffset.UtcNow, RequiereNivel.N1YN2);

        Assert.NotNull(segundaN2.MatrizSatisfecha);
    }
}
