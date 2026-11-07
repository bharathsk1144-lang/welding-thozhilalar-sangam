export default function DemoPassport({ className = '', alt = 'Synthetic sample portrait' }) {
  return <div className={`demo-passport ${className}`} role="img" aria-label={alt}>
    <svg viewBox="0 0 100 120" aria-hidden="true"><rect width="100" height="120" fill="#f8edcf" /><circle cx="50" cy="40" r="21" fill="#732438" /><path d="M12 116v-14a38 38 0 0 1 76 0v14" fill="#732438" /></svg>
    <small>SAMPLE</small>
  </div>;
}
