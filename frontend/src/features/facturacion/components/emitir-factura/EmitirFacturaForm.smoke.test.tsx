import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EmitirFacturaForm } from './EmitirFacturaForm';

// Selectores de catálogo stubbeados: el smoke valida la estructura de
// pestañas del form, no los combobox de catálogos (B16 incluidos).
vi.mock('@/components/erp', () => ({
  SucursalSelector: () => <input aria-label="Sucursal (stub)" />,
  RegimenFiscalSelector: () => <input aria-label="Régimen (stub)" />,
  UsoCfdiSelector: () => <input aria-label="Uso CFDI (stub)" />,
  FormaPagoSelector: () => <input aria-label="Forma de pago (stub)" />,
}));
vi.mock('@/components/erp/selectors/MonedaSelector', () => ({
  MonedaSelector: () => <input aria-label="Moneda (stub)" />,
}));
// El CTA "Corregir en catálogo" y el banner de bloqueo usan <Link/>; el
// smoke corre sin RouterProvider (mismo patrón que DetallePedido.smoke).
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
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
  mswServer.use(
    http.get('*/api/v1/facturacion/emisor-defaults', () =>
      HttpResponse.json(EMISOR),
    ),
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
  it('renderiza las 3 pestañas con TODO el contenido montado (forceMount) y el emisor prellenado', async () => {
    render(<EmitirFacturaForm onSuccess={() => {}} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    expect(
      await screen.findByRole('tab', { name: /Encabezado/i }),
    ).toBeInTheDocument();
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
    const contenidoTotales = screen
      .getByText(/Resumen de importes/i)
      .closest('div[data-state]');
    expect(contenidoTotales).toHaveAttribute('data-state', 'inactive');

    // Radix TabsTrigger selecciona en mousedown (no en click).
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Totales/i }), {
      button: 0,
    });
    await waitFor(() =>
      expect(contenidoTotales).toHaveAttribute('data-state', 'active'),
    );
  });

  it('receptor SIEMPRE solo lectura: sin inputs, sin CTA sin permiso, y submit bloqueado sin cliente', async () => {
    render(<EmitirFacturaForm onSuccess={() => {}} onCancel={() => {}} />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByRole('tab', { name: /Encabezado/i });

    // No hay inputs de captura del receptor para nadie: el resumen de
    // solo lectura marca lo que falta en el master.
    expect(screen.queryByText('RFC receptor')).not.toBeInTheDocument();
    expect(screen.getAllByText('Falta en el master').length).toBeGreaterThan(0);
    // El CTA al catálogo lo controla el permiso — sin él no aparece.
    expect(screen.queryByText(/Corregir en catálogo/i)).not.toBeInTheDocument();
    expect(
      screen.getByText(/Selecciona un cliente del catálogo para poder emitir/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Emitir y timbrar/i }),
    ).toBeDisabled();
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
    expect(
      screen.getByText(/complétalos en Datos Maestros → Clientes/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Emitir y timbrar/i }),
    ).toBeDisabled();
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
    fireEvent.click(
      await screen.findByRole('button', { name: /Emitir y timbrar/i }),
    );
    await waitFor(() => {
      const tabPosiciones = screen.getByRole('tab', { name: /Posiciones/i });
      expect(
        tabPosiciones.querySelector('[aria-label$="errores"]'),
      ).not.toBeNull();
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
    await waitFor(() =>
      expect(screen.queryByText('Clave SAT *')).not.toBeInTheDocument(),
    );
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
    expect(
      screen.getByText(/Faltan datos fiscales del artículo/i),
    ).toBeInTheDocument();
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
});
