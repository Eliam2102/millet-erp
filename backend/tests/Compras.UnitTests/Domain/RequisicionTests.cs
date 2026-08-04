using Millet.Compras.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Domain;

public class RequisicionTests
{
    private static Requisicion CrearValida(
        Guid? empresaId = null,
        Folio? folio = null,
        short folioAnio = 2026,
        Clasificacion clasificacion = Clasificacion.MateriaPrima,
        Prioridad prioridad = Prioridad.Normal,
        string? descripcion = null)
    {
        return new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: empresaId ?? Guid.CreateVersion7(),
            folio: folio ?? Folio.Parse("MID2026-000001"),
            folioAnio: folioAnio,
            clasificacion: clasificacion,
            sucursalId: Guid.CreateVersion7(),
            departamentoId: Guid.CreateVersion7(),
            almacenDestinoId: Guid.CreateVersion7(),
            requisitanteId: Guid.CreateVersion7(),
            creadorId: Guid.CreateVersion7(),
            prioridad: prioridad,
            fechaSolicitud: DateTimeOffset.UtcNow,
            descripcion: descripcion);
    }

    [Fact]
    public void Should_Create_WithEstadoBorrador_When_ValuesAreValid()
    {
        var rq = CrearValida();

        Assert.Equal(EstadoRequisicion.Borrador, rq.Estado);
    }

    [Fact]
    public void Should_AssignAllStructuralFields()
    {
        var folio = Folio.Parse("MID2026-000042");
        var empresaId = Guid.CreateVersion7();

        var rq = CrearValida(empresaId: empresaId, folio: folio, folioAnio: 2026,
                             clasificacion: Clasificacion.Servicio, prioridad: Prioridad.Alta,
                             descripcion: "Compra de tornillería");

        Assert.Equal(empresaId, rq.EmpresaId);
        Assert.Equal(folio, rq.Folio);
        Assert.Equal((short)2026, rq.FolioAnio);
        Assert.Equal(Clasificacion.Servicio, rq.Clasificacion);
        Assert.Equal(Prioridad.Alta, rq.Prioridad);
        Assert.Equal("Compra de tornillería", rq.Descripcion);
    }

    [Fact]
    public void Should_LeaveTerminationFieldsNull_When_NewlyCreated()
    {
        var rq = CrearValida();

        Assert.Null(rq.MotivoTerminacionId);
        Assert.Null(rq.MotivoTerminacionTexto);
        Assert.Null(rq.ActorTerminacionId);
        Assert.Null(rq.FechaTerminacion);
    }

    [Theory]
    [InlineData("empresaId")]
    [InlineData("sucursalId")]
    [InlineData("departamentoId")]
    [InlineData("almacenDestinoId")]
    [InlineData("requisitanteId")]
    [InlineData("creadorId")]
    public void Should_Throw_When_StructuralGuidIsEmpty(string nombreCampo)
    {
        var folio = Folio.Parse("MID2026-000001");
        var ahora = DateTimeOffset.UtcNow;

        var ex = Assert.Throws<BusinessRuleException>(() =>
            new Requisicion(
                id: Guid.CreateVersion7(),
                empresaId: nombreCampo == "empresaId" ? Guid.Empty : Guid.CreateVersion7(),
                folio: folio,
                folioAnio: 2026,
                clasificacion: Clasificacion.MateriaPrima,
                sucursalId: nombreCampo == "sucursalId" ? Guid.Empty : Guid.CreateVersion7(),
                departamentoId: nombreCampo == "departamentoId" ? Guid.Empty : Guid.CreateVersion7(),
                almacenDestinoId: nombreCampo == "almacenDestinoId" ? Guid.Empty : Guid.CreateVersion7(),
                requisitanteId: nombreCampo == "requisitanteId" ? Guid.Empty : Guid.CreateVersion7(),
                creadorId: nombreCampo == "creadorId" ? Guid.Empty : Guid.CreateVersion7(),
                prioridad: Prioridad.Normal,
                fechaSolicitud: ahora));

        Assert.Equal("GUID_VACIO", ex.Code);
        Assert.Contains(nombreCampo, ex.Message);
    }

    [Theory]
    [InlineData((short)1999)]
    [InlineData((short)2101)]
    public void Should_Throw_When_FolioAnioOutOfRange(short anio)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => CrearValida(folioAnio: anio));

        Assert.Equal("FOLIO_ANIO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Should_Throw_When_DescripcionExceeds500Chars()
    {
        var descripcionLarga = new string('x', 501);

        var ex = Assert.Throws<BusinessRuleException>(() => CrearValida(descripcion: descripcionLarga));

        Assert.Equal("DESCRIPCION_DEMASIADO_LARGA", ex.Code);
    }

    [Fact]
    public void Should_Accept_Descripcion_AtBoundary()
    {
        var descripcion500 = new string('x', 500);

        var rq = CrearValida(descripcion: descripcion500);

        Assert.Equal(descripcion500, rq.Descripcion);
    }

    [Fact]
    public void Should_AcceptOptionalFieldsAsNull()
    {
        var rq = CrearValida();

        Assert.Null(rq.FechaEntregaDeseada);
        Assert.Null(rq.ProveedorSugeridoId);
        Assert.Null(rq.Descripcion);
    }

    // --- F2-PR1: manipulación de líneas ---

    [Fact]
    public void AgregarLinea_Should_Succeed_When_Estado_Borrador()
    {
        var rq = CrearValida();

        var linea = rq.AgregarLinea(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioEstimado: Millet.SharedKernel.Domain.Money.Mxn(15.50m));

        Assert.Single(rq.Lineas);
        Assert.Equal((short)1, linea.Posicion);
    }

    [Fact]
    public void AgregarLinea_Should_AsignarPosicionConsecutiva()
    {
        var rq = CrearValida();

        var l1 = rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(10m));
        var l2 = rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 2m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(20m));

        Assert.Equal((short)1, l1.Posicion);
        Assert.Equal((short)2, l2.Posicion);
    }

    [Fact]
    public void AgregarLinea_Should_FailWith422_When_LineaCantidadInvalida()
    {
        var rq = CrearValida();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), cantidad: 0m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(10m)));

        Assert.Equal("LINEA_CANTIDAD_INVALIDA", ex.Code);
    }

    [Fact]
    public void ActualizarLineaEstructural_Should_Succeed_When_SinCubrimiento_EnBorrador()
    {
        var rq = CrearValida();
        var linea = rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(10m));

        rq.ActualizarLineaEstructural(
            lineaId: linea.Id,
            articuloId: Guid.CreateVersion7(),
            cantidad: 25m,
            unidadMedida: "KG",
            precioEstimado: Millet.SharedKernel.Domain.Money.Mxn(20m));

        Assert.Equal(25m, linea.Cantidad);
        Assert.Equal("KG", linea.UnidadMedida);
    }

    [Fact]
    public void ActualizarLineaNotas_Should_Succeed_When_NotInTerminalState()
    {
        var rq = CrearValida();
        var linea = rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(10m));

        rq.ActualizarLineaNotas(linea.Id, "Pendiente de revisión");

        Assert.Equal("Pendiente de revisión", linea.Notas);
    }

    [Fact]
    public void EliminarLinea_Should_Succeed_When_Estado_Borrador()
    {
        var rq = CrearValida();
        var linea = rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA", Millet.SharedKernel.Domain.Money.Mxn(10m));

        rq.EliminarLinea(linea.Id);

        Assert.Empty(rq.Lineas);
    }

    [Fact]
    public void EliminarLinea_Should_Throw_When_LineaNoExiste()
    {
        var rq = CrearValida();

        var ex = Assert.Throws<BusinessRuleException>(() => rq.EliminarLinea(Guid.CreateVersion7()));

        Assert.Equal("LINEA_NO_ENCONTRADA", ex.Code);
    }

    // --- F2-PR4: rechazar / eliminar ---

    private static Requisicion CrearEnAutorizacion()
    {
        var rq = CrearValida();
        rq.AgregarLinea(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            10m,
            "PZA",
            Millet.SharedKernel.Domain.Money.Mxn(15m));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        return rq;
    }

    [Fact]
    public void Rechazar_Should_TransicionarA_Rechazada_Y_LlenarCamposTerminacion()
    {
        var rq = CrearEnAutorizacion();
        var motivoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        rq.Rechazar(motivoId, actorId, ahora, "presupuesto agotado");

        Assert.Equal(EstadoRequisicion.Rechazada, rq.Estado);
        Assert.Equal(motivoId, rq.MotivoTerminacionId);
        Assert.Equal("presupuesto agotado", rq.MotivoTerminacionTexto);
        Assert.Equal(actorId, rq.ActorTerminacionId);
        Assert.Equal(ahora, rq.FechaTerminacion);
    }

    [Fact]
    public void Rechazar_Should_Throw_When_NoEstaEnAutorizacion()
    {
        var rq = CrearValida(); // Borrador

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Rechazar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("RECHAZAR_SOLO_DESDE_EN_AUTORIZACION", ex.Code);
    }

    [Fact]
    public void Rechazar_Should_Throw_When_MotivoIdVacio()
    {
        var rq = CrearEnAutorizacion();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Rechazar(Guid.Empty, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("MOTIVO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Rechazar_Should_Throw_When_ActorVacio()
    {
        var rq = CrearEnAutorizacion();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Rechazar(Guid.CreateVersion7(), Guid.Empty, DateTimeOffset.UtcNow));

        Assert.Equal("ACTOR_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Rechazar_Should_Throw_When_TextoExcede500()
    {
        var rq = CrearEnAutorizacion();
        var textoLargo = new string('x', 501);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Rechazar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow, textoLargo));

        Assert.Equal("MOTIVO_TEXTO_DEMASIADO_LARGO", ex.Code);
    }

    [Fact]
    public void Eliminar_Should_TransicionarA_Eliminada_DesdeBorrador()
    {
        var rq = CrearValida();
        var motivoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        rq.Eliminar(motivoId, actorId, ahora);

        Assert.Equal(EstadoRequisicion.Eliminada, rq.Estado);
        Assert.Equal(motivoId, rq.MotivoTerminacionId);
        Assert.Equal(actorId, rq.ActorTerminacionId);
        Assert.Equal(ahora, rq.FechaTerminacion);
    }

    [Fact]
    public void Eliminar_Should_TransicionarA_Eliminada_DesdeEnAutorizacion()
    {
        var rq = CrearEnAutorizacion();

        rq.Eliminar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.Eliminada, rq.Estado);
    }

    [Fact]
    public void Eliminar_NoToca_DeletedAt()
    {
        var rq = CrearValida();
        Assert.Null(rq.DeletedAt);

        rq.Eliminar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        // Estado terminal de negocio, NO soft-delete del framework.
        Assert.Null(rq.DeletedAt);
    }

    [Theory]
    [InlineData(EstadoRequisicion.Autorizada)]
    [InlineData(EstadoRequisicion.Rechazada)]
    [InlineData(EstadoRequisicion.Eliminada)]
    [InlineData(EstadoRequisicion.Cancelada)]
    [InlineData(EstadoRequisicion.Cerrada)]
    public void Eliminar_Should_Throw_When_EstadoNoPermitido(EstadoRequisicion estadoForzado)
    {
        var rq = CrearValida();
        // Usamos reflection para forzar el estado: el agregado no expone
        // setter público, pero la regla bajo prueba es la guardia, no la
        // máquina de estados completa.
        var prop = typeof(Requisicion).GetProperty(nameof(Requisicion.Estado))!;
        prop.SetValue(rq, estadoForzado);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Eliminar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("ELIMINAR_SOLO_DESDE_BORRADOR_O_EN_AUTORIZACION", ex.Code);
    }

    // --- F4-PR1: registrar cubrimiento ---

    /// <summary>
    /// Construye una RQ con N líneas de cantidad fija y la lleva a
    /// estado <c>Autorizada</c>: transmite + autoriza N1 con
    /// <c>RequiereNivel.SoloN1</c> (cumple matriz inmediato).
    /// </summary>
    private static Requisicion CrearAutorizada(params decimal[] cantidades)
    {
        var rq = CrearValida();
        foreach (var cant in cantidades)
        {
            rq.AgregarLinea(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                cant,
                "PZA",
                Millet.SharedKernel.Domain.Money.Mxn(15m));
        }
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow);
        rq.RegistrarAutorizacion(
            autorizacionId: Guid.CreateVersion7(),
            nivel: NivelAutorizacion.Nivel1,
            usuarioId: Guid.CreateVersion7(),
            fechaHora: DateTimeOffset.UtcNow,
            requiereNivel: Millet.Compras.Domain.Matriz.RequiereNivel.SoloN1);
        return rq;
    }

    [Fact]
    public void RegistrarCubrimiento_StockTotal_TransicionaA_EnSurtido_SinCerrar()
    {
        // ADR-0043 #3 (conmutación): el cubrimiento 100% stock YA NO cierra la
        // RQ; queda en EnSurtido (antes iba directo Autorizada→Cerrada). Así el
        // material de stock se entrega por Salidas y la RQ cierra por entrega.
        var rq = CrearAutorizada(10m);
        var lineaId = rq.Lineas.Single().Id;

        var resultado = rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 10m, CantidadDeCompra: 0m) },
            DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        Assert.Equal(EstadoRequisicion.EnSurtido, resultado.CubrimientoEvento.EstadoFinal);
        // Ya no se emite RequisicionCerradaEvent al cubrir (el cierre es por entrega).
        Assert.Null(resultado.CerradaEvento);

        var linea = rq.Lineas.Single();
        Assert.Equal(10m, linea.CantidadDeAlmacen);
        Assert.Equal(0m, linea.CantidadDeCompra);
        Assert.Equal(0m, linea.Cubrimiento.CantidadPendiente);
    }

    [Fact]
    public void RegistrarCubrimiento_ConSaldo_NoEmiteCerrada()
    {
        var rq = CrearAutorizada(10m);
        var lineaId = rq.Lineas.Single().Id;

        var resultado = rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 4m, CantidadDeCompra: 6m) },
            DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        Assert.Null(resultado.CerradaEvento);
    }

    [Fact]
    public void RegistrarCubrimiento_StockCero_TransicionaA_EnSurtido()
    {
        var rq = CrearAutorizada(10m);
        var lineaId = rq.Lineas.Single().Id;

        rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 0m, CantidadDeCompra: 10m) },
            DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        var linea = rq.Lineas.Single();
        Assert.Equal(10m, linea.CantidadDeCompra);
        Assert.Equal(10m, linea.Cubrimiento.CantidadPendiente);
    }

    [Fact]
    public void RegistrarCubrimiento_Mixto_TransicionaA_EnSurtido()
    {
        var rq = CrearAutorizada(10m, 20m);
        var lineas = rq.Lineas.ToList();

        rq.RegistrarCubrimiento(
            new[]
            {
                new CubrimientoLinea(lineas[0].Id, 10m, 0m), // todo almacén
                new CubrimientoLinea(lineas[1].Id, 5m, 15m), // mitad y mitad
            },
            DateTimeOffset.UtcNow);

        // Cualquier saldo > 0 → EnSurtido (aunque la 1ra esté cubierta).
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
    }

    [Fact]
    public void RegistrarCubrimiento_DesdeEnAutorizacion_Lanza()
    {
        var rq = CrearValida();
        rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA",
                        Millet.SharedKernel.Domain.Money.Mxn(15m));
        rq.EnviarAAutorizacion(DateTimeOffset.UtcNow); // EnAutorizacion, no Autorizada

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarCubrimiento(
                new[] { new CubrimientoLinea(rq.Lineas.Single().Id, 10m, 0m) },
                DateTimeOffset.UtcNow));

        Assert.Equal("CUBRIMIENTO_SOLO_DESDE_AUTORIZADA", ex.Code);
    }

    [Fact]
    public void RegistrarCubrimiento_ListaVacia_Lanza()
    {
        var rq = CrearAutorizada(10m);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarCubrimiento(Array.Empty<CubrimientoLinea>(), DateTimeOffset.UtcNow));

        Assert.Equal("CUBRIMIENTO_LINEAS_VACIO", ex.Code);
    }

    [Fact]
    public void RegistrarCubrimiento_CountMismatch_Lanza()
    {
        var rq = CrearAutorizada(10m, 20m); // 2 líneas

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarCubrimiento(
                new[] { new CubrimientoLinea(rq.Lineas.First().Id, 10m, 0m) }, // solo 1
                DateTimeOffset.UtcNow));

        Assert.Equal("CUBRIMIENTO_LINEAS_DESPAREJAS", ex.Code);
    }

    [Fact]
    public void RegistrarCubrimiento_LineaInexistente_Lanza()
    {
        var rq = CrearAutorizada(10m);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarCubrimiento(
                new[] { new CubrimientoLinea(Guid.CreateVersion7(), 10m, 0m) },
                DateTimeOffset.UtcNow));

        Assert.Equal("LINEA_NO_ENCONTRADA", ex.Code);
    }

    [Fact]
    public void RegistrarCubrimiento_SumaExcedeOriginal_Lanza()
    {
        var rq = CrearAutorizada(10m);
        var lineaId = rq.Lineas.Single().Id;

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarCubrimiento(
                new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 8m, CantidadDeCompra: 5m) },
                DateTimeOffset.UtcNow));

        Assert.Equal("CUBRIMIENTO_EXCEDE_ORIGINAL", ex.Code);
    }

    [Fact]
    public void RegistrarCubrimiento_DevuelveEvento_ConSnapshotPorLinea()
    {
        var rq = CrearAutorizada(10m, 20m);
        var lineas = rq.Lineas.ToList();
        var ahora = DateTimeOffset.UtcNow;

        var resultado = rq.RegistrarCubrimiento(
            new[]
            {
                new CubrimientoLinea(lineas[0].Id, 10m, 0m),
                new CubrimientoLinea(lineas[1].Id, 5m, 15m),
            },
            ahora);

        var evento = resultado.CubrimientoEvento;
        Assert.Equal(rq.Id, evento.RequisicionId);
        Assert.Equal(rq.EmpresaId, evento.EmpresaId);
        Assert.Equal(EstadoRequisicion.EnSurtido, evento.EstadoFinal);
        Assert.Equal(ahora, evento.OcurridoEn);
        Assert.Equal(2, evento.Lineas.Count);
        var snap0 = evento.Lineas.Single(s => s.LineaId == lineas[0].Id);
        Assert.Equal(10m, snap0.CantidadDeAlmacen);
        Assert.Equal(0m, snap0.CantidadDeCompra);
    }

    // --- F4-PR3: cancelar ---

    /// <summary>
    /// Crea una RQ y la deja en estado <c>EnSurtido</c> (cubrimiento con
    /// saldo a OC). Configura la cantidad para que parte vaya a almacén
    /// y parte a compra, validando que <c>RegistrarCubrimiento</c>
    /// transicione correctamente.
    /// </summary>
    private static Requisicion CrearEnSurtido()
    {
        var rq = CrearAutorizada(10m);
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 4m, CantidadDeCompra: 6m) },
            DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        return rq;
    }

    [Fact]
    public void Cancelar_DesdeEnSurtido_TransicionaA_Cancelada_Y_LlenaCamposTerminacion()
    {
        var rq = CrearEnSurtido();
        var motivoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        var evento = rq.Cancelar(motivoId, actorId, ahora, "presupuesto recortado");

        Assert.Equal(EstadoRequisicion.Cancelada, rq.Estado);
        Assert.Equal(motivoId, rq.MotivoTerminacionId);
        Assert.Equal("presupuesto recortado", rq.MotivoTerminacionTexto);
        Assert.Equal(actorId, rq.ActorTerminacionId);
        Assert.Equal(ahora, rq.FechaTerminacion);

        Assert.Equal(rq.Id, evento.RequisicionId);
        Assert.Equal(rq.EmpresaId, evento.EmpresaId);
        Assert.Equal(motivoId, evento.MotivoId);
        Assert.Equal(actorId, evento.ActorId);
        Assert.Equal(ahora, evento.OcurridoEn);
    }

    [Fact]
    public void Cancelar_DesdeAutorizada_OK()
    {
        // Construye RQ y fuerza estado a Autorizada (caso defensivo del
        // diseño: RegistrarCubrimiento puede dejarla ahí si el cubrimiento
        // sale degenerado).
        var rq = CrearAutorizada(10m);
        // No llamamos RegistrarCubrimiento → queda en Autorizada.

        rq.Cancelar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.Cancelada, rq.Estado);
    }

    [Theory]
    [InlineData(EstadoRequisicion.Borrador)]
    [InlineData(EstadoRequisicion.EnAutorizacion)]
    [InlineData(EstadoRequisicion.Cerrada)]
    [InlineData(EstadoRequisicion.Cancelada)]
    [InlineData(EstadoRequisicion.Rechazada)]
    [InlineData(EstadoRequisicion.Eliminada)]
    public void Cancelar_DesdeEstadoNoPermitido_Lanza(EstadoRequisicion estadoForzado)
    {
        var rq = CrearValida();
        var prop = typeof(Requisicion).GetProperty(nameof(Requisicion.Estado))!;
        prop.SetValue(rq, estadoForzado);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Cancelar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("CANCELAR_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO", ex.Code);
    }

    [Fact]
    public void Cancelar_Doble_Lanza_DesdeCancelada()
    {
        var rq = CrearEnSurtido();
        rq.Cancelar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Cancelar(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("CANCELAR_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO", ex.Code);
    }

    [Fact]
    public void Cancelar_MotivoIdVacio_Lanza()
    {
        var rq = CrearEnSurtido();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Cancelar(Guid.Empty, Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("MOTIVO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Cancelar_ActorVacio_Lanza()
    {
        var rq = CrearEnSurtido();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.Cancelar(Guid.CreateVersion7(), Guid.Empty, DateTimeOffset.UtcNow));

        Assert.Equal("ACTOR_REQUERIDO", ex.Code);
    }

    // --- F5-PR1: registrar recepción + cierre automático ---

    [Fact]
    public void RegistrarRecepcion_Parcial_ActualizaCantidadYDejaEnSurtido()
    {
        var rq = CrearEnSurtido(); // CantidadDeAlmacen=4, CantidadDeCompra=6, original=10
        var lineaId = rq.Lineas.Single().Id;

        var evento = rq.RegistrarRecepcion(lineaId, cantidadRecibida: 3m, ocurridoEn: DateTimeOffset.UtcNow);

        Assert.Null(evento); // todavía pendiente
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        var linea = rq.Lineas.Single();
        Assert.Equal(3m, linea.CantidadRecibida);
        Assert.Equal(3m, linea.Cubrimiento.CantidadPendiente); // 10 - 4 - 3
    }

    [Fact]
    public void RegistrarRecepcion_Total_AcumulaRecibida_NoCierra()
    {
        // ADR-0043 #3: la recepción YA NO cierra; acumula CantidadRecibida y la
        // RQ se queda en EnSurtido (cierra por entrega, no por recepción).
        var rq = CrearEnSurtido(); // 10 = 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;
        var ahora = DateTimeOffset.UtcNow;

        var evento = rq.RegistrarRecepcion(lineaId, cantidadRecibida: 6m, ocurridoEn: ahora);

        Assert.Null(evento); // ya no devuelve evento de cierre
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        var linea = rq.Lineas.Single();
        Assert.Equal(6m, linea.CantidadRecibida);
        Assert.Equal(0m, linea.Cubrimiento.CantidadPendiente); // cubierto, pero NO cerrado
    }

    [Fact]
    public void RegistrarRecepcion_Acumulativa_NoCierra()
    {
        var rq = CrearEnSurtido(); // 10 = 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;

        rq.RegistrarRecepcion(lineaId, 4m, DateTimeOffset.UtcNow);
        var ev = rq.RegistrarRecepcion(lineaId, 2m, DateTimeOffset.UtcNow);

        Assert.Null(ev);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
        Assert.Equal(6m, rq.Lineas.Single().CantidadRecibida);
    }

    [Fact]
    public void RegistrarRecepcion_ExcedeCantidadDeCompra_Lanza()
    {
        var rq = CrearEnSurtido(); // 10 = 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarRecepcion(lineaId, cantidadRecibida: 7m, ocurridoEn: DateTimeOffset.UtcNow));

        // Cubrimiento ctor lanza CUBRIMIENTO_RECIBIDA_EXCEDE_COMPRA.
        Assert.Equal("CUBRIMIENTO_RECIBIDA_EXCEDE_COMPRA", ex.Code);
        // No mutó la línea (recepción atómica).
        Assert.Equal(0m, rq.Lineas.Single().CantidadRecibida);
    }

    [Fact]
    public void RegistrarRecepcion_CantidadInvalida_Lanza()
    {
        var rq = CrearEnSurtido();
        var lineaId = rq.Lineas.Single().Id;

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarRecepcion(lineaId, cantidadRecibida: 0m, ocurridoEn: DateTimeOffset.UtcNow));

        Assert.Equal("RECEPCION_CANTIDAD_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData(EstadoRequisicion.Borrador)]
    [InlineData(EstadoRequisicion.EnAutorizacion)]
    [InlineData(EstadoRequisicion.Autorizada)]
    [InlineData(EstadoRequisicion.Cerrada)]
    [InlineData(EstadoRequisicion.Cancelada)]
    [InlineData(EstadoRequisicion.Rechazada)]
    [InlineData(EstadoRequisicion.Eliminada)]
    [InlineData(EstadoRequisicion.CerradaSinSurtir)]
    [InlineData(EstadoRequisicion.CerradaSurtidaParcial)]
    public void RegistrarRecepcion_DesdeEstadoNoPermitido_Lanza(EstadoRequisicion estadoForzado)
    {
        var rq = CrearValida();
        rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA",
                        Millet.SharedKernel.Domain.Money.Mxn(15m));
        var prop = typeof(Requisicion).GetProperty(nameof(Requisicion.Estado))!;
        prop.SetValue(rq, estadoForzado);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarRecepcion(rq.Lineas.Single().Id, 1m, DateTimeOffset.UtcNow));

        Assert.Equal("RECEPCION_SOLO_DESDE_ENSURTIDO", ex.Code);
    }

    [Fact]
    public void RegistrarRecepcion_LineaInexistente_Lanza()
    {
        var rq = CrearEnSurtido();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarRecepcion(Guid.CreateVersion7(), 1m, DateTimeOffset.UtcNow));

        Assert.Equal("LINEA_NO_ENCONTRADA", ex.Code);
    }

    [Fact]
    public void RegistrarRecepcion_VariasLineas_AcumulaPeroNoCierra()
    {
        // ADR-0043 #3: ni siquiera la recepción total de todas las líneas cierra
        // la RQ; queda en EnSurtido hasta que se entregue (cierre por entrega).
        var rq = CrearAutorizada(10m, 20m);
        var lineas = rq.Lineas.ToList();
        // Línea 0: 5 almacén + 5 compra; Línea 1: 0 almacén + 20 compra.
        rq.RegistrarCubrimiento(
            new[]
            {
                new CubrimientoLinea(lineas[0].Id, 5m, 5m),
                new CubrimientoLinea(lineas[1].Id, 0m, 20m),
            },
            DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);

        var ev1 = rq.RegistrarRecepcion(lineas[0].Id, 5m, DateTimeOffset.UtcNow);
        Assert.Null(ev1);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);

        // Recepción total de la última línea: cubre todo, pero YA NO cierra.
        var ev2 = rq.RegistrarRecepcion(lineas[1].Id, 20m, DateTimeOffset.UtcNow);
        Assert.Null(ev2);
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
    }

    // --- ADR-0043: registrar entrega al solicitante (canal Almacén→Compras) ---

    [Fact]
    public void RegistrarEntrega_Parcial_AcumulaCantidadEntregada_SinCerrar()
    {
        var rq = CrearEnSurtido(); // 4 almacén + 6 compra, original 10
        var lineaId = rq.Lineas.Single().Id;

        var r = rq.RegistrarEntrega(lineaId, cantidadEntregada: 3m, ocurridoEn: DateTimeOffset.UtcNow);

        Assert.Equal(3m, rq.Lineas.Single().CantidadEntregada);
        Assert.Null(r.CierreEvento);                  // 3 < 10
        Assert.False(r.ExcedioTecho);                 // 3 <= techo (4)
        Assert.Equal(3m, r.TotalEntregado);
        Assert.Equal(4m, r.TechoDisponible);          // almacén 4 + recibida 0
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);
    }

    [Fact]
    public void RegistrarEntrega_Acumulativa_Suma()
    {
        var rq = CrearEnSurtido();
        var lineaId = rq.Lineas.Single().Id;

        rq.RegistrarEntrega(lineaId, 2m, DateTimeOffset.UtcNow);
        var r = rq.RegistrarEntrega(lineaId, 1m, DateTimeOffset.UtcNow);

        Assert.Equal(3m, rq.Lineas.Single().CantidadEntregada);
        Assert.Equal(3m, r.TotalEntregado);
    }

    [Fact]
    public void RegistrarEntrega_TodoEntregado_DesdeEnSurtido_Cierra_Y_DevuelveEvento()
    {
        // Mundo post-PR#3 simulado: la RQ tiene todo el material disponible
        // (4 almacén + 6 recibida = techo 10) pero sigue EnSurtido porque el
        // cierre viejo se forzó a NO haber disparado. Aquí el cierre nuevo
        // (por entrega total) sí transiciona.
        var rq = CrearEnSurtido();          // 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarRecepcion(lineaId, 6m, DateTimeOffset.UtcNow); // recibida=6 → cierre viejo → Cerrada
        ForzarEstado(rq, EstadoRequisicion.EnSurtido);            // simula PR#3 (recepción ya no cierra)
        var ahora = DateTimeOffset.UtcNow;

        var r = rq.RegistrarEntrega(lineaId, cantidadEntregada: 10m, ocurridoEn: ahora);

        Assert.Equal(EstadoRequisicion.Cerrada, rq.Estado);
        Assert.NotNull(r.CierreEvento);
        Assert.Equal(rq.Id, r.CierreEvento!.RequisicionId);
        Assert.Equal(ahora, r.CierreEvento.OcurridoEn);
        Assert.Equal(10m, rq.Lineas.Single().CantidadEntregada);
    }

    [Fact]
    public void RegistrarEntrega_SobreRqCerradaManualmente_Acumula_NoLanza_NoTransiciona()
    {
        // ADR-0043 #3: tras la conmutación, una RQ terminal ya no viene del
        // cierre viejo (eliminado) sino del CIERRE MANUAL (#2). Caso diseñado:
        // material en vuelo (recibido) que llega DESPUÉS de un cierre manual con
        // OC en vuelo. La entrega debe seguir tolerando la RQ terminal: acumula,
        // NO truena, NO transiciona (garantía conservada del cierre dormido #1).
        var rq = CrearEnSurtido();          // 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarRecepcion(lineaId, 6m, DateTimeOffset.UtcNow); // recibida=6, sigue EnSurtido (post-#3)
        rq.CerrarManual(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, rq.Estado); // terminal por cierre manual

        var r = rq.RegistrarEntrega(lineaId, cantidadEntregada: 4m, ocurridoEn: DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, rq.Estado); // no transiciona
        Assert.Null(r.CierreEvento);
        Assert.Equal(4m, rq.Lineas.Single().CantidadEntregada);      // acumuló
        Assert.False(r.ExcedioTecho);                                // 4 <= techo (4+6)
    }

    [Fact]
    public void RegistrarEntrega_ExcedeTecho_Acumula_SinLanzar_SenialaExcedio()
    {
        // Robusto: entregar más de lo físicamente disponible (techo=4) NO
        // lanza; acumula y señala ExcedioTecho para que el handler advierta.
        var rq = CrearEnSurtido();          // techo = 4 almacén + 0 recibida
        var lineaId = rq.Lineas.Single().Id;

        var r = rq.RegistrarEntrega(lineaId, cantidadEntregada: 5m, ocurridoEn: DateTimeOffset.UtcNow);

        Assert.True(r.ExcedioTecho);
        Assert.Equal(4m, r.TechoDisponible);
        Assert.Equal(5m, r.TotalEntregado);
        Assert.Equal(5m, rq.Lineas.Single().CantidadEntregada); // acumuló pese a exceder
    }

    [Fact]
    public void RegistrarEntrega_CantidadInvalida_Lanza()
    {
        var rq = CrearEnSurtido();
        var lineaId = rq.Lineas.Single().Id;

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarEntrega(lineaId, cantidadEntregada: 0m, ocurridoEn: DateTimeOffset.UtcNow));

        Assert.Equal("ENTREGA_CANTIDAD_INVALIDA", ex.Code);
        Assert.Equal(0m, rq.Lineas.Single().CantidadEntregada);
    }

    [Fact]
    public void RegistrarEntrega_LineaInexistente_Lanza()
    {
        var rq = CrearEnSurtido();

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.RegistrarEntrega(Guid.CreateVersion7(), 1m, DateTimeOffset.UtcNow));

        Assert.Equal("LINEA_NO_ENCONTRADA", ex.Code);
    }

    // --- ADR-0043: cierre manual (jefe de almacén / almacenista) ---

    [Fact]
    public void CerrarManual_SinEntregas_TransicionaA_CerradaSinSurtir()
    {
        var rq = CrearEnSurtido(); // EnSurtido, nada entregado
        var motivoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        var evento = rq.CerrarManual(motivoId, actorId, ahora, "ya no se requiere");

        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, rq.Estado);
        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, evento.EstadoFinal);
        Assert.Equal(rq.Id, evento.RequisicionId);
        Assert.Equal(motivoId, evento.MotivoId);
        Assert.Equal("ya no se requiere", evento.MotivoTexto);
        Assert.Equal(actorId, evento.ActorId);
        Assert.Equal(ahora, evento.OcurridoEn);
    }

    [Fact]
    public void CerrarManual_ConEntregaParcial_TransicionaA_CerradaSurtidaParcial()
    {
        var rq = CrearEnSurtido(); // 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarEntrega(lineaId, 2m, DateTimeOffset.UtcNow); // algo entregado, sigue EnSurtido
        Assert.Equal(EstadoRequisicion.EnSurtido, rq.Estado);

        var evento = rq.CerrarManual(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.CerradaSurtidaParcial, rq.Estado);
        Assert.Equal(EstadoRequisicion.CerradaSurtidaParcial, evento.EstadoFinal);
    }

    [Fact]
    public void CerrarManual_DesdeAutorizada_TransicionaA_CerradaSinSurtir()
    {
        // Autorizada sin cubrimiento → entregado = 0 → CerradaSinSurtir.
        var rq = CrearAutorizada(10m);

        var evento = rq.CerrarManual(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);

        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, rq.Estado);
        Assert.Equal(EstadoRequisicion.CerradaSinSurtir, evento.EstadoFinal);
    }

    [Fact]
    public void CerrarManual_LlenaCamposTerminacion()
    {
        var rq = CrearEnSurtido();
        var motivoId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var ahora = DateTimeOffset.UtcNow;

        rq.CerrarManual(motivoId, actorId, ahora, "presupuesto recortado");

        Assert.Equal(motivoId, rq.MotivoTerminacionId);
        Assert.Equal("presupuesto recortado", rq.MotivoTerminacionTexto);
        Assert.Equal(actorId, rq.ActorTerminacionId);
        Assert.Equal(ahora, rq.FechaTerminacion);
    }

    [Theory]
    [InlineData(EstadoRequisicion.Borrador)]
    [InlineData(EstadoRequisicion.EnAutorizacion)]
    [InlineData(EstadoRequisicion.Cerrada)]
    [InlineData(EstadoRequisicion.Cancelada)]
    [InlineData(EstadoRequisicion.Rechazada)]
    [InlineData(EstadoRequisicion.Eliminada)]
    [InlineData(EstadoRequisicion.CerradaSinSurtir)]
    [InlineData(EstadoRequisicion.CerradaSurtidaParcial)]
    public void CerrarManual_DesdeEstadoNoPermitido_Lanza(EstadoRequisicion estadoForzado)
    {
        var rq = CrearValida();
        rq.AgregarLinea(Guid.CreateVersion7(), Guid.CreateVersion7(), 10m, "PZA",
                        Millet.SharedKernel.Domain.Money.Mxn(15m));
        ForzarEstado(rq, estadoForzado);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            rq.CerrarManual(Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));

        Assert.Equal("CIERRE_MANUAL_SOLO_DESDE_AUTORIZADA_O_ENSURTIDO", ex.Code);
    }

    // --- ADR-0043 #3: pendiente de entregar (fórmula única del dominio) ---

    [Fact]
    public void CantidadPendienteEntregar_IncluyeRecibido_Y_DescuentaEntregado()
    {
        // pendiente = (CantidadDeAlmacen + CantidadRecibida) − CantidadEntregada.
        var rq = CrearEnSurtido(); // 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarRecepcion(lineaId, 6m, DateTimeOffset.UtcNow); // recibida=6 → disponible 4+6=10

        Assert.Equal(10m, rq.Lineas.Single().CantidadPendienteEntregar);

        rq.RegistrarEntrega(lineaId, 3m, DateTimeOffset.UtcNow);   // entregado=3
        Assert.Equal(7m, rq.Lineas.Single().CantidadPendienteEntregar);
    }

    [Fact]
    public void CantidadPendienteEntregar_Linea100Compra_Recibida_EsSurtible()
    {
        // El bug que arregla #3: una línea 100% compra (CantDeAlmacen=0) recibida
        // antes daba pendiente 0 (inentregable). Ahora suma lo recibido → surtible.
        var rq = CrearAutorizada(10m);
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarCubrimiento(
            new[] { new CubrimientoLinea(lineaId, CantidadDeAlmacen: 0m, CantidadDeCompra: 10m) },
            DateTimeOffset.UtcNow);
        rq.RegistrarRecepcion(lineaId, 10m, DateTimeOffset.UtcNow); // recibida=10

        Assert.Equal(10m, rq.Lineas.Single().CantidadPendienteEntregar);
    }

    [Fact]
    public void CantidadPendienteEntregar_NoNegativa_SiSeEntregoDeMas()
    {
        var rq = CrearEnSurtido(); // 4 almacén + 6 compra
        var lineaId = rq.Lineas.Single().Id;
        rq.RegistrarRecepcion(lineaId, 6m, DateTimeOffset.UtcNow); // disponible 10
        rq.RegistrarEntrega(lineaId, 12m, DateTimeOffset.UtcNow);  // entrega de más (robusto)

        Assert.Equal(0m, rq.Lineas.Single().CantidadPendienteEntregar); // piso en 0
    }

    private static void ForzarEstado(Requisicion rq, EstadoRequisicion estado) =>
        typeof(Requisicion).GetProperty(nameof(Requisicion.Estado))!.SetValue(rq, estado);
}
