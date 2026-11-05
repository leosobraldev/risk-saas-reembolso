const brl = new Intl.NumberFormat('pt-BR', { style: 'currency', currency: 'BRL' })
const dateTime = new Intl.DateTimeFormat('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
const relative = new Intl.RelativeTimeFormat('pt-BR', { numeric: 'auto' })

export const formatBRL = (value: number) => brl.format(value)

export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))

/** "ha 5 minutos", "ontem": o instante exato fica no title/tooltip do elemento. */
export function formatRelative(iso: string, now: Date = new Date()): string {
  const seconds = Math.round((new Date(iso).getTime() - now.getTime()) / 1000)
  const abs = Math.abs(seconds)
  if (abs < 60) return relative.format(seconds, 'second')
  if (abs < 3600) return relative.format(Math.round(seconds / 60), 'minute')
  if (abs < 86_400) return relative.format(Math.round(seconds / 3600), 'hour')
  if (abs < 2_592_000) return relative.format(Math.round(seconds / 86_400), 'day')
  return dateTime.format(new Date(iso))
}

/** Nome para exibir. Plataforma nova sem entrada aqui aparece com o id capitalizado (o nome oficial vem de GET /platforms). */
export const platformLabel = (platform: string) =>
  ({ shopify: 'Shopify', nuvemshop: 'Nuvemshop', wake: 'Wake' })[platform.toLowerCase()] ??
  platform.charAt(0).toUpperCase() + platform.slice(1)
