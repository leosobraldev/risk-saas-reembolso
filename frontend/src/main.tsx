import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router'
import { AppRoutes } from './App'
import { AuthProviders } from './auth/AuthProviders'
import { loadConfig } from './config'
import './index.css'

const root = createRoot(document.getElementById('root')!)

async function start() {
  // `import.meta.env.DEV` é substituído por `false` no build: o modo demonstração não vai para produção.
  if (import.meta.env.DEV && import.meta.env.VITE_DEMO === '1') {
    const { DemoApp } = await import('./dev/demo')
    root.render(
      <StrictMode>
        <DemoApp />
      </StrictMode>,
    )
    return
  }

  try {
    const config = loadConfig()
    root.render(
      <StrictMode>
        <BrowserRouter>
          <AuthProviders config={config}>
            <AppRoutes />
          </AuthProviders>
        </BrowserRouter>
      </StrictMode>,
    )
  } catch (error) {
    // configuração ausente: mostra o motivo em vez de uma tela em branco
    root.render(
      <main role="alert" style={{ padding: '2rem', fontFamily: 'sans-serif' }}>
        <h1>RiskGuard não está configurado</h1>
        <p>{error instanceof Error ? error.message : 'Configuração inválida.'}</p>
      </main>,
    )
  }
}

void start()
