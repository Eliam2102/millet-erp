import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { LoteCargaPage } from '@/features/cargas/pages/LoteCargaPage';

const toastMock = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }));
vi.mock('sonner', () => ({ toast: toastMock }));

const TIPO = 'contabilidad.saldos-iniciales';
const tipo = {
  tipo: TIPO, nombre: 'Saldos iniciales contables', requiereFechaCorte: true, puedeSubir: true, puedeAutorizar: true,
  columnas: [
    { nombre: 'cuenta', ayuda: 'Cuenta del catálogo nuevo', obligatoria: true, alias: [] },
    { nombre: 'cargo', ayuda: 'Cargo', obligatoria: true, alias: [] },
    { nombre: 'abono', ayuda: 'Abono', obligatoria: true, alias: [] },
  ],
};
const resumen = (estado: string, extra: Record<string, unknown> = {}) => ({
  id: 'l-1', tipo: TIPO, nombreArchivo: 'balanza.csv', estado, fechaCorte: '2026-10-31', totalFilas: 2, totalErrores: 1,
  puedeAplicar: false, subidoPor: 'Contadora', subidoEn: '2026-11-09T10:00:00Z', decididoPor: null, decididoEn: null,
  folioResultado: null, ultimoError: null, ...extra,
});
const detalle = (estado: string, extra: Record<string, unknown> = {}) => ({
  lote: resumen(estado, extra), huella: 'A'.repeat(64), mapeo: {}, resumen: { cargos: '1,000.00', abonos: '700.00' },
  erroresArchivo: [{ fila: null, campo: 'archivo', codigo: 'AP_NO_CUADRA', mensaje: 'No cuadra por 300.00' }],
  filas: [
    { fila: 2, valores: { cuenta: '105.01.00.00', cargo: '1000', abono: '' }, errores: [] },
    { fila: 3, valores: { cuenta: '105.09.00.00', cargo: '', abono: '700' },
      errores: [{ fila: 3, campo: 'cuenta', codigo: 'AP_CUENTA_NO_EXISTE', mensaje: 'Cuenta no existe en el catálogo' }] },
  ],
  filasMostradas: 2, motivoRechazo: null, referencia: null, historial: [{ accion: 'Subido', usuario: 'Contadora', en: '2026-11-09T10:00:00Z', detalle: null }],
  puedeAutorizar: false, puedeRechazar: true, puedeReintentar: false,
});

describe('<LoteCargaPage> — smoke (M-047 + M-167)', () => {
  it('sube el archivo, mapea las columnas sugeridas y muestra la vista previa con los errores por renglón', async () => {
    let mapeoEnviado = '';
    mswServer.use(
      http.get('*/api/v1/cargas/tipos', () => HttpResponse.json([tipo])),
      http.get(`*/api/v1/cargas/tipos/${TIPO}/lotes`, () => HttpResponse.json([])),
      http.post(`*/api/v1/cargas/tipos/${TIPO}/encabezados`, () =>
        HttpResponse.json({ encabezados: ['No. cuenta', 'Debe', 'Haber'], mapeoSugerido: { 'No. cuenta': 'cuenta' } })),
      http.post(`*/api/v1/cargas/tipos/${TIPO}/lotes`, async ({ request }) => {
        mapeoEnviado = (await request.text()).match(/name="mapeo"\r\n\r\n([^\r]*)/)?.[1] ?? '';
        return HttpResponse.json(detalle('Subido'));
      }),
      http.get('*/api/v1/cargas/lotes/l-1', () => HttpResponse.json(detalle('Subido'))),
    );
    render(<LoteCargaPage tipo={TIPO} titulo="Saldos iniciales contables" descripcion="d" />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: /Nueva carga/ }));
    fireEvent.change(screen.getByLabelText(/1\. Archivo/), { target: { files: [new File(['x'], 'balanza.csv', { type: 'text/csv' })] } });
    await waitFor(() => expect(screen.getByLabelText('cuenta *')).toHaveValue('No. cuenta'));
    const validar = screen.getByRole('button', { name: /Validar y ver vista previa/ });
    expect(validar).toBeDisabled();
    expect(screen.getByText(/Elija la columna para: cargo, abono/)).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('cargo *'), { target: { value: 'Debe' } });
    fireEvent.change(screen.getByLabelText('abono *'), { target: { value: 'Haber' } });
    fireEvent.change(screen.getByLabelText('Fecha de corte'), { target: { value: '2026-10-31' } });
    expect(validar).toBeEnabled();
    fireEvent.click(validar);

    // La vista previa se vuelve a pintar al refrescar el lote: se espera el estado final.
    await waitFor(() => {
      expect(screen.getByText('Cuenta no existe en el catálogo')).toBeInTheDocument();
      expect(screen.getByText('No cuadra por 300.00')).toBeInTheDocument();
    });
    expect(JSON.parse(mapeoEnviado)).toEqual({ 'No. cuenta': 'cuenta', Debe: 'cargo', Haber: 'abono' });
    expect(screen.queryByRole('button', { name: 'Autorizar y aplicar' })).toBeNull();
  });

  it('otra persona autoriza y aplica; el aviso dice el folio de la póliza', async () => {
    toastMock.success.mockClear();
    mswServer.use(
      http.get('*/api/v1/cargas/tipos', () => HttpResponse.json([tipo])),
      http.get(`*/api/v1/cargas/tipos/${TIPO}/lotes`, () => HttpResponse.json([resumen('Subido', { totalErrores: 0, puedeAplicar: true })])),
      http.get('*/api/v1/cargas/lotes/l-1', () => HttpResponse.json({
        ...detalle('Subido', { totalErrores: 0, puedeAplicar: true }), erroresArchivo: [], puedeAutorizar: true })),
      http.post('*/api/v1/cargas/lotes/l-1/autorizar', () => HttpResponse.json({
        ...detalle('Aplicado', { totalErrores: 0, puedeAplicar: true, folioResultado: 'SI-000001' }), erroresArchivo: [] })),
    );
    render(<LoteCargaPage tipo={TIPO} titulo="Saldos iniciales contables" descripcion="d" />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: 'balanza.csv' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Autorizar y aplicar' }));
    await waitFor(() => expect(toastMock.success).toHaveBeenCalledWith('Carga aplicada: SI-000001.'));
  });
});
