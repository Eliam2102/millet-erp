import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmitirFacturaForm } from './EmitirFacturaForm';
import { toast } from 'sonner';
import type { EmitirFacturaPrefill } from './prefill';

const navegar = vi.hoisted(() => vi.fn());
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

// Selectores de catálogo stubbeados: el smoke valida la estructura de
// pestañas del form, no los combobox de catálogos (B16 incluidos).
vi.mock('@/components/erp', () => ({
  SucursalSelector: () => <input aria-label="Sucursal (stub)" />,
  RegimenFiscalSelector: () => <input aria-label="Régimen (stub)" />,
  UsoCfdiSelector: () => <input aria-label="Uso CFDI (stub)" />,
  FormaPagoSelector: () => <input aria-label="Forma de pago (stub)" />,
}));
vi.mock('@/components/erp/selectors/ClaveSatSelector', () => ({
  ClaveSatSelector: ({
    catalogo,
    value,
    onChange,
  }: {
    catalogo: string;
    value?: string;
    onChange: (item: { codigo: string }) => void;
  }) => (
    <input
      aria-label={catalogo}
      value={value ?? ''}
      onChange={(e) => onChange({ codigo: e.target.value })}
    />
  ),
}));
vi.mock('@/components/erp/selectors/MonedaSelector', () => ({
  MonedaSelector: () => <input aria-label="Moneda (stub)" />,
}));
// El CTA "Corregir en catálogo" y el banner de bloqueo usan <Link/>; el
// smoke corre sin RouterProvider (mismo patrón que DetallePedido.smoke).
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => navegar,
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
}));

