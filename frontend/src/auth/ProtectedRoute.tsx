import { useAuth0 } from '@auth0/auth0-react'
import { Navigate, Outlet, useLocation } from 'react-router'

/** Só renderiza as rotas filhas com sessão ativa; sem sessão, manda para a tela de entrada e volta depois. */
export function ProtectedRoute() {
  const { isLoading, isAuthenticated } = useAuth0()
  const location = useLocation()

  if (isLoading) {
    return (
      <div role="status" className="grid min-h-screen place-items-center text-sm text-ink-muted">
        Verificando sua sessão…
      </div>
    )
  }

  if (!isAuthenticated) {
    return <Navigate to="/entrar" replace state={{ returnTo: `${location.pathname}${location.search}` }} />
  }

  return <Outlet />
}
