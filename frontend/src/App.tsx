import { Navigate, Route, Routes } from 'react-router'
import { ProtectedRoute } from '@/auth/ProtectedRoute'
import { AppShell } from '@/components/AppShell'
import { ActionsPage } from '@/pages/ActionsPage'
import { AuditPage } from '@/pages/AuditPage'
import { LoginPage } from '@/pages/LoginPage'
import { OrdersPage } from '@/pages/OrdersPage'
import { SettingsPage } from '@/pages/SettingsPage'

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/entrar" element={<LoginPage />} />
      <Route element={<ProtectedRoute />}>
        <Route element={<AppShell />}>
          <Route index element={<OrdersPage />} />
          <Route path="acoes" element={<ActionsPage />} />
          <Route path="configuracoes" element={<SettingsPage />} />
          <Route path="auditoria" element={<AuditPage />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}
