import { render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { DevelopmentQueryDevtools } from './DevelopmentQueryDevtools';

vi.mock('@tanstack/react-query-devtools', () => ({
  ReactQueryDevtools: () => <div data-testid="rq-devtools" />,
}));
vi.mock('@/lib/auth/auth-store', () => ({
  useAuthStore: (selector: (state: { status: string }) => unknown) =>
    selector({ status: 'authenticated' }),
}));

describe('DevelopmentQueryDevtools', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('no renderiza nada sin VITE_QUERY_DEVTOOLS', () => {
    vi.stubEnv('VITE_QUERY_DEVTOOLS', '');
    const { container } = render(<DevelopmentQueryDevtools />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renderiza el devtools con VITE_QUERY_DEVTOOLS=true', () => {
    vi.stubEnv('VITE_QUERY_DEVTOOLS', 'true');
    const { getByTestId } = render(<DevelopmentQueryDevtools />);
    expect(getByTestId('rq-devtools')).toBeInTheDocument();
  });
});
