import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ETIQUETA_DIMENSION } from '../lib/dimensiones';
import { MovimientosPruebaPage } from './MovimientosPruebaPage';

const API = '*/api/v1/contabilidad';
const cuenta = {
  id: 'c1', codigo: 'FIX-501.01', nombre: 'FIX Mantenimiento', padreId: 'p', nivel: 2, naturaleza: 'Deudora', tipo: 'Afectable',
  estatus: 'Activo', activa: true, cuentaControl: 'Ninguna', codigoAgrupador: null, grupoReporte: null, pendienteValidacion: false, version: 1,
  clase: 'Cuenta', rubroId: null,
};
const req = (dimension: string, requerimiento: string, reglaId: string | null = null) => ({
  dimension, nombreDimension: dimension, requerimiento, reglaId, cuentaOrigenCodigo: reglaId ? 'FIX-501' : null, heredada: !!reglaId,
  paraTodosLosTipos: false, vigenteDesde: reglaId ? '2026-10-01' : null, vigenteHasta: null, esPrueba: !!reglaId,
});
const MATRIZ = [req('Dim1', 'Opcional'), req('Dim2', 'Opcional'), req('Dim3', 'Obligatorio', 'r1')];

function servidor(over: Parameters<typeof mswServer.use> = []) {
  const centrosPedidos: URLSearchParams[] = [];
  mswServer.use(
    http.get(`${API}/movimientos/sucursales`, () => HttpResponse.json([
      { id: 's1', clave: 'MER', nombre: 'FIX Mérida', activa: true },
      { id: 's2', clave: 'CUN', nombre: 'FIX Cancún', activa: true },
    ])),
    http.get(`${API}/tipos-documento`, () => HttpResponse.json([{ id: 't1', clave: 'FIX-FP', nombre: 'FIX Factura', activo: true, esPrueba: true, version: 1 }])),
    http.get(`${API}/cuentas`, () => HttpResponse.json({ items: [cuenta], total: 1, offset: 0, limit: 50 })),
    http.get(`${API}/reglas-dimension/efectivas`, () => HttpResponse.json(MATRIZ)),
    http.get(`${API}/movimientos-prueba`, () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 10 })),
    http.get(`${API}/movimientos/centros`, ({ request }) => {
      centrosPedidos.push(new URL(request.url).searchParams);
      return HttpResponse.json([]);
    }),
    ...over,
  );
  return centrosPedidos;
}

async function capturar() {
  render(<MovimientosPruebaPage />, { wrapper: createQueryWrapper() });
  await screen.findByRole('option', { name: 'MER — FIX Mérida' });
  fireEvent.change(screen.getByLabelText('Sucursal'), { target: { value: 's1' } });
  await screen.findByRole('option', { name: 'FIX-FP — FIX Factura' });
  fireEvent.change(screen.getByLabelText('Tipo de documento'), { target: { value: 't1' } });
  fireEvent.click(screen.getByLabelText('Cuenta'));
  fireEvent.click(await screen.findByText('FIX Mantenimiento'));
  // El popover de la cuenta termina de cerrarse (y devuelve el foco) antes de seguir capturando.
  await waitFor(() => expect(screen.getByLabelText('Cuenta')).toHaveTextContent('FIX-501.01'));
  await waitFor(() => expect(screen.queryByPlaceholderText('Buscar cuenta por código o nombre…')).not.toBeInTheDocument());
}

