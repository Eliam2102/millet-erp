using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ComercioExterior.UnitTests;
public sealed class P20RecepcionTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Capturista = Guid.NewGuid();
    private static readonly DateOnly Viernes = new(2026, 11, 6);
    private static readonly DateTimeOffset Ahora = new(2026, 11, 6, 18, 0, 0, TimeSpan.Zero);
    private static Expediente Crear()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "P20", TipoExpediente.Importacion, Guid.NewGuid(), "Recepción de prueba", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
        var c = Guid.NewGuid(); e.GuardarContenedor(c, new("CONT-P20", null, null, "BL-P20", null, null, null, null, ModalidadTransporte.Maritimo));
        e.AsignarPartidaImportacion(c, Guid.NewGuid(), 1, 10, 20, "PZA", Actor);
        e.AgregarParticipante(Guid.NewGuid(), "Agente ficticio", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(Guid.NewGuid(), "Naviera ficticia", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        e.RegistrarEmbarque(c, Viernes, Viernes);
        return e;
    }
    private static void Recibir(Expediente e, decimal recibida, Guid? evento = null) => e.ConciliarRecepcion(evento ?? Guid.NewGuid(), e.PartidasImportacion.Single().Id, recibida, Viernes, Viernes.AddDays(4), Capturista, Ahora);
    [Theory]
    [InlineData(9, TipoIncidencia.MercanciaFaltante)]
    [InlineData(11, TipoIncidencia.MercanciaSobrante)]
    public void Diferencia_crea_incidencia_con_responsable_y_bloqueo(decimal recibida, TipoIncidencia tipo)
    {
        var e = Crear(); Recibir(e, recibida);
        var i = e.Incidencias.Single();
        i.Automatica.Should().BeTrue(); i.DiferenciaCantidad.Should().Be(1); i.Tipo.Should().Be(tipo);
        i.ResponsableUsuarioId.Should().Be(Actor); i.FechaCompromiso.Should().Be(new DateOnly(2026, 11, 10));
        i.SiguienteAccion.Should().Be("Aclarar la diferencia con el proveedor y el agente aduanal");
        Assert.Throws<BusinessRuleException>(e.ExigirSinIncidencias);
    }
    [Fact] public void Igual_no_crea_incidencia() { var e = Crear(); Recibir(e, 10); e.Incidencias.Should().BeEmpty(); }
    [Fact] public void Parcialidad_actualiza_y_al_cuadrar_cierra()
    {
        var e = Crear(); Recibir(e, 8); var id = e.Incidencias.Single().Id;
        Recibir(e, 9); e.Incidencias.Single().DiferenciaCantidad.Should().Be(1);
        Recibir(e, 10); e.Incidencias.Single().Id.Should().Be(id);
        e.Incidencias.Single().Estado.Should().Be(EstadoIncidencia.Resuelta);
        e.Incidencias.Single().Historial.Last().Motivo.Should().Be("Cuadrada por recepción");
    }
    [Fact] public void Reentrega_no_duplica_ni_actualiza_historial()
    {
        var e = Crear(); var evento = Guid.NewGuid(); Recibir(e, 9, evento); Recibir(e, 9, evento);
        e.Incidencias.Should().ContainSingle(); e.Incidencias.Single().Historial.Should().HaveCount(1);
    }
    [Fact] public void Ajuste_autorizado_recalcula()
    {
        var e = Crear(); Recibir(e, 9); var id = Guid.NewGuid();
        e.AbrirIncidencia(id, new(TipoIncidencia.AjusteCantidades, Viernes, Actor, "Corregir embarcado", PartidaId: e.PartidasImportacion.Single().Id, FechaCompromiso: Viernes, SiguienteAccion: "Autorizar ajuste", CantidadNueva: 9), [], Actor, Ahora, Viernes);
        e.AutorizarAjuste(id, Guid.NewGuid(), Ahora, false, 20);
        e.Incidencias.Single(i => i.Automatica).Estado.Should().Be(EstadoIncidencia.Resuelta);
    }
    [Fact] public void Responsable_edita_plazo_y_accion_con_historial()
    {
        var e = Crear(); Recibir(e, 9); var i = e.Incidencias.Single();
        Assert.Throws<BusinessRuleException>(() => e.EditarSeguimientoIncidencia(i.Id, Viernes.AddDays(7), "Solicitar aclaración", "Nueva fecha acordada", Guid.NewGuid(), Ahora));
        e.EditarSeguimientoIncidencia(i.Id, Viernes.AddDays(7), "Solicitar aclaración", "Nueva fecha acordada", Actor, Ahora);
        i.Historial.Last().FechaCompromisoAnterior.Should().Be(Viernes.AddDays(4));
        i.Historial.Last().SiguienteAccionAnterior.Should().Be("Aclarar la diferencia con el proveedor y el agente aduanal");
    }
    [Theory]
    [InlineData(2, 10)]
    [InlineData(5, 13)]
    public async Task Plazo_usa_dias_habiles_configurados(int dias, int dia)
    {
        var c = new ConfiguracionCe(Guid.NewGuid()); c.CambiarPlazo(dias);
        var fecha = await Millet.ComercioExterior.Application.PlazoRecepcionCe.SumarAsync(new Calendario(), Viernes, c.DiasHabilesIncidenciaRecepcion, default);
        fecha.Should().Be(new DateOnly(2026, 11, dia));
    }
    [Fact] public async Task Festivo_del_calendario_compartido_no_cuenta()
    {
        var fecha = await Millet.ComercioExterior.Application.PlazoRecepcionCe.SumarAsync(new Calendario(new DateOnly(2026, 11, 9)), Viernes, 2, default);
        fecha.Should().Be(new DateOnly(2026, 11, 11));
    }
    [Fact] public void Sin_responsable_heredado_usa_al_capturista_de_recepcion()
    {
        var e = Crear(); typeof(Expediente).GetProperty(nameof(Expediente.ResponsableUsuarioId))!.SetValue(e, Guid.Empty);
        Recibir(e, 9); e.Incidencias.Single().ResponsableUsuarioId.Should().Be(Capturista);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(366)]
    public void Configuracion_rechaza_plazo_invalido(int dias)
    { Assert.Throws<BusinessRuleException>(() => new ConfiguracionCe(Guid.NewGuid()).CambiarPlazo(dias)); }
    [Fact] public void Incidencia_automatica_usa_el_aviso_de_vencimiento_existente()
    {
        var e = Crear(); Recibir(e, 9);
        e.GenerarAvisosIncidencias(Viernes.AddDays(5), Ahora.AddDays(5)).Should().Be(1);
        e.GenerarAvisosIncidencias(Viernes.AddDays(6), Ahora.AddDays(6)).Should().Be(0);
        e.AvisosIncidencias.Single(a => a.EsVencimiento).DestinatarioUsuarioId.Should().Be(Actor);
    }
    [Theory]
    [InlineData(9, 1)]
    [InlineData(10, 0)]
    public void Partida_repartida_compara_suma_y_actualiza_una_incidencia(decimal recibida, int incidencias)
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "P22", TipoExpediente.Importacion, Guid.NewGuid(), "Prueba reparto", Guid.NewGuid(), Guid.NewGuid(), "USD", Actor, []);
        var linea = Guid.NewGuid();
        e.AgregarParticipante(Guid.NewGuid(), "Agente ficticio", TipoParticipante.AgenteAduanal, null, PapelParticipante.Captura, null, null);
        e.AgregarParticipante(Guid.NewGuid(), "Naviera ficticia", TipoParticipante.Naviera, null, PapelParticipante.Captura, null, null);
        foreach (var cantidad in new[] { 6m, 4m })
        {
            var c = Guid.NewGuid(); e.GuardarContenedor(c, new(c.ToString(), null, null, "BL", null, null, null, null, ModalidadTransporte.Maritimo));
            e.AsignarPartidaImportacion(c, linea, 1, cantidad, 10, "PZA", Actor); e.RegistrarEmbarque(c, Viernes, Viernes);
        }
        var evento = Guid.NewGuid(); var partida = e.PartidasImportacion.First().Id;
        e.ConciliarRecepcion(evento, partida, recibida, Viernes, Viernes.AddDays(4), Capturista, Ahora);
        e.ConciliarRecepcion(evento, partida, recibida, Viernes, Viernes.AddDays(4), Capturista, Ahora);
        Assert.Equal(incidencias, e.Incidencias.Count);
        Assert.Equal(recibida, e.PartidasImportacion.Sum(p => p.CantidadRecibida));
        if (incidencias == 0) return;
        Assert.Equal(1m, e.Incidencias.Single().DiferenciaCantidad);
        e.ConciliarRecepcion(Guid.NewGuid(), partida, 10, Viernes, Viernes.AddDays(4), Capturista, Ahora);
        Assert.Equal(EstadoIncidencia.Resuelta, Assert.Single(e.Incidencias).Estado);
        e.ConciliarRecepcion(Guid.NewGuid(), partida, 9, Viernes, Viernes.AddDays(4), Capturista, Ahora);
        Assert.Equal(EstadoIncidencia.Abierta, Assert.Single(e.Incidencias).Estado);
    }
    private sealed class Calendario(DateOnly? festivo = null) : Millet.SharedKernel.Application.Calendario.ICalendarioHabil
    {
        public Task<int> ContarDiasAsync(DateOnly desde, DateOnly hasta, CancellationToken ct) => Task.FromResult(Millet.SharedKernel.Application.Calendario.CalendarioHabil.ContarDias(desde, hasta, festivo is DateOnly f ? new HashSet<DateOnly> { f } : new HashSet<DateOnly>()));
        public Task<DateTimeOffset> SumarHorasAsync(DateOnly fecha, int horas, CancellationToken ct) => throw new NotSupportedException();
    }
}
