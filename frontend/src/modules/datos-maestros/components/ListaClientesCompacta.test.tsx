import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ListaClientesCompacta } from '@/modules/datos-maestros/components/ListaClientesCompacta';
import type { ClienteItem } from '@/modules/datos-maestros/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    [key: string]: unknown;
  }) => (
    <a href={to} className={className} {...rest}>
      {children}
    </a>
  ),
}));

const BASE: Omit<ClienteItem, 'id' | 'clave' | 'razonSocial'> = {
  referenciaExterna: null,
  rfc: null,
  regimenFiscal: null,
  codigoPostalFiscal: null,
  monedaDefault: 'MXN',
  esGenerico: false,
  origen: 1,
  datosFiscalesCompletos: false,
  estatus: 0,
};

const SIN_CLASIFICAR: ClienteItem = {
  ...BASE,
  id: 'c-1',
  clave: 'CLI-001',
  razonSocial: 'Vidrios del Centro S.A. de C.V.',
  clasificacionId: null,
  clasificacionNombre: null,
};

const CLASIFICADO: ClienteItem = {
  ...BASE,
  id: 'c-2',
  clave: 'CLI-002',
  razonSocial: 'Edificaciones Norte S.A.',
  clasificacionId: '00000012-0001-0000-0000-000000000001',
  clasificacionNombre: 'Constructora',
};

describe('ListaClientesCompacta · clasificación', () => {
  it('muestra "Sin clasificar" en el cliente sin clasificación y el nombre en el clasificado', () => {
    render(
      <ListaClientesCompacta items={[SIN_CLASIFICAR, CLASIFICADO]} idActivo={null} />,
    );

    const filaSin = screen.getByRole('link', { name: /CLI-001/ });
    const filaCon = screen.getByRole('link', { name: /CLI-002/ });

    expect(filaSin).toHaveTextContent('Sin clasificar');
    expect(filaCon).toHaveTextContent('Constructora');
    expect(filaCon).not.toHaveTextContent('Sin clasificar');
  });
});
