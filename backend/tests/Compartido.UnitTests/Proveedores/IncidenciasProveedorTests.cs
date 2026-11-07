using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Application.Abstractions;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.Application.Incidencias;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Incidencias;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Domain.Incidencias;

namespace Millet.Compartido.UnitTests.Proveedores;

/// <summary>
/// U-4 (86e3yzemw) · pasos de proveedor de la oleada 1 conectados a la pieza común de incidencias, con el servicio y el puerto
/// reales sobre la misma base en memoria: documento observado (ADM-proveedor-09) y cambio de cuenta bancaria rechazado
/// (ADM-cuentas-07). Datos FICTICIOS.
/// </summary>
public sealed class IncidenciasProveedorTests
{
    private const string ClabeValida = "002010077777777771";

    private sealed class FakeUsuarios : IIncidenciaUsuariosReadPort
    {
        public Dictionary<Guid, string> Nombres { get; } = [];
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerNombresAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(Nombres.Where(n => ids.Contains(n.Key)).ToDictionary());
    }

    private sealed class SinSucursales : IUsuarioSucursalReadPort
    {
        public Task<IReadOnlyList<Guid>> ListarIdsAsync(Guid u, CancellationToken ct) => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> EstaAsociadoAsync(Guid u, Guid s, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class Entorno : IDisposable
    {
        public AdjuntosEntorno E { get; } = new();
        public FakeUsuarios Usuarios { get; } = new();
        public IncidenciasAdapter Puerto { get; }
        public Proveedor Proveedor { get; } =
            new(Guid.NewGuid(), "FIX-PROV", "FIX Proveedor SA", "FIX010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        public AdjuntoTipoDocumento TipoActa { get; } = new(Guid.NewGuid(), "proveedor", "acta_constitutiva", "Acta constitutiva", 1, true, null, false);
        public AdjuntoTipoDocumento TipoEdoCuenta { get; } = new(Guid.NewGuid(), "proveedor",
            ValidarCuentaBancariaProveedorHandler.TipoDocumentoSoporte, "Estado de cuenta o carta bancaria", 6, false, 3, false);
        /// <summary>Quien sube documentos y captura cuentas (alta de proveedores).</summary>
        public Guid Captura { get; } = Guid.NewGuid();
        /// <summary>Quien revisa y rechaza (usuario actual).</summary>
        public Guid Revisor { get; }

        public Entorno()
        {
            Revisor = E.Usuario.UserId!.Value;
            Usuarios.Nombres[Captura] = "Alta de proveedores";
            Usuarios.Nombres[Revisor] = "Revisor de CxP";
            var servicio = new IncidenciasServicio(E.Db, E.Usuario, E.Permisos, E.Empresa, new SinSucursales(), Usuarios, E.Clock);
            Puerto = new IncidenciasAdapter(E.Db, servicio, E.Usuario);
            E.Db.Proveedores.Add(Proveedor);
            E.Db.AdjuntoTiposDocumento.AddRange(TipoActa, TipoEdoCuenta);
            E.Db.SaveChanges();
        }

        public async Task<Adjunto> SubirAsync(AdjuntoTipoDocumento tipo, string nombre, Guid? subidoPor = null, DateOnly? vigenteHasta = null)
        {
            var a = new Adjunto(Guid.NewGuid(), "proveedor", Proveedor.Id, null, tipo.Id, nombre, "application/pdf", 10, new string('a', 64),
                $"proveedor/{Proveedor.Id}/{nombre}", vigenteHasta, subidoPor ?? Captura, E.Clock.UtcNow);
            E.Db.Adjuntos.Add(a);
            await E.Db.SaveChangesAsync();
            return a;
        }

        public Task<Millet.Compartido.Application.Adjuntos.AdjuntoResponse> Revisar(Adjunto a, ResultadoRevisionAdjunto r, string? obs = null) =>
            new RevisarDocumentoProveedorHandler(E.Db, E.Usuario, E.Empresa, E.Clock, E.Audit, Puerto)
                .Handle(new(Proveedor.Id, a.Id, r, obs), default);

        /// <summary>La captura el alta de proveedores; valida o rechaza otra persona (segregación).</summary>
        public async Task<CuentaBancariaProveedorDto> CapturarCuentaAsync(string clabe = ClabeValida, string? numeroCuenta = null,
            string? swift = null, string moneda = "MXN", string banco = "BBVA")
        {
            var revisor = E.Usuario.UserId;
            E.Usuario.UserId = Captura;
            var c = await new AgregarCuentaBancariaProveedorHandler(E.Db, E.Usuario, E.Empresa, E.Clock, E.Audit)
                .Handle(new(Proveedor.Id, banco, string.IsNullOrEmpty(clabe) ? null : clabe, numeroCuenta, swift, moneda, "FIX Proveedor SA"), default);
            E.Usuario.UserId = revisor;
            return c;
        }

        public Task<CuentaBancariaProveedorDto> Rechazar(CuentaBancariaProveedorDto c, string motivo = "FIX la CLABE no coincide con la carta") =>
            new CambiarCuentaBancariaProveedorHandler(E.Db, E.Usuario, E.Empresa, E.Audit, E.Clock, Puerto)
                .Handle(new(Proveedor.Id, c.Id, c.Version, AccionCuentaBancariaProveedor.Rechazar, motivo), default);

        public Task<List<Incidencia>> IncidenciasAsync() => E.Db.Incidencias.AsNoTracking().Include(i => i.Historial).ToListAsync();

        public Task<List<Adjunto>> EvidenciaAsync(Guid incidenciaId) =>
            E.Db.Adjuntos.AsNoTracking().Where(a => a.TipoEntidad == "incidencia" && a.EntidadId == incidenciaId).ToListAsync();

        public void Dispose() => E.Dispose();
    }

    [Fact]
    public async Task P2_el_documento_observado_abre_su_incidencia_con_quien_lo_subio_evidencia_y_paso_para_retomar()
    {
        using var e = new Entorno();
        var acta = await e.SubirAsync(e.TipoActa, "acta.pdf");

        await e.Revisar(acta, ResultadoRevisionAdjunto.Rechazado, "FIX la razón social no coincide con la constancia");

        var i = (await e.IncidenciasAsync()).Should().ContainSingle().Subject;
        i.Paso.Should().Be(PasosIncidencia.AdmProveedorDocumentoObservado);
        i.Modulo.Should().Be("datos_maestros");
        i.TipoDocumento.Should().Be("proveedor");
        i.DocumentoId.Should().Be(e.Proveedor.Id);
        i.DocumentoEtiqueta.Should().Be("FIX-PROV · FIX Proveedor SA");
        i.ResponsableUsuarioId.Should().Be(e.Captura, "quien subió el documento consigue el correcto");
        i.Motivo.Should().Be("FIX la razón social no coincide con la constancia");
        i.CodigoMotivo.Should().Be(IncidenciasProveedor.CodigoDocumentoObservado);
        i.DetalleClave.Should().Be(e.TipoActa.Id.ToString());
        i.PasoReanudacion.Should().Be(PasosIncidencia.AdmProveedorExpediente);
        i.RutaReanudacion.Should().Be($"/admin/datos-maestros/proveedores/{e.Proveedor.Id}");
        i.Estado.Should().Be(EstadoIncidencia.Abierta);
        var evidencia = (await e.EvidenciaAsync(i.Id)).Should().ContainSingle().Subject;
        evidencia.NombreArchivo.Should().Be("acta.pdf");
        evidencia.BlobRef.Should().Be(acta.BlobRef, "apunta al mismo archivo, sin volver a subirlo");
    }

    [Fact]
    public async Task P2_al_aceptar_un_documento_del_mismo_tipo_la_incidencia_se_resuelve_sola_y_otro_tipo_no_la_toca()
    {
        using var e = new Entorno();
        var acta = await e.SubirAsync(e.TipoActa, "acta.pdf");
        await e.Revisar(acta, ResultadoRevisionAdjunto.Rechazado, "FIX acta ilegible");
        var otroTipo = await e.SubirAsync(e.TipoEdoCuenta, "edo.pdf", vigenteHasta: DateOnly.FromDateTime(e.E.Clock.UtcNow.UtcDateTime).AddMonths(2));

        await e.Revisar(otroTipo, ResultadoRevisionAdjunto.Aceptado);
        (await e.IncidenciasAsync()).Single().Estado.Should().Be(EstadoIncidencia.Abierta);

        var nueva = await e.SubirAsync(e.TipoActa, "acta-correcta.pdf");
        await e.Revisar(nueva, ResultadoRevisionAdjunto.Aceptado);

        var i = (await e.IncidenciasAsync()).Single();
        i.Estado.Should().Be(EstadoIncidencia.Resuelta);
        i.Resolucion.Should().Contain("Acta constitutiva");
    }

    [Fact]
    public async Task P2_el_mismo_tipo_observado_dos_veces_no_duplica_la_incidencia_pendiente()
    {
        using var e = new Entorno();
        await e.Revisar(await e.SubirAsync(e.TipoActa, "acta.pdf"), ResultadoRevisionAdjunto.Rechazado, "FIX acta ilegible");
        await e.Revisar(await e.SubirAsync(e.TipoActa, "acta-2.pdf"), ResultadoRevisionAdjunto.Rechazado, "FIX sigue ilegible");

        (await e.IncidenciasAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task P2_si_quien_subio_ya_no_existe_el_responsable_es_quien_lo_reviso()
    {
        using var e = new Entorno();
        var acta = await e.SubirAsync(e.TipoActa, "acta.pdf", subidoPor: Guid.NewGuid());

        await e.Revisar(acta, ResultadoRevisionAdjunto.Rechazado, "FIX acta de otro proveedor");

        (await e.IncidenciasAsync()).Single().ResponsableUsuarioId.Should().Be(e.Revisor);
    }

    [Fact]
    public async Task P3_la_cuenta_rechazada_abre_la_aclaracion_con_quien_la_capturo_y_el_estado_de_cuenta_como_evidencia()
    {
        using var e = new Entorno();
        var hoy = DateOnly.FromDateTime(e.E.Clock.UtcNow.UtcDateTime);
        await e.SubirAsync(e.TipoEdoCuenta, "edo-viejo.pdf", vigenteHasta: hoy.AddDays(-1));
        var vigente = await e.SubirAsync(e.TipoEdoCuenta, "edo-octubre.pdf", vigenteHasta: hoy.AddMonths(2));
        var cuenta = await e.CapturarCuentaAsync();

        await e.Rechazar(cuenta);

        var i = (await e.IncidenciasAsync()).Should().ContainSingle().Subject;
        i.Paso.Should().Be(PasosIncidencia.AdmProveedorCuentaBancaria);
        i.ResponsableUsuarioId.Should().Be(e.Captura);
        i.Motivo.Should().Be("FIX la CLABE no coincide con la carta");
        i.CodigoMotivo.Should().Be(IncidenciasProveedor.CodigoCuentaRechazada);
        i.DetalleClave.Should().Be("MXN");
        i.Detalle.Should().NotContain(ClabeValida, "la cuenta va enmascarada");
        i.PasoReanudacion.Should().Be(PasosIncidencia.AdmProveedorCuentas);
        i.RutaReanudacion.Should().Be($"/admin/datos-maestros/proveedores/{e.Proveedor.Id}");
        (await e.EvidenciaAsync(i.Id)).Should().ContainSingle().Which.BlobRef.Should().Be(vigente.BlobRef);
    }

    [Fact]
    public async Task P3_al_validar_una_cuenta_de_la_misma_moneda_se_resuelve_sola_y_la_de_otra_moneda_no()
    {
        using var e = new Entorno();
        var hoy = DateOnly.FromDateTime(e.E.Clock.UtcNow.UtcDateTime);
        var edo = await e.SubirAsync(e.TipoEdoCuenta, "edo.pdf", vigenteHasta: hoy.AddMonths(2));
        await e.Rechazar(await e.CapturarCuentaAsync());

        var usd = await e.CapturarCuentaAsync(clabe: "", numeroCuenta: "1234567890", swift: "BNMXMXMM", moneda: "USD", banco: "Banamex");
        await new ValidarCuentaBancariaProveedorHandler(e.E.Db, e.E.Usuario, e.E.Empresa, e.E.Clock, e.E.Audit, e.Puerto)
            .Handle(new(e.Proveedor.Id, usd.Id, usd.Version, edo.Id, true, null), default);
        (await e.IncidenciasAsync()).Single().Estado.Should().Be(EstadoIncidencia.Abierta, "la cuenta en dólares no aclara la de pesos");

        var correcta = await e.CapturarCuentaAsync(clabe: "012180001234567899");
        await new ValidarCuentaBancariaProveedorHandler(e.E.Db, e.E.Usuario, e.E.Empresa, e.E.Clock, e.E.Audit, e.Puerto)
            .Handle(new(e.Proveedor.Id, correcta.Id, correcta.Version, edo.Id, true, null), default);

        var i = (await e.IncidenciasAsync()).Single();
        i.Estado.Should().Be(EstadoIncidencia.Resuelta);
        i.Resolucion.Should().Contain("MXN").And.NotContain("012180001234567899");
    }

    [Fact]
    public async Task P3_inhabilitar_una_cuenta_no_abre_incidencia()
    {
        using var e = new Entorno();
        var c = await e.CapturarCuentaAsync();

        await new CambiarCuentaBancariaProveedorHandler(e.E.Db, e.E.Usuario, e.E.Empresa, e.E.Audit, e.E.Clock, e.Puerto)
            .Handle(new(e.Proveedor.Id, c.Id, c.Version, AccionCuentaBancariaProveedor.Inhabilitar, "FIX el proveedor la cerró"), default);

        (await e.IncidenciasAsync()).Should().BeEmpty();
    }
}