const EMISOR = {
  rfcEmisor: 'MIL010101ABC',
  razonSocialEmisor: 'Millet SA de CV',
  regimenFiscalEmisor: '601',
  sucursalIdDefault: '33333333-3333-4333-8333-333333333333',
  tasaIvaDefault: null,
  codigoPostalEmisor: '76120',
};

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mswServer.use(
    http.get('*/api/v1/facturacion/emisor-defaults', () => HttpResponse.json(EMISOR)),
    // FAC-ING-PR3: el selector de canal consume el lookup del catálogo.
    http.get('*/api/v1/facturacion/catalogos/canales-venta', () =>
      HttpResponse.json([
        { id: 1, nombre: 'Tienda Cancún' },
        { id: 8, nombre: 'Exportación' },
      ]),
    ),
  );
});
afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<EmitirFacturaForm> — smoke (FAC-UX-PR2/PR3)', () => {
  const pedido = (id: string, obraNombre: string, valorUnitario: number): EmitirFacturaPrefill => ({
    pedidoFacturableId: id,
    receptorNombre: 'Cliente de prueba',
    receptorRfc: 'XAXX010101000',
    receptorRegimenFiscal: '616',
    receptorCodigoPostal: '76000',
    obraNombre,
    lineas: [
      {
        productoId: null,
        claveProdServSat: '43211701',
        descripcion: `Vidrio ${id}`,
        claveUnidadSat: 'H87',
        cantidad: 1,
        valorUnitario,
        descuento: 0,
        requierePedimento: false,
        tasaIvaTraslado: 0.16,
      },
    ],
  });

  it('cambia de PED-1002 a PED-1001 sin conservar obra ni total del pedido anterior', async () => {
    const onSuccess = vi.fn();
    const onCancel = vi.fn();
    const { rerender } = render(
      <EmitirFacturaForm
        prefill={pedido('p-2', 'Torre Cancún – Fase 1', 87000)}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByDisplayValue('Torre Cancún – Fase 1');
    expect(screen.getAllByText('100920.00 MXN').length).toBeGreaterThan(0);

    rerender(
      <EmitirFacturaForm
        prefill={pedido('p-1', 'Obra PED-1001', 4000)}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );
    expect(await screen.findByDisplayValue('Obra PED-1001')).toBeInTheDocument();
    expect(screen.getAllByText('4640.00 MXN').length).toBeGreaterThan(0);
    expect(screen.queryByDisplayValue('Torre Cancún – Fase 1')).not.toBeInTheDocument();
    expect(screen.queryByText('Vidrio p-2')).not.toBeInTheDocument();
    expect(screen.queryByText('100920.00 MXN')).not.toBeInTheDocument();
  });

  it('bloquea el pedido ligado a CE y muestra los datos del expediente en lectura', async () => {
    const p = pedido('11111111-1111-4111-8111-111111111111', 'DEMO', 100);
    p.embarqueCe = {
      ligado: true,
      expedienteId: 'ce',
      sucursalId: 'mid',
      listoParaFacturar: false,
      codigoBloqueo: 'FAC_PEDIDO_NO_EMBARCADO',
      motivo: 'Falta embarcar posición 1: 0 de 60 H87.',
      folioExpediente: 'CE-EXP-DEMO',
      numeroPedimento: 'DEMO-PED',
      clavePedimento: 'RT',
      fechaPedimento: '2026-10-12',
      incoterm: 'DDP',
      contenedores: [],
    };
    render(<EmitirFacturaForm prefill={p} onSuccess={vi.fn()} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    expect(await screen.findByText('CE-EXP-DEMO')).toBeInTheDocument();
    expect(screen.getByText('Falta embarcar posición 1: 0 de 60 H87.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Emitir y timbrar' })).toBeDisabled();
    expect(screen.getByText('RT · Sin CCE.')).toBeInTheDocument();
    expect(screen.queryByPlaceholderText('Buscar pedimento…')).not.toBeInTheDocument();
  });

  it('conserva el comportamiento fiscal en RT y bloquea exportación con CCE por confirmar', async () => {
    const p = pedido('11111111-1111-4111-8111-111111111111', 'DEMO', 100);
    p.comportamientoFiscal = 3;
    p.embarqueCe = {
      ligado: true,
      expedienteId: 'ce',
      sucursalId: EMISOR.sucursalIdDefault,
      codigoBloqueo: null,
      motivo: null,
      numeroPedimento: 'DEMO-RT',
      fechaPedimento: '2026-10-12',
      listoParaFacturar: true,
      clavePedimento: 'RT',
      folioExpediente: 'CE-RT',
      incoterm: 'DDP',
      contenedores: [],
    };
    const { container } = render(
      <EmitirFacturaForm prefill={p} onSuccess={vi.fn()} onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    expect(
      await screen.findByText(
        'La exportación RT sin complemento requiere confirmación de Fiscal (VAL-CE-08).',
      ),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Emitir y timbrar' })).toBeDisabled();
    expect(container.querySelector('[name="comportamientoFiscal"]')).toHaveValue('3');
  });

  it('emite un pedido A1 listo con los datos del expediente en lectura', async () => {
    const p = pedido('11111111-1111-4111-8111-111111111111', 'DEMO', 100);
    p.comportamientoFiscal = 3;
    p.embarqueCe = {
      ligado: true,
      expedienteId: 'ce',
      sucursalId: EMISOR.sucursalIdDefault,
      listoParaFacturar: true,
      codigoBloqueo: null,
      motivo: null,
      folioExpediente: 'CE-A1-LISTO',
      numeroPedimento: 'DEMO-A1',
      clavePedimento: 'A1',
      fechaPedimento: '2026-10-12',
      incoterm: 'DDP',
      contenedores: [
        { id: 'c', numero: 'DEMO-CONT', blGuia: 'DEMO-BL', fechaEmbarqueReal: '2026-10-12' },
      ],
    };
    const enviado = vi.fn();
    const onSuccess = vi.fn();
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas/', async ({ request }) => {
        enviado(await request.json());
        return HttpResponse.json(
          { id: 'f-a1', folio: 'FA-A1', total: 116, version: 1, estado: 'Timbrado', uuid: 'uuid' },
          { status: 201 },
        );
      }),
    );
    const { container } = render(
      <EmitirFacturaForm prefill={p} onSuccess={onSuccess} onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByText('CE-A1-LISTO');
    const resumen = screen.getByRole('region', { name: 'Datos de embarque del expediente' });
    expect(within(resumen).getByText('DEMO-A1 · A1')).toBeInTheDocument();
    expect(within(resumen).getByText('DDP')).toBeInTheDocument();
    expect(within(resumen).getByText('DEMO-CONT')).toBeInTheDocument();
    expect(resumen.querySelector('input,select')).toBeNull();
    expect(screen.queryByLabelText('clave-pedimento')).not.toBeInTheDocument();
    const cambiar = (name: string, value: string) =>
      fireEvent.change(container.querySelector(`[name="${name}"]`)!, { target: { value } });
    cambiar('cceTcDof', '20');
    cambiar('cceReceptorNumRegIdTrib', 'DEMO-TAX');
    cambiar('cceReceptorDomicilioEstado', 'Texas');
    cambiar('cceReceptorDomicilioCodigoPostal', '00000');
    fireEvent.change(screen.getByLabelText('pais'), { target: { value: 'USA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Editar', hidden: true }));
    fireEvent.change(screen.getByLabelText('fraccion-arancelaria'), {
      target: { value: '70071999' },
    });
    fireEvent.change(screen.getByLabelText('unidad-aduana'), { target: { value: '06' } });
    cambiar('lineas.0.cantidadAduana', '1');
    cambiar('lineas.0.valorUnitarioAduana', '100');
    cambiar('lineas.0.valorDolares', '100');
    fireEvent.click(screen.getByRole('button', { name: 'Emitir y timbrar' }));
    await waitFor(() =>
      expect(enviado).toHaveBeenCalledWith(
        expect.objectContaining({
          pedidoFacturableId: p.pedidoFacturableId,
          comportamientoFiscal: 3,
          cce: expect.objectContaining({ claveDePedimento: 'A1', incoterm: 'DDP' }),
        }),
      ),
    );
    expect(onSuccess).toHaveBeenCalled();
  });

  it.each([
    [
      400,
      'CONFIG_PAC_NO_DISPONIBLE',
      'No hay configuración de PAC activa.',
      'No hay timbrado configurado para esta empresa. Pide a un administrador que lo active en Administración → Integraciones fiscales.',
    ],
    [
      422,
      'REGLA_FISCAL',
      'El importe no cumple la validación fiscal.',
      'El importe no cumple la validación fiscal.',
    ],
    [
      409,
      'CONFLICTO_FISCAL',
      'El comprobante cambió. Recarga los datos.',
      'El comprobante cambió. Recarga los datos.',
    ],
    [
      500,
      'FALLO_FISCAL',
      'No fue posible contactar al servicio fiscal.',
      'No fue posible contactar al servicio fiscal.',
    ],
  ])(
    'muestra un HTTP %s aunque el error apunte a un campo ausente y permite reintentar',
    async (status, code, detail, mensaje) => {
      const onSuccess = vi.fn();
      mswServer.use(
        http.post('*/api/v1/facturacion/facturas/', () =>
          HttpResponse.json(
            {
              type: 'about:blank',
              title: 'No se pudo timbrar',
              status,
              code,
              detail,
              errores: [{ campo: 'configuracionPac', codigo: code, mensaje: detail }],
            },
            { status },
          ),
        ),
      );
      render(
        <EmitirFacturaForm
          prefill={pedido('11111111-1111-4111-8111-111111111111', 'Obra', 4000)}
          onSuccess={onSuccess}
          onCancel={() => {}}
        />,
        { wrapper: createQueryWrapper() },
      );
      fireEvent.click(await screen.findByRole('button', { name: 'Emitir y timbrar' }));
      expect(await screen.findByText(mensaje)).toBeInTheDocument();
      expect(screen.getByRole('note')).toHaveTextContent(mensaje);
      expect(screen.getByRole('button', { name: 'Emitir y timbrar' })).toBeEnabled();
      expect(onSuccess).not.toHaveBeenCalled();
      expect(
        screen.queryByRole('link', { name: 'Abrir integraciones fiscales' }),
      ).not.toBeInTheDocument();
    },
  );

  it('permite emitir de nuevo después de un 422 genérico y retira el aviso', async () => {
    let intentos = 0;
    const onSuccess = vi.fn();
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas/', () => {
        intentos += 1;
        return intentos === 1
          ? HttpResponse.json(
              {
                type: 'about:blank',
                title: 'Validación fiscal',
                status: 422,
                detail: 'Revisa los datos fiscales.',
              },
              { status: 422 },
            )
          : HttpResponse.json(
              {
                id: 'f-correcta',
                folio: 'FA-2',
                total: 4640,
                version: 1,
                estado: 'Timbrado',
                uuid: 'uuid-ficticio',
              },
              { status: 201 },
            );
      }),
    );
    render(
      <EmitirFacturaForm
        prefill={pedido('11111111-1111-4111-8111-111111111111', 'Obra', 4000)}
        onSuccess={onSuccess}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(await screen.findByRole('button', { name: 'Emitir y timbrar' }));
    expect(await screen.findByText('Revisa los datos fiscales.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Emitir y timbrar' }));
    await waitFor(() =>
      expect(onSuccess).toHaveBeenCalledWith(expect.objectContaining({ id: 'f-correcta' })),
    );
    expect(intentos).toBe(2);
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
  });

  it('ofrece la ruta fiscal solo con permiso de consulta', async () => {
    setPermisos([PermisosCanonicos.IntegracionesFiscalLeer]);
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas/', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Sin PAC',
            status: 400,
            code: 'CONFIG_PAC_NO_DISPONIBLE',
          },
          { status: 400 },
        ),
      ),
    );
    render(
      <EmitirFacturaForm
        prefill={pedido('11111111-1111-4111-8111-111111111111', 'Obra', 4000)}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(await screen.findByRole('button', { name: 'Emitir y timbrar' }));
    expect(
      await screen.findByRole('link', { name: 'Abrir integraciones fiscales' }),
    ).toHaveAttribute('href', '/admin/integraciones/fiscal');
  });

  it.each(['403', '400'])(
    'un HTTP 201 con rechazo PAC %s muestra error y acceso al reintento',
    async (codigo) => {
      const onSuccess = vi.fn();
      mswServer.use(
        http.post('*/api/v1/facturacion/facturas/', () =>
          HttpResponse.json(
            {
              id: 'f-fallida',
              folio: 'FA-1',
              total: 4640,
              version: 1,
              estado: 'TimbradoFallido',
              uuid: null,
              timbradoErrorCodigo: codigo,
              timbradoErrorMensaje: 'No encontrado en lista LCO',
            },
            { status: 201 },
          ),
        ),
      );
      render(
        <EmitirFacturaForm
          prefill={pedido('11111111-1111-4111-8111-111111111111', 'Obra', 4000)}
          onSuccess={onSuccess}
          onCancel={() => {}}
        />,
        { wrapper: createQueryWrapper() },
      );
      fireEvent.click(await screen.findByRole('button', { name: 'Emitir y timbrar' }));
      await waitFor(() =>
        expect(toast.error).toHaveBeenCalledWith(
          'No se pudo timbrar la factura FA-1',
          expect.objectContaining({
            description: `${codigo}: No encontrado en lista LCO`,
            action: expect.objectContaining({ label: 'Reintentar timbrado' }),
          }),
        ),
      );
      expect(toast.success).not.toHaveBeenCalled();
      expect(onSuccess).toHaveBeenCalledWith(expect.objectContaining({ id: 'f-fallida' }));
      const opciones = vi.mocked(toast.error).mock.calls[0][1];
      if (opciones?.action && typeof opciones.action === 'object' && 'onClick' in opciones.action) {
        opciones.action.onClick({} as React.MouseEvent<HTMLButtonElement>);
      }
      expect(navegar).toHaveBeenCalledWith({
        to: '/facturacion/facturas/$id',
        params: { id: 'f-fallida' },
      });
    },
  );

  it('renderiza las 3 pestañas con TODO el contenido montado (forceMount) y el emisor prellenado', async () => {
    render(<EmitirFacturaForm onSuccess={() => {}} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    expect(await screen.findByRole('tab', { name: /Encabezado/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Posiciones/i })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Totales/i })).toBeInTheDocument();

    // forceMount: aunque la pestaña activa es Encabezado, los inputs de
    // Posiciones y el resumen de Totales existen en el DOM (ocultos) —
    // react-hook-form los necesita montados para conservar valores/errores.
    expect(screen.getByText(/Conceptos \(1\)/i)).toBeInTheDocument();
    expect(screen.getByText(/Resumen de importes/i)).toBeInTheDocument();

    // Emisor fijo (datos de la empresa): se muestra informativo en la
    // <EmisorInfoBar/> — sin inputs editables de RFC/régimen del emisor.
    expect(screen.getByText('MIL010101ABC')).toBeInTheDocument();
    expect(screen.getByText('Millet SA de CV')).toBeInTheDocument();
    expect(screen.queryByDisplayValue('MIL010101ABC')).not.toBeInTheDocument();
  });

  it('las pestañas inactivas quedan marcadas data-state=inactive (el CSS las oculta) y el click las activa', async () => {
    render(<EmitirFacturaForm onSuccess={() => {}} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('tab', { name: /Encabezado/i });

    // Regresión FAC-UX-PR8: con forceMount, Radix NO oculta el contenido
    // inactivo por sí solo — el TabsContent debe llevar
    // data-[state=inactive]:hidden para que las pestañas "funcionen".
    const contenidoTotales = screen.getByText(/Resumen de importes/i).closest('div[data-state]');
    expect(contenidoTotales).toHaveAttribute('data-state', 'inactive');

    // Radix TabsTrigger selecciona en mousedown (no en click).
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Totales/i }), {
      button: 0,
    });
    await waitFor(() => expect(contenidoTotales).toHaveAttribute('data-state', 'active'));
  });

  it('receptor SIEMPRE solo lectura: sin inputs, sin CTA sin permiso, y submit bloqueado sin cliente', async () => {
    render(<EmitirFacturaForm onSuccess={() => {}} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('tab', { name: /Encabezado/i });

    // No hay inputs de captura del receptor para nadie: el resumen de
    // solo lectura marca lo que falta en el master.
    expect(screen.queryByText('RFC receptor')).not.toBeInTheDocument();
    expect(screen.getAllByText('Falta en el catálogo').length).toBeGreaterThan(0);
    // El CTA al catálogo lo controla el permiso — sin él no aparece.
    expect(screen.queryByText(/Corregir en catálogo/i)).not.toBeInTheDocument();
    expect(
      screen.getByText(/Selecciona un cliente del catálogo para poder emitir/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Emitir y timbrar/i })).toBeDisabled();
  });

  it('incluso con el permiso, el receptor no es capturable: solo aparece el CTA al catálogo', async () => {
    setPermisos([PermisosCanonicos.FacturacionFacturasEditarReceptor]);
    render(
      <EmitirFacturaForm
        prefill={{
          pedidoFacturableId: 'p-1',
          clienteId: 'c-1',
          receptorNombre: 'Cliente Demo SA',
          datosFiscalesIncompletos: true,
        }}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByRole('tab', { name: /Encabezado/i });

    expect(screen.queryByText('RFC receptor')).not.toBeInTheDocument();
    expect(screen.getByText(/Corregir en catálogo/i)).toBeInTheDocument();
    // Cliente incompleto (gap G12) → bloqueo con liga al master.
    expect(screen.getByText(/complétalos en Datos Maestros → Clientes/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Emitir y timbrar/i })).toBeDisabled();
  });

  it('con receptor completo, al fallar el submit muestra el badge de errores en Posiciones', async () => {
    render(
      <EmitirFacturaForm
        prefill={{
          pedidoFacturableId: 'p-1',
          clienteId: 'c-1',
          receptorNombre: 'Cliente Demo SA',
          receptorRfc: 'XAXX010101000',
          receptorRegimenFiscal: '616',
          receptorCodigoPostal: '76000',
        }}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );

    // Receptor completo → submit habilitado; la línea default vacía hace
    // fallar Posiciones (descripción/clave del concepto).
    fireEvent.click(await screen.findByRole('button', { name: /Emitir y timbrar/i }));
    await waitFor(() => {
      const tabPosiciones = screen.getByRole('tab', { name: /Posiciones/i });
      expect(tabPosiciones.querySelector('[aria-label$="errores"]')).not.toBeNull();
    });
  });

  it('Cancelar sin cambios invoca onCancel sin confirm', async () => {
    const onCancel = vi.fn();
    render(<EmitirFacturaForm onSuccess={() => {}} onCancel={onCancel} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(await screen.findByRole('button', { name: /Cancelar/i }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('las líneas del pedido llegan como filas compactas; Editar abre el form extenso', async () => {
    render(
      <EmitirFacturaForm
        prefill={{
          pedidoFacturableId: 'p-1',
          receptorNombre: 'Cliente Demo SA',
          receptorRfc: 'XAXX010101000',
          receptorRegimenFiscal: '616',
          receptorCodigoPostal: '76000',
          lineas: [
            {
              productoId: null,
              claveProdServSat: '43211701',
              descripcion: 'Vidrio templado 6mm',
              claveUnidadSat: 'H87',
              cantidad: 2,
              valorUnitario: 150,
              descuento: 0,
              requierePedimento: false,
              tasaIvaTraslado: 0.16,
            },
          ],
        }}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByRole('tab', { name: /Posiciones/i });

    // Fila compacta de solo lectura: descripción + importe, sin el form
    // extenso (el input "Clave SAT *" no está montado).
    expect(screen.getByText('Vidrio templado 6mm')).toBeInTheDocument();
    // El importe aparece en la fila compacta y en el pie de totales.
    expect(screen.getAllByText('300.00').length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText('Clave SAT *')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Editar/i }));
    expect(await screen.findByText('Clave SAT *')).toBeInTheDocument();
    // "Listo" colapsa de vuelta a la fila compacta.
    fireEvent.click(screen.getByRole('button', { name: /Listo/i }));
    await waitFor(() => expect(screen.queryByText('Clave SAT *')).not.toBeInTheDocument());
  });

  it('línea de pedido con artículo sin clave SAT: fila roja informativa, sin modo edición y sin CTA sin permiso', async () => {
    render(
      <EmitirFacturaForm
        prefill={{
          pedidoFacturableId: 'p-1',
          receptorNombre: 'Cliente Demo SA',
          receptorRfc: 'XAXX010101000',
          receptorRegimenFiscal: '616',
          receptorCodigoPostal: '76000',
          lineas: [
            {
              productoId: 'prod-1',
              claveProdServSat: '',
              descripcion: 'Cristal claro 9mm',
              claveUnidadSat: '',
              cantidad: 1,
              valorUnitario: 100,
              descuento: 0,
              requierePedimento: false,
            },
          ],
        }}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByRole('tab', { name: /Posiciones/i });

    // La fila NO se expande sola: informa en rojo y sigue compacta.
    expect(screen.getByText(/Faltan datos fiscales del artículo/i)).toBeInTheDocument();
    expect(screen.queryByText('Clave SAT *')).not.toBeInTheDocument();
    // El CTA al catálogo de productos lo controla el permiso
    // editar-articulo — sin él no aparece.
    expect(screen.queryByText(/Corregir en catálogo/i)).not.toBeInTheDocument();
  });

  it('con permiso editar-articulo: CTA al catálogo del producto; Editar NO expone inputs fiscales', async () => {
    setPermisos([PermisosCanonicos.FacturacionFacturasEditarArticulo]);
    render(
      <EmitirFacturaForm
        prefill={{
          pedidoFacturableId: 'p-1',
          receptorNombre: 'Cliente Demo SA',
          receptorRfc: 'XAXX010101000',
          receptorRegimenFiscal: '616',
          receptorCodigoPostal: '76000',
          lineas: [
            {
              productoId: 'prod-1',
              claveProdServSat: '',
              descripcion: 'Cristal claro 9mm',
              claveUnidadSat: 'H87',
              cantidad: 1,
              valorUnitario: 100,
              descuento: 0,
              requierePedimento: false,
            },
          ],
        }}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByRole('tab', { name: /Posiciones/i });

    // CTA visible en la fila compacta roja.
    expect(screen.getByText(/Corregir en catálogo/i)).toBeInTheDocument();

    // Editar abre solo lo comercial: lo fiscal queda como resumen de
    // solo lectura ("Falta en el catálogo"), sin inputs de clave SAT.
    fireEvent.click(screen.getByRole('button', { name: /Editar/i }));
    expect(await screen.findByText('Descripción *')).toBeInTheDocument();
    expect(screen.queryByText('Clave SAT *')).not.toBeInTheDocument();
    expect(screen.getByText('Falta en el catálogo')).toBeInTheDocument();
  });
  it('envía ClienteId al emitir desde el cliente seleccionado', async () => {
    const clienteId = '00000003-0000-0000-0000-000000000001';
    const solicitudes: unknown[] = [];
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas', async ({ request }) => {
        solicitudes.push(await request.json());
        return HttpResponse.json(
          {
            code: 'RECEPTOR_FISCAL_INVALIDO',
            title: 'Corrige el receptor',
            status: 422,
            clienteId,
            campos: [{ campo: 'codigoPostalFiscal', motivo: 'Falta el CP fiscal en el maestro.' }],
          },
          { status: 422 },
        );
      }),
    );
    setPermisos([PermisosCanonicos.FacturacionFacturasEmitir]);
    render(
      <EmitirFacturaForm
        prefill={{
          pedidoFacturableId: '00000003-0000-0000-0000-000000000002',
          clienteId,
          receptorNombre: 'CLIENTE FICTICIO',
          receptorRfc: 'AAA010101AAA',
          receptorRegimenFiscal: '601',
          receptorCodigoPostal: '97000',
          receptorUsoCfdi: 'G03',
          lineas: [
            {
              productoId: null,
              claveProdServSat: '01010101',
              descripcion: 'Vidrio prueba',
              claveUnidadSat: 'H87',
              cantidad: 1,
              valorUnitario: 100,
              descuento: 0,
              requierePedimento: false,
              tasaIvaTraslado: 0.16,
            },
          ],
        }}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(await screen.findByRole('button', { name: /Emitir y timbrar/i }));
    await waitFor(() => expect(solicitudes).toEqual([expect.objectContaining({ clienteId })]));
  });
});

describe('<EmitirFacturaForm> — venta directa (F6.1)', () => {
  const pedidoVd = (
    extra: Partial<EmitirFacturaPrefill> = {},
  ): EmitirFacturaPrefill => ({
    pedidoFacturableId: 'p-vd',
    origen: 'Manual',
    claseManual: 'VentaDirecta',
    subtipoVentaDirecta: 'Activo',
    clienteId: '22222222-2222-4222-8222-222222222222',
    receptorNombre: 'Cliente Activo',
    receptorRfc: 'AAA010101AAA',
    receptorRegimenFiscal: '601',
    receptorCodigoPostal: '97000',
    // El pedido llega con otro comportamiento: la emisión lo fija en activo.
    comportamientoFiscal: 1,
    lineas: [
      {
        productoId: null,
        claveProdServSat: '01010101',
        descripcion: 'Montacargas usado',
        claveUnidadSat: 'H87',
        cantidad: 1,
        valorUnitario: 1000,
        descuento: 0,
        requierePedimento: false,
        tasaIvaTraslado: 0.16,
      },
    ],
    ...extra,
  });

  beforeEach(() => {
    mswServer.use(
      http.get('*/api/v1/facturacion/activos/autorizaciones', () => HttpResponse.json([])),
    );
  });

  it('pedido VD · Activo: comportamiento fijo, autorización obligatoria y leyenda de serie', async () => {
    render(
      <EmitirFacturaForm prefill={pedidoVd()} onSuccess={() => {}} onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    const comportamiento = await screen.findByRole('textbox', {
      name: /Comportamiento fiscal/i,
    });
    expect(comportamiento).toHaveValue('Venta de activo fijo');
    expect(comportamiento).toHaveAttribute('readonly');
    expect(screen.getAllByText(/autorización del Contador General/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/\(obligatoria\)/i)).toBeInTheDocument();
    expect(screen.getByTestId('leyenda-serie-venta-directa')).toHaveTextContent(
      'Se timbrará con la serie de Venta directa de la sucursal',
    );
  });

  it('sin autorización no envía la emisión del activo', async () => {
    const solicitudes: unknown[] = [];
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas/', async ({ request }) => {
        solicitudes.push(await request.json());
        return HttpResponse.json({}, { status: 201 });
      }),
    );
    render(
      <EmitirFacturaForm prefill={pedidoVd()} onSuccess={() => {}} onCancel={() => {}} />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(await screen.findByRole('button', { name: 'Emitir y timbrar' }));
    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(solicitudes).toHaveLength(0);
  });

  it('422 SERIE_VENTA_DIRECTA_NO_CONFIGURADA: callout con enlace a Series', async () => {
    setPermisos([PermisosCanonicos.FacturacionFacturasEmitir, PermisosCanonicos.AdminSeriesGestionar]);
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas/', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'SERIE_VENTA_DIRECTA_NO_CONFIGURADA',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(
      <EmitirFacturaForm
        prefill={pedidoVd({ subtipoVentaDirecta: 'Almacenable' })}
        onSuccess={() => {}}
        onCancel={() => {}}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(await screen.findByRole('button', { name: 'Emitir y timbrar' }));
    expect(await screen.findByText('Falta la serie de Venta directa')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Administración → Series/i })).toHaveAttribute(
      'href',
      '/admin/series',
    );
  });

  it('pedido manual sin clase: bloquea la emisión y ofrece editar el pedido', async () => {
    const onEditarPedido = vi.fn();
    render(
      <EmitirFacturaForm
        prefill={pedidoVd({ claseManual: null, subtipoVentaDirecta: null })}
        onSuccess={() => {}}
        onCancel={() => {}}
        onEditarPedido={onEditarPedido}
      />,
      { wrapper: createQueryWrapper() },
    );
    expect(await screen.findByText('Pedido manual sin clase')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Emitir y timbrar' })).toBeDisabled();
    expect(screen.queryByTestId('leyenda-serie-venta-directa')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Editar pedido' }));
    expect(onEditarPedido).toHaveBeenCalledTimes(1);
  });
});
