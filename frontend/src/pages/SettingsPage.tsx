import { useState, type ReactNode } from 'react'
import { errorMessage } from '@/api/errors'
import { useAnonymizeCustomer, useConnections, useRemoveConnection, useSaveConnection, useTenant, useUpdateSettings } from '@/api/queries'
import { PLATFORMS, type Platform, type PlatformConnection, type TenantSettings } from '@/api/types'
import { PageHeader } from '@/components/PageHeader'
import { ErrorState } from '@/components/states'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Button } from '@/components/ui/button'
import { Input, Label } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { formatRelative, platformLabel } from '@/lib/format'

function Section({ title, description, children }: { title: string; description: string; children: ReactNode }) {
  return (
    <section className="rounded-lg border border-rule bg-surface p-5">
      <h2 className="text-lg font-bold">{title}</h2>
      <p className="mt-1 mb-4 max-w-prose text-sm text-ink-muted">{description}</p>
      {children}
    </section>
  )
}

function Feedback({ ok, error }: { ok?: string; error?: unknown }) {
  if (error) {
    return (
      <p role="alert" className="text-sm font-semibold text-ink">
        {errorMessage(error)}
      </p>
    )
  }
  return ok ? (
    <p role="status" className="text-sm font-semibold text-ink">
      {ok}
    </p>
  ) : null
}

export function SettingsPage() {
  return (
    <>
      <PageHeader title="Configurações" description="Defina quando o RiskGuard age, conecte suas lojas e exerça os direitos de privacidade dos seus compradores." />
      <div className="flex flex-col gap-6">
        <RiskRules />
        <Connections />
        <Privacy />
      </div>
    </>
  )
}

function RiskRules() {
  const tenant = useTenant()
  if (tenant.isPending) return <Skeleton className="h-40 w-full" />
  if (tenant.isError) return <ErrorState error={tenant.error} onRetry={() => void tenant.refetch()} />
  return <RiskRulesForm saved={tenant.data.settings} />
}

function RiskRulesForm({ saved }: { saved: TenantSettings }) {
  const update = useUpdateSettings()
  const [threshold, setThreshold] = useState(String(saved.highRiskThreshold))
  const [autoBlock, setAutoBlock] = useState(saved.autoBlockEnabled)

  const parsed = Number(threshold)
  const valid = threshold.trim() !== '' && Number.isInteger(parsed) && parsed >= 0 && parsed <= 100
  const dirty = parsed !== saved.highRiskThreshold || autoBlock !== saved.autoBlockEnabled

  return (
    <Section
      title="Regras de risco"
      description="Pedidos com score igual ou acima do limite são tratados como risco alto. Com o bloqueio automático ligado, o RiskGuard retém esses pedidos na sua loja; desligado, só sinaliza."
    >
      <form
        className="flex flex-col gap-5"
        onSubmit={(event) => {
          event.preventDefault()
          if (valid) update.mutate({ highRiskThreshold: parsed, autoBlockEnabled: autoBlock })
        }}
      >
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="threshold">Limite de risco alto (0 a 100)</Label>
          <Input
            id="threshold"
            type="number"
            inputMode="numeric"
            min={0}
            max={100}
            className="w-32"
            value={threshold}
            aria-invalid={!valid}
            aria-describedby="threshold-help"
            onChange={(event) => setThreshold(event.target.value)}
          />
          <p id="threshold-help" className={valid ? 'text-xs text-ink-muted' : 'text-xs font-semibold text-ink'}>
            {valid ? 'Quanto menor o limite, mais pedidos são tratados como de risco alto.' : 'Informe um número inteiro de 0 a 100.'}
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Switch id="auto-block" checked={autoBlock} onCheckedChange={setAutoBlock} />
          <Label htmlFor="auto-block">Bloquear automaticamente pedidos de risco alto</Label>
        </div>

        <div className="flex flex-wrap items-center gap-4">
          <Button type="submit" disabled={!valid || !dirty || update.isPending}>
            {update.isPending ? 'Salvando…' : 'Salvar regras'}
          </Button>
          <Feedback ok={update.isSuccess && !dirty ? 'Regras salvas. Valem para os próximos pedidos.' : undefined} error={update.error} />
        </div>
      </form>
    </Section>
  )
}

function Connections() {
  const connections = useConnections()

  return (
    <Section
      title="Lojas conectadas"
      description="O RiskGuard recebe os pedidos por webhook e age na API da sua loja. Os segredos só entram: depois de salvos, não são mostrados de novo."
    >
      {connections.isPending ? (
        <Skeleton className="h-32 w-full" />
      ) : connections.isError ? (
        <ErrorState error={connections.error} onRetry={() => void connections.refetch()} />
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {PLATFORMS.map((platform) => (
            <ConnectionCard key={platform} platform={platform} current={connections.data.find((c) => c.platform.toLowerCase() === platform)} />
          ))}
        </div>
      )}
    </Section>
  )
}

