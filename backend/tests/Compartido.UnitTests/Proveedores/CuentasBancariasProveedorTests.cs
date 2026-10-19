using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;

namespace Millet.Compartido.UnitTests.Proveedores;

/// <summary>86e3pdmee · ADM-02: varias cuentas, validación con estado de cuenta y segregación.</summary>
public sealed class CuentasBancariasProveedorTests
{
    private const string ClabeValida = "002010077777777771";

    private static async Task<(AdjuntosEntorno E, Guid ProveedorId, Guid TipoId)> EntornoAsync()
    {
        var e = new AdjuntosEntorno();
        var p = new Proveedor(Guid.NewGuid(), "FIX-PROV", "FIX Proveedor SA", "FIX010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        var tipo = new AdjuntoTipoDocumento(Guid.NewGuid(), "proveedor", ValidarCuentaBancariaProveedorHandler.TipoDocumentoSoporte,
            "Estado de cuenta o carta bancaria", 6, false, 3, false);
        e.Db.Proveedores.Add(p);
        e.Db.AdjuntoTiposDocumento.Add(tipo);
        await e.Db.SaveChangesAsync();
        return (e, p.Id, tipo.Id);
    }

    private static async Task<Guid> AdjuntarAsync(AdjuntosEntorno e, Guid proveedorId, Guid tipoId, DateOnly vigenteHasta)
    {
        var a = new Adjunto(Guid.NewGuid(), "proveedor", proveedorId, null, tipoId, "edo.pdf", "application/pdf", 10,
            new string('a', 64), $"proveedor/{proveedorId}/edo.pdf", vigenteHasta, Guid.NewGuid(), e.Clock.UtcNow);
        e.Db.Adjuntos.Add(a);
        await e.Db.SaveChangesAsync();
        return a.Id;
    }

    private static Task<CuentaBancariaProveedorDto> AgregarAsync(AdjuntosEntorno e, Guid proveedorId, string clabe = ClabeValida, string moneda = "MXN") =>
        new AgregarCuentaBancariaProveedorHandler(e.Db, e.Usuario, e.Empresa, e.Clock, e.Audit)
            .Handle(new(proveedorId, "BBVA", clabe, null, null, moneda, "FIX Proveedor SA"), default);

    private static Task<CuentaBancariaProveedorDto> ValidarAsync(AdjuntosEntorno e, Guid proveedorId, CuentaBancariaProveedorDto c, Guid documento, bool cotejo = true) =>
        new ValidarCuentaBancariaProveedorHandler(e.Db, e.Usuario, e.Empresa, e.Clock, e.Audit)
            .Handle(new(proveedorId, c.Id, c.Version, documento, cotejo, null), default);

