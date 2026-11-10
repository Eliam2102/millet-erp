using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Compartido.Application.Adjuntos;
using Millet.Compartido.Application.Catalogos.Proveedores;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.UnitTests.Adjuntos;
using Millet.DatosMaestros.Application.Catalogos;
using Millet.DatosMaestros.Application.Proveedores;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain.Adjuntos;
using Millet.SharedKernel.Infrastructure.Persistence;

namespace Millet.Compartido.UnitTests;

/// <summary>86e3yzemu: Tax ID extranjero sin duplicar, revisión por documento y excepción pendiente.</summary>
public sealed class ProveedorCompletoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static (CompartidoDbContext Db, CoreDbContext Core, FakeEmpresa Empresa) Contextos()
    {
        var empresa = new FakeEmpresa();
        var db = new CompartidoDbContext(new DbContextOptionsBuilder<CompartidoDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var core = new CoreDbContext(new DbContextOptionsBuilder<CoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        return (db, core, empresa);
    }

    private static CrearProveedorCommand Alta(string clave, string rfc, string? taxId) =>
        new(clave, "Glass Components Ltd", rfc, TipoPersonaProveedor.Moral, null, null, null, null, null,
            Origen: OrigenProveedor.Extranjero, Pais: "USA", TaxId: taxId);

    [Fact]
    public async Task Alta_de_extranjero_con_un_Tax_ID_ya_registrado_se_rechaza_sin_importar_mayusculas()
    {
        var (db, _, _) = Contextos();
        await using var _db = db;
        var handler = new CrearProveedorHandler(db);
        await handler.Handle(Alta("P000101", "XEXX010101000", "US-84-1234567"), default);

        var error = await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(Alta("P000102", "XEXX010101000", " us-84-1234567 "), default));
        Assert.Equal("PROVEEDOR_TAXID_DUPLICADO", error.Code);
        Assert.Contains("P000101", error.Message);

        // Otro Tax ID y otro proveedor sin Tax ID sí se dan de alta (el RFC genérico se repite sin problema).
        await handler.Handle(Alta("P000103", "XEXX010101000", "US-84-7654321"), default);
        await handler.Handle(Alta("P000104", "XEXX010101000", null), default);
        Assert.Equal(3, await db.Proveedores.CountAsync());
    }

    [Fact]
    public async Task Editar_un_extranjero_con_el_Tax_ID_de_otro_se_rechaza_pero_conservar_el_propio_no()
    {
        var (db, _, _) = Contextos();
        await using var _db = db;
        var crear = new CrearProveedorHandler(db);
        var a = await crear.Handle(Alta("P000201", "XEXX010101000", "DE-811907980"), default);
        var b = await crear.Handle(Alta("P000202", "XEXX010101000", "DE-999888777"), default);
        var editar = new ActualizarProveedorHandler(db);

        var error = await Assert.ThrowsAsync<ConflictException>(() => editar.Handle(new ActualizarProveedorCommand(b.Id, TaxId: "de-811907980"), default));
        Assert.Equal("PROVEEDOR_TAXID_DUPLICADO", error.Code);
        await editar.Handle(new ActualizarProveedorCommand(a.Id, TaxId: "DE-811907980", Calle: "Industriestrasse 12"), default);
    }

    // La búsqueda por Tax ID usa Translate (solo Postgres): se prueba en pantalla y en CI (integración).

    [Fact]
    public void Revisar_un_documento_guarda_quien_cuando_y_resultado_y_rechazar_exige_observacion()
    {
        var adjunto = new Adjunto(Guid.NewGuid(), "proveedor", Guid.NewGuid(), null, Guid.NewGuid(), "opinion.pdf", "application/pdf", 10,
            new string('a', 64), "blob/1", null, Guid.NewGuid(), Ahora);
        var revisor = Guid.NewGuid();

        Assert.Equal("ADJUNTO_REVISION_OBSERVACION_INVALIDA",
            Assert.Throws<BusinessRuleException>(() => adjunto.Revisar(ResultadoRevisionAdjunto.Rechazado, " ", revisor, "Ana", Ahora)).Code);
        Assert.Null(adjunto.ResultadoRevision);

        adjunto.Revisar(ResultadoRevisionAdjunto.Rechazado, "El RFC no coincide con el del proveedor", revisor, "Ana Revisora", Ahora);
        Assert.Equal((ResultadoRevisionAdjunto.Rechazado, revisor, "Ana Revisora", Ahora), (adjunto.ResultadoRevision!.Value, adjunto.RevisadoPorId!.Value, adjunto.RevisadoPorNombre, adjunto.RevisadoEn!.Value));

        adjunto.Revisar(ResultadoRevisionAdjunto.Aceptado, null, revisor, "Ana Revisora", Ahora.AddHours(1));
        Assert.Equal(ResultadoRevisionAdjunto.Aceptado, adjunto.ResultadoRevision);
        Assert.Null(adjunto.ObservacionRevision);

        adjunto.DarDeBaja("Se subió el archivo equivocado", revisor, Ahora);
        Assert.Equal("ADJUNTO_DADO_DE_BAJA",
            Assert.Throws<BusinessRuleException>(() => adjunto.Revisar(ResultadoRevisionAdjunto.Aceptado, null, revisor, "Ana", Ahora)).Code);
    }

    [Fact]
    public void Excepcion_pendiente_la_resuelve_otra_persona_y_negar_exige_motivo()
    {
        var pide = Guid.NewGuid(); var resuelve = Guid.NewGuid();
        var e = new AdjuntoExcepcion("proveedor", Guid.NewGuid(), Guid.NewGuid(), "Proveedor del extranjero sin contrato formal", pide, "Luis Compras", Ahora);
        Assert.Equal(EstadoExcepcionAdjunto.Pendiente, e.Estado);
        Assert.False(e.Vigente);
        Assert.Equal("ADJUNTO_EXCEPCION_NO_OTORGADA", Assert.Throws<BusinessRuleException>(() => e.Revocar(resuelve, Ahora)).Code);

        Assert.Equal("ADJUNTO_EXCEPCION_MISMO_USUARIO", Assert.Throws<BusinessRuleException>(() => e.Otorgar(pide, "Luis Compras", Ahora)).Code);
        Assert.Equal("ADJUNTO_EXCEPCION_RESOLUCION_INVALIDA", Assert.Throws<BusinessRuleException>(() => e.Negar(resuelve, "Ana CxP", Ahora, "no")).Code);
        Assert.Equal(EstadoExcepcionAdjunto.Pendiente, e.Estado);

        e.Otorgar(resuelve, "Ana CxP", Ahora.AddHours(2));
        Assert.True(e.Vigente);
        Assert.Equal(("Ana CxP", Ahora.AddHours(2)), (e.ResueltaPorNombre, e.ResueltaEn!.Value));
        Assert.Equal("ADJUNTO_EXCEPCION_NO_PENDIENTE", Assert.Throws<BusinessRuleException>(() => e.Negar(resuelve, "Ana CxP", Ahora, "ya se otorgó antes")).Code);

        var n = new AdjuntoExcepcion("proveedor", Guid.NewGuid(), Guid.NewGuid(), "Proveedor del extranjero sin contrato formal", pide, "Luis Compras", Ahora);
        n.Negar(resuelve, "Ana CxP", Ahora, "Sin contrato no se paga a este proveedor");
        Assert.Equal(EstadoExcepcionAdjunto.Negada, n.Estado);
        Assert.False(n.Vigente);
    }

    [Fact]
    public async Task Una_excepcion_pendiente_no_cubre_el_documento_y_la_negada_se_ve_en_el_expediente()
    {
        var (db, _, _) = Contextos();
        await using var _db = db;
        var proveedor = new Proveedor(Guid.NewGuid(), "P000401", "Glass Components Ltd", "XEXX010101000", TipoPersonaProveedor.Moral);
        var tipo = new AdjuntoTipoDocumento(Guid.NewGuid(), "proveedor", "contrato", "Contrato", 1, false, null, false);
        tipo.Configurar("Contrato", true, true, null, false, AdjuntoAplicaOrigen.Todos, true);
        db.Proveedores.Add(proveedor); db.AdjuntoTiposDocumento.Add(tipo);
        var e = new AdjuntoExcepcion("proveedor", proveedor.Id, tipo.Id, "Proveedor del extranjero sin contrato formal", Guid.NewGuid(), "Luis Compras", Ahora);
        db.AdjuntoExcepciones.Add(e); await db.SaveChangesAsync();
        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);

        var pendiente = await ExpedienteAdjuntos.CalcularAsync(db, "proveedor", proveedor.Id, true, hoy, default);
        var doc = Assert.Single(pendiente.Documentos);
        Assert.Equal(EstadoExpedienteDocumento.Faltante, doc.Estado);
        Assert.Equal(EstadoExcepcionAdjunto.Pendiente, doc.Excepcion!.Estado);
        Assert.False(pendiente.Completo);

        e.Negar(Guid.NewGuid(), "Ana CxP", Ahora, "Sin contrato no se paga a este proveedor"); await db.SaveChangesAsync();
        doc = Assert.Single((await ExpedienteAdjuntos.CalcularAsync(db, "proveedor", proveedor.Id, true, hoy, default)).Documentos);
        Assert.Equal((EstadoExpedienteDocumento.Faltante, EstadoExcepcionAdjunto.Negada, "Ana CxP"), (doc.Estado, doc.Excepcion!.Estado, doc.Excepcion.ResueltaPorNombre));
    }

    [Fact]
    public async Task Alta_proveedor_extranjero_hegla_con_tax_id_aleman_normalizado_y_excepcion_otorgada_sin_exigir_rfc_mexicano()
    {
        var (db, _, _) = Contextos();
        await using var _db = db;
        var crear = new CrearProveedorHandler(db);

        // Hegla GmbH & Co. KG con Tax ID alemán y espacios para normalizar
        var heglaCmd = new CrearProveedorCommand(
            Clave: "PRV-HEGLA",
            RazonSocial: "Hegla GmbH & Co. KG",
            Rfc: null, // Sin RFC mexicano obligatorio
            TipoPersona: TipoPersonaProveedor.Moral,
            NombreComercial: "Hegla",
            CondicionesPagoDias: 30,
            MonedaPreferidaId: null,
            Email: "contact@hegla.de",
            Telefono: "+49 5273 905-0",
            Origen: OrigenProveedor.Extranjero,
            Pais: "DEU",
            TaxId: "  DE811124639  ");

        var alta = await crear.Handle(heglaCmd, default);
        var guardado = await db.Proveedores.SingleAsync(p => p.Id == alta.Id);

        Assert.Equal("DE811124639", guardado.TaxId);
        Assert.Equal(Proveedor.RfcGenericoExtranjero, guardado.Rfc);
        Assert.Equal(OrigenProveedor.Extranjero, guardado.Origen);

        // Intento de duplicado de Tax ID con distintas mayúsculas/minúsculas y espacios se rechaza
        var dupCmd = new CrearProveedorCommand(
            Clave: "PRV-HEGLA-2",
            RazonSocial: "Hegla Machinery Ltd",
            Rfc: null,
            TipoPersona: TipoPersonaProveedor.Moral,
            NombreComercial: null,
            CondicionesPagoDias: null,
            MonedaPreferidaId: null,
            Email: null,
            Telefono: null,
            Origen: OrigenProveedor.Extranjero,
            Pais: "DEU",
            TaxId: " de811124639 ");

        var err = await Assert.ThrowsAsync<ConflictException>(() => crear.Handle(dupCmd, default));
        Assert.Equal("PROVEEDOR_TAXID_DUPLICADO", err.Code);

        // Flujo de excepción documental otorgada para extranjero sin RFC mexicano
        var tipo = new AdjuntoTipoDocumento(Guid.NewGuid(), "proveedor", "opinion_sat", "Opinión SAT", 1, false, null, false);
        tipo.Configurar("Opinión SAT", true, true, null, false, AdjuntoAplicaOrigen.SoloExtranjero, true);
        db.AdjuntoTiposDocumento.Add(tipo);

        var excepcion = new AdjuntoExcepcion("proveedor", guardado.Id, tipo.Id,
            "Proveedor extranjero Hegla sin RFC mexicano (opera con Tax ID DE811124639)",
            Guid.NewGuid(), "Guillermo Pantoja", Ahora);
        excepcion.Otorgar(Guid.NewGuid(), "Laura Cerón", Ahora.AddHours(1));
        db.AdjuntoExcepciones.Add(excepcion);
        await db.SaveChangesAsync();

        var hoy = DateOnly.FromDateTime(Ahora.UtcDateTime);
        var expediente = await ExpedienteAdjuntos.CalcularAsync(db, "proveedor", guardado.Id, true, hoy, default);
        Assert.True(expediente.Completo);
        var doc = Assert.Single(expediente.Documentos);
        Assert.Equal(EstadoExpedienteDocumento.Exceptuado, doc.Estado);
    }
}
