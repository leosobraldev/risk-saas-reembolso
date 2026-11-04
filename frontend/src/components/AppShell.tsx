import { useAuth0 } from '@auth0/auth0-react'
import { LogOut } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { useTenant } from '@/api/queries'
import { BrandMark } from '@/components/Brand'
import { ErrorState } from '@/components/states'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'

const NAV = [
  { to: '/', label: 'Pedidos', end: true },
  { to: '/acoes', label: 'Ações', end: false },
  { to: '/configuracoes', label: 'Configurações', end: false },
  { to: '/auditoria', label: 'Auditoria', end: false },
]

export function AppShell() {
  const { logout } = useAuth0()
  const tenant = useTenant()

  const signOut = () => void logout({ logoutParams: { returnTo: window.location.origin } })

  return (
    <div className="min-h-screen md:grid md:grid-cols-[220px_1fr]">
      <aside className="flex flex-col bg-rail text-rail-ink md:sticky md:top-0 md:h-screen">
        <div className="flex items-center gap-2.5 px-4 py-4 md:py-5">
          <BrandMark />
          <span className="text-lg font-bold tracking-tight">RiskGuard</span>
        </div>

        <nav aria-label="Principal" className="relative flex gap-1 overflow-x-auto px-2 pb-2 md:flex-1 md:flex-col md:gap-0 md:overflow-visible md:px-4 md:pb-0">
          {/* a linha: um trilho vertical que liga as estações (só no desktop) */}
          <span aria-hidden="true" className="absolute bottom-4 left-[1.625rem] top-5 hidden w-0.5 bg-rail-line md:block" />
          {NAV.map(({ to, label, end }) => (
            <NavLink
              key={to}
              to={to}
              end={end}
              className={({ isActive }) =>
                cn(
                  'relative flex h-10 shrink-0 items-center gap-2.5 rounded-md px-2.5 text-sm font-semibold md:h-11 md:rounded-none md:pl-8 md:pr-2',
                  isActive ? 'bg-white text-rail md:bg-transparent md:text-white' : 'text-rail-ink hover:bg-rail-line md:hover:bg-transparent md:hover:text-white',
                )
              }
            >
              {({ isActive }) => (
                <>
                  {/* estação: anel vazio; a página atual é a estação preenchida (só no desktop) */}
                  <span
                    aria-hidden="true"
                    className={cn(
                      'absolute left-[0.45rem] top-1/2 hidden size-3.5 -translate-y-1/2 rounded-full border-2 border-white md:block',
                      isActive ? 'bg-white' : 'bg-rail',
                    )}
                  />
                  {label}
                </>
              )}
            </NavLink>
          ))}
        </nav>

        <div className="hidden border-t border-rail-line p-4 text-sm md:block">
          {tenant.data ? (
            <>
              <p className="truncate font-semibold" title={tenant.data.name}>
                {tenant.data.name}
              </p>
              <p className="truncate text-xs text-rail-muted" title={tenant.data.email}>
                {tenant.data.email}
              </p>
            </>
          ) : (
            <Skeleton className="h-9 bg-rail-line" />
          )}
          <button
            type="button"
            onClick={signOut}
            className="mt-3 inline-flex h-9 items-center gap-2 rounded-md px-2 text-sm font-semibold text-rail-ink hover:bg-rail-line"
          >
            <LogOut aria-hidden="true" className="size-4" />
            Sair
          </button>
        </div>
      </aside>

      <div className="min-w-0">
        <div className="flex items-center justify-between border-b border-rule bg-surface px-4 py-2 text-sm md:hidden">
          <span className="truncate font-semibold">{tenant.data?.name ?? 'RiskGuard'}</span>
          <button type="button" onClick={signOut} className="inline-flex h-8 items-center gap-1.5 rounded-md px-2 font-semibold hover:bg-sunken">
            <LogOut aria-hidden="true" className="size-4" />
            Sair
          </button>
        </div>

        <main className="mx-auto w-full max-w-6xl px-4 py-6 md:px-8 md:py-8">
          {tenant.isError ? (
            // sem loja ativa (403) ou sessão inválida: nada do painel faz sentido, então não mostramos as telas
            <ErrorState error={tenant.error} onRetry={() => void tenant.refetch()} />
          ) : (
            <Outlet />
          )}
        </main>
      </div>
    </div>
  )
}