    [Fact]
    public async Task Cuenta_nueva_queda_por_validar_y_el_duplicado_se_rechaza()
    {
        var (e, prov, _) = await EntornoAsync();
        using var _e = e;
        var c = await AgregarAsync(e, prov);
        c.Estado.Should().Be(EstadoCuentaBancariaProveedor.PorValidar);
        e.Audit.Registros.Should().ContainSingle(r => r.Operacion == "proveedor.cuenta-agregada" && !r.Resumen.Contains(ClabeValida));

        var otra = () => AgregarAsync(e, prov);
        (await otra.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("PROVEEDOR_CUENTA_DUPLICADA");
    }

    [Fact]
    public async Task Clabe_repetida_en_otro_proveedor_no_bloquea_pero_advierte()
    {
        var (e, prov, _) = await EntornoAsync();
        using var _e = e;
        var otro = new Proveedor(Guid.NewGuid(), "FIX-OTRO", "FIX Otro SA", "FOT010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        e.Db.Proveedores.Add(otro);
        await e.Db.SaveChangesAsync();

        var primera = await AgregarAsync(e, otro.Id);
        primera.Advertencia.Should().BeNull();

        var segunda = await AgregarAsync(e, prov);
        segunda.Estado.Should().Be(EstadoCuentaBancariaProveedor.PorValidar);
        segunda.Advertencia.Should().Contain("FIX-OTRO");

        // Una cuenta rechazada del otro proveedor ya no cuenta como repetida.
        e.Usuario.UserName = "Admin";
        await new CambiarCuentaBancariaProveedorHandler(e.Db, e.Usuario, e.Empresa, e.Audit, e.Clock)
            .Handle(new(otro.Id, primera.Id, primera.Version, AccionCuentaBancariaProveedor.Rechazar, "FIX captura equivocada"), default);
        var rechazada = await e.Db.CuentasBancariasProveedor.SingleAsync(c => c.Id == primera.Id);
        rechazada.UltimoCambioPorNombre.Should().Be("Admin");
        rechazada.UltimoCambioEn.Should().NotBeNull();
    }

    [Fact]
    public async Task Clabe_con_digito_verificador_invalido_se_rechaza()
    {
        var (e, prov, _) = await EntornoAsync();
        using var _e = e;
        var mala = () => AgregarAsync(e, prov, "002010077777777770");
        (await mala.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("CLABE_DIGITO_CONTROL");
    }

    [Fact]
    public async Task Quien_capturo_no_valida_y_sin_documento_vigente_no_se_valida()
    {
        var (e, prov, tipo) = await EntornoAsync();
        using var _e = e;
        var c = await AgregarAsync(e, prov);
        var hoy = DateOnly.FromDateTime(e.Clock.UtcNow.UtcDateTime);
        var vencido = await AdjuntarAsync(e, prov, tipo, hoy.AddDays(-1));
        var vigente = await AdjuntarAsync(e, prov, tipo, hoy.AddMonths(3));

        var mismo = () => ValidarAsync(e, prov, c, vigente);
        (await mismo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PROVEEDOR_CUENTA_MISMA_PERSONA");

        e.Usuario.UserId = Guid.NewGuid();
        e.Usuario.UserName = "Admin Valida";
        var conVencido = () => ValidarAsync(e, prov, c, vencido);
        (await conVencido.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PROVEEDOR_CUENTA_DOCUMENTO_VENCIDO");
        var sinCotejo = () => ValidarAsync(e, prov, c, vigente, cotejo: false);
        (await sinCotejo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PROVEEDOR_CUENTA_COTEJO_INCOMPLETO");

        var validada = await ValidarAsync(e, prov, c, vigente);
        validada.Estado.Should().Be(EstadoCuentaBancariaProveedor.Activa);
        validada.Predeterminada.Should().BeTrue("es la primera cuenta activa en MXN");
        validada.ValidadaPorNombre.Should().Be("Admin Valida");
    }

    [Fact]
    public async Task Inhabilitar_y_reactivar_conservan_la_validacion()
    {
        var (e, prov, tipo) = await EntornoAsync();
        using var _e = e;
        var c = await AgregarAsync(e, prov);
        e.Usuario.UserId = Guid.NewGuid();
        var doc = await AdjuntarAsync(e, prov, tipo, DateOnly.FromDateTime(e.Clock.UtcNow.UtcDateTime).AddMonths(3));
        c = await ValidarAsync(e, prov, c, doc);

        var cambiar = new CambiarCuentaBancariaProveedorHandler(e.Db, e.Usuario, e.Empresa, e.Audit, e.Clock);
        var sinMotivo = () => cambiar.Handle(new(prov, c.Id, c.Version, AccionCuentaBancariaProveedor.Inhabilitar, "x"), default);
        (await sinMotivo.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be("PROVEEDOR_CUENTA_MOTIVO_INVALIDO");

        e.Usuario.UserName = "Admin Inhabilita";
        c = await cambiar.Handle(new(prov, c.Id, c.Version, AccionCuentaBancariaProveedor.Inhabilitar, "FIX cambio de banco"), default);
        c.Estado.Should().Be(EstadoCuentaBancariaProveedor.Inhabilitada);
        c.Predeterminada.Should().BeFalse();
        // Historial: quién y cuándo (la UI lo muestra con el motivo).
        c.UltimoCambioPorNombre.Should().Be("Admin Inhabilita");
        c.UltimoCambioEn.Should().Be(e.Clock.UtcNow);
        c.MotivoEstado.Should().Be("FIX cambio de banco");

        c = await cambiar.Handle(new(prov, c.Id, c.Version, AccionCuentaBancariaProveedor.Reactivar, "FIX error al inhabilitar"), default);
        c.Estado.Should().Be(EstadoCuentaBancariaProveedor.Activa);
    }

    [Fact]
    public async Task Listado_enmascara_sin_permiso()
    {
        var (e, prov, _) = await EntornoAsync();
        using var _e = e;
        await AgregarAsync(e, prov);
        var lista = await new ListarCuentasBancariasProveedorHandler(e.Db, e.Usuario).Handle(new(prov, false), default);
        lista.Single().Numero.Should().Be("**************7771");
        lista.Single().CapturadaPorMi.Should().BeTrue();
    }
}

/// <summary>86e3pdmee · ADM-01: origen, régimen y retención esperada del proveedor.</summary>
public sealed class ClasificacionFiscalProveedorTests
{
    private static Proveedor Nuevo() => new(Guid.NewGuid(), "FIX-CF", "FIX Proveedor SA", "FIX010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);

    [Fact]
    public void Heredado_queda_por_revisar_y_nacional_exige_regimen()
    {
        var p = Nuevo();
        p.Origen.Should().BeNull();
        var sinRegimen = () => p.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, null, null);
        sinRegimen.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("PROVEEDOR_REGIMEN_REQUERIDO");
        p.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", " sin_retencion ");
        p.RegimenFiscalCodigo.Should().Be("601");
        p.ConceptoRetencion.Should().Be("SIN_RETENCION");
    }

    [Fact]
    public void Extranjero_no_guarda_regimen()
    {
        var p = Nuevo();
        p.ActualizarClasificacionFiscal(OrigenProveedor.Extranjero, "601", null);
        p.RegimenFiscalCodigo.Should().BeNull();
    }
}
