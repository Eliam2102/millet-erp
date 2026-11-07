using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.SharedKernel.UnitTests.Domain;

/// <summary>U-4 (86e3yzemw): incidencia común con responsable, estado, historial y paso para retomar.</summary>
public class IncidenciaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid Responsable = Guid.CreateVersion7();
    private static readonly Guid Otro = Guid.CreateVersion7();

    private static Incidencia.Datos Datos(string paso = PasosIncidencia.AlmRecepcionDiferenciaOc, string motivo = "Llegaron 12 piezas, la OC pide 10",
        string? ruta = "/almacen/recepciones/x", string? detalleClave = null) =>
        new(Guid.CreateVersion7(), "almacen", "recepcion", Guid.CreateVersion7(), "REC-000123", Guid.CreateVersion7(), paso,
            "RECEPCION_EXCEDE_PENDIENTE", motivo, null, detalleClave, Responsable, "Jefe de almacén", new DateOnly(2026, 11, 7),
            PasosIncidencia.AlmRecepcionRegistrar, ruta);

    [Fact]
    public void Abrir_por_el_sistema_queda_abierta_automatica_y_con_historial()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);

        i.Estado.Should().Be(EstadoIncidencia.Abierta);
        i.Pendiente.Should().BeTrue();
        i.Automatica.Should().BeTrue();
        i.AbiertaPorNombre.Should().Be(Incidencia.ActorSistema);
        i.DetalleClave.Should().BeEmpty();
        i.PasoReanudacion.Should().Be(PasosIncidencia.AlmRecepcionRegistrar);
        i.Historial.Should().ContainSingle(m => m.Accion == "Abierta" && m.EstadoNuevo == EstadoIncidencia.Abierta && m.EstadoAnterior == null);
    }

    [Fact]
    public void Abrir_por_un_usuario_guarda_quien_la_abrio()
    {
        var actor = Guid.CreateVersion7();
        var i = Incidencia.Abrir(Datos(), actor, "Recepcionista", Ahora);

        i.Automatica.Should().BeFalse();
        i.AbiertaPorId.Should().Be(actor);
        i.AbiertaPorNombre.Should().Be("Recepcionista");
    }

    [Theory]
    [InlineData("Alm.Recepcion")]
    [InlineData("alm recepcion")]
    [InlineData("")]
    public void Abrir_rechaza_claves_de_paso_invalidas(string paso)
    {
        var act = () => Incidencia.Abrir(Datos(paso: paso), null, null, Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("INCIDENCIA_CLAVE_INVALIDA");
    }

    [Fact]
    public void Abrir_exige_motivo_de_al_menos_cinco_caracteres()
    {
        var act = () => Incidencia.Abrir(Datos(motivo: "mal"), null, null, Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("INCIDENCIA_MOTIVO_REQUERIDO");
    }

    [Fact]
    public void Abrir_exige_ruta_que_empiece_con_diagonal()
    {
        var act = () => Incidencia.Abrir(Datos(ruta: "almacen/recepciones"), null, null, Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("INCIDENCIA_RUTA_INVALIDA");
    }

    [Fact]
    public void Ciclo_atender_y_resolver_deja_quien_resolvio_y_el_historial_en_orden()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);

        i.Atender(Responsable, "Jefe de almacén", "Reviso con el proveedor", Ahora.AddHours(1));
        i.Resolver(Responsable, "Jefe de almacén", "Se recibieron 10 y se devolvieron 2", Ahora.AddHours(2));

        i.Estado.Should().Be(EstadoIncidencia.Resuelta);
        i.Pendiente.Should().BeFalse();
        i.ResueltaPorId.Should().Be(Responsable);
        i.Resolucion.Should().Be("Se recibieron 10 y se devolvieron 2");
        i.Historial.Select(m => m.Accion).Should().Equal("Abierta", "Atendida", "Resuelta");
        i.Historial.Select(m => m.Orden).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Resolver_por_el_sistema_al_retomar_el_paso()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);

        i.Resolver(null, null, "Se registró la recepción del pendiente", Ahora.AddDays(1));

        i.ResueltaPorNombre.Should().Be(Incidencia.ActorSistema);
        i.ResueltaPorId.Should().BeNull();
    }

    [Fact]
    public void Atender_solo_desde_abierta()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);
        i.Atender(Responsable, "Jefe", null, Ahora);

        var act = () => i.Atender(Responsable, "Jefe", null, Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("INCIDENCIA_TRANSICION_INVALIDA");
    }

    [Fact]
    public void Una_incidencia_cerrada_no_se_resuelve_cancela_ni_reasigna()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);
        i.Cancelar(Otro, "Supervisor", "Se registró por error", Ahora);

        i.Estado.Should().Be(EstadoIncidencia.Cancelada);
        new Action[]
        {
            () => i.Resolver(Responsable, "Jefe", "Resuelta igual", Ahora),
            () => i.Cancelar(Otro, "Supervisor", "Otra vez", Ahora),
            () => i.Reasignar(Otro, "Otro", Responsable, "Jefe", null, Ahora),
        }.Should().AllSatisfy(a => a.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("INCIDENCIA_TRANSICION_INVALIDA"));
    }

    [Fact]
    public void Resolver_y_cancelar_exigen_texto()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);

        ((Action)(() => i.Resolver(Responsable, "Jefe", " ", Ahora))).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("INCIDENCIA_RESOLUCION_REQUERIDA");
        ((Action)(() => i.Cancelar(Responsable, "Jefe", "no", Ahora))).Should().Throw<BusinessRuleException>()
            .Which.Code.Should().Be("INCIDENCIA_MOTIVO_REQUERIDO");
    }

    [Fact]
    public void Reasignar_cambia_el_responsable_y_lo_anota_sin_cambiar_estado()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);

        i.Reasignar(Otro, "Comprador", Responsable, "Jefe de almacén", "Le toca a compras", Ahora);

        i.ResponsableUsuarioId.Should().Be(Otro);
        i.ResponsableNombre.Should().Be("Comprador");
        i.Estado.Should().Be(EstadoIncidencia.Abierta);
        i.Historial.Last().Accion.Should().Be("Reasignada");
        i.Historial.Last().Nota.Should().Contain("De Jefe de almacén a Comprador").And.Contain("Le toca a compras");
    }

    [Fact]
    public void Reasignar_al_mismo_responsable_se_rechaza()
    {
        var i = Incidencia.Abrir(Datos(), null, null, Ahora);
        var act = () => i.Reasignar(Responsable, "Jefe", Responsable, "Jefe", null, Ahora);
        act.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("INCIDENCIA_RESPONSABLE_INVALIDO");
    }

    [Fact]
    public void Catalogo_de_pasos_da_nombre_y_conserva_claves_desconocidas()
    {
        PasosIncidencia.Nombre(PasosIncidencia.AlmValeRectificar).Should().Be("Rectificar vale y su requisición");
        PasosIncidencia.Nombre("modulo.paso-nuevo").Should().Be("modulo.paso-nuevo");
        PasosIncidencia.Todos.Select(p => p.Clave).Should().OnlyHaveUniqueItems();
    }
}
