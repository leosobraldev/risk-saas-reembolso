import { useAuth0 } from '@auth0/auth0-react'
import { Navigate, useLocation } from 'react-router'
import { BrandMark } from '@/components/Brand'
import { Button } from '@/components/ui/button'

export function LoginPage() {
  const { isLoading, isAuthenticated, loginWithRedirect, error } = useAuth0()
  const location = useLocation()
  const returnTo = (location.state as { returnTo?: string } | null)?.returnTo ?? '/'

  if (isAuthenticated) return <Navigate to={returnTo} replace />

  return (
    <main className="grid min-h-screen grid-rows-[1fr_auto] bg-rail text-rail-ink">
      <div className="mx-auto flex w-full max-w-sm flex-col justify-center gap-8 px-6">
        <div className="flex items-center gap-3">
          <BrandMark className="size-10" />
          <span className="text-2xl font-bold tracking-tight">RiskGuard</span>
        </div>

        <div className="flex flex-col gap-2">
          <h1 className="text-xl font-semibold">Entre para ver seus pedidos</h1>
          <p className="text-sm text-rail-muted">Acompanhe o risco de reembolso de cada pedido e libere os que foram retidos por engano.</p>
        </div>

        {error && (
          <p role="alert" className="rounded-md border-2 border-white bg-rail-line px-3 py-2 text-sm font-semibold text-white">
            Não foi possível entrar. Tente de novo; se continuar, fale com o suporte do RiskGuard.
          </p>
        )}

        <Button
          className="h-11 border-white bg-white text-rail hover:bg-rail-ink"
          disabled={isLoading}
          onClick={() => void loginWithRedirect({ appState: { returnTo } })}
        >
          {isLoading ? 'Carregando…' : 'Entrar'}
        </Button>
      </div>

      {/* a linha do Metrô como rodapé: Verde, Amarela, Vermelha */}
      <div aria-hidden="true" className="flex h-1.5">
        <span className="flex-1 bg-line-green" />
        <span className="flex-1 bg-line-yellow" />
        <span className="flex-1 bg-line-red" />
      </div>
    </main>
  )
}