describe('<MovimientosPruebaPage>', () => {
  it('las acciones explican por qué están deshabilitadas hasta completar la captura', async () => {
    servidor();
    render(<MovimientosPruebaPage />, { wrapper: createQueryWrapper() });
    const registrar = screen.getByRole('button', { name: 'Registrar movimiento de prueba' });
    expect(registrar).toBeDisabled();
    expect(registrar).toHaveAttribute('title', expect.stringContaining('Elige sucursal'));
    // Sin sucursal no se puede elegir centro: solo se ofrecen los de la sucursal.
    expect(screen.getByLabelText(ETIQUETA_DIMENSION.Dim3)).toBeDisabled();
  });

  it('muestra qué dimensiones pide la cuenta y, al validar, el error junto al campo que falta', async () => {
    let body: unknown;
    servidor([http.post(`${API}/movimientos/validar`, async ({ request }) => {
      body = await request.json();
      return HttpResponse.json({
        valido: false, requerimientos: MATRIZ, centros: { dim1Id: null, dim2Id: null, dim3Id: null },
        errores: [{ codigo: 'CONTAB_DIM_OBLIGATORIA_FALTANTE', mensaje: `Falta ${ETIQUETA_DIMENSION.Dim3}: es obligatoria para la cuenta FIX-501.01 en «FIX Factura».`, campo: 'dim3Id', dimension: 'Dim3' }],
      });
    })]);
    await capturar();
    // Requerimiento efectivo visible antes de validar, con su origen (regla heredada de la rama).
    expect(await screen.findByText('Obligatoria')).toBeInTheDocument();
    expect(screen.getByText(`${ETIQUETA_DIMENSION.Dim3}: Heredada de la cuenta FIX-501, desde 1 oct 2026, (regla de prueba).`)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Validar' }));
    const alerta = await screen.findByRole('alert');
    expect(alerta).toHaveTextContent('La combinación no es válida');
    expect(document.getElementById('mov-dim3Id-err')).toHaveTextContent(`Falta ${ETIQUETA_DIMENSION.Dim3}`);
    expect(screen.getByLabelText(ETIQUETA_DIMENSION.Dim3)).toHaveAttribute('aria-invalid', 'true');
    expect(body).toMatchObject({ cuentaId: 'c1', tipoDocumentoId: 't1', sucursalId: 's1', dim1Id: null, dim2Id: null, dim3Id: null, origen: 'Manual' });
  });

  it('registrar: 422 con errores de centro de otra sucursal se explica; 201 confirma el registro', async () => {
    let intentos = 0;
    servidor([http.post(`${API}/movimientos-prueba`, () => {
      intentos += 1;
      if (intentos === 1)
        return HttpResponse.json({
          type: 'x', title: 'El movimiento no cumple las reglas de dimensión.', status: 422, code: 'CONTAB_DIM_MOVIMIENTO_INVALIDO',
          errores: [{ codigo: 'CONTAB_DIM_CENTRO_OTRA_SUCURSAL', mensaje: 'El centro QB01 no está asignado a la sucursal FIX Mérida.', campo: 'dim3Id', dimension: 'Dim3' }],
        }, { status: 422 });
      return HttpResponse.json({
        id: 'm1', sucursalId: 's1', sucursalNombre: 'FIX Mérida', cuentaId: 'c1', cuentaCodigo: 'FIX-501.01', tipoDocumentoId: 't1',
        tipoDocumentoClave: 'FIX-FP', fechaContable: '2026-10-04', dim1: null, dim2: null, dim3: null, referencia: null,
        reglasAplicadas: MATRIZ, confirmadoEn: '2026-10-04T18:00:00Z', confirmadoPor: 'uziel',
      }, { status: 201 });
    })]);
    await capturar();
    fireEvent.click(screen.getByRole('button', { name: 'Registrar movimiento de prueba' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('no está asignado a la sucursal FIX Mérida');

    fireEvent.click(screen.getByRole('button', { name: 'Registrar movimiento de prueba' }));
    expect(await screen.findByRole('status')).toHaveTextContent('Movimiento de prueba registrado');
  });

  it('el selector de centros consulta solo los de la sucursal elegida y lo explica si no hay', async () => {
    const pedidos = servidor();
    await capturar();
    fireEvent.click(screen.getByLabelText(ETIQUETA_DIMENSION.Dim2));
    await waitFor(() => expect(pedidos.some((p) => p.get('sucursalId') === 's1' && p.get('nivel') === 'Dim2')).toBe(true));
    expect(await screen.findByText('Esta sucursal no tiene centros de este nivel asignados.')).toBeInTheDocument();
  });
});
