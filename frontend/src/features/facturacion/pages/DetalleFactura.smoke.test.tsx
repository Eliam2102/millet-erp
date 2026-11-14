import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleFactura } from '@/features/facturacion/pages/DetalleFactura';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));
// F1-CXC-05 P2: el selector real consulta el estado de cuenta; aquí basta con elegir un anticipo.
vi.mock('@/features/facturacion/components/AnticipoPicker', () => ({
  AnticipoPicker: ({ onChange }: { onChange: (a: unknown) => void }) => (
    <button
      type="button"
      onClick={() => onChange({ anticipoId: 'a-7', folio: 'FANT-7', saldo: 500, estado: 'Abierto' })}
    >
      Elegir anticipo FANT-7
    </button>
  ),
}));

// FAC-08b: el panel real monta el formulario de emisión completo; aquí basta con saber que se abre.
vi.mock('@/features/facturacion/components/SustituirFacturaPanel', () => ({
  SustituirFacturaPanel: ({ facturaId }: { facturaId: string }) => <p>Panel sustituir {facturaId}</p>,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params }: { children: React.ReactNode; to: string; params?: { id: string } }) => (
    <a href={params ? to.replace('$id', params.id) : to}>{children}</a>
  ),
  useParams: () => ({ id: 'f-1' }),
  useSearch: () => ({}),
}));

beforeEach(() => {
  mswServer.use(http.get('*/api/v1/contabilidad/polizas/por-documento', () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 10 })));
});

const detalle = {
  id: 'f-1',
  tipo: 'Ingreso',
  folio: 'A-1',
  estado: 'Timbrado',
  uuid: '11111111-2222-3333-4444-555555555555',
  receptorRfc: 'XAXX010101000',
  receptorNombre: 'Público en general',
  moneda: 'MXN',
  subtotal: 100,
  descuento: 0,
  impuestosTrasladados: 16,
  retenciones: 0,
  total: 116,
  fechaTimbrado: '2026-05-30T10:00:00Z',
  version: 2,
  lineas: [
    {
      posicion: 1,
      claveProdServSat: '01010101',
      descripcion: 'Vidrio templado 6mm',
      claveUnidadSat: 'H87',
      cantidad: 1,
      valorUnitario: 100,
      descuento: 0,
      importe: 100,
    },
  ],
  relaciones: [
    {
      tipoRelacion: '07',
      uuidRelacionado: 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE',
      folio: 'FANT-9',
      tipoComprobante: 'I',
      total: 50,
      fechaTimbrado: '2026-05-29T10:00:00Z',
    },
  ],
  // [Decisión 13-K]
  totalAcreditado: 0,
  totalPorCobrar: 116,
  notasCreditoAplicadas: [],
};

