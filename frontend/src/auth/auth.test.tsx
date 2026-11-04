import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { LoginPage } from '@/pages/LoginPage'
import { ProtectedRoute } from './ProtectedRoute'

const auth0 = vi.hoisted(() => ({ state: {} as Record<string, unknown> }))
vi.mock('@auth0/auth0-react', () => ({ useAuth0: () => auth0.state }))

const setAuth = (state: Record<string, unknown>) => {
  auth0.state = { isLoading: false, isAuthenticated: false, loginWithRedirect: vi.fn(), logout: vi.fn(), error: undefined, ...state }
}

function renderRoutes(route: string, state?: unknown) {
  return render(
    <MemoryRouter initialEntries={[{ pathname: route, state }]}>
      <Routes>
        <Route path="/entrar" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route path="/" element={<p>Área protegida</p>} />
          <Route path="/auditoria" element={<p>Auditoria protegida</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('ProtectedRoute', () => {
  it('sem sessão manda para a tela de entrada e não renderiza a área protegida', () => {
    setAuth({ isAuthenticated: false })

    renderRoutes('/')

    expect(screen.getByRole('heading', { name: 'Entre para ver seus pedidos' })).toBeInTheDocument()
    expect(screen.queryByText('Área protegida')).not.toBeInTheDocument()
  })

  it('enquanto verifica a sessão, não mostra a área protegida nem a tela de entrada', () => {
    setAuth({ isLoading: true })

    renderRoutes('/')

    expect(screen.getByRole('status')).toHaveTextContent('Verificando sua sessão')
    expect(screen.queryByText('Área protegida')).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Entre para ver seus pedidos' })).not.toBeInTheDocument()
  })

  it('com sessão renderiza a área protegida', () => {
    setAuth({ isAuthenticated: true })

    renderRoutes('/auditoria')

    expect(screen.getByText('Auditoria protegida')).toBeInTheDocument()
  })
})

describe('LoginPage', () => {
  it('Entrar inicia o login no provedor e lembra para onde voltar', async () => {
    const loginWithRedirect = vi.fn()
    setAuth({ loginWithRedirect })

    renderRoutes('/entrar', { returnTo: '/auditoria' })
    await userEvent.click(screen.getByRole('button', { name: 'Entrar' }))

    expect(loginWithRedirect).toHaveBeenCalledWith({ appState: { returnTo: '/auditoria' } })
  })

  it('o login que falhou mostra aviso sem detalhes técnicos', () => {
    setAuth({ error: new Error('access_denied: detalhes internos do provedor') })

    renderRoutes('/entrar')

    expect(screen.getByRole('alert')).toHaveTextContent('Não foi possível entrar.')
    expect(screen.queryByText(/access_denied/)).not.toBeInTheDocument()
  })

  it('quem já tem sessão e abre /entrar é levado de volta ao painel', () => {
    setAuth({ isAuthenticated: true })

    renderRoutes('/entrar')

    expect(screen.getByText('Área protegida')).toBeInTheDocument()
  })

  it('o botão fica desabilitado enquanto o provedor carrega', () => {
    setAuth({ isLoading: true })

    renderRoutes('/entrar')

    expect(screen.getByRole('button', { name: 'Carregando…' })).toBeDisabled()
  })
})
