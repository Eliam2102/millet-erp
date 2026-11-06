using Millet.ComercioExterior.Domain;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.ComercioExterior.UnitTests;
public sealed class P19AnticiposTests
{
    private static AnticipoAgente Nuevo()
    {
        var e = new Expediente(Guid.NewGuid(), Guid.NewGuid(), "CE-P19", TipoExpediente.Importacion,
            Guid.NewGuid(), "P19", Guid.NewGuid(), Guid.NewGuid(), "MXN", Guid.NewGuid(), []);
        return e.RegistrarAnticipo(Guid.NewGuid(), Guid.NewGuid(), "PED", new(50000, 0, 0), new(2026, 10, 1), null);
    }
    [Fact]
    public void Solicitud_exige_soporte_y_pago_proviene_del_banco()
    {
        var a = Nuevo();
        Assert.Throws<BusinessRuleException>(() => a.Solicitar(Guid.Empty, 50000));
        a.Solicitar(Guid.NewGuid(), 50000);
        Assert.Equal("Solicitado", a.Estado);
        Assert.Throws<BusinessRuleException>(() => a.Comprobar(new(47000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2)));
        var pago = Guid.NewGuid(); a.RegistrarPago(pago, 50000, "BANCO-REAL");
        a.RegistrarPago(pago, 50000, "BANCO-REAL");
        Assert.Equal("Pagado", a.Estado); Assert.Equal("BANCO-REAL", a.ReferenciaPago);
    }
    [Fact]
    public void Devolucion_no_salda_hasta_deposito_y_reentrega_no_duplica()
    {
        var a = Nuevo();
        a.Comprobar(new(47000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2), Guid.NewGuid());
        var devolucion = a.AprobarDevolucion(3000, new(2026, 10, 3), "Devolver", Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(3000, a.Saldo);
        Assert.Throws<BusinessRuleException>(() => a.Corregir(new(46000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 3), Guid.NewGuid(), "Motivo", []));
        a.ConfirmarDevolucion(devolucion, 3000, "DEPOSITO-REAL"); a.ConfirmarDevolucion(devolucion, 3000, "DEPOSITO-REAL");
        Assert.Equal(0, a.Saldo); Assert.Equal("Saldado", a.Estado); Assert.Single(a.Resoluciones);
        Assert.Throws<BusinessRuleException>(() => a.Corregir(new(46000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 3), Guid.NewGuid(), "Motivo", []));
    }
    [Fact]
    public void Comprobador_no_aprueba_y_correccion_conserva_anterior()
    {
        var a = Nuevo(); var usuario = Guid.NewGuid();
        a.Comprobar(new(47000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 2), usuario);
        Assert.Equal(3000, a.Saldo);
        Assert.Throws<BusinessRuleException>(() => a.Resolver("devolucion", 3000, new(2026, 10, 3), "Devolver", Guid.NewGuid(), usuario));
        a.Corregir(new(46000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026, 10, 3), usuario, "Corrección de impuestos", []);
        Assert.Equal(47000, a.HistorialComprobaciones.Single().Impuestos.Total);
        Assert.Equal(4000, a.Saldo);
    }
    [Fact]
    public void Reversa_es_idempotente_y_solo_sin_comprobacion()
    {
        var a = Nuevo(); a.Solicitar(Guid.NewGuid(), 50000);
        var pago = Guid.NewGuid(); a.RegistrarPago(pago, 50000, "TRF-123");
        a.RevertirPago(pago, 50000, "Pago equivocado"); a.RevertirPago(pago, 50000, "Pago equivocado");
        Assert.Equal(0, a.PagadoMxn); Assert.Equal("Autorizado", a.Estado);
        var segundo = Guid.NewGuid(); a.RegistrarPago(segundo, 50000, "TRF-124");
        a.Comprobar(new(47000, 0, 0), Guid.NewGuid(), Guid.NewGuid(), new(2026,10,2), Guid.NewGuid());
        Assert.Throws<BusinessRuleException>(() => a.RevertirPago(segundo, 50000, "Pago equivocado"));
    }
    [Fact]
    public void Solicitud_cancelada_no_admite_pago_y_exige_motivo()
    {
        var a = Nuevo(); a.Solicitar(Guid.NewGuid(), 50000);
        Assert.Throws<BusinessRuleException>(() => a.Cancelar(" "));
        a.Cancelar("Solicitud duplicada"); Assert.Equal("Cancelado", a.Estado);
        Assert.Throws<BusinessRuleException>(() => a.RegistrarPago(Guid.NewGuid(), 50000, "TRF-123"));
    }

}