describe('<DetalleFactura> — smoke', () => {
  it.each([
    { pesoKg: null, areaPie2: null, totalKg: 0, totalPie2: 0, kg: '—', area: '—' },
    { pesoKg: 1234.5, areaPie2: 2500.25, totalKg: 1234.5, totalPie2: 2500.25, kg: '1,234.5', area: '2,500.25' },
    { pesoKg: 0, areaPie2: 0, totalKg: 0, totalPie2: 0, kg: '0', area: '0' },
  ])('formatea totales físicos y distingue medidas ausentes: $kg', async (datos) => {
    mswServer.use(http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json({
      ...detalle, totalKg: datos.totalKg, totalPie2: datos.totalPie2,
      lineas: [{ ...detalle.lineas[0], pesoKg: datos.pesoKg, areaPie2: datos.areaPie2 }],
    })));
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await screen.findByText('A-1');
    expect(screen.getByText('Total de kilos').nextElementSibling).toHaveTextContent(datos.kg);
    expect(screen.getByText('Total de pies²').nextElementSibling).toHaveTextContent(datos.area);
  });

  // M20 (prueba S3-4): el XML existe desde el timbre; se descarga también con la cancelación en proceso y ya cancelada.
  it.each([
    { estado: 'Timbrado', habilitado: true },
    { estado: 'CancelacionPendiente', habilitado: true },
    { estado: 'Cancelado', habilitado: true },
    { estado: 'Borrador', habilitado: false },
    { estado: 'TimbradoFallido', habilitado: false },
  ])('botón XML con estado $estado: habilitado = $habilitado', async ({ estado, habilitado }) => {
    mswServer.use(http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json({ ...detalle, estado })));
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await screen.findByText('A-1');
    const xml = screen.getByRole('button', { name: /^XML$/i });
    if (habilitado) expect(xml).toBeEnabled();
    else expect(xml).toBeDisabled();
  });

  it.each(['CLI-001', 'CLI-002', null])('A21 conserva nombre histórico y muestra clave %s al abrir factura', async (clienteClave) => {
    mswServer.use(http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json({ ...detalle, clienteClave })));
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await screen.findByText('A-1');
    expect(screen.getAllByText(clienteClave ?? 'Sin código')[0]).toBeVisible();
    expect(screen.getAllByText(/Público en general/).length).toBeGreaterThan(0);
  });

  it('renderiza folio, conceptos, cadena de relaciones y bitácora de envío', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json(detalle)),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () =>
        HttpResponse.json([
          {
            id: 'e-1',
            destinatario: 'cliente@demo.com',
            estado: 'Enviado',
            intentos: 1,
            enviadoAt: '2026-05-30T12:00:00Z',
            ultimoError: null,
          },
        ]),
      ),
      http.get('*/api/v1/facturacion/comprobantes/f-1/cancelar', () =>
        HttpResponse.json({
          comprobanteId: 'f-1',
          estadoComprobante: 'Timbrado',
          solicitudId: null,
          estadoSolicitud: null,
          motivoSat: null,
          estatusSat: null,
          mensajeError: null,
          solicitadaEn: null,
          resueltaEn: null,
        }),
      ),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    expect(screen.getByText('Vidrio templado 6mm')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /Partidas \(1\)/i })).toBeInTheDocument();
    expect(screen.getByText(/Cadena de relaciones CFDI \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Aplicación de anticipo/i)).toBeInTheDocument();
    expect(await screen.findByText('cliente@demo.com')).toBeInTheDocument();
    // Acciones de descarga presentes.
    expect(screen.getByRole('button', { name: /^XML$/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Imprimir/i })).toBeInTheDocument();
  });

  it('muestra NC aplicadas y el monto por cobrar ([Decisión 13-K])', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle,
          relaciones: [],
          totalAcreditado: 50,
          totalPorCobrar: 66,
          notasCreditoAplicadas: [
            {
              id: 'nc-1',
              folio: 'NC-3',
              motivo: 'Amortizacion',
              total: 50,
              uuid: 'BBBBBBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF',
              fechaTimbrado: '2026-05-30T11:00:00Z',
            },
          ],
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    expect(screen.getByText(/Notas de crédito aplicadas \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText('NC-3')).toBeInTheDocument();
    expect(screen.getByText(/Aplicación de anticipo/i)).toBeInTheDocument();
    expect(screen.getByText('Por cobrar')).toBeInTheDocument();
    expect(screen.getByText('$66.00')).toBeInTheDocument();
  });

  it('columna Reembolso: botón en NC de bonificación y «Solicitado» si ya se pidió', async () => {
    const nc = {
      id: 'nc-9',
      folio: 'NC-9',
      motivo: 'Bonificacion',
      total: 20,
      uuid: null,
      fechaTimbrado: '2026-05-30T11:00:00Z',
    };
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle,
          relaciones: [],
          notasCreditoAplicadas: [
            nc,
            { ...nc, id: 'nc-10', folio: 'NC-10', reembolsoSolicitado: true },
          ],
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('NC-9')).toBeInTheDocument());
    expect(screen.getAllByRole('button', { name: 'Solicitar reembolso' })).toHaveLength(1);
    expect(screen.getByText('Solicitado')).toBeInTheDocument();
  });

  it.each([
    {
      estadoSolicitud: 'PendienteAutorizacion',
      motivoRechazo: null,
      decididaPor: null,
      textos: [/Pendiente de autorización/, /No se envía al SAT hasta que el Contador General/],
      retirar: true,
    },
    {
      estadoSolicitud: 'RechazadaPorAutorizador',
      motivoRechazo: 'Se corrige con nota de crédito',
      decididaPor: 'Contador demo',
      textos: [/No autorizada/, /Motivo: Se corrige con nota de crédito\. La factura sigue vigente\./, /No la autorizó: Contador demo/],
      retirar: false,
    },
  ])('F6.5: muestra la solicitud $estadoSolicitud', async (caso) => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json(detalle)),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
      http.get('*/api/v1/facturacion/comprobantes/f-1/cancelar', () =>
        HttpResponse.json({
          comprobanteId: 'f-1',
          estadoComprobante: 'Timbrado',
          solicitudId: 's-1',
          estadoSolicitud: caso.estadoSolicitud,
          motivoSat: '02',
          estatusSat: null,
          mensajeError: null,
          solicitadaEn: '2026-10-31T10:00:00Z',
          resueltaEn: null,
          uuidSustituto: null,
          solicitadaPor: 'Ingresos demo',
          decididaPor: caso.decididaPor,
          decididaEn: null,
          motivoRechazo: caso.motivoRechazo,
        }),
      ),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    for (const texto of caso.textos) expect(await screen.findByText(texto)).toBeInTheDocument();
    expect(screen.getByText('Solicitó: Ingresos demo')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Retirar solicitud' }) != null).toBe(caso.retirar);
    // Con una solicitud pendiente no se ofrece pedir otra.
    expect(screen.queryByRole('button', { name: /^Cancelar$/ }) == null).toBe(caso.retirar);
  });

  it('F6.3: muestra el estado del reembolso que reporta Tesorería y «Pagado» con fecha y referencia', async () => {
    const nc = {
      id: 'nc-20',
      folio: 'NC-20',
      motivo: 'Bonificacion',
      total: 50,
      uuid: null,
      fechaTimbrado: '2026-10-30T11:00:00Z',
      reembolsoSolicitado: true,
    };
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle,
          relaciones: [],
          notasCreditoAplicadas: [
            { ...nc, estadoReembolso: 'Pagado', reembolsoPagadoEl: '2026-10-31', reembolsoReferencia: 'SPEI 987' },
            { ...nc, id: 'nc-21', folio: 'NC-21', estadoReembolso: 'Rechazado', reembolsoMotivoResolucion: 'Cuenta no coincide' },
            { ...nc, id: 'nc-22', folio: 'NC-22', estadoReembolso: 'Autorizado' },
          ],
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('NC-20')).toBeInTheDocument());
    const pagada = screen.getByTestId('reembolso-nc-NC-20');
    expect(pagada).toHaveTextContent('Pagado');
    expect(pagada).toHaveTextContent('SPEI 987');
    expect(pagada).toHaveTextContent(/31 oct 2026/i);
    expect(screen.getByTestId('reembolso-nc-NC-21')).toHaveTextContent('RechazadoCuenta no coincide');
    expect(screen.getByTestId('reembolso-nc-NC-22')).toHaveTextContent('Autorizado');
    expect(screen.queryByRole('button', { name: 'Solicitar reembolso' })).not.toBeInTheDocument();
  });

  it('F6.3: la NC de bonificación con «Devolver dinero al cliente» envía la cuenta destino', async () => {
    let cuerpo: unknown = null;
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json({ ...detalle, relaciones: [] })),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
      http.post('*/api/v1/facturacion/notas-credito/bonificacion', async ({ request }) => {
        cuerpo = await request.json();
        return HttpResponse.json({ id: 'nc-30', estado: 'Timbrado', uuid: 'U', folio: 'NC-30', total: 10 }, { status: 201 });
      }),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /Nota de crédito/i }));
    fireEvent.change(screen.getAllByRole('spinbutton')[0], { target: { value: '10' } });
    fireEvent.click(screen.getByRole('checkbox', { name: /Devolver dinero al cliente/i }));
    fireEvent.change(screen.getByLabelText(/Banco/), { target: { value: 'BBVA' } });
    fireEvent.change(screen.getByLabelText(/CLABE/), { target: { value: '0321-8000 0118 359719' } });
    fireEvent.change(screen.getByLabelText(/Titular/), { target: { value: 'Cliente SA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Emitir NC' }));

    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo).toMatchObject({
      reembolso: { banco: 'BBVA', clabe: '032180000118359719', titular: 'Cliente SA' },
    });
  });

  it('P53: la NC de cancelación va por el saldo pendiente completo con tipo Cancelacion', async () => {
    let cuerpo: unknown = null;
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({ ...detalle, relaciones: [], totalPorCobrar: 80 }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
      http.post('*/api/v1/facturacion/notas-credito/bonificacion', async ({ request }) => {
        cuerpo = await request.json();
        return HttpResponse.json({ id: 'nc-31', estado: 'Timbrado', uuid: 'U', folio: 'NC-31', total: 80 }, { status: 201 });
      }),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: /Nota de crédito/i }));
    fireEvent.change(screen.getByLabelText(/Tipo/), { target: { value: 'Cancelacion' } });
    expect(screen.getByLabelText(/Monto total/)).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Emitir NC' }));

    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo).toMatchObject({ tipo: 'Cancelacion', montoTotal: 80 });
  });

  it('F1-CXC-05 P2: aplica un anticipo del cliente a la factura timbrada con tope en lo que falta por cobrar', async () => {
    let peticion: { url: string; cuerpo: unknown } | null = null;
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({ ...detalle, relaciones: [], clienteId: 'c-1', totalPorCobrar: 300 }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
      http.post('*/api/v1/facturacion/anticipos/:id/aplicar', async ({ request }) => {
        peticion = { url: request.url, cuerpo: await request.json() };
        return HttpResponse.json({
          notaCreditoId: 'nc-40', folio: 'NC-40', uuid: 'U', importe: 300, saldoAnticipo: 200, saldoFactura: 0,
        });
      }),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: /Aplicar anticipo/i }));
    fireEvent.click(screen.getByRole('button', { name: 'Elegir anticipo FANT-7' }));
    // Propone el menor entre el saldo del anticipo (500) y lo que falta por cobrar (300).
    expect(screen.getByLabelText(/Importe/)).toHaveValue(300);
    fireEvent.click(screen.getAllByRole('button', { name: 'Aplicar anticipo' }).at(-1)!);

    await waitFor(() => expect(peticion).not.toBeNull());
    expect(peticion!.url).toContain('/anticipos/a-7/aplicar');
    expect(peticion!.cuerpo).toEqual({ facturaVentaId: 'f-1', importe: 300 });
  });

  it('F1-CXC-05 P2: sin saldo por cobrar no ofrece aplicar anticipo', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({ ...detalle, relaciones: [], clienteId: 'c-1', totalPorCobrar: 0 }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('A-1')).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /Aplicar anticipo/i })).not.toBeInTheDocument();
  });

  it('estado error cuando la factura no existe', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'No encontrada', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/No se pudo cargar la factura/i)).toBeInTheDocument(),
    );
  });
  it('M-216: la factura retenida por pedimento dice qué falta para liberarla', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle, estado: 'PendientePedimento', uuid: null, fechaTimbrado: null, relaciones: [],
          motivoPedimento: 'El expediente EXP-25-0007 aún no tiene pedimento capturado.',
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    expect(await screen.findByTestId('motivo-pedimento'))
      .toHaveTextContent('El expediente EXP-25-0007 aún no tiene pedimento capturado.');
    expect(screen.getByRole('button', { name: /Aplicar pedimento y timbrar/ })).toBeInTheDocument();
  });

  it('FAC-08b: «Sustituir» abre el borrador de la sustituta en una factura vigente', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json({ ...detalle, relaciones: [] })),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /Sustituir/ }));
    expect(await screen.findByText('Panel sustituir f-1')).toBeInTheDocument();
  });

  it('FAC-08b: la original muestra «Sustituida por» y ya no ofrece sustituir', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle,
          relaciones: [],
          sustituidaPor: { id: 'f-2', folio: 'A-2', uuid: 'bbbbbbbb-0000-0000-0000-000000000002', estado: 'Timbrado' },
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    const aviso = await screen.findByTestId('aviso-sustitucion');
    expect(aviso).toHaveTextContent('Sustituida por A-2 (relación 04 · vigente).');
    expect(screen.queryByRole('button', { name: /Sustituir/ })).not.toBeInTheDocument();
  });

  it('FAC-08b: la sustituta muestra «Sustituye a» y el aviso de sustitución rechazada', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/facturas/f-1', () =>
        HttpResponse.json({
          ...detalle,
          relaciones: [],
          sustituyeA: { id: 'f-0', folio: 'A-0', uuid: 'aaaaaaaa-0000-0000-0000-000000000000', estado: 'Timbrado' },
          sustitucionRechazadaMotivo: 'El cliente conserva la original',
        }),
      ),
      http.get('*/api/v1/facturacion/facturas/f-1/envios', () => HttpResponse.json([])),
    );
    render(<DetalleFactura />, { wrapper: createQueryWrapper() });
    const aviso = await screen.findByTestId('aviso-sustitucion');
    expect(aviso).toHaveTextContent('Sustituye a A-0 (relación 04 · vigente).');
    expect(aviso).toHaveTextContent('Sustitución rechazada.');
    expect(aviso).toHaveTextContent('«El cliente conserva la original»');
  });
});


it('integra la póliza del cliente mediante su identidad canónica', async () => {
  const consultas: URL[] = [];
  mswServer.use(
    http.get('*/api/v1/facturacion/facturas/f-1', () => HttpResponse.json(detalle)),
    http.get('*/api/v1/contabilidad/polizas/por-documento', ({ request }) => {
      consultas.push(new URL(request.url));
      return HttpResponse.json({ items: [{ id: 'poliza-c-21', folio: 'IN-000021', fecha: '2026-11-12', estado: 'Autorizada', polizaAnuladaId: null, anuladaPorPolizaId: null, polizaRelacionadaId: null, relacion: null }], total: 1, offset: 0, limit: 10 });
    }),
  );
  render(<DetalleFactura />, { wrapper: createQueryWrapper() });
  expect(await screen.findByRole('link', { name: 'IN-000021' })).toHaveAttribute('href', '/contabilidad/polizas/poliza-c-21');
  expect(consultas.length).toBeGreaterThan(0);
  expect(consultas[0].searchParams.get('modulo')).toBe('facturacion');
  expect(consultas[0].searchParams.get('tipo')).toBe('comprobante');
  expect(consultas[0].searchParams.get('documentoId')).toBe('f-1');
});
