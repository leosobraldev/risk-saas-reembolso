import { formatBRL, formatRelative, platformLabel } from './format'

describe('format', () => {
  it('formata valores em reais', () => {
    expect(formatBRL(1234.5).replace(/\s/g, ' ')).toBe('R$ 1.234,50')
  })

  it('mostra o tempo relativo em português', () => {
    const now = new Date('2026-11-04T12:00:00Z')

    expect(formatRelative('2026-11-04T11:55:00Z', now)).toBe('há 5 minutos')
    expect(formatRelative('2026-11-04T09:00:00Z', now)).toBe('há 3 horas')
    expect(formatRelative('2026-11-03T12:00:00Z', now)).toBe('ontem')
    expect(formatRelative('2026-11-04T11:59:40Z', now)).toBe('há 20 segundos')
  })

  it('datas com mais de 30 dias aparecem como data', () => {
    expect(formatRelative('2026-08-01T12:00:00Z', new Date('2026-11-04T12:00:00Z'))).toMatch(/\d{2}\/\d{2}\/\d{4}/)
  })

  it('nomeia as plataformas conhecidas e preserva as desconhecidas', () => {
    expect(platformLabel('shopify')).toBe('Shopify')
    expect(platformLabel('NUVEMSHOP')).toBe('Nuvemshop')
    expect(platformLabel('wake')).toBe('Wake')
    expect(platformLabel('outra')).toBe('Outra') // plataforma nova sem entrada: id capitalizado
  })
})
