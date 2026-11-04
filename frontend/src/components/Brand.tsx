/** Marca RiskGuard: o símbolo é uma linha de três trechos (Verde, Amarela, Vermelha) com uma estação. */
export function BrandMark({ className = 'size-7' }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={className}>
      <rect width="32" height="32" rx="4" fill="#ffffff" />
      <path d="M6 23h7" stroke="#007e5e" strokeWidth="3" strokeLinecap="round" />
      <path d="M13 23h6" stroke="#ffd100" strokeWidth="3" />
      <path d="M19 23h7" stroke="#d71920" strokeWidth="3" strokeLinecap="round" />
      <circle cx="19" cy="12" r="4.5" fill="#ffffff" stroke="#0b2a4a" strokeWidth="2.5" />
      <path d="M19 16.5V23" stroke="#0b2a4a" strokeWidth="2" />
    </svg>
  )
}
