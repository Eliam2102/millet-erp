import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { OrigenBadge } from '@/features/compras/components/OrigenBadge';
import { OrigenRequisicion } from '@/features/compras/api/types';

describe('<OrigenBadge>', () => {
  it('muestra "Sistema" para origen Sistema', () => {
    render(<OrigenBadge origen={OrigenRequisicion.Sistema} />);
    expect(screen.getByText('Sistema')).toBeInTheDocument();
  });

  it('no renderiza nada para origen Manual (el caso normal)', () => {
    const { container } = render(
      <OrigenBadge origen={OrigenRequisicion.Manual} />,
    );
    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByText('Sistema')).not.toBeInTheDocument();
  });
});
