using Millet.Contabilidad.Application;
using Millet.Contabilidad.Application.Importacion;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application.Exceptions;
using static Millet.Contabilidad.UnitTests.Fixtures.FixturesCatalogo;

namespace Millet.Contabilidad.UnitTests;

public sealed class P9CatalogoTests
{
    private static FormatoCatalogo Formato()
    {
        var o = new CatalogoOpciones();
        o.AplicarDefaults();
        return new(o);
    }

    [Theory]
    [InlineData(true, OrigenMovimiento.Manual, false)]
    [InlineData(false, OrigenMovimiento.Manual, true)]
    [InlineData(true, OrigenMovimiento.AuxiliarCxC, true)]
    public void Marca_rechaza_solo_el_origen_manual(bool marcada, OrigenMovimiento origen, bool valida)
    {
        var cuenta = new CuentaContable(Guid.NewGuid(), "FIX-1.1", "FIX", Guid.NewGuid(), 2,
            NaturalezaCuenta.Deudora, TipoCuenta.Afectable, CuentaControl.Ninguna, null, null, noAfectableManual: marcada);
        var r = CuentaContableReadAdapter.Validar(cuenta, origen, Formato());
        Assert.Equal(valida, r.Valida);
        Assert.Equal(valida ? null : "CUENTA_NO_AFECTABLE_MANUAL", r.Codigo);
    }

    [Theory]
    [InlineData("Sí", true, true)]
    [InlineData("No", false, true)]
    [InlineData("true", true, true)]
    [InlineData("0", false, true)]
    [InlineData("quizá", false, false)]
    public void Importacion_columna_opcional_valida_booleanos(string valor, bool marca, bool valida)
    {
        var r = Request($"codigo;nombre;no_afectable_manual\nFIX-1;FIX;{valor}");
        var a = new ImportadorCatalogo(Formato()).Analizar(LectorTabla.Leer(r), "FIX", ExistenteCatalogo.Vacio);
        Assert.Equal(valida, a.PuedeAplicar);
        Assert.Equal(marca, a.Filas.Single().NoAfectableManual);
    }

    [Fact]
    public void Lote_marcado_respeta_padres_por_segmentos_sin_relajar_la_validacion()
    {
        var invalido = Analizar("codigo;nombre;naturaleza;no_afectable_manual\n"
            + "FIX-1;FIX raíz;Deudora;No\nFIX-1.1;FIX banco;Deudora;Sí\nFIX-1.2;FIX gasto;Deudora;No\n");
        Assert.False(invalido.PuedeAplicar);
        Assert.Contains(invalido.Hallazgos, e => e.Codigo == "CONTAB_IMPORT_PADRE_INEXISTENTE");

        var valido = Analizar("codigo;nombre;naturaleza;no_afectable_manual\n"
            + "FIX-100.00.00.00;FIX raíz;Deudora;No\n"
            + "FIX-100.10.00.00;FIX banco;Deudora;Sí\n"
            + "FIX-100.20.00.00;FIX gasto;Deudora;No\n");
        Assert.True(valido.PuedeAplicar);
        Assert.All(valido.Filas, f => Assert.Equal(Accion.Crear, f.Accion));
        Assert.Equal([1, 2, 2], valido.Filas.Select(f => f.Nivel).ToArray());
        Assert.Equal([false, true, false], valido.Filas.Select(f => f.NoAfectableManual).ToArray());
        Assert.Equal("FIX-100.00.00.00", valido.Filas[1].PadreCodigo);
    }

    [Fact]
    public void Importar_sin_columna_conserva_marca_y_cambiarla_modifica_la_huella()
    {
        var existente = new CuentaExistente(Guid.NewGuid(), "FIX-1", "FIX", null, NaturalezaCuenta.Deudora,
            TipoCuenta.Titulo, CuentaControl.Ninguna, null, null, true, false, NoAfectableManual: true);
        var importador = new ImportadorCatalogo(Formato());
        var sin = importador.Analizar(LectorTabla.Leer(Request("codigo;nombre\nFIX-1;FIX")), "FIX", new([existente], ExistenteCatalogo.Vacio.OrigenACodigo));
        Assert.True(sin.Filas.Single().NoAfectableManual);
        var no = importador.Analizar(LectorTabla.Leer(Request("codigo;nombre;no_afectable_manual\nFIX-1;FIX;No")), "FIX", new([existente], ExistenteCatalogo.Vacio.OrigenACodigo));
        Assert.False(no.Filas.Single().NoAfectableManual);
        Assert.Equal(Accion.Actualizar, no.Filas.Single().Accion);
        Assert.NotEqual(sin.Huella, no.Huella);
    }

    [Fact]
    public void Autor_no_puede_autorizar_y_solicitud_sigue_pendiente()
    {
        var autor = Guid.NewGuid();
        var s = Nueva(autor);
        var e = Assert.Throws<BusinessRuleException>(() => s.Resolver(autor, "FIX admin", DateTimeOffset.UtcNow, true, null));
        Assert.Equal("CONTAB_SOLICITUD_MISMO_AUTOR", e.Code);
        Assert.Equal("Pendiente", s.Estado);
    }

    [Fact]
    public void Rechazo_exige_motivo_y_conserva_actor_fecha_y_motivo()
    {
        var s = Nueva(Guid.NewGuid());
        var daf = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;
        Assert.Throws<BusinessRuleException>(() => s.Resolver(daf, "FIX DAF", ahora, false, " "));
        s.Resolver(daf, "FIX DAF", ahora, false, "  Corregir naturaleza  ");
        Assert.Equal("Rechazada", s.Estado);
        Assert.Equal("Corregir naturaleza", s.MotivoRechazo);
        Assert.Equal(daf, s.ResueltaPorId);
        Assert.Equal(ahora, s.ResueltaEn);
        Assert.Throws<BusinessRuleException>(() => s.Resolver(daf, "FIX", ahora, true, null));
    }

    [Fact]
    public void DAF_distinto_autoriza_una_sola_vez()
    {
        var s = Nueva(Guid.NewGuid());
        s.Resolver(Guid.NewGuid(), "FIX DAF", DateTimeOffset.UtcNow, true, null);
        Assert.Equal("Autorizada", s.Estado);
        Assert.Null(s.MotivoRechazo);
        Assert.Throws<BusinessRuleException>(() => s.Resolver(Guid.NewGuid(), "FIX", DateTimeOffset.UtcNow, true, null));
    }

    private static SolicitudCatalogo Nueva(Guid autor) => new(Guid.NewGuid(), "Cambio", "{}", "FIX", "[]", autor, "FIX Contador", DateTimeOffset.UtcNow);
}