function ConnectionCard({ platform, current }: { platform: Platform; current?: PlatformConnection }) {
  const save = useSaveConnection()
  const remove = useRemoveConnection()
  const [accountId, setAccountId] = useState(current?.accountId ?? '')
  const [webhookSecret, setWebhookSecret] = useState('')
  const [accessToken, setAccessToken] = useState('')
  const name = platformLabel(platform)
  const idLabel = platform === 'shopify' ? 'Domínio da loja' : 'Id da loja'
  const idHint = platform === 'shopify' ? 'minha-loja.myshopify.com' : 'Número da loja na Nuvemshop'

  return (
    <form
      className="flex flex-col gap-3 rounded-md border border-rule p-4"
      onSubmit={(event) => {
        event.preventDefault()
        save.mutate(
          { platform, accountId: accountId.trim(), webhookSecret, accessToken: accessToken || undefined },
          {
            onSuccess: () => {
              // o segredo não fica na tela depois de enviado
              setWebhookSecret('')
              setAccessToken('')
            },
          },
        )
      }}
    >
      <div className="flex items-baseline justify-between gap-2">
        <h3 className="font-bold">{name}</h3>
        <span className="text-xs text-ink-muted">
          {current ? `Conectada · atualizada ${formatRelative(current.updatedAt)}` : 'Não conectada'}
        </span>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor={`${platform}-account`}>{idLabel}</Label>
        <Input id={`${platform}-account`} value={accountId} placeholder={idHint} autoComplete="off" onChange={(e) => setAccountId(e.target.value)} />
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={`${platform}-secret`}>Segredo do webhook</Label>
        <Input
          id={`${platform}-secret`}
          type="password"
          value={webhookSecret}
          autoComplete="new-password"
          placeholder={current ? 'Digite para substituir' : ''}
          onChange={(e) => setWebhookSecret(e.target.value)}
        />
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor={`${platform}-token`}>Token de acesso {current?.hasAccessToken && <span className="font-normal text-ink-muted">(já informado)</span>}</Label>
        <Input id={`${platform}-token`} type="password" value={accessToken} autoComplete="new-password" placeholder="Necessário para bloquear e liberar pedidos" onChange={(e) => setAccessToken(e.target.value)} />
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" size="sm" disabled={save.isPending || accountId.trim() === '' || webhookSecret === ''}>
          {save.isPending ? 'Salvando…' : current ? 'Substituir conexão' : 'Conectar'}
        </Button>
        {current && (
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button type="button" variant="outline" size="sm" disabled={remove.isPending}>
                Desconectar
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogTitle>Desconectar a {name}?</AlertDialogTitle>
              <AlertDialogDescription>
                O RiskGuard deixa de aceitar webhooks e de agir nos pedidos desta loja. Você pode conectar de novo quando quiser.
              </AlertDialogDescription>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancelar</AlertDialogCancel>
                <AlertDialogAction onClick={() => remove.mutate(platform)}>Desconectar</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        )}
      </div>
      <Feedback ok={save.isSuccess ? 'Conexão salva.' : undefined} error={save.error ?? remove.error} />
    </form>
  )
}

function Privacy() {
  const anonymize = useAnonymizeCustomer()
  const [email, setEmail] = useState('')
  const valid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())

  return (
    <Section
      title="Privacidade dos compradores"
      description="Direito de eliminação (LGPD, art. 18): remove os dados pessoais de um comprador dos pedidos e do histórico de risco. A ação é irreversível."
    >
      <form className="flex flex-col gap-3 sm:max-w-md" onSubmit={(event) => event.preventDefault()}>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="anonymize-email">E-mail do comprador</Label>
          <Input id="anonymize-email" type="email" value={email} autoComplete="off" placeholder="comprador@exemplo.com" onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div className="flex flex-wrap items-center gap-3">
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button type="button" variant="danger" disabled={!valid || anonymize.isPending}>
                Eliminar dados do comprador
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogTitle>Eliminar os dados deste comprador?</AlertDialogTitle>
              <AlertDialogDescription>
                Os dados pessoais ligados a {email.trim()} serão apagados e não poderão ser recuperados. O pedido de eliminação fica registrado na auditoria, sem o e-mail.
              </AlertDialogDescription>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancelar</AlertDialogCancel>
                <AlertDialogAction
                  className="border-ink bg-ink hover:bg-black"
                  onClick={() => anonymize.mutate(email.trim(), { onSuccess: () => setEmail('') })}
                >
                  Eliminar dados
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
          <Feedback ok={anonymize.isSuccess ? 'Pedido de eliminação registrado.' : undefined} error={anonymize.error} />
        </div>
      </form>
    </Section>
  )
}
