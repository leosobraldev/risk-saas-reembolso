import type { RiskLevel } from '@/api/types'

const FILL: Record<RiskLevel, string> = { Low: '#007e5e', Medium: '#ffd100', High: '#d71920' }

/**
 * O score como estação num trajeto de 0 a 100. A estação de conexão (tique) marca o limite de risco alto do lojista, que o
 * cabeçalho da coluna nomeia em texto (a API só informa esse limite; não inventamos outras divisões). À direita do
 * limite, a linha fica vermelha. Sem o limite (ainda carregando), só a régua e o pedido.
 */
export function RiskTrack({ score, level, threshold }: { score: number; level: RiskLevel; threshold?: number }) {
  const clamp = (value: number) => Math.min(100, Math.max(0, value))
  const x = (value: number) => 6 + (clamp(value) / 100) * 108 // 6..114 dentro de viewBox 120
  const label =
    threshold === undefined ? `Score ${score} de 100.` : `Score ${score} de 100. Limite de risco alto: ${threshold}.`

  return (
    <svg viewBox="0 0 120 24" role="img" aria-label={label} className="h-auto w-16 shrink-0 sm:w-[120px]">
      <title>{label}</title>
      <line x1={x(0)} x2={x(threshold ?? 100)} y1="12" y2="12" stroke="#b9b3a4" strokeWidth="3" strokeLinecap="round" />
      {threshold !== undefined && (
        <>
          <line x1={x(threshold)} x2={x(100)} y1="12" y2="12" stroke="#d71920" strokeWidth="3" strokeLinecap="round" />
          {/* estação de conexão: tique vertical no limite do lojista */}
          <line x1={x(threshold)} x2={x(threshold)} y1="2" y2="22" stroke="#14202b" strokeWidth="2" strokeLinecap="round" />
        </>
      )}
      {/* estação do pedido */}
      <circle cx={x(score)} cy="12" r="5.5" fill={FILL[level]} stroke="#14202b" strokeWidth="1.75" />
    </svg>
  )
}
