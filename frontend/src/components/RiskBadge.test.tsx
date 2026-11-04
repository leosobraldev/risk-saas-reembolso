import { render, screen } from '@testing-library/react'
import { RiskBadge, StatusBadge } from './RiskBadge'
import { RiskTrack } from './RiskTrack'

describe('RiskBadge', () => {
  it.each([
    ['Low', 'Risco Baixo', 'bg-line-green'],
    ['Medium', 'Risco Médio', 'bg-line-yellow'],
    ['High', 'Risco Alto', 'bg-line-red'],
  ] as const)('%s mostra o texto do nível, nunca só a cor', (level, text, color) => {
    render(<RiskBadge level={level} />)

    expect(screen.getByText(text)).toHaveClass(color)
  })

  it('identifica a linha do metrô no tooltip', () => {
    render(<RiskBadge level="High" />)

    expect(screen.getByTitle('Linha Vermelha')).toBeInTheDocument()
  })
})

describe('StatusBadge', () => {
  it.each([
    ['Pending', 'Na fila'],
    ['Succeeded', 'Aplicada'],
    ['Failed', 'Falhou'],
    ['Skipped', 'Não aplicada'],
    ['Releasing', 'Liberando'],
    ['Released', 'Liberada'],
    ['ReleaseFailed', 'Falha ao liberar'],
  ] as const)('%s => %s', (status, text) => {
    render(<StatusBadge status={status} />)

    expect(screen.getByText(text)).toBeInTheDocument()
  })
})

describe('RiskTrack', () => {
  it('descreve score e limite para leitores de tela', () => {
    render(<RiskTrack score={85} level="High" threshold={80} />)

    expect(screen.getByRole('img', { name: 'Score 85 de 100. Limite de risco alto: 80.' })).toBeInTheDocument()
  })

  it('sem o limite (ainda carregando) não inventa um', () => {
    render(<RiskTrack score={40} level="Low" />)

    expect(screen.getByRole('img', { name: 'Score 40 de 100.' })).toBeInTheDocument()
  })

  it('scores fora de 0 a 100 ficam dentro da régua', () => {
    const { container } = render(<RiskTrack score={250} level="High" threshold={80} />)

    const circle = container.querySelector('circle')!
    expect(Number(circle.getAttribute('cx'))).toBeLessThanOrEqual(114)
  })
})
