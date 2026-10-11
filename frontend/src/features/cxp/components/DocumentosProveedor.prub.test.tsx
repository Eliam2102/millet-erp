import { beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { NuevoAnticipoSheet } from './NuevoAnticipoSheet';
import { NuevaNotaCreditoSheet } from './NuevaNotaCreditoSheet';
import { createQueryWrapper } from '@/test/test-query-client';
import type { CfdiListItem } from '../api/types';
const mocks = vi.hoisted(() => ({ buscar: vi.fn() }));
vi.mock('@/features/catalogos/api', () => ({ fetchProveedoresPorRfc: mocks.buscar }));
vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => false }));
vi.mock('./SerieAnticipoProveedor', () => ({ SerieAnticipoProveedor: () => null }));
vi.mock('./CargarCfdiSheet', () => ({ CargarCfdiSheet: () => null }));
vi.mock('./OcPicker', () => ({ OcPicker: () => null }));
vi.mock('@/features/cxp/api/useCfdis', () => ({ fetchCfdiParseado: async () => ({ cfdiRelacionados: [] }), fetchCfdiDetalle: vi.fn() }));
vi.mock('@/components/erp', () => ({
  OrdenCompraSelector: () => null,
  ProveedorSelector: ({ value, onChange }: { value: string | null; onChange: (id: string | null) => void }) => <select aria-label="Proveedor" value={value ?? ''} onChange={e => onChange(e.target.value)}><option value="">Selecciona</option><option value="proveedor-rfc">Proveedor emisor</option><option value="proveedor-manual">Proveedor manual</option></select>,
}));
const cfdi = { id: 'cfdi-prub', uuidCfdi: '11111111-1111-4111-8111-111111111111', rfcEmisor: 'AAA010101AAA', serie: 'FANT', folio: '1', fechaCfdi: '2026-10-10T12:00:00Z', moneda: 'MXN', subtotal: 100, impuestosTrasladados: 16, retenciones: 0, total: 116, tipoCambio: null } as CfdiListItem;
vi.mock('./CfdiPorProcesarPicker', () => ({ CfdiPorProcesarPicker: ({ onSelect }: { onSelect: (item: CfdiListItem) => void }) => <button type="button" onClick={() => onSelect(cfdi)}>Vincular CFDI</button> }));
beforeEach(() => { mocks.buscar.mockReset(); });
describe.each([['anticipo', NuevoAnticipoSheet], ['nota de crédito', NuevaNotaCreditoSheet]] as const)('Proveedor de %s', (_nombre, Componente) => {
  it('se llena desde el RFC del emisor y permanece tras cambiar los importes', async () => {
    mocks.buscar.mockResolvedValue([{ id: 'proveedor-rfc' }]);
    render(<Componente open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Vincular CFDI' }));
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Proveedor' })).toHaveValue('proveedor-rfc'));
    expect(mocks.buscar).toHaveBeenCalledWith('AAA010101AAA');
    fireEvent.change(screen.getByPlaceholderText('12345'), { target: { value: '2' } });
    expect(screen.getByRole('combobox', { name: 'Proveedor' })).toHaveValue('proveedor-rfc');
  });
  it('una respuesta tardía de RFC conserva la selección manual', async () => {
    let resolver: (items: { id: string }[]) => void = () => {};
    mocks.buscar.mockReturnValue(new Promise<{ id: string }[]>(resolve => { resolver = resolve; }));
    render(<Componente open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Vincular CFDI' }));
    fireEvent.change(screen.getByRole('combobox', { name: 'Proveedor' }), { target: { value: 'proveedor-manual' } });
    await act(async () => { resolver([{ id: 'proveedor-rfc' }]); });
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Proveedor' })).toHaveValue('proveedor-manual'));
  });
});
