import type { ReactNode } from 'react';
import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EnviarRevisionSheet } from './EnviarRevisionSheet';

vi.mock('@/lib/api', () => ({ useFormIdempotencyKey: () => 'DEMO-FICTICIA' }));
vi.mock('@/features/cxp/api/useRevision', () => ({
  useEnviarFacturaARevision: () => ({ isPending: false }),
  useDependenciasRevisoras: () => ({ data: [] }),
  useMotivosRevision: () => ({ data: [
    { id: 'demo-1', nombre: 'Daños o defectos en mercancía', slaDias: 5, dependenciaRevisoraDefaultCodigo: 'COMPRAS', areaAvisadaCodigo: null },
    { id: 'demo-2', nombre: 'No conformidad', slaDias: 5, dependenciaRevisoraDefaultCodigo: 'AREA_USUARIA', areaAvisadaCodigo: 'COMPRAS' },
    { id: 'demo-3', nombre: 'Calidad insuficiente', slaDias: 5, dependenciaRevisoraDefaultCodigo: 'AREA_USUARIA', areaAvisadaCodigo: 'COMPRAS' },
    { id: 'demo-4', nombre: 'Disputa contractual', slaDias: 15, dependenciaRevisoraDefaultCodigo: 'DIRECCION_F', areaAvisadaCodigo: null },
    { id: 'demo-5', nombre: 'Otros', slaDias: 5, dependenciaRevisoraDefaultCodigo: 'CXP', areaAvisadaCodigo: null },
  ] }),
}));
vi.mock('@/components/ui/select', () => ({
  Select: ({ children }: { children: ReactNode }) => <>{children}</>,
  SelectTrigger: () => null, SelectValue: () => null,
  SelectContent: ({ children }: { children: ReactNode }) => <>{children}</>,
  SelectItem: ({ children }: { children: ReactNode }) => <div role="option">{children}</div>,
}));

describe('Motivos de revisión: áreas en español', () => {
  it('muestra responsable y distingue a Compras como área a avisar', () => {
    render(<EnviarRevisionSheet open onOpenChange={vi.fn()} factura={null} />);
    expect(screen.getByRole('option', { name: /Daños.*Área responsable: Compras/ })).toBeInTheDocument();
    for (const motivo of ['No conformidad', 'Calidad insuficiente']) {
      expect(screen.getByRole('option', { name: new RegExp(`${motivo}.*Área responsable: Área solicitante.*Avisar a: Compras`) })).toBeInTheDocument();
    }
    expect(screen.getByRole('option', { name: /Disputa.*Área responsable: Dirección de Finanzas/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /Otros.*Área responsable: Cuentas por pagar/ })).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/AREA_USUARIA|DIRECCION_F|CXP|COMPRAS/);
  });
});
